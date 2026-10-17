# AGENTS.md

shinshi 저장소에서 일하는 AI 에이전트와 팀원이 **반드시** 따를 것만 적는다. 나머지는 필요할 때 [docs/ai/README.md](docs/ai/README.md)에서 찾는다.

## 프로젝트

- 저장소 루트가 곧 Unity 프로젝트 루트다 (`Assets/`, `Packages/`, `ProjectSettings/`).
- Unity 6000.4.7f1, URP, Unity Test Framework. 입력은 구 Input Manager(`UnityEngine.Input`, Active Input Handling = Both). 네트워크는 Mirror 96.11.3(`Assets/Mirror/`), 데디케이티드 서버 방식(확정). 서버 구성·호스팅은 `docs/unity/NETWORK_AUTHORITY.md` 7장.
- 게임 기획·기술 설계는 [docs/00-reading-guide.md](docs/00-reading-guide.md)에서 시작한다. 작업 트리에는 서버 M-A의 인증·재접속·전투·세션·정산 코드가 `01.Network/`·`02.Combat/`·`98.Servers/`(마을 서버는 `98.Servers/05.Town/`)에 있고, `00.Common/`에는 공통 계약·GameDB 기반과 엔진 비의존 DTO(`Contracts/`)가 있다. `Backend/`에는 ASP.NET Core .NET 10·Npgsql+Dapper·SQL 마이그레이션 `0001`~`0019`(`0017`은 캐릭터 경험치, `0018`은 장비 옵션 재설정, `0019`는 소셜(친구·길드·전달함), `0008`은 운영 어드민, `0011`~`0013`은 성장·출정 계약·생활, `0014`는 공지 분류, `0015`는 유저 경매장·1:1 거래, `0016`은 직업 ID 0~5 제약), `deploy/`·`tools/`에는 배포·M2 준비 파일이 있다. 제공된 검증 결과는 Unity EditMode 2241건·실제 PostgreSQL 18(Docker) 백엔드 930건 통과(2026-10-03)와 백엔드 1388건 통과(실제 PostgreSQL 18, 2026-10-08, 병렬 2, 구현 실행 보고), 스킬창·소셜 구현 뒤 Unity EditMode 6427건 중 6426건 통과(1건은 기존 제외)와 실제 PostgreSQL 백엔드 1607건 통과(2026-10-16)이며, 실제 서버 빌드·M2/M3·IL2CPP·배포와 스킬창·소셜의 게임 실행·로컬 스택·다인 접속은 **구현됨·실행 미검증**이다. 전체 게임의 구현 상태는 [로드맵](docs/05-implementation-roadmap.md)을 확인하고, 설계에 적힌 예정 파일·도구를 실제로 존재한다고 가정하지 않는다.

## 반드시 읽을 것

| 언제 | 읽을 것 |
|---|---|
| C# 코드를 쓰거나 고칠 때 | [docs/coding-convention.md](docs/coding-convention.md) - **모든 C# 규칙의 유일한 기준** |
| 씬 · 프리팹 · 에셋 · `.meta`를 만질 때 | [docs/unity/SCENE_AND_ASSET_SAFETY.md](docs/unity/SCENE_AND_ASSET_SAFETY.md) |
| 3D 모델 · 재질 · 목표 원화를 제작하거나 가공할 때 | [docs/unity/ART_ASSET_PRODUCTION_WORKFLOW.md](docs/unity/ART_ASSET_PRODUCTION_WORKFLOW.md) - **실사에 가까운 3D 스타일화 판타지·AO/PBR·LOD·프리팹 기본 과정** |
| 네트워크 코드(`NetworkBehaviour`, Command, Rpc, SyncVar)를 만질 때 | [docs/unity/NETWORK_AUTHORITY.md](docs/unity/NETWORK_AUTHORITY.md) |
| 커밋 · PR 제목을 쓸 때 | [docs/commit-convention.md](docs/commit-convention.md) - **커밋 메시지 형식의 유일한 기준** |
| "완료"를 보고할 때 | [docs/ai/03_VERIFICATION_PLAYBOOK.md](docs/ai/03_VERIFICATION_PLAYBOOK.md) |

## 지킬 것

- 한국어로, 결론부터 답한다.
- 사실, 추정, 미검증으로 구분한다.
- 코드나 설정을 읽어 확인할 수 있는것을 질문으로 되돌리지 않는다.
- 보고 형식은 [docs/ai/03_VERIFICATION_PLAYBOOK.md](docs/ai/03_VERIFICATION_PLAYBOOK.md)를 따른다.
- 브랜치는 담당자별 `feature/{팀원이름}`. 통합 브랜치는 `develop`이며 PR은 `develop`으로 올린다. `develop`과 `main`에 직접 커밋하지 않는다.
- 다른 파트 소유 영역(`Net/`, `98.Servers/`, `01.Network/`, `00.Common/`은 {팀장}, 경로별 세부는 `.github/CODEOWNERS`)을 고치기 전에 담당자에게 확인한다. 서버 전용 코드는 `98.Servers/`(`Shinshi.Server` 어셈블리)에 두고 `NetworkBehaviour`를 partial로 나누지 않는다.
- 에셋은 `.meta`와 함께 커밋하고, 이동 · 이름 변경은 Unity 에디터에서 한다.
- 모델 작업은 별도 요청 없이 원화 대조·실사에 가까운 3D 스타일화 판타지 재질·실제 AO와 노멀/거칠기 처리·LOD·용도별 충돌 설정·`Assets/02.Prefab/` 프리팹 생성·참조/시각 검증까지 진행한다. AO는 실제 베이크나 기존 검증된 버텍스 AO를 사용하며, 재질 특성상 생략하거나 다른 표현을 쓸 때는 이유를 기록한다. 세부와 셰이더별 차이는 [아트 제작 기본 과정](docs/unity/ART_ASSET_PRODUCTION_WORKFLOW.md)을 따른다.
- 개인 디자인 시안·AI 프롬프트·비교 설계 노트와 모델별 상세 디자인·제작 기록·렌더는 Git에서 제외된 `.claude/docs/{처음 쓴 날짜}/`에 보관한다. 공용 문서에는 재사용할 제작 규칙, 배치용 에셋 경로·사용 조건과 검증 범위 요약을 남긴다.
- 비밀값(키, 토큰, 비밀번호)은 커밋하지 않는다.
- 커밋 메시지는 `<타입>: <무엇>` 형식(`feat`/`fix`/`refactor`/`docs`/`chore`)으로 쓰고, 커밋 하나에 한 가지 변경만 담는다. 세부는 [커밋 컨벤션](docs/commit-convention.md). PR에는 변경 내용, 관련 이슈, 실제로 한 검증, 화면 변경 시 스크린샷을 적고, 못 한 검증은 못 했다고 쓴다.
- 개인 기록은 Git에서 제외된 아래 두 폴더에만 쓰고 커밋하지 않는다. `{날짜}`는 처음 쓴 날(`YYYY-MM-DD`)이고, 이어지는 작업이면 새로 만들지 않고 원본을 갱신한다. 현재 작업 색인은 아직 두지 않는다(미정).
  - 내 작업의 세션 인수인계: `.claude/docs/{날짜}/{내용}.md`
  - 다른 담당자가 고쳐야 할 내용을 넘기는 전달 문서: `.claude/handover/{날짜}/{내용}.md`
- 커밋 · PR · 이슈 · 주석 · 인수인계는 도구별 해당 스킬 절차를 따른다. Codex는 `.agents/skills/`, Claude Code는 `.claude/skills/`를 사용한다. 스킬 목록과 호출 예시는 [AI 작업 문서](docs/ai/README.md#도구별-스킬)에 있다.
