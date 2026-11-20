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

        private int crunchCount;
        private float lastCrunchStrength;

        private void OnEnable() => stateMachine.Crunched += HandleCrunched;
        private void OnDisable() => stateMachine.Crunched -= HandleCrunched;

        private void HandleCrunched(float strength)
        {
            crunchCount++;
            lastCrunchStrength = strength;
        }

        private void Update()
        {
            label.text = $"State: {stateMachine.State}\nGrip: {stateMachine.Grip:0.00}\nMix: {stateMachine.MixProgress:P0}\nCrunch: {crunchCount} ({lastCrunchStrength:0.00})";
        }
    }
}
