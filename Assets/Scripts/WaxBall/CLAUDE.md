# Assets/Scripts/WaxBall/CLAUDE.md

Unity / AR 구현 세부 안내서다. 팀 전체 개요, 상태 흐름, 이벤트 사용법, 역할 분담, Git 규칙은 레포 루트 `CLAUDE.md`를 먼저 본다.
이 폴더의 코드는 **장비 SDK(WEART, SenseGlove)에 직접 의존하지 않는다.** 장비 의존 코드는 `Assets/Scripts/Haptics/` 어댑터로 분리한다.

---

## 1. 이 폴더와 관련 파일

| 파일 | 역할 |
|---|---|
| `WaxBall/WaxBallStateMachine.cs` | 상태 판정의 유일한 장소. `SetGrip()`, `ResetBall()`, `StateChanged`, `Crunched`, `Grip`, `MixProgress` |
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
- Broken 이후에는 grip 변화량을 `KneadAccumulated`에 누적하고, `MixProgress = KneadAccumulated / kneadAmountToMix`(0~1)이다.
- **Crunched**: Broken 상태에서 grip 변화량이 `crunchGripStep`(기본 0.2)만큼 쌓일 때마다 한 번 발생한다.
  - 세기는 grip 변화 속도로 정한다. `crunchFullSpeed`(기본 2 grip/초) 이상이면 1, 가장 느리면 `crunchMinStrength`(기본 0.2).
  - C# `event Action<float> Crunched`, Inspector `UnityEvent<float> onCrunched`.
  - Broken에 막 진입한 순간의 grip 변화는 세지 않는다. Mixed에서는 발생하지 않는다.
- `ResetBall()`은 grip, 섞임 누적값, Crunched 누적값을 0으로 돌리고 `Idle`로 바꾼다. 어느 상태에서든, 횟수 제한 없이 호출할 수 있다.
- 런타임에 생성한 오브젝트가 생기면 리셋 시 반드시 정리한다(무한 반복해도 메모리가 누적되지 않게). 현재는 런타임 생성 오브젝트가 없다.
- 이 폴더의 코드는 `WaxBall` 네임스페이스를 쓴다.

---

## 3. 시각 효과 설계 (`docs/references/README.md` 기준)

**구조: 투명 고무층 > 색깔 있는 왁스 껍질(납작한 판) > 흰 점토.** 파편·파티클은 고무층 안에 갇혀서 **밖으로 튀지 않으므로 파편 오브젝트나 파티클 효과는 만들지 않는다.** 모든 표현은 `WaxShell` 셰이더와 `WaxBallShellVisual`로 한다.

| 상태 | 모습 |
|---|---|
| Idle / Pressed | 매끈한 단색 공 + 투명 고무층의 젖은 듯한 광택. 균열 없음 |
| Cracked | 판은 거의 그대로. 삐뚤빼뚤한 균열 두 줄을 따라 흰 점토가 불룩하게 비집고 나옴. grip이 커질수록 균열이 길어지고 틈이 조금 넓어짐 |
| Broken | 큰 판 몇 개로 갈라지고 틈은 흰 점토. grip과 주무르기에 따라 큰 판 안에 작은 판 경계가 하나씩 생겨 더 잘게 쪼개짐. 손가락 쪽으로 오목하게 눌리고 실루엣이 반죽처럼 불규칙해짐 |
| Mixed | (미구현) 흰색이 표면 대부분을 덮고 작은 판 조각만 군데군데 박힘 |

공통 규칙
- 판은 매끈하고 반짝인다. **판마다 밝기를 다르게 하지 않는다.** 검은 균열선은 쓰지 않는다(틈은 흰 점토 색).
- 판 가장자리에는 밝은 테두리를 둬서 두께감을 준다.
- 균열과 틈 선은 화면에서 **1px 아래로 가늘어지지 않게** 보정한다(`Min Pixel Width`).
- 광택은 **하이라이트 위주**로 낸다(광원 하이라이트 + 시점 기준 창 모양 하이라이트 + 약한 프레넬). AR에서는 배경이 실제 방이므로 **스카이박스 같은 환경 반사는 쓰지 않는다**(공 가운데에 지평선 띠가 생기는 원인이었다).
- 껍질 색(`Shell Color`, 기본 청사과 초록)과 점토 색(`Clay Color`, 기본 흰색)은 머티리얼 Inspector에서 바꿀 수 있다.
- Quest 성능을 위해 단일 Forward 패스, 텍스처 없이 절차적으로만 계산한다. 픽셀마다 Voronoi를 두 번 계산하므로 Quest에서 성능을 확인해야 한다.

### 셰이더 값과 `WaxBallShellVisual`

`WaxBallShellVisual`이 `MaterialPropertyBlock`으로 매 프레임 넘기는 값:

| 값 | 의미 |
|---|---|
| `_CrackAmount` | 균열 진행도(0~1). Cracked에서 grip에 따라 커지고, 한 번 간 값은 되돌아가지 않음(리셋에서만 0) |
| `_BrokenAmount` | 파열 정도(0~1). Broken 진입 시 약 0.1초 만에 1로 올라가 "파삭" 하고 갈라짐 |
| `_Fragment` | 판이 쪼개진 정도(0~1). 쥐는 힘과 `MixProgress`에 따라 늘고 줄지 않음 |
| `_MixAmount` | `MixProgress`(Mixed 연출용, 셰이더는 아직 쓰지 않음) |
| `_DentDir`, `_DentAmount` | 오목하게 눌리는 방향(오브젝트 공간)과 정도 |
| `_Crack1A/B`, `_Crack2A/B` | 균열 두 줄의 모양 파라미터 |

균열 파라미터(`A = 시작점 xyz + 길이 배율`, `B = 방향 각도, 휘어짐, 시드, 폭 배율`)는 **리셋(Idle)마다 줄마다 따로 무작위로 뽑는다.** 두 줄은 방향, 길이, 휘어짐, 폭이 서로 달라야 한다.
- 기본 시작점은 카메라 쪽(`randomStartForward`) 원뿔(`randomStartConeAngle`) 안에서 뽑고, 두 시작점은 `minStartSeparation` 이상 떨어진다.
- 나중에 손가락 위치로 바꾸려면 `SetCrackStarts(방향1, 방향2)`(공 로컬 방향)를 호출한다. 호출하면 리셋해도 유지되고, `ClearCrackStarts()`로 해제한다. 오목하게 눌리는 중심도 첫 번째 시작점을 따른다.

찌그러짐: `maxSquash`(Cracked까지)보다 `brokenSquash`(Broken 이후)가 크다. 껍질이 더 이상 버티지 않기 때문이다.

셰이더 튜닝 값(수치는 눈으로 보고 조정): `Crack Length`, `Gap Width`, `Plate Scale`, `Fine Scale Multiplier`, `Plate Gap Width`, `Clay Bulge`, `Rim Width`, `Rim Brightness`, `Gloss`, `Window Highlight`.

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
- 장비 없이 Slider로 `Idle → … → Mixed → 리셋` 전체 흐름과 Crunched를 확인할 수 있어야 한다. R 키 또는 리셋 버튼으로 리셋한다.
- Unity에서 `Cmd+Z`로 빌더 결과를 되돌리면 UI 자식 오브젝트가 흩어질 수 있다. 되돌리지 말고 메뉴를 다시 실행한다.

---

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
2. 시각 효과 고도화: Idle/Cracked/Broken 구현, Mixed(흰색이 덮음) 구현, Quest 성능 확인
3. Quest 빈 씬 Build And Run 성공 (개발자 모드 해결 후)
4. 스펀지 공을 쥔 상태에서 Quest 핸드트래킹 안정성 테스트 → grip 변환 + 보정
5. 가상 공을 실물 공 위치에 정렬, 균열 시작점을 손가락 위치로 연결
6. 장갑 입력 연결

---

## 8. Claude Code 작업 시 주의 (이 폴더)

- 스크립트 파일이나 클래스 이름을 바꾸지 말 것(.meta GUID와 씬 참조가 끊긴다). 꼭 바꿔야 하면 먼저 확인받는다.
- `.meta` 파일을 지우거나 새로 만들지 말 것.
- 셰이더 속성 이름을 바꾸면 `WaxBallShellVisual`과 머티리얼의 저장된 값이 어긋난다. 함께 고친다.
- 환경 반사, 파편 오브젝트, 파티클은 넣지 않는다(3장 이유 참고).
