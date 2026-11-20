using System;
using UnityEngine;
using UnityEngine.Events;

namespace WaxBall
{
    public enum WaxBallState
    {
        Idle,
        Pressed,
        Cracked,
        Broken,
        Mixed
    }

    [Serializable]
    public class WaxBallStateEvent : UnityEvent<WaxBallState> { }

    /// <summary>인자: 손가락 번호(0~4), 세기(0~1).</summary>
    [Serializable]
    public class WaxBallCrunchEvent : UnityEvent<int, float> { }

    /// <summary>
    /// 왁뿌볼 상태 판정의 유일한 장소. 상태는 공 전체 기준이고, 입력은 SetGrip(0~1)로 받는다.
    /// 손가락별 정보(SetFingerPressure)는 Crunched 이벤트를 만드는 데만 쓰인다.
    /// 다른 모듈은 StateChanged / Crunched 이벤트와 Grip / MixProgress 프로퍼티만 읽는다.
    /// </summary>
    public class WaxBallStateMachine : MonoBehaviour
    {
        public const int FingerCount = 5;

        [Header("임계값 (Interaction 담당이 튜닝)")]
        [SerializeField, Range(0f, 1f)] private float pressThreshold = 0.15f;
        [SerializeField, Range(0f, 1f)] private float crackThreshold = 0.55f;
        [SerializeField, Range(0f, 1f)] private float breakThreshold = 0.85f;
        [SerializeField, Min(0.01f)] private float kneadAmountToMix = 3.0f;

        [Header("Crunched (Broken 상태에서 손가락으로 주무를 때)")]
        [Tooltip("한 손가락의 세기 변화량이 이만큼 쌓일 때마다 그 손가락에 Crunched를 한 번 보낸다.")]
        [SerializeField, Min(0.01f)] private float crunchGripStep = 0.2f;
        [Tooltip("가장 천천히 주물렀을 때의 세기.")]
        [SerializeField, Range(0f, 1f)] private float crunchMinStrength = 0.2f;
        [Tooltip("이 속도(세기/초) 이상으로 주무르면 세기가 1이 된다.")]
        [SerializeField, Min(0.01f)] private float crunchFullSpeed = 2f;

        [Header("Inspector 이벤트")]
        [SerializeField] private WaxBallStateEvent onStateChanged = new WaxBallStateEvent();
        [SerializeField] private WaxBallCrunchEvent onCrunched = new WaxBallCrunchEvent();

        public event Action<WaxBallState, WaxBallState> StateChanged;

        /// <summary>
        /// Broken 상태에서 손가락으로 주무를 때마다 발생한다. 인자는 손가락 번호(0~4)와 세기(0~1).
        /// 해당 손가락에만 진동을 주고 바삭 소리를 내는 용도.
        /// </summary>
        public event Action<int, float> Crunched;

        private readonly float[] fingerPressure = new float[FingerCount];
        private readonly float[] fingerTime = new float[FingerCount];
        private readonly float[] fingerSpeed = new float[FingerCount];
        private readonly float[] fingerCrunchAccumulated = new float[FingerCount];
        private readonly bool[] fingerSkipDelta = new bool[FingerCount];

        public WaxBallState State { get; private set; } = WaxBallState.Idle;
        public float Grip { get; private set; }
        public float KneadAccumulated { get; private set; }
        public float MixProgress => Mathf.Clamp01(KneadAccumulated / kneadAmountToMix);

        public float PressThreshold => pressThreshold;
        public float CrackThreshold => crackThreshold;
        public float BreakThreshold => breakThreshold;

        public WaxBallStateEvent OnStateChanged => onStateChanged;
        public WaxBallCrunchEvent OnCrunched => onCrunched;

        public void SetGrip(float grip)
        {
            grip = Mathf.Clamp01(grip);

            // Broken 이후에는 grip 변화량을 누적해 섞임 진행도를 만든다.
            if (State >= WaxBallState.Broken)
                KneadAccumulated += Mathf.Abs(grip - Grip);

            Grip = grip;
            Evaluate();
        }

        /// <summary>
        /// 손가락 하나의 세기(0~1)를 알린다. Broken 상태에서 세기가 변하면 그 손가락에 Crunched가 발생한다.
        /// 상태 판정에는 쓰이지 않는다(상태는 SetGrip 기준).
        /// </summary>
        public void SetFingerPressure(int finger, float pressure)
        {
            if (finger < 0 || finger >= FingerCount) return;
            pressure = Mathf.Clamp01(pressure);

            float delta = Mathf.Abs(pressure - fingerPressure[finger]);
            float elapsed = Mathf.Min(Time.time - fingerTime[finger], 0.1f);
            if (elapsed > 1e-4f) fingerSpeed[finger] = delta / elapsed;

            fingerPressure[finger] = pressure;
            fingerTime[finger] = Time.time;

            // Broken에 막 진입한 순간의 변화는 세지 않는다.
            if (fingerSkipDelta[finger])
            {
                fingerSkipDelta[finger] = false;
                return;
            }

            if (State != WaxBallState.Broken) return;

            fingerCrunchAccumulated[finger] += delta;
            if (fingerCrunchAccumulated[finger] >= crunchGripStep)
            {
                fingerCrunchAccumulated[finger] %= crunchGripStep;
                RaiseCrunched(finger);
            }
        }

        /// <summary>손가락이 떨어졌을 때(추적을 잃었을 때 포함) 부른다. Crunched 없이 기준 세기만 0으로 돌린다.</summary>
        public void ReleaseFinger(int finger)
        {
            if (finger < 0 || finger >= FingerCount) return;
            fingerPressure[finger] = 0f;
            fingerSpeed[finger] = 0f;
        }

        /// <summary>어느 상태에서든 호출 가능, 횟수 제한 없음.</summary>
        public void ResetBall()
        {
            Grip = 0f;
            KneadAccumulated = 0f;
            for (int i = 0; i < FingerCount; i++)
            {
                fingerPressure[i] = 0f;
                fingerSpeed[i] = 0f;
                fingerCrunchAccumulated[i] = 0f;
                fingerSkipDelta[i] = false;
            }
            ChangeState(WaxBallState.Idle);
        }

        private void Evaluate()
        {
            // 한 번에 큰 값이 들어와도 중간 상태 이벤트를 빠뜨리지 않도록 한 단계씩 진행한다.
            while (true)
            {
                switch (State)
                {
                    case WaxBallState.Idle:
                        if (Grip >= pressThreshold) { ChangeState(WaxBallState.Pressed); continue; }
                        return;
                    case WaxBallState.Pressed:
                        if (Grip >= crackThreshold) { ChangeState(WaxBallState.Cracked); continue; }
                        if (Grip < pressThreshold) { ChangeState(WaxBallState.Idle); }
                        return;
                    case WaxBallState.Cracked:
                        if (Grip >= breakThreshold) { ChangeState(WaxBallState.Broken); continue; }
                        return;
                    case WaxBallState.Broken:
                        if (KneadAccumulated >= kneadAmountToMix) { ChangeState(WaxBallState.Mixed); }
                        return;
                    default:
                        return;
                }
            }
        }

        private void RaiseCrunched(int finger)
        {
            float strength = Mathf.Lerp(crunchMinStrength, 1f, Mathf.Clamp01(fingerSpeed[finger] / crunchFullSpeed));
            Crunched?.Invoke(finger, strength);
            onCrunched.Invoke(finger, strength);
        }

        private void ChangeState(WaxBallState next)
        {
            if (State == next) return;
            if (next == WaxBallState.Broken)
            {
                for (int i = 0; i < FingerCount; i++)
                {
                    fingerCrunchAccumulated[i] = 0f;
                    fingerSkipDelta[i] = true;
                }
            }
            var prev = State;
            State = next;
            StateChanged?.Invoke(prev, next);
            onStateChanged.Invoke(next);
        }
    }
}
