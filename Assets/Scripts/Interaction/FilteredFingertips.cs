using UnityEngine;
using WaxBall;

namespace Interaction
{
    /// <summary>
    /// 손가락 끝 입력(IFingertipSource) 위에 필터를 얹은 입력. 손가락마다 FingertipFilter로 손목 좌표계에서 위치를 부드럽게 하고,
    /// 한 프레임짜리 튐을 없애고, 속도가 비정상이거나 다른 손가락과 겹친 값(가려진 손가락을 추정으로 잘못 찍은 경우)을 거른다.
    /// 자기도 IFingertipSource라서 뒤쪽 어댑터는 원본이든 필터 결과든 같은 방식으로 읽는다.
    /// 손가락 끝에 작은 흰 구(필터 결과)를 띄운다. 이번 프레임에 값을 거르면 주황색이 된다.
    /// </summary>
    public class FilteredFingertips : MonoBehaviour, IFingertipSource
    {
        [Tooltip("튜닝 값을 모아 둔 설정 파일. 비워 두면 코드 기본값을 쓴다.")]
        [SerializeField] private WaxBallTuning tuning;

        [Tooltip("원본 손가락 끝 입력. IFingertipSource를 구현한 컴포넌트(QuestHandFingertips 등).")]
        [SerializeField] private MonoBehaviour source;

        [Tooltip("엄지, 검지, 중지 순서의 표시용 구(필터 결과). 비워 두면 표시하지 않는다.")]
        [SerializeField] private Transform[] markers = new Transform[WaxBallStateMachine.FingerCount];
        [SerializeField] private bool showMarkers = true;

        private WaxBallTuning Tuning => tuning != null ? tuning : WaxBallTuning.Default;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color FilteredColor = Color.white;
        private static readonly Color RejectedColor = new Color(1f, 0.55f, 0f);

        private IFingertipSource sourceInterface;
        private readonly FingertipFilter[] filters = new FingertipFilter[WaxBallStateMachine.FingerCount];
        private readonly FingertipSample[] rawSamples = new FingertipSample[WaxBallStateMachine.FingerCount];
        private readonly FingertipSample[] filteredSamples = new FingertipSample[WaxBallStateMachine.FingerCount];
        private readonly bool[] active = new bool[WaxBallStateMachine.FingerCount];
        private readonly bool[] suspect = new bool[WaxBallStateMachine.FingerCount];
        private Renderer[] markerRenderers;
        private MaterialPropertyBlock block;
        private bool lastHadPose;
        private int speedRejectTotal;
        private int overlapRejectTotal;

        /// <summary>속도 제한 때문에 거른 프레임 수와 손가락 겹침 때문에 거른 프레임 수(누적). 어느 쪽이 거르는지 보려는 값이다.</summary>
        public int SpeedRejectTotal => speedRejectTotal;
        public int OverlapRejectTotal => overlapRejectTotal;

        private void Awake()
        {
            sourceInterface = source as IFingertipSource;
            if (sourceInterface == null) Debug.LogError("FilteredFingertips: source가 IFingertipSource가 아니다.", this);

            for (int i = 0; i < filters.Length; i++) filters[i] = new FingertipFilter();

            block = new MaterialPropertyBlock();
            markerRenderers = new Renderer[markers.Length];
            for (int i = 0; i < markers.Length; i++)
                if (markers[i] != null) markerRenderers[i] = markers[i].GetComponent<Renderer>();
        }

        // 손 뼈대(OVRSkeleton)가 갱신된 뒤의 값을 읽도록 LateUpdate에서 처리한다.
        private void LateUpdate()
        {
            if (sourceInterface == null) return;

            var t = Tuning;
            float dt = Time.deltaTime;

            // 손목 좌표계에서 필터링한다. 손 전체가 같이 움직이는 것은 손가락 끝의 상대 위치를 바꾸지 않으므로 튄 값으로 거르지 않고,
            // 손가락끼리의 움직임(진짜 튐이나 가려짐 오류)만 검사한다. 손 자세를 못 주는 입력은 월드 좌표로 필터링한다.
            bool hasPose = sourceInterface.TryGetHandPose(out Vector3 wristPosition, out Quaternion wristRotation);
            if (hasPose != lastHadPose)
            {
                for (int i = 0; i < filters.Length; i++) filters[i].Reset();
                lastHadPose = hasPose;
            }
            Quaternion inverse = hasPose ? Quaternion.Inverse(wristRotation) : Quaternion.identity;

            for (int i = 0; i < filters.Length; i++)
            {
                sourceInterface.TryGetFingertip((WaxFinger)i, out FingertipSample sample);
                rawSamples[i] = sample;
                active[i] = sample.tracked;
                suspect[i] = false;

                if (!sample.tracked)
                {
                    filters[i].Reset();
                    filteredSamples[i] = default;
                    continue;
                }

                Vector3 input = hasPose ? inverse * (sample.worldPosition - wristPosition) : sample.worldPosition;
                float minCutoff = t.fingertipMinCutoff * (sample.highConfidence ? 1f : t.fingertipLowConfidenceScale);
                filters[i].Begin(input, dt, minCutoff, t.fingertipBeta, t.fingertipMaxSpeed);
            }

            // 두 손가락 끝이 겹치면(실제 손가락은 같은 자리에 있을 수 없다) 최근에 더 많이 움직인 쪽을 의심한다.
            for (int a = 0; a < filters.Length; a++)
            {
                for (int b = a + 1; b < filters.Length; b++)
                {
                    if (!active[a] || !active[b]) continue;
                    if (Vector3.Distance(filters[a].Candidate, filters[b].Candidate) >= t.fingertipMinSeparation) continue;
                    int moved = filters[a].Motion >= filters[b].Motion ? a : b;
                    suspect[moved] = true;
                }
            }

            for (int i = 0; i < filters.Length; i++)
            {
                if (!active[i]) continue;
                bool overlap = suspect[i];
                filters[i].Commit(overlap || filters[i].SpeedSuspect, t.fingertipRejectFrames);
                if (filters[i].Rejected)
                {
                    if (filters[i].SpeedSuspect) speedRejectTotal++;
                    else if (overlap) overlapRejectTotal++;
                }

                Vector3 position = filters[i].Position;
                filteredSamples[i] = new FingertipSample
                {
                    tracked = true,
                    highConfidence = rawSamples[i].highConfidence,
                    worldPosition = hasPose ? wristPosition + wristRotation * position : position
                };
            }

            UpdateMarkers();
        }

        private void UpdateMarkers()
        {
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] == null) continue;

                bool visible = showMarkers && filteredSamples[i].tracked;
                markers[i].gameObject.SetActive(visible);
                if (!visible) continue;

                markers[i].position = filteredSamples[i].worldPosition;
                if (markerRenderers[i] == null) continue;
                markerRenderers[i].GetPropertyBlock(block);
                block.SetColor(BaseColorId, filters[i].Rejected || filters[i].Despiked ? RejectedColor : FilteredColor);
                markerRenderers[i].SetPropertyBlock(block);
            }
        }

        /// <summary>필터를 거친 손가락 끝. 필터가 거르고 있는 동안에는 직전 위치다.</summary>
        public bool TryGetFingertip(WaxFinger finger, out FingertipSample sample)
        {
            int i = (int)finger;
            sample = default;
            if (i < 0 || i >= filteredSamples.Length) return false;
            sample = filteredSamples[i];
            return sample.tracked;
        }

        /// <summary>손 기준 자세(손목). 원본 입력이 주는 값을 그대로 전달한다.</summary>
        public bool TryGetHandPose(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            return sourceInterface != null && sourceInterface.TryGetHandPose(out position, out rotation);
        }

        /// <summary>필터 전 원본 손가락 끝(이번 프레임).</summary>
        public FingertipSample GetRaw(WaxFinger finger)
        {
            int i = (int)finger;
            return i >= 0 && i < rawSamples.Length ? rawSamples[i] : default;
        }

        /// <summary>이번 프레임에 이 손가락의 값을 걸렀는가.</summary>
        public bool WasRejected(WaxFinger finger)
        {
            int i = (int)finger;
            return i >= 0 && i < filters.Length && filters[i].Rejected;
        }

        /// <summary>지금까지 이 손가락에서 한 프레임짜리 튀는 값을 중앙 값으로 바꿔 쓴 프레임 수(앱을 켠 뒤 누적).</summary>
        public int DespikedCount(WaxFinger finger)
        {
            int i = (int)finger;
            return i >= 0 && i < filters.Length ? filters[i].DespikedTotal : 0;
        }

        /// <summary>지금까지 이 손가락의 값을 거른 프레임 수(앱을 켠 뒤 누적).</summary>
        public int RejectedCount(WaxFinger finger)
        {
            int i = (int)finger;
            return i >= 0 && i < filters.Length ? filters[i].RejectedTotal : 0;
        }
    }
}
