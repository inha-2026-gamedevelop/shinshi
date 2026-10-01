# AI 작업 문서

> shinshi 프로젝트에서 AI 도구와 함께 일하기 위한 운영 문서.
> 2026-09-30 shinshi 기준으로 이식했다. 멀티플레이(Mirror) Unity 프로젝트를 전제로 한다.

---

## 처음이라면

**전부 읽지 않는다.** 지금 하려는 일에 해당하는 것만 연다.

| 상황 | 읽을 것 |
|---|---|
| 게임의 규칙과 구현 방향을 볼 때 | [기획·기술 문서 안내](../00-reading-guide.md) → [기획서](../02-game-design.md) · [기술설계서](../03-technical-design.md) |
| AI에게 작업을 시킬 때 | [작업 브리프](02_TASK_BRIEF_TEMPLATE.md) |
| AI가 "다 됐다"고 했을 때 | [검증 플레이북](03_VERIFICATION_PLAYBOOK.md) |
| 네트워크 코드를 만질 때 | [네트워크 권위](../unity/NETWORK_AUTHORITY.md) · 규칙/상태/서버/표현 분리와 `98.Servers/` 담당은 [서버·클라이언트 협업](../unity/SERVER_CLIENT_WORKFLOW.md) |
| 멀티플레이 접속 테스트를 해야 할 때 | [멀티플레이 테스트](04_MULTIPLAYER_TEST_PROTOCOL.md) |
| 작업을 중단할 때 | [세션 인수인계](05_SESSION_HANDOFF.md) 양식으로 루트 지침에 지정된 위치에 개인 세션 기록을 쓴다 |
| 기획서를 구현으로 옮길 때 | [기획 문서 → 구현](09_DOCUMENT_ANALYSIS_WORKFLOW.md) |
| C# 코드를 쓸 때 | [코딩 컨벤션](../coding-convention.md) |
| 커밋 · PR 제목을 쓸 때 | [커밋 컨벤션](../commit-convention.md) |

저장소 루트의 [`AGENTS.md`](../../AGENTS.md)는 AI가 자동으로 읽는다. 사람이 먼저 읽을 필요는 없다.

## 도구별 스킬

Codex용 스킬은 [`.agents/skills/`](../../.agents/skills/), Claude Code용 원본은 [`.claude/skills/`](../../.claude/skills/)에 둔다. 프로젝트 스킬은 개인 전역 폴더에 중복 설치하지 않는다. 공통 규칙은 `AGENTS.md`와 관련 `docs/` 문서를 기준으로 하며, 스타일 규칙은 [코딩 컨벤션](../coding-convention.md), 커밋 메시지 형식은 [커밋 컨벤션](../commit-convention.md)에서만 관리한다.

| 스킬 | 사용 시점 |
|---|---|
| [guided-commit](../../.agents/skills/guided-commit/SKILL.md) | 변경 검토·문서 동기화·커밋 준비와 실행 |
| [guided-pull-request](../../.agents/skills/guided-pull-request/SKILL.md) | 실제 PR 템플릿·검증·리뷰 담당에 맞춘 PR 준비 |
| [guided-issue](../../.agents/skills/guided-issue/SKILL.md) | 작업·버그·밸런스 이슈와 재현 로그 정리 |
| [check-code-ownership](../../.agents/skills/check-code-ownership/SKILL.md) | 다른 파트·공용·네트워크 계약 수정 전 소유 경계 확인 |
| [clarify-ambiguous-request](../../.agents/skills/clarify-ambiguous-request/SKILL.md) | 결과를 바꾸는 핵심 미확정 입력 확인 |
| [present-alternatives](../../.agents/skills/present-alternatives/SKILL.md) | 기술 대안의 프로젝트 적합성과 비용 비교 |
| [write-comments](../../.agents/skills/write-comments/SKILL.md) | 코딩 컨벤션에 맞춘 의도·제약 주석 작성 |
| [write-handoff](../../.agents/skills/write-handoff/SKILL.md) | 미완료 작업의 근거·차단 조건·다음 행동 기록 |

Codex에서는 `$guided-commit`처럼 스킬 이름을 지정하거나 목적을 직접 요청한다. 명시적인 초안 요청은 게시·커밋 요청으로 바꾸지 않고, 이미 허용된 실행을 다시 승인받지 않는다. `.github`의 이슈 · PR 템플릿은 실제 파일을 읽어 따르고, `CODEOWNERS`도 실제 파일을 읽어 따르고, 없는 규칙이나 계정을 가정하지 않는다. 개인 기록 위치는 루트 [`AGENTS.md`](../../AGENTS.md)를 따른다.

---

## 문서 지도

### 게임 기획·기술 설계

| 문서 | 언제 |
|---|---|
| [00 읽기 안내](../00-reading-guide.md) | 역할별 읽기 순서와 설계·구현 상태를 구분할 때 |
| [01 시스템 개요](../01-system-design.md) | 세계관·게임 루프·범위·팀을 이해할 때 |
| [02 게임 기획서](../02-game-design.md) | 상세 규칙·수치·성장·경제·콘텐츠를 확인할 때 |
| [03 기술설계서](../03-technical-design.md) | 서버 권한·요청·영속성·장애 계약을 구현할 때 |
| [04 아키텍처](../04-architecture.md) | 모듈·씬·데이터·상태 전이의 위치를 찾을 때 |
| [05 구현 로드맵](../05-implementation-roadmap.md) | 작업 선행 관계·담당·수용 기준을 확인할 때 |
| [3D 모델 제작 목록](../design/model-production-list.md) | 공용 원본·지역 조립·후속 모델과 Meshy CLI → Blender 제작 흐름을 볼 때 |
| [아트 제작 기본 과정](../unity/ART_ASSET_PRODUCTION_WORKFLOW.md) | 원화·목표 이미지부터 AO/PBR·LOD·`02.Prefab/` 프리팹까지 별도 요청 없이 진행할 때 |
| [맵 성능 사전 준비](../unity/MAP_PERFORMANCE_PREPARATION.md) | 최소 사양 60 FPS 목표로 모델·조명 베이크, VFX 준비·풀·품질 대안과 실제 측정 절차를 검토할 때 (적용 미검증) |

### AI 작업 운영

| 문서 | 언제 |
|---|---|
| [01 실행 정책](01_AGENT_EXECUTION_POLICY.md) | 승인 경계와 중단 조건을 정할 때 |
| [02 작업 브리프](02_TASK_BRIEF_TEMPLATE.md) | 기능 · 버그 · 조사를 AI에 의뢰할 때 |
| [03 검증 플레이북](03_VERIFICATION_PLAYBOOK.md) | 완료 주장에 증거를 연결할 때 |
| [04 멀티플레이 테스트](04_MULTIPLAYER_TEST_PROTOCOL.md) | **다인 접속 검증이 필요할 때 — 이 프로젝트의 핵심** |
| [05 세션 인수인계](05_SESSION_HANDOFF.md) | 작업을 중단하거나 넘길 때 |
| [06 컨텍스트 관리](06_CONTEXT_HYGIENE.md) | 긴 세션이나 대형 파일을 다룰 때 |
| [07 프로젝트 지식 맵](07_PROJECT_KNOWLEDGE_MAP.md) | "이건 어디 문서에 있나" 싶을 때 |
| [08 병렬 작업 분업](08_SUBAGENT_ORCHESTRATION.md) | 여러 작업을 동시에 돌릴 때 |
| [09 기획 문서 → 구현](09_DOCUMENT_ANALYSIS_WORKFLOW.md) | 기획서를 작업 단위로 쪼갤 때 |
| [10 응답 스타일](10_RESPONSE_STYLE_GUIDE.md) | AI 보고 형식을 통일할 때 |
| [11 다단계 작업 절차](11_MULTI_STAGE_WORKFLOW_SOP.md) | 여러 세션에 걸친 작업을 관리할 때 |
| [12 에셋 · 데이터 정책](12_ASSET_AND_DATA_POLICY.md) | 외부 AI에 무엇을 넣어도 되는지 판단할 때 |

### Unity

| 문서 | 언제 |
|---|---|
| [처음 프로젝트를 열 때](../unity/UNITY_START_HERE.md) | 클론 후 첫 실행까지 |
| [코딩 컨벤션](../coding-convention.md) | C# 스타일 · 폴더 · 네임스페이스를 정할 때 |
| [커밋 컨벤션](../commit-convention.md) | 커밋 메시지 · PR 제목 형식과 커밋 단위를 정할 때 |
| [네트워크 권위](../unity/NETWORK_AUTHORITY.md) | 서버냐 클라냐 판단할 때 |
| [서버·클라이언트 협업](../unity/SERVER_CLIENT_WORKFLOW.md) | 규칙 · 상태 · 서버 · 표현 층 배치, `98.Servers/` 담당과 판정 · AI 작성 흐름을 확인할 때 |
| [씬 · 에셋 안전](../unity/SCENE_AND_ASSET_SAFETY.md) | 씬 · `.meta` · LFS를 다룰 때 |
| [밸런스 데이터 작업](../unity/BALANCE_DATA_WORKFLOW.md) | 수치를 만들거나 바꿀 때 |
| [엔지니어링 패턴](../unity/UNITY_ENGINEERING_PATTERNS.md) | 성능 · 수명 문제가 실제로 생겼을 때 |
| [디자인 패턴](../unity/DESIGN_PATTERNS.md) | 구조를 새로 잡거나 패턴 도입을 판단할 때 |
| [도입 기능 계획](../unity/FEATURE_ADOPTION_PLAN.md) | 개발 웹 패널 · 데이터 검증 · 게임플레이 태그 · 능력치/효과 구조 · 서버 판단 기록기를 착수할 때 |
| [버텍스 컬러 폴리지 셰이더](../unity/VERTEX_COLOR_FOLIAGE_SHADER.md) | 꽃 · 잎 재질과 텍스처 · 모델 임포트를 설정할 때 |

문서 자체의 분류와 현재성 기준은 [문서 분류](../DOCUMENTATION_POLICY.md)를 따른다.

---

## shinshi 전제

| 항목 | 값 |
|---|---|
| 엔진 | Unity 6000.4.7f1, URP |
| 네트워크 | **Mirror 96.11.3** (`Assets/Mirror/`). API 매핑은 [네트워크 권위](../unity/NETWORK_AUTHORITY.md) |
| 인원 · 호스트 방식 | 전투 1~4인·공유 마을 포함 목표 동시 접속 10~100명 · 데디케이티드 서버 (용량 미검증) |
| 브랜치 전략 | **`feature/{팀원이름}` 담당자별 브랜치** |
| 게임 장르 · 기획서 | PC 쿼터뷰 협동 액션 RPG · [기획서](../02-game-design.md) · [기술설계서](../03-technical-design.md) |
| 현재 코드 | 팀 작성 게임 코드는 공통 계약(`Constants`, enum, GameDB 골격)뿐이다. 외부 플러그인과 설치 기록은 [에셋 대장](../licenses/ASSET_REGISTRY.md) 참조 |

값이 확정되면 이 표와 해당 원본 문서를 함께 고친다.

---

## 유지보수

- 코드와 문서가 다르면 **코드를 우선**하고 차이를 기록한다
- 한 규칙을 여러 문서에 복제하지 않고 원본을 링크한다
- 구현된 밸런스 수치는 ScriptableObject에서 확인한다. 아직 SO가 없는 항목의 설계 원본은 [기획서](../02-game-design.md)와 연결 부록이며, 구현 시 설계 목표와 실행값을 대조한다
- 더 이상 유효하지 않은 문서는 지운다. 역사 보존이 필요하면 인수인계로 옮긴다
