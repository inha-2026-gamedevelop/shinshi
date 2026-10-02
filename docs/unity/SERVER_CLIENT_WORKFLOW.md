# 서버·클라이언트 협업

> 2026-10-02 M-A 코드와 동기화. 네트워크 라이브러리는 Mirror 96.11.3(`Assets/Mirror/`), 호스트 방식은 데디케이티드 서버(확정)다. **역할별 클래스 분리와 서버 전용 어셈블리**가 적용돼 있고, 연결·인증·전투·출정 저장·정산 코드가 있다. 서버 빌드·M2/M3·IL2CPP·배포는 `구현됨·실행 미검증`이며, 일반전 웨이브·스폰 실행 구현과 보스 마지막 단계 연결은 남아 있다.

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
  02.Combat/Skills/                      전투 담당: 기술 입력·동료 상태
  {NN}.UI/                              UI 담당
  98.Servers/                           Shinshi.Server (UNITY_SERVER || UNITY_EDITOR)
    Shinshi.Server.asmdef               {팀장}
    01.Network/                        {팀장}: 부트스트랩·인증·게이트웨이·존재 신호·종료
    01.Network/Session/                {팀장}: 출정 명단·이벤트 저장·드롭·정산
    02.Combat/                         전투 담당: 서버 틱·Player/·Damage/·Skills/
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
| 연결·입장 | [ShinshiNetworkManager](../../Assets/01.Scripts/01.Network/ShinshiNetworkManager.cs), [ShinshiClientAuthenticator](../../Assets/01.Scripts/01.Network/ShinshiClientAuthenticator.cs), `JoinRequestMessage`·`JoinResponseMessage`·`ServerLinks` | [ServerBootstrap](../../Assets/01.Scripts/98.Servers/01.Network/ServerBootstrap.cs), `ShinshiServerAuthenticator`·`ApiJoinTokenVerifier`·`ServerSessionRegistry`·`SessionBinding` |
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

## 8. 인증·재접속·서버 수명 연결

1. `ServerBootstrap`이 역할·포트·콘텐츠 버전을 읽고 GameDB를 확인한 뒤 콘텐츠 해시를 계산한다. 전투 설치, 던전 역할의 `DungeonSessionHost`·플레이어 유지 창구 결합, 서버 인증기 설치를 끝낸 뒤 `StartServer`를 부른다.
2. 클라이언트는 `ShinshiClientAuthenticator.SetJoinToken`을 `StartClient` 전에 부른다. 연결 직후 `JoinRequestMessage(protocolVersion, contentHash, joinToken)`을 보낸다. 서버는 버전·해시·토큰 길이와 인증 대기 상한·마감을 먼저 확인한다.
3. 출시 서버는 `ApiJoinTokenVerifier`가 [일회용 토큰 교환](../../Assets/01.Scripts/00.Common/Contracts/Join/JoinApiRoutes.cs)을 한 번 호출하고 인스턴스·역할·캐릭터·출정·참가자 정보를 확인한다. 게임 서버의 HTTP 호출은 `Authorization: Bearer {token}`을 쓴다. 서비스 자격은 서버 임대 발급에, 이후 게임 서버 경로는 `ServerPresence`가 받은 서버 임대 토큰에 묶인다. 일반 플레이어 접근 토큰으로 서버 전용 경로를 호출하지 않는다.
4. `ServerSessionRegistry`가 같은 참가자의 동시 연결을 거절하고 `SessionBinding`을 만든다. 마을 참가자 키는 캐릭터 ID, 던전 키는 출정 참가자 ID다. 계정 전체의 단일 접속 임대는 백엔드가 담당한다. 던전은 `DungeonSessionHost.Admit`까지 통과해야 연결의 `authenticationData`를 설정하고 `ServerAccept`한다. 인증 이전에는 플레이어 생성·Command를 허용하지 않는다.
5. 동시 접속 거절은 `AlreadyConnected(809)`이며 사용자 안내는 **“이미 접속 중인 계정”**이다. 이전 연결의 끊김 감지가 늦을 때를 위해 클라이언트는 2초 간격으로 최대 6회 같은 토큰을 다시 보낸다. 서버 검증기는 교환 성공 정보를 이 서버에서만 최대 20초 기억하고 입장 성공 시 지운다. 재시도 중에는 `JoinRetryScheduled`, 최종 거절 때는 `Rejected`를 UI가 받는다. 백엔드 일시 불가는 `BackendUnavailable(810)`이고, `RateLimited(811)`과 정산 상한 `SettlementLimitExceeded(904)`는 별도 코드다.
6. 진행 중 던전에서 끊긴 원래 참가자는 `ShinshiNetworkManager.OnServerDisconnect`가 소유권만 떼어 서버에 개체를 남긴다. 유예 중에도 피격·사망·기술 타이머가 진행된다. 새 일회 재입장 토큰을 [rejoin-ticket 경로](../../Assets/01.Scripts/00.Common/Contracts/ApiRoutes.cs)로 받고 인증되면 같은 참가자·같은 개체를 새 연결에 넘긴다. 개체가 실제로 삭제된 경로는 `ReconnectVault`가 전투 스냅샷을 보존한다. 유예 만료 후 진행 중 출정은 참가자를 닫고 개인 실패 정산으로 이어지며 재합류를 막는다.
7. 마을 연결 종료는 참가자 키를 지우고 `AccountLeaseReleaser`로 계정 접속 임대 해제를 보낸다. 시작 전 던전 끊김은 start를 막고 예약 만료를 기다린다. 던전 입장자의 계정 임대 이관이 모두 확정된 뒤 같은 참가자 집합·operationId로 start를 요청하고 영수증을 받아 Running에 들어간다.
8. 서비스 자격이 있으면 `ServerPresence`가 임대 발급 → 새 bootId로 풀 등록 → heartbeat를 수행한다. 던전은 출정 하나가 끝나면 `ServerShutdownController`가 드레인을 요청하고 결과 확인 유예·연결 종료·전송 flush 뒤 프로세스를 종료한다. systemd 던전 유닛은 `Restart=always`로 새 프로세스·새 bootId를 띄운다. 이 애플리케이션 종료 경로와 OS `SIGTERM`의 자동 드레인 연동은 구분하며, 후자의 실행 검증 근거는 없다.

에디터·개발 빌드의 기본 검증기는 `dev`이고 형식만 확인한다. `JOIN_TOKEN_VERIFIER=api` 또는 `-join-verifier api`와 서비스 자격을 넣어 API 경로를 시험한다. 개발 저장 경로는 경제 결과를 남기지 않는다. 출시 빌드는 개발 검증기를 포함하지 않는다. 실제 실행 설정·자격 공급은 [BackendLaunchSettings](../../Assets/01.Scripts/98.Servers/01.Network/BackendLaunchSettings.cs)와 [서버 운영 원칙](NETWORK_AUTHORITY.md) 7장을 따른다.
