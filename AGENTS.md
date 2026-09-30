# AGENTS.md

shinshi 저장소에서 일하는 AI 에이전트와 팀원이 **반드시** 따를 것만 적는다. 나머지는 필요할 때 [docs/ai/README.md](docs/ai/README.md)에서 찾는다.

## 프로젝트

- 저장소 루트가 곧 Unity 프로젝트 루트다 (`Assets/`, `Packages/`, `ProjectSettings/`).
- Unity 6000.4.7f1, URP, Unity Test Framework. 입력은 구 Input Manager(`UnityEngine.Input`, Active Input Handling = Both). 네트워크는 Mirror 96.11.3(`Assets/Mirror/`), 데디케이티드 서버 방식(확정). 서버 구성·호스팅은 `docs/unity/NETWORK_AUTHORITY.md` 7장.
- 게임 기획·기술 설계는 [docs/00-reading-guide.md](docs/00-reading-guide.md)에서 시작한다. 게임플레이 C#과 백엔드는 아직 미구현이다. `Assets/01.Scripts/00.Common/`에는 공통 계약(`Constants`, enum, GameDB 골격)만 있으며, 설계에 적힌 예정 파일·도구를 실제로 존재한다고 가정하지 않는다.

## 반드시 읽을 것

| 언제 | 읽을 것 |
|---|---|
| C# 코드를 쓰거나 고칠 때 | [docs/coding-convention.md](docs/coding-convention.md) - **모든 C# 규칙의 유일한 기준** |
| 씬 · 프리팹 · 에셋 · `.meta`를 만질 때 | [docs/unity/SCENE_AND_ASSET_SAFETY.md](docs/unity/SCENE_AND_ASSET_SAFETY.md) |
| 네트워크 코드(`NetworkBehaviour`, Command, Rpc, SyncVar)를 만질 때 | [docs/unity/NETWORK_AUTHORITY.md](docs/unity/NETWORK_AUTHORITY.md) |
| 커밋 · PR 제목을 쓸 때 | [docs/commit-convention.md](docs/commit-convention.md) - **커밋 메시지 형식의 유일한 기준** |
| "완료"를 보고할 때 | [docs/ai/03_VERIFICATION_PLAYBOOK.md](docs/ai/03_VERIFICATION_PLAYBOOK.md) |

## 지킬 것

- 브랜치는 담당자별 `feature/{팀원이름}`. `main`에 직접 커밋하지 않는다.
- 다른 파트 소유 영역(`Net/`, `98.Servers/`, `01.Network/`, `00.Common/`은 {팀장}, 경로별 세부는 `.github/CODEOWNERS`)을 고치기 전에 담당자에게 확인한다. 서버 전용 코드는 `98.Servers/`(`Shinshi.Server` 어셈블리)에 두고 `NetworkBehaviour`를 partial로 나누지 않는다.
- 에셋은 `.meta`와 함께 커밋하고, 이동 · 이름 변경은 Unity 에디터에서 한다.
- 비밀값(키, 토큰, 비밀번호)은 커밋하지 않는다.
- 커밋 메시지는 `<타입>: <무엇>` 형식(`feat`/`fix`/`refactor`/`docs`/`chore`)으로 쓰고, 커밋 하나에 한 가지 변경만 담는다. 세부는 [커밋 컨벤션](docs/commit-convention.md). PR에는 변경 내용, 관련 이슈, 실제로 한 검증, 화면 변경 시 스크린샷을 적고, 못 한 검증은 못 했다고 쓴다.
- 개인 기록은 Git에서 제외된 아래 두 폴더에만 쓰고 커밋하지 않는다. `{날짜}`는 처음 쓴 날(`YYYY-MM-DD`)이고, 이어지는 작업이면 새로 만들지 않고 원본을 갱신한다. 현재 작업 색인은 아직 두지 않는다(미정).
  - 내 작업의 세션 인수인계: `.claude/docs/{날짜}/{내용}.md`
  - 다른 담당자가 고쳐야 할 내용을 넘기는 전달 문서: `.claude/handover/{날짜}/{내용}.md`
- 커밋 · PR · 이슈 · 주석 · 인수인계는 도구별 해당 스킬 절차를 따른다. Codex는 `.agents/skills/`, Claude Code는 `.claude/skills/`를 사용한다. 스킬 목록과 호출 예시는 [AI 작업 문서](docs/ai/README.md#도구별-스킬)에 있다.
