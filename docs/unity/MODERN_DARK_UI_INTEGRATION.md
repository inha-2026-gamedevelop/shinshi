# ModernDark UI 연동 방식

ModernDark 창 묶음(수련·신체강화·부적·파티모집·친구·인벤토리 등)과 이름표·장비 숨김이 개발 씬과 Release 씬에 어떻게 닿는지 정리한다. 이 문서는 연동 방식과 검증 범위를 기록하며 씬 파일을 바꾸지 않는다.

## 연동 경로

| 경로 | 동작 | 씬 수정 |
|---|---|---|
| 각 View의 `Awake`/`EnsureModernLayout` | 창 자신이 코드로 ModernDark 배치를 만든다. 인벤토리, 친구, 스킬, 메일, 던전 입장 등 | 필요 없음 |
| 프리팹 직렬화 값 | 친구창은 `PF_FriendWindow`의 `_windowSize`(960x680)를 `Awake`에서 적용한다. 씬은 프리팹 인스턴스라 프리팹 변경이 그대로 반영된다 | 필요 없음 |
| `ModernDarkRuntimeBootstrap` | 씬 로드 뒤 `HudMenuDropdown`이 있는 실제 클라이언트 씬에만 수련·신체강화·부적·파티모집 항목과 창을 만든다 | 필요 없음 |
| `NetworkOverlayView` | 이름표를 런타임에 자동 생성한다 | 필요 없음 |

Bootstrap 동작은 아래와 같다.

- 배치 모드와 그래픽 Null(전용 서버)에서는 시작하지 않는다.
- 씬당 한 번만 실행한다(`scene.handle` 보관). 숨김 Runner가 0.5초 간격 8회까지 `HudMenuDropdown`을 다시 찾는다.
- `HudMenuDropdown`이 현재 씬에 없으면 아무것도 만들지 않는다. 그래서 메뉴가 없는 개발 씬에는 수련창이 중복 생성되지 않는다.
- 수련 프리팹은 카탈로그만 참조하고 어떤 씬에도 박혀 있지 않다.
- 던전 역할(`ClientRoles.IsDungeon()`)에서는 파티모집을 열지 않는다.

## 개발 씬 견본과 실접속의 구분

개발 씬의 `TrainingPreviewBinder`와 `FriendsPreviewBinder`는 예시 데이터로 화면을 채우는 견본이다. 실접속에서는 Release 씬의 실제 바인더(예: `SortiePrepBinder`, `CharDetailBinder`, `MailBoxBinder`)가 서버 데이터를 읽는다. 견본 화면이 보인다고 실접속 동작이 확인된 것은 아니다.

| 씬 | 연동 |
|---|---|
| `Release/ShinshiTown`, `Release/DungeonForest` | 메인 View와 `PF_SystemMenu`가 씬에 있다. 위 네 경로가 모두 적용된다 |
| `Dev_HUD` 계열 | `HudMenuDropdown`이 있으면 Bootstrap이 적용된다. 창 자체 배치는 항상 적용된다 |
| `Dev_Training`, `Dev_EquipmentEnhance`, `Dev_Setting` | `HudMenuDropdown`이 없어 Bootstrap은 건너뛰고 창 자체 배치와 `*PreviewBinder`만 쓴다 |

| 프리팹/스크립트 | 경로 |
|---|---|
| 수련창 | `Assets/02.Prefab/UI/Training/PF_TrainingWindow.prefab` |
| 친구창 | `Assets/02.Prefab/UI/Windows/PF_FriendWindow.prefab` |
| 시스템 메뉴 | `Assets/02.Prefab/UI/Windows/PF_SystemMenu.prefab` |
| 카탈로그 | `Assets/02.Prefab/UI/Resources/ModernDarkUiCatalog.asset` |
| Bootstrap | `Assets/01.Scripts/06.UI/Common/ModernDarkRuntimeBootstrap.cs` |
| 파티모집 | `Assets/01.Scripts/06.UI/PartyRecruit/PartyRecruitView.cs` |

## 빌드 의존

- 카탈로그는 `Resources` 폴더에 있어 플레이어 빌드에 자동 포함된다. 카탈로그가 참조하는 수련 프리팹, 폰트, 배경 스프라이트도 함께 들어간다.
- 카탈로그가 없으면 경고를 한 번 남기고 수련·신체강화 메뉴만 만들지 않는다. 부적과 파티모집은 계속 동작한다.
- `ModernDarkGrowth` 이미지는 `Resources.Load`가 필요해 `Assets/01.Scripts/05.Art/UI/ModernDark/Resources/`에 있다. 아트 폴더 규칙(`Textures/` 전용)의 예외다. 폴더 구조를 바꾸면 로드 경로도 같이 바꾼다.
- 카탈로그 폰트(`HudQuestKorean-Regular`)는 동적 아틀라스다. 폰트 6종의 HEAD와 작업 트리를 정적으로 비교했을 때 `m_AtlasPopulationMode` 1, 원본 폰트 GUID, 멀티 아틀라스 1이 모두 같았다. 기존 폰트 변경은 이번 UI 커밋에 포함하지 않는다.

## 검증 범위

- Unity EditMode 251/251 통과(job `e15d953de9db4247ad20f4e2afff152f`, 9.0145627초).
- 정적 배열 명명 보정 후 관련 14/14 재검증 통과(job `de3d06d223b84ae99c8b619ef8d571ef`, 0.7558293초). 위 251건에 포함되는 테스트의 재실행이다.
- 실제 Play Mode에서 `Dev_Training`과 `Dev_SortiePrep`을 열어 새 레이아웃 적용과 각 View 1개를 확인했다. 개발 씬 견본 데이터 기준이다.
- Release 실서버 귀환은 M1(단일 클라이언트) 10/10을 확인했다.

## 검증 한계

- M1(전용 서버와 클라이언트 1개, 같은 PC)만 검증했다. M2(클라이언트 2개 이상, 같은 PC)와 M3(서버와 클라이언트가 서로 다른 PC) 접속은 검증하지 않았다.
- 실서버 접속 중 파티모집 요청, 이름표·장비 숨김의 다인 동기화, 플레이어 빌드는 실행하지 않았다.
- HEAD 폰트만으로 한글이 실제로 렌더되는지는 실행해 보지 않았다. 위 정적 비교로 같은 원본과 설정임만 확인했다.
