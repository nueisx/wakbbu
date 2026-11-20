using UnityEngine;

namespace WaxBall
{
    /// <summary>
    /// 손가락이 누른 자리의 파손 범위 모델. 렌더링은 하지 않고 위치와 범위만 계산한다(시각·소리 모듈이 읽는다).
    /// - 상태가 Cracked 이상이 되면, 일정 세기 이상으로 누르는 손가락 아래에 "파손 흔적(stamp)"이 생긴다.
    /// - 세게 누르거나 주무를수록 흔적의 범위(반경)가 커진다. 반경은 줄지 않는다.
    /// - 손가락을 떼도 흔적은 남는다(실물은 복원되지 않는다). 리셋(Idle)에서만 모두 지운다.
    /// - MixProgress가 커질수록 모든 흔적이 공 전체로 퍼진다.
    /// - 첫 균열은 가장 세게 누른 손가락의 흔적에, 두 번째 균열은 두 번째로 세게 누른 손가락의 흔적에 붙는다.
    /// 접촉점 입력(WaxBallContacts)이 한 번도 들어오지 않은 씬(Slider만 쓰는 경우)은 LocalMode가 false이고,
    /// 이때 시각 효과는 공 전체가 깨지는 예전 방식으로 동작한다.
    /// </summary>
    public class WaxBallDamage : MonoBehaviour
    {
        public const int MaxStamps = 8;

        [SerializeField] private WaxBallStateMachine stateMachine;
        [SerializeField] private WaxBallContacts contacts;

        [Header("파손 범위")]
        [Tooltip("이 세기(0~1) 이상으로 누르는 손가락만 파손을 만든다.")]
        [SerializeField, Range(0f, 1f)] private float damagePressureThreshold = 0.3f;
        [Tooltip("파손 정도 0일 때의 반경(도). Cracked 직후의 작은 범위.")]
        [SerializeField, Range(5f, 60f)] private float minRadiusDegrees = 20f;
        [Tooltip("파손 정도 1일 때의 반경(도). 한 손가락만으로는 공 전체로 퍼지지 않는다.")]
        [SerializeField, Range(30f, 120f)] private float maxRadiusDegrees = 85f;
        [Tooltip("세기 1로 누를 때 초당 늘어나는 파손 정도.")]
        [SerializeField, Min(0f)] private float pressureDamageRate = 0.5f;
        [Tooltip("손가락 세기가 1만큼 변할 때(주무를 때) 늘어나는 파손 정도.")]
        [SerializeField, Min(0f)] private float kneadDamageGain = 1f;

        [Header("접촉점 묶기")]
        [Tooltip("손가락이 흔적에서 이 각도(도) 안에 있으면 같은 흔적을 키우고, 벗어나면 새 흔적을 만든다.")]
        [SerializeField, Range(5f, 60f)] private float rebindAngleDegrees = 25f;

        private struct Stamp
        {
            public bool used;
            public Vector3 direction;   // 공 로컬 방향(길이 1)
            public float damage;        // 0~1
        }

        // 균열 우선순위: 세기가 같으면 검지, 중지, 엄지 순으로 먼저 붙는다.
        private static readonly WaxFinger[] FingerPriority = { WaxFinger.Index, WaxFinger.Middle, WaxFinger.Thumb };

        private readonly Stamp[] stamps = new Stamp[MaxStamps];
        private readonly int[] fingerStamp = new int[WaxBallContacts.MaxContacts];
        private readonly float[] lastPressure = new float[WaxBallContacts.MaxContacts];
        private int crack1Stamp = -1;
        private int crack2Stamp = -1;

        /// <summary>접촉점 입력이 연결된 씬인가. false면 공 전체가 깨지는 방식을 쓴다.</summary>
        public bool LocalMode => contacts != null && contacts.HasReceivedContact;

        public int StampCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < MaxStamps; i++) if (stamps[i].used) count++;
                return count;
            }
        }

        /// <summary>첫 번째 균열이 붙은 손가락. 아직 없으면 null.</summary>
        public WaxFinger? Crack1Finger { get; private set; }

        /// <summary>두 번째 균열이 붙은 손가락. 아직 없으면 null.</summary>
        public WaxFinger? Crack2Finger { get; private set; }

        private void Awake()
        {
            if (stateMachine == null) stateMachine = GetComponent<WaxBallStateMachine>();
            if (contacts == null) contacts = GetComponent<WaxBallContacts>();
            ClearAll();
        }

        private void OnEnable()
        {
            stateMachine.StateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (stateMachine != null) stateMachine.StateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(WaxBallState prev, WaxBallState next)
        {
            if (next == WaxBallState.Idle) ClearAll();
        }

        private void Update()
        {
            if (contacts == null) return;

            // 껍질이 멀쩡한 동안(Cracked 전)에는 파손이 없다.
            bool damaging = stateMachine.State >= WaxBallState.Cracked;
            float dt = Time.deltaTime;

            for (int i = 0; i < WaxBallContacts.MaxContacts; i++)
            {
                bool touching = contacts.TryGetContact((WaxFinger)i, out FingerContact contact);
                float pressure = touching ? contact.pressure : 0f;

                if (!damaging || !touching || pressure < damagePressureThreshold)
                {
                    fingerStamp[i] = -1;
                    lastPressure[i] = pressure;
                    continue;
                }

                int s = fingerStamp[i];
                if (s >= 0 && Vector3.Angle(stamps[s].direction, contact.localDirection) > rebindAngleDegrees) s = -1;
                if (s < 0) s = FindOrCreateStamp(contact.localDirection);
                fingerStamp[i] = s;

                float press = Mathf.InverseLerp(damagePressureThreshold, 1f, pressure);
                float knead = Mathf.Abs(pressure - lastPressure[i]) * kneadDamageGain;
                stamps[s].damage = Mathf.Min(1f, stamps[s].damage + press * pressureDamageRate * dt + knead);
                lastPressure[i] = pressure;
            }

            if (damaging) AssignCracks();
        }

        /// <summary>균열 슬롯(0 = 첫 번째, 1 = 두 번째)이 붙은 위치(공 로컬 방향). 아직 붙지 않았으면 false.</summary>
        public bool TryGetCrackAnchor(int slot, out Vector3 direction)
        {
            int s = slot == 0 ? crack1Stamp : crack2Stamp;
            direction = Vector3.forward;
            if (s < 0 || !stamps[s].used) return false;
            direction = stamps[s].direction;
            return true;
        }

        /// <summary>
        /// 셰이더에 넘길 흔적 배열을 채운다. 각 원소는 (방향 xyz, 반경의 코사인)이고, 빈 칸은 코사인이 2다.
        /// mix(MixProgress)가 커질수록 반경이 공 전체(180도)로 퍼진다.
        /// </summary>
        public void FillShaderStamps(Vector4[] destination, float mix)
        {
            float spread = Mathf.SmoothStep(0f, 1f, mix);
            for (int i = 0; i < MaxStamps && i < destination.Length; i++)
            {
                if (!stamps[i].used)
                {
                    destination[i] = new Vector4(0f, 0f, 1f, 2f);
                    continue;
                }

                float t = Mathf.SmoothStep(0f, 1f, stamps[i].damage);
                float radius = Mathf.Lerp(minRadiusDegrees, maxRadiusDegrees, t) * Mathf.Deg2Rad;
                radius = Mathf.Lerp(radius, Mathf.PI, spread);

                Vector3 d = stamps[i].direction;
                destination[i] = new Vector4(d.x, d.y, d.z, Mathf.Cos(radius));
            }
        }

        private int FindOrCreateStamp(Vector3 direction)
        {
            int nearest = -1;
            float nearestAngle = float.MaxValue;
            int free = -1;

            for (int i = 0; i < MaxStamps; i++)
            {
                if (!stamps[i].used)
                {
                    if (free < 0) free = i;
                    continue;
                }

                float angle = Vector3.Angle(stamps[i].direction, direction);
                if (angle < nearestAngle)
                {
                    nearestAngle = angle;
                    nearest = i;
                }
            }

            // 가까운 흔적이 있으면 그것을 키운다. 없으면 새로 만들고, 자리가 없으면 가장 가까운 흔적에 합친다.
            if (nearest >= 0 && nearestAngle <= rebindAngleDegrees) return nearest;
            if (free < 0) return nearest;

            stamps[free] = new Stamp { used = true, direction = direction.normalized, damage = 0f };
            return free;
        }

        // 첫 균열은 가장 세게 누른 손가락, 두 번째 균열은 그 흔적이 아닌 곳에서 두 번째로 세게 누른 손가락에 붙인다.
        // 한 번 붙은 균열은 옮기지 않는다.
        private void AssignCracks()
        {
            if (crack1Stamp < 0)
            {
                int finger = StrongestFinger(-1);
                if (finger >= 0)
                {
                    crack1Stamp = fingerStamp[finger];
                    Crack1Finger = (WaxFinger)finger;
                }
                else
                {
                    crack1Stamp = MostDamagedStamp(-1);
                }
            }

            if (crack2Stamp < 0 && crack1Stamp >= 0)
            {
                int finger = StrongestFinger(crack1Stamp);
                if (finger >= 0)
                {
                    crack2Stamp = fingerStamp[finger];
                    Crack2Finger = (WaxFinger)finger;
                }
            }
        }

        // 지금 흔적에 붙어 누르는 손가락 중 가장 센 손가락(excludeStamp에 붙은 손가락은 제외). 없으면 -1.
        private int StrongestFinger(int excludeStamp)
        {
            int best = -1;
            float bestPressure = -1f;

            foreach (WaxFinger finger in FingerPriority)
            {
                int i = (int)finger;
                int s = fingerStamp[i];
                if (s < 0 || s == excludeStamp) continue;
                if (lastPressure[i] > bestPressure)
                {
                    bestPressure = lastPressure[i];
                    best = i;
                }
            }
            return best;
        }

        private int MostDamagedStamp(int excludeStamp)
        {
            int best = -1;
            float bestDamage = -1f;
            for (int i = 0; i < MaxStamps; i++)
            {
                if (!stamps[i].used || i == excludeStamp) continue;
                if (stamps[i].damage > bestDamage)
                {
                    bestDamage = stamps[i].damage;
                    best = i;
                }
            }
            return best;
        }

        private void ClearAll()
        {
            for (int i = 0; i < MaxStamps; i++) stamps[i] = default;
            for (int i = 0; i < fingerStamp.Length; i++)
            {
                fingerStamp[i] = -1;
                lastPressure[i] = 0f;
            }
            crack1Stamp = -1;
            crack2Stamp = -1;
            Crack1Finger = null;
            Crack2Finger = null;
        }
    }
}
