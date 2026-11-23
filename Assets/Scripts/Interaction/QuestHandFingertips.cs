using UnityEngine;
using WaxBall;

namespace Interaction
{
    /// <summary>
    /// Quest 맨손 핸드트래킹에서 엄지·검지·중지 끝 위치와 손가락별 추적 신뢰도를 읽는 어댑터.
    /// 읽기만 하고 WaxBallContacts에는 아직 아무것도 넣지 않는다(접촉점 변환은 다음 단계).
    /// 켜 두면 손가락 끝에 작은 구 표시(markers)를 띄운다. 신뢰도가 높으면 손가락 색(엄지 빨강, 검지 초록, 중지 파랑),
    /// 낮으면 회색이고, 추적이 끊기면 숨긴다.
    /// OVRHand / OVRSkeleton은 씬 빌더가 오른손(또는 설정한 손) 쪽에서 연결한다.
    /// </summary>
    public class QuestHandFingertips : MonoBehaviour, IFingertipSource
    {
        [SerializeField] private OVRHand hand;
        [SerializeField] private OVRSkeleton skeleton;
        [Tooltip("엄지, 검지, 중지 순서의 표시용 구. 비워 두면 표시하지 않는다.")]
        [SerializeField] private Transform[] markers = new Transform[WaxBallStateMachine.FingerCount];
        [SerializeField] private bool showMarkers = true;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color[] FingerColors = { new Color(1f, 0.25f, 0.2f), new Color(0.3f, 0.9f, 0.3f), new Color(0.3f, 0.5f, 1f) };
        private static readonly Color LowConfidenceColor = new Color(0.6f, 0.6f, 0.6f);

        private readonly Transform[] tips = new Transform[WaxBallStateMachine.FingerCount];
        private Renderer[] markerRenderers;
        private MaterialPropertyBlock block;
        private int cachedBoneCount = -1;
        private OVRSkeleton.SkeletonType cachedType;

        /// <summary>손이 추적되고 있는가(OVRHand.IsTracked).</summary>
        public bool HandTracked => hand != null && hand.IsTracked;

        /// <summary>손 전체 추적 신뢰도가 높은가.</summary>
        public bool HandHighConfidence => hand != null && hand.IsTracked && hand.HandConfidence == OVRHand.TrackingConfidence.High;

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            markerRenderers = new Renderer[markers.Length];
            for (int i = 0; i < markers.Length; i++)
                if (markers[i] != null) markerRenderers[i] = markers[i].GetComponent<Renderer>();
        }

        private void Update()
        {
            RefreshTips();

            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] == null) continue;

                bool tracked = showMarkers && TryGetFingertip((WaxFinger)i, out FingertipSample sample) && sample.tracked;
                markers[i].gameObject.SetActive(tracked);
                if (!tracked) continue;

                markers[i].position = sample.worldPosition;
                if (markerRenderers[i] == null) continue;
                markerRenderers[i].GetPropertyBlock(block);
                block.SetColor(BaseColorId, sample.highConfidence ? FingerColors[i] : LowConfidenceColor);
                markerRenderers[i].SetPropertyBlock(block);
            }
        }

        public bool TryGetFingertip(WaxFinger finger, out FingertipSample sample)
        {
            sample = default;
            int i = (int)finger;
            if (i < 0 || i >= tips.Length || hand == null || skeleton == null) return false;
            if (!hand.IsTracked || !skeleton.IsInitialized || !skeleton.IsDataValid || tips[i] == null) return false;

            sample.tracked = true;
            sample.worldPosition = tips[i].position;
            sample.highConfidence = hand.GetFingerConfidence(ToHandFinger(finger)) == OVRHand.TrackingConfidence.High;
            return true;
        }

        private static OVRHand.HandFinger ToHandFinger(WaxFinger finger)
        {
            switch (finger)
            {
                case WaxFinger.Thumb: return OVRHand.HandFinger.Thumb;
                case WaxFinger.Index: return OVRHand.HandFinger.Index;
                default: return OVRHand.HandFinger.Middle;
            }
        }

        // 손 뼈대가 만들어지거나 종류(OVR/OpenXR)가 바뀌면 끝 뼈의 Transform을 다시 찾는다.
        private void RefreshTips()
        {
            if (skeleton == null || !skeleton.IsInitialized || skeleton.Bones == null)
            {
                cachedBoneCount = -1;
                System.Array.Clear(tips, 0, tips.Length);
                return;
            }

            var type = skeleton.GetSkeletonType();
            if (skeleton.Bones.Count == cachedBoneCount && type == cachedType) return;
            cachedBoneCount = skeleton.Bones.Count;
            cachedType = type;

            bool openXr = type == OVRSkeleton.SkeletonType.XRHandLeft || type == OVRSkeleton.SkeletonType.XRHandRight;
            var ids = openXr
                ? new[] { OVRSkeleton.BoneId.XRHand_ThumbTip, OVRSkeleton.BoneId.XRHand_IndexTip, OVRSkeleton.BoneId.XRHand_MiddleTip }
                : new[] { OVRSkeleton.BoneId.Hand_ThumbTip, OVRSkeleton.BoneId.Hand_IndexTip, OVRSkeleton.BoneId.Hand_MiddleTip };

            for (int f = 0; f < tips.Length; f++)
            {
                tips[f] = null;
                foreach (var bone in skeleton.Bones)
                {
                    if (bone.Id == ids[f]) { tips[f] = bone.Transform; break; }
                }
            }
        }
    }
}
