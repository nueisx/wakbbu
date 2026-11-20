using UnityEngine;

namespace WaxBall
{
    public enum GripAggregation
    {
        /// <summary>손가락 중 가장 센 값. 한 손가락만 세게 눌러도 상태가 진행된다.</summary>
        Max,
        /// <summary>접촉 중인 손가락의 평균 값. 여러 손가락이 같이 눌러야 상태가 진행된다.</summary>
        Average
    }

    /// <summary>한 손가락의 접촉점. localDirection은 공 중심에서 본 방향(공 로컬, 길이 1)이다.</summary>
    public struct FingerContact
    {
        public bool active;
        public Vector3 localDirection;
        public float pressure;
    }

    /// <summary>
    /// 손가락 접촉점 입력(엄지, 검지, 중지 3개). 장비와 무관하게 "공 표면 위 위치 + 누르는 세기(0~1)"만 받는다.
    /// Quest 핸드트래킹, WEART, 에디터의 가짜 손가락 어댑터가 모두 같은 함수를 부른다.
    /// 장비 SDK에 의존하지 않는다. 공 오브젝트(상태 머신이 있는 오브젝트)에 붙인다.
    /// </summary>
    public class WaxBallContacts : MonoBehaviour
    {
        public const int MaxContacts = WaxBallStateMachine.FingerCount;

        [SerializeField] private WaxBallStateMachine stateMachine;

        [Header("전체 grip 합치기")]
        [Tooltip("켜면 접촉점 세기를 합쳐서 매 프레임 SetGrip에 넣는다. 실제 손(핸드트래킹, 장갑) 입력에서 켠다. " +
                 "Slider로 grip을 직접 넣는 에디터 테스트에서는 끈다.")]
        [SerializeField] private bool driveGrip;
        [SerializeField] private GripAggregation aggregation = GripAggregation.Max;

        private readonly FingerContact[] contacts = new FingerContact[MaxContacts];

        public WaxBallStateMachine StateMachine => stateMachine;

        /// <summary>
        /// 접촉점 입력이 한 번이라도 들어왔는가(리셋해도 유지). false면 접촉점을 쓰지 않는 씬(Slider만 사용)이다.
        /// 이때 시각 효과는 공 전체가 깨지는 방식으로 동작한다.
        /// </summary>
        public bool HasReceivedContact { get; private set; }
        public GripAggregation Aggregation { get => aggregation; set => aggregation = value; }
        public bool DriveGrip { get => driveGrip; set => driveGrip = value; }

        /// <summary>접촉 중인 손가락 수.</summary>
        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < MaxContacts; i++) if (contacts[i].active) count++;
                return count;
            }
        }

        /// <summary>접촉 중인 손가락 세기를 설정(aggregation)에 따라 합친 값(0~1). 접촉이 없으면 0.</summary>
        public float AggregatedPressure
        {
            get
            {
                float max = 0f, sum = 0f;
                int count = 0;
                for (int i = 0; i < MaxContacts; i++)
                {
                    if (!contacts[i].active) continue;
                    max = Mathf.Max(max, contacts[i].pressure);
                    sum += contacts[i].pressure;
                    count++;
                }
                if (count == 0) return 0f;
                return aggregation == GripAggregation.Max ? max : sum / count;
            }
        }

        /// <summary>월드 좌표의 접촉 위치. 표면 위가 아니어도 되고, 공 중심에서 본 방향으로 표면에 투영된다.</summary>
        public void SetContact(WaxFinger finger, Vector3 worldPosition, float pressure)
        {
            SetContactLocal(finger, transform.InverseTransformPoint(worldPosition), pressure);
        }

        /// <summary>공 로컬 좌표의 접촉 위치.</summary>
        public void SetContactLocal(WaxFinger finger, Vector3 localPosition, float pressure)
        {
            int i = (int)finger;
            if (i < 0 || i >= MaxContacts) return;
            if (localPosition.sqrMagnitude < 1e-8f) return;

            pressure = Mathf.Clamp01(pressure);
            HasReceivedContact = true;
            contacts[i] = new FingerContact
            {
                active = true,
                localDirection = localPosition.normalized,
                pressure = pressure
            };

            if (stateMachine != null) stateMachine.SetFingerPressure(finger, pressure);
        }

        /// <summary>손가락이 떨어졌을 때(추적을 잃었을 때 포함) 부른다.</summary>
        public void ClearContact(WaxFinger finger)
        {
            int i = (int)finger;
            if (i < 0 || i >= MaxContacts || !contacts[i].active) return;
            contacts[i] = default;
            if (stateMachine != null) stateMachine.ReleaseFinger(finger);
        }

        public void ClearAllContacts()
        {
            for (int i = 0; i < MaxContacts; i++) ClearContact((WaxFinger)i);
        }

        public bool TryGetContact(WaxFinger finger, out FingerContact contact)
        {
            contact = default;
            int i = (int)finger;
            if (i < 0 || i >= MaxContacts) return false;
            contact = contacts[i];
            return contact.active;
        }

        private void Update()
        {
            if (driveGrip && stateMachine != null) stateMachine.SetGrip(AggregatedPressure);
        }
    }
}
