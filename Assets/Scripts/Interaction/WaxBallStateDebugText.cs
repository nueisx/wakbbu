using TMPro;
using UnityEngine;
using WaxBall;

namespace Interaction
{
    /// <summary>현재 상태, grip, 섞임 진행도를 화면에 표시하는 디버그용 텍스트.</summary>
    public class WaxBallStateDebugText : MonoBehaviour
    {
        [SerializeField] private WaxBallStateMachine stateMachine;
        [SerializeField] private TMP_Text label;
        [Tooltip("선택 사항. 연결하면 파손 흔적 수와 균열이 붙은 손가락을 표시한다.")]
        [SerializeField] private WaxBallDamage damage;

        private int crunchCount;
        private string lastCrunchFinger = "-";
        private float lastCrunchStrength;

        private void OnEnable() => stateMachine.Crunched += HandleCrunched;
        private void OnDisable() => stateMachine.Crunched -= HandleCrunched;

        private void HandleCrunched(WaxFinger finger, float strength)
        {
            crunchCount++;
            lastCrunchFinger = finger.ToString();
            lastCrunchStrength = strength;
        }

        private void Update()
        {
            string text = $"State: {stateMachine.State}\nGrip: {stateMachine.Grip:0.00}\nMix: {stateMachine.MixProgress:P0}\nCrunch: {crunchCount} ({lastCrunchFinger}, {lastCrunchStrength:0.00})";

            if (damage != null)
            {
                string crack1 = damage.Crack1Finger.HasValue ? damage.Crack1Finger.Value.ToString() : "-";
                string crack2 = damage.Crack2Finger.HasValue ? damage.Crack2Finger.Value.ToString() : "-";
                text += $"\nDamage: {(damage.LocalMode ? "Local" : "Global")}, {damage.StampCount} stamps\nCracks: {crack1} / {crack2}";
            }

            label.text = text;
        }
    }
}
