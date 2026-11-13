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

    /// <summary>
    /// 왁뿌볼 상태 판정의 유일한 장소. 입력은 SetGrip(0~1) 하나로만 받는다.
    /// 다른 모듈은 StateChanged 이벤트와 Grip / MixProgress 프로퍼티만 읽는다.
    /// </summary>
    public class WaxBallStateMachine : MonoBehaviour
    {
        [Header("임계값 (Interaction 담당이 튜닝)")]
        [SerializeField, Range(0f, 1f)] private float pressThreshold = 0.15f;
        [SerializeField, Range(0f, 1f)] private float crackThreshold = 0.55f;
        [SerializeField, Range(0f, 1f)] private float breakThreshold = 0.85f;
        [SerializeField, Min(0.01f)] private float kneadAmountToMix = 3.0f;

        [Header("Inspector 이벤트")]
        [SerializeField] private WaxBallStateEvent onStateChanged = new WaxBallStateEvent();

        public event Action<WaxBallState, WaxBallState> StateChanged;

        public WaxBallState State { get; private set; } = WaxBallState.Idle;
        public float Grip { get; private set; }
        public float KneadAccumulated { get; private set; }
        public float MixProgress => Mathf.Clamp01(KneadAccumulated / kneadAmountToMix);

        public WaxBallStateEvent OnStateChanged => onStateChanged;

        public void SetGrip(float grip)
        {
            grip = Mathf.Clamp01(grip);

            // Broken 이후에는 grip 변화량을 누적해 섞임 진행도를 만든다.
            if (State >= WaxBallState.Broken)
                KneadAccumulated += Mathf.Abs(grip - Grip);

            Grip = grip;
            Evaluate();
        }

        /// <summary>어느 상태에서든 호출 가능, 횟수 제한 없음.</summary>
        public void ResetBall()
        {
            Grip = 0f;
            KneadAccumulated = 0f;
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

        private void ChangeState(WaxBallState next)
        {
            if (State == next) return;
            var prev = State;
            State = next;
            StateChanged?.Invoke(prev, next);
            onStateChanged.Invoke(next);
        }
    }
}
