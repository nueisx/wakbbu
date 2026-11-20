using UnityEngine;
using WaxBall;

namespace Interaction
{
    /// <summary>
    /// Quest 컨트롤러 입력 어댑터. 오른손 컨트롤러 트리거(0~1)를 grip으로 넣고, A 버튼으로 리셋한다.
    /// 에디터나 헤드셋이 없으면 OVRInput이 0을 돌려줘서 아무 일도 일어나지 않는다.
    /// </summary>
    public class QuestControllerInput : MonoBehaviour
    {
        [SerializeField] private WaxBallStateMachine stateMachine;
        [SerializeField] private OVRInput.Controller controller = OVRInput.Controller.RTouch;
        [Tooltip("오른손 컨트롤러의 A 버튼이 OVRInput.Button.One이다.")]
        [SerializeField] private OVRInput.Button resetButton = OVRInput.Button.One;
        [Tooltip("리셋한 뒤 트리거를 한 번 놓을 때까지 grip을 0으로 둔다. 트리거를 쥔 채 리셋해도 새 공이 바로 깨지지 않는다.")]
        [SerializeField] private bool waitForReleaseAfterReset = true;
        [SerializeField, Range(0f, 0.5f)] private float releaseThreshold = 0.05f;

        private bool waitingForRelease;

        private void Update()
        {
            float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller);

            if (OVRInput.GetDown(resetButton, controller))
            {
                stateMachine.ResetBall();
                waitingForRelease = waitForReleaseAfterReset;
            }

            if (waitingForRelease && trigger <= releaseThreshold) waitingForRelease = false;

            stateMachine.SetGrip(waitingForRelease ? 0f : trigger);
        }
    }
}
