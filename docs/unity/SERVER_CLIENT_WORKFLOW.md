# 서버·클라이언트 협업

> 2026-10-02 M-A 코드와 동기화. 네트워크 라이브러리는 Mirror 96.11.3(`Assets/Mirror/`), 호스트 방식은 데디케이티드 서버(확정)다. **역할별 클래스 분리와 서버 전용 어셈블리**가 적용돼 있고, 연결·인증·전투·출정 저장·정산 코드가 있다. 개발 빌드와 격리 기동 검증 이력은 [로드맵](../05-implementation-roadmap.md)에 있으며 실제 M2/M3·배포 수용은 미검증이다. 보스 AI 서버 뼈대는 8장에 있다. 일반전 웨이브·스폰 실행 구현과 보스 마지막 단계 연결은 남아 있다.

같은 Unity 프로젝트와 저장소, Mirror 서버 권위 구조를 쓰며 **실행 위치와 작성 담당을 분리한다.** 판정·AI는 서버에서만 실행되지만(권위 원칙 그대로, [네트워크 권위](NETWORK_AUTHORITY.md)) 작성은 원래 파트 담당이 하고, {팀장}은 네트워크 연결 코드를 맡는다.
서버 구현 전체를 팀장 한 명이 맡으면 판정 규칙을 모르는 사람이 규칙을 쓰게 되고 병목이 생긴다. 그래서 판정·AI 작성은 파트 담당에게 둔다.

2026-10-12 Release 통합: GroundPaint 맵을 마을에 복사하면서 기존 UI와 카메라·스폰 루트 16개와 Release NavMesh GUID를 유지했다. 던전은 공용 HUD·파티·채팅·기술·설정·결과/귀환 화면을 사용하고 마을 전용 메뉴는 비활성화한다. `ReleaseCombatHudBinder`는 보스 HP·전투 미터 단계·물약 상태를 읽고 미복제 웨이브/부활 값은 숨긴다. 관련 EditMode 41/41(job `56568bbcda2b47be93fa6909d8f23e50`), Play Mode 표시와 스폰 8곳 NavMesh 표본을 확인했다. 실서버 접속·M2/M3는 미실시다. 비포커스 상태의 최종 테스트도 41/41 통과했다. 보스 프리팹 Play Mode에서 클라이언트 효과 활성화·루트 분리·이동 뒤 월드 좌표 유지·단일 presenter·파괴 후 효과 제거와 Console 오류 0건을 확인했다. 최종 Windows IL2CPP Release `build-bde9cbfa16`는 74.443346초, 오류 0건·경고 5건으로, Linux Mono 전용 서버 Release `build-fb5c281675`는 71.8607225초, 오류 0건·경고 80건으로 성공했다. 서버 백엔드는 `NamedBuildTarget.Server`의 `Mono2x`로 확인했다. Windows는 무화면으로 15초 기동을 유지했고 로그에서 Exception/Error가 없었다. 렌더·로그인 검증은 아니며 이번 Linux 실행은 미검증이다.

## 1. 구조 요약

개체 하나를 실행 위치별로 네 클래스로 나눈다. 규칙·명명·배너·코드 예시는 [코딩 컨벤션](../coding-convention.md) 9장, 어셈블리 구성은 0장이 기준이다.

| 층 | 위치 | 어셈블리 | 서버 빌드 | 클라 빌드 |
|---|---|---|---|---|
| 규칙 | `{NN}.{도메인}/{기능}/Rules/` | `Shinshi.Runtime` | 있음 | 있음 (미리보기·예고·입력 사전 검사) |
| 상태 | `{NN}.{도메인}/{기능}/Net/{이름}State.cs` | `Shinshi.Runtime` | 있음 | 있음 |
| 서버 | `98.Servers/{NN}.{도메인}/{기능}/` | `Shinshi.Server` | 있음 | **없음** |
| 표현 | `{NN}.{도메인}/{기능}/View/`, `{NN}.UI/` | `Shinshi.Runtime` | 꺼짐 | 있음 |

- 클라이언트 빌드에는 AI 결정, 보상 추첨, 정산, 서버 자격 증명을 쓰는 백엔드 호출이 **컴파일 단계에서** 들어가지 않는다.
- 헤드리스 서버에서는 표현이 스스로 꺼진다. 던전 풀 프로세스마다 표현 갱신 비용을 쓰지 않는다.
- 규칙과 서버 클래스는 `NetworkBehaviour`가 아니므로 EditMode 테스트가 바로 부른다.

## 2. 담당 경계

| 담당 | 구현하는 것 | 사용하는 것 |
|---|---|---|
| {팀장} | 상태 클래스(`Net/{이름}State.cs`: 동기화 필드, `[Command]`, Rpc, 처리기 인터페이스), `98.Servers/`의 요청 처리기·서버 부트스트랩·백엔드 게이트웨이·세션, `01.Network/`·`00.Common/`, asmdef, 통합 | 파트 담당이 작성한 규칙·AI·표현 |
| {팀원이름} ({파트}) | 담당 도메인 폴더(`{NN}.{도메인}/`)의 `Rules/`·`View/`와 표현. AI·웨이브 담당은 `98.Servers/{NN}.{도메인}/`의 AI 두뇌·진행 클래스도 작성 | 상태의 읽기 프로퍼티·이벤트, 공개 요청 창구 |
| {팀원이름} (UI 담당) | `{NN}.UI/`의 입력·UI | 공개 요청 창구·조회·결과 이벤트 |

현재 파트 배정(민성·기웅·진욱)과 경로별 리뷰 담당은 `.github/CODEOWNERS`가 기준이다.

**적이 언제 공격할지, 누구를 공격할지, 어디에 스폰할지는 서버에서만 실행되는 기능이며, 로직 작성은 해당 파트 담당이 한다.** 그 코드는 `98.Servers/`에 있어 클라이언트 빌드에 들어가지 않는다. 표현은 서버가 확정한 예고와 상태를 표시한다. 애니메이션 완료를 피해·공격 종료의 판정 근거로 삼지 않는다.
씬·프리팹·데이터 작업 소유는 파트 담당에게 있되 NetworkIdentity, 상태 컴포넌트, 판정 콜라이더, NavMesh·스폰 배선 변경은 {팀장}과 함께 검토한다. 밸런스 값은 [밸런스 데이터 작업](BALANCE_DATA_WORKFLOW.md) 규칙을 따른다.

## 3. 코드 배치와 소유

```text
Assets/01.Scripts/
  Shinshi.Runtime.asmdef                {팀장}
  00.Common/Contracts/                  {팀장}: Unity·백엔드 공용 DTO
  00.Common/Utility/ClientView.cs        {팀장}: 서버 빌드에서 표현 끄기
  01.Network/                           {팀장}: ServerLinks·매니저·클라이언트 인증기
  02.Combat/Player/Net/                  {팀장}: PlayerInputState·PlayerCombatState
  02.Combat/Player/Rules/                전투 담당: 공용 입력·이동·생사 계산
  02.Combat/Player/View/                 전투 담당: 입력·예측
  02.Combat/Damage/                      전투 담당: 피해 계약·공용 계산
  02.Combat/Skill/                      전투 담당: 기술 입력·동료 상태
  {NN}.UI/                              UI 담당
  98.Servers/                           Shinshi.Server (UNITY_SERVER || UNITY_EDITOR || SHINSHI_DEV_TEST)
    Shinshi.Server.asmdef               {팀장}
    01.Network/Bootstrap/              {팀장}: 부트스트랩·실행 설정·프로세스 제어·종료
    01.Network/Presence/               {팀장}: 서버 풀 등록·존재 신호
    01.Network/Backend/                {팀장}: 백엔드 게이트웨이·HTTP 전송·서버 자격
    01.Network/Auth/                   {팀장}: 입장 토큰 검증·인증 대기·참가 명단
    01.Network/Session/                {팀장}: 출정 진행·명단 (Events/ 저장, Settlement/ 정산, Reward/ 드롭)
    02.Combat/                         전투 담당: 서버 틱·Player/·Damage/·Skill/·Boss/(보스 몸체)
    03.AI/Boss/                        AI 담당·{팀장}: 보스 두뇌·패턴
    03.AI/Enemy/                       AI 담당·{팀장}: 일반 적 두뇌(근접·돌진 프로토타입)
    03.AI/Spawn/                       AI 담당·{팀장}: 스폰 계약 (구현 예정)
    04.Systems/Wave/                   웨이브 담당·{팀장}: 웨이브 계약 (구현 예정)
  99.Dev/                               Shinshi.Dev
```

- `98.Servers/` 하위 경로는 `Assets/01.Scripts/{NN}.{도메인}/{기능}/`을 그대로 비춘다. 기능 폴더 아래에 `Server/`를 다시 만들지 않는다.
- `98.Servers/`는 {팀장} 소유다. 예외로 AI·웨이브 경로(`98.Servers/03.AI/`, `98.Servers/04.Systems/Wave/`)는 해당 파트 담당과 {팀장}이 함께 소유한다. 경로별 지정은 `.github/CODEOWNERS`가 기준이다.
- 각 도메인의 `Net/`은 {팀장} 소유다. 새 동기화 필드·`[Command]`·Rpc나 처리기 인터페이스 변경이 필요하면 파트 담당이 {팀장}에게 요청한다.
- 서버 클래스는 프리팹·씬에 붙이지 않는다. `ServerBootstrap`이 `CombatServerInstaller`를 통해 상태 시작·종료 처리기를 등록한다. 상태의 `OnStartServer` 알림을 받은 `CombatServerRuntime`이 해당 서버 판정 객체를 만든다. 인증기·전투 틱·백엔드 펌프·프로세스 수명 드라이버도 서버 시작 때 실행 중에 부착한다.
- 표현·UI는 상태의 읽기 프로퍼티·이벤트와 공개 요청 창구만 쓴다. `Shinshi.Server` 타입은 `Shinshi.Runtime`에서 보이지 않으므로 컴파일 단계에서 막힌다. 같은 어셈블리의 `[Server]` 상태 변경 메서드를 부르는 것은 리뷰로 막는다.
- 서버와 클라이언트가 함께 쓰는 계산(예고 표시가 쓰는 패턴 명세, UI가 쓰는 비용 조회)은 도메인 `Rules/`에 두고 `98.Servers/`로 옮기지 않는다. 서버만 쓰는 계산을 `Rules/`에 두면 클라이언트 빌드에 실린다.

## 4. 기능 하나를 진행하는 순서

1. 기능 이슈에 사용자 행동, 요청 인자, 읽을 상태, 성공·거절 표시를 적는다. 예: 상품 버튼 → 상품 종류·현재 revision 전송 → 서버 결과 후 잔액 갱신.
2. {팀장}이 상태 클래스와 처리기 인터페이스(최소 API 계약과 동작하는 요청 창구)를 먼저 만든다. 파트 담당은 `Rules/`에 공용 계산을, AI·웨이브 담당은 `98.Servers/`에 서버 두뇌를 작성한다. 판정 PR·네트워크 PR·소비자 PR을 서로 연결한다.
3. 표현·UI 담당은 표시용 샘플 상태로 레이아웃·애니메이션을 병행한다. 샘플은 테스트·개발 표현에만 사용하고 생산 서버 상태에 쓰지 않는다. 실제 요청 경로가 없으면 UI에서 사용 불가로 표시한다.
4. {팀장}은 `98.Servers/`의 처리기에서 호출자·소유권·상태·거리·revision·중복 요청 검증을 하고, 규칙을 호출해 상태를 바꾼 뒤 결과를 전달한다. 판정 규칙 자체는 판정 담당 파트가 작성한다. 서버 자체 행동인 AI·스폰은 네트워크 요청 API를 만들 필요가 없다.
5. 클라이언트·UI 코드는 공개 요청 계약대로 요청하고, 결과·현재 상태로 화면을 갱신한다. 현재 입력 창구는 [PlayerInputState](../../Assets/01.Scripts/02.Combat/Player/Net/PlayerInputState.cs), HTTP 계약은 [Contracts 원본](../../Assets/01.Scripts/00.Common/Contracts/ApiRoutes.cs)과 [Backend 안내](../../Backend/README.md)를 확인한다. API나 네트워크 멤버가 부족하면 {팀장}에게 계약 변경을 요청한다.
6. 기능별로 판정·네트워크·소비자 PR을 짧게 병합한다. 서버 계약 변경을 먼저 통합하고, 오래된 소비자를 깨는 변경은 해당 소비자 수정도 함께 준비한다. 브랜치는 담당자 브랜치 `feature/{팀원이름}`을 유지한다.

요청 계약에는 최소한 **호출 가능한 객체·시점, 인자 의미, 반환값 의미, 결과 이벤트, 거절 사유, 늦은 구독·재접속 처리**를 적는다. 모든 메서드의 반환 형식이 같다고 가정하지 않는다.

## 5. 검증과 리뷰

- 클라이언트는 에디터에서 서버·클라를 함께 실행할 때도 같은 공개 요청 경로를 사용한다. `isServer`를 검사해 판정을 직접 호출하는 소비자를 만들지 않는다.
- 서버 코드를 `Shinshi.Runtime` 쪽에서 참조하는 변경은 클라이언트 빌드에서 컴파일이 실패한다. 서버 코드를 건드린 PR은 에디터 컴파일과 함께 **클라이언트 대상 빌드(또는 `UNITY_SERVER` 없이 컴파일)** 가 통과하는지 확인한다.
- 판정 담당 파트는 판정 결과를, {팀장}은 중복·오래된 revision·다른 사람의 객체 요청 거절을 검증한다.
- 표현·UI 담당은 요청 불가·대기·성공·거절 화면과 구독 해제·재바인딩을 검증한다.
- 네트워크 기능은 여러 명 접속에서 승인·거절·각자 상태 표시를 확인한다. [검증 플레이북](../ai/03_VERIFICATION_PLAYBOOK.md)의 L1/L2/L3 기준과 [멀티플레이 테스트 절차](../ai/04_MULTIPLAYER_TEST_PROTOCOL.md)를 따른다. 파일 이동만으로 이전 테스트 결과를 새 코드 검증으로 간주하지 않는다.
- `.github/CODEOWNERS`는 리뷰 담당 지정이다. 2026-10-01부터 민성(@aurenixs), 기웅(@oname36-creator), 진욱(@kjw1237)의 경로별 규칙이 켜져 있다. 필수 승인에는 대상 브랜치의 Code Owner 리뷰 요구 설정이 필요하다. 여러 소유자를 같은 경로에 적으면 그중 한 명의 승인으로 충분하므로, 연결 코드(`Net/`, `98.Servers/`의 요청 처리기·부트스트랩)는 {팀장} 단독으로 둔다. [GitHub 공식 설명](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners)
- {팀장} 본인의 PR은 다른 팀원이 요구사항·검증 증거를 리뷰한다. 팀장이 자기 PR에 승인할 수 있다고 가정하지 않는다. 실제 보호 규칙에 따른 병합 가능 여부를 확인한다.

## 6. 현재 적용 범위

클라이언트 진입 흐름은 `Bootstrap` → `Release/Lobby` → `Release/CharacterSelect` (↔ `Release/CharacterCreate`) → `Release/ShinshiTown`이다. 코드는 `01.Network/Lobby/`(`LobbyFlow`, `LobbyApiClient`, 화면별 `*Screen`)에 있다.

- 로비(`LobbyScreen`): 런처가 넘긴 게임 세션(`LauncherSession`)을 기다린다. 에디터와 개발 빌드에서 런처 없이 실행하면 개발 로그인 패널(API 주소, 아이디, 비밀번호, 가입할 때만 쓰는 이메일, 로그인과 가입 후 로그인 버튼)이 보이고 `POST /v1/auth/exchange` → `POST /v1/auth/game-session`으로 받은 하위 세션을 같은 자리에 넣는다. 개발 로그인의 빌드 ID는 `dev-local`(`-build-id`로 변경), 기본 API 주소는 `http://127.0.0.1:5080`(`-api-base-url`로 변경)이며 API의 허용 buildId와 `AuthBuild__ContentHash`가 맞아야 한다.
- 캐릭터 선택(`CharacterSelectScreen`): `GET /v1/characters`로 최대 3칸을 채우고, 고른 캐릭터로 `POST /v1/town/join-ticket`을 받아 `ShinshiClientAuthenticator`로 마을 서버에 접속한다. 목록 API는 2026-10-11부터 누적 경험치를 제공하며 선택 화면은 같은 레벨 설정으로 레벨을 표시한다. 전투력은 API가 제공하지 않아 `전투력 -`로 표시한다. 전투 HUD와 캐릭터 상세의 레벨 능력치도 경험치에 연결했다.
- 캐릭터 생성(`CharacterCreateScreen`): 직업 3종, Cute 얼굴 1종, 헤어·옷 각 3종과 이름(`GET /v1/characters/name-availability`)을 정해 `POST /v1/characters`로 만든다. 헤어·옷은 직업과 독립적으로 고르고, 슬라이더·색은 중립값을 보낸다. 저장·미리보기·마을 적용 계약은 [캐릭터 커스터마이즈](CHARACTER_CUSTOMIZATION.md)를 따른다.
- 마을: Bootstrap 네트워크 매니저의 `onlineScene`이 `Release/ShinshiTown`이다. 마을 역할(`ConfigureServerRole(Town)`)은 서버와 클라이언트 모두 이 값을 쓰고, 던전 역할은 비운다. 인증이 끝나면 서버가 마을 씬을 안내하고 클라이언트가 불러온 뒤 플레이어가 `NetworkStartPosition`에 스폰된다. 마을에서 연결이 끊기면 캐릭터 선택으로 돌아간다(`offlineScene`은 쓰지 않는다. Mirror가 매니저를 DontDestroyOnLoad에서 꺼내 지우기 때문이다).
- 게임 세션 갱신(`/v1/auth/game-session/refresh`)은 아직 연결하지 않았다. 하위 세션은 15분이라 그 뒤의 목록·입장 요청은 다시 로그인해야 한다.

2026-10-02 작업 트리 기준 다음 타입이 존재한다. 표는 코드의 배치·호출 경계를 설명하며 다인 접속 성공이나 출시 빌드 성공을 뜻하지 않는다.

| 기능 | Runtime·Contracts 원본 | Server 구현 |
|---|---|---|
| 연결·입장 | [ShinshiNetworkManager](../../Assets/01.Scripts/01.Network/ShinshiNetworkManager.cs), [ShinshiClientAuthenticator](../../Assets/01.Scripts/01.Network/ShinshiClientAuthenticator.cs), `JoinRequestMessage`·`JoinResponseMessage`·`ServerLinks` | [ServerBootstrap](../../Assets/01.Scripts/98.Servers/01.Network/Bootstrap/ServerBootstrap.cs), `ShinshiServerAuthenticator`·`ApiJoinTokenVerifier`·`ServerSessionRegistry`·`SessionBinding` |
| 입력·이동·생사 | [PlayerInputState](../../Assets/01.Scripts/02.Combat/Player/Net/PlayerInputState.cs), `PlayerCombatState`, `PlayerInputHeader`, `PlayerMoveAuthority`, `Player/Rules/`, `PlayerInputReader`·`PlayerPrediction` | [CombatServerRuntime](../../Assets/01.Scripts/98.Servers/02.Combat/CombatServerRuntime.cs), `PlayerServer`·`PlayerInputGate`·`ReconnectVault`·`ReviveBudget` |
| 피해·기술 | [피해 계약](../../Assets/01.Scripts/02.Combat/Damage/IDamageResolver.cs), `DamageRules`, `PlayerSkillInput`·[CompanionCombatState](../../Assets/01.Scripts/02.Combat/Skill/Net/CompanionCombatState.cs)·`CompanionView` | `DamageResolver`·`HitLedger`·`CombatTargetRegistry`, `SkillServer`·`CompanionServer`·`ResonanceLedger`, `CompanionSpawnTracker`·`CompanionNetworkSpawner` |
| 출정·개인 보상 | [Sessions DTO](../../Assets/01.Scripts/00.Common/Contracts/Sessions/GameServerRoutes.cs), [Reservations DTO](../../Assets/01.Scripts/00.Common/Contracts/Reservations/ReservationRequest.cs) | [DungeonSessionHost](../../Assets/01.Scripts/98.Servers/01.Network/Session/DungeonSessionHost.cs), `DungeonSession`·`ParticipantRoster`·`SessionEventQueue`·`LootRoller`·`SessionSettlement`·`SettlementRules` |
| API·프로세스 수명 | [Join DTO](../../Assets/01.Scripts/00.Common/Contracts/Join/JoinTokenExchangeRequest.cs), [Auth DTO](../../Assets/01.Scripts/00.Common/Contracts/Auth/AuthExchangeRequest.cs) | `BackendGateway`·`HttpClientBackendTransport`·`ServerCredentials`, `ServerPresence`·`ServerProcessDriver`·`ServerShutdownController` |

- `Shinshi.Runtime`·`Shinshi.Contracts`·`Shinshi.Server`·`Shinshi.Dev`·EditMode/PlayMode 테스트 asmdef가 있다. `Contracts`는 엔진 참조가 없고, [백엔드 csproj](../../Backend/src/Shinshi.Contracts/Shinshi.Contracts.csproj)가 DTO 원본을 링크한다. 참조와 빌드 조건은 [코딩 컨벤션](../coding-convention.md) 0장과 실제 asmdef를 따른다.
- [ClientView](../../Assets/01.Scripts/00.Common/Utility/ClientView.cs)가 `UNITY_SERVER`에서 표현을 끈다. 서버 판정은 `Shinshi.Server`에 있고 Runtime은 이 어셈블리를 참조하지 않는다.
- `DungeonSessionHost.BeginSession`은 현재 웨이브 없는 `DungeonSession`을 생성한다. 일반전 `CreateNormal`과 source ID·보상 등록을 실제 스폰·웨이브 구현에 연결하는 작업, 보스 마지막 단계의 생산 종료 연결(M-Boss)이 남았다. 개발 전용 보스 종료 창구를 완성된 보스전으로 보지 않는다.
- `PF_NetPlayer` 프리팹에는 `PlayerInputReader`와 입력·전투 상태·몸체 Collider·`SkillRangeIndicatorView` 참조가 연결되어 있다. 기술 입력은 Q/W/E/R(모든 직업), Z/X/C(신령사 강림)이며, 방향·자기 위치 기술은 키를 누를 때 바로 요청하고 지점 기술은 범위를 띄운 뒤 왼쪽 클릭으로 확정, 오른쪽 클릭·Esc·회피로 취소한다. 지정점은 서버와 같은 기준([SkillInputRules](../../Assets/01.Scripts/02.Combat/Skill/Rules/SkillInputRules.cs): 사거리 여유 제한, 내비 메시, 높이 차, `Obstacle` 시야)으로 미리 검사하며 판정은 서버가 다시 한다. 시야 검사 [CombatLineOfSight](../../Assets/01.Scripts/02.Combat/Damage/CombatLineOfSight.cs)는 이를 위해 `Shinshi.Runtime`으로 옮겼다. 사람이 직접 하는 기술 입력은 실행 미검증이다. 클릭 이동과 회피는 아래 개발 빌드 실행에서 자동 조작으로 확인했고, 사람이 직접 하는 클릭·공격 입력과 dungeon 역할 다인 접속 수용은 실행 미검증이다. API checkpoint 경로와 DTO는 있지만 `DungeonSessionHost.CompleteTick`의 30초 주기 송신은 TODO다. 직렬화된 참조와 실제 실행 검증을 구분한다.
- 신령사 동료는 서버 소유 네트워크 개체 [PF_NetCompanion](../../Assets/02.Prefab/Network/NetCompanion/PF_NetCompanion.prefab)으로 보인다. 프리팹은 `NetworkIdentity`·`CompanionCombatState`·`NetworkTransformReliable`(위치만, 서버→클라이언트, 적과 같은 10Hz)·`CompanionView`(임시 도형, 종류별 색, yaw로 시각 피벗만 회전)로 구성한다. 연결 권한이 없고 Command가 없어 클라이언트는 요청을 보낼 수 없으며 `OwnerCombatId`로 소유 플레이어(`PlayerCombatState.CombatId`)를 가린다. `ServerBootstrap`이 매니저 `spawnPrefabs`에서 프리팹을 찾아 `CombatServerRuntime.EnableCompanionObjects`에 넘기고, 런타임은 매 틱 부활 승인 뒤 `CompanionSpawnTracker`로 활성 동료와 개체의 짝을 맞춘다. 소환 확정 시 스폰하고, 동료가 멈추는 모든 경로(만료, 본체 사망, 피격 사망, 세션 종료, 보존 해제, 개체 정리)와 재호출(전투 번호 변경, 옛 개체를 내리고 새로 스폰)에서 `NetworkServer.UnSpawn`으로 내려 서버 풀에 둔다. 재접속 보존 중에는 같은 `SkillServer`가 남으므로 개체도 유지된다. 클라이언트는 `ShinshiNetworkManager`가 `spawnPrefabs`를 적 복제 간격 처리기로 다시 등록해 만든다. 등록은 {팀장} 소유 `Bootstrap`·`Dev_PlayerMovement` 씬에만 했고, 프리팹이 없는 씬의 서버는 경고만 남기고 동료 판정만 돌린다. 추적 규칙은 EditMode, 스폰·풀 재사용·클라이언트 처리기는 에디터 Play Mode 단독 호출로만 확인했고 실제 시전·다인 접속은 실행 미검증이다.
- 서비스 장애 취소의 현행 정책은 완료 웨이브 기본 정기와 서버 원장으로 확인된 미정산 추가 정기를 모두 100% 지급하는 것이다(2026-10-08 사용자 결정, 기존 추가 50% 정책 대체). 2026-10-10부터 `SettlementRules`의 `ServiceCancelled`와 API 서비스 장애 취소는 추가 정기도 100%, `Assets/09.Data/Session/SessionConfig.asset`은 2(`FullGrant`)다. 실제 서버 소실 검증은 남아 있으며 운영자 `ForceClose`는 추가 50%를 유지하는 별도 정책(기획 미정)이다. 일반 실패·포기의 추가 정기 50% 보존은 유지한다. 상세는 [기술 설계](../03-technical-design.md)와 [로드맵](../05-implementation-roadmap.md)을 따른다.
- 코드 확인과 문서 링크·diff 점검만 이번 동기화에서 수행했다. 기존 실행 근거로 제공된 결과는 **사용자 제공 Unity Test Runner EditMode 780개 통과, PostgreSQL 18 Docker를 사용한 Backend 396개 통과**다. 이번 작업 트리의 새 실행 검증으로 재표시하지 않는다.
- 2026-10-02 `Dev_PlayerMovement` 씬의 Windows 개발 서버 빌드 1개와 클라이언트 빌드 2개를 같은 PC에서 town 역할로 실행해 접속, 소유 이동 예측, 회피, 원격 표시를 지연 시뮬레이션 RTT 0/50/150ms에서 확인했다(자동 조작, 스냅 0회, 정지 시 서버 위치 차이 0). Linux 서버·M3·IL2CPP·배포·dungeon 역할 다인 접속은 실행하지 않았다. [Start-M2.ps1](../../tools/Start-M2.ps1)과 [배포 템플릿](../../deploy/gameserver/shinshi-dungeon@.service)은 존재하지만 실행 결과는 미검증이다. GitHub 브랜치 보호 설정 적용 여부도 별도 확인이 필요하다.
- 보스 AI 서버 뼈대(`98.Servers/03.AI/Boss/`, `Shinshi.AI`)와 서버 연결(`98.Servers/02.Combat/Boss/`), 복제 상태(`03.AI/Boss/Net/BossState`)는 8장에 있다. 서버 틱 구동·세션 결과·네트워크 개체 스폰·사건 복제·늦은 접속 복원은 구현됐고 EditMode만 검증했다(전용 서버·다인 접속 미검증). 2026-10-12 `BossNetworkViewBinder`를 네트워크 프리팹에 연결했으며 실접속 수용은 남았다.
- 이후 파일 분리·이동에서도 `.cs.meta` GUID와 씬·프리팹 참조를 보존한다.

## 7. 일반전 웨이브 서버 경계

웨이브·스폰 작성(기웅)과 세션 연결(민성)은 아래 인터페이스로 만난다. 스폰·웨이브 실행 클래스는 아직 없고, 민성 쪽 수신 구현인 `DungeonSession : IWaveSessionListener`와 출정 호스트·저장 큐는 존재한다. 작성·소유 경계는 그대로 유지한다.

| 파일 | 네임스페이스 | 구현 | 호출 |
|---|---|---|---|
| `98.Servers/03.AI/Spawn/ISpawnDirector.cs` | `Shinshi.AI` | 기웅 | 웨이브 세션이 `TryRequestSpawn(waveIndex, sourceId)`로 출현 요청, 활성 수·대기 수 조회, `Halt` |
| `98.Servers/03.AI/Spawn/ISpawnProgressListener.cs` | `Shinshi.AI` | 웨이브 세션(기웅) | 스폰 디렉터가 출현·처치·소멸을 알림 |
| `98.Servers/04.Systems/Wave/IWaveSession.cs` | `Shinshi.Systems` | 기웅 | 세션 바인딩이 `Begin`·`Tick`(서버 시각)·`Abort`, `Progress` 조회 |
| `98.Servers/04.Systems/Wave/IWaveSessionListener.cs` | `Shinshi.Systems` | `DungeonSession`(민성, 구현됨) | 웨이브 세션이 처치·웨이브 종료·세션 종료(`Success`/`Timeout`)를 알림 |
| `WaveSessionOutcome`·`WaveProgressSnapshot` | `Shinshi.Systems` | 값 타입 | 종료 결과, HUD 동기화 값 |

- 의존은 `Wave → Spawn` 한 방향이다. 세션 바인딩은 `IWaveSession`·`IWaveSessionListener`만 알고 스폰 디렉터를 직접 부르지 않는다.
- 웨이브 쪽은 `WaveConfigSO` 내부 필드를 계약에 노출하지 않고 `int waveIndex`(0부터)와 `string sourceId`만 주고받는다. 참가자 ID는 `string`이다.
- 활성 상한 때문에 바로 나오지 못한 출현은 디렉터가 대기열에 넣고 빈자리에 순서대로 내보낸다. 수락한 요청은 버리지 않는다(지연 스폰 누락 0).
- 적의 퇴장은 활성 수를 줄여야 하므로 반드시 스폰 디렉터를 거쳐 통지한다.
- 개인 드롭은 웨이브에서 판정하지 않는다. 세션 바인딩이 `OnEnemyDefeated`의 `sourceId`를 원인 ID로 개인 판정에 넘긴다. 처치자 ID는 기록용이며 보상 자격은 참가자 명단이 정한다.
- 한 처치가 웨이브를 끝내면 `OnEnemyDefeated → OnWaveCleared → OnWaveSessionEnded` 순서로 부른다. `DungeonSession`은 종료 후보를 보관한 뒤 `CompleteTick`에서 원인을 확정하고 `SessionEnded` 이벤트를 저장한다. `DungeonSessionHost`는 그 이벤트의 저장 확인 뒤 `SessionSettlement`로 정산을 요청한다. 서비스 장애 취소 정산은 API 복구 경로가 맡는다.
- 제한 시간은 `Begin`에 넘긴 시각(Running 진입)부터 잰다. 시각은 바인딩이 `NetworkTime`에서 읽어 넘기며 웨이브 코드는 Mirror를 참조하지 않는다.
- `Abort`는 서비스 장애 취소처럼 바깥 원인으로 멈출 때 쓰며 결과 통지를 보내지 않는다.
- 마지막 처치와 420초 도달이 같은 틱이면 **성공 우선**이다(2026-10-01 결정). `DungeonSession.CompleteTick`이 피해·처치 처리 뒤 종료 후보를 평가하고 성공을 시간 초과보다 먼저 적용한다. 같은 틱에 겹친 종료 후보는 저장 장애 → 보스 기믹의 최종 성공 → 기믹 실패 → 전멸 → 일반 성공 → 전원 이탈 → 시간 초과 순으로 하나만 적용한다(2026-10-14). 보스전의 일반 처치는 같은 틱 전원 사망에 지고(전멸 우선), 하드 월백 최후의 영역이 내준 최종 성공만 전멸보다 앞선다. 부활 승인은 종료 판정 뒤다.
- 웨이브 실행 객체의 생성·호스트 주입, 소멸 적 집계와 시간 초과 뒤 남은 적 정리는 기웅 쪽 실행 구현·연결 작업이 남아 있다. `IWaveSession.Tick`의 과거 미정 주석을 새 기획 판단의 근거로 쓰지 않는다.

## 8. 보스 서버 경계

보스 AI의 서버 뼈대를 `98.Servers/03.AI/Boss/`(`Shinshi.AI`, 기웅 작성, 민성 공동 리뷰)에 구현했다. 서버 시각을 바깥에서 받아 진행하는 일반 C# 클래스이며 Mirror와 `MonoBehaviour`에 의존하지 않는다. 가짜 의존성으로 EditMode 테스트(`98.Servers/03.AI/Boss/Tests/`의 별도 asmdef `Shinshi.Tests.Server.AI`)를 돌려 검증했다. 전용 서버와 클라이언트를 띄운 실행 검증은 하지 않았다. `Assets/Tests/`가 민성 소유라 테스트를 이 폴더에 두었다.

구조는 `상위 FSM(BossBrain) → Combat(BossCombat) → PhaseState / AttackState FSM → Combat BT → 상황별 가중치 선택 → 패턴 실행`이다.

| 클래스 | 책임 |
|---|---|
| `BossBrain` | 상위 FSM(`Inactive → Appearing → Combat → Defeated / Ended`). 등장 대기와 사망, 전투 종료를 판단해 취소를 지시한다. 진입점 `Begin`·`BeginAppearance`·`Tick(serverTime)`·`RequestCombatEnd` |
| `BossCombat` | 전투 중 한 틱의 순서를 지키고 두 FSM과 BT를 묶는다 |
| `BossPhaseFsm` | `Active(1) → TransitionPending → AwaitingHandoff(2) → Active(2)` 또는 `AwaitingReadiness`. 전환은 1페이즈에서 체력이 전환 비율 이하(경계 포함)가 될 때 한 전투에 한 번만 시작하고, 전환을 마치는 순간 페이즈 번호가 2가 된다. `BossBrain.CompleteHandoff()`가 인계를 마치면 2페이즈에 실행 가능한 패턴이 있을 때 `Active`, 없으면 `AwaitingReadiness`(새 선택도 오류 로그도 없이 `EmptyWeightRetrySeconds` 주기로 준비를 다시 확인)로 간다. 새 선택은 `Active`일 때만 허용한다 |
| `BossAttackFsm` | `Ready → Telegraph → Execute → Recover → Ready`. 단계 타이머와 직접 제어 구간을 서버 시각으로 계산한다 |
| `BossSelectionTree`, `Bt*` | 경량 BT. 공격 FSM이 `Ready`이고 페이즈가 `Active`일 때만 평가한다 |
| `BossPatternSelector` | 거리·방향·주변 인원 구간과 가중치 곱, 비례 선택. 상태 없는 순수 계산 |
| `BossDamageGate` | 보스의 피해 창구. 전투 중에만 열리고 사망이나 전투 종료로 닫히면 다시 열리지 않는다 |
| `BossFanBlastPattern` | P02 전방 부채꼴 요력 폭발. 시간과 범위와 피해 배수는 패턴 정의에서 읽고 `DamageRef x Href`를 방어 전 `RawDamage`로 낸다. 예고, 추적 중 방향 변경, 방향 고정, 타격, 취소 시점은 `IBossFanTelegraphListener`로 알린다(구현 없음) |
| `BossCircleBlastPattern` | P03 중심 원형 여우불 폭발. 예고 시작 때 보스의 서버 위치를 공격 중심으로 저장하고 예고 중 보스와 타깃이 움직여도 바꾸지 않는다. 예고가 끝나면 반경 안(수평 거리, 경계 포함)의 살아 있는 플레이어와 소환물을 한 번 타격하며 `DamageRef x Href`를 방어 전 `RawDamage`로 낸다. 예고 시작, 타격, 취소 시점은 `IBossCircleTelegraphListener`(중심, 반경, 시전 번호)로 알린다(구현 없음). 부채꼴 알림과 별도 인터페이스다 |
| `IBossPattern`, `BossPatternBase` | 패턴 한 번의 실행. 피해는 `IDamageResolver`가 아니라 게이트로만 낸다. 취소나 게이트 폐쇄 뒤에는 피해 요청이 나가지 않는다 |

확정된 규칙(2026-10-02).

- 한 틱의 우선순위는 사망·전투 종료 → 페이즈 전환 → 허용된 직접 제어 → 일반 공격 진행·새 선택이다. 이미 적용된 피해는 되돌리지 않는다.
- 상위 FSM이 중단 이유(`EBossInterruptReason`)를 정하고 취소를 지시한다. 현재 패턴은 자신의 실행과 예약 타격을 정리하고, 지속 장판과 투사체는 `IBossEffectLifetime`이 이유별로 정리한다.
- 예고, 실행, 회복 중에도 사망, 페이즈 전환, 직접 제어 검사는 계속한다. BT는 새 행동을 고를 수 있을 때만 평가한다.
- 기믹이 붙지 않은 보스는 체력 0이면 사망을 확정한다(`ImmediateBossDeathPolicy`). 월백 체력 기믹(75% 부적진, 50% 색 결계, 25% 여우불 봉인, 하드 0% 최후의 영역)을 붙인 전투에서는 기믹이 받는 피해 상한과 사망 보류를 걸어 체력 0에서도 보류가 풀릴 때까지 사망을 확정하지 않는다(2026-10-14 운영 서버 연결, 구현됨, 실행 미검증). 기믹은 기본 꺼짐이며 켜는 방법과 세션 처리는 아래 "월백 체력 기믹의 세션 처리" 항목에 적었다.
- 보스 사망과 전원 사망이 같은 시각이면 기획 문서의 초기 기준대로 전멸 실패를 우선한다(실제 전투 피드백 뒤 조정될 수 있다). 예외는 하드 월백 최후의 영역의 최종 성공 하나이며 같은 틱 전멸보다 앞선다. 뼈대는 두 사실을 모두 기록(`IsBossDeathRecorded`, `IsCombatEndRecorded`)하고 `FlowState`는 `Ended`가 된다. 플레이어 사망 상태는 바꾸지 않으며 승패 판정은 세션이 한다.
- 보스가 죽으면 남은 장판과 투사체를 즉시 제거하고 예약 타격을 포함한 추가 피해를 막는다. 이미 적용된 피해는 되돌리지 않는다. 막는 방법은 `IBossEffectLifetime`의 즉시 제거와 `BossDamageGate`의 폐쇄 두 겹이다. 전투 종료(전멸 등)에서도 같은 정리를 한다.
- 등장 컷씬: 보스는 컷씬 전에 미리 스폰한다. `BeginAppearance(serverTime, 길이)`로 `Appearing` 상태에 들어가며, 이 동안은 타깃 선택, 공격, 피해 처리를 하지 않고 등장 모션만 허용한다. 전투 시작 시점은 서버 시각으로만 정하며 클라이언트 컷씬 종료 신호는 받는 API가 없다. 컷씬을 언제 시작하고 길이를 얼마로 할지(전원 준비 시 진행, 이미 본 참가자의 별도 대기 등)는 기획 문서와 세션이 정하며, 뼈대는 시작 시각과 길이를 받기만 한다.
- 등장 대기와 `OpeningDelaySeconds`는 이어서 적용한다. 전투 시작은 등장 대기가 끝나는 시각이고, 첫 선택은 거기서 `OpeningDelaySeconds` 뒤다. 틱이 늦어도 시작 시각은 등장 대기가 끝나는 경계로 잡는다.
- 고정 오프닝과 강제 후속 공격은 이번 구현에서 제외한다. 등장 대기가 끝나면 가중치로 첫 패턴을 고르고, 각 패턴의 회복이 끝난 뒤 그 시점의 상황(타깃, 거리, 방향, 주변 인원, 최근 기록)을 다시 읽어 다음 패턴을 가중 선택한다. 일반 가중 선택 앞에 평가되는 `IBossSelectionRule` 자리는 유지하며 등록된 규칙은 없다.
- 직접 제어는 기획에 적힌 규칙대로 한다. 패턴별 허용 구간에서만 받고, 한 패턴에 최초 1회이며 적용 뒤 면역 시간 동안은 받지 않는다. 취소형 패턴(P06)은 예고 마지막 구간에서 성공하면 공격을 취소한다. 속박 등 취소형이 아닌 제어는 보스 동작이 협의 전이라 적용 표시와 면역 시작과 알림만 하고, 제어를 이동·회전·공격 정지로 일괄 해석하지 않는다. 가능성을 열어 두려고 제어는 값 묶음(`BossDirectControl`: 출처 ID, 요청 시간)으로 받고, 적용된 제어는 `BossBrain.DirectControlApplied` 알림(`BossDirectControlApplied`: 적용 시간(보스 정의의 최대 시간으로 자름), 패턴 취소 여부, 패턴 ID)으로 알린다. 보스를 실제로 어떻게 움직일지는 이 알림을 받는 쪽에서 나중에 붙인다.

월백 2페이즈 선택 기반(Easy, 2026-10-03, EditMode 검증, 실제 서버 실행 미검증). 개별 공격은 P01~P06 여섯 개를 모두 구현했다. Hard 전용 낙하와 HP 0% 발악은 이 선택 기반의 범위 밖이다. HP 0%는 월백 체력 기믹의 최후의 영역(외곽 바닥 제거와 낙하 사망 포함)이 맡으며 2026-10-14에 운영 서버에 연결했다(구현됨, 실행 미검증, 기본 꺼짐. 아래 "월백 체력 기믹의 세션 처리" 항목).

- 전환: 전체 최대 체력의 50% 이하(경계 포함)에서 한 번만 시작한다. 진행 중인 공격과 지속물과 예약을 정리하고, 최근 패턴 기록과 직전 그룹을 비운다. 사망과 전투 종료는 전환과 새 선택보다 먼저 처리한다. 연출 시간, 무적, 순간이동은 두지 않았다.
- 패턴 ID와 그룹: 1페이즈 ID(`wolbaek_p01`~`06`)는 그대로이고 2페이즈는 `wolbaek_ph2_p01`~`06`이다. P01 회전 빔, P02 절단 빔, P03 반월 연쇄는 Large, P04 강화 장판과 P06 원형 폭발은 Pressure, P05 투사체는 Basic이다. 그룹은 `BossPatternCatalogSO.EPatternGroup`이다.
- 가중치: `FinalWeight = 기본값 x 거리 x 방향 x 주변 인원 x 최근 기록 x 흐름 배율`(`BossPatternSelector.ComputeSecondPhaseWeight`). 체력이 25% 이하(경계 포함)이면 강화 기본값과 강화 흐름 배율을 쓰고, 직전 그룹이 없는 첫 선택의 흐름 배율은 1이다. 최근 배율은 0, 0.3, 0.7, 1이며 비례 랜덤 선택이다. 기록은 예고를 실제로 시작한 시점에 갱신한다. 진행 중인 공격은 체력이 변해도 바뀌지 않는다. 표 값은 기획안과 같고 `BossPatternCatalog.asset`(패턴 12개, 보스의 `EnhancedHealthRate`와 `FlowMultipliers`)에 입력했다.
- 데이터와 실행 준비의 분리: 선택 데이터를 먼저 넣었고 공격 시간, 범위, 피해량은 여섯 패턴 모두 채웠다(P01은 시작안이라 테스트로 조정한다). `IsTuned`는 1페이즈 기준 그대로이며, 2페이즈는 `IsPhaseTuned(보스, 2)`와 패턴별 `IsPatternReady`가 따로 본다. 2페이즈 패턴은 데이터가 지정되고(`IBossPatternReadiness`) 팩토리가 만들 수 있을 때(`IBossPatternAvailability.CanCreate`)만 후보가 된다. `BossServer`가 빔 관리자(`BossBeamManager`), 별형 빔 관리자(`BossStarManager`), 전장 입력(`IBossArenaSource`, 몸체의 전장 중심과 반경)을 넘기므로(2026-10-03) 서버에서는 P01~P06 여섯 개가 모두 만들 수 있는 후보다(EditMode 테스트 작성, 실행 미검증). 팩토리는 여전히 빔이나 별형 빔 창구, 전장 입력이 없으면 P02, P01을 만들지 않는다. 1페이즈 선택 동작은 바꾸지 않았다.
- 한계: 실행 가능한 패턴이 하나뿐이면 직전 기록 x0 때문에 그 패턴이 쓰인 뒤 다음 선택이 비어 재조회만 반복한다(오류 로그는 없다). 공격을 단계적으로 추가할 때 둘 이상이 준비돼야 전투가 이어진다.

- 2페이즈 P06 보스 중심 원형 여우불 폭발(`wolbaek_ph2_p06`, Pressure, 2026-10-03, EditMode와 PlayMode 검증, 실제 서버 실행 미검증): 새 실행 클래스 없이 1페이즈 원형 공격(`BossCircleBlastPattern`)을 그대로 쓰고 팩토리의 `Create`와 `CanCreate`에 연결했다. 수치는 에셋 정의에서 읽는다. 예고 1.2초, 반경 5m, 방어 전 피해 0.28Href, 반격 가능, 즉발(발동 0초), 회복 0.8초, 직접 제어 구간 없음이다. 예고 시작 때 중심을 보스의 서버 위치로 고정하고 예고가 끝나면 한 번 타격한다. 체력 25% 이하에서도 공격 수치는 같고 강화 기본값과 흐름 배율만 선택에 적용한다. 다단 타격, 잔류 장판, 넉백은 없다. 표현은 기존 원형 예고를 그대로 쓰고 서버 확정(`Strike`) 전에는 폭발하지 않는다. 로컬 시험 연결부(`BossTelegraphLocalBridge`)는 진행 중인 패턴 ID를 읽어 이 시전을 2페이즈 ID로 표시한다. 원형 알림에 패턴 ID가 없어 실제 복제 계층도 시전의 패턴 ID를 함께 보내야 한다.
- 2페이즈 P05 순차 여우불 투사체(`wolbaek_ph2_p05`, Basic, 2026-10-03, EditMode와 PlayMode 검증, 실제 서버 실행 미검증): 새 클래스 없이 1페이즈 P04의 `BossProjectilePattern`과 `BossProjectileManager`를 확장해 썼다. 예고 0.9초, 처음 0.45초 최대 120도/초 추적 뒤 방향 고정, 같은 방향으로 3발을 예고 종료 순간, 0.3초 뒤, 0.6초 뒤에 쏘고 마지막 발 뒤 회복 0.5초다. 직접 제어는 회복 구간 0.5초만 받는다. 발마다 1페이즈 P04와 같이 폭 1m, 속도 10m/초, 사거리 35m, 방어 전 피해 0.18Href, 반격 가능이고 체력 25% 이하에서도 같다.
  - 데이터: 패턴 정의에 `ProjectileCount`와 `ProjectileIntervalSeconds`를 더했다. 미지정(0)은 한 발이라 1페이즈 P04는 그대로 단발이다. 실행 구간(`ActiveSeconds`)은 첫 발부터 마지막 발까지(`(발 수 - 1) x 간격`)를 덮어야 하고 검증이 이를 확인한다.
  - 발사: 발의 예약 시각은 실행 시작 경계 + k x 간격이고 발사 시각은 틱 시각이 아니라 이 예약 시각이다. 첫 발은 실행 단계에 들어가는 순간, 나머지는 실행 중 틱에서, 실행 끝 경계의 마지막 발은 회복 단계에 들어가는 순간 낸다. 큰 틱에서도 세 발이 중복 없이 모두 나간다. 발마다 새 동작 번호를 받고 발사 위치는 그 발을 처리하는 시점의 보스 서버 위치, 경로 길이는 그때 기존 벽 조회 규칙으로 잰다. 한계: 과거 위치를 기록하지 않아 틱이 거칠면 늦게 처리된 발은 예약 시각의 위치가 아니라 처리 시점의 보스 위치와 벽 조회를 쓴다.
  - 발별 식별: 한 시전의 발들은 시전 번호를 함께 쓰므로 투사체 한 발은 동작 번호(`ActionInstanceId`)로 구분한다. 투사체 알림 `IBossProjectileListener`의 발사와 소멸에 `projectileId`를 더했고(1페이즈 P04도 동작 번호를 쓴다), 지속물 관리자는 같은 동작 번호의 중복 발사를 받지 않는다. 시전 번호 단위 정리(`Cancel`)는 그대로다. 클라이언트의 투사체 시작 값(`BossProjectileStart`)은 투사체 번호와 시전 번호(`CastId`)를 따로 가지며, 첫 발사가 같은 시전의 예고를 마무리한다. 한 발의 소멸이나 취소는 나머지를 지우지 않는다.
  - 정리: 사망, 페이즈 전환, 전투 종료는 게이트를 닫고 미발사 예약과 비행 중 투사체를 모두 정리한다. 회복 중 직접 제어와 정상 종료는 이미 쏜 투사체를 지우지 않는다. 동시에 날 수 있는 투사체는 8개이고 가득 차면 새 발은 발사되지 않는다.
  - 표현: 기존 P04 예고와 비행 표현을 그대로 쓴다. 서버 발사 알림마다 한 발이고 클라이언트가 스스로 세 발을 만들지 않는다. 직사각형 표현 풀은 예고와 세 발이 함께 쓰므로 4개에서 8개로 늘렸다.
- 2페이즈 P04 강화형 지정 여우불 장판(`wolbaek_ph2_p04`, Pressure, 2026-10-03, EditMode와 PlayMode 검증, 실제 서버 실행 미검증): 새 클래스 없이 1페이즈 P05의 `BossAreaPattern`과 `BossAreaManager`를 확장해 썼다. 예고 1초, 반경 3m, 지속 6초, 간격 1초, 회당 방어 전 0.08Href, 방어 불가, 회복 0.5초, 직접 제어 없음이다. 서로 다른 생존 플레이어 최대 2명(솔로는 1명)에게 장판을 하나씩 동시에 설치하고 체력 25% 이하에서도 대상 수와 수치는 같다. 활성 순간이 첫 피해라 정상 진행에서 활성 기준 0~5초에 6회, 6초에 추가 타격 없이 끝난다.
  - 데이터: 패턴 정의에 `AreaTargetCount`(미지정 0은 단일 장판)와 `AreaSkipsMissedRounds`를 더했다. 1페이즈 P05는 값이 없어 단일 장판과 기존의 놓친 회차 모두 처리를 그대로 유지한다. 대상 수가 2 이상이면 `MaxConcurrentAreas`가 그 이상이어야 한다는 검증이 있다.
  - 대상 선정: 첫 대상은 두뇌가 고른 타깃이고(소환물과 사망자 제외), 둘째는 남은 생존 참가 플레이어(미접속 생존자 포함)에서 두뇌와 같은 난수 의존성(`BossPatternContext.Random`)으로 균등하게 뽑는다. 같은 플레이어를 두 번 뽑지 않고 위치는 예고 시작 순간 서버 위치로 고정한다. 위치를 얻지 못한 대상에게는 설치하지 않고 보스 위치로 대신하지 않는다. 한 명도 정하지 못하면 패턴이 시작을 거절하고(`IBossPatternStartResult`) 두뇌는 기록 없이 기존 재조회 시간 뒤에 다시 고른다.
  - 슬롯 상한: 같은 보스의 기존 월백 장판과 P04의 예약과 활성을 합쳐 최대 3개다(`TryReserveWithinTotal`, 검사와 예약이 한 번에 이뤄진다). 빈 슬롯이 둘 이상이면 두 개, 하나면 한 개, 없으면 후보에서 제외한다(선택 트리가 `IBossAreaCapacity.TotalAreaCount`로 보스 전체 수를 센다). 기존 장판은 지우지 않고, 예약 실패, 예고 취소, 활성화 실패, 수명 종료에서 슬롯을 정확히 한 번 돌려준다. 1페이즈 P05는 패턴별 상한을 그대로 쓴다. 참고: 페이즈 전환은 기존 장판을 모두 정리하므로 2페이즈의 기존 장판은 2페이즈에서 새로 만든 것이다.
  - 장판별 식별: 한 시전의 장판들은 시전 번호를 함께 쓰고 장판마다 서버가 발급한 장판 번호(`BossDamageGate.IssueInstanceId`)를 가진다. 슬롯, 활성, 타격, 종료 알림, 클라이언트 목록과 복원 모두 장판 번호가 키다. 알림 인터페이스(`IBossAreaTelegraphListener`, `IBossAreaListener`)에 `areaId`를 더했고 시전 번호를 넘긴 정리(`Cancel`)만 그 시전의 장판을 함께 지운다. 장판의 수명 종료는 그 장판 하나만 끝낸다.
  - 피해: 각 장판은 독립된 공격이다. 겹치면 같은 대상이 장판마다 한 번씩 맞아 합계가 0.16Href(기존 장판까지 셋이면 0.24Href)이고 공용 쿨다운이나 상쇄는 없다. 장판의 회차마다 현재 범위를 다시 조회해 대상당 한 번만 판정하고(중심점, 경계 포함, 소환물 포함) 장판별 회차마다 새 동작 번호를 쓴다. 지연된 틱에서는 장판 하나당 한 틱에 가장 최근 도래한 회차만 현재 위치로 판정하고 이전 회차는 건너뛰며 다음 회차는 원래 활성 시각 기준이다. 종료 시각을 지났으면 미처리 회차 없이 끝난다. 이 제한은 장판 안에서만 적용된다. 한계: 과거 위치를 기록하지 않아 건너뛴 회차는 복원하지 않는다.
  - 정리: 사망, 페이즈 전환, 전투 종료는 게이트를 닫고 예약과 활성 장판과 미처리 피해를 모두 정리한다. 정상 종료, 다음 공격 시작, 이후 공격의 직접 제어는 기존 장판을 지우지 않는다.
  - 표현: 기존 원형 예고와 지속 장판 표현을 그대로 쓴다. 한 시전의 예고 둘은 묶음 번호(`BossTelegraphStart.GroupId`)로 서로 지우지 않고(다른 시전의 새 예고는 기존대로 이전 예고를 끝낸다), 장판은 서버 활성 알림마다 하나씩 생긴다. 장판 표현 풀은 활성 장판과 옅어지는 장판이 함께 쓰므로 4개에서 6개로 늘렸다.
- 2페이즈 P03 반월 연쇄 폭발(`wolbaek_ph2_p03`, Large, 2026-10-03, EditMode와 PlayMode 검증, 실제 서버 실행 미검증): 전용 실행 클래스(`BossSweepBlastPattern`, 시전 전체 피격 기록과 구역 순서 관리)이고 부채꼴 판정은 1페이즈 P02와 같은 `BossFanShape`로 공용화했다(P02의 판정은 바꾸지 않고 같은 함수를 부르게만 했다). 예고 1.2초, 반경 6m, 선택한 쪽 반원 180도를 45도 4구역, 구역 피해 0.18Href 반격 가능, 실행 0.75초(첫 폭발부터 마지막 폭발까지), 회복 0.8초, 직접 제어 없음이다. 체력 25% 이하에서도 수치와 순서는 같고 강화 기본값과 흐름 배율만 선택에 적용한다.
  - 데이터: 패턴 정의에 `SweepZoneCount`(구역 수)와 `SweepIntervalSeconds`(간격)를 더했다. 전체 각도와 반경은 형태(`Fan`)의 `ArcAngle`과 `Range`다. 구역 하나의 각도는 전체 각도를 구역 수로 나눈 값이다. 검증이 부채꼴 형태, 간격, 실행 구간이 첫 구역부터 마지막 구역까지를 덮는지 확인한다.
  - 시작: 예고 시작 순간 보스 서버 위치를 중심으로, 유효 타깃(소환물과 사망자 제외)을 향한 수평 방향을 기준 방향으로 고정한다. 이후 타깃 이동이나 보스 회전으로 바뀌지 않는다. 좌우는 두뇌와 같은 난수 의존성(`BossPatternContext.Random`)으로 각각 50%다. 유효 타깃이 없으면 보스 위치나 임의 방향으로 대신하지 않고 시작을 거절하며(`IBossPatternStartResult`) 두뇌는 최근 사용과 직전 그룹을 기록하지 않고 기존 재조회로 다시 고른다. 같은 수평 위치라 방향을 못 구하면 기존 월백 조준 규칙대로 보스의 유효 정면을 쓴다. 시작 뒤 타깃이 죽거나 나가도 공격은 취소되지 않는다.
  - 시간과 큰 틱: 구역 k의 예약 시각은 실행 시작 경계 + k x 0.25초이다. 첫 구역은 실행 단계 진입 순간, 나머지는 실행 중 틱, 실행 끝 경계의 마지막 구역은 회복 단계 진입 순간에 놓치지 않고 낸다. 한 틱에 도래한 구역은 순서대로 한 번씩 처리하고 폭발 시각은 틱 시각이 아니라 예약 시각이다. 한계: 과거 위치를 기록하지 않으므로 늦게 처리된 구역은 예약 시각의 대상 위치가 아니라 처리 시점의 위치로 판정한다.
  - 경계 판정: 구역마다 폭발 시점에 현재 대상(살아 있는 플레이어와 소환물, 미접속 생존 참가자 포함)을 다시 조회하고 중심점 기준 거리와 각도 경계를 포함한다(구역 부채꼴의 가운데 방향에서 45도의 절반 + 0.001도 이내). 피격 기록은 시전 전체에서 하나라 방어, 반격, 무적 결과와 상관없이 처리한 대상은 다시 판정하지 않는다. 그래서 이웃 구역의 공유 경계와 보스 중심점의 대상은 먼저 폭발하는 구역에서 한 번만 맞고, 여러 구역을 가로질러도, 밀린 구역이 몰려도 대상당 한 번이다. 구역마다 새 동작 번호(구역 번호)를 쓰고 시전 번호는 함께 쓴다.
  - 정리: 사망, 페이즈 전환, 전투 종료는 게이트를 닫고 미처리 구역과 예고를 정리한다. 이미 적용한 피해는 되돌리지 않는다.
  - 표현: 구역마다 기존 부채꼴 예고를 하나씩(구역 번호가 키, 시전 번호가 묶음 번호) 그리고 구역의 예고 길이는 그 구역의 폭발 시각까지다. 서버 구역 폭발 알림(`IBossSweepTelegraphListener`)이 와야 그 구역이 터지고 예고 시간이 끝나는 것만으로는 터지지 않는다. 여우불 하나(`BossSweepCueView`, 임시 구체)가 서버가 정한 쪽 반원을 서버 시각으로 따라 이동해 방향을 미리 알린다. 클라이언트는 좌우 추첨, 타깃 추적, 판정을 하지 않는다. 취소는 묶음 단위(`CancelGroup`)로 아직 폭발하지 않은 구역과 여우불을 정리하고 이미 폭발한 구역은 끝까지 간다. 부채꼴 표현 풀은 구역 4개를 수용하도록 4개에서 8개로 늘렸다.
- 2페이즈 P02 필드 절단 여우불 빔(`wolbaek_ph2_p02`, Large, 2026-10-03, EditMode와 PlayMode 검증, 실제 서버 실행 미검증): 전장 중심에서 기준 방향의 좌우 5m에 폭 2m 평행 빔 두 줄을 놓는다(중심선 사이 10m, 가운데 안전 간격 8m). 피해 영역은 폭 띠와 원형 전장의 교집합이다(중심선만 전장에 맞춰 자르지 않는다). 예고 1.5초, 유지 4초(실행 구간), 활성 순간부터 0.5초 간격 8회(0~3.5초, 끝 시각에 추가 타격 없음), 회당 방어 전 0.08Href, 방어 불가, 회복 0.8초, 직접 제어 없음이다. 체력 25% 이하에서도 수치는 같고 강화 기본값과 흐름 배율만 선택에 적용한다.
  - 기하: 공용 순수 계산(`BossBeamGeometry`, `BossBeamShape`, `03.AI/Boss/Rules`)이 배치(`Place`)와 포함 판정(`Contains`)을 맡는다. 서버 판정, 표현 메시, 시험이 같은 함수와 값을 쓰므로 경계와 끝이 일치한다. 줄 수와 오프셋만 바꾸면 P01 회전 빔 같은 다른 빔도 이 계산을 재사용할 수 있다(회전과 추적은 넣지 않았다).
  - 시작: 전장 중심과 반지름은 전장 입력(`IBossArenaSource`)에서 얻고 보스 위치를 전장 중심으로 보지 않는다. 기준 방향은 예고 시작 순간 보스에서 유효 타깃(소환물과 사망자 제외)을 향한 수평 방향이고, 같은 수평 위치면 보스의 유효 정면을 쓴다. 예고 시작에 고정하고 이후 타깃, 보스, 전장 입력이 바뀌어도 바꾸지 않는다. 전장 정보나 유효 타깃을 못 얻거나 슬롯을 모두 예약하지 못하면 시작을 거절하고(`IBossPatternStartResult`) 이미 예약한 슬롯을 돌려주며 예고도 알리지 않는다. 두뇌는 기록 없이 기존 재조회로 다시 고른다.
  - 구조: 패턴 객체(`BossBeamPattern`)는 배치 고정, 예약, 예고, 활성만 맡고 유지 시간 동안의 주기 피해와 정리는 빔 관리자(`BossBeamManager`, `IBossEffectLifetime`, `IBossTickableEffects`, `IBossBeamHost`)가 맡는다. 장판 관리자와 슬롯과 개수가 별개이고 `IBossAreaCapacity`를 구현하지 않아 보스 전체 장판 상한(3개)에 빔이 들어가지 않으며 기존 장판의 설치와 피해는 서로 영향이 없다. `BossEffectComposite`에 함께 넣어 쓴다. 활성 순간의 첫 타격은 패턴이 아니라 관리자 Tick이 한 번만 처리한다(같은 BossCombat 틱 안).
  - 피해와 큰 틱: 빔 한 줄이 회차마다 전장 안을 다시 조회해 폭 띠 안(중심점, 경계 포함)의 살아 있는 플레이어와 소환물에게 한 번씩 요청한다. 빔 한 줄의 회차마다 새 동작 번호를 쓰고 시전 번호는 함께 쓰며 피해는 `BossDamageGate`로만 낸다. 지연된 틱에서는 빔마다 한 틱에 가장 최근 도래한 회차만 판정하고 이전 회차는 건너뛴다(다음 회차는 원래 활성 시각 기준). 종료 시각을 지났으면 미처리 회차 없이 끝나 종료 뒤 밀린 피해가 없다. 사망, 페이즈 전환, 전투 종료는 예고와 활성 빔과 미처리 회차를 모두 정리하고 이미 적용한 피해는 되돌리지 않는다.
  - 데이터: 패턴 정의에 `BeamCount`(줄 수), `BeamLateralOffset`(전장 중심에서 중심선까지 거리), `BeamTickInterval`(피해 간격)을 더했다. 폭은 형태(`Line`)의 `Width`, 유지 시간은 `ActiveSeconds`다. 형태의 길이(36m)는 전장 안쪽으로 잘리므로 판정에 쓰지 않는 값이다. 검증이 선 형태, 폭과 줄 사이 거리(겹침), 간격이 유지 시간 이하인지를 확인한다. 기존 `Line` 형태는 전장에 고정되고 전장 원으로 잘리는 빔을 정확히 표현하지 못하는 자리값이며, 열거형(`EEnemyAttackShape`) 추가는 공용 타입 소유자와 협의가 필요해 바꾸지 않았다.
  - 표현: 빔 한 줄마다 예고 알림(`IBossBeamTelegraphListener`, 빔 번호가 키, 시전 번호가 묶음)과 활성, 타격, 소멸 알림(`IBossBeamListener`)을 보낸다. 클라이언트(`BossTelegraphPresenter`의 `BeginBeam`, `ActivateBeam`, `TickBeam`, `EndBeam`, `CancelBeam`, `RestoreBeam`)는 서버가 확정한 값만 그린다. 서버 활성 확정이 오기 전에는 예고만 보이고, 예고 시간이 끝나는 것만으로는 지속 빔이 되지 않는다. 폭발형 직사각형 예고를 지속 빔으로 쓰지 않고 별도 메시와 셰이더(`BeamGroundVFXController`, `BossBeam.shader`)로 폭 띠와 전장 원의 교집합을 그대로 그린다(두 끝이 전장 경계를 따른다, 임시 표현). 활성이 예고보다 먼저 오거나 예고가 대기 시간으로 지워진 뒤 늦게 와도 서버 활성 시각 기준으로 이어서 그리고, 취소가 시작보다 먼저 와도 늦은 시작이 되살아나지 않으며, 한 빔의 취소나 소멸은 같은 시전의 다른 빔을 지우지 않는다. 빔 표현 풀은 4개다.
  - 한계: 과거 위치를 기록하지 않아 건너뛴 회차는 복원하지 않는다. 장판과 빔이 겹친 상태에서 안전 구역으로 이동할 수 있는지는 실제 플레이로 확인하지 않았다.
- 2페이즈 P01 별형 회전 여우불 빔(`wolbaek_ph2_p01`, Large, 2026-10-03, EditMode와 PlayMode 검증, 실제 서버 실행 미검증): 전장 중심 반경 14m 정오각형에 여우불 5개를 놓고 두 칸 뒤 꼭짓점을 이어 오각별 빔 5개(0-2, 1-3, 2-4, 3-0, 4-1, 폭 1.5m)를 만든다. 예고 2초 동안 별을 고정하고, 서버 활성 확정 순간부터 서버가 고른 방향(좌우 각 50%)으로 6초 동안 초당 20도(총 120도) 회전한다. 판정은 활성 순간부터 0.25초 간격 24회, 회당 방어 전 0.03Href(0.5초 간격 0.06Href와 같은 총량), 방어 불가, 회복 1초, 직접 제어 없음이다. 체력 25% 이하에서도 수치는 같고 강화 기본값과 흐름 배율만 선택에 적용한다. 수치는 시작안이며 테스트로 조정한다.
  - 기하: 공용 순수 계산(`BossStarGeometry`, `BossStarShape`)이 배치, 회전각, 포함 판정을 맡고 서버 판정과 표현이 같은 함수를 쓴다. 별 전체가 전장 중심을 축으로 같이 돌아 모양이 유지되고, 회전각은 `방향 x 속도 x (서버 시각 - 활성 서버 시각)`을 유지 시간으로 자른 값이라 누적 오차가 없다. 빔 한 줄은 두 꼭짓점 사이 직사각형(길이와 폭 양 변과 끝 변 포함)이고 판정과 표현 메시가 같은 영역이다. 중앙 안전 공간은 반지름 약 3.6m 원(중심에서 빔 중심선까지 4.33m, 폭 절반 0.75m 제외)이고 별 끝 사이 골과 별 바깥이 그 밖의 안전 공간이다.
  - 시작: 전장 중심과 반지름은 전장 입력(`IBossArenaSource`)에서 받고 보스 위치를 중심으로 쓰지 않는다. 꼭짓점 0은 예고 시작 순간 보스에서 유효 타깃을 향한 수평 방향(같은 위치면 보스의 유효 정면)에 놓이고, 회전 방향은 두뇌와 같은 난수 의존성(`BossPatternContext.Random`)으로 추첨한다. 예고 시작에 고정하고 활성 직전에 다시 배치하지 않는다. 전장 정보, 유효 타깃, 난수가 없거나 배치 반경이 전장 반지름보다 크거나 슬롯이 없으면 시작을 거절하고 예고도 알리지 않는다(`IBossPatternStartResult`).
  - 구조: 패턴(`BossStarPattern`)은 배치 고정, 방향 추첨, 예약, 예고, 활성만 맡고 회전과 주기 피해는 별형 관리자(`BossStarManager`)가 맡는다. 고정 평행 빔(P02)과 판정 영역이 시간에 따라 도는 차이가 있어 관리자를 따로 두되 시간 규칙은 같다. 장판 상한에 들어가지 않으며 첫 타격은 관리자 Tick이 한 번만 처리한다.
  - 판정 방식: 회차(0.25초)마다 그 틱의 서버 시각 기준 회전각으로 별을 만들고 전장 안 현재 대상 위치를 다시 조회해, 빔 다섯 줄 중 하나라도 닿으면 대상당 한 번 요청한다(교차점에서도 한 번). 회차 사이에 빔이 지나갔다는 이유로 추가 피해를 만들지 않고(회전 경로 전체 판정이 아니다) 연속 접촉 피해도 아니다. 0.5초 간격일 때보다 회전 중 건너뛰는 폭이 작아지도록 0.25초 간격을 택했다(건너뜀이 생기는 위치는 중심에서 약 17.7m 밖으로 사실상 전장 가장자리뿐이다). 지연된 틱은 빔 구조당 한 틱에 가장 최근 회차만 현재 서버 시각의 회전 배치와 현재 위치로 판정하고 이전 회차는 건너뛰며 다음 회차는 원래 활성 시각 기준이다. 종료 시각을 지났으면 미처리 회차 없이 끝난다. 과거 위치와 배치를 복원했다고 주장하지 않는다.
  - 피해와 정리: 회차마다 새 동작 번호를 쓰고 시전 번호는 함께 쓰며 피해는 `BossDamageGate`로만 낸다. 사망, 페이즈 전환, 전투 종료는 예고와 회전과 미처리 회차를 모두 정리하고 이미 적용한 피해는 되돌리지 않는다. 다른 시전의 장판과 투사체, 고정 빔의 피해와 독립이다.
  - 데이터: 패턴 정의에 `StarVertexCount`, `StarRingRadius`, `StarConnectStep`, `StarRotationDegrees`를 더했다. 폭은 형태(`Line`)의 `Width`, 유지 시간은 `ActiveSeconds`, 피해 간격은 `BeamTickInterval`이다. 검증이 선 형태, 꼭짓점 수(5~8), 연결 칸 수(꼭짓점 수와 서로소, 2 이상 절반 이하), 배치 반경이 전장 반지름 이하인지, 회전 속도와 간격을 확인한다.
  - 표현: 별 구조 하나가 `BossStarView`의 한 건(구조 번호가 키, 시전 번호가 묶음, 안의 빔은 꼭짓점 번호 순서)이고 여우불 5개와 빔 5개를 한 물체로 같이 돌린다(임시 표현). 예고 동안은 정지한 별에서 여우불이 회전 방향으로 차례로 맥동해 방향을 알리고 위치는 바꾸지 않는다. 서버 활성 확정 전에는 예고 시간이 끝나도 회전하지 않고, 늦은 활성과 중간 복원은 서버 시각의 현재 각도로 이어서 그린다. 클라이언트는 좌우 추첨, 각도 계산, 판정을 하지 않는다.
  - 한계: 한 시전의 장판과 별이 겹쳐 안전 공간이 줄어드는 상황, 회전을 따라가며 피할 수 있는지는 실제 플레이로 확인하지 않았다.
- 1페이즈 P05 지연 규칙 수정(2026-10-03, P03과 별개): 확정 규칙에 맞춰 1페이즈 P05 정의의 `AreaSkipsMissedRounds`를 켰다(에셋과 기본값). 지연된 틱에서 장판당 한 틱에 가장 최근 도래한 회차만 판정하고 이전 회차는 건너뛰며 다음 회차는 원래 활성 시각 기준이다. 종료 시각을 지났으면 미처리 회차 없이 종료한다. 정상 진행의 6회는 그대로다. 장판 관리자의 기본(스펙에 설정을 주지 않은 직접 호출) 동작은 놓친 회차를 모두 처리한다.
- 한계 보충: (서버 몸체가 P01, P02를 연결하기 전 기준이다. 연결 뒤 P01~P06 전체의 선택 분포는 미검증이다) 후보가 P02~P06뿐인 지금은 원거리에서 P06 가중치가 0이고 직전 P05도 0이라 후보가 비면 오류 로그 없이 재조회만 반복한다. 나머지 패턴이 구현되면 이어진다.

연결 계약(모두 `Shinshi.AI`).

| 인터페이스 | 역할 | 구현 담당 |
|---|---|---|
| `IBossVitals` | 보스 체력 비율과 체력 0 여부 읽기. `ICombatTarget`에 체력 읽기가 없어 새로 둠 | 민성 (`BossServer`, 구현됨·실행 미검증) |
| `IBossControlSource` | 도착한 직접 제어를 `BossDirectControl`(출처 ID, 요청 시간) 한 건씩 넘김. 플레이어 기술의 속박(`ISkillControlTarget.ApplyRoot(sourceId, 시간, 시각)`)을 `BossServer`가 틱당 한 칸에 담아 넘기고 틱 끝에 비운다 | 민성 (`BossServer`, 구현됨·실행 미검증) |
| `IBossPatternFactory` | 패턴 정의로 시전 실행 객체 생성. 구현 `WolbaekPatternFactory`는 P01(`BossDashPattern`, `IBossMovement` 이동 창구가 있을 때만)과 P02와 P03과 P04(`BossProjectilePattern`, `IBossProjectileLauncher` 발사 창구가 있을 때만)와 P05(`BossAreaPattern`, `IBossAreaHost` 장판 관리자가 있을 때만)와 P06을 만든다. 2페이즈 P02(`BossBeamPattern`)는 `IBossBeamHost` 빔 관리자와 `IBossArenaSource` 전장 입력이 모두 있을 때만 만든다. 2페이즈 P01(`BossStarPattern`)은 `IBossStarHost` 별형 빔 관리자와 전장 입력이 모두 있을 때만 만든다. `BossServer`가 빔 관리자, 별형 빔 관리자, 전장 입력과 2페이즈 반월 연쇄·빔·별형 빔 리스너를 모두 넘긴다(2026-10-03, 구현됨·실제 서버 실행 미검증). `IBossMovement`(벽 검사와 위치 이동)와 1페이즈 여섯 패턴의 예고·지속물 리스너는 서버 몸체 `BossServer`가 구현해 넘긴다. 생성자에 Href를 받으며 기본값은 없다 | 기웅 (U10, 6패턴 구현됨), 서버 연결 민성 |
| `IBossEffectLifetime` | 지속 장판·투사체 수명, 이유별 취소, 동시 장판 수 조회. 투사체는 `BossProjectileManager`(`IBossTickableEffects`도 구현해 `BossCombat`이 공격 진행 뒤 매 틱 부른다)가 맡고, 사망·페이즈 전환·전투 종료로만 제거하며 직접 제어와 패턴 종료는 투사체를 지우지 않는다. 장판은 `BossAreaManager`(슬롯 예약·활성·주기 피해·정리)가 맡고, 투사체와 장판을 함께 넘길 때는 `BossEffectComposite`로 묶는다. `BossServer`가 투사체·장판·빔(`BossBeamManager`)·별형 빔(`BossStarManager`) 관리자 넷을 만들어 팩토리와 `Effects`에 같은 객체로 넘긴다 | 기웅 (U10, 구현됨), 서버 연결 민성 (구현됨·실행 미검증) |
| `IBossCastListener` | 시전의 발동(예고 끝 경계)과 회복까지의 정상 종료를 FSM 단계 경계 시각으로 알린다. 적중 수와 상관없이 오고 취소된 시전에는 종료가 오지 않는다. `BossBrainDependencies.Casts`(선택)로 넘기며 `BossAttackFsm`이 부른다 | 민성 (2026-10-03, 구현됨·EditMode 검증) |
| `IBossPatternCatalog` | 보스·패턴 정의 조회. 어댑터 `BossPatternCatalogSource`가 `BossPatternCatalogSO`를 감싼다 | 기웅 (구현됨) |
| `IBossRandom` | 타깃·패턴 선택 난수. 기본 `SeededBossRandom` | 기웅 (구현됨) |
| `BossTelegraphPresenter` (`03.AI/Boss/View/`, `Shinshi.Runtime`) | 서버가 확정한 P01·P02·P03·P06 예고·발동·취소를 시전 번호별로 그리는 클라이언트 표현 진입점. 서버 시각을 받아 늦게 받은 예고를 이어서 그리고 `Restore`로 복원한다. 임시 표현이며 샘플 입력(`BossTelegraphSampleInput`)과 시험 씬으로만 확인했다. 전달 창구는 `BossState`(아래 복제)이며 2026-10-12 `BossNetworkViewBinder`를 연결했다. 실제 서버 수신과 지연 중 표시 일치는 미검증 | 기웅 (표현), 민성 (네트워크 통합) |

기존 계약 `ICombatTargetQuery`(`CombatTargetRegistry`), `ICombatTarget`, `IDamageResolver`(`SkillDamageResolver`), `DamageRequest`를 그대로 쓴다.

서버 연결(민성, `98.Servers/02.Combat/Boss/`, 구현됨·EditMode 검증·실제 서버 실행 미검증).

- `BossServer`가 보스 몸체다. `IDamageable`·`ISkillControlTarget`·`IBossVitals`·`IBossControlSource`·`IBossMovement`, 1페이즈 여섯 패턴의 예고·지속물 리스너, 2페이즈 반월 연쇄·빔·별형 빔 리스너와 전장 입력(`IBossArenaSource`), `IBossCastListener`를 구현하고 `BossBrain`을 가진다. 최대 체력은 `HealthRef × Aref × 보스 체력 배율`, 방어는 보스 정의 값이다. 치명타는 `IDamageable` 계약대로 그 자리에서 `Dead`로 바꾸고, 등장 대기·시작 전·종료 뒤에는 무적이다. 예고 시작·조준 변경·방향 고정·돌진 시작 방향으로 정면을 돌린다.
- 이동: 돌진(P01)이 `Move`로 몸체를 옮긴다. 살아 있고 흐름이 `Combat`일 때만 움직이며 취소·사망·종료 뒤 늦게 온 호출은 현재 위치를 돌려준다. `GetClearDistance`는 원형 전장 경계(`BossArenaRules`, 반경은 정의의 `ArenaDiameter`/2에서 몸체 반경을 뺀 원, 경계 위·밖에서도 안쪽으로는 갈 수 있음)와 정적 지형(`BossTerrainProbe`, `Obstacle` 레이어 구 스윕) 중 가까운 쪽이다. 물리 스윕은 시작에서 겹친 콜라이더를 통째로 무시하므로, 지형 검사는 닿은 벽에서 법선 방향 0.05m 앞에 멈춰 다음 스윕이 겹친 채 시작하지 않게 한다. 구의 아래 끝은 발 위 0.25m라 평평한 바닥과 겹치지 않는다. 처음부터 벽 안에 놓인 몸체는 막지 못하므로 생성 지점은 벽에서 몸체 반경 이상 떨어뜨린다. 투사체 경로 길이도 같은 검사를 쓴다. 전장 지름이 몸체 반경 이하이면 `Begin`이 거절한다. 실제 지형에서는 미검증이다.
- 지속물: `BossServer`가 `BossProjectileManager`·`BossAreaManager`·`BossBeamManager`·`BossStarManager`를 만들어 팩토리와 `Effects`(`BossEffectComposite`)에 같은 객체로 넘긴다. 그래서 사망·페이즈 전환·전투 종료 정리와 매 틱 진행이 빔과 별형 빔에도 간다. 2페이즈 P01·P02의 전장 입력(`IBossArenaSource`)은 몸체의 전장 중심과 반경(몸체 반경을 빼지 않은 값)이다.
- 2페이즈 인계와 50% 결계(2026-10-03): 월백(`boss_wolbaek`)이면 `BossServer`가 `BossBarrierDirector`를 만든다(설정은 `BossBarrierSettings` 임시 기본값, 생성자 인자로 주입 가능, 카탈로그에는 아직 없음). 매 틱 두뇌 진행 뒤 흐름이 `Combat`이면 `Tick(serverTime, PhaseState == AwaitingHandoff)`를 부르고, 사망·전투 종료로 흐름이 바뀐 틱에는 `Cancel(Death 또는 CombatEnd)`로 위험 구역을 정리한다. 전환한 틱에 결계가 시작되고(보스가 전장 중심으로 이동 → 예고 → 활성 → 완료), 완료한 틱에 `BossBrain.CompleteHandoff()`를 불러 그 뒤로 2페이즈 선택이 열린다(실행 가능한 패턴이 없으면 `AwaitingReadiness`). 결계 위험 구역은 완료 뒤에도 사망·전투 종료까지 남아 바깥 원과 안쪽 안전 반경 사이에 지속 피해를 낸다. 피해는 패턴과 같은 `damage` 경로(`BossDamageScaler`)와 Href를 쓴다. 결계가 없는 보스(무철, 시험 정의)는 전처럼 전환 다음 틱 처음에 바로 인계한다. 개발 리셋 경로는 서버에 없어 `Reset()`은 부르지 않는다(EditMode 테스트 작성, 실행 미검증). 지속물 Tick은 `BossCombat`이 부르고 피해는 패턴의 게이트(`BossDamageScaler` 경로)로만 나간다.
- 패턴 팩토리: 보스 ID가 `MucheolPatternFactory.BOSS_ID`(`boss_mucheol`)이면 패턴 없는 `MucheolPatternFactory`, 그 밖은 `WolbaekPatternFactory`다. 무철은 카탈로그 수치가 비어(`HealthRef` 0, `IsTuned` 거짓) `Begin`이 "최대 체력이 0" 오류로 거절하고 `CombatServerRuntime`이 보스 없이 진행한다(코드 경로 확인과 EditMode 테스트 작성, 실행 미검증).
- `CombatServerRuntime`이 보스전 출정이 진행되는 첫 틱(배율 고정 직후)에 보스를 만들어 레지스트리에 적 진영으로 등록하고 `Begin`으로 전투를 연 뒤 네트워크 개체를 스폰한다. 틱 순서는 플레이어 기술·공격 → 보스 `Tick` → 생사 보고 → 세션 종료 판정 → (출정이 끝났으면) 같은 시각 `RequestCombatEnd`와 `Tick` → 보스 상태 기록이다.
- 보스 사망이 확정된 틱에 `DungeonSessionHost.ReportBossDefeated` → `DungeonSession.ReportBossDefeated`로 성공을 예약한다. 종료는 `CompleteTick`이 판정하므로 같은 틱 전원 사망이면 전멸이 우선한다. 승패의 기준은 세션 종료 사유다. 이 순서에서 보스는 이미 `Defeated`라 같은 틱 종료 요청을 받지 않아 `IsCombatEndRecorded`가 기록되지 않는다(기웅에게 계약 변경 요청).
- 월백 체력 기믹의 세션 처리(2026-10-14, 구현됨, 세션 EditMode 테스트 작성, 실행 미검증). `CombatServerRuntime.TickBoss`가 보스 `Tick` 뒤, 처치 보고 앞에 세션 틱에서만 본다.
  - 최종 성공(하드 최후의 영역): `BossServer.TryConsumeFinalSuccess` → `DungeonSessionHost.ReportBossFinalSuccess`. 같은 틱 전멸과 기믹 실패보다 앞서 `Success`로 끝나며 보상과 정산은 일반 성공과 같은 종료 경로 한 번이다. 세션이 받지 않은 틱이면 런타임이 들고 있다가 다음 세션 틱에 다시 알린다.
  - 정지(`Halted`, 전장·지점·바닥 창구 장애): `CancelSortieService` → `ServiceCancelled`.
  - 전멸 아닌 실패(`Failed`): `ReportBossFightFailed(사유)`. 봉인 60초 만료는 `Timeout`, 부적진의 진행 불가는 `AllDead`로 끝낸다(새 종료 사유를 만들지 않으려고 기존 값을 쓴 선택이다). 기믹의 전멸 실패는 따로 알리지 않고 세션의 전멸 판정이 닫는다.
  - 부활: 색 결계 타격 중에는 횟수를 쓰지 않고 미룬다. 최후의 영역이 지점을 맡으면 그 지점으로 되살리고, 줄 지점이 없으면 횟수를 쓰지 않고 미룬다(기존 지점 선택으로 되돌아가지 않는다).
  - 스위치(2026-10-18 변경): **기본은 켬**이다. 기믹 사건의 Net 복제(`BossState`)와 클라이언트 표시(`BossNetworkViewBinder`)가 연결돼 있다. 끄려면 던전 서버 환경 변수 `BOSS_GIMMICKS=0` 또는 실행 인자 `-no-boss-gimmicks`(값 없음)를 준다. 끄면 상한 없음, 즉시 사망, 결계 전개의 기존 동작 그대로다. 월백(`boss_wolbaek`)에만 붙고, 묶음을 만들지 못하면 오류 로그를 남기고 기믹 없이 연다.
  - 2페이즈 전환 컷신 구간(2026-10-18): 결계를 대신하는 기믹 묶음이 붙은 보스는 50% 전환 틱에 9.6초 구간(`BossServer.Phase2Transition*`, 클라이언트 Timeline `TL_WolbaekPhase2Transition_Combat` 길이와 같음)을 열고, 그동안 무적이며 인계를 미룬다. 구간은 `BossState` SyncVar(번호, 서버 시작·종료 시각)로 복제하고 `BossNetworkViewBinder`가 `PF_NetWolbaekBoss`의 `WolbaekPhase2TransitionPlayer`로 재생한다. 늦은 접속은 스폰 상태로 중간부터 재생한다. 프로토콜 14.
  - 현행 프로토콜(2026-10-19): 파티 요청·결과 메시지 끝에 요청 ID(`RequestId`)를 더해 wire가 바뀌었으므로 프로토콜 버전은 15. 서버와 클라이언트는 같은 빌드를 쓴다.
- 보스가 내는 피해는 `BossDamageScaler`가 시작 인원 피해 배율을 `RawDamage`에 곱해 `SkillDamageResolver`에 넘긴다. 번호·위치는 바꾸지 않아 반격 방어와 중복 적중 판정이 유지된다.
- 던전 맵 선택(2026-10-19): 실행 설정 `-boss-id`는 계약 없는 개발 출정과 부팅 전장의 기본값이다. 실제 첫 출정 계약이 일반전이면 `DungeonForest`, 무철이면 `DungeonMucheol`, 월백이면 `DungeonWolbaek`을 서버와 클라이언트가 함께 연다. `DungeonSessionHost.MapLoader`와 `ShinshiServerAuthenticator`는 맵 준비까지 인증 완료를 보류하며 로딩 상한은 15초다. `onlineScene`이 빈 단일 씬 개발 구성은 전환하지 않는다.

복제(민성, `03.AI/Boss/Net/`, 2026-10-03, 구현됨·EditMode 검증·전용 서버와 다인 접속 미검증).

- 원칙: 서버는 사건(무엇을, 언제, 어디서)과 원본 상태만 보내고 클라이언트는 그리기만 한다. 보스 상태를 바꾸는 `[Command]`는 없다. 표현 값(색, 진행도)은 보내지 않는다.
- 개체: `BossState`(`NetworkBehaviour`)를 붙인 `PF_NetWolbaekBoss`(`02.Prefab/Network/NetBoss/`, `PF_WolbaekBoss`의 프리팹 변형)를 `Bootstrap`·`Dev_PlayerMovement` 씬 매니저의 `spawnPrefabs`에 등록했다. `ServerBootstrap`이 `BossNetworkSpawner`를 `EnableBossObjects`로 연결하고, 스포너는 첫 상태를 실은 뒤 `NetworkServer.Spawn`하고 `BossServer.SetEventListener`로 사건을 잇는다. 서버 개체 생성은 동료(`CompanionNetworkSpawner`)와 같은 서버 주도 스폰이며, 연결이 만드는 플레이어처럼 `ServerLinks`를 거치지 않는다. 프리팹이 없으면 경고 뒤 보스 판정만 돈다.
- 동기화 값(`SyncVar`): 전투 번호(`CombatId`, 사건의 `BossId`와 같음)와 정의 ID(`BossId`), 위치, yaw(`FacingRules`), 체력·최대 체력, 생사, 흐름(`EBossFlowState`), 페이즈(`EBossPhaseState`). 두 열거형은 클라이언트가 읽을 수 있게 `03.AI/Boss/Rules/`로 옮겼다(네임스페이스 그대로). 주기는 `Constants.Network.BOSS_SYNC_INTERVAL_SECONDS`(10Hz, 적과 같음)이고 동기화 방식은 `Reliable`로 고정한다(Hybrid 기준선은 초기 상태 직렬화를 다시 불러 복원 기록을 주기마다 싣는다).
- 위치는 `NetworkTransform` 없이 `SyncVar` 하나로 보낸다. 돌진 중에는 표현이 돌진 사건(시작 위치·방향·거리·속도·시작 시각)으로 몸체를 그리고 그 밖에는 복제 위치를 따른다. 상태는 루트 Transform을 움직이지 않으므로 몸체를 옮기는 계층은 표현 하나다.
- 사건(`IBossEventListener`, `[ClientRpc]`): 예고 시작(`BossTelegraphStart`), 조준 갱신, 방향 고정, 발동(부채꼴·원형·직사각형), 돌진 시작·정지, 정상 종료, 취소, 투사체 발사(`BossProjectileStart`)·소멸, 장판 활성(`BossAreaStart`)·타격 회차·소멸. 시각은 모두 서버 시각이며 받은 순간의 시각으로 바꾸지 않는다. 예고 시작·조준 갱신은 틱 시각, 발동·종료·돌진 시작·발사·활성·장판 회차는 FSM 단계 경계 시각이다. 투사체·장판 번호는 시전 번호다. 장판 예고 뒤 활성이 실패하면(슬롯 경합) 발동 대신 취소를 보낸다. 2페이즈 사건(2026-10-03 추가): 반월 연쇄 시작(`BossSweepStart`)·구역 예고(`BossTelegraphStart`, 구역 번호가 키·시전 번호가 묶음)·구역 폭발·묶음 취소, 빔 예고(`BossBeamStart`)·활성 전 취소·활성·타격 회차·소멸, 별형 빔 예고(`BossStarStart`)·활성 전 취소·활성·타격 회차·소멸. 빔·별형 빔·구역은 서버가 발급한 번호가 키다. 시작 값 구조체는 `BossNetSerializer`가 직렬화하며 예고 시작에 묶음 번호(`GroupId`)도 싣는다. 50% 결계 사건(2026-10-03 추가): 예고 시작(`BossBarrierStart`, 결계 번호·중심·바깥/안쪽 반경·피해 간격·예고/활성/완료 서버 시각)·활성·완료·정리(결계 번호). 예고 전에 끊긴 결계(번호 0)는 정리를 보내지 않는다.
- 늦은 접속·재접속 복원: 서버는 진행 중인 예고·투사체·장판과 예고 중이거나 활성된 빔·별형 빔, 예고부터 정리 전까지의 결계(`BossBarrierStart` 하나, 단계는 시각으로 재현하므로 시작 값만, `BossBarrierDirector.TryGetSnapshot`과 같은 구간)만 `BossEffectLedger`에 남기고(반월 연쇄는 짧은 시전이라 남기지 않는다), 새 관찰자에게 가는 스폰 초기 상태(`OnSerialize`의 `initialState`)에 싣는다. 재접속도 새 연결의 스폰이라 같은 경로다. 클라이언트도 같은 사건으로 기록을 이어 가고, 표현 연결부가 `BossState.SetListener`로 붙는 순간과 스폰 직후에 `IBossStateListener`의 복원 메서드로 넘긴다. 끝난 공격과 반환된 예약은 넣지 않는다.
- 시각: 서버 틱 시각은 서버 `NetworkTime.time`을 틱 간격으로 자른 값이고 클라이언트 `NetworkTime.time`은 서버 시각의 추정이다. 표현 연결부는 `BossTelegraphPresenter.SetServerClock`에 `NetworkTime.time`을 넘긴다.

연결되지 않은 것.

- 2026-10-12 `BossNetworkViewBinder`(`IBossStateListener`)를 `PF_NetWolbaekBoss`에 연결했다. `BossState` 사건을 `BossTelegraphPresenter`·`BossView`·`BossBarrierView`로 전달하며 효과 루트를 몸체에서 분리한다. `ResetNetworkSession`으로 재접속 때 이전 시전 기록을 지운다. 관련 EditMode 통과는 실제 M-Boss 수용을 대신하지 않는다.
- 출정 계약에서 보스·지역을 받는 것, 던전 씬의 보스 생성 지점과 전장 중심(지금은 원점), 보스를 만들지 못한 보스전의 출시 처리(서비스 취소 여부)는 미정이다(민성, 전장 맵은 진욱).
- 월백 1페이즈 여섯 패턴이 서버 몸체에서 모두 만들어지고 사건이 나가는 것은 실제 카탈로그로 EditMode에서 확인했다(`BossServerWolbaekTests`). 회복 시간 등 수치는 `BossPatternCatalog.asset`에서 조정한다.
- 50% 인계 대기(`AwaitingHandoff`)는 흐름이 `Combat`인 채라 세션은 전투를 계속하고 보스 종료나 전멸로 처리하지 않는다. 월백은 결계 전개가 끝날 때까지(임시값 기준 이동 최대 3초 + 예고 2.5초 + 전개 1.5초) 대기하고, 그동안 보스는 피해를 받는다(무적 여부는 기획 미정, 지금은 무적 아님). 결계가 없는 보스는 대기가 한 틱이다.
- 2페이즈 사건과 50% 결계 사건은 `BossNetworkViewBinder`를 거쳐 빔·별형 빔·반월 연쇄·결계 표현으로 전달한다. 실제 네트워크 수신·지연·재접속 화면은 미검증이다. 결계는 연결부가 `OnBarrierStarted`·`OnBarrierRestored`를 `BossBarrierView.Begin`, `OnBarrierCleared`를 `BossBarrierView.Clear`로 넘기고 `SetServerClock`에 `NetworkTime.time`을 준다(`BossBarrierLocalBridge`와 같은 대응, 기웅). 연결부는 `BossTelegraphLocalBridge`와 같은 대응(`BeginSweepCue`·구역 `BeginTelegraph`·`Strike`·`CancelGroup`, `BeginBeam`·`ActivateBeam`·`TickBeam`·`EndBeam`·`CancelBeam`·`RestoreBeam`, `BeginStar`·`ActivateStar`·`TickStar`·`EndStar`·`CancelStar`·`RestoreStar`)으로 넘긴다.
- 장판 예고 사건(1페이즈 P05, 2페이즈 P04)은 장판 번호를 예고 번호로, 시전 번호를 묶음 번호로 보낸다(2026-10-03, 로컬 시험 연결부와 같은 형식). 장판 예고 취소도 장판 번호로 보낸다. 복원 기록은 예고 한 칸만 남기므로 늦은 접속은 같은 시전의 장판 예고 중 마지막 하나만 받는다(예고 1초라 남겨 둠, 실행 미검증).
- P06(`BossLineBlastPattern`): 1.6초 예고, 처음 0.6초 동안 90도/초로 타깃을 추적한 뒤 방향을 고정하고, 예고가 끝나면 한 번 타격한다(`ActiveSeconds` 0). 판정은 타격 시점의 보스 서버 위치를 시작점으로 고정된 조준 방향의 수평 직사각형(전방 0~14m, 좌우 각 2m, 네 경계 포함, 뒤쪽 제외)이며, 데이터의 `Shape.Range`가 길이, `Shape.Width`가 전체 폭이다. 조회는 원형 반경 `sqrt(길이² + (폭/2)²)`에 1mm 여유를 더해 먼 모서리까지 후보에 넣고, 안팎은 직사각형으로 다시 가른다. 방어 불가(`Unblockable`)이며 RawDamage는 `DamageRef(0.45) × Href`다. 예고의 시작점·방향·길이·폭·시간은 `IBossLineTelegraphListener`(예고 시작, 추적 방향 변경, 방향 고정, 타격, 취소)로 내보낸다. 예고 시작 알림의 시작점은 그때의 보스 위치이고 타격은 그 시점의 보스 위치에서 판정하므로, 보스가 예고 중 움직이면 표현 쪽이 보스를 따라가야 한다(보스 이동은 미정).
- P06 직접 제어: `wolbaek_p06` 데이터에 예고 단계, 마지막 0.4초, 취소형으로 입력했고 월백 보스 정의에 최대 1초와 면역 2초를 입력했다. 취소는 새 경로 없이 기존 `BossCombat`→`BossAttackFsm` 흐름을 쓴다. 구간 시작 직전은 취소되지 않고(그 제어는 버려지며 같은 시전의 구간 안 제어는 받는다), 시작 경계와 구간 안은 취소되며, 취소 뒤에는 타격이 나가지 않는다. 구간 계산은 `(double)ControlWindowSeconds`와 `(double)TelegraphSeconds`를 쓰므로 0.4, 1.6이 아니라 float 값 기준이다.
- 직접 제어 구간의 끝 경계(예고 종료 시각) 현재 동작: `BossAttackFsm.IsControlWindowOpen`이 끝 경계를 포함(`<=`)하지만 한 틱의 순서가 직접 제어 → 공격 진행이라, 예고 종료 시각과 정확히 같은 틱에 제어가 먼저 도착해 아직 예고 단계일 때만 취소된다. 틱이 그 시각을 넘어서 도착하면 단계가 아직 예고여도 `serverTime > 종료`라 버려진다. 포함 여부는 기획 미정이며 이번 작업에서 바꾸지 않았고 이 경계에 기대는 테스트도 두지 않았다.
- P02, P03, P06에 넘기는 Href는 전장 지역의 기준값이고 시작 인원 피해 배율은 `BossDamageScaler`가 곱한다(위 서버 연결). 예고 사건은 위 복제와 `BossNetworkViewBinder`를 통해 클라이언트 표현에 연결된다(2026-10-12). 원형·직사각형 VFX 모듈과 P03·P06 예고 시각 일치, P06 예고 방향 고정과 몸체 연출은 미검증이다. 회복 중 취소가 와도 패턴은 취소 알림을 낸다(P02, P03, P06 공통, 타격 이후라 피해에는 영향 없다). 표현 쪽은 타격 알림을 받은 시전의 취소 알림을 무시해야 한다.
- 컷씬 길이를 담는 데이터 필드가 없어 `BeginAppearance`의 길이는 호출자가 넘긴다.
- 속박 등 취소형이 아닌 제어가 보스를 움직이는 동작은 구현하지 않았다. 알림만 나간다.

미정·보류인 것.

- 월백 2페이즈 개별 공격 여섯 개(P01 별형 회전 빔, P02 절단 빔, P03 반월 연쇄, P04 지정 장판, P05 투사체, P06 원형 폭발)는 모두 구현됐다. P01의 수치는 시작안이라 테스트 후 조정한다. 전환, 인계, 선택 기반은 구현됨(EditMode 검증)이며 아래 "월백 2페이즈 선택 기반"에 적었다
- 결계 전개의 서버 연결과 완료 뒤 인계, 복제·늦은 접속 복원은 구현했다(민성, 2026-10-03, EditMode 테스트 작성·실행 미검증). 남은 것: 결계 설정값을 `BossPatternCatalogSO`로 옮기는 일과 확정 수치(기획), 인계 대기 중 보스 무적 여부(기획), 연결된 클라이언트 결계 표현의 실접속 검증, 개발 리셋 경로가 생기면 `BossBarrierDirector.Reset()` 연결
- 출정 계약의 보스 ID: 지금은 던전 서버 실행 설정 `-boss-id` 하나로 서버당 한 보스다. 계약에 보스 ID가 생기면 출정마다 그 값을 쓴다(민성, 코드에 TODO). 무철은 수치와 패턴이 비어 전투를 열지 않는다(기웅)
- 그로기 도입과 세부 규칙
- 직접 제어 구간의 끝 경계 포함 여부. 지금 코드는 포함하지만 확정이 아니며 이 값에 기대는 테스트는 없다
- 속박 등 직접 제어가 보스를 어떻게 움직이는지(기획 문서는 허용 구간에서만, 끌어오지 않고 이동 없이 최대 1초까지만 정했다. 속박 중 공격과 회전을 멈추는지는 없다)
- 같은 시각 보스 사망과 전원 사망의 결과(기획의 초기 기준은 전멸 우선이며 실제 전투 피드백 뒤 조정). 하드 월백 최종 성공만 전멸보다 앞서도록 코드를 바꿨고(2026-10-14) 기획 문서의 같은 규칙 서술은 아직 고치지 않았다
- 월백 체력 기믹의 Net 복제·늦은 접속 복원과 클라이언트 표시, 맵 좌표(지점 후보, 중앙 바닥 반경). 이것이 연결될 때까지 기믹은 기본 꺼짐이다
- 등장 컷씬의 시작 조건과 길이, 길이 데이터의 위치

## 9. 인증·재접속·서버 수명 연결

1. `ServerBootstrap`이 역할·포트·콘텐츠 버전을 읽고 GameDB를 확인한 뒤 콘텐츠 해시를 계산한다. 전투 설치, 던전 역할의 `DungeonSessionHost`·플레이어 유지 창구 결합, 서버 인증기 설치를 끝낸 뒤 `StartServer`를 부른다.
2. 클라이언트는 `ShinshiClientAuthenticator.SetJoinToken`을 `StartClient` 전에 부른다. 연결 직후 `JoinRequestMessage(protocolVersion, contentHash, joinToken)`을 보낸다. 접속 프로토콜은 `Constants.Network.PROTOCOL_VERSION`(현재 6)을 따른다. 닉네임·외형 복제 필드는 해당 프로토콜 계약의 일부다. 서버는 버전·해시·토큰 길이와 인증 대기 상한·마감을 먼저 확인한다.
3. 출시 서버는 `ApiJoinTokenVerifier`가 [일회용 토큰 교환](../../Assets/01.Scripts/00.Common/Contracts/Join/JoinApiRoutes.cs)을 한 번 호출하고 인스턴스·역할·캐릭터·직업(`classId`)·출정·참가자 정보를 확인한다. 직업은 마을과 던전 모두 이 응답에서만 오며, 모르는 값이면 입장을 거절하고 `SessionBinding.PlayerClass`로 `CombatServerRuntime`이 개체를 만든다. 게임 서버의 HTTP 호출은 `Authorization: Bearer {token}`을 쓴다. 서비스 자격은 서버 임대 발급에, 이후 게임 서버 경로는 `ServerPresence`가 받은 서버 임대 토큰에 묶인다. 일반 플레이어 접근 토큰으로 서버 전용 경로를 호출하지 않는다.
4. `ServerSessionRegistry`가 같은 참가자의 동시 연결을 거절하고 `SessionBinding`을 만든다. 토큰 교환 응답의 DB 닉네임·외형을 검증해 `SessionBinding`에서 `PlayerCombatState`로 전달한 뒤 최초 생성·재결합한다. 외형 payload 버전은 [캐릭터 커스터마이즈](CHARACTER_CUSTOMIZATION.md)에서 구분한다. 마을 참가자 키는 캐릭터 ID, 던전 키는 출정 참가자 ID다. 계정 전체의 단일 접속 임대는 백엔드가 담당한다. 던전은 `DungeonSessionHost.Admit`까지 통과해야 연결의 `authenticationData`를 설정하고 `ServerAccept`한다. 인증 이전에는 플레이어 생성·Command를 허용하지 않는다.
5. 동시 접속 거절은 `AlreadyConnected(809)`이며 사용자 안내는 **“이미 접속 중인 계정”**이다. 이전 연결의 끊김 감지가 늦을 때를 위해 클라이언트는 2초 간격으로 최대 6회 같은 토큰을 다시 보낸다. 서버 검증기는 교환 성공 정보를 이 서버에서만 최대 20초 기억하고 입장 성공 시 지운다. 재시도 중에는 `JoinRetryScheduled`, 최종 거절 때는 `Rejected`를 UI가 받는다. 백엔드 일시 불가는 `BackendUnavailable(810)`이고, `RateLimited(811)`과 정산 상한 `SettlementLimitExceeded(904)`는 별도 코드다.
6. 진행 중 던전에서 끊긴 원래 참가자는 `ShinshiNetworkManager.OnServerDisconnect`가 소유권만 떼어 서버에 개체를 남긴다. 유예 중에도 피격·사망·기술 타이머가 진행된다. 새 일회 재입장 토큰을 [rejoin-ticket 경로](../../Assets/01.Scripts/00.Common/Contracts/ApiRoutes.cs)로 받고 인증되면 같은 참가자·같은 개체를 새 연결에 넘긴다. 개체가 실제로 삭제된 경로는 `ReconnectVault`가 전투 스냅샷을 보존한다. 유예 만료 후 진행 중 출정은 참가자를 닫고 개인 실패 정산으로 이어지며 재합류를 막는다.
7. 마을 연결 종료는 참가자 키를 지우고 `AccountLeaseReleaser`로 계정 접속 임대 해제를 보낸다. 시작 전 던전 끊김은 start를 막고 예약 만료를 기다린다. 던전 입장자의 계정 임대 이관이 모두 확정된 뒤 같은 참가자 집합·operationId로 start를 요청하고 영수증을 받아 Running에 들어간다.
8. 서비스 자격이 있으면 `ServerPresence`가 임대 발급 → 새 bootId로 풀 등록 → heartbeat를 수행한다. 던전은 출정 하나가 끝나면 `ServerShutdownController`가 드레인을 요청하고 결과 확인 유예·연결 종료·전송 flush 뒤 프로세스를 종료한다. systemd 던전 유닛은 `Restart=always`로 새 프로세스·새 bootId를 띄운다. 이 애플리케이션 종료 경로와 OS `SIGTERM`의 자동 드레인 연동은 구분하며, 후자의 실행 검증 근거는 없다.

에디터·개발 빌드의 기본 검증기는 `dev`이고 형식만 확인한다. `JOIN_TOKEN_VERIFIER=api` 또는 `-join-verifier api`와 서비스 자격을 넣어 API 경로를 시험한다. 개발 저장 경로는 경제 결과를 남기지 않는다. 출시 빌드는 개발 검증기를 포함하지 않는다. 실제 실행 설정·자격 공급은 [BackendLaunchSettings](../../Assets/01.Scripts/98.Servers/01.Network/Backend/BackendLaunchSettings.cs)와 [서버 운영 원칙](NETWORK_AUTHORITY.md) 7장을 따른다.


### 릴리즈 전장과 몬스터 연결 (2026-10-19)

- Assets/00.Scenes/Release/의 숲·무철·월백은 원본 Dev_HUD 전장을 복제한 릴리즈 씬이며 원본 Dev 씬은 보존한다. 재생성 도구는 ReleaseDungeonMapBuilder다.
- 일반전은 WaveSessionFactory와 DungeonSpawnLayout을 연결한다. 현재 구역 전멸 후 다음 도착 지점으로 이동하면 다음 웨이브를 시작한다. 적 상태·공격 예고·투사체는 PF_NetEnemy와 PF_NetEnemyEffect로 복제한다.
- 몬스터 프리팹 생성에는 공유 외부 에셋 Horror Creatures의 Beast·Vendigo가 필요하다. 월백 원본 벽·재질은 KT_SplineBuilder와 Real Materials에 의존한다. Tools/Resources/WaterReflection.cs 중복 사본은 SHINSHI_ENABLE_LEGACY_WATER_REFLECTION 조건부로 제외하고 VFX/Simple Water Shader 사본만 컴파일한다. 이 외부 파일은 Git 제외이므로 공유 에셋에도 동일한 중복 제외가 필요하다.
- 검증: Windows 릴리즈 클라이언트 IL2CPP·전용 서버 빌드 성공, 통합 표적 EditMode 138/138·씬 25/25 통과. 서버 빌드 오류 0·경고 90(주로 기존 외부 API와 UI 경고, 동적 글꼴 임포트 포함). 보스 파괴 표현은 PlayMode에서 확인했으나 실제 거래·입력·웨이브·보스 판정의 M2는 검증 대기다. 돌진 중 처음 관찰한 Vendigo의 현재 돌진 모션 복원은 제한이 남는다.
