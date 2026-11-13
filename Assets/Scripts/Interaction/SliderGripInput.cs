using UnityEngine;
using UnityEngine.UI;
using WaxBall;

namespace Interaction
{
    /// <summary>
    /// 장비 없는 에디터 테스트용 입력 어댑터. Slider 값을 grip으로 넘기고, R 키로 리셋한다.
    /// </summary>
    public class SliderGripInput : MonoBehaviour
    {
        [SerializeField] private WaxBallStateMachine stateMachine;
        [SerializeField] private Slider gripSlider;
        [SerializeField] private KeyCode resetKey = KeyCode.R;

        private void OnEnable()
        {
            gripSlider.onValueChanged.AddListener(stateMachine.SetGrip);
        }

        private void OnDisable()
        {
            gripSlider.onValueChanged.RemoveListener(stateMachine.SetGrip);
        }

        private void Update()
        {
            if (Input.GetKeyDown(resetKey)) ResetAll();
        }

        /// <summary>재생성 버튼과 R 키 공용. 슬라이더도 0으로 되돌린다.</summary>
        public void ResetAll()
        {
            gripSlider.SetValueWithoutNotify(0f);
            stateMachine.ResetBall();
        }
    }
}
