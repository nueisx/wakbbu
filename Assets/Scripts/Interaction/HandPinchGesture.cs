using UnityEngine;
using UnityEngine.Events;
using WaxBall;

namespace Interaction
{
    /// <summary>
    /// 왼손 엄지-검지 핀치를 길게 누르면(기본 1초) 리셋하는 제스처. 누르는 동안 진행 원(PinchProgressRing)이 채워진다.
    /// 컨트롤러 없이 손만 쓸 때 A 버튼 대신 쓴다. 에디터에서는 키보드(기본 P)를 누르고 있는 것으로 대신한다.
    /// 왼손 검지 핀치는 Meta 시스템 제스처(손바닥을 얼굴 쪽으로 향하고 핀치 = 메뉴)와 겹치므로, 시스템 제스처가 진행 중이면 무시한다.
    /// 손바닥이 얼굴을 향하지 않게 핀치하는 것이 안전하다.
    /// 리셋이 발동하면 onReset이 불린다(에디터 Slider 씬은 슬라이더도 0으로 돌리는 SliderGripInput.ResetAll, Quest 씬은 ResetBall을 연결).
    /// </summary>
    public class HandPinchGesture : MonoBehaviour
    {
        [SerializeField] private WaxBallTuning tuning;
        [Tooltip("왼손. 비우면(에디터) 키보드만 쓴다.")]
        [SerializeField] private OVRHand leftHand;
        [Tooltip("에디터에서 핀치 대신 누르고 있을 키.")]
        [SerializeField] private KeyCode resetKey = KeyCode.P;
        [SerializeField] private PinchProgressRing resetRing;
        [SerializeField] private UnityEvent onReset = new UnityEvent();

        private readonly PinchHold resetHold = new PinchHold();

        /// <summary>리셋이 발동할 때 불리는 이벤트(씬 빌더가 연결한다).</summary>
        public UnityEvent OnReset => onReset;

        private WaxBallTuning Tuning => tuning != null ? tuning : WaxBallTuning.Default;

        /// <summary>리셋 핀치 진행(0~1). 디버그 표시용.</summary>
        public float ResetProgress => resetHold.Progress;

        private void Update()
        {
            bool pinching = Input.GetKey(resetKey) || LeftIndexPinch();
            resetHold.Update(pinching, Time.deltaTime, Tuning.pinchHoldSeconds);

            if (resetRing != null) resetRing.SetProgress(resetHold.Progress);
            if (resetHold.Triggered) onReset.Invoke();
        }

        private bool LeftIndexPinch()
        {
            if (leftHand == null || !leftHand.IsTracked || leftHand.IsSystemGestureInProgress) return false;
            return leftHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        }
    }
}
