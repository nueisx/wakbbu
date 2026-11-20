# CLAUDE.md

이 파일은 팀 전체(Unity, 햅틱, Interaction 담당)와 Claude Code가 이 레포에서 작업할 때 가장 먼저 읽는 프로젝트 안내서다.
문서·주석·커밋 메시지는 한국어로 쓴다. 코드 식별자(클래스, 변수, 파일명)는 영어로 쓴다.

- **햅틱·소리 담당**은 이 파일만 읽어도 `StateChanged` / `Crunched` 이벤트를 연결할 수 있다(5장).
- 셰이더, 시각 효과 세부, 테스트 씬 빌더 같은 **Unity 구현 세부는 `Assets/Scripts/WaxBall/CLAUDE.md`** 에 있다.

---

## 1. 프로젝트 한 줄 요약

**AR 환경(Meta Quest 3 패스스루)에서 "왁뿌볼"을 손으로 쥐어 부수는 촉감을 재현하는 햅틱 인터랙션 시스템.**

사용자가 가상의 왁뿌볼을 쥐면 `Idle → Pressed → Cracked → Broken → Mixed` 순서로 상태가 바뀌고,
상태마다 **AR 시각 효과 + 햅틱 피드백 + 소리**가 함께 바뀐다.

- 주안점: Fun + Interaction + Physical Realism
- 과제 카테고리: 3번 New VR/AR Interaction Methodology

---

## 2. 왁뿌볼이란 (도메인 정의)

**왁뿌볼 = "왁스 뿌시기 볼"의 줄임말.** 2026년 국내 SNS(숏폼/ASMR)를 중심으로 2030까지 유행한 촉감 장난감이다.

### 물리적 구조
| 층 | 재질 | 특징 |
|---|---|---|
| 투명 고무 케이스 | 가장 바깥의 **얇고 말랑한 투명 고무**(몇 mm 두께, 공기 없이 속이 꽉 차 있음). 딱딱한 공이 아니다 | 모든 단계에서 반질반질한 광택을 만든다. 깨진 왁스 파편과 점토를 안에 가둔다. **파편은 밖으로 튀지 않고 손에 점토가 묻지 않는다** |
| 왁스 껍질 | 색깔 있는 얇고 납작한 왁스 판(파라핀 등 석유계 왁스) | 딱딱하고 잘 부서짐, 바삭한 소리 |
| 속 | **흰 점토**(말랑한 클레이·점토·스퀴시·점액) | 물컹하고 쫀득함 |

### 참고 이미지
실제 왁뿌볼 사진과 상태별 특징(색, 판 크기, 틈, 광택, 모양 변화), 투명 고무 케이스 사진(`docs/references/casing.png`) 설명은 **`docs/references/README.md`** 에 정리했다.
시각 효과는 이 이미지를 기준으로 만든다. 소리·햅틱 설계 때도 상태별 모습을 참고한다.

### 사용 경험 (재현 목표)
1. 처음 쥐면 단단한 왁스 껍질이 **저항**한다.
2. 힘을 더 주면 껍질에 금이 가고, 어느 순간 **"파삭" 하고 깨진다.** 이때 바삭한 소리가 나고 저항이 갑자기 빠진다. 왁스는 **투명 고무 케이스 안에서** 깨지므로 **파편은 밖으로 튀지 않는다**(AR에서도 파편·파티클을 밖으로 날리지 않는다).
3. 깨진 뒤에는 속의 **물컹·쫀득한 말랑이**가 느껴진다.
4. 계속 주무르면 왁스 조각이 점토에 **섞인다.**
5. **실물은 일회성이다.** 한 번 깨진 껍질은 복원되지 않는다.
   - 우리 시스템도 손을 놓는 것만으로는 Cracked 이후 상태가 되돌아가지 않는다(실물 감각 유지).
   - 대신 **재생성(리셋) 버튼으로 언제든, 무제한으로 새 왁뿌볼을 만들 수 있다.** 이것이 가상 왁뿌볼의 장점이다.

### 우리 시스템에서 각 느낌을 담당하는 것
| 느낌 | 담당 |
|---|---|
| 손에 뭔가 쥐고 있다는 감각, 말랑·물컹한 촉감 | **실물 스펀지 공** (패시브 햅틱, 구현 불필요) |
| 단단한 왁스 껍질의 저항 | 햅틱 장갑 (force feedback 등) |
| "파삭" 깨지는 순간 (저항이 갑자기 빠짐 + 짧은 진동) | 햅틱 장갑 |
| 깨진 뒤 주무를 때 바삭한 알갱이 감각 | 햅틱 장갑 손가락 끝 진동 + 오디오 (`Crunched` 이벤트) |
| 바삭·와자작 소리 | 오디오 |
| 균열(흰 점토가 비집고 나옴), 파열(큰 판으로 갈라짐), 섞임(흰색이 덮고 작은 판 조각만 박힘) 모습 | AR 시각 효과 (참고 이미지 기준, 파편이 튀는 연출은 쓰지 않는다) |

참고 사항:
- 얼리면 껍질이 더 단단하고 바삭해진다(난이도와 파열감 조절 아이디어).
- 인기의 핵심은 **촉감 + 바삭한 소리 + 즉각적인 통제감**이다. 그래서 소리의 비중이 크다.
- 코드 영문 표기는 `WaxBall`이다. "왁스볼"이라는 한국어 표기는 쓰지 않고 항상 **"왁뿌볼"**로 쓴다.

---

## 3. 실물 스펀지 공 (패시브 햅틱)

사용자는 **실제로 스펀지 공(말랑한 공)을 손에 쥔다.** 가상 왁뿌볼은 그 위에 AR로 겹쳐 보인다.

**이렇게 하는 이유**
- 속 말랑이의 물컹한 촉감을 장갑으로 만들 필요가 없다. 실물이 그대로 준다.
- 실제로 손에 뭔가를 쥐어야 "쥐어서 부순다"는 동작과 감각이 자연스럽게 재현된다.
- 그래서 장갑은 **왁스 껍질의 단단함과 깨지는 순간**에만 집중하면 된다.

**팀이 알아야 할 점**
- 가상 왁뿌볼은 **실물 공과 같은 위치·크기**로 보여야 한다. 실물 공 지름을 재서 가상 공 크기를 맞춘다.
- **grip 값**은 스펀지 공을 쥔 손가락이 얼마나 굽혀졌는지로 계산한다. 공을 쥔 상태에서는 굽힘 범위가 맨손보다 좁으므로, 최소(살짝 잡음)~최대(꽉 쥠) 값을 보정(calibration)해서 0~1로 맞춘다.
- 실물 공이 손가락을 가려서 **Quest 핸드트래킹이 불안정할 수 있다.** 장갑 자체 tracking을 쓸지 결정할 때 이 점을 반드시 테스트한다.
- 리셋해도 실물 공은 그대로이므로, 리셋 = 가상 왁뿌볼만 새로 만드는 것이다.
- 스펀지 공은 위생과 내구성을 생각해 여러 개를 준비하고, 같은 지름·같은 경도로 통일한다(실험 조건 통제).

---

## 4. 상태 흐름

입력은 **grip(0~1, 쥐는 정도) 하나**로 추상화한다. 장비(Slider / Quest 핸드트래킹 / 햅틱 장갑)와 무관하게 `SetGrip(float)`으로 값을 넣는다.

| 상태 | 의미 | 진입 조건 (기본값, 튜닝 대상) | grip을 줄였을 때 |
|---|---|---|---|
| `Idle` | 아무 힘 없음 | 시작 / `ResetBall()` | — |
| `Pressed` | 껍질이 눌리며 저항 | grip ≥ 0.15 | grip < 0.15이면 Idle로 복귀 |
| `Cracked` | 껍질에 균열 | grip ≥ 0.55 | 유지 (리셋으로만 복귀) |
| `Broken` | 껍질 파열, 속 점토 노출 | grip ≥ 0.85 | 유지 (리셋으로만 복귀) |
| `Mixed` | 왁스 조각이 점토와 섞임 | Broken 이후 grip 변화량 누적 ≥ 3.0 | 유지 (리셋으로만 복귀) |

- grip이 한 번에 크게 뛰어도 상태 이벤트는 **건너뛰지 않고 한 단계씩** 순서대로 발생한다(예: Idle → Pressed → Cracked → Broken).
- 임계값은 `WaxBallStateMachine`의 `[SerializeField]` 값이고, Interaction 담당이 정한다(`pressThreshold`, `crackThreshold`, `breakThreshold`, `kneadAmountToMix`).

**리셋 / 재생성**
- `ResetBall()`은 **어느 상태에서든** 호출할 수 있고, 횟수 제한이 없다. 호출하면 grip, 섞임 누적값, 시각 효과를 초기화하고 `Idle`로 돌아간다.
- AR 공간에 **재생성 버튼**(손으로 누르는 3D 버튼 또는 UI)을 두고 `ResetBall()`에 연결한다. 에디터 테스트용으로 키보드 단축키(R)도 둔다.
- 리셋도 상태 변경이라 `StateChanged(prev, Idle)`가 발생한다. 햅틱·소리 모듈은 `next == Idle && prev >= Cracked`일 때 재생성 연출(햅틱 정지, 효과음)을 처리한다.
- 같은 `Idle`이라도 `Pressed → Idle`(손을 살짝 풀었을 때)은 리셋이 아니다. `prev`로 구분한다.

---

## 5. 이벤트 사용법 (햅틱·소리 담당용)

시각·햅틱·소리 모듈은 **이벤트를 구독만 한다.** 상태 판정 로직은 `WaxBallStateMachine`(`Assets/Scripts/WaxBall/`)에만 있다.

### 이벤트 목록
| 이벤트 | 언제 | 값 |
|---|---|---|
| `StateChanged(prev, next)` | 상태가 바뀔 때마다 (리셋 포함) | 이전·다음 `WaxBallState` |
| `Crunched(finger, strength)` | **Broken 상태에서** 손가락으로 주무를 때, 그 손가락의 세기 변화량이 `crunchGripStep`(기본 0.2)만큼 쌓일 때마다 | 손가락 번호 0~4, 세기 0~1 (빠르게 주무를수록 큼) |

- C#: `event Action<WaxBallState, WaxBallState> StateChanged`, `event Action<int, float> Crunched`
- Inspector: `UnityEvent<WaxBallState> onStateChanged`, `UnityEvent<int, float> onCrunched`
- `finger`는 `SetContact`에 넣은 손가락 번호와 같다. 그 손가락에만 진동을 주면 된다(손가락 번호와 실제 손가락의 대응은 입력 어댑터가 정한다).
- `Crunched`는 Broken에서만 발생한다. Mixed가 된 뒤에는 발생하지 않는다.
- 연속적인 값은 이벤트가 아니라 프로퍼티를 매 프레임 읽는다: `Grip`(0~1, 쥐는 정도), `MixProgress`(0~1, 섞임 진행도), `State`(현재 상태).

### 연결 예시 (`Assets/Scripts/Haptics/`에 둔다)
```csharp
using UnityEngine;
using WaxBall;

// 장비 SDK(WEART, SenseGlove)를 호출하는 코드는 이런 어댑터에만 둔다. WaxBall/ 폴더에는 넣지 않는다.
public class WaxBallHapticsAdapter : MonoBehaviour
{
    [SerializeField] private WaxBallStateMachine stateMachine;

    private void OnEnable()
    {
        stateMachine.StateChanged += OnStateChanged;
        stateMachine.Crunched += OnCrunched;
    }

    private void OnDisable()
    {
        stateMachine.StateChanged -= OnStateChanged;
        stateMachine.Crunched -= OnCrunched;
    }

    private void OnStateChanged(WaxBallState prev, WaxBallState next)
    {
        switch (next)
        {
            case WaxBallState.Pressed:
                // 껍질 저항 시작 (force feedback)
                break;
            case WaxBallState.Broken:
                // 저항 해제 + 짧은 진동 + "파삭" 소리, 이후 장갑 출력은 끄고 스펀지 공 촉감에 맡긴다
                break;
            case WaxBallState.Idle when prev >= WaxBallState.Cracked:
                // 리셋: 햅틱 정지, 재생성 효과음
                break;
        }
    }

    private void OnCrunched(int finger, float strength)
    {
        // finger 0~4: 해당 손가락 끝에만 진동을 준다. strength 0~1: 진동 세기와 바삭 소리 크기에 매핑한다.
    }

    private void Update()
    {
        // 껍질 저항 세기 등 연속값은 매 프레임 읽는다.
        float grip = stateMachine.Grip;
    }
}
```
- 장갑 tracking이나 핸드트래킹으로 손가락을 읽는다면 입력 어댑터에서 `WaxBallContacts`(공 오브젝트에 있음)에 손가락마다 접촉점을 넣는다:
  `contacts.SetContact(finger, 손가락 끝 월드 위치, 세기 0~1)`, 떨어지면 `contacts.ClearContact(finger)`.
  전체 grip은 접촉점 세기를 합쳐서(기본 최댓값, Inspector에서 평균으로 변경 가능) `SetGrip`으로 들어가며, `Drive Grip`을 켜면 자동이다.
  손가락별 정보 없이 grip 하나만 넣고 싶다면 `stateMachine.SetGrip(grip)`을 직접 부른다.
- 에디터에서 장비 없이 테스트: Unity 담당의 `Dev_WaxBall` 씬에서 Slider로 grip을 올리며 이벤트를 확인한다(세부는 `Assets/Scripts/WaxBall/CLAUDE.md`).

---

## 6. 개발 환경 요약과 Git 규칙

- Unity **2022.3.62f3** (다른 버전으로 열지 말 것), URP, Meta XR SDK, Build Target **Android**, 실행 기기 Meta Quest 3(패스스루 AR), 개발 OS macOS.
- 패키지·빌드·씬 구성 같은 Unity 세부는 `Assets/Scripts/WaxBall/CLAUDE.md`를 본다.

```
Assets/
  Scripts/
    WaxBall/        # 상태 머신, 시각 효과 (Unity/AR 담당)
    Haptics/        # 장갑 SDK 연동, 햅틱 프리셋 (햅틱 담당)
    Interaction/    # 입력 변환, 튜닝 도구
  Scenes/
    Dev_WaxBall.unity   # Unity 담당 개인 테스트 씬
  Audio/            # 파열·바삭 소리
docs/
  references/       # 실제 왁뿌볼 참고 이미지와 정리(README.md)
Packages/
ProjectSettings/
```

- Git에 포함: `Assets/`, `Packages/`, `ProjectSettings/`, `docs/`, **모든 `.meta` 파일**
- Git에서 제외: `Library/`, `Temp/`, `Logs/`, `obj/`, `Build/`, `*.apk`
- 씬 파일(.unity)은 머지 충돌이 잦다. 공용 씬은 한 사람만 수정하고, 실험은 개인 `Dev_*.unity` 씬에서 한다.
- 작업 흐름: `git pull` → 작업 → `git add` → `git commit` → `git push`. 기능 단위로 브랜치를 쓴다(`feature/...`).

### Claude Code 작업 시 주의
- 스크립트 파일이나 클래스 이름을 바꾸지 말 것. 바꾸면 씬·프리팹의 컴포넌트 참조가 끊긴다(.meta GUID 문제). 꼭 바꿔야 하면 먼저 확인받는다.
- `.meta` 파일을 지우거나 새로 만들지 말 것. Unity가 관리한다.
- `Packages/manifest.json`의 버전을 임의로 올리지 말 것.
- 장비 SDK(WEART, SenseGlove)에 직접 의존하는 코드를 `WaxBall/` 폴더에 넣지 말 것. 장비 의존 코드는 어댑터로 분리한다.

---

## 7. 역할 분담

### 1번 — Unity / AR 구현 (이 레포 Unity 파트의 주 담당자)
**책임 범위**
- Unity 프로젝트 구조와 씬 관리
- Quest 3 AR 환경: 패스스루, XR 리그, 빌드·배포
- 가상 왁뿌볼 오브젝트(모델, 머티리얼, 콜라이더) — **실물 스펀지 공과 크기·위치 일치**
- 실물 공 정렬 방식 구현(손 기준 고정 / 시작 위치 보정 / 실물 추적 중 택1)
- **상태 머신** `WaxBallStateMachine`과 `StateChanged` / `Crunched` 이벤트
- **재생성(리셋) 버튼**: 언제든 무제한으로 새 왁뿌볼 생성
- 손 접촉 / 눌림 / 파열 판정 로직
- 상태별 **시각 효과**(`docs/references/README.md` 기준)
- 입력 어댑터: Slider → Quest 핸드트래킹 → (확정 후) 장갑 입력을 grip 0~1로 변환해 `SetGrip()`에 연결
- **grip 보정(calibration)**: 스펀지 공을 살짝 잡았을 때 / 꽉 쥐었을 때 값을 기록해 0~1로 매핑

**산출물이 지켜야 할 것**
- 다른 팀원은 `StateChanged` / `Crunched` 이벤트와 `Grip` / `MixProgress` 값만으로 작업할 수 있어야 한다.
- 장비가 없어도 에디터에서 Slider만으로 전체 흐름을 테스트할 수 있어야 한다.

(작업 우선순위와 구현 세부는 `Assets/Scripts/WaxBall/CLAUDE.md`)

### 2번 — 햅틱 담당
- WEART TouchDIVER Pro / SenseGlove Nova 2 SDK 연동
- 손가락 입력값 읽기(장갑 tracking 사용 시 grip 값 제공)
- force / vibration / texture 출력 테스트
- 상태별 햅틱 프리셋: 왁스 껍질의 단단한 저항 → 파열 순간 저항 해제와 짧은 진동 → 이후엔 장갑 출력을 끄고 스펀지 공 촉감에 맡김
- Broken에서 주무를 때: `Crunched(strength)`로 손가락 끝 진동(WEART) 출력
- 말랑한 촉감은 실물 스펀지 공이 담당하므로 구현하지 않는다
- 스펀지 공을 쥔 상태에서 장갑 착용·force feedback이 제대로 동작하는지 확인
- `StateChanged` / `Crunched` 구독으로 Unity 상태와 연결(5장), 실제 파열감 튜닝
- 작업 위치: `Assets/Scripts/Haptics/`

### 3번 — Interaction / 실험 담당
- 실물 왁뿌볼 특성 분석(파열 힘, 소리, 촉감 변화 녹음·촬영)
- **스펀지 공 선정**: 지름, 경도(눌리는 정도), 재질을 정하고 실험용으로 같은 공을 여러 개 준비
- 상태별 촉각 패턴 설계, **파열 임계값 정의** (`pressThreshold`, `crackThreshold`, `breakThreshold`, `kneadAmountToMix`)와 `Crunched` 간격·세기(`crunchGripStep` 등)
- 소리 설계(바삭·와자작 파열음, 주무르는 소리)
- 사용자 실험 설계, 평가 지표, 데이터 분석, 발표 자료

---

## 8. 장비 후보

- **헤드셋:** Meta Quest 3 (대여 기기)
- **실물 스펀지 공:** 사용자가 실제로 쥐는 공 (사양 미확정)
- **햅틱 장갑 (미확정):**
  - SenseGlove Nova 2: force feedback이 있어 "저항하다 갑자기 깨지는 느낌"에 유리할 가능성이 크다.
  - WEART TouchDIVER Pro: force / texture / thermal

---

## 9. 미확정 사항 (작업 전 확인 필요)

- [ ] 장갑 최종 선택: WEART vs SenseGlove Nova 2
- [ ] 손 추적 방식: Quest 핸드트래킹 vs 장갑 tracking
- [x] 실물 볼 병행 여부 → **스펀지 공을 실제로 쥐는 방식으로 결정** (3장 참고)
- [ ] 스펀지 공 사양(지름, 경도, 재질)
- [ ] 가상 공을 실물 공에 맞추는 방식(손 기준 고정 / 시작 위치 보정 / 실물 추적)
- [ ] 스펀지 공을 쥐었을 때 Quest 핸드트래킹이 쓸 만한지
- [ ] 상태별 실제 임계값, `Crunched` 간격·세기
- [ ] 최종 햅틱 출력 방식
- [ ] Quest Developer Mode 활성화(대여 기기라 primary 계정이 학교/랩 계정일 수 있음 → MR Center·교수님에게 요청)
- [ ] Quest ↔ Unity 실제 빌드 테스트
- [ ] 장갑 ↔ Unity 실제 연동
- [ ] 최종 사용자 실험 조건

확정되면 이 섹션과 관련 섹션을 함께 갱신한다.
