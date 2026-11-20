using UnityEngine;

namespace WaxBall
{
    /// <summary>
    /// 왁스 껍질 시각 효과: grip에 따른 찌그러짐, 균열(_CrackAmount), 파열(_BrokenAmount), 섞임(_MixAmount).
    /// 왁뿌볼은 고무막 안에서 왁스가 깨지므로 파편은 밖으로 튀지 않고, 표면 변형과 질감으로만 표현한다.
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
        [Tooltip("파열 변형이 나타나는 속도(1/초). 클수록 파삭 하고 즉시 어긋난다.")]
        [SerializeField, Min(0.1f)] private float brokenRampSpeed = 10f;

        private static readonly int CrackAmountId = Shader.PropertyToID("_CrackAmount");
        private static readonly int BrokenAmountId = Shader.PropertyToID("_BrokenAmount");
        private static readonly int MixAmountId = Shader.PropertyToID("_MixAmount");

        private Renderer rend;
        private MaterialPropertyBlock block;
        private Vector3 baseScale;
        private float crackAmount;
        private float brokenAmount;

        private void Awake()
        {
            rend = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            baseScale = transform.localScale;
            if (stateMachine == null) stateMachine = GetComponentInParent<WaxBallStateMachine>();
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

        private void HandleStateChanged(WaxBallState prev, WaxBallState next)
        {
            // 한 번 간 균열은 grip을 줄여도 되돌아가지 않고, 리셋(Idle)에서만 사라진다.
            if (next == WaxBallState.Idle)
            {
                crackAmount = 0f;
                brokenAmount = 0f;
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
            float target = stateMachine.State >= WaxBallState.Broken ? 1f : 0f;
            brokenAmount = Mathf.MoveTowards(brokenAmount, target, brokenRampSpeed * Time.deltaTime);
        }

        private void ApplyProperties()
        {
            rend.GetPropertyBlock(block);
            block.SetFloat(CrackAmountId, crackAmount);
            block.SetFloat(BrokenAmountId, brokenAmount);
            block.SetFloat(MixAmountId, stateMachine.MixProgress);
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
    }
}
