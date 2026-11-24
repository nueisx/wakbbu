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

        [Tooltip("접촉점을 쓰는 씬에서, 깨진 판의 평균 섞임(깨진 면적 가중, 판마다 주무른 양 기준)이 이 값 이상이어야 Mixed가 된다(0.9 = 90%).")]
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

        [Header("섞임 (판별 주무른 양)")]
        [Tooltip("한 판이 완전히 섞이기까지 필요한 주무른 양(세기 변화량 합). 판 하나에는 가장 많이 움직인 손가락 하나의 변화량만 쌓인다. 필드 이름은 예전 그대로다. " +
                 "기본 20(전체 기준 4.5의 약 4.4배)라서 몇 번 주무르는 동안 초록 판 + 흰 틈이 보인다.")]
        [Min(0.1f)] public float kneadAmountPerStamp = 20f;

        [Header("번지기 (아주 약하게)")]
        [Tooltip("같은 자리를 주물러 세기가 1만큼 변할 때 자리 반경이 더 넓어지는 각도(도). 0이면 꺼진다.")]
        [Min(0f)] public float spreadDegreesPerKnead = 2f;

        [Tooltip("번지기로 늘어나는 반경의 상한(도). 반경은 최대 55 + 10 = 65도다. 세 손가락이 같은 자리면 어디서 멈추는지는 에디터에서 확인한다.")]
        [Min(0f)] public float maxSpreadDegrees = 10f;

        [Header("접촉점 묶기")]
        [Tooltip("손가락이 자리에서 이 각도(도) 안에 있으면 같은 자리를 키우고, 벗어나면 새 자리를 만든다.")]
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

        [Tooltip("주무른 양(MixProgress, 접촉점 씬은 판마다 주무른 양)이 쪼개짐에 주는 비중. 클수록 여러 번 주물러야 점점 잘게 깨진다.")]
        [Range(0f, 1f)] public float fragmentKneadWeight = 0.7f;

        [Tooltip("접촉점 씬에서 남은 부분까지 채우기 시작하는 진행도(평균 섞임 / Mixed 기준값). 깨진 면적이 기준 이상일 때만 채운다.")]
        [Range(0.5f, 1f)] public float mixFillStart = 0.85f;

        [Tooltip("grip이 최대일 때 손가락 쪽이 둥글게 들어가는 정도.")]
        [Range(0f, 1f)] public float dentStrength = 0.5f;

        [Tooltip("오목한 정도가 grip을 따라가는 속도.")]
        [Min(1f)] public float dentFollowSpeed = 12f;

        // ---- 핸드트래킹 ----
        [Header("핸드트래킹: 공")]
        [Tooltip("실물 스펀지 공의 지름(m). 가상 공의 지름과 같아야 한다. 스펀지 공을 고르는 중이라 임시로 7cm. 공 중심 계산(2b)에서 쓴다.")]
        [Range(0.03f, 0.15f)] public float ballDiameter = 0.07f;

        [Header("핸드트래킹: 손가락 끝 필터 (One Euro)")]
        [Tooltip("손가락 끝이 느리게 움직일 때의 차단 주파수(Hz). 작을수록 가만히 있을 때 덜 떨리지만 따라오는 게 늦다.")]
        [Min(0.1f)] public float fingertipMinCutoff = 1.5f;

        [Tooltip("손가락 끝이 빠를수록 차단 주파수를 올리는 정도(Hz per m/s). 클수록 빠른 움직임을 지연 없이 따라간다.")]
        [Min(0f)] public float fingertipBeta = 20f;

        [Tooltip("손가락 신뢰도가 Low일 때 느린 움직임의 차단 주파수(fingertipMinCutoff)에 곱하는 값. 작을수록 더 강하게 부드럽게 한다.")]
        [Range(0.05f, 1f)] public float fingertipLowConfidenceScale = 0.5f;

        [Header("핸드트래킹: 공 중심 계산 (Follow Hand)")]
        [Tooltip("손가락 끝 뼈가 공 표면에서 떨어져 있는 거리(m). 뼈 끝점은 손가락 살 안쪽에 있어서 공 표면에서 이만큼 바깥이다. 캘리브레이션(2c)으로 맞춘다.")]
        [Range(0f, 0.03f)] public float ballFingertipPadOffset = 0.008f;

        [Tooltip("세 손가락 끝 사이 거리가 (공 지름 + 보정) × 이 값보다 크면 공을 쥔 게 아니라고 본다(펴진 손에서 공이 튀지 않게).")]
        [Range(1f, 2f)] public float ballHoldSpanScale = 1.4f;

        [Tooltip("손가락 끝이 공을 눌러 안쪽으로 들어가면 세 점을 지나는 원이 작아지고 중심 추정이 손바닥 쪽으로 밀린다(눌림 1mm에 약 2mm). " +
                 "이 원의 반지름이 쥐는 동안의 기준(가장 컸을 때)의 이 비율보다 작아지면 중심 갱신을 멈추고 마지막 중심을 유지한다. 1에 가까울수록 일찍 멈춘다.")]
        [Range(0.7f, 1f)] public float ballCenterSqueezeTolerance = 0.96f;

        [Tooltip("공 중심(손목 좌표계)의 느린 움직임 차단 주파수(Hz). 작을수록 여러 프레임을 길게 평균한다.")]
        [Min(0.1f)] public float ballCenterMinCutoff = 1f;

        [Tooltip("공 중심이 빠를수록 차단 주파수를 올리는 정도(Hz per m/s).")]
        [Min(0f)] public float ballCenterBeta = 10f;

        [Tooltip("공 중심이 이 속도(m/s)를 넘게 움직인 프레임은 튄 값으로 보고 직전 위치를 유지한다.")]
        [Min(0.1f)] public float ballCenterMaxSpeed = 1.5f;

        [Header("핸드트래킹: 왼손 핀치 제스처")]
        [Tooltip("왼손 엄지-검지 핀치를 이 시간(초) 동안 누르고 있으면 리셋한다. 누르는 동안 진행 원이 채워진다.")]
        [Min(0.2f)] public float pinchHoldSeconds = 1f;

        [Header("핸드트래킹: 튀는 값 거르기")]
        [Tooltip("손가락 끝이 이 속도(m/s)를 넘게 움직인 프레임은 튄 값으로 보고 직전 위치를 유지한다.")]
        [Min(0.5f)] public float fingertipMaxSpeed = 3f;

        [Tooltip("두 손가락 끝이 이 거리(m)보다 가까우면 겹친 것으로 보고, 최근에 더 많이 움직인 쪽을 의심 값으로 거른다.")]
        [Min(0f)] public float fingertipMinSeparation = 0.01f;

        [Tooltip("의심 값이 이 프레임 수 넘게 계속되면 진짜 움직임으로 인정하고 새 위치에서 다시 시작한다(필터가 고착되지 않게).")]
        [Range(1, 30)] public int fingertipRejectFrames = 6;
    }
}
