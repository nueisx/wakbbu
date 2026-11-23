using UnityEngine;

namespace WaxBall
{
    /// <summary>
    /// 손가락이 누른 자리의 파손 범위 모델. 렌더링은 하지 않고 판별 상태만 계산한다(시각·소리 모듈이 읽는다).
    /// 파손은 큰 판(Voronoi 셀, WaxBallCellMap) 단위다. 판마다 깨짐(0~1)과 주무른 양을 따로 들고, 셰이더는 자기 판의 칸만 읽는다.
    /// - 상태가 Cracked 이상이 되면, 일정 세기 이상으로 누르는 손가락 아래에 "자리(spot)"가 생긴다. 자리는 방향 하나와 파손 정도(도달 반경)를 가진다.
    /// - 세게 누르거나 주무를수록 자리의 도달 반경이 커진다. 반경은 줄지 않는다. 같은 자리를 주무르면 반경이 아주 조금씩 넓어진다(spreadDegreesPerKnead).
    /// - Broken 이후 도달 반경 안의 판이 깨진다. 판마다 문턱(cellHash)이 달라서 영향이 큰 판부터 하나씩 깨지고, 한 번 깨진 판은 되돌아가지 않는다(리셋에서만 지운다).
    ///   자리는 반경을 키울 때만 필요해서 16개까지 기억하고 넘치면 가장 오래 안 쓴 자리부터 잊는다. 판은 이미 깨져 있어 화면과 면적은 그대로다.
    /// - 판마다 주무른 양(knead)을 쌓는다. 깨진 판 위에 손가락이 닿아 있을 때 그 손가락의 세기 변화량이 쌓이고(한 판에는 가장 많이 움직인 손가락 하나만),
    ///   kneadAmountPerStamp만큼 쌓이면 그 판은 완전히 섞인 것(섞임 1)이다. 새로 깬 판은 항상 섞임 0에서 시작한다.
    /// - 깨진 판 면적 비율(DamagedRatio)과 깨진 판의 평균 섞임(AverageMix, 면적 가중)을 상태 머신에 넘기고, 두 값이 각각 기준을 넘으면 Mixed가 된다.
    /// - 첫 균열은 가장 세게 누른 손가락의 자리에, 두 번째 균열은 두 번째로 세게 누른 손가락의 자리에 붙는다.
    /// 접촉점 입력(WaxBallContacts)이 한 번도 들어오지 않은 씬(Slider만 쓰는 경우)은 LocalMode가 false이고,
    /// 이때 시각 효과는 공 전체가 깨지는 예전 방식으로 동작한다.
    /// </summary>
    public class WaxBallDamage : MonoBehaviour
    {
        [Tooltip("튜닝 값(임계값, 범위, 속도 등)을 모아 둔 설정 파일. 비워 두면 코드 기본값(WaxBallTuning의 필드 초기값)을 쓴다. Assets/Settings/WaxBallTuning.asset 하나를 모든 씬이 같이 쓴다.")]
        [SerializeField] private WaxBallTuning tuning;

        private WaxBallTuning Tuning => tuning != null ? tuning : WaxBallTuning.Default;
        private float damagePressureThreshold => Tuning.damagePressureThreshold;
        private float minRadiusDegrees => Tuning.minRadiusDegrees;
        private float maxRadiusDegrees => Tuning.maxRadiusDegrees;
        private float pressureDamageRate => Tuning.pressureDamageRate;
        private float kneadDamageGain => Tuning.kneadDamageGain;
        private float kneadAmountPerStamp => Tuning.kneadAmountPerStamp;
        private float spreadDegreesPerKnead => Tuning.spreadDegreesPerKnead;
        private float maxSpreadDegrees => Tuning.maxSpreadDegrees;
        private float rebindAngleDegrees => Tuning.rebindAngleDegrees;
        private float brokenRampDuration => Tuning.brokenRampDuration;

        public const int MaxSpots = 16;

        [SerializeField] private WaxBallStateMachine stateMachine;
        [SerializeField] private WaxBallContacts contacts;

        // 손가락이 누른 자리. 도달 반경(파손 정도 + 번지기)을 키우는 데만 쓰고, 깨진 결과는 판(cell)에 남는다.
        private struct Spot
        {
            public bool used;
            public Vector3 direction;   // 공 로컬 방향(길이 1)
            public float damage;        // 0~1
            public float spread;        // 번지기로 더해진 반경(도)
            public float lastUsed;      // 마지막으로 손가락이 붙어 있던 시각
        }

        // 균열 우선순위: 세기가 같으면 검지, 중지, 엄지 순으로 먼저 붙는다.
        private static readonly WaxFinger[] FingerPriority = { WaxFinger.Index, WaxFinger.Middle, WaxFinger.Thumb };

        private readonly Spot[] spots = new Spot[MaxSpots];
        private readonly float[] spotCos = new float[MaxSpots];
        private readonly int[] fingerSpot = new int[WaxBallContacts.MaxContacts];
        private readonly float[] lastPressure = new float[WaxBallContacts.MaxContacts];
        private readonly float[] fingerKneadStep = new float[WaxBallContacts.MaxContacts];

        // 균열이 붙은 자리의 방향은 자리를 잊어도 남는다.
        private bool crack1Assigned;
        private bool crack2Assigned;
        private Vector3 crack1Anchor;
        private Vector3 crack2Anchor;
        private int crack1Spot = -1;
        private int crack2Spot = -1;

        // 판별 상태(판 번호는 WaxBallCellMap의 slot). 판 표는 WaxBallShellVisual이 들고 있다.
        private WaxBallShellVisual visual;
        private WaxBallCellMap cellMap;
        private int cellMapVersion = -1;
        private readonly float[] cellBroken = new float[WaxBallCellMap.SlotCount];
        private readonly float[] cellKnead = new float[WaxBallCellMap.SlotCount];
        private float brokenRamp;

        /// <summary>공 표면 중 깨진 판이 차지하는 면적 비율(0~1).</summary>
        public float DamagedRatio { get; private set; }

        /// <summary>깨진 판의 평균 섞임(0~1, 면적 가중). 깨진 판이 없으면 0.</summary>
        public float AverageMix { get; private set; }

        /// <summary>깨진 판 개수와 표면에 있는 큰 판 개수(디버그 텍스트용).</summary>
        public int BrokenCellCount { get; private set; }
        public int CellCount => cellMap != null ? cellMap.UsedCount : 0;

        /// <summary>접촉점 입력이 연결된 씬인가. false면 공 전체가 깨지는 방식을 쓴다.</summary>
        public bool LocalMode => contacts != null && contacts.HasReceivedContact;

        public int SpotCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < MaxSpots; i++) if (spots[i].used) count++;
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
            visual = GetComponent<WaxBallShellVisual>();
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
            SyncCellMap();

            // 껍질이 멀쩡한 동안(Cracked 전)에는 파손이 없다.
            bool damaging = stateMachine.State >= WaxBallState.Cracked;
            bool broken = stateMachine.State >= WaxBallState.Broken;
            float dt = Time.deltaTime;
            System.Array.Clear(fingerKneadStep, 0, fingerKneadStep.Length);

            for (int i = 0; i < WaxBallContacts.MaxContacts; i++)
            {
                bool touching = contacts.TryGetContact((WaxFinger)i, out FingerContact contact);
                float pressure = touching ? contact.pressure : 0f;

                if (!damaging || !touching || pressure < damagePressureThreshold)
                {
                    fingerSpot[i] = -1;
                    lastPressure[i] = pressure;
                    continue;
                }

                int s = fingerSpot[i];
                if (s >= 0 && Vector3.Angle(spots[s].direction, contact.localDirection) > rebindAngleDegrees) s = -1;
                if (s < 0) s = FindOrCreateSpot(contact.localDirection);
                fingerSpot[i] = s;
                spots[s].lastUsed = Time.time;

                float press = Mathf.InverseLerp(damagePressureThreshold, 1f, pressure);
                float change = Mathf.Abs(pressure - lastPressure[i]);
                spots[s].damage = Mathf.Min(1f, spots[s].damage + press * pressureDamageRate * dt + change * kneadDamageGain);
                spots[s].spread = Mathf.Min(maxSpreadDegrees, spots[s].spread + change * spreadDegreesPerKnead);

                // 깨진 뒤(Broken 이후)에만 주무른 양이 쌓인다. 이 손가락의 변화량은 아래에서 손가락이 닿은 깨진 판에 더해진다.
                if (broken) fingerKneadStep[i] = change;
                lastPressure[i] = pressure;
            }

            if (damaging) AssignCracks();

            if (broken)
            {
                brokenRamp = Mathf.MoveTowards(brokenRamp, 1f, dt / brokenRampDuration);
                UpdateCells();
            }

            ComputeArea();
            if (LocalMode) stateMachine.SetDamagedRatio(DamagedRatio, AverageMix);
        }

        // 판 표가 바뀌면(머티리얼의 _PlateScale을 바꿨을 때) 판 번호가 달라지므로 판별 상태를 지운다.
        private void SyncCellMap()
        {
            if (visual == null) return;
            var map = visual.EnsureCellMap();
            if (map == cellMap && map.Version == cellMapVersion) return;

            cellMap = map;
            cellMapVersion = map.Version;
            System.Array.Clear(cellBroken, 0, cellBroken.Length);
            System.Array.Clear(cellKnead, 0, cellKnead.Length);
        }

        private float RadiusRadians(in Spot spot)
        {
            float t = Mathf.SmoothStep(0f, 1f, spot.damage);
            float degrees = Mathf.Lerp(minRadiusDegrees, maxRadiusDegrees, t) + spot.spread;
            return Mathf.Min(degrees, 175f) * Mathf.Deg2Rad;
        }

        // 판마다 섞임: 주무른 양 / kneadAmountPerStamp (0~1)
        private float CellMix(int slot)
        {
            return Mathf.Clamp01(cellKnead[slot] / kneadAmountPerStamp);
        }

        // 자리 반경 안에서 판 중심 방향의 영향(0~1). 반경 가장자리에서 0, 안쪽 45% 지점에서 1이다.
        // 0.5 이상이면 "자리에 덮인 판"이다(셰이더의 예전 흔적 마스크 경계와 같은 값).
        private float Influence(int spot, Vector3 direction)
        {
            float cosR = spotCos[spot];
            float inner = Mathf.Lerp(cosR, 1f, 0.45f);
            float t = Mathf.Clamp01((Vector3.Dot(direction, spots[spot].direction) - cosR) / Mathf.Max(inner - cosR, 1e-4f));
            return t * t * (3f - 2f * t);
        }

        // 판 단위 파손과 섞임. 깨짐은 셰이더의 CellBroken과 같은 식이라 영향 * 깨짐 진행(brokenRamp)이 문턱(0.15 + 0.7 * cellHash)을 넘는 판부터 깨진다.
        private void UpdateCells()
        {
            if (cellMap == null) return;

            for (int i = 0; i < MaxSpots; i++)
                if (spots[i].used) spotCos[i] = Mathf.Cos(RadiusRadians(spots[i]));

            bool changed = false;
            for (int c = 0; c < WaxBallCellMap.SlotCount; c++)
            {
                if (!cellMap.Used[c]) continue;
                Vector3 direction = cellMap.SiteDirection[c];

                float influence = 0f;
                for (int i = 0; i < MaxSpots; i++)
                    if (spots[i].used) influence = Mathf.Max(influence, Influence(i, direction));

                float target = Mathf.Clamp01((influence * brokenRamp - (0.15f + 0.7f * cellMap.CellHash[c])) * 6f);
                if (target > cellBroken[c]) cellBroken[c] = target;

                // 깨진 판 위에 닿은 손가락 중 가장 많이 움직인 손가락 하나의 변화량만 쌓는다.
                if (cellBroken[c] >= 0.5f)
                {
                    float step = 0f;
                    for (int f = 0; f < WaxBallContacts.MaxContacts; f++)
                    {
                        int s = fingerSpot[f];
                        if (s < 0 || fingerKneadStep[f] <= step) continue;
                        if (Influence(s, direction) >= 0.5f) step = fingerKneadStep[f];
                    }
                    cellKnead[c] += step;
                }

                changed |= cellMap.SetState(c, cellBroken[c], CellMix(c));
            }

            if (changed) cellMap.Apply();
        }

        // 깨진 판(깨짐 0.5 이상)이 차지하는 면적 비율과, 그 판들의 면적 가중 평균 섞임.
        private void ComputeArea()
        {
            float area = 0f, mixSum = 0f;
            int brokenCount = 0;
            if (cellMap != null)
            {
                for (int c = 0; c < WaxBallCellMap.SlotCount; c++)
                {
                    if (!cellMap.Used[c] || cellBroken[c] < 0.5f) continue;
                    brokenCount++;
                    area += cellMap.AreaRatio[c];
                    mixSum += cellMap.AreaRatio[c] * CellMix(c);
                }
            }

            BrokenCellCount = brokenCount;
            DamagedRatio = Mathf.Clamp01(area);
            AverageMix = area > 0f ? Mathf.Clamp01(mixSum / area) : 0f;
        }

        /// <summary>균열 슬롯(0 = 첫 번째, 1 = 두 번째)이 붙은 위치(공 로컬 방향). 아직 붙지 않았으면 false.</summary>
        public bool TryGetCrackAnchor(int slot, out Vector3 direction)
        {
            bool assigned = slot == 0 ? crack1Assigned : crack2Assigned;
            direction = slot == 0 ? crack1Anchor : crack2Anchor;
            if (!assigned) direction = Vector3.forward;
            return assigned;
        }

        private int FindOrCreateSpot(Vector3 direction)
        {
            int nearest = -1;
            float nearestAngle = float.MaxValue;
            int free = -1;

            for (int i = 0; i < MaxSpots; i++)
            {
                if (!spots[i].used)
                {
                    if (free < 0) free = i;
                    continue;
                }

                float angle = Vector3.Angle(spots[i].direction, direction);
                if (angle < nearestAngle)
                {
                    nearestAngle = angle;
                    nearest = i;
                }
            }

            // 가까운 자리가 있으면 그것을 키운다. 없으면 새로 만들고, 자리가 없으면 가장 오래 안 쓴 자리를 잊고 그 자리에 만든다.
            if (nearest >= 0 && nearestAngle <= rebindAngleDegrees) return nearest;
            if (free < 0) free = OldestUnboundSpot();
            if (free < 0) return nearest;

            if (crack1Spot == free) crack1Spot = -1;
            if (crack2Spot == free) crack2Spot = -1;
            spots[free] = new Spot { used = true, direction = direction.normalized, damage = 0f, lastUsed = Time.time };
            return free;
        }

        // 지금 손가락이 붙어 있지 않은 자리 중 가장 오래 안 쓴 자리. 없으면 -1.
        private int OldestUnboundSpot()
        {
            int oldest = -1;
            float oldestTime = float.MaxValue;
            for (int i = 0; i < MaxSpots; i++)
            {
                if (!spots[i].used || IsBound(i)) continue;
                if (spots[i].lastUsed < oldestTime)
                {
                    oldestTime = spots[i].lastUsed;
                    oldest = i;
                }
            }
            return oldest;
        }

        private bool IsBound(int spot)
        {
            for (int f = 0; f < fingerSpot.Length; f++) if (fingerSpot[f] == spot) return true;
            return false;
        }

        // 첫 균열은 가장 세게 누른 손가락, 두 번째 균열은 그 자리가 아닌 곳에서 두 번째로 세게 누른 손가락에 붙인다.
        // 한 번 붙은 균열은 옮기지 않는다.
        private void AssignCracks()
        {
            if (!crack1Assigned)
            {
                int finger = StrongestFinger(-1);
                int spot = finger >= 0 ? fingerSpot[finger] : MostDamagedSpot(-1);
                if (spot >= 0)
                {
                    crack1Assigned = true;
                    crack1Spot = spot;
                    crack1Anchor = spots[spot].direction;
                    if (finger >= 0) Crack1Finger = (WaxFinger)finger;
                }
            }

            if (!crack2Assigned && crack1Assigned)
            {
                int finger = StrongestFinger(crack1Spot);
                if (finger >= 0)
                {
                    crack2Assigned = true;
                    crack2Spot = fingerSpot[finger];
                    crack2Anchor = spots[crack2Spot].direction;
                    Crack2Finger = (WaxFinger)finger;
                }
            }
        }

        // 지금 자리에 붙어 누르는 손가락 중 가장 센 손가락(excludeSpot에 붙은 손가락은 제외). 없으면 -1.
        private int StrongestFinger(int excludeSpot)
        {
            int best = -1;
            float bestPressure = -1f;

            foreach (WaxFinger finger in FingerPriority)
            {
                int i = (int)finger;
                int s = fingerSpot[i];
                if (s < 0 || s == excludeSpot) continue;
                if (lastPressure[i] > bestPressure)
                {
                    bestPressure = lastPressure[i];
                    best = i;
                }
            }
            return best;
        }

        private int MostDamagedSpot(int excludeSpot)
        {
            int best = -1;
            float bestDamage = -1f;
            for (int i = 0; i < MaxSpots; i++)
            {
                if (!spots[i].used || i == excludeSpot) continue;
                if (spots[i].damage > bestDamage)
                {
                    bestDamage = spots[i].damage;
                    best = i;
                }
            }
            return best;
        }

        private void ClearAll()
        {
            for (int i = 0; i < MaxSpots; i++) spots[i] = default;
            for (int i = 0; i < fingerSpot.Length; i++)
            {
                fingerSpot[i] = -1;
                lastPressure[i] = 0f;
            }
            crack1Assigned = false;
            crack2Assigned = false;
            crack1Spot = -1;
            crack2Spot = -1;
            Crack1Finger = null;
            Crack2Finger = null;
            DamagedRatio = 0f;
            AverageMix = 0f;
            BrokenCellCount = 0;
            brokenRamp = 0f;
            System.Array.Clear(cellBroken, 0, cellBroken.Length);
            System.Array.Clear(cellKnead, 0, cellKnead.Length);

            // 판 표(텍스처)의 깨짐·섞임도 0으로 돌린다.
            if (cellMap != null)
            {
                for (int c = 0; c < WaxBallCellMap.SlotCount; c++)
                    if (cellMap.Used[c]) cellMap.SetState(c, 0f, 0f);
                cellMap.Apply();
            }
        }
    }
}
