# 스킬창(스킬 부적창) 설계

> 상태(2026-10-16): **구현됨·실행 미검증.** 10절 결정을 모두 받아 코드·프리팹(`PF_SkillWindow`)·표시 데이터(`SkillDisplayCatalog`)·마을 씬 연결을 넣었다. 자동 검증은 같은 날 Unity EditMode 6,427건 중 6,426건 통과(1건은 이 작업과 무관한 기존 제외 항목)와 백엔드 실제 PostgreSQL 1,607건 통과이고, 편집기에서 미리보기 캡처를 확인했다. **게임 실행(Play 모드), 로컬 스택 기동, 실제 마을 서버·백엔드 요청(등록·강화·분해·조합 저장), 다인 접속은 하지 않았다.** 기준 규칙은 [부적 45개](talisman-catalog.md), [성장 기준안](growth-economy.md), [전투 부록](combat-design.md)이며 이 문서는 그 규칙을 화면과 요청 흐름으로 옮긴다. 수치·규칙이 다르면 위 문서가 우선한다.

## 1. 목적과 범위

스킬창은 내 직업의 기본 공격과 Q/W/E/R 네 기술을 보여 주고, 기술마다 붙는 부적 효과(보조 1단계, 보조 2단계, 핵심 3단계)를 등록·강화·장착·분해하는 창이다. 기획서의 "스킬 부적창"(장비 소켓을 대신하는 창)이 이것이다.

| 포함 | 제외 |
|---|---|
| 기본 공격(콤보 단계, 직업 자원 규칙) 정보 표시 | 기본 공격에 부적 장착(기획상 부적창 밖) |
| Q/W/E/R 이름, 설명, 기력, 쿨다운, 사거리 표시 | 기술 레벨·기술 수련(기획에 없음) |
| 기술별 부적 3단계 슬롯, 효과 선택(단계당 하나) | 구 직업(0~2) 강림·신령 기술 |
| 부적 등록, 강화(Lv1→5), 분해 | 부적 카드 획득(드랍·우편은 기존 경로) |
| 조합 변경은 신시(마을)에서만. 던전에서는 기술 정보만 표시(5.2절) | 새 아이콘 제작(임시 아이콘 사용, 7절) |

## 2. 데이터 원천

| 화면 값 | 원천 | 비고 |
|---|---|---|
| 기술 정의(슬롯, 기력, 쿨다운, 범위, 대표 여부) | `SkillCatalogSO.TryGetSkillBySlot(playerClass, slot)` (`GameDB.Skills`) | 직업은 `PlayerCombatState.PlayerClass`(마을에서는 캐릭터 선택 결과) |
| 기본 공격 콤보 단계 | `PlayerConfigSO.GetBasicAttack(class)`의 `ComboStepCount`, 단계별 계수·주기 | 검사는 단계 명세 없음(단일 값) |
| 직업 스택 규칙 | `SkillCatalogSO`의 `MageknightStackMax`·`ArcherStackMax`, 기술 `StackGain`/`RequiredStacks`, 궁수 `ArcherElement` | 마검사·궁수만 표시 |
| 부적 효과 정의(기술 ID, 단계, 선택, 강화 항목, Lv1 보너스) | `TalismanCatalogSO` | 런타임 ID `Tnn-tierChoice` |
| 보유 효과·레벨·등록 실패 보정, 현재 조합, 조합 판번호, 정기, 카드 수 | `TownGrowthClient.Snapshot`(`GrowthSnapshotMessage`의 `Talismans`, `Loadout`, `LoadoutRevision`, `Essence`, `AccountItems`) | 창을 열 때 스냅샷 요청 |
| 등록·강화·분해 비용, 성공률, 보너스 배율 | 미리보기는 기존 GameDB를 재사용한다: 비용·성공률은 `GameDB.Growth`(`GrowthConfigSO`), 레벨 배율·보너스 계산은 `GameDB.Talismans`(`TalismanCatalogSO`). 별도 표시 표를 만들지 않는다 | 최종 값은 서버 결과. GameDB 값과 백엔드 `growth-content.json`의 일치는 기존·신규 EditMode 시험으로 확인한다 |
| 기술·효과 한국어 이름과 설명 | **새 표시 데이터 필요**(3절) | 현재 코드에 없음 |

### 2.1 부적 효과 상태

| 상태 | 판정 |
|---|---|
| 미보유 | 카드도 없고 `Talismans` 행도 없거나 `Level == 0` |
| 등록 가능 | 카드 1장 이상, 정기 150 이상, `Level == 0`(행 없음 포함). 행이 있으면 `RegisterFailures`로 다음 성공률(실패 보정)을 보인다 |
| 등록됨 Lv n | `Talismans` 행의 `Level >= 1`. 백엔드는 등록 실패도 `Level = 0` 행으로 저장하므로 행 존재만으로 판정하지 않는다 |
| 장착됨 | 스냅샷 `Loadout`에 (기술, 단계, 효과)로 있음 |
| 효과 없음(재설계 대기) | 2026-10-15 개편으로 새 기술 형태와 맞지 않아 서버가 무시하는 효과(마검사 4개, 궁수 9개). 목록은 [부적 45개](talisman-catalog.md)를 따른다 |

효과 없음 표시는 클라이언트 표시 표의 플래그로 둔다. 서버 판정을 바꾸지 않으며 장착을 막지도 않는다. 장착 시 "이 효과는 현재 기술에 적용되지 않는다" 경고만 보인다.

## 3. 표시 데이터(새로 추가)

기술·효과 이름과 설명이 코드에 없다. `com.unity.localization`은 설치돼 있으나 쓰는 코드가 없으므로 이번 범위에서는 도입하지 않는다.

- `SkillDisplayCatalogSO`(신규, `00.Common/Data`)를 GameDB에 추가한다. 항목은 `DefinitionId`(기술 또는 부적 효과 ID) → 한국어 이름, 한 줄 설명, 강화 항목 설명, 효과 없음 플래그.
- 값은 [부적 45개](talisman-catalog.md), [궁수 기준안](archer-design.md), [전투 부록](combat-design.md)의 표에서 옮긴다. 문서용 코드(KS/MK/AR)와 런타임 ID(T13~T24)의 대응은 문서 표를 기준으로 한 번 만들고 EditMode 시험으로 90효과 중 신규 45효과 누락이 없는지 확인한다.
- 이름이 없으면 정의 ID를 그대로 보인다(기존 인벤토리·경매 창과 같은 대체 규칙).
- 콘텐츠 해시에 포함되는 SO이므로 추가하면 클라이언트·서버를 함께 다시 빌드해야 한다.

## 4. 화면 구성

```
┌ 스킬 ────────────────────────────── [X] ┐
│ [직업명]  스택 규칙 한 줄(마검사·궁수)    │
│ ┌기본 공격┐ ┌ Q ┐ ┌ W ┐ ┌ E ┐ ┌ R ┐       │  ← 기술 탭(아이콘+키)
│ 선택 기술: 이름 / 기력 / 쿨다운 / 사거리  │
│ 설명 2~3줄                               │
│ ─ 부적 ─────────────────────────────── │
│ 1단계 보조 [A 효과][B 효과]  ← 단계당 1개 │
│ 2단계 보조 [A][B]                        │
│ 3단계 핵심 [A][B]                        │
│ ─ 선택한 효과 상세 ──────────────────── │
│ 이름 Lv n/5, 현재 보너스, 다음 레벨 보너스 │
│ [등록 1장+150] [강화 n장+m] [분해] [장착]  │
│ 보유 정기 / 카드 n장 / 조합 n/12          │
│ [조합 저장]  (출정 중이면 잠김 안내)      │
└──────────────────────────────────────────┘
```

- 기본 공격 탭은 부적 영역 대신 콤보 단계 표(단계별 계수·주기, 궁수 3단 피격이상 면역)와 직업 스택 규칙을 보인다.
- 비대표 기술은 단계당 후보 1개, 대표 기술은 단계당 2개다. 후보가 1개인 단계는 버튼 하나만 그린다.
- 장착 변경은 창 안에서 임시 조합으로 모았다가 [조합 저장]으로 한 번에 보낸다(서버 요청이 전체 교체형이기 때문, 5절).
- 창을 닫을 때 저장하지 않은 변경이 있으면 확인 대화상자를 띄운다. 기존 `HotkeyWindowToggle`은 즉시 `SetActive(false)`를 실행해 닫기 취소 계약이 없으므로, Esc·단축키·HUD 메뉴·[X] 버튼의 모든 닫기 입력이 같은 `RequestClose()`를 거치게 한다(8절).

## 5. 요청 흐름

모든 쓰기는 마을 서버를 거쳐 백엔드가 처리한다(클라이언트 SyncVar로 쓰지 않는다).

| 동작 | 호출 | 서버 규칙 | 화면 처리 |
|---|---|---|---|
| 창 열기 | `TownGrowthClient` 스냅샷 요청 | — | 응답 전에는 로딩 표시, 버튼 잠금 |
| 등록 | `RegisterTalisman(effectId)` | 카드 1 + 정기 150, 실패 보정 50/65/80/100% | 결과(성공/실패)와 남은 보정 표시 |
| 강화 | `UpgradeTalisman(effectId)` | 중복 1/1/2/3장, 정기 300/600/1000/1600, 확정 | 새 레벨·보너스 갱신 |
| 분해 | `DisassembleTalisman(string effectId, int quantity)` | 카드 실물 `quantity`장만 소모하고 정기를 지급한다. 등록 효과·레벨·장착은 그대로 둔다 | 분해할 카드 수 입력(보유 수 상한), 지급 정기 미리보기. 장착 해제 안내는 없다 |
| 조합 저장 | `ReplaceTalismanLoadout(TownLoadoutEntry[])` | 전체 교체, 최대 12행, 단계당 1개, 기술당 3개, 등록한 효과만, `LoadoutRevision` 낙관적 잠금 | 판번호 충돌이면 스냅샷 다시 받고 "다른 곳에서 바뀜" 안내 |

- 클라이언트는 미리보기와 버튼 활성만 판단한다. 비용·성공·조합 유효성의 최종 판정은 서버다.

### 5.1 결과 불명과 재시도

`TownGrowthClient`는 모든 성장 쓰기 요청을 **전역 단일 슬롯**에 보관하고 `RetryLast()`는 그 한 요청만 다시 보낸다. 스킬창 요청 A가 결과 불명인 동안 다른 성장창(장비 강화, 옵션 재설정, 수련)이 B를 보내면 스킬창의 재시도가 B를 보내게 된다.

- `TownGrowthClient`에 **공용 쓰기 잠금**을 둔다: 결과 불명 요청이 있으면 `HasUnresolvedWrite`가 참이고, 모든 성장창의 쓰기 버튼이 이 값을 보고 잠긴다. 새 쓰기는 잠금이 풀린 뒤에만 보낸다.
- 재시도는 요청을 보낸 창만 할 수 있게 `RetryLast`가 보관 요청의 종류(예: 부적 등록)를 노출하고, 스킬창은 자기 종류일 때만 재시도 버튼을 연다. 다른 창의 요청이면 "다른 창의 요청 결과를 기다리는 중"만 표시한다.
- 이 변경은 기존 성장창 3개에도 적용되므로 그 창들의 Binder/View 수정과 시험을 같은 작업에 포함한다.

### 5.2 쓰기 잠금 조건

`ClientRoles.IsTown()`은 연결 여부와 Town 역할만 본다. 출정 대기(파티 Queued)에서도 마을 연결이 유지되어 참이므로 이것만으로 잠그면 안 된다.

| 상황 | 판정 | 스킬창 |
|---|---|---|
| 마을, 출정 진행 없음 | `IsTown()` 참, 파티 출정 단계 없음, 서버 전환 중 아님 | 쓰기 가능 |
| 마을, 출정 대기·준비·전환 중 | `IsTown()` 참이지만 파티 출정 단계가 Queued 이후이거나 서버 전환 진행 중 | 읽기 전용, "출정 중에는 조합을 바꿀 수 없다" |
| 던전 | `IsTown()` 거짓 | 기술 정보 탭만. 부적 영역은 "신시에서 확인" 안내(아래) |

던전 이동은 클라이언트를 재시작하고 `TownGrowthClient` 스냅샷을 초기화하며, 성장 요청 처리기는 Town 역할에만 있다. 따라서 **던전에서는 보유 효과·레벨·조합을 공급할 경로가 없다.** 1차 범위에서는 던전에서 부적 영역을 숨기고 기술 정보(카탈로그 값)만 보인다. 던전에서도 장착 조합을 보려면 던전 최초 접속·재접속 때 개인 부적 조합을 내려주는 읽기 전용 계약이 필요하며 이는 10절 결정 항목이다.

## 6. 코드 구조

가장 가까운 기존 창은 장비 옵션 재설정(EquipmentOption)이다. 같은 구조를 따른다.

| 파일(신규) | 역할 |
|---|---|
| `06.UI/SkillWindow/SkillWindowView.cs` | UGUI 표시와 버튼 이벤트만. 서버를 부르지 않는다 |
| `06.UI/SkillWindow/SkillWindowViewRules.cs` | 순수 정적 규칙: 슬롯 상태, 단계 선택 유효성, 버튼 활성, 비용·보너스 미리보기, 임시 조합 검증 |
| `06.UI/Binding/SkillWindowBinder.cs` | `TownGrowthClient` 구독, 스냅샷 요청, 요청 전송, 결과 반영, 재시도 처리 |
| `06.UI/SkillWindow/SkillWindowPreviewBinder.cs` | 개발 검수용 가짜 스냅샷 |
| `06.UI/SkillWindow/Editor/SkillWindowBuilder.cs` | 프리팹 생성 `Build()`와 마을 씬 연결 `WireTownScene()`(Canvas, Binder, `HotkeyWindowToggle`) |
| `00.Common/Data/SkillDisplayCatalogSO.cs` + 자산 | 3절 표시 데이터 |
| `Assets/02.Prefab/UI/Windows/PF_SkillWindow.prefab` | 빌더 산출물, 비활성 저장 |
| `Assets/Tests/EditMode/SkillWindowViewRulesTests.cs`, `SkillDisplayCatalogTests.cs` | 규칙·표시 데이터 시험 |

| 파일(수정) | 변경 |
|---|---|
| `01.Network/Town/TownGrowthClient.cs` | 공용 쓰기 잠금(`HasUnresolvedWrite`)과 보관 요청 종류 노출(5.1절) |
| 기존 성장창 Binder/View(장비 강화, 옵션 재설정, 수련) | 공용 쓰기 잠금을 보고 쓰기 버튼 잠금, 자기 요청일 때만 재시도 |
| `04.Systems/Hotkey/HotkeyWindowToggle.cs`, `HotkeyRouter`(Esc), HUD 메뉴 | 선택적 닫기 가드(8절) |
| `04.Systems/Hotkey/EHotkeyAction.cs`, `09.Data/UI/HotkeyConfig.asset` | `SkillWindow = 11`과 키 배정 |
| 파티 출정 단계를 클라이언트에서 읽는 곳 | 5.2절 쓰기 잠금 판정 입력. 이미 공개된 값이 있으면 재사용하고 없으면 표현용 값 추가 |

- UGUI + TextMeshPro. UI Toolkit을 섞지 않는다.
- 스타일은 `EquipmentGrowthStyle`(남색·민트·금색·보라 팔레트), 제목줄 끌기는 `UiWindowDragHandle`.
- 한글 글꼴은 기존 `HudQuestKorean-Regular/Bold`를 재사용한다. 글리프가 모자라면 `DungeonEntryFonts` 방식으로 동적 SDF를 준비한다.
- 빌더를 다시 돌리면 수동 프리팹 수정은 덮어쓴다(기존 빌더와 같음).

## 7. 아이콘

`SkillCatalog.asset`에 기술 아이콘이 배정되어 있지 않다. 이번 범위에서는 다음 순서로 대체한다.

1. 부적 효과: 기존 `UiIconLibrarySO`의 효과 ID 아이콘(`Items64/T_Item64_T13~T24`)과 단계 배지.
2. 기술: 그 기술의 대표 부적 아이콘을 임시로 쓰고, 없으면 키 글자(Q/W/E/R)만 그린다.
3. 정식 기술 아이콘이 생기면 HUD와 스킬창이 함께 쓴다. 단, `SkillCatalogSO._icon`(Sprite)에 값을 넣으면 현재 콘텐츠 해시(`GameDBContentHash`)가 null이 아닌 Sprite를 지원하지 않아 예외가 난다. 정식 아이콘 적용 전에 아이콘을 UI 전용 카탈로그(`UiIconLibrarySO` 등, 해시 대상 밖)로 분리하거나, 해시가 Sprite를 안정적인 GUID로 다루는 규약을 먼저 정한다. 임시 아이콘 범위는 영향이 없다.

## 8. 단축키와 겹침

- `EHotkeyAction` 끝에 `SkillWindow = 11`을 추가하고 `HotkeyConfig.asset`에 키를 배정한다. 이미 쓰는 키는 I, Y, P, Enter, M, U, O, Esc이고 전투 키(Space, Q/W/E/R, Z/X/C, 1/2)와 F(상호작용)는 쓸 수 없다. **확정: 스킬창 K**(2026-10-16 사용자 결정). K를 쓰던 딜미터(`CombatMeter`)는 L로 옮겼다.
- Esc는 기존 창 스택 규칙으로 닫는다. 캔버스 정렬 순서는 빌더가 기준 캔버스 +1로 둔다.
- 닫기 가드: `HotkeyWindowToggle`에 선택적 닫기 확인 훅(예: `ICloseGuard.RequestClose(Action confirmClose)`)을 추가하고, 가드가 있으면 즉시 끄지 않고 가드에 맡긴다. 가드가 없는 기존 창은 지금처럼 즉시 닫힌다. Esc 스택 처리와 HUD 메뉴 버튼도 같은 경로를 쓴다.
- 창이 열려 있어도 전투 입력(Q/W/E/R)은 막지 않는다(기존 창과 같음). 마을에서는 전투가 없어 실질 충돌은 없다.
- HUD 메뉴 버튼에 "스킬" 항목을 추가하고 `HotkeyWindowToggle.Toggle`을 부른다.

## 9. 검증 계획

| 단계 | 내용 |
|---|---|
| EditMode | 규칙(슬롯 상태와 Lv0 실패 행, 단계당 1개·기술당 3개·총 12개, 미등록 장착 거부, 비용 미리보기, 효과 없음 경고, 쓰기 잠금 조건 표), 공용 쓰기 잠금과 재시도 소유(다른 창 요청 중 스킬창 재시도 금지), 닫기 가드(미저장 변경 시 Esc·단축키·메뉴가 확인을 거침), 표시 데이터 누락 없음, GameDB 성장 수치와 백엔드 콘텐츠 일치, 단축키 설정 검증 통과 |
| 개발 씬 | `Dev_SkillWindow.unity`(또는 기존 `Dev_EquipmentGrowthStyle` 확장)에서 미리보기 바인더로 직업 3개 화면 캡처 |
| 로컬 스택 | 테스트 계정으로 마을에서 등록·강화·조합 저장, 출정 중 잠김, 판번호 충돌 재현 |
| 문서 | 구현 후 이 문서 상태 줄, [로드맵](../05-implementation-roadmap.md), [프로젝트 지식 맵](../ai/07_PROJECT_KNOWLEDGE_MAP.md) 갱신. 실행 안 한 항목은 `구현됨·실행 미검증`으로 둔다 |

## 10. 사용자 결정(결정됨, 2026-10-16)

1. 스킬창 단축키: **K**. 딜미터는 K에서 **L**로 옮긴다(`HotkeyConfig.asset`, `HotkeyConfigSO` 기본값).
2. 범위: 등록·강화·분해·조합 저장을 **한 번에** 구현한다.
3. 효과 없음 13개(마검사 4, 궁수 9): **장착 허용, 경고만**. 후보 칸에 "효과 없음", 상세에 경고 문구를 보인다.
4. 기술 아이콘: **임시**(7절 순서, 그 기술의 1단계 A 부적 아이콘). `SkillCatalogSO._icon`에는 넣지 않는다.
5. 던전: **부적 영역을 숨기고 기술 정보만** 보인다. 읽기 전용 조합 계약은 추가하지 않는다.

### 10.1 구현 메모

- 공용 쓰기 잠금: `GrowthWriteLock`(01.Network/Town)을 `TownGrowthClient`가 들고 `HasUnresolvedWrite`, `UnresolvedAction`, `UnresolvedOperationId`, `IsUnresolvedOutcomeUnknown`, `WriteLockChanged`, `ReleaseWrite(operationId)`를 공개한다. 잠긴 동안 새 쓰기는 `Guid.Empty`로 거절한다. 확정이나 확정 거절이면 풀고, `BackendUnavailable`이면 결과 불명으로 남긴다. 장비 강화·수련 창은 기존처럼 시간 초과 뒤 서버 상태를 다시 읽으며 자기 요청만 `ReleaseWrite`로 푼다. 옵션 재설정과 스킬창은 자기 요청일 때만 `RetryLast`를 부른다. 다른 창 요청 중에는 "다른 창의 요청 결과를 기다리는 중입니다"를 보인다(`GrowthWriteGuard`).
- 스킬창은 응답이 `API_TIMEOUT_SECONDS` 안에 오지 않으면 결과 불명으로 보고 잠금을 유지한 채 [다시 확인]을 연다.
- 닫기 가드: `ICloseGuard`(04.Systems/Hotkey)와 `HotkeyWindowToggle._closeGuard`. Esc, K, HUD 메뉴, [X]가 모두 `HotkeyWindowToggle.RequestClose`를 거친다. 확인이 떠 있을 때 다시 닫기를 누르면 확인만 접는다.
- 출정 잠금: `TownPartyClient.Room`이 방 안이고 단계가 `Open`이 아니거나(카운트다운·예약·대기열·입장), `ShinshiNetworkManager.IsTransferringServer`이면 읽기 전용이다.
- HUD 메뉴: 캐릭터 묶음의 빈 칸(276, -400)에 "스킬" 버튼을 넣고 경제·시스템 묶음을 한 줄(52) 내렸다. `ReleaseMenuWiring.Wire()`를 다시 돌리면 거래·출정 준비 버튼 좌표를 그 코드의 상수로 되돌리므로 메뉴 배치를 다시 확인해야 한다.
- `SkillDisplayCatalogSO`는 GameDB 콘텐츠 해시에 들어간다. 서버·클라이언트를 함께 다시 빌드해야 한다.

## 11. 검증 이력

| 날짜 | 검증 | 결과 |
|---|---|---|
| 2026-10-16 | 구현 뒤 Unity EditMode(관련 시험 474건, 표시 계약 등 176건) | 모두 통과. 미리보기 바인더로 검사·마검사·궁수 화면을 편집기에서 캡처(개인 기록). 실제 서버·백엔드 요청과 플레이 모드는 미실행 |
| 2026-10-16 | 소셜 작업과 함께 한 전체 자동 검증 | Unity EditMode 6,427건 중 6,426건 통과(1건은 기존 제외 항목), 백엔드 실제 PostgreSQL 1,607건 통과. 게임 실행·로컬 스택·다인 접속은 미실행 |
| 2026-10-16 | Codex `gpt-6.1-sol` ultra 읽기 전용 정적 검증 | 지적 8건(높음 1, 중간 6, 낮음 1) 모두 수용해 2.1절(Lv0 실패 행), 2절(GameDB 수치 재사용), 4·8절(닫기 가드), 5절(분해 계약), 5.1절(공용 쓰기 잠금·재시도 소유), 5.2절(출정 대기 잠금, 던전 읽기 경로 없음), 7절(아이콘 해시)에 반영. 1번(던전 읽기 경로)은 범위를 줄여 수용하고 10절 5번 결정으로 남김. 재검증은 하지 않음 |
