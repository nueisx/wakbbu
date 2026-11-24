// 팀 상태 머신(WaxBall) 이벤트를 WaxHapticsPlayer로 연결한다. WEART SDK 호출은 WaxHapticsPlayer 안에만 있다.
using UnityEngine;
using WaxBall;

namespace WaxHaptics
{
    [RequireComponent(typeof(WaxHapticsPlayer))]
    public class WaxBallHapticsAdapter : MonoBehaviour
    {
        [SerializeField] private WaxBallStateMachine stateMachine;
        [Tooltip("다 섞인 뒤 점토 진동을 켤지")]
        [SerializeField] private bool mixFeel = false; // 기본은 CLAUDE.md대로 Broken 이후 출력 끔

        private WaxHapticsPlayer _player;

        private void Awake() => _player = GetComponent<WaxHapticsPlayer>();

        private void OnEnable()
        {
            stateMachine.StateChanged += OnStateChanged;
            stateMachine.Crunched += OnCrunched;
        }

        private void OnDisable()
        {
            stateMachine.StateChanged -= OnStateChanged;
            stateMachine.Crunched -= OnCrunched;
        }

        private void OnStateChanged(WaxBallState prev, WaxBallState next)
        {
            switch (next)
            {
                case WaxBallState.Broken:
                    _player.PlayBreak();            // 저항 해제 + 파삭
                    break;
                case WaxBallState.Mixed:
                    if (mixFeel) _player.SetMix(true);
                    break;
                case WaxBallState.Idle when prev >= WaxBallState.Cracked:
                    _player.StopAll();              // 리셋(재생성)
                    break;
            }
        }

        private void OnCrunched(WaxFinger finger, float strength)
        {
            _player.PlayCrunch(ToHaptic(finger), strength);
        }

        private void Update()
        {
            // 깨지기 전(Idle/Pressed/Cracked)에만 쥐는 만큼 껍질 저항
            if (stateMachine.State <= WaxBallState.Cracked)
                _player.SetPressByGrip(stateMachine.Grip);
        }

        private static HapticFinger ToHaptic(WaxFinger f) => f switch
        {
            WaxFinger.Thumb => HapticFinger.Thumb,
            WaxFinger.Index => HapticFinger.Index,
            _ => HapticFinger.Middle,
        };
    }
}
