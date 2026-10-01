# 프로젝트 지식 맵

2026-10-01 shinshi 작업 트리 기준. 실제 코드·SO·설정에서 현재 동작을 확인하고, 게임 기획의 목표와 구분한다. 사용자의 최신 명시 지시가 권한과 요구사항을 정한다.

## 기능별 진입점

경로는 저장소 루트(Unity 프로젝트 루트) 기준이고, 코드 경로는 `Assets/01.Scripts/` 기준이다. 게임플레이 C#은 아직 없고 `00.Common/`에 공통 계약(`Constants`, enum, GameDB 골격)만 있으므로, 나머지 코드 진입점은 [코딩 컨벤션](../coding-convention.md)에 정한 예정 위치다. 일부 아트와 별도 에디터 스크립트는 존재한다.

| 찾는 것 | 공유 문서 | 실제 진입점 | 확인할 것 |
|---|---|---|---|
| 게임 기획 (장르·규칙·스코프) | [기획서](../02-game-design.md), [시스템 개요](../01-system-design.md) | `docs/design/` 상세 부록, [컨셉아트 기록](../design/concept-art.md) | 확정 규칙과 위임 초기 수치, 월백 2페이즈 입력 대기 |
| 기술 설계 | [기술설계서](../03-technical-design.md), [아키텍처](../04-architecture.md) | 제안 모듈·API·스키마 | 전투 1~4인, 전용 서버·공유 마을·ASP.NET Core·PostgreSQL; 구현 상태 구분 |
| 구현 현황 | [구현 로드맵](../05-implementation-roadmap.md) | `Assets/01.Scripts/` (폴더·일부 아트·공통 계약 존재, 게임플레이 C# 미구현) | 코드 존재와 검증 완료를 구분 |
| 클라이언트 API | [기술설계서](../03-technical-design.md) | 도메인별 요청·조회 창구 (설계, 아직 미구현) | 원본 상태는 서버, UI는 요청/조회 |
| 첫 실행·개발 환경 | [처음 프로젝트를 열 때](../unity/UNITY_START_HERE.md) | `ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json` | Unity 6000.4.7f1, 패키지 버전 |
| C# 스타일·폴더·네임스페이스 | [코딩 컨벤션](../coding-convention.md) | `Assets/01.Scripts/`, 루트 네임스페이스 `Shinshi` | 도메인 폴더 번호는 파트 확정 시 조정 |
| 네트워크 권위 (Mirror) | [네트워크 권위](../unity/NETWORK_AUTHORITY.md) | `Assets/01.Scripts/01.Network/` (Mirror 96.11.3 설치됨) | 서버 판정, Command/Rpc 매핑 |
| 서버/클라이언트 경계·소유 | [서버·클라이언트 협업](../unity/SERVER_CLIENT_WORKFLOW.md) | 도메인 `Rules/`·`Net/`·`View/`와 `Assets/01.Scripts/98.Servers/`(`Shinshi.Server` 어셈블리) | `Net/`·`98.Servers/`는 {팀장} 소유(AI·웨이브 하위는 공동), 서버 코드는 클라 빌드에서 제외 |
| 멀티플레이 테스트 | [멀티플레이 테스트](04_MULTIPLAYER_TEST_PROTOCOL.md) | — | 테스트 등급 M0~M3, 실제 경로·빌드·조건 기록 |
| 검증 증거 | [검증 플레이북](03_VERIFICATION_PLAYBOOK.md) | `Assets/Tests/EditMode/`, `Assets/Tests/PlayMode/` (폴더만 있고 테스트 없음) | 증거 등급과 상태 어휘 |
| 씬·에셋·`.meta` | [씬 · 에셋 안전](../unity/SCENE_AND_ASSET_SAFETY.md) | `Assets/00.Scenes/SampleScene.unity` | 씬 소유, `.meta` 동반 커밋 |
| 외부 에셋·라이선스·설치 기록 | [에셋 대장](../licenses/ASSET_REGISTRY.md) | `Assets/99.ThirdParty/` | 분류, 버전, 출처, Git 제외, 경로 보정 |
| 3D 모델 제작·공용 재사용 | [모델 제작 목록](../design/model-production-list.md), [컨셉아트 기록](../design/concept-art.md) | 제작 계획, 기존 벚꽃나무는 `Assets/01.Scripts/05.Art/Environment/CherryTree/` | 원본 수와 배치 수 구분, 기존 모델 우선 검토, Meshy CLI → Blender 흐름 |
| 아트 재질·AO·프리팹 기본 과정 | [아트 제작 기본 과정](../unity/ART_ASSET_PRODUCTION_WORKFLOW.md) | 모델·재질 `Assets/01.Scripts/05.Art/`, 배치 프리팹 `Assets/02.Prefab/` | AO/PBR 기본 처리, 셰이더별 지원, AI 목표 이미지와 실제 검증 구분 |
| 렌더링 설정 (URP) | — | `Assets/Settings/` (PC · Mobile RP 에셋) | 공용 설정 변경은 팀 영향 |
| 폴리지 셰이더·재질 | [버텍스 컬러 폴리지 셰이더](../unity/VERTEX_COLOR_FOLIAGE_SHADER.md) | `Assets/01.Scripts/05.Art/Shaders/`, `Assets/01.Scripts/05.Art/Environment/CherryTree/` | 버텍스 컬러 AO, 알파 클립, 밉맵 커버리지 보존 |
| 입력 | [코딩 컨벤션](../coding-convention.md) 23장 | `ProjectSettings/InputManager.asset` | 구 Input Manager(`UnityEngine.Input`), Active Input Handling = Both |
| 밸런스 데이터 | [밸런스 데이터 작업](../unity/BALANCE_DATA_WORKFLOW.md) | `Assets/09.Data/` (GameDB 루트, 플레이어·세션 설정) | 수치는 ScriptableObject에 둔다 |
| 성능·수명 패턴 | [엔지니어링 패턴](../unity/UNITY_ENGINEERING_PATTERNS.md) | — | 실제 문제가 생겼을 때 적용 |
| 구조·디자인 패턴 | [디자인 패턴](../unity/DESIGN_PATTERNS.md) | — | 도입 판단 기준 |
| 도입 기능 계획·개발 도구 | [도입 기능 계획](../unity/FEATURE_ADOPTION_PLAN.md) | `Assets/01.Scripts/99.Dev/` (`.gitkeep`만 있음) | 제안 상태, 권위 위치별 패널 분리, 외부 체험판 빌드 제거 |
| 외부 AI에 넣을 에셋·데이터 | [에셋 · 데이터 정책](12_ASSET_AND_DATA_POLICY.md) | — | 구매 에셋·비공개 데이터 취급 |
| AI 작업 운영 전체 | [AI 작업 문서](README.md), 루트 [`AGENTS.md`](../../AGENTS.md) | — | 승인 경계, 인수인계, 브랜치 `feature/{팀원이름}` |

## 확인 기준

- 코드 존재만으로 기능/시각/네트워크 검증이 끝난 것은 아니다. [테스트 절차](04_MULTIPLAYER_TEST_PROTOCOL.md)에 실제 경로·빌드·조건을 기록한다.
- 역할과 폴더 소유자는 팀 합의와 [서버·클라이언트 협업](../unity/SERVER_CLIENT_WORKFLOW.md)을 확인한다. `.github/CODEOWNERS`에 경로별 담당(@aurenixs, @oname36-creator, @kjw1237)이 지정되어 있다.
- 구현된 밸런스 값은 `Assets/09.Data/` 원본에서 읽는다. 지금은 `PlayerConfig`, `SessionConfig`만 있으므로 나머지 설계 수치는 [기획서](../02-game-design.md)와 연결 부록에서 확인한다.
- 공유되지 않는 개인 로그 대신 재현 가능한 절차와 코드 근거를 쓴다. [문서 분류](../DOCUMENTATION_POLICY.md)를 따른다.
- 실행 환경, 배포 버전, 테스트 수, 개인 계정 상태는 영구 상수로 취급하지 않는다.
- 새 공유 문서를 만들면 이 표의 해당 행을 갱신한다. `(작성 예정)` 행은 문서가 생기면 링크로 바꾼다.
