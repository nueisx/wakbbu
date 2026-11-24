using UnityEngine;

namespace Interaction
{
    /// <summary>
    /// 3D 위치 하나의 필터(손가락 끝, 공 중심). One Euro 필터(느릴 때는 떨림을 강하게 누르고, 빠를 때는 지연 없이 따라감)와
    /// 튄 값 거르기(한 프레임짜리 튐 제거, 속도 제한, 겹침 의심)를 한다. 값이 튀면 직전 위치를 유지하고, 의심이 오래 계속되면 진짜 움직임으로 인정한다.
    /// 사용 순서: 프레임마다 Begin(원본 위치) → (바깥에서 다른 손가락과의 겹침 검사) → Commit(의심 여부).
    /// 장비와 무관하므로 나중에 장갑 tracking 위치에도 같이 쓸 수 있다.
    /// </summary>
    public sealed class FingertipFilter
    {
        // 속도 추정에 쓰는 차단 주파수(Hz). One Euro 필터의 표준 값이다.
        private const float DerivativeCutoff = 1f;

        // 원본이 직전 세 값의 중앙 값에서 이만큼(m) 넘게 벗어났으면 한 프레임짜리 튀는 값으로 보고 센다.
        private const float DespikeDistance = 0.01f;

        private Vector3 position;
        private Vector3 velocity;
        private Vector3 lastRaw;
        private Vector3 rawPosition;
        private Vector3 candidatePosition;
        private Vector3 candidateVelocity;
        private Vector3 previous1;
        private Vector3 previous2;
        private int historyCount;

        /// <summary>필터를 거친 위치(월드). HasValue일 때만 의미가 있다.</summary>
        public Vector3 Position => position;

        /// <summary>이번 프레임 Begin이 계산한 후보 위치. 겹침 검사에 쓴다.</summary>
        public Vector3 Candidate => candidatePosition;

        public bool HasValue { get; private set; }

        /// <summary>이번 프레임 원본이 직전 필터 위치에서 속도 제한을 넘게 움직였는가.</summary>
        public bool SpeedSuspect { get; private set; }

        /// <summary>이번 프레임 원본이 직전 필터 위치에서 움직인 거리(m).</summary>
        public float Motion { get; private set; }

        /// <summary>이번 프레임 원본이 한 프레임짜리 튀는 값이라 중앙 값으로 바꿔 쓴 프레임인가.</summary>
        public bool Despiked { get; private set; }

        /// <summary>지금까지 튀는 값을 바꿔 쓴 프레임 수(앱을 켠 뒤 누적).</summary>
        public int DespikedTotal { get; private set; }

        /// <summary>이번 프레임에 값을 거부하고 직전 위치를 유지했는가.</summary>
        public bool Rejected { get; private set; }

        /// <summary>연속으로 거부한 프레임 수.</summary>
        public int RejectRun { get; private set; }

        /// <summary>지금까지 거부한 프레임 수(앱을 켠 뒤 누적).</summary>
        public int RejectedTotal { get; private set; }

        /// <summary>추적이 끊겼을 때 부른다. 다음 값은 필터 없이 그대로 시작한다.</summary>
        public void Reset()
        {
            HasValue = false;
            SpeedSuspect = false;
            Rejected = false;
            RejectRun = 0;
            Motion = 0f;
            Despiked = false;
            historyCount = 0;
        }

        /// <summary>
        /// 이번 프레임 원본 값을 받아 후보 위치를 계산한다. minCutoff는 느린 움직임의 차단 주파수(Hz), beta는 빠를수록 차단 주파수를 올리는 정도,
        /// maxSpeed(m/s)를 넘게 움직인 프레임은 의심 값(SpeedSuspect)이다. 손가락 끝뿐 아니라 어떤 3D 위치에도 쓴다(공 중심 등).
        /// </summary>
        public void Begin(Vector3 raw, float dt, float minCutoff, float beta, float maxSpeed)
        {
            // 직전 두 값과 이번 값 중 나머지 둘과 가장 가까운 값(벡터 중앙 값)을 쓴다. 한 프레임짜리 튀는 값은 크기와 상관없이 완전히 사라진다
            // (한 프레임 늦어지는 대신). 이 뒤의 필터와 속도 제한에는 튄 값이 들어가지 않는다.
            Vector3 input = raw;
            Despiked = false;
            if (historyCount >= 2)
            {
                input = VectorMedian(previous2, previous1, raw);
                Despiked = (raw - input).magnitude > DespikeDistance;
                if (Despiked) DespikedTotal++;
            }
            previous2 = previous1;
            previous1 = raw;
            historyCount = Mathf.Min(historyCount + 1, 2);

            raw = input;
            rawPosition = raw;
            dt = Mathf.Max(dt, 1e-3f);

            if (!HasValue)
            {
                candidatePosition = raw;
                candidateVelocity = Vector3.zero;
                Motion = 0f;
                SpeedSuspect = false;
                return;
            }

            Motion = (raw - position).magnitude;
            SpeedSuspect = Motion / dt > maxSpeed;

            Vector3 rawVelocity = (raw - lastRaw) / dt;
            candidateVelocity = Vector3.Lerp(velocity, rawVelocity, Alpha(DerivativeCutoff, dt));
            float cutoff = minCutoff + beta * candidateVelocity.magnitude;
            candidatePosition = Vector3.Lerp(position, raw, Alpha(cutoff, dt));
        }

        /// <summary>suspect가 true면 값을 거부하고 직전 위치를 유지한다. 연속 거부가 rejectFrames를 넘으면 인정하고 원본 위치에서 다시 시작한다.</summary>
        public void Commit(bool suspect, int rejectFrames)
        {
            Rejected = false;

            if (!HasValue)
            {
                position = candidatePosition;
                velocity = Vector3.zero;
                lastRaw = rawPosition;
                HasValue = true;
                RejectRun = 0;
                return;
            }

            lastRaw = rawPosition;

            if (suspect && RejectRun < rejectFrames)
            {
                RejectRun++;
                RejectedTotal++;
                Rejected = true;
                return;
            }

            if (suspect)
            {
                position = rawPosition;
                velocity = Vector3.zero;
                RejectRun = 0;
                return;
            }

            RejectRun = 0;
            position = candidatePosition;
            velocity = candidateVelocity;
        }

        private static Vector3 VectorMedian(Vector3 a, Vector3 b, Vector3 c)
        {
            float da = (a - b).magnitude + (a - c).magnitude;
            float db = (b - a).magnitude + (b - c).magnitude;
            float dc = (c - a).magnitude + (c - b).magnitude;
            if (da <= db && da <= dc) return a;
            return db <= dc ? b : c;
        }

        private static float Alpha(float cutoff, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoff, 1e-3f));
            return 1f / (1f + tau / dt);
        }
    }
}
