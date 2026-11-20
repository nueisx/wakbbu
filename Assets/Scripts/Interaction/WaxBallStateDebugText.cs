using TMPro;
using UnityEngine;
using WaxBall;

namespace Interaction
{
    /// <summary>현재 상태, grip, 섞임 진행도 등을 표시하는 디버그용 텍스트. 화면 UI 문자열은 영어로 쓴다.</summary>
    public class WaxBallStateDebugText : MonoBehaviour
    {
        [SerializeField] private WaxBallStateMachine stateMachine;
        [SerializeField] private TMP_Text label;
        [Tooltip("선택 사항. 연결하면 파손 흔적 수와 균열이 붙은 손가락을 표시한다.")]
        [SerializeField] private WaxBallDamage damage;
        [Tooltip("켜면 FPS와 프레임 간격(ms)을 표시한다. 화면 주사율 기준이라 GPU 여유는 OVR Metrics Tool로 따로 본다.")]
        [SerializeField] private bool showFps;

        private int crunchCount;
        private readonly System.Collections.Generic.Queue<float> crunchTimes = new System.Collections.Generic.Queue<float>();
        private string lastCrunchFinger = "-";
        private float lastCrunchStrength;
        private float smoothedFrameTime = 1f / 72f;

        private void OnEnable()
        {
            stateMachine.Crunched += HandleCrunched;
            stateMachine.StateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            stateMachine.Crunched -= HandleCrunched;
            stateMachine.StateChanged -= HandleStateChanged;
        }

        // 리셋(Idle로 돌아감)하면 Crunch 표시도 처음 상태로 돌린다. 손을 살짝 푼 Pressed → Idle은 리셋이 아니다.
        private void HandleStateChanged(WaxBallState prev, WaxBallState next)
        {
            if (next != WaxBallState.Idle || prev < WaxBallState.Cracked) return;
            crunchCount = 0;
            crunchTimes.Clear();
            lastCrunchFinger = "-";
            lastCrunchStrength = 0f;
        }

        private void HandleCrunched(WaxFinger finger, float strength)
        {
            crunchCount++;
            crunchTimes.Enqueue(Time.unscaledTime);
            lastCrunchFinger = finger.ToString();
            lastCrunchStrength = strength;
        }

        private void Update()
        {
            while (crunchTimes.Count > 0 && Time.unscaledTime - crunchTimes.Peek() > 1f) crunchTimes.Dequeue();
            string text = $"State: {stateMachine.State}\nGrip: {stateMachine.Grip:0.00}\nMix: {stateMachine.MixProgress:P0}\nCrunch: {crunchCount} ({lastCrunchFinger}, {lastCrunchStrength:0.00}), {crunchTimes.Count}/s";

            if (damage != null)
            {
                string crack1 = damage.Crack1Finger.HasValue ? damage.Crack1Finger.Value.ToString() : "-";
                string crack2 = damage.Crack2Finger.HasValue ? damage.Crack2Finger.Value.ToString() : "-";
                text += $"\nDamage: {(damage.LocalMode ? "Local" : "Global")}, {damage.StampCount} stamps\nDamaged: {(damage.LocalMode ? $"{damage.DamagedRatio:P0}" : "Global")}\nCracks: {crack1} / {crack2}";
            }

            if (showFps)
            {
                smoothedFrameTime = Mathf.Lerp(smoothedFrameTime, Time.unscaledDeltaTime, 0.05f);
                text += $"\nFPS: {1f / smoothedFrameTime:0} ({smoothedFrameTime * 1000f:0.0} ms)";
            }

            label.text = text;
        }
    }
}
