using UnityEngine;
using WaxBall;

namespace Interaction
{
    /// <summary>
    /// 에디터 시험용 가짜 손가락 끝 입력(IFingertipSource). 진짜 핸드트래킹과 똑같이
    /// 손가락 끝 위치 → 필터(FilteredFingertips) → 공 중심 계산(BallCenterEstimator) → (2c) 침투 세기 → SetContact 경로로 들어간다.
    /// handCenter(진짜 공 중심) 주변에 손가락 세 개가 정해진 방향으로 있고, 상태 머신의 grip(Slider 값)만큼 안쪽으로 눌린다.
    /// handCenter를 Scene 뷰에서 옮기면 손이 움직이는 것이고, 아래 노이즈 옵션으로 떨림과 튀는 값, 가려진 손가락(엄지가 검지 자리에 찍힘)을 섞어 필터를 시험한다.
    /// 시험 도구라서 값은 이 컴포넌트의 Inspector에 있다(WaxBallTuning에 넣지 않는다).
    /// </summary>
    public class FakeFingertipSource : MonoBehaviour, IFingertipSource
    {
        [SerializeField] private WaxBallTuning tuning;
        [SerializeField] private WaxBallStateMachine stateMachine;

        [Tooltip("진짜 공 중심. 이 위치를 공 중심 계산이 찾아내야 한다. 회전하면 손이 같이 돈다.")]
        [SerializeField] private Transform handCenter;

        [Tooltip("엄지, 검지, 중지가 공 중심에서 놓인 방향(handCenter 기준). 손바닥은 이 방향들의 반대쪽이다.")]
        [SerializeField] private Vector3[] directions =
        {
            new Vector3(-0.95f, -0.1f, -0.3f),
            new Vector3(0.8f, 0.4f, -0.45f),
            new Vector3(0.8f, -0.2f, -0.55f),
        };

        [SerializeField] private bool[] fingerTracked = { true, true, true };

        [Tooltip("grip 1일 때 손가락 끝이 안쪽으로 눌리는 깊이(m). 실물 스펀지 공이 눌리는 것을 흉내 낸다.")]
        [SerializeField, Range(0f, 0.03f)] private float squeezeDepth = 0.012f;

        [Tooltip("손목이 공 중심에서 손바닥 쪽으로 떨어진 거리(m).")]
        [SerializeField, Range(0.05f, 0.2f)] private float wristDistance = 0.1f;

        [Header("노이즈 시험")]
        [Tooltip("손가락 끝마다 매 프레임 구 안 무작위로 더하는 위치 떨림의 최대 크기(m). 0이면 떨림 없음. 실제 손에서는 약 2~5mm가 보였다.")]
        [SerializeField, Range(0f, 0.02f)] private float jitter;

        [Tooltip("프레임마다 한 손가락이 갑자기 튈 확률(0~1). 튀는 값 거르기를 시험한다.")]
        [SerializeField, Range(0f, 0.2f)] private float spikeChance;

        [Tooltip("튈 때 벗어나는 거리(m).")]
        [SerializeField, Range(0.01f, 0.15f)] private float spikeDistance = 0.05f;

        [Tooltip("켜면 몇 초마다 잠깐 엄지가 검지 자리에 찍힌다(가려진 손가락을 추정으로 잘못 채운 경우).")]
        [SerializeField] private bool thumbOnIndex;
        [SerializeField, Min(1f)] private float thumbOnIndexPeriod = 4f;
        [SerializeField, Range(0.1f, 2f)] private float thumbOnIndexSeconds = 0.5f;

        [Tooltip("원본 손가락 끝 위치를 보여 주는 구(엄지, 검지, 중지). 비워 두면 표시하지 않는다.")]
        [SerializeField] private Transform[] markers = new Transform[WaxBallStateMachine.FingerCount];

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color[] FingerColors = { new Color(1f, 0.25f, 0.2f), new Color(0.3f, 0.9f, 0.3f), new Color(0.3f, 0.5f, 1f) };

        private WaxBallTuning Tuning => tuning != null ? tuning : WaxBallTuning.Default;

        private readonly FingertipSample[] samples = new FingertipSample[WaxBallStateMachine.FingerCount];
        private Vector3 wristPosition;
        private Quaternion wristRotation = Quaternion.identity;
        private bool havePose;
        private MaterialPropertyBlock block;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
        }

        private void Update()
        {
            havePose = false;
            System.Array.Clear(samples, 0, samples.Length);
            if (handCenter == null) return;

            var t = Tuning;
            float radius = t.ballDiameter * 0.5f + t.ballFingertipPadOffset;
            float squeeze = stateMachine != null ? stateMachine.Grip * squeezeDepth : 0f;
            Quaternion rotation = handCenter.rotation;
            Vector3 center = handCenter.position;

            Vector3 meanDirection = Vector3.zero;
            for (int i = 0; i < directions.Length; i++) meanDirection += directions[i].normalized;

            wristRotation = rotation;
            wristPosition = center - rotation * meanDirection.normalized * wristDistance;
            havePose = true;

            for (int i = 0; i < samples.Length && i < directions.Length; i++)
            {
                if (i < fingerTracked.Length && !fingerTracked[i]) continue;

                Vector3 position = center + rotation * directions[i].normalized * (radius - squeeze);
                position += Random.insideUnitSphere * jitter;
                if (spikeChance > 0f && Random.value < spikeChance)
                    position += Random.onUnitSphere * spikeDistance;

                samples[i] = new FingertipSample { tracked = true, highConfidence = true, worldPosition = position };
            }

            // 엄지가 검지 자리에 찍힌 것처럼 보이게 한다(약간 벗어나게 해서 완전히 같은 점이 되지 않게).
            if (thumbOnIndex && samples[(int)WaxFinger.Index].tracked && samples[(int)WaxFinger.Thumb].tracked
                && Time.time % thumbOnIndexPeriod < thumbOnIndexSeconds)
            {
                samples[(int)WaxFinger.Thumb].worldPosition = samples[(int)WaxFinger.Index].worldPosition + Vector3.up * 0.004f;
            }

            UpdateMarkers();
        }

        private void UpdateMarkers()
        {
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] == null) continue;
                markers[i].gameObject.SetActive(samples[i].tracked);
                if (!samples[i].tracked) continue;

                markers[i].position = samples[i].worldPosition;
                var markerRenderer = markers[i].GetComponent<Renderer>();
                if (markerRenderer == null) continue;
                markerRenderer.GetPropertyBlock(block);
                block.SetColor(BaseColorId, FingerColors[i]);
                markerRenderer.SetPropertyBlock(block);
            }
        }

        public bool TryGetFingertip(WaxFinger finger, out FingertipSample sample)
        {
            int i = (int)finger;
            sample = default;
            if (i < 0 || i >= samples.Length) return false;
            sample = samples[i];
            return sample.tracked;
        }

        public bool TryGetHandPose(out Vector3 position, out Quaternion rotation)
        {
            position = wristPosition;
            rotation = wristRotation;
            return havePose;
        }
    }
}
