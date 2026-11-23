using UnityEngine;

namespace WaxBall
{
    /// <summary>
    /// 왁뿌볼 튜닝 값(상태 임계값, 파손 범위·속도, 섞임, 쪼개짐, Crunch 기준, 깨지는 시간 등)을 한 파일로 모은 설정.
    /// Assets/Settings/WaxBallTuning.asset 하나를 모든 씬(dev, Quest)의 컴포넌트가 참조한다.
    /// 값을 바꿀 때는 이 파일만 고친다(컴포넌트나 씬에 따로 저장된 값이 없으므로 씬마다 어긋나지 않는다).
    /// 연결이 비어 있는 컴포넌트는 아래 기본값(`Default`)을 쓴다.
    /// </summary>
    [CreateAssetMenu(fileName = "WaxBallTuning", menuName = "WaxBall/Tuning")]
    public class WaxBallTuning : ScriptableObject
    {
        private static WaxBallTuning fallback;

        /// <summary>연결된 설정이 없을 때 쓰는 기본값(저장되지 않는 인스턴스).</summary>
        public static WaxBallTuning Default
        {
            get
            {
                if (fallback == null)
                {
                    fallback = CreateInstance<WaxBallTuning>();
                    fallback.hideFlags = HideFlags.HideAndDontSave;
                }
                return fallback;
            }
        }

        // ---- 상태 머신 ----
        [Header("임계값 (Interaction 담당이 튜닝)")]
        [Range(0f, 1f)] public float pressThreshold = 0.15f;

        [Range(0f, 1f)] public float crackThreshold = 0.55f;

        [Range(0f, 1f)] public float breakThreshold = 0.85f;

        [Min(0.01f)] public float kneadAmountToMix = 4.5f;

        [Tooltip("접촉점을 쓰는 씬에서, 공 표면 중 파손된 면적 비율이 이 값 이상이면 Mixed가 된다(0.8 = 80%). 접촉점이 없는 씬은 kneadAmountToMix를 쓴다.")]
        [Range(0.1f, 1f)] public float mixAreaThreshold = 0.8f;

        [Tooltip("접촉점을 쓰는 씬에서, 깨진 자리의 평균 섞임(깨진 면적 가중, 흔적마다 주무른 양 기준)이 이 값 이상이어야 Mixed가 된다(0.9 = 90%).")]
        [Range(0.1f, 1f)] public float mixAverageThreshold = 0.9f;

        [Header("Crunched (Broken 상태에서 손가락으로 주무를 때)")]
        [Tooltip("한 손가락의 세기 변화량이 이만큼 쌓일 때마다 그 손가락에 Crunched를 한 번 보낸다.")]
        [Min(0.01f)] public float crunchGripStep = 0.5f;

        [Tooltip("한 손가락의 Crunched 최소 간격(초). 이보다 빨리 쌓여도 간격이 지날 때까지 기다린다. 장갑 진동이 떨림이 되지 않게 한다.")]
        [Min(0f)] public float crunchMinInterval = 0.1f;

        [Tooltip("가장 천천히 주물렀을 때의 세기.")]
        [Range(0f, 1f)] public float crunchMinStrength = 0.2f;

        [Tooltip("이 속도(세기/초) 이상으로 주무르면 세기가 1이 된다.")]
        [Min(0.01f)] public float crunchFullSpeed = 2f;

        // ---- 파손 범위 ----
        [Header("파손 범위")]
        [Tooltip("이 세기(0~1) 이상으로 누르는 손가락만 파손을 만든다.")]
        [Range(0f, 1f)] public float damagePressureThreshold = 0.3f;

        [Tooltip("파손 정도 0일 때의 반경(도). Cracked 직후의 작은 범위.")]
        [Range(5f, 60f)] public float minRadiusDegrees = 20f;

        [Tooltip("파손 정도 1일 때의 반경(도). 한 손가락만으로는 공 전체로 퍼지지 않는다.")]
        [Range(30f, 120f)] public float maxRadiusDegrees = 55f;

        [Tooltip("세기 1로 누르고만 있을 때 초당 늘어나는 파손 정도. 거의 0으로 두어 첫 깨짐 이후에는 주물러야 범위가 커진다.")]
        [Min(0f)] public float pressureDamageRate = 0.03f;

        [Tooltip("손가락 세기가 1만큼 변할 때(주무를 때) 늘어나는 파손 정도.")]
        [Min(0f)] public float kneadDamageGain = 0.4f;

        [Header("섞임 (흔적별 주무른 양)")]
        [Tooltip("한 흔적이 완전히 섞이기까지 필요한 주무른 양(세기 변화량 합). 흔적 하나에는 가장 많이 움직인 손가락 하나의 변화량만 쌓인다. " +
                 "기본 20(전체 기준 4.5의 약 4.4배)라서 몇 번 주무르는 동안 초록 판 + 흰 틈이 보인다.")]
        [Min(0.1f)] public float kneadAmountPerStamp = 20f;

        [Header("번지기 (아주 약하게)")]
        [Tooltip("같은 자리를 주물러 세기가 1만큼 변할 때 흔적 반경이 더 넓어지는 각도(도). 0이면 꺼진다.")]
        [Min(0f)] public float spreadDegreesPerKnead = 2f;

        [Tooltip("번지기로 늘어나는 반경의 상한(도). 반경은 최대 55 + 10 = 65도라 한 흔적이 약 22%이고, 세 손가락이 같은 자리면 40~50%에서 멈춘다.")]
        [Min(0f)] public float maxSpreadDegrees = 10f;

        [Header("접촉점 묶기")]
        [Tooltip("손가락이 흔적에서 이 각도(도) 안에 있으면 같은 흔적을 키우고, 벗어나면 새 흔적을 만든다.")]
        [Range(5f, 60f)] public float rebindAngleDegrees = 25f;

        // ---- 시각 효과 ----
        [Header("찌그러짐·균열")]
        [Tooltip("Cracked까지 grip이 최대일 때의 찌그러짐.")]
        [Range(0f, 0.5f)] public float maxSquash = 0.04f;

        [Tooltip("껍질이 깨진 뒤(Broken 이후)의 최대 찌그러짐. 껍질이 안 버티므로 maxSquash보다 크다.")]
        [Range(0f, 0.7f)] public float brokenSquash = 0.08f;

        [Tooltip("Cracked에 막 들어갔을 때 균열이 이미 자라 있는 정도(0~1).")]
        [Range(0f, 1f)] public float minCrackAmount = 0.15f;

        [Header("파열")]
        [Tooltip("Broken에 들어갈 때 큰 판으로 갈라지는 데 걸리는 시간(초). 시각 효과만 늦춘다. StateChanged와 햅틱 이벤트는 즉시 발생한다.")]
        [Min(0.01f)] public float brokenRampDuration = 0.6f;

        [Tooltip("판이 더 잘게 쪼개지는 속도(1/초). 작을수록 천천히 쪼개진다.")]
        [Min(0.01f)] public float fragmentSpeed = 0.5f;

        [Tooltip("Broken에 막 들어갔을 때 이미 쪼개져 있는 정도(0~1).")]
        [Range(0f, 1f)] public float fragmentBase = 0f;

        [Tooltip("쥐는 힘(grip)이 쪼개짐에 주는 비중. 작을수록 트리거를 조금 움직여서는 쪼개지지 않는다.")]
        [Range(0f, 1f)] public float fragmentGripWeight = 0.05f;

        [Tooltip("주무른 양(MixProgress, 접촉점 씬은 흔적마다 주무른 양)이 쪼개짐에 주는 비중. 클수록 여러 번 주물러야 점점 잘게 깨진다.")]
        [Range(0f, 1f)] public float fragmentKneadWeight = 0.7f;

        [Tooltip("접촉점 씬에서 남은 부분까지 채우기 시작하는 진행도(평균 섞임 / Mixed 기준값). 깨진 면적이 기준 이상일 때만 채운다.")]
        [Range(0.5f, 1f)] public float mixFillStart = 0.85f;

        [Tooltip("grip이 최대일 때 손가락 쪽이 둥글게 들어가는 정도.")]
        [Range(0f, 1f)] public float dentStrength = 0.5f;

        [Tooltip("오목한 정도가 grip을 따라가는 속도.")]
        [Min(1f)] public float dentFollowSpeed = 12f;
    }
}
