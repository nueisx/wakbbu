using UnityEngine;

namespace WaxBall
{
    /// <summary>
    /// 임시 시각 효과: 상태별 색 + grip에 따른 찌그러짐. 이벤트를 구독만 한다.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class WaxBallTempVisual : MonoBehaviour
    {
        [SerializeField] private WaxBallStateMachine stateMachine;
        [SerializeField, Range(0f, 0.5f)] private float maxSquash = 0.25f;

        [Header("상태별 색")]
        [SerializeField] private Color idleColor = Color.white;
        [SerializeField] private Color pressedColor = new Color(1f, 0.92f, 0.6f);
        [SerializeField] private Color crackedColor = new Color(1f, 0.6f, 0.2f);
        [SerializeField] private Color brokenColor = new Color(0.9f, 0.25f, 0.25f);
        [SerializeField] private Color mixedColor = new Color(0.6f, 0.35f, 0.8f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Renderer rend;
        private MaterialPropertyBlock block;
        private Vector3 baseScale;

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
            ApplyColor(stateMachine.State);
        }

        private void OnDisable()
        {
            if (stateMachine != null) stateMachine.StateChanged -= HandleStateChanged;
        }

        private void Update()
        {
            float squash = stateMachine.Grip * maxSquash;
            transform.localScale = new Vector3(
                baseScale.x * (1f + squash * 0.5f),
                baseScale.y * (1f - squash),
                baseScale.z * (1f + squash * 0.5f));
        }

        private void HandleStateChanged(WaxBallState prev, WaxBallState next) => ApplyColor(next);

        private void ApplyColor(WaxBallState state)
        {
            Color c;
            switch (state)
            {
                case WaxBallState.Pressed: c = pressedColor; break;
                case WaxBallState.Cracked: c = crackedColor; break;
                case WaxBallState.Broken: c = brokenColor; break;
                case WaxBallState.Mixed: c = mixedColor; break;
                default: c = idleColor; break;
            }
            rend.GetPropertyBlock(block);
            block.SetColor(BaseColorId, c);
            rend.SetPropertyBlock(block);
        }
    }
}
