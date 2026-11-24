using UnityEngine;
using WaxBall;

namespace Interaction
{
    /// <summary>한 손가락 끝의 추적 결과. worldPosition은 월드 좌표이고, tracked가 false면 값이 없다.</summary>
    public struct FingertipSample
    {
        public bool tracked;
        public bool highConfidence;
        public Vector3 worldPosition;
    }

    /// <summary>
    /// 손가락 끝 위치를 주는 입력. Quest 핸드트래킹(QuestHandFingertips)과 나중에 쓸 햅틱 장갑 tracking이 같은 모양으로 구현한다.
    /// 이 값을 WaxBallContacts.SetContact로 옮기는 일은 입력 변환 어댑터가 한다(장비마다 접촉 판정이 달라서 여기에 넣지 않는다).
    /// </summary>
    public interface IFingertipSource
    {
        bool TryGetFingertip(WaxFinger finger, out FingertipSample sample);

        /// <summary>
        /// 손 기준 자세(손목의 월드 위치와 회전). 공 중심은 손바닥 쪽에 있으므로 어느 쪽이 손바닥인지 알려 주는 기준이고,
        /// 중심을 손 좌표계에서 평균하는 데도 쓴다. 추적이 안 되면 false.
        /// </summary>
        bool TryGetHandPose(out Vector3 position, out Quaternion rotation);
    }
}
