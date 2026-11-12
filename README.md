# wakbbu (VR/AR 텀프로젝트)

햅틱 AR 왁뿌볼: 실물 스펀지볼 + WEART 햅틱 장갑 + Meta Quest 패스스루(AR) + 파괴음으로 왁뿌볼 깨는 촉감을 재현하는 프로젝트

## 개발 환경 (팀 전원 동일하게)
- **Unity 2022.3.62f3 LTS**
- 모듈: Android Build Support + OpenJDK + Android SDK & NDK Tools
- 빌드 플랫폼: **Android** (Meta Quest 3, 단독 실행)
- Meta XR All-in-One SDK **201.0.0** (Packages/manifest.json에 들어 있음, 처음 열 때 자동 설치 / 업데이트하지 말 것)
- XR Plug-in: Oculus

## 시작하기
1. GitHub Desktop → Clone repository
2. Unity Hub → 프로젝트 → 추가 → 받은 폴더 선택 (처음 열 때 몇 분 걸림)
3. Assets/Scenes/SampleScene 열기 (Camera Rig + Passthrough + 테스트 큐브)

## 협업 규칙
- 작업 시작 전에 항상 **Fetch / Pull** 먼저
- **각자 자기 씬**에서 작업 (예: Assets/Scenes/AR_Test, Haptic_Test). 같은 씬 동시 수정 금지
- 작업 끝나면 바로 Commit + Push
- 빌드 결과물(Builds/, *.apk)은 올리지 않음
