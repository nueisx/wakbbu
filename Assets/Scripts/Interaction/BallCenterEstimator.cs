using UnityEngine;
using WaxBall;

namespace Interaction
{
    /// <summary>
    /// 손가락 끝 세 점(또는 두 점)으로 공 중심을 구하고, Follow Hand 모드에서는 가상 공을 그 위치로 옮긴다.
    /// 공 중심은 손목 좌표계로 바꿔서 여러 프레임에 걸쳐 평균(One Euro 필터)한다. 손 전체가 흔들리는 오차는 손가락과 중심에 같이 실려서 상쇄되고,
    /// 튀는 값은 속도 제한으로 거른다. 손가락 끝 사이가 너무 벌어져 있으면(펴진 손) 공을 쥔 게 아니라고 보고 마지막 중심을 손에 붙은 채로 유지한다.
    /// 입력은 IFingertipSource(보통 FilteredFingertips)이고 장비와 무관하다.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class BallCenterEstimator : MonoBehaviour
    {
        public enum PlacementMode
        {
            /// <summary>가상 공은 제자리에 둔다. 접촉점은 추정한 중심 기준으로 공 주변에 옮겨서 넣는다.</summary>
            Fixed,
            /// <summary>쥐고 있는 동안 가상 공을 추정한 중심으로 옮기고, 놓아도 손에 붙은 채로 따라간다.</summary>
            FollowHand
        }

        [SerializeField] private WaxBallTuning tuning;
        [Tooltip("손가락 끝 입력. IFingertipSource를 구현한 컴포넌트(FilteredFingertips 등).")]
        [SerializeField] private MonoBehaviour source;
        [Tooltip("가상 공(WaxBall 오브젝트).")]
        [SerializeField] private Transform ball;
        [SerializeField] private PlacementMode mode = PlacementMode.FollowHand;
        [Tooltip("에디터 시험용. 진짜 공 중심을 아는 오브젝트(가짜 손의 중심)를 넣으면 추정 오차를 디버그 텍스트에 보여 준다.")]
        [SerializeField] private Transform truthCenter;

        private WaxBallTuning Tuning => tuning != null ? tuning : WaxBallTuning.Default;

        private IFingertipSource sourceInterface;
        private readonly FingertipFilter centerFilter = new FingertipFilter();
        private readonly Vector3[] points = new Vector3[WaxBallStateMachine.FingerCount];
        private bool haveCenter;
        private bool wasHolding;
        private int lastTipCount;
        private float spreadReference;
        private int frozenShiftRun;

        /// <summary>추정한(평균한) 공 중심(월드). HasCenter일 때만 의미가 있다.</summary>
        public Vector3 Center { get; private set; }

        /// <summary>한 번이라도 공을 쥔 적이 있어서 중심이 있다.</summary>
        public bool HasCenter => haveCenter;

        /// <summary>지금 공을 쥐고 있다고 보는가(손가락 끝 2개 이상이 공 크기 안에 모여 있음).</summary>
        public bool Holding { get; private set; }

        /// <summary>중심 계산에 쓴 손가락 끝 개수(공을 쥐고 있지 않으면 0).</summary>
        public int HoldingTipCount { get; private set; }

        /// <summary>쥐고 있지만 공이 눌려 중심 갱신을 멈춘 상태(마지막 중심을 유지 중).</summary>
        public bool Frozen { get; private set; }

        /// <summary>진짜 중심(truthCenter)과의 거리(m). 지정하지 않았으면 음수.</summary>
        public float ErrorMeters { get; private set; } = -1f;

        /// <summary>이번 프레임 중심 값을 거른 프레임 수(누적).</summary>
        public int RejectedCount => centerFilter.RejectedTotal;

        public PlacementMode Mode { get => mode; set => mode = value; }

        /// <summary>
        /// 손가락 끝 월드 위치를 "가상 공 기준 위치"로 옮긴다. FollowHand에서는 공이 추정 중심에 있으므로 그대로이고,
        /// Fixed에서는 추정 중심에서의 상대 위치를 가상 공 주변으로 옮겨서 접촉 방향이 맞게 한다. 중심이 아직 없으면 그대로 돌려준다.
        /// </summary>
        public Vector3 ToBallSpace(Vector3 worldPosition)
        {
            if (!haveCenter || ball == null) return worldPosition;
            return ball.position + (worldPosition - Center);
        }

        private void Awake()
        {
            sourceInterface = source as IFingertipSource;
            if (sourceInterface == null) Debug.LogError("BallCenterEstimator: source가 IFingertipSource가 아니다.", this);
        }

        // FilteredFingertips가 이번 프레임 값을 갱신한 뒤에 돌도록 실행 순서를 뒤로 했다.
        private void LateUpdate()
        {
            if (sourceInterface == null) return;

            Holding = false;
            Frozen = false;
            HoldingTipCount = 0;

            if (!sourceInterface.TryGetHandPose(out Vector3 wristPosition, out Quaternion wristRotation))
            {
                UpdateError();
                return;
            }

            var t = Tuning;
            float radius = t.ballDiameter * 0.5f + t.ballFingertipPadOffset;

            int count = 0;
            for (int i = 0; i < WaxBallStateMachine.FingerCount; i++)
            {
                if (sourceInterface.TryGetFingertip((WaxFinger)i, out FingertipSample sample) && sample.tracked)
                    points[count++] = sample.worldPosition;
            }

            if (count >= 2 && WithinHoldSpan(points, count, 2f * radius * t.ballHoldSpanScale)
                && BallCenterMath.TryEstimate(points[0], points[1], points[2], count, radius, wristPosition, out Vector3 raw, out float spread))
            {
                Holding = true;
                HoldingTipCount = count;

                // 쥐는 동안 가장 컸던 퍼짐을 기준으로 삼는다. 쥐기 시작할 때와 손가락 수가 바뀔 때 다시 잡고,
                // 커지는 쪽은 초당 100%까지만(튀는 값 하나가 기준을 부풀리지 못하게), 줄어드는 쪽은 천천히 따라간다.
                if (!wasHolding || count != lastTipCount) spreadReference = spread;
                else if (spread > spreadReference) spreadReference = Mathf.MoveTowards(spreadReference, spread, spreadReference * Time.deltaTime);
                else spreadReference = Mathf.Lerp(spreadReference, spread, Time.deltaTime * 0.2f);

                Vector3 local = Quaternion.Inverse(wristRotation) * (raw - wristPosition);

                // 공이 눌려 퍼짐이 기준보다 작아지면 중심 추정이 밀리므로 갱신하지 않고 마지막 중심을 유지한다.
                bool reliable = spread >= spreadReference * t.ballCenterSqueezeTolerance || !haveCenter;

                // 멈춘 중심과 새로 계산한 중심이 눌림으로는 설명이 안 될 만큼 계속 벌어져 있으면 잘못된 중심에 멈춘 것이니 풀고 다시 시작한다.
                if (!reliable)
                {
                    frozenShiftRun = (local - centerFilter.Position).magnitude > t.ballCenterFrozenMaxShift ? frozenShiftRun + 1 : 0;
                    if (frozenShiftRun >= t.fingertipRejectFrames)
                    {
                        reliable = true;
                        spreadReference = spread;
                        centerFilter.Reset();
                    }
                }
                else frozenShiftRun = 0;

                if (reliable)
                {
                    centerFilter.Begin(local, Time.deltaTime, t.ballCenterMinCutoff, t.ballCenterBeta, t.ballCenterMaxSpeed);
                    centerFilter.Commit(centerFilter.SpeedSuspect, t.fingertipRejectFrames);
                    haveCenter = true;
                }
                else Frozen = true;
            }
            wasHolding = Holding;
            lastTipCount = HoldingTipCount;

            // 쥐고 있지 않아도 마지막 중심은 손목에 붙어서 손과 함께 움직인다.
            if (haveCenter)
            {
                Center = wristPosition + wristRotation * centerFilter.Position;
                if (mode == PlacementMode.FollowHand && ball != null) ball.position = Center;
            }

            UpdateError();
        }

        private static bool WithinHoldSpan(Vector3[] points, int count, float maxSpan)
        {
            for (int a = 0; a < count; a++)
                for (int b = a + 1; b < count; b++)
                    if (Vector3.Distance(points[a], points[b]) > maxSpan) return false;
            return true;
        }

        private void UpdateError()
        {
            ErrorMeters = truthCenter != null && haveCenter ? Vector3.Distance(Center, truthCenter.position) : -1f;
        }
    }
}
