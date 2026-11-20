using UnityEngine;
using WaxBall;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Interaction
{
    /// <summary>
    /// 에디터 테스트용 가짜 손가락 접촉점 5개. 공 주변의 FakeFinger 오브젝트를 Scene 뷰에서 드래그해 위치를 옮긴다.
    /// 세기는 상태 머신의 grip(Slider 값)을 그대로 쓴다. 실제 입력은 이 컴포넌트 대신 같은 API(WaxBallContacts)를 부르면 된다.
    /// </summary>
    public class FakeFingerContacts : MonoBehaviour
    {
        [SerializeField] private WaxBallContacts contacts;
        [SerializeField] private WaxBallStateMachine stateMachine;
        [SerializeField] private Transform[] fingers = new Transform[WaxBallContacts.MaxContacts];
        [Tooltip("끄면 그 손가락은 접촉하지 않는 것으로 친다.")]
        [SerializeField] private bool[] fingerEnabled = { true, true, true, true, true };
        [Tooltip("손가락별 세기 배율. 기본은 모두 1(Slider 값 그대로).")]
        [SerializeField] private float[] pressureScale = { 1f, 1f, 1f, 1f, 1f };
        [SerializeField] private bool showMarkersInGame;

        private void Awake()
        {
            if (showMarkersInGame) return;
            foreach (var finger in fingers)
            {
                if (finger == null) continue;
                foreach (var markerRenderer in finger.GetComponentsInChildren<Renderer>())
                    markerRenderer.enabled = false;
            }
        }

        private void Update()
        {
            if (contacts == null || stateMachine == null) return;

            for (int i = 0; i < fingers.Length && i < WaxBallContacts.MaxContacts; i++)
            {
                bool enabledFinger = fingers[i] != null && i < fingerEnabled.Length && fingerEnabled[i];
                if (!enabledFinger)
                {
                    contacts.ClearContact(i);
                    continue;
                }

                float scale = i < pressureScale.Length ? pressureScale[i] : 1f;
                contacts.SetContact(i, fingers[i].position, stateMachine.Grip * scale);
            }
        }

        private void OnDrawGizmos()
        {
            if (contacts == null) return;

            Transform ball = contacts.transform;
            float radius = ball.lossyScale.x * 0.5f;

            for (int i = 0; i < fingers.Length; i++)
            {
                if (fingers[i] == null) continue;
                bool on = i < fingerEnabled.Length && fingerEnabled[i];

                Vector3 dir = (fingers[i].position - ball.position).normalized;
                Vector3 surface = ball.position + dir * radius;
                float pressure = Application.isPlaying && stateMachine != null ? stateMachine.Grip : 0f;

                Gizmos.color = on ? Color.Lerp(Color.white, Color.red, pressure) : Color.gray;
                Gizmos.DrawLine(fingers[i].position, surface);
                Gizmos.DrawWireSphere(fingers[i].position, radius * 0.18f);
                Gizmos.DrawSphere(surface, radius * 0.08f);
#if UNITY_EDITOR
                Handles.Label(fingers[i].position + Vector3.up * radius * 0.25f, i.ToString());
#endif
            }
        }
    }
}
