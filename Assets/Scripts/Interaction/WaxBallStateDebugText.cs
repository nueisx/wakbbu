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
        [Tooltip("선택 사항. 비워 두면 같은 공에서 자동으로 찾는다. 연결하면 파손 흔적 수와 균열이 붙은 손가락을 표시한다.")]
        [SerializeField] private WaxBallDamage damage;
        [Tooltip("선택 사항. 비워 두면 씬에서 자동으로 찾는다. 연결하면 손 추적 상태와 손가락별 신뢰도, 손가락 끝 사이 거리를 표시한다.")]
        [SerializeField] private QuestHandFingertips fingertips;
        [Tooltip("선택 사항. 비워 두면 씬에서 자동으로 찾는다. 연결하면 필터 결과로 손가락 끝 거리를 보여 주고 거른 프레임 수를 표시한다.")]
        [SerializeField] private FilteredFingertips filtered;
        [Tooltip("선택 사항. 비워 두면 씬에서 자동으로 찾는다. 연결하면 CSV 기록 상태와 지금 해야 할 동작을 표시한다.")]
        [SerializeField] private FingertipRecorder recorder;
        [Tooltip("선택 사항. 비워 두면 씬에서 자동으로 찾는다. 연결하면 공 중심 모드, 쥔 손가락 수, 추정 오차를 표시한다.")]
        [SerializeField] private BallCenterEstimator estimator;
        [Tooltip("켜면 FPS와 프레임 간격(ms)을 표시한다. 화면 주사율 기준이라 GPU 여유는 OVR Metrics Tool로 따로 본다.")]
        [SerializeField] private bool showFps;

        private int crunchCount;
        private readonly System.Collections.Generic.Queue<float> crunchTimes = new System.Collections.Generic.Queue<float>();
        private string lastCrunchFinger = "-";
        private float lastCrunchStrength;
        private float smoothedFrameTime = 1f / 72f;

        private void Awake()
        {
            // 연결이 빠져 있어도 같은 공의 WaxBallDamage를 찾아 파손 줄을 표시한다.
            if (damage == null && stateMachine != null) damage = stateMachine.GetComponent<WaxBallDamage>();
            if (fingertips == null) fingertips = FindObjectOfType<QuestHandFingertips>();
            if (filtered == null) filtered = FindObjectOfType<FilteredFingertips>();
            if (recorder == null) recorder = FindObjectOfType<FingertipRecorder>();
            if (estimator == null) estimator = FindObjectOfType<BallCenterEstimator>();
        }

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

        // 손 추적 줄. 손가락별 신뢰도는 H(높음) / L(낮음) / -(추적 안 됨).
        // Gap은 엄지 끝에서 검지·중지 끝까지의 거리(cm)이고, 필터가 있으면 원본(r)과 필터 결과(f)를 같이 보여 준다.
        // Rej는 필터가 튄 값으로 보고 거른 프레임 수(엄지, 검지, 중지).
        private string HandText()
        {
            fingertips.TryGetFingertip(WaxFinger.Thumb, out FingertipSample thumb);
            fingertips.TryGetFingertip(WaxFinger.Index, out FingertipSample index);
            fingertips.TryGetFingertip(WaxFinger.Middle, out FingertipSample middle);

            string hand = fingertips.HandTracked ? $"tracked, {(fingertips.HandHighConfidence ? "High" : "Low")}" : "lost";
            string text = $"\nHand: {hand}\nTips: T {Confidence(thumb)}  I {Confidence(index)}  M {Confidence(middle)}";

            if (filtered == null)
                return text + $"\nGap: T-I {Gap(thumb, index)} cm, T-M {Gap(thumb, middle)} cm";

            filtered.TryGetFingertip(WaxFinger.Thumb, out FingertipSample fThumb);
            filtered.TryGetFingertip(WaxFinger.Index, out FingertipSample fIndex);
            filtered.TryGetFingertip(WaxFinger.Middle, out FingertipSample fMiddle);
            return text + $"\nGap T-I: r {Gap(thumb, index)} f {Gap(fThumb, fIndex)} cm\nGap T-M: r {Gap(thumb, middle)} f {Gap(fThumb, fMiddle)} cm";
        }

        // 필터가 튄 값으로 보고 거른 프레임 수(엄지, 검지, 중지).
        private string FilterText()
        {
            return $"\nRej: T {filtered.RejectedCount(WaxFinger.Thumb)} I {filtered.RejectedCount(WaxFinger.Index)} M {filtered.RejectedCount(WaxFinger.Middle)}";
        }

        // 공 중심: 모드, 쥐고 있는 손가락 수(0이면 안 쥔 것), 진짜 중심을 아는 시험 씬에서는 추정 오차(mm)와 중심을 거른 프레임 수.
        private string BallText()
        {
            string hold = estimator.Holding ? $"hold {estimator.HoldingTipCount}{(estimator.Frozen ? " frozen" : "")}" : "no hold";
            string error = estimator.ErrorMeters >= 0f ? $", err {estimator.ErrorMeters * 1000f:0.0} mm" : "";
            return $"\nBall: {estimator.Mode}, {hold}{error}, rej {estimator.RejectedCount}";
        }

        private static string Confidence(FingertipSample sample)
        {
            if (!sample.tracked) return "-";
            return sample.highConfidence ? "H" : "L";
        }

        private static string Gap(FingertipSample a, FingertipSample b)
        {
            if (!a.tracked || !b.tracked) return "-";
            return $"{Vector3.Distance(a.worldPosition, b.worldPosition) * 100f:0.0}";
        }

        private void Update()
        {
            while (crunchTimes.Count > 0 && Time.unscaledTime - crunchTimes.Peek() > 1f) crunchTimes.Dequeue();
            string text = $"State: {stateMachine.State}\nGrip: {stateMachine.Grip:0.00}\nMix: {stateMachine.MixProgress:P0}\nCrunch: {crunchCount} ({lastCrunchFinger}, {lastCrunchStrength:0.00}), {crunchTimes.Count}/s";

            if (damage != null)
            {
                string crack1 = damage.Crack1Finger.HasValue ? damage.Crack1Finger.Value.ToString() : "-";
                string crack2 = damage.Crack2Finger.HasValue ? damage.Crack2Finger.Value.ToString() : "-";
                text += $"\nDamage: {(damage.LocalMode ? "Local" : "Global")}, {damage.BrokenCellCount}/{damage.CellCount} plates\nDamaged: {(damage.LocalMode ? $"{damage.DamagedRatio:P0}" : "Global")}\nCracks: {crack1} / {crack2}";
            }

            if (fingertips != null) text += HandText();
            if (filtered != null) text += FilterText();
            if (estimator != null) text += BallText();
            if (recorder != null) text += "\n" + recorder.StatusText;

            if (showFps)
            {
                smoothedFrameTime = Mathf.Lerp(smoothedFrameTime, Time.unscaledDeltaTime, 0.05f);
                text += $"\nFPS: {1f / smoothedFrameTime:0} ({smoothedFrameTime * 1000f:0.0} ms)";
            }

            label.text = text;
        }
    }
}
