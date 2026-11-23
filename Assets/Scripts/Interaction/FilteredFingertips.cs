using UnityEngine;
using WaxBall;

namespace Interaction
{
    /// <summary>
    /// 손가락 끝 입력(IFingertipSource) 위에 필터를 얹은 입력. 손가락마다 FingertipFilter로 위치를 부드럽게 하고,
    /// 속도가 비정상이거나 다른 손가락과 겹친 값(가려진 손가락을 추정으로 잘못 찍은 경우)을 거른다.
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
                filters[i].Begin(sample.worldPosition, dt, !sample.highConfidence, t);
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
                filters[i].Commit(suspect[i] || filters[i].SpeedSuspect, t.fingertipRejectFrames);
                filteredSamples[i] = new FingertipSample
                {
                    tracked = true,
                    highConfidence = rawSamples[i].highConfidence,
                    worldPosition = filters[i].Position
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
                block.SetColor(BaseColorId, filters[i].Rejected ? RejectedColor : FilteredColor);
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

        /// <summary>지금까지 이 손가락의 값을 거른 프레임 수(앱을 켠 뒤 누적).</summary>
        public int RejectedCount(WaxFinger finger)
        {
            int i = (int)finger;
            return i >= 0 && i < filters.Length ? filters[i].RejectedTotal : 0;
        }
    }
}
