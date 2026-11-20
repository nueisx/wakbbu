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

    /// <summary>인자: 손가락, 세기(0~1).</summary>
    [Serializable]
    public class WaxBallCrunchEvent : UnityEvent<WaxFinger, float> { }

    /// <summary>
    /// 왁뿌볼 상태 판정의 유일한 장소. 상태는 공 전체 기준이고, 입력은 SetGrip(0~1)로 받는다.
    /// 손가락별 정보(SetFingerPressure)는 Crunched 이벤트를 만드는 데만 쓰인다.
    /// 다른 모듈은 StateChanged / Crunched 이벤트와 Grip / MixProgress 프로퍼티만 읽는다.
    /// </summary>
    public class WaxBallStateMachine : MonoBehaviour
    {
        public const int FingerCount = 3;

        [Header("임계값 (Interaction 담당이 튜닝)")]
        [SerializeField, Range(0f, 1f)] private float pressThreshold = 0.15f;
        [SerializeField, Range(0f, 1f)] private float crackThreshold = 0.55f;
        [SerializeField, Range(0f, 1f)] private float breakThreshold = 0.85f;
        [SerializeField, Min(0.01f)] private float kneadAmountToMix = 4.5f;
        [Tooltip("접촉점을 쓰는 씬에서, 공 표면 중 파손된 면적 비율이 이 값 이상이면 Mixed가 된다(0.8 = 80%). 접촉점이 없는 씬은 kneadAmountToMix를 쓴다.")]
        [SerializeField, Range(0.1f, 1f)] private float mixAreaThreshold = 0.8f;
        [Tooltip("접촉점을 쓰는 씬에서, 깨진 자리의 평균 섞임(깨진 면적 가중, 흔적마다 주무른 양 기준)이 이 값 이상이어야 Mixed가 된다(0.9 = 90%).")]
        [SerializeField, Range(0.1f, 1f)] private float mixAverageThreshold = 0.9f;

        [Header("Crunched (Broken 상태에서 손가락으로 주무를 때)")]
        [Tooltip("한 손가락의 세기 변화량이 이만큼 쌓일 때마다 그 손가락에 Crunched를 한 번 보낸다.")]
        [SerializeField, Min(0.01f)] private float crunchGripStep = 0.5f;
        [Tooltip("한 손가락의 Crunched 최소 간격(초). 이보다 빨리 쌓여도 간격이 지날 때까지 기다린다. 장갑 진동이 떨림이 되지 않게 한다.")]
        [SerializeField, Min(0f)] private float crunchMinInterval = 0.1f;
        [Tooltip("가장 천천히 주물렀을 때의 세기.")]
        [SerializeField, Range(0f, 1f)] private float crunchMinStrength = 0.2f;
        [Tooltip("이 속도(세기/초) 이상으로 주무르면 세기가 1이 된다.")]
        [SerializeField, Min(0.01f)] private float crunchFullSpeed = 2f;

        [Header("Inspector 이벤트")]
        [SerializeField] private WaxBallStateEvent onStateChanged = new WaxBallStateEvent();
        [SerializeField] private WaxBallCrunchEvent onCrunched = new WaxBallCrunchEvent();

        public event Action<WaxBallState, WaxBallState> StateChanged;

        /// <summary>
        /// Broken 상태에서 손가락으로 주무를 때마다 발생한다. 인자는 손가락(WaxFinger)과 세기(0~1).
        /// 해당 손가락에만 진동을 주고 바삭 소리를 내는 용도.
        /// </summary>
        public event Action<WaxFinger, float> Crunched;

        private readonly float[] fingerPressure = new float[FingerCount];
        private readonly float[] fingerTime = new float[FingerCount];
        private readonly float[] fingerSpeed = new float[FingerCount];
        private readonly float[] fingerCrunchAccumulated = new float[FingerCount];
        private readonly float[] fingerLastCrunchTime = new float[FingerCount];
        private readonly bool[] fingerSkipDelta = new bool[FingerCount];

        public WaxBallState State { get; private set; } = WaxBallState.Idle;
        public float Grip { get; private set; }
        public float KneadAccumulated { get; private set; }

        /// <summary>파손된 표면 비율(0~1). WaxBallDamage가 넘긴다. 한 번이라도 넘어오면(접촉점 씬) 섞임 판정이 면적 기준이 된다.</summary>
        public float DamagedRatio { get; private set; }

        /// <summary>깨진 자리의 평균 섞임(0~1, 깨진 면적 가중). 흔적마다 쌓은 주무른 양에서 WaxBallDamage가 계산한다.</summary>
        public float AverageMix { get; private set; }
        public bool AreaMixMode { get; private set; }
        public float MixAreaThreshold => mixAreaThreshold;
        public float MixAverageThreshold => mixAverageThreshold;
        public float KneadAmountToMix => kneadAmountToMix;

        /// <summary>
        /// 섞임 진행도(0~1). 접촉점 씬은 깨진 자리의 평균 섞임(Broken 전에는 0), 아니면 전체 주무른 양 기준이다.
        /// </summary>
        public float MixProgress
        {
            get
            {
                if (!AreaMixMode) return Mathf.Clamp01(KneadAccumulated / kneadAmountToMix);
                return State >= WaxBallState.Broken ? AverageMix : 0f;
            }
        }

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

        /// <summary>파손된 표면 비율과 깨진 자리의 평균 섞임(0~1)을 알린다. 이 값이 들어오면 Mixed 판정이 전체 주무른 양 대신 이 두 값 기준이 된다.</summary>
        public void SetDamagedRatio(float ratio, float averageMix)
        {
            AreaMixMode = true;
            DamagedRatio = Mathf.Clamp01(ratio);
            AverageMix = Mathf.Clamp01(averageMix);
            Evaluate();
        }

        /// <summary>
        /// 손가락 하나의 세기(0~1)를 알린다. Broken 상태에서 세기가 변하면 그 손가락에 Crunched가 발생한다.
        /// 상태 판정에는 쓰이지 않는다(상태는 SetGrip 기준).
        /// </summary>
        public void SetFingerPressure(WaxFinger finger, float pressure)
        {
            int i = (int)finger;
            if (i < 0 || i >= FingerCount) return;
            pressure = Mathf.Clamp01(pressure);

            float delta = Mathf.Abs(pressure - fingerPressure[i]);
            float elapsed = Mathf.Min(Time.time - fingerTime[i], 0.1f);
            if (elapsed > 1e-4f) fingerSpeed[i] = delta / elapsed;

            fingerPressure[i] = pressure;
            fingerTime[i] = Time.time;

            // Broken에 막 진입한 순간의 변화는 세지 않는다.
            if (fingerSkipDelta[i])
            {
                fingerSkipDelta[i] = false;
                return;
            }

            if (State != WaxBallState.Broken) return;

            fingerCrunchAccumulated[i] += delta;
            if (fingerCrunchAccumulated[i] >= crunchGripStep && Time.time - fingerLastCrunchTime[i] >= crunchMinInterval)
            {
                fingerLastCrunchTime[i] = Time.time;
                fingerCrunchAccumulated[i] %= crunchGripStep;
                RaiseCrunched(finger);
            }
        }

        /// <summary>손가락이 떨어졌을 때(추적을 잃었을 때 포함) 부른다. Crunched 없이 기준 세기만 0으로 돌린다.</summary>
        public void ReleaseFinger(WaxFinger finger)
        {
            int i = (int)finger;
            if (i < 0 || i >= FingerCount) return;
            fingerPressure[i] = 0f;
            fingerSpeed[i] = 0f;
        }

        /// <summary>어느 상태에서든 호출 가능, 횟수 제한 없음.</summary>
        public void ResetBall()
        {
            Grip = 0f;
            KneadAccumulated = 0f;
            DamagedRatio = 0f;
            AverageMix = 0f;
            for (int i = 0; i < FingerCount; i++)
            {
                fingerPressure[i] = 0f;
                fingerSpeed[i] = 0f;
                fingerCrunchAccumulated[i] = 0f;
                fingerLastCrunchTime[i] = float.NegativeInfinity;
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
                        if (ShouldMix()) { ChangeState(WaxBallState.Mixed); }
                        return;
                    default:
                        return;
                }
            }
        }

        private bool ShouldMix()
        {
            if (!AreaMixMode) return KneadAccumulated >= kneadAmountToMix;
            return DamagedRatio >= mixAreaThreshold && AverageMix >= mixAverageThreshold;
        }

        private void RaiseCrunched(WaxFinger finger)
        {
            float strength = Mathf.Lerp(crunchMinStrength, 1f, Mathf.Clamp01(fingerSpeed[(int)finger] / crunchFullSpeed));
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
