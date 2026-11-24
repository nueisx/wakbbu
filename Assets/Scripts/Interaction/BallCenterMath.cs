using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 손가락 끝 위치에서 공 중심을 구하는 계산(순수 함수). 손가락 끝이 반지름 radius인 구 위에 있다고 보고,
    /// 세 점이면 세 점을 지나는 원(외접원)의 중심에서 평면 수직 방향으로 √(radius² − 외접원 반지름²)만큼 떨어진 점,
    /// 두 점이면 중점에서 √(radius² − (거리/2)²)만큼 떨어진 점이 중심이다. 평면 어느 쪽인지는 손바닥 쪽(palmPoint)으로 정한다.
    /// </summary>
    public static class BallCenterMath
    {
        /// <summary>
        /// 점 개수는 2 또는 3. 계산할 수 없으면(점이 같은 자리 등) false.
        /// spread는 점들이 얼마나 퍼져 있는지(세 점이면 외접원 반지름, 두 점이면 거리의 절반, m)이다.
        /// 공이 눌려 손가락이 안쪽으로 들어가면 spread가 줄고 중심 추정이 손바닥 쪽으로 밀리므로, 호출하는 쪽이 이 값으로 믿을 만한지 가린다.
        /// </summary>
        public static bool TryEstimate(Vector3 p0, Vector3 p1, Vector3 p2, int count, float radius, Vector3 palmPoint, out Vector3 center, out float spread)
        {
            center = default;
            spread = 0f;
            if (count == 3) return TryThree(p0, p1, p2, radius, palmPoint, out center, out spread);
            if (count == 2) return TryTwo(p0, p1, radius, palmPoint, out center, out spread);
            return false;
        }

        private static bool TryTwo(Vector3 a, Vector3 b, float radius, Vector3 palmPoint, out Vector3 center, out float spread)
        {
            center = default;
            spread = 0f;
            Vector3 edge = b - a;
            float distance = edge.magnitude;
            if (distance < 1e-5f) return false;
            edge /= distance;
            spread = distance * 0.5f;

            Vector3 mid = (a + b) * 0.5f;
            float h = Mathf.Sqrt(Mathf.Max(0f, radius * radius - distance * distance * 0.25f));

            // 두 점을 잇는 선에 수직이면서 손바닥 쪽인 방향
            Vector3 toPalm = palmPoint - mid;
            Vector3 side = toPalm - edge * Vector3.Dot(toPalm, edge);
            if (side.sqrMagnitude < 1e-10f)
            {
                side = Vector3.Cross(edge, Vector3.up);
                if (side.sqrMagnitude < 1e-10f) side = Vector3.Cross(edge, Vector3.right);
            }

            center = mid + side.normalized * h;
            return true;
        }

        private static bool TryThree(Vector3 p0, Vector3 p1, Vector3 p2, float radius, Vector3 palmPoint, out Vector3 center, out float spread)
        {
            Vector3 a = p0 - p2;
            Vector3 b = p1 - p2;
            Vector3 axb = Vector3.Cross(a, b);
            float denominator = 2f * axb.sqrMagnitude;

            // 세 점이 거의 한 줄이거나 외접원이 공보다 훨씬 크면 구를 정할 수 없으니 두 점 방식으로 돌린다(가장 먼 두 점).
            if (denominator > 1e-12f)
            {
                Vector3 circumcenter = p2 + Vector3.Cross(a.sqrMagnitude * b - b.sqrMagnitude * a, axb) / denominator;
                float circumRadius = (circumcenter - p2).magnitude;
                if (circumRadius <= radius * 1.2f)
                {
                    float h = Mathf.Sqrt(Mathf.Max(0f, radius * radius - circumRadius * circumRadius));
                    Vector3 normal = axb.normalized;
                    if (Vector3.Dot(normal, palmPoint - circumcenter) < 0f) normal = -normal;
                    center = circumcenter + normal * h;
                    spread = circumRadius;
                    return true;
                }
            }

            float ab = (p0 - p1).sqrMagnitude, ac = (p0 - p2).sqrMagnitude, bc = (p1 - p2).sqrMagnitude;
            if (ab >= ac && ab >= bc) return TryTwo(p0, p1, radius, palmPoint, out center, out spread);
            if (ac >= bc) return TryTwo(p0, p2, radius, palmPoint, out center, out spread);
            return TryTwo(p1, p2, radius, palmPoint, out center, out spread);
        }
    }
}
