using UnityEngine;

namespace WaxBall
{
    /// <summary>
    /// 왁스 껍질 시각 효과. 구조는 투명 고무층 > 왁스 껍질 > 흰 점토이고, 파편은 고무층 안에 갇혀 밖으로 튀지 않는다.
    /// grip에 따른 찌그러짐, 균열(_CrackAmount), 파열(_BrokenAmount, _Fragment), 섞임(_MixAmount)을 셰이더에 넘긴다.
    /// 균열의 시작점, 방향, 길이, 휘어짐은 리셋(Idle)마다 무작위로 바뀐다.
    /// 이벤트와 Grip/MixProgress 프로퍼티를 읽기만 하고 상태 판정은 하지 않는다.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class WaxBallShellVisual : MonoBehaviour
    {
        [SerializeField] private WaxBallStateMachine stateMachine;
        [SerializeField, Range(0f, 0.5f)] private float maxSquash = 0.25f;
        [Tooltip("껍질이 깨진 뒤(Broken 이후)의 최대 찌그러짐. 껍질이 안 버티므로 maxSquash보다 크다.")]
        [SerializeField, Range(0f, 0.7f)] private float brokenSquash = 0.45f;
        [SerializeField, Range(0f, 1f)] private float minCrackAmount = 0.15f;

        [Header("파열")]
        [Tooltip("파열 변형이 나타나는 속도(1/초). 클수록 파삭 하고 즉시 갈라진다.")]
        [SerializeField, Min(0.1f)] private float brokenRampSpeed = 10f;
        [Tooltip("판이 더 잘게 쪼개지는 속도(1/초).")]
        [SerializeField, Min(0.1f)] private float fragmentSpeed = 1.5f;
        [Tooltip("손가락 쪽으로 오목하게 눌리는 정도.")]
        [SerializeField, Range(0f, 1f)] private float dentStrength = 0.8f;

        [Header("균열 시작점 (무작위)")]
        [Tooltip("무작위 시작점을 뽑을 때 중심이 되는 방향(공 로컬 좌표). 개발 씬 카메라 쪽이 -z이다.")]
        [SerializeField] private Vector3 randomStartForward = new Vector3(0f, 0f, -1f);
        [SerializeField, Range(10f, 180f)] private float randomStartConeAngle = 70f;
        [SerializeField, Range(0f, 90f)] private float minStartSeparation = 35f;

        private static readonly int CrackAmountId = Shader.PropertyToID("_CrackAmount");
        private static readonly int BrokenAmountId = Shader.PropertyToID("_BrokenAmount");
        private static readonly int FragmentId = Shader.PropertyToID("_Fragment");
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

        // 셰이더에 넘기는 균열 파라미터. A = (시작점 xyz, 길이 배율), B = (방향 각도, 휘어짐, 시드, 폭 배율)
        private Vector3 crackStart1;
        private Vector3 crackStart2;
        private Vector4 crack1A, crack1B, crack2A, crack2B;
        private bool startsOverridden;

        private void Awake()
        {
            rend = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            baseScale = transform.localScale;
            if (stateMachine == null) stateMachine = GetComponentInParent<WaxBallStateMachine>();
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
            brokenAmount = Mathf.MoveTowards(brokenAmount, broken ? 1f : 0f, brokenRampSpeed * Time.deltaTime);

            if (!broken) return;

            // 쥐는 힘과 주무른 양이 늘수록 판이 잘게 쪼개진다. 한 번 쪼개진 판은 다시 합쳐지지 않는다.
            float gripFactor = Mathf.InverseLerp(stateMachine.BreakThreshold, 1f, stateMachine.Grip);
            float target = Mathf.Clamp01(0.2f + 0.4f * gripFactor + 0.4f * stateMachine.MixProgress);
            fragmentAmount = Mathf.Max(fragmentAmount, Mathf.MoveTowards(fragmentAmount, target, fragmentSpeed * Time.deltaTime));
        }

        private void ApplyProperties()
        {
            rend.GetPropertyBlock(block);
            block.SetFloat(CrackAmountId, crackAmount);
            block.SetFloat(BrokenAmountId, brokenAmount);
            block.SetFloat(FragmentId, fragmentAmount);
            block.SetFloat(MixAmountId, stateMachine.MixProgress);
            block.SetVector(DentDirId, crackStart1);
            block.SetFloat(DentAmountId, brokenAmount * dentStrength);
            block.SetVector(Crack1AId, crack1A);
            block.SetVector(Crack1BId, crack1B);
            block.SetVector(Crack2AId, crack2A);
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
