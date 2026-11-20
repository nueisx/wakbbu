using UnityEngine;

namespace WaxBall
{
    /// <summary>
    /// 왁스 껍질 시각 효과. 구조는 투명 고무층 > 왁스 껍질 > 흰 점토이고, 파편은 고무층 안에 갇혀 밖으로 튀지 않는다.
    /// grip에 따른 찌그러짐, 균열(_CrackAmounts), 파열(_BrokenAmount, _Fragment), 섞임(_MixAmount)을 셰이더에 넘긴다.
    /// 접촉점을 쓰는 씬(WaxBallDamage.LocalMode)에서는 파손 흔적 주변만 깨지고, 균열은 손가락 아래에서 시작한다.
    /// 접촉점을 쓰지 않는 씬(Slider만 사용)에서는 공 전체가 깨지고, 균열의 시작점은 리셋(Idle)마다 무작위로 바뀐다.
    /// 균열의 방향, 길이, 휘어짐은 어느 경우든 리셋마다 무작위로 바뀐다.
    /// 이벤트와 Grip/MixProgress 프로퍼티를 읽기만 하고 상태 판정은 하지 않는다.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class WaxBallShellVisual : MonoBehaviour
    {
        [Tooltip("튜닝 값(임계값, 범위, 속도 등)을 모아 둔 설정 파일. 비워 두면 코드 기본값(WaxBallTuning의 필드 초기값)을 쓴다. Assets/Settings/WaxBallTuning.asset 하나를 모든 씬이 같이 쓴다.")]
        [SerializeField] private WaxBallTuning tuning;

        private WaxBallTuning Tuning => tuning != null ? tuning : WaxBallTuning.Default;
        private float maxSquash => Tuning.maxSquash;
        private float brokenSquash => Tuning.brokenSquash;
        private float minCrackAmount => Tuning.minCrackAmount;
        private float brokenRampDuration => Tuning.brokenRampDuration;
        private float fragmentSpeed => Tuning.fragmentSpeed;
        private float fragmentBase => Tuning.fragmentBase;
        private float fragmentGripWeight => Tuning.fragmentGripWeight;
        private float fragmentKneadWeight => Tuning.fragmentKneadWeight;
        private float mixFillStart => Tuning.mixFillStart;
        private float dentStrength => Tuning.dentStrength;
        private float dentFollowSpeed => Tuning.dentFollowSpeed;

        [SerializeField] private WaxBallStateMachine stateMachine;
        [Tooltip("파손 범위 모델. 비워 두면 같은 오브젝트에서 찾고, 없으면 공 전체가 깨진다.")]
        [SerializeField] private WaxBallDamage damage;

        [Header("균열 시작점 (무작위)")]
        [Tooltip("무작위 시작점을 뽑을 때 중심이 되는 방향(공 로컬 좌표). 개발 씬 카메라 쪽이 -z이다.")]
        [SerializeField] private Vector3 randomStartForward = new Vector3(0f, 0f, -1f);
        [SerializeField, Range(10f, 180f)] private float randomStartConeAngle = 70f;
        [SerializeField, Range(0f, 90f)] private float minStartSeparation = 35f;

        private static readonly int CrackAmountsId = Shader.PropertyToID("_CrackAmounts");
        private static readonly int LocalDamageId = Shader.PropertyToID("_LocalDamage");
        private static readonly int StampsId = Shader.PropertyToID("_Stamps");
        private static readonly int BrokenAmountId = Shader.PropertyToID("_BrokenAmount");
        private static readonly int FragmentId = Shader.PropertyToID("_Fragment");
        private static readonly int StampMixId = Shader.PropertyToID("_StampMix");
        private static readonly int MixFillId = Shader.PropertyToID("_MixFill");
        private static readonly int FragmentKneadId = Shader.PropertyToID("_FragmentKnead");
        private static readonly int MixAmountId = Shader.PropertyToID("_MixAmount");
        private static readonly int DentDirId = Shader.PropertyToID("_DentDir");
        private static readonly int DentAmountId = Shader.PropertyToID("_DentAmount");
        private static readonly int Crack1AId = Shader.PropertyToID("_Crack1A");
        private static readonly int Crack1BId = Shader.PropertyToID("_Crack1B");
        private static readonly int Crack2AId = Shader.PropertyToID("_Crack2A");
        private static readonly int Crack2BId = Shader.PropertyToID("_Crack2B");

        private Renderer rend;
        private MaterialPropertyBlock block;
        private Vector3 baseScale;
        private float crackAmount;
        private float brokenAmount;
        private float fragmentAmount;
        private float dentAmount;
        private float mixFill;

        // 셰이더에 넘기는 균열 파라미터. A = (시작점 xyz, 길이 배율), B = (방향 각도, 휘어짐, 시드, 폭 배율)
        private Vector3 crackStart1;
        private Vector3 crackStart2;
        private Vector4 crack1A, crack1B, crack2A, crack2B;
        private bool startsOverridden;
        private readonly Vector4[] stamps = new Vector4[WaxBallDamage.MaxStamps];
        private readonly float[] stampMix = new float[WaxBallDamage.MaxStamps];

        private void Awake()
        {
            rend = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            baseScale = transform.localScale;
            if (stateMachine == null) stateMachine = GetComponentInParent<WaxBallStateMachine>();
            if (damage == null) damage = GetComponent<WaxBallDamage>();
            RandomizeCracks();
        }

        private void OnEnable()
        {
            stateMachine.StateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (stateMachine != null) stateMachine.StateChanged -= HandleStateChanged;
        }

        private void Update()
        {
            UpdateCrack();
            UpdateBroken();
            ApplyProperties();
            UpdateSquash();
        }

        /// <summary>
        /// 균열 두 줄의 시작점을 직접 지정한다(공 로컬 방향). 손가락이 닿은 위치로 바꿀 때 쓴다.
        /// 오목하게 눌리는 중심도 첫 번째 시작점을 따른다. 리셋해도 유지되며, 해제하려면 ClearCrackStarts()를 부른다.
        /// </summary>
        public void SetCrackStarts(Vector3 localDirection1, Vector3 localDirection2)
        {
            startsOverridden = true;
            crackStart1 = localDirection1.normalized;
            crackStart2 = localDirection2.normalized;
            BuildCrackParameters();
        }

        /// <summary>시작점을 다시 무작위로 뽑는다.</summary>
        public void ClearCrackStarts()
        {
            startsOverridden = false;
            RandomizeCracks();
        }

        private void HandleStateChanged(WaxBallState prev, WaxBallState next)
        {
            // 한 번 간 균열은 grip을 줄여도 되돌아가지 않고, 리셋(Idle)에서만 사라진다.
            if (next == WaxBallState.Idle)
            {
                crackAmount = 0f;
                brokenAmount = 0f;
                fragmentAmount = 0f;
                dentAmount = 0f;
                RandomizeCracks();
            }
        }

        private void UpdateCrack()
        {
            float target;
            switch (stateMachine.State)
            {
                case WaxBallState.Cracked:
                    float t = Mathf.InverseLerp(stateMachine.CrackThreshold, stateMachine.BreakThreshold, stateMachine.Grip);
                    target = Mathf.Lerp(minCrackAmount, 1f, t);
                    break;
                case WaxBallState.Broken:
                case WaxBallState.Mixed:
                    target = 1f;
                    break;
                default:
                    target = 0f;
                    break;
            }
            crackAmount = Mathf.Max(crackAmount, target);
        }

        private void UpdateBroken()
        {
            bool broken = stateMachine.State >= WaxBallState.Broken;
            brokenAmount = Mathf.MoveTowards(brokenAmount, broken ? 1f : 0f, Time.deltaTime / brokenRampDuration);

            // 껍질이 깨진 뒤에는 grip에 따라 손가락 쪽이 둥글게 들어간다(손에 든 스펀지 공이 눌리는 것과 맞춘다).
            float dentTarget = broken ? stateMachine.Grip * dentStrength : 0f;
            dentAmount = Mathf.Lerp(dentAmount, dentTarget, 1f - Mathf.Exp(-dentFollowSpeed * Time.deltaTime));

            if (!broken) return;

            // 주물러서 쌓인 양(MixProgress)이 늘수록 판이 점점 잘게 쪼개지고, 쥐는 힘은 조금만 거든다.
            // 한 번 쪼개진 판은 다시 합쳐지지 않는다.
            float gripFactor = Mathf.InverseLerp(stateMachine.BreakThreshold, 1f, stateMachine.Grip);
            // 접촉점 씬은 주무른 양 몫을 셰이더가 흔적마다 더하므로 여기서는 뺀다.
            bool local = damage != null && damage.LocalMode;
            float kneadPart = local ? 0f : fragmentKneadWeight * stateMachine.MixProgress;
            float target = Mathf.Clamp01(fragmentBase + fragmentGripWeight * gripFactor + kneadPart);
            fragmentAmount = Mathf.Max(fragmentAmount, Mathf.MoveTowards(fragmentAmount, target, fragmentSpeed * Time.deltaTime));
        }

        private void ApplyProperties()
        {
            bool local = damage != null && damage.LocalMode;

            // 두 번째 균열은 늦게 나타나고 더 짧다.
            float amount1 = crackAmount;
            float amount2 = Mathf.Clamp01((crackAmount - 0.3f) / 0.7f) * 0.75f;
            Vector4 a1 = crack1A;
            Vector4 a2 = crack2A;
            Vector3 dentDirection = crackStart1;

            rend.GetPropertyBlock(block);

            if (local)
            {
                // 접촉점을 쓰면 균열은 손가락 아래에서 시작한다. 아직 붙은 손가락이 없으면 그 균열은 보이지 않는다.
                if (damage.TryGetCrackAnchor(0, out Vector3 anchor1))
                {
                    a1 = new Vector4(anchor1.x, anchor1.y, anchor1.z, crack1A.w);
                    dentDirection = anchor1;
                }
                else amount1 = 0f;

                if (damage.TryGetCrackAnchor(1, out Vector3 anchor2))
                    a2 = new Vector4(anchor2.x, anchor2.y, anchor2.z, crack2A.w);
                else amount2 = 0f;

                damage.FillShaderStamps(stamps, stampMix);
                block.SetVectorArray(StampsId, stamps);
                block.SetFloatArray(StampMixId, stampMix);

                // 깨진 면적이 기준 이상이고 평균 섞임이 Mixed에 가까울 때만 남은 부분까지 채운다.
                float progress = Mathf.Clamp01(stateMachine.MixProgress / stateMachine.MixAverageThreshold);
                bool areaReached = damage.DamagedRatio >= stateMachine.MixAreaThreshold;
                mixFill = areaReached ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(mixFillStart, 1f, progress)) : 0f;
            }

            block.SetFloat(LocalDamageId, local ? 1f : 0f);
            block.SetFloat(MixFillId, local ? mixFill : 0f);
            block.SetFloat(FragmentKneadId, local ? fragmentKneadWeight : 0f);
            block.SetVector(CrackAmountsId, new Vector4(amount1, amount2, 0f, 0f));
            block.SetFloat(BrokenAmountId, brokenAmount);
            block.SetFloat(FragmentId, fragmentAmount);
            block.SetFloat(MixAmountId, stateMachine.MixProgress);
            block.SetVector(DentDirId, dentDirection);
            block.SetFloat(DentAmountId, dentAmount);
            block.SetVector(Crack1AId, a1);
            block.SetVector(Crack1BId, crack1B);
            block.SetVector(Crack2AId, a2);
            block.SetVector(Crack2BId, crack2B);
            rend.SetPropertyBlock(block);
        }

        private void UpdateSquash()
        {
            float squash = stateMachine.Grip * Mathf.Lerp(maxSquash, brokenSquash, brokenAmount);
            transform.localScale = new Vector3(
                baseScale.x * (1f + squash * 0.5f),
                baseScale.y * (1f - squash),
                baseScale.z * (1f + squash * 0.5f));
        }

        private void RandomizeCracks()
        {
            if (!startsOverridden)
            {
                crackStart1 = RandomDirectionInCone(randomStartForward, randomStartConeAngle);
                crackStart2 = crackStart1;
                for (int i = 0; i < 12 && Vector3.Angle(crackStart1, crackStart2) < minStartSeparation; i++)
                    crackStart2 = RandomDirectionInCone(randomStartForward, randomStartConeAngle);
            }
            BuildCrackParameters();
        }

        // 두 줄의 방향, 길이, 휘어짐, 폭을 서로 다르게 뽑는다.
        private void BuildCrackParameters()
        {
            float angle1 = Random.Range(0f, Mathf.PI);
            float angle2 = angle1 + Random.Range(0.8f, 2.3f);

            crack1A = new Vector4(crackStart1.x, crackStart1.y, crackStart1.z, Random.Range(0.8f, 1.3f));
            crack1B = new Vector4(angle1, Random.Range(0.15f, 0.45f), Random.Range(0f, 50f), Random.Range(0.85f, 1.2f));
            crack2A = new Vector4(crackStart2.x, crackStart2.y, crackStart2.z, Random.Range(0.45f, 0.8f));
            crack2B = new Vector4(angle2, Random.Range(0.1f, 0.5f), Random.Range(50f, 100f), Random.Range(0.6f, 0.95f));
        }

        private static Vector3 RandomDirectionInCone(Vector3 forward, float halfAngleDegrees)
        {
            float cosMax = Mathf.Cos(halfAngleDegrees * Mathf.Deg2Rad);
            float cosTheta = Random.Range(cosMax, 1f);
            float sinTheta = Mathf.Sqrt(1f - cosTheta * cosTheta);
            float phi = Random.Range(0f, 2f * Mathf.PI);
            var local = new Vector3(sinTheta * Mathf.Cos(phi), sinTheta * Mathf.Sin(phi), cosTheta);
            return Quaternion.FromToRotation(Vector3.forward, forward.normalized) * local;
        }
    }
}
