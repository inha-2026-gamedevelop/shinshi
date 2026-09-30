---
name: guided-commit
description: shinshi 변경을 커밋할 때 변경 범위와 코딩 컨벤션을 검토하고, 문서를 동기화해 관심사별 커밋과 메시지를 준비·실행한다. push는 별도 요청 범위다.
---

# 가이드 커밋

사용자가 커밋을 요청할 때 적용한다. 초안 요청과 실행 요청을 구분하고, [AGENTS.md](../../../AGENTS.md)와 [실행 정책](../../../docs/ai/01_AGENT_EXECUTION_POLICY.md)의 승인 범위를 따른다. 이미 승인된 파일 범위·메시지·실행은 다시 확인받지 않는다.

## 변경과 규칙 확인

1. `git status --short`, `git branch --show-current`, `git diff`, `git diff --cached`로 브랜치와 변경을 확인한다. 미추적 파일은 내용도 직접 읽는다. 작업 브랜치는 저장소 지침의 `feature/{팀원이름}`을 따른다. `main`에 직접 커밋하지 않는다.
2. 이번 요청의 파일과 기존 사용자 변경을 구분한다. 새 Unity 에셋의 `.meta`를 확인하고 [씬·에셋 안전](../../../docs/unity/SCENE_AND_ASSET_SAFETY.md)을 따른다.
3. C# 변경분은 [코딩 컨벤션](../../../docs/coding-convention.md)으로 검토한다. 명명, 매직넘버, Allman, using, 규칙·상태·서버·표현 층 분리(`NetworkBehaviour` partial 금지), 주석, null 검사와 가드 절을 확인한다. 명확한 위반은 허용된 변경 범위에서 먼저 고치고 이유를 보고한다. 다른 담당자 영역은 [소유권 확인](../check-code-ownership/SKILL.md)을 적용한다.
4. [검증 플레이북](../../../docs/ai/03_VERIFICATION_PLAYBOOK.md)에 맞는 검증을 수행하고 결과·미실행 이유를 기록한다. 코드가 없는 문서 작업에 런타임 테스트를 했다고 쓰지 않는다.

## 커밋 단위와 문서 동기화

- 커밋 하나에 한 가지 변경을 담는다. 자연스럽게 하나인 작업을 억지로 나누지 않는다.
- 메시지는 [커밋 컨벤션](../../../docs/commit-convention.md)을 따른다. `<타입>: <무엇>` 한국어 제목(타입은 `feat`/`fix`/`refactor`/`docs`/`chore`), 필요할 때만 "왜"를 적는 본문. 없는 타입·스코프 괄호를 만들지 않는다.
- `Co-Authored-By` 트레일러를 넣지 않는다. 도구가 붙였고 push 전이면 amend로 지운다.
- 구현 상태나 팀 계약이 바뀌면 관련 기준 문서를 같은 변경에 맞춘다.

| 변경 | 함께 확인할 문서 |
|---|---|
| 폴더·네임스페이스 | [코딩 컨벤션](../../../docs/coding-convention.md) |
| 서버·클라이언트 계약 | [협업 경계](../../../docs/unity/SERVER_CLIENT_WORKFLOW.md), [네트워크 권위](../../../docs/unity/NETWORK_AUTHORITY.md) |
| 씬·데이터·진입점 | [씬·에셋 안전](../../../docs/unity/SCENE_AND_ASSET_SAFETY.md), [밸런스 작업](../../../docs/unity/BALANCE_DATA_WORKFLOW.md), [지식 맵](../../../docs/ai/07_PROJECT_KNOWLEDGE_MAP.md) |
| 실제 빌드·테스트 명령 | 저장소 `README.md` |

현재·계획·실행 미검증을 [문서 분류 기준](../../../docs/DOCUMENTATION_POLICY.md)에 따라 구분한다. 기획 결정이 필요한 불일치는 팀 결정을 대신하지 않는다. 스킬 파일은 게임 구현 상태를 서술하는 기준 문서가 아니다.

## 실행과 보고

1. 커밋별 파일 목록과 메시지 전체를 검토 가능하게 제시한다. 새 승인이 필요하면 준비와 검증을 끝낸 뒤 이 단계에서 요청한다. 명시적으로 승인된 커밋 요청은 같은 승인을 반복하지 않고 실행한다.
2. 파일 경로를 지정해 stage한다. `git add .`로 무관한 변경을 포함하지 않는다. `git diff --cached --check`와 staged diff로 실제 커밋 범위를 확인한다.
3. 확정한 메시지로 커밋하고 해시·포함 범위·검증 결과를 보고한다. 여러 줄 메시지는 임시 파일과 `--file`을 사용한다. push는 별도로 허용된 경우에만 수행한다.

다른 파트가 이어서 할 일이 생기면 수신자, 이유, 재현, 코드 위치, 필요한 결정과 다음 행동을 담은 자립적인 전달 문서를 준비한다. 전달 문서는 `.claude/handover/{날짜}/{내용}.md`(Git 제외)에 쓰고, 팀 전체가 지켜야 할 계약 변경은 `docs/`에 따로 반영한다. 문서 작성과 상대에게 보내는 행위는 구분하고, 연락은 명시적으로 요청받은 경우에만 한다. 다음 세션 기록은 [인수인계](../write-handoff/SKILL.md)로 남긴다.
