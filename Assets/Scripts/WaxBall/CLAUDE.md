# Assets/Scripts/WaxBall/CLAUDE.md

Unity / AR 구현 세부 안내서다. 팀 전체 개요, 상태 흐름, 이벤트 사용법, 역할 분담, Git 규칙은 레포 루트 `CLAUDE.md`를 먼저 본다.
이 폴더의 코드는 **장비 SDK(WEART, SenseGlove)에 직접 의존하지 않는다.** 장비 의존 코드는 `Assets/Scripts/Haptics/` 어댑터로 분리한다.

---

## 1. 이 폴더와 관련 파일

| 파일 | 역할 |
|---|---|
| `WaxBall/WaxBallStateMachine.cs` | 상태 판정의 유일한 장소(공 전체 기준). `SetGrip()`, `SetFingerPressure()`, `ResetBall()`, `StateChanged`, `Crunched(finger, strength)`, `Grip`, `MixProgress` |
| `WaxBall/WaxFinger.cs` | `enum WaxFinger { Thumb, Index, Middle }` (WEART TouchDIVER G1이 지원하는 세 손가락) |
| `WaxBall/WaxBallContacts.cs` | 손가락 접촉점 입력 API(세 손가락, 위치 + 세기). 장비 의존 없음. 전체 grip 합치기(최댓값/평균) |
| `../Interaction/QuestControllerInput.cs` | Quest 컨트롤러 입력 어댑터(오른손 트리거 0~1 → `SetGrip`, A 버튼 → 리셋) |
| `../Interaction/FaceHeadset.cs` | 월드 공간 UI(디버그 텍스트)가 헤드셋을 향하게 함 |
| `Assets/Editor/WaxBallQuestSceneBuilder.cs` | 메뉴 `WaxBall > Create Quest WaxBall Scene`으로 Quest용 씬(`Dev_WaxBallQuest`)을 만듦 |
| `../Interaction/FakeFingerContacts.cs` | 에디터 테스트용 가짜 손가락 3개(Scene 뷰에서 드래그, 세기는 Slider grip). 기본 배치는 세 손가락으로 집는 모양(엄지가 검지·중지 반대편) |
| `WaxBall/WaxBallDamage.cs` | 손가락이 누른 자리의 파손 범위 모델(파손 흔적 최대 8개). 렌더링은 하지 않음. 균열을 붙일 손가락 선택 |
| `WaxBall/WaxBallShellVisual.cs` | 상태와 grip을 읽어 셰이더 값(균열·파열·섞임)과 찌그러짐을 갱신. 균열 모양을 리셋마다 무작위로 뽑음 |
| `WaxBall/WaxBallTempVisual.cs` | 초기 임시 시각 효과(상태별 색). 지금 빌더는 쓰지 않는다 |
| `../Interaction/SliderGripInput.cs` | 에디터 테스트용 입력 어댑터(Slider → `SetGrip`, R 키/버튼 → 리셋) |
| `../Interaction/WaxBallStateDebugText.cs` | 상태, grip, 섞임 진행도, Crunch 횟수·세기를 화면에 표시 |
| `Assets/Editor/WaxBallDevSceneBuilder.cs` | 메뉴 `WaxBall > Create Dev Test Objects`로 테스트 오브젝트 구성 |
| `Assets/Shaders/WaxBall/WaxShell.shader` | 왁뿌볼 껍질 셰이더(URP) |
| `Assets/Materials/WaxShell.mat` | 껍질 머티리얼 |

시각·햅틱·소리 모듈은 **이벤트를 구독만 한다.** 상태 판정 로직은 `WaxBallStateMachine`에만 둔다.
연속값(찌그러짐 정도, 섞임 진행도)은 이벤트가 아니라 `Grip`, `MixProgress`를 매 프레임 읽는다.

---

## 2. 상태 머신 구현 세부

- 임계값(`pressThreshold`, `crackThreshold`, `breakThreshold`, `kneadAmountToMix`)은 하드코딩하지 않고 `[SerializeField]`로 노출한다. 값은 Interaction 담당이 정한다.
- `SetGrip()`은 grip을 0~1로 clamp하고, 한 번에 큰 값이 들어와도 **중간 상태 이벤트를 빠뜨리지 않도록 한 단계씩** 진행한다.
- Broken 이후에는 grip 변화량을 `KneadAccumulated`에 누적하고, 접촉점이 없는 씬의 `MixProgress = KneadAccumulated / kneadAmountToMix`(0~1)이다. 접촉점 씬은 깨진 면적과 흔적별 평균 섞임으로 판정한다(`mixAreaThreshold`, `mixAverageThreshold`, 3장).
- **Crunched(WaxFinger finger, float strength)**: Broken 상태에서 **손가락별로** 세기(`SetFingerPressure`) 변화량이 `crunchGripStep`(기본 0.2)만큼 쌓일 때마다 그 손가락에 한 번 발생한다. WEART로 해당 손가락에만 진동을 주는 용도다.
  - 세기는 그 손가락의 세기 변화 속도로 정한다. `crunchFullSpeed`(기본 2/초) 이상이면 1, 가장 느리면 `crunchMinStrength`(기본 0.2).
  - C# `event Action<WaxFinger, float> Crunched`, Inspector `UnityEvent<WaxFinger, float> onCrunched`.
  - Broken에 막 진입한 순간의 변화는 세지 않는다. Mixed에서는 발생하지 않는다. `SetGrip`만으로는 발생하지 않는다.
  - 상태(`StateChanged`)와 `MixProgress`는 여전히 공 전체 grip(`SetGrip`) 기준이다.
- `ResetBall()`은 grip, 섞임 누적값, Crunched 누적값을 0으로 돌리고 `Idle`로 바꾼다. 어느 상태에서든, 횟수 제한 없이 호출할 수 있다.
- 런타임에 생성한 오브젝트가 생기면 리셋 시 반드시 정리한다(무한 반복해도 메모리가 누적되지 않게). 현재는 런타임 생성 오브젝트가 없다.
- 이 폴더의 코드는 `WaxBall` 네임스페이스를 쓴다.

---

## 3. 시각 효과 설계 (`docs/references/README.md` 기준)

**구조: 얇고 말랑한 투명 고무 케이스(공기 없이 꽉 참, `docs/references/casing.png` 참고) > 색깔 있는 왁스 껍질(납작한 판) > 흰 점토.** 파편·파티클은 케이스 안에 갇혀서 **밖으로 튀지 않으므로 파편 오브젝트나 파티클 효과는 만들지 않는다.** 모든 표현은 `WaxShell` 셰이더와 `WaxBallShellVisual`로 한다.

**투명 고무 케이스 표현(모든 단계)**: 실제 케이스는 참고 이미지의 반질반질한 광택을 만들지만, **가상 공에서는 광택(하이라이트)을 쓰지 않는다.** 창 모양·광원 하이라이트와 표면 물결을 넣어 봤더니 흰 덩어리처럼 보여 부자연스러웠다. 케이스는 가장자리로만 표현한다.
- 가장자리 프레넬과 실루엣 가장자리의 밝고 투명한 테두리(idle 이미지 참고). 속성: `Fresnel Power`, `Fresnel Strength`, `Casing Edge`(0으로 하면 케이스 표현이 모두 사라짐)
- 하이라이트, 물결, 환경 반사는 만들지 않는다. 다시 넣으려면 먼저 사용자와 상의한다.
- 투명 메시를 한 겹 더 씌우지 않고 셰이더 안에서 처리한다. 반투명 오버드로로 Quest의 픽셀 채움 부담이 커지고, 패스스루 위 반투명 정렬 문제가 생기며, 안쪽과 같은 정점 변형을 중복해서 따라가야 하기 때문이다. 대신 실제 투명이 아니라 프레넬로 만든 테두리라 케이스 너머로 배경이 비치지는 않는다.

| 상태 | 모습 |
|---|---|
| Idle / Pressed | 매끈한 단색 공 + 가장자리의 투명한 테두리. 광택 없음. 균열 없음 |
| Cracked | 판은 거의 그대로. 삐뚤빼뚤한 균열 두 줄을 따라 흰 점토가 불룩하게 비집고 나옴. grip이 커질수록 균열이 길어지고 틈이 조금 넓어짐 |
| Broken | 큰 판 몇 개로 갈라지고 틈은 흰 점토. grip과 주무르기에 따라 큰 판 안에 작은 판 경계가 하나씩 생겨 더 잘게 쪼개짐. grip에 따라 손가락 쪽이 둥글게 들어감(실루엣은 매끈한 구) |
| Mixed | 흰색이 표면 대부분을 덮고 작은 초록 판 조각만 군데군데 박힘. `MixProgress`가 늘수록 판이 제각각 줄어들어 사라지고(mixed), 끝(mixed-02)에는 표면의 약 20%에 작고 납작한 초록 조각이 군데군데 남음. 점토는 깨끗한 흰색이고 고무에 눌려 **매끈하다**(주름 없음) |

공통 규칙
- 판은 매끈하고 반짝인다. **판마다 밝기를 다르게 하지 않는다.** 검은 균열선은 쓰지 않는다(틈은 흰 점토 색).
- 판 가장자리에는 밝은 테두리를 둬서 두께감을 준다.
- 균열과 틈 선은 화면에서 **1px 아래로 가늘어지지 않게** 보정한다(`Min Pixel Width`).
- **광택(하이라이트)과 환경 반사는 쓰지 않는다.** AR에서는 배경이 실제 방이고, 스카이박스 반사는 공 가운데에 지평선 띠를 만들었다.
- 점토는 **매끈하게** 그린다. 주름 노이즈는 쓰지 않고, 점토는 광원 색과 무관하게 깨끗한 흰색 조명(회색 음영 없이)을 쓴다. 기본 방향성 조명이 따뜻한 크림색이라 광원 색을 곱하면 점토가 누렇게 보이기 때문이다. 판은 일반 방향성 음영을 쓴다.
- **실루엣 노이즈와 표면 요철(법선 흔들림)은 쓰지 않는다.** 고무 케이스가 감싸고 있어 실제로는 매끈한 구에 가깝고, 손에 든 스펀지 공도 둥글기 때문이다. 틈 가장자리 음영(`Clay Bulge`)도 기본 0.1로 작게 둔다. 형태 변화는 grip에 따라 손가락 쪽이 둥글게 들어가는 것(`dentStrength` 기본 0.5, 최대 grip일 때)과 찌그러짐뿐이다.
- 껍질 색(`Shell Color`, 기본 청사과 초록)과 점토 색(`Clay Color`, 기본 흰색)은 머티리얼 Inspector에서 바꿀 수 있다.
- Quest 성능을 위해 단일 Forward 패스, 텍스처 없이 절차적으로만 계산한다. 픽셀마다 Voronoi를 두 번 계산하므로 Quest에서 성능을 확인해야 한다.

### 파손 범위: 누른 자리만 깨진다 (`WaxBallDamage`)

실제로는 손가락으로 누른 부분의 왁스만 깨진다. 접촉점(`WaxBallContacts`)을 쓰는 씬에서는 다음과 같이 동작한다.

- **파손 흔적(stamp)**: 상태가 Cracked 이상일 때, `damagePressureThreshold`(기본 0.3) 이상으로 누르는 손가락 아래에 흔적이 생긴다. 흔적은 공 로컬 방향 하나와 파손 정도(0~1)를 가진다. 최대 8개다.
- **범위가 커지는 조건**: 세게 누르면(초당 `pressureDamageRate`) 파손 정도가 늘고, 주무르면(세기 변화량 × `kneadDamageGain`) 더 늘어난다. 반경은 `minRadiusDegrees`(20도)에서 `maxRadiusDegrees`(55도)까지 커지고 줄지 않는다.
- **손가락을 떼도 흔적은 남는다.** 리셋(Idle)에서만 모두 지운다. 손가락이 흔적에서 `rebindAngleDegrees`(25도) 안이면 같은 흔적을 키우고, 벗어나면 새 흔적을 만든다. 자리가 없으면 가장 가까운 흔적에 합친다.
- **번지기(아주 약하게)**: 같은 자리를 주무르면(세기 변화량 1당 `spreadDegreesPerKnead`, 기본 2도) 그 흔적의 반경이 조금 더 넓어진다. 상한은 `maxSpreadDegrees`(기본 10도)이고 0이면 꺼진다. 한 자리만으로는 80%에 닿지 못한다.
- **처음엔 작게**: `pressureDamageRate` 0.03(거의 0)이라 누르고만 있으면 첫 깨짐 이후 범위가 거의 안 커지고, 주무를 때(`kneadDamageGain` 0.4)만 커진다. 반경은 최대 55 + 번지기 10 = 65도라 한 흔적이 표면의 약 22%이고, 세 손가락이 같은 자리를 쥐면 40~50%에서 멈춘다. 80%에는 손가락을 여러 자리로 옮겨야 닿는다.
- **깨진 면적과 섞임은 따로다.** `DamagedRatio`는 "판이 갈라진 범위"이고, 흔적마다 쌓은 주무른 양이 "쪼개짐과 흰색 정도"(섞임)다.
  - **면적**: `WaxBallDamage`가 공 표면의 균일한 표본점 512개(공 로컬) 중 흔적 반경 안에 든 비율을 `DamagedRatio`로 계산한다. 흔적 경계는 셰이더 마스크 0.5 지점이라 숫자와 화면이 맞는다.
  - **흔적별 주무른 양**: Broken 이후, 흔적에 붙은 손가락의 세기 변화량이 그 흔적의 `knead`에 쌓인다. 흔적의 섞임 = `knead / kneadAmountToMix`(0~1, 기본 4.5). 새로 깬 자리는 섞임 0이라 항상 초록 판 + 흰 틈에서 시작한다.
  - **평균 섞임**(`AverageMix`): 깨진 표본점마다 가장 깊이 든 흔적의 섞임을 모아 면적 가중 평균을 낸다. `MixProgress`는 접촉점 씬에서 이 값이다(Broken 전에는 0). 디버그 텍스트의 `Mix` 줄도 이 값이다.
  - **Mixed 조건**: `DamagedRatio ≥ mixAreaThreshold`(기본 0.8) 그리고 `AverageMix ≥ mixAverageThreshold`(기본 0.9). 둘 다 `WaxBallStateMachine` Inspector에서 조절한다. 접촉점이 없는 씬은 예전처럼 전체 주무른 양(`kneadAmountToMix`)으로 판정한다.
  - **셰이더**: 판 줄어듦과 쪼개짐은 픽셀이 속한 흔적의 섞임(`_StampMix[8]`)을 따른다. Mixed 직전(면적 기준 이상, 평균 섞임이 기준의 `mixFillStart`=0.85 이상)에는 `_MixFill`이 남은 부분까지 채운다.
- **균열 두 줄의 시작점**: 첫 균열은 **가장 세게 누른 손가락 아래**에서, 두 번째 균열은 그 흔적이 아닌 곳에서 **두 번째로 세게 누른 손가락 아래**에서 시작한다. 한 번 붙은 균열은 옮기지 않는다. 세기가 같으면 검지, 중지, 엄지 순으로 먼저 붙는다(가짜 손가락은 `Pressure Scale`이 엄지 1.0, 검지·중지 0.9라서 첫 균열은 엄지 = 기본 배치에서 공 앞쪽, 두 번째는 검지다). 붙은 손가락이 없는 균열은 보이지 않는다.
- **셰이더 마스크**: 픽셀마다 흔적 8개까지의 거리로 "얼마나 깨졌나"(0~1)를 계산한다. 판 사이 경계는 경계마다 정해진 반경에서 하나씩 나타나서, 파손 범위가 원형 스텐실이 아니라 경계를 따라 번져 나가는 모양이다. Mixed에서 판이 줄어드는 것도 같은 마스크 안에서만 일어난다. 마스크 계산은 흔적 8개에 대한 내적이라 비용이 작다.
- **접촉점이 없는 씬(Slider만 사용)**: `WaxBallContacts.HasReceivedContact`가 false이면(접촉점 입력이 한 번도 안 들어온 경우) 공 전체가 깨지는 예전 방식으로 동작하고, 균열 시작점은 리셋마다 무작위다. 개발 씬에서 이 방식을 보려면 Play 전에 `FakeFingers` 오브젝트를 끈다. 한 번 접촉점이 들어오면 그 실행 동안은 계속 접촉점 방식이다.
- 디버그 텍스트의 `Damage: Local, n stamps`, `Damaged: 45%`(파손 면적), 그리고 `Cracks: Index / Middle`로 흔적 수와 균열이 붙은 손가락을 확인한다.
- 손가락마다 둥글게 들어가는 효과(세 손가락 각자)는 아직 첫 번째 균열의 위치 하나만 따른다(다음 단계).

### 셰이더 값과 `WaxBallShellVisual`

`WaxBallShellVisual`이 `MaterialPropertyBlock`으로 매 프레임 넘기는 값:

| 값 | 의미 |
|---|---|
| `_CrackAmounts` | 균열 두 줄의 진행도(x: 첫 번째, y: 두 번째, 0~1). Cracked에서 grip에 따라 커지고, 한 번 간 값은 되돌아가지 않음(리셋에서만 0). 손가락에 붙지 않은 균열은 0 |
| `_LocalDamage`, `_Stamps[8]` | 접촉점을 쓰는 씬이면 1. 파손 흔적 8개(방향 xyz, 반경의 코사인). 빈 칸은 코사인 2 |
| `_BrokenAmount` | 파열 정도(0~1). Broken 진입 시 `brokenRampDuration`(기본 0.6초) 동안 1로 올라가며 큰 판으로 갈라짐. 시각 효과만 늦추고 `StateChanged`와 햅틱 이벤트는 즉시 발생 |
| `_Fragment` | 판이 쪼개진 정도(0~1). `fragmentBase` + `fragmentGripWeight` × grip + `fragmentKneadWeight` × `MixProgress`를 목표로 `fragmentSpeed`(기본 0.5/초)로 따라가고 줄지 않음. 주무른 양이 대부분을 좌우하므로 여러 번 주물러야 점점 잘게 깨진다 |
| `_MixAmount` | `MixProgress`(0~1). 접촉점이 없는 씬에서 판 줄어듦을 정한다(전체 주무른 양). Mixed 진입 시 1이 되므로 Mixed 상태는 mixed-02 모습(작은 초록 조각이 군데군데 남음)이다 |
| `_StampMix[8]`, `_MixFill`, `_FragmentKnead` | 접촉점 씬에서: 흔적마다 섞임(0~1), Mixed 직전 남은 부분 채움 정도, 주무른 양이 쪼개짐에 더하는 비중(`fragmentKneadWeight`) |
| `_DentDir`, `_DentAmount` | 둥글게 들어가는 방향(오브젝트 공간)과 정도. 정도는 Broken 이후 grip을 부드럽게 따라간다 |
| `_Crack1A/B`, `_Crack2A/B` | 균열 두 줄의 모양 파라미터 |

균열 파라미터(`A = 시작점 xyz + 길이 배율`, `B = 방향 각도, 휘어짐, 시드, 폭 배율`)는 **리셋(Idle)마다 줄마다 따로 무작위로 뽑는다.** 접촉점을 쓰는 씬에서는 시작점(`A.xyz`)만 손가락 아래 흔적 위치로 대체된다. 두 줄은 방향, 길이, 휘어짐, 폭이 서로 달라야 한다.
- 기본 시작점은 카메라 쪽(`randomStartForward`) 원뿔(`randomStartConeAngle`) 안에서 뽑고, 두 시작점은 `minStartSeparation` 이상 떨어진다.
- 나중에 손가락 위치로 바꾸려면 `SetCrackStarts(방향1, 방향2)`(공 로컬 방향)를 호출한다. 호출하면 리셋해도 유지되고, `ClearCrackStarts()`로 해제한다. 오목하게 눌리는 중심도 첫 번째 시작점을 따른다.

찌그러짐: `maxSquash`(Cracked까지)보다 `brokenSquash`(Broken 이후)가 크다. 껍질이 더 이상 버티지 않기 때문이다.

Mixed 구현: 작은 판(Voronoi 셀)마다 무작위 값을 둔다. 셀의 `Piece Fraction`(기본 0.45)만 **남는 판**이고, 이 판은 `Piece Inset`(기본 0.03)만큼 가장자리가 줄어든 작은 조각이 된다. 나머지 판은 `MixProgress`에 따라 셀마다 다른 시점(약 0.3~0.95)에 점토에 묻혀 사라진다. 큰 판 사이 틈은 섞일수록 가늘어져서 남은 조각을 자르지 않는다. 조각이 차지하는 면적은 대략 `Piece Fraction` × (줄어든 판의 면적 비율)로 약 20%를 목표로 한다. 판이 줄어든 자리는 기존 틈 처리(흰 점토, 밝은 테두리, 1px 보정)를 그대로 쓴다.

셰이더 튜닝 값(수치는 눈으로 보고 조정): `Crack Length`, `Gap Width`, `Plate Scale`, `Fine Scale Multiplier`, `Plate Gap Width`, `Piece Fraction`(남는 조각 수), `Piece Inset`(조각 크기, 클수록 작음), `Clay Bulge`, `Rim Width`, `Rim Brightness`, 케이스 표현(`Fresnel Power`, `Fresnel Strength`, `Casing Edge`).

---

## 4. 개발 환경 (Unity 세부)

| 항목 | 값 |
|---|---|
| Unity | **2022.3.62f3** (다른 버전으로 열지 말 것) |
| 렌더 파이프라인 | URP 14.0.12 |
| XR | Meta XR All-in-One SDK 201.0.0, Oculus XR Plugin 4.5.6 |
| 기타 패키지 | TextMeshPro, Timeline, Visual Scripting, Unity UI |
| Build Target | **Android** (Meta Quest 3, 패스스루 AR) |
| 필수 Unity 모듈 | Android Build Support, Android SDK & NDK Tools, OpenJDK |

- URP Lit 머티리얼 색은 `_BaseColor`로 바꾼다(`_Color` 아님). `WaxShell`은 자체 셰이더라서 `_ShellColor`, `_ClayColor`를 쓴다.
- Quest 빌드 시 `Build And Run`을 사용하고, Meta Quest Developer Hub로 기기를 확인한다.
- Unity가 자동으로 지우거나 만드는 파일(`Assets/StreamingAssets/RuntimeActionBindings.json` 등)은 커밋 전에 확인한다. 의도하지 않은 삭제는 `git restore`로 복원한다.

---

## 5. 테스트 씬과 빌더

- 테스트 씬은 `Assets/Scenes/dev_waxball.unity`(개인 `Dev_*` 씬, 파일명은 소문자)다. 공용 씬 수정은 한 사람만 한다.
- 메뉴 **`WaxBall > Create Dev Test Objects`** 가 공, Slider, 리셋 버튼, 디버그 텍스트를 만든다. 실행하기 전에 기존 `WaxBall`, `DevCanvas`, `SliderGripInput`을 지우므로 여러 번 실행해도 중복되지 않는다.
- 공은 **지름 7cm**(`BallDiameter`)로 만들고, 메인 카메라를 공 앞 **35cm**(`CameraDistance`)에 두며 near clip을 0.01로 낮춘다(실제 Quest 시점과 비슷하게). near clip이 0.3 그대로면 공이 잘려 보인다.
- **화면에 보이는 UI 문자열(디버그 텍스트, 버튼 등)은 영어로 쓴다.** 예: `Crunch: 65 (Index, 1.00)`. 코드 주석·문서·커밋 메시지는 한국어다.
- 장비 없이 Slider로 `Idle → … → Mixed → 리셋` 전체 흐름과 Crunched를 확인할 수 있어야 한다. R 키 또는 리셋 버튼으로 리셋한다.
- Unity에서 `Cmd+Z`로 빌더 결과를 되돌리면 UI 자식 오브젝트가 흩어질 수 있다. 되돌리지 말고 메뉴를 다시 실행한다.

---

### Quest 실기기 씬 (`Dev_WaxBallQuest.unity`)

- 메뉴 **`WaxBall > Create Quest WaxBall Scene`** 이 패스스루가 켜진 `Dev_QuestBuildTest`를 복사해 만든다(여러 번 실행해도 같은 파일을 다시 구성한다). 에디터용 Slider 씬(`dev_waxball`)은 건드리지 않는다.
- 공은 **지름 7cm, 눈앞 35cm**(`(0, 0, 0.35)`)에 있다. 추적 원점이 EyeLevel이라 앱을 켠 순간 머리 위치가 원점이다.
- **입력**: 오른손 컨트롤러 **트리거(0~1) = grip**, **A 버튼 = 리셋**. 리셋 뒤에는 트리거를 한 번 놓을 때까지 grip이 0이라 쥔 채로 리셋해도 새 공이 바로 깨지지 않는다.
- **디버그 텍스트**는 공 오른쪽 12cm에 월드 공간으로 작게(15cm 폭, 글자 약 9mm) 뜨고 헤드셋을 향한다. 상태, grip, 섞임, Crunch, 파손 흔적, FPS를 표시한다.
- 접촉점 입력이 없으므로 기본은 **공 전체가 깨지는 방식**(셰이더가 가장 무거운 경우)이다. 가짜 손가락(`FakeFingers`)은 꺼 두었다. 켜면 고정된 세 손가락 자리 주변에서만 깨지는 접촉점 방식을 Quest에서 볼 수 있다.
- 큐브와 `Global Volume`(포스트 프로세싱)은 지웠다. 이 씬을 만들면 Build Settings에서 이 씬만 켜진다.

**양쪽 눈 확인 (Multiview)**: Quest는 Multiview로 양쪽 눈을 한 번에 그린다. 커스텀 셰이더에는 스테레오 인스턴싱 매크로가 필수이고 `WaxShell.shader`에 넣어 두었다(없으면 한쪽 눈이 비거나 양쪽이 같은 그림이 된다). 새 셰이더를 만들 때도 같은 매크로를 넣는다. 확인은 헤드셋을 쓰고 공을 가까이 본 채 한쪽 눈씩 번갈아 감아서, 두 눈에 같은 위치와 균열이 보이고 입체감이 있는지, 깜빡이거나 이중으로 보이는지 본다.

**성능 확인**: 디버그 텍스트의 FPS는 프레임 간격이라 GPU 여유까지 알려 주지 않는다. **OVR Metrics Tool**(헤드셋 안 오버레이)이나 Meta Quest Developer Hub의 Performance Analyzer로 앱 FPS, GPU 사용률, 프레임 타임을 본다. 셰이더가 가장 무거운 경우(공 전체가 깨진 Broken에서 Mixed로 가는 구간, 공을 가까이)에서 90Hz 기준 GPU 프레임 타임이 약 11ms 안인지, 72/90 FPS 아래로 떨어지지 않는지 확인한다. 부담이면 Voronoi 계산을 줄인다.

## 6. 실물 공 정렬과 grip 보정 (Unity 담당 구현)

- 가상 왁뿌볼은 실물 스펀지 공과 **같은 위치·크기**로 보여야 한다. 실물 공 지름을 재서 가상 공 크기를 맞춘다.
- 공 위치를 맞추는 방법(미확정):
  - (A) 손 기준 고정: 쥐는 동안 가상 공을 손바닥 기준 위치에 붙인다. 가장 단순하다.
  - (B) 시작 위치 보정: 시작할 때 공을 정해진 자리(받침대 등)에 두고 맞춘 뒤 손을 따라간다.
  - (C) 실물 공 추적: 마커나 컨트롤러 부착 등. 가장 정확하지만 복잡하다.
- **grip 보정(calibration)**: 실물 공을 쥐고 최소(살짝 잡음)~최대(꽉 쥠) 값을 기록해 0~1로 매핑한다. 공을 쥔 손은 굽힘 범위가 맨손보다 좁다.
- 실물 공이 손가락을 가려 **Quest 핸드트래킹이 불안정할 수 있다.** 장갑 자체 tracking 사용 여부를 정할 때 테스트한다.
- 공이 손을 따라 움직이거나 회전하면 균열 위치도 공과 함께 돈다(셰이더가 오브젝트 공간 기준).

---

## 7. 현재 우선순위 (Unity 담당)

1. [완료] 장비와 독립적인 상태 시스템 (Slider → grip → 상태 판정 → 이벤트 → 리셋 버튼), `Crunched` 이벤트
2. 시각 효과 고도화: Idle/Cracked/Broken/Mixed 구현 완료(Unity 확인 중), Quest 성능 확인
3. Quest 빈 씬 Build And Run 성공 (개발자 모드 해결 후)
4. 스펀지 공을 쥔 상태에서 Quest 핸드트래킹 안정성 테스트 → grip 변환 + 보정
5. 가상 공을 실물 공 위치에 정렬, 균열 시작점을 손가락 위치로 연결
6. 장갑 입력 연결

---

## 8. 나중 계획 메모 (아직 구현하지 않음)

### (a) 실물 공 위치 정합: 손가락 끝 세 점과 공 반지름으로 공 중심 계산
- 정합 방식은 **손가락 끝 3점과 공 반지름으로 공 중심을 계산**하는 방식으로 할 예정이다(6장 (A)·(B)·(C) 중 별도 마커 없이 손가락만 쓰는 방식).
  - 엄지, 검지, 중지 끝 세 점이 모두 공 표면에 닿아 있다고 보고, 세 점에서 실물 공 반지름만큼 떨어진 점을 공 중심으로 잡는다. 세 점을 지나는 반지름 R인 구의 중심은 두 곳(세 점이 이루는 평면의 양쪽)이므로, 손바닥 쪽에 있는 해를 고른다.
  - 손가락 끝 위치는 Quest 핸드트래킹이나 장갑 tracking에서 얻고, 반지름은 사용할 스펀지 공의 지름에서 얻는다(스펀지 공 사양은 미확정).
- **공의 회전은 추적하지 않는다.** 공은 항상 방향이 고정(회전 없음)이고 위치만 따라간다. 파손 흔적과 균열 위치는 공 로컬 좌표라서 공이 움직이면 같이 움직이지만, 공이 회전하는 것은 반영하지 않는다.

### (b) 햅틱 제안 (햅틱 담당과 협의)
- **처음 깨질 때**(`StateChanged`로 Broken 진입): 지금 **닿아 있는 세 손가락 모두** 진동한다.
- **주무를 때**(`Crunched(finger, strength)`): **깨진 손가락만** 진동한다.
- 구현에 새 이벤트는 필요 없다. 어댑터가 `Broken` 진입 시 접촉 중인 손가락 전체에, 이후에는 `Crunched`가 알려 주는 손가락에만 진동을 주면 된다.

## 9. Claude Code 작업 시 주의 (이 폴더)

- 화면에 보이는 UI 문자열은 영어로 쓴다.
- 스크립트 파일이나 클래스 이름을 바꾸지 말 것(.meta GUID와 씬 참조가 끊긴다). 꼭 바꿔야 하면 먼저 확인받는다.
- `.meta` 파일을 지우거나 새로 만들지 말 것.
- 셰이더 속성 이름을 바꾸면 `WaxBallShellVisual`과 머티리얼의 저장된 값이 어긋난다. 함께 고친다.
- 환경 반사, 파편 오브젝트, 파티클은 넣지 않는다(3장 이유 참고).
- 새 셰이더에는 스테레오 인스턴싱 매크로(`UNITY_VERTEX_INPUT_INSTANCE_ID`, `UNITY_VERTEX_OUTPUT_STEREO`, `UNITY_SETUP_INSTANCE_ID`, `UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO`, `UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX`)를 반드시 넣는다. Quest는 Multiview를 쓴다.
