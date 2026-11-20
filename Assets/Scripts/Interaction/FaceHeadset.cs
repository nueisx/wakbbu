using UnityEngine;

namespace Interaction
{
    /// <summary>월드 공간 UI(디버그 텍스트)가 항상 메인 카메라(헤드셋) 쪽을 보게 한다.</summary>
    public class FaceHeadset : MonoBehaviour
    {
        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            // 월드 공간 캔버스는 앞면(+z)이 보는 쪽 반대로 향하므로 카메라에서 멀어지는 방향을 바라보게 한다.
            Vector3 away = transform.position - cam.transform.position;
            if (away.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
