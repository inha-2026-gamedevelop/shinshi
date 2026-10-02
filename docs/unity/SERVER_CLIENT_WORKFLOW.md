# 서버·클라이언트 협업

> 2026-10-02 M-A 코드와 동기화. 네트워크 라이브러리는 Mirror 96.11.3(`Assets/Mirror/`), 호스트 방식은 데디케이티드 서버(확정)다. **역할별 클래스 분리와 서버 전용 어셈블리**가 적용돼 있고, 연결·인증·전투·출정 저장·정산 코드가 있다. 서버 빌드·M2/M3·IL2CPP·배포는 `구현됨·실행 미검증`이며, 보스 AI 서버 뼈대는 8장에 있다. 일반전 웨이브·스폰 실행 구현과 보스 마지막 단계 연결은 남아 있다.

같은 Unity 프로젝트와 저장소, Mirror 서버 권위 구조를 쓰며 **실행 위치와 작성 담당을 분리한다.** 판정·AI는 서버에서만 실행되지만(권위 원칙 그대로, [네트워크 권위](NETWORK_AUTHORITY.md)) 작성은 원래 파트 담당이 하고, {팀장}은 네트워크 연결 코드를 맡는다.
서버 구현 전체를 팀장 한 명이 맡으면 판정 규칙을 모르는 사람이 규칙을 쓰게 되고 병목이 생긴다. 그래서 판정·AI 작성은 파트 담당에게 둔다.

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
  98.Servers/                           Shinshi.Server (UNITY_SERVER || UNITY_EDITOR)
    Shinshi.Server.asmdef               {팀장}
    01.Network/Bootstrap/              {팀장}: 부트스트랩·실행 설정·프로세스 제어·종료
    01.Network/Presence/               {팀장}: 서버 풀 등록·존재 신호
    01.Network/Backend/                {팀장}: 백엔드 게이트웨이·HTTP 전송·서버 자격
    01.Network/Auth/                   {팀장}: 입장 토큰 검증·인증 대기·참가 명단
    01.Network/Session/                {팀장}: 출정 진행·명단 (Events/ 저장, Settlement/ 정산, Reward/ 드롭)
    02.Combat/                         전투 담당: 서버 틱·Player/·Damage/·Skill/·Boss/(보스 몸체)
    03.AI/Boss/                        AI 담당·{팀장}: 보스 두뇌·패턴
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

2026-10-02 작업 트리 기준 다음 타입이 존재한다. 표는 코드의 배치·호출 경계를 설명하며 다인 접속 성공이나 출시 빌드 성공을 뜻하지 않는다.

| 기능 | Runtime·Contracts 원본 | Server 구현 |
|---|---|---|
| 연결·입장 | [ShinshiNetworkManager](../../Assets/01.Scripts/01.Network/ShinshiNetworkManager.cs), [ShinshiClientAuthenticator](../../Assets/01.Scripts/01.Network/ShinshiClientAuthenticator.cs), `JoinRequestMessage`·`JoinResponseMessage`·`ServerLinks` | [ServerBootstrap](../../Assets/01.Scripts/98.Servers/01.Network/Bootstrap/ServerBootstrap.cs), `ShinshiServerAuthenticator`·`ApiJoinTokenVerifier`·`ServerSessionRegistry`·`SessionBinding` |
| 입력·이동·생사 | [PlayerInputState](../../Assets/01.Scripts/02.Combat/Player/Net/PlayerInputState.cs), `PlayerCombatState`, `PlayerInputHeader`, `PlayerMoveAuthority`, `Player/Rules/`, `PlayerInputReader`·`PlayerPrediction` | [CombatServerRuntime](../../Assets/01.Scripts/98.Servers/02.Combat/CombatServerRuntime.cs), `PlayerServer`·`PlayerInputGate`·`ReconnectVault`·`ReviveBudget` |
| 피해·기술 | [피해 계약](../../Assets/01.Scripts/02.Combat/Damage/IDamageResolver.cs), `DamageRules`, `PlayerSkillInput`·`CompanionCombatState` | `DamageResolver`·`HitLedger`·`CombatTargetRegistry`, `SkillServer`·`CompanionServer`·`ResonanceLedger` |
| 출정·개인 보상 | [Sessions DTO](../../Assets/01.Scripts/00.Common/Contracts/Sessions/GameServerRoutes.cs), [Reservations DTO](../../Assets/01.Scripts/00.Common/Contracts/Reservations/ReservationRequest.cs) | [DungeonSessionHost](../../Assets/01.Scripts/98.Servers/01.Network/Session/DungeonSessionHost.cs), `DungeonSession`·`ParticipantRoster`·`SessionEventQueue`·`LootRoller`·`SessionSettlement`·`SettlementRules` |
| API·프로세스 수명 | [Join DTO](../../Assets/01.Scripts/00.Common/Contracts/Join/JoinTokenExchangeRequest.cs), [Auth DTO](../../Assets/01.Scripts/00.Common/Contracts/Auth/AuthExchangeRequest.cs) | `BackendGateway`·`HttpClientBackendTransport`·`ServerCredentials`, `ServerPresence`·`ServerProcessDriver`·`ServerShutdownController` |

- `Shinshi.Runtime`·`Shinshi.Contracts`·`Shinshi.Server`·`Shinshi.Dev`·EditMode/PlayMode 테스트 asmdef가 있다. `Contracts`는 엔진 참조가 없고, [백엔드 csproj](../../Backend/src/Shinshi.Contracts/Shinshi.Contracts.csproj)가 DTO 원본을 링크한다. 참조와 빌드 조건은 [코딩 컨벤션](../coding-convention.md) 0장과 실제 asmdef를 따른다.
- [ClientView](../../Assets/01.Scripts/00.Common/Utility/ClientView.cs)가 `UNITY_SERVER`에서 표현을 끈다. 서버 판정은 `Shinshi.Server`에 있고 Runtime은 이 어셈블리를 참조하지 않는다.
- `DungeonSessionHost.BeginSession`은 현재 웨이브 없는 `DungeonSession`을 생성한다. 일반전 `CreateNormal`과 source ID·보상 등록을 실제 스폰·웨이브 구현에 연결하는 작업, 보스 마지막 단계의 생산 종료 연결(M-Boss)이 남았다. 개발 전용 보스 종료 창구를 완성된 보스전으로 보지 않는다.
- `PF_NetPlayer` 프리팹에는 `PlayerInputReader`와 입력·전투 상태·몸체 Collider 참조가 연결되어 있다. 클릭 이동과 회피는 아래 개발 빌드 실행에서 자동 조작으로 확인했고, 사람이 직접 하는 클릭·공격 입력과 dungeon 역할 다인 접속 수용은 실행 미검증이다. API checkpoint 경로와 DTO는 있지만 `DungeonSessionHost.CompleteTick`의 30초 주기 송신은 TODO다. 직렬화된 참조와 실제 실행 검증을 구분한다.
- 서비스 장애 취소 정책은 2026-10-01에 완료 웨이브 기본 정기 전액과 적립 추가 정기 50%(내림)로 확정됐고 `SettlementRules`가 이 계산을 제공한다. [SessionConfigSO](../../Assets/01.Scripts/00.Common/Data/SessionConfigSO.cs)의 정책 기본값은 아직 `Undecided`이며 [배포 빌드 검증기](../../Assets/Editor/Build/GameDBBuildValidator.cs)는 그 값의 비개발 빌드를 막는다. 정책 확정과 SO 설정 반영은 별개로 확인한다.
- 코드 확인과 문서 링크·diff 점검만 이번 동기화에서 수행했다. 기존 실행 근거로 제공된 결과는 **사용자 제공 Unity Test Runner EditMode 780개 통과, PostgreSQL 18 Docker를 사용한 Backend 396개 통과**다. 이번 작업 트리의 새 실행 검증으로 재표시하지 않는다.
- 2026-10-02 `Dev_PlayerMovement` 씬의 Windows 개발 서버 빌드 1개와 클라이언트 빌드 2개를 같은 PC에서 town 역할로 실행해 접속, 소유 이동 예측, 회피, 원격 표시를 지연 시뮬레이션 RTT 0/50/150ms에서 확인했다(자동 조작, 스냅 0회, 정지 시 서버 위치 차이 0). Linux 서버·M3·IL2CPP·배포·dungeon 역할 다인 접속은 실행하지 않았다. [Start-M2.ps1](../../tools/Start-M2.ps1)과 [배포 템플릿](../../deploy/gameserver/shinshi-dungeon@.service)은 존재하지만 실행 결과는 미검증이다. GitHub 브랜치 보호 설정 적용 여부도 별도 확인이 필요하다.
- 보스 AI 서버 뼈대(`98.Servers/03.AI/Boss/`, `Shinshi.AI`)와 서버 연결(`98.Servers/02.Combat/Boss/`)은 8장에 있다. 서버 틱 구동과 세션 결과 연결은 있고(실제 서버 실행 미검증), 복제는 아직 없다.
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
- 마지막 처치와 420초 도달이 같은 틱이면 **성공 우선**이다(2026-10-01 결정). `DungeonSession.CompleteTick`이 피해·처치 처리 뒤 종료 후보를 평가하고 성공을 시간 초과보다 먼저 적용한다. 보스전의 같은 틱 전원 사망은 전멸이 우선이며, 부활 승인은 종료 판정 뒤다.
- 웨이브 실행 객체의 생성·호스트 주입, 소멸 적 집계와 시간 초과 뒤 남은 적 정리는 기웅 쪽 실행 구현·연결 작업이 남아 있다. `IWaveSession.Tick`의 과거 미정 주석을 새 기획 판단의 근거로 쓰지 않는다.

## 8. 보스 서버 경계

보스 AI의 서버 뼈대를 `98.Servers/03.AI/Boss/`(`Shinshi.AI`, 기웅 작성, 민성 공동 리뷰)에 구현했다. 서버 시각을 바깥에서 받아 진행하는 일반 C# 클래스이며 Mirror와 `MonoBehaviour`에 의존하지 않는다. 가짜 의존성으로 EditMode 테스트(`98.Servers/03.AI/Boss/Tests/`의 별도 asmdef `Shinshi.Tests.Server.AI`)를 돌려 검증했다. 전용 서버와 클라이언트를 띄운 실행 검증은 하지 않았다. `Assets/Tests/`가 민성 소유라 테스트를 이 폴더에 두었다.

구조는 `상위 FSM(BossBrain) → Combat(BossCombat) → PhaseState / AttackState FSM → Combat BT → 상황별 가중치 선택 → 패턴 실행`이다.

| 클래스 | 책임 |
|---|---|
| `BossBrain` | 상위 FSM(`Inactive → Appearing → Combat → Defeated / Ended`). 등장 대기와 사망, 전투 종료를 판단해 취소를 지시한다. 진입점 `Begin`·`BeginAppearance`·`Tick(serverTime)`·`RequestCombatEnd` |
| `BossCombat` | 전투 중 한 틱의 순서를 지키고 두 FSM과 BT를 묶는다 |
| `BossPhaseFsm` | `Active → TransitionPending → AwaitingHandoff`. 새 선택은 `Active`일 때만 허용한다 |
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
- 최종 발악은 이번 구현에서 제외한다. 체력 0이면 사망을 확정하며(`ImmediateBossDeathPolicy`), 발악을 추가할 때 `IBossDeathPolicy`를 교체할 수 있게 자리를 유지한다.
- 보스 사망과 전원 사망이 같은 시각이면 기획 문서의 초기 기준대로 전멸 실패를 우선한다(실제 전투 피드백 뒤 조정될 수 있다). 뼈대는 두 사실을 모두 기록(`IsBossDeathRecorded`, `IsCombatEndRecorded`)하고 `FlowState`는 `Ended`가 된다. 플레이어 사망 상태는 바꾸지 않으며 승패 판정은 세션이 한다.
- 보스가 죽으면 남은 장판과 투사체를 즉시 제거하고 예약 타격을 포함한 추가 피해를 막는다. 이미 적용된 피해는 되돌리지 않는다. 막는 방법은 `IBossEffectLifetime`의 즉시 제거와 `BossDamageGate`의 폐쇄 두 겹이다. 전투 종료(전멸 등)에서도 같은 정리를 한다.
- 등장 컷씬: 보스는 컷씬 전에 미리 스폰한다. `BeginAppearance(serverTime, 길이)`로 `Appearing` 상태에 들어가며, 이 동안은 타깃 선택, 공격, 피해 처리를 하지 않고 등장 모션만 허용한다. 전투 시작 시점은 서버 시각으로만 정하며 클라이언트 컷씬 종료 신호는 받는 API가 없다. 컷씬을 언제 시작하고 길이를 얼마로 할지(전원 준비 시 진행, 이미 본 참가자의 별도 대기 등)는 기획 문서와 세션이 정하며, 뼈대는 시작 시각과 길이를 받기만 한다.
- 등장 대기와 `OpeningDelaySeconds`는 이어서 적용한다. 전투 시작은 등장 대기가 끝나는 시각이고, 첫 선택은 거기서 `OpeningDelaySeconds` 뒤다. 틱이 늦어도 시작 시각은 등장 대기가 끝나는 경계로 잡는다.
- 고정 오프닝과 강제 후속 공격은 이번 구현에서 제외한다. 등장 대기가 끝나면 가중치로 첫 패턴을 고르고, 각 패턴의 회복이 끝난 뒤 그 시점의 상황(타깃, 거리, 방향, 주변 인원, 최근 기록)을 다시 읽어 다음 패턴을 가중 선택한다. 일반 가중 선택 앞에 평가되는 `IBossSelectionRule` 자리는 유지하며 등록된 규칙은 없다.
- 직접 제어는 기획에 적힌 규칙대로 한다. 패턴별 허용 구간에서만 받고, 한 패턴에 최초 1회이며 적용 뒤 면역 시간 동안은 받지 않는다. 취소형 패턴(P06)은 예고 마지막 구간에서 성공하면 공격을 취소한다. 속박 등 취소형이 아닌 제어는 보스 동작이 협의 전이라 적용 표시와 면역 시작과 알림만 하고, 제어를 이동·회전·공격 정지로 일괄 해석하지 않는다. 가능성을 열어 두려고 제어는 값 묶음(`BossDirectControl`: 출처 ID, 요청 시간)으로 받고, 적용된 제어는 `BossBrain.DirectControlApplied` 알림(`BossDirectControlApplied`: 적용 시간(보스 정의의 최대 시간으로 자름), 패턴 취소 여부, 패턴 ID)으로 알린다. 보스를 실제로 어떻게 움직일지는 이 알림을 받는 쪽에서 나중에 붙인다.

연결 계약(모두 `Shinshi.AI`).

| 인터페이스 | 역할 | 구현 담당 |
|---|---|---|
| `IBossVitals` | 보스 체력 비율과 체력 0 여부 읽기. `ICombatTarget`에 체력 읽기가 없어 새로 둠 | 민성 (`BossServer`, 구현됨·실행 미검증) |
| `IBossControlSource` | 도착한 직접 제어를 `BossDirectControl`(출처 ID, 요청 시간) 한 건씩 넘김. 플레이어 기술의 속박(`ISkillControlTarget.ApplyRoot(sourceId, 시간, 시각)`)을 `BossServer`가 틱당 한 칸에 담아 넘기고 틱 끝에 비운다 | 민성 (`BossServer`, 구현됨·실행 미검증) |
| `IBossPatternFactory` | 패턴 정의로 시전 실행 객체 생성. 구현 `WolbaekPatternFactory`는 P01(`BossDashPattern`, `IBossMovement` 이동 창구가 있을 때만)과 P02와 P03과 P04(`BossProjectilePattern`, `IBossProjectileLauncher` 발사 창구가 있을 때만)와 P05(`BossAreaPattern`, `IBossAreaHost` 장판 관리자가 있을 때만)와 P06을 만든다. `IBossMovement`(벽 검사와 위치 이동)와 `IBossDashTelegraphListener`는 구현이 없으며 서버 몸체가 맡는다. 생성자에 Href를 받으며 기본값은 없고, 부채꼴·원형·직사각형 알림 리스너는 선택 인자다 | 기웅 (U10, P01과 P04, P05는 미구현) |
| `IBossEffectLifetime` | 지속 장판·투사체 수명, 이유별 취소, 동시 장판 수 조회. 투사체는 `BossProjectileManager`(`IBossTickableEffects`도 구현해 `BossCombat`이 공격 진행 뒤 매 틱 부른다)가 맡고, 사망·페이즈 전환·전투 종료로만 제거하며 직접 제어와 패턴 종료는 투사체를 지우지 않는다. 장판은 `BossAreaManager`(슬롯 예약·활성·주기 피해·정리)가 맡고, 투사체와 장판을 함께 넘길 때는 `BossEffectComposite`로 묶는다. 서버는 아직 빈 구현 `EmptyBossEffectLifetime`을 넣고 있어 P04와 P05는 실행되지 않는다 | 기웅 (U10, 투사체·장판 구현됨·서버 연결 미구현) |
| `IBossPatternCatalog` | 보스·패턴 정의 조회. 어댑터 `BossPatternCatalogSource`가 `BossPatternCatalogSO`를 감싼다 | 기웅 (구현됨) |
| `IBossRandom` | 타깃·패턴 선택 난수. 기본 `SeededBossRandom` | 기웅 (구현됨) |
| `BossTelegraphPresenter` (`03.AI/Boss/View/`, `Shinshi.Runtime`) | 서버가 확정한 P01·P02·P03·P06 예고·발동·취소를 시전 번호별로 그리는 클라이언트 표현 진입점. 서버 시각을 받아 늦게 받은 예고를 이어서 그리고 `Restore`로 복원한다. 임시 표현이며 서버→클라이언트 전달 창구는 없어 샘플 입력(`BossTelegraphSampleInput`)으로만 확인했다 | 기웅 (구현됨·실제 전달 미연결) |

기존 계약 `ICombatTargetQuery`(`CombatTargetRegistry`), `ICombatTarget`, `IDamageResolver`(`SkillDamageResolver`), `DamageRequest`를 그대로 쓴다.

서버 연결(민성, `98.Servers/02.Combat/Boss/`, 구현됨·EditMode 검증·실제 서버 실행 미검증).

- `BossServer`가 보스 몸체다. `IDamageable`·`ISkillControlTarget`·`IBossVitals`·`IBossControlSource`와 세 예고 리스너를 구현하고 `BossBrain`을 가진다. 최대 체력은 `HealthRef × Aref × 보스 체력 배율`, 방어는 보스 정의 값이다. 치명타는 `IDamageable` 계약대로 그 자리에서 `Dead`로 바꾸고, 등장 대기·시작 전·종료 뒤에는 무적이다. 예고 방향(조준 변경, 방향 고정)으로 정면을 돌린다. 위치는 원점 고정이다.
- `CombatServerRuntime`이 보스전 출정이 진행되는 첫 틱(배율 고정 직후)에 보스를 만들어 레지스트리에 적 진영으로 등록하고 `Begin`으로 전투를 연다. 틱 순서는 플레이어 기술·공격 → 보스 `Tick` → 생사 보고 → 세션 종료 판정 → (출정이 끝났으면) 같은 시각 `RequestCombatEnd`와 `Tick`이다.
- 보스 사망이 확정된 틱에 `DungeonSessionHost.ReportBossDefeated` → `DungeonSession.ReportBossDefeated`로 성공을 예약한다. 종료는 `CompleteTick`이 판정하므로 같은 틱 전원 사망이면 전멸이 우선한다. 승패의 기준은 세션 종료 사유다. 이 순서에서 보스는 이미 `Defeated`라 같은 틱 종료 요청을 받지 않아 `IsCombatEndRecorded`가 기록되지 않는다(기웅에게 계약 변경 요청).
- 보스가 내는 피해는 `BossDamageScaler`가 시작 인원 피해 배율을 `RawDamage`에 곱해 `SkillDamageResolver`에 넘긴다. 번호·위치는 바꾸지 않아 반격 방어와 중복 적중 판정이 유지된다.
- 보스와 전장 지역은 출정 계약에 없어 `Constants.Combat.DEV_BOSS_ID`(`boss_wolbaek`)·`DEV_BOSS_REGION_ID`(`region_forest`, 체험판 Aref·Href)로 연다. 정의가 없거나 `IsTuned`가 거짓이면 오류 로그 뒤 보스 없이 진행하며 개발 빌드는 `TryCompleteBossForDevelopment`로 끝낸다. 보스 몸체 반경은 임시값 `BOSS_BODY_RADIUS`다.

연결되지 않은 것.

- 보스 상태와 예고를 클라이언트에 보내는 복제(`NetworkBehaviour`, Rpc)와 보스 네트워크 개체 생성은 없다(민성). 지금 보스는 서버 메모리에만 있다.
- 출정 계약에서 보스·지역을 받는 것, 던전 씬의 보스 생성 지점, 보스를 만들지 못한 보스전의 출시 처리(서비스 취소 여부)는 미정이다(민성).
- 월백 P02와 P03과 P06만 실행이 구현됐고(EditMode 검증, 실제 서버 실행 미검증) P01과 P04, P05, 지속물 수명 관리자 구현은 없다(기웅). P02, P03, P06의 회복 0.5초는 임시값이며 확정 밸런스가 아니다. `BossPatternCatalog.asset`의 `wolbaek_p02`, `wolbaek_p03`, `wolbaek_p06` 데이터에서 조정한다. P03과 P06은 실행 수치만 입력했고 선택 가중치와 배율은 미입력이며(기획 문서에는 있다), 월백은 `IsTuned`가 거짓인 채다.
- P06(`BossLineBlastPattern`): 1.6초 예고, 처음 0.6초 동안 90도/초로 타깃을 추적한 뒤 방향을 고정하고, 예고가 끝나면 한 번 타격한다(`ActiveSeconds` 0). 판정은 타격 시점의 보스 서버 위치를 시작점으로 고정된 조준 방향의 수평 직사각형(전방 0~14m, 좌우 각 2m, 네 경계 포함, 뒤쪽 제외)이며, 데이터의 `Shape.Range`가 길이, `Shape.Width`가 전체 폭이다. 조회는 원형 반경 `sqrt(길이² + (폭/2)²)`에 1mm 여유를 더해 먼 모서리까지 후보에 넣고, 안팎은 직사각형으로 다시 가른다. 방어 불가(`Unblockable`)이며 RawDamage는 `DamageRef(0.45) × Href`다. 예고의 시작점·방향·길이·폭·시간은 `IBossLineTelegraphListener`(예고 시작, 추적 방향 변경, 방향 고정, 타격, 취소)로 내보낸다. 예고 시작 알림의 시작점은 그때의 보스 위치이고 타격은 그 시점의 보스 위치에서 판정하므로, 보스가 예고 중 움직이면 표현 쪽이 보스를 따라가야 한다(보스 이동은 미정).
- P06 직접 제어: `wolbaek_p06` 데이터에 예고 단계, 마지막 0.4초, 취소형으로 입력했고 월백 보스 정의에 최대 1초와 면역 2초를 입력했다. 취소는 새 경로 없이 기존 `BossCombat`→`BossAttackFsm` 흐름을 쓴다. 구간 시작 직전은 취소되지 않고(그 제어는 버려지며 같은 시전의 구간 안 제어는 받는다), 시작 경계와 구간 안은 취소되며, 취소 뒤에는 타격이 나가지 않는다. 구간 계산은 `(double)ControlWindowSeconds`와 `(double)TelegraphSeconds`를 쓰므로 0.4, 1.6이 아니라 float 값 기준이다.
- 직접 제어 구간의 끝 경계(예고 종료 시각) 현재 동작: `BossAttackFsm.IsControlWindowOpen`이 끝 경계를 포함(`<=`)하지만 한 틱의 순서가 직접 제어 → 공격 진행이라, 예고 종료 시각과 정확히 같은 틱에 제어가 먼저 도착해 아직 예고 단계일 때만 취소된다. 틱이 그 시각을 넘어서 도착하면 단계가 아직 예고여도 `serverTime > 종료`라 버려진다. 포함 여부는 기획 미정이며 이번 작업에서 바꾸지 않았고 이 경계에 기대는 테스트도 두지 않았다.
- P02, P03, P06에 넘기는 Href는 전장 지역의 기준값이고 시작 인원 피해 배율은 `BossDamageScaler`가 곱한다(위 서버 연결). 예고 리스너를 받아 클라이언트 VFX로 전달하는 복제와 보스 몸체 이동은 없다(회전은 서버 정면만 바꾼다). 원형·직사각형 VFX 모듈과 P03·P06 예고 시각 일치, P06 예고 방향 고정과 몸체 연출은 미검증이다. 회복 중 취소가 와도 패턴은 취소 알림을 낸다(P02, P03, P06 공통, 타격 이후라 피해에는 영향 없다). 표현 쪽은 타격 알림을 받은 시전의 취소 알림을 무시해야 한다.
- 컷씬 길이를 담는 데이터 필드가 없어 `BeginAppearance`의 길이는 호출자가 넘긴다.
- 속박 등 취소형이 아닌 제어가 보스를 움직이는 동작은 구현하지 않았다. 알림만 나간다.

미정·보류인 것.

- 월백 2페이즈의 세부 설계와 구현 순서(`AwaitingHandoff`에서 멈춘다)
- 그로기 도입과 세부 규칙
- 직접 제어 구간의 끝 경계 포함 여부. 지금 코드는 포함하지만 확정이 아니며 이 값에 기대는 테스트는 없다
- 속박 등 직접 제어가 보스를 어떻게 움직이는지(기획 문서는 허용 구간에서만, 끌어오지 않고 이동 없이 최대 1초까지만 정했다. 속박 중 공격과 회전을 멈추는지는 없다)
- 같은 시각 보스 사망과 전원 사망의 결과(기획의 초기 기준은 전멸 우선이며 실제 전투 피드백 뒤 조정)
- 등장 컷씬의 시작 조건과 길이, 길이 데이터의 위치

## 9. 인증·재접속·서버 수명 연결

1. `ServerBootstrap`이 역할·포트·콘텐츠 버전을 읽고 GameDB를 확인한 뒤 콘텐츠 해시를 계산한다. 전투 설치, 던전 역할의 `DungeonSessionHost`·플레이어 유지 창구 결합, 서버 인증기 설치를 끝낸 뒤 `StartServer`를 부른다.
2. 클라이언트는 `ShinshiClientAuthenticator.SetJoinToken`을 `StartClient` 전에 부른다. 연결 직후 `JoinRequestMessage(protocolVersion, contentHash, joinToken)`을 보낸다. 서버는 버전·해시·토큰 길이와 인증 대기 상한·마감을 먼저 확인한다.
3. 출시 서버는 `ApiJoinTokenVerifier`가 [일회용 토큰 교환](../../Assets/01.Scripts/00.Common/Contracts/Join/JoinApiRoutes.cs)을 한 번 호출하고 인스턴스·역할·캐릭터·출정·참가자 정보를 확인한다. 게임 서버의 HTTP 호출은 `Authorization: Bearer {token}`을 쓴다. 서비스 자격은 서버 임대 발급에, 이후 게임 서버 경로는 `ServerPresence`가 받은 서버 임대 토큰에 묶인다. 일반 플레이어 접근 토큰으로 서버 전용 경로를 호출하지 않는다.
4. `ServerSessionRegistry`가 같은 참가자의 동시 연결을 거절하고 `SessionBinding`을 만든다. 마을 참가자 키는 캐릭터 ID, 던전 키는 출정 참가자 ID다. 계정 전체의 단일 접속 임대는 백엔드가 담당한다. 던전은 `DungeonSessionHost.Admit`까지 통과해야 연결의 `authenticationData`를 설정하고 `ServerAccept`한다. 인증 이전에는 플레이어 생성·Command를 허용하지 않는다.
5. 동시 접속 거절은 `AlreadyConnected(809)`이며 사용자 안내는 **“이미 접속 중인 계정”**이다. 이전 연결의 끊김 감지가 늦을 때를 위해 클라이언트는 2초 간격으로 최대 6회 같은 토큰을 다시 보낸다. 서버 검증기는 교환 성공 정보를 이 서버에서만 최대 20초 기억하고 입장 성공 시 지운다. 재시도 중에는 `JoinRetryScheduled`, 최종 거절 때는 `Rejected`를 UI가 받는다. 백엔드 일시 불가는 `BackendUnavailable(810)`이고, `RateLimited(811)`과 정산 상한 `SettlementLimitExceeded(904)`는 별도 코드다.
6. 진행 중 던전에서 끊긴 원래 참가자는 `ShinshiNetworkManager.OnServerDisconnect`가 소유권만 떼어 서버에 개체를 남긴다. 유예 중에도 피격·사망·기술 타이머가 진행된다. 새 일회 재입장 토큰을 [rejoin-ticket 경로](../../Assets/01.Scripts/00.Common/Contracts/ApiRoutes.cs)로 받고 인증되면 같은 참가자·같은 개체를 새 연결에 넘긴다. 개체가 실제로 삭제된 경로는 `ReconnectVault`가 전투 스냅샷을 보존한다. 유예 만료 후 진행 중 출정은 참가자를 닫고 개인 실패 정산으로 이어지며 재합류를 막는다.
7. 마을 연결 종료는 참가자 키를 지우고 `AccountLeaseReleaser`로 계정 접속 임대 해제를 보낸다. 시작 전 던전 끊김은 start를 막고 예약 만료를 기다린다. 던전 입장자의 계정 임대 이관이 모두 확정된 뒤 같은 참가자 집합·operationId로 start를 요청하고 영수증을 받아 Running에 들어간다.
8. 서비스 자격이 있으면 `ServerPresence`가 임대 발급 → 새 bootId로 풀 등록 → heartbeat를 수행한다. 던전은 출정 하나가 끝나면 `ServerShutdownController`가 드레인을 요청하고 결과 확인 유예·연결 종료·전송 flush 뒤 프로세스를 종료한다. systemd 던전 유닛은 `Restart=always`로 새 프로세스·새 bootId를 띄운다. 이 애플리케이션 종료 경로와 OS `SIGTERM`의 자동 드레인 연동은 구분하며, 후자의 실행 검증 근거는 없다.

에디터·개발 빌드의 기본 검증기는 `dev`이고 형식만 확인한다. `JOIN_TOKEN_VERIFIER=api` 또는 `-join-verifier api`와 서비스 자격을 넣어 API 경로를 시험한다. 개발 저장 경로는 경제 결과를 남기지 않는다. 출시 빌드는 개발 검증기를 포함하지 않는다. 실제 실행 설정·자격 공급은 [BackendLaunchSettings](../../Assets/01.Scripts/98.Servers/01.Network/Backend/BackendLaunchSettings.cs)와 [서버 운영 원칙](NETWORK_AUTHORITY.md) 7장을 따른다.
