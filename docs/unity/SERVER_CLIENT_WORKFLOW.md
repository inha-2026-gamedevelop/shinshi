# 서버·클라이언트 협업

> 2026-10-01 개정. 네트워크 라이브러리는 Mirror 96.11.3(`Assets/Mirror/`), 호스트 방식은 데디케이티드 서버(확정)다. 그믐에서 이식한 partial 분할(`.server.cs`·`.network.cs`) 대신 **역할별 클래스 분리와 서버 전용 어셈블리**를 쓴다. asmdef(`Shinshi.Runtime`·`Shinshi.Server`·`Shinshi.Contracts`·`Shinshi.Dev`·테스트)와 서버 부트스트랩은 있고 `Net/` 상태 클래스는 아직 없으며, 첫 네트워크 기능부터 이 구조로 시작한다.

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
  00.Common/                            {팀장}: 공용 계약·ClientView 기반 클래스
  01.Network/                           {팀장}: 세션·연결·ServerLinks
  {NN}.{도메인}/Enemy/
    Rules/EnemyTelegraphRules.cs        파트 담당: 서버·클라 공용 계산
    Net/EnemyState.cs                   {팀장}: 동기화 상태·Rpc
    Net/IEnemyServerHandler.cs          {팀장}: 서버 처리기 계약 (요청이 있을 때만)
    View/EnemyView.cs                   파트 담당: 애니메이션·VFX
  {NN}.UI/                              UI 담당
  98.Servers/                           Shinshi.Server (UNITY_SERVER || UNITY_EDITOR)
    Shinshi.Server.asmdef               {팀장}
    01.Network/ServerBootstrap.cs       {팀장}: 역할별 서버 객체 생성·ServerLinks 등록
    {NN}.{도메인}/Enemy/EnemyBrain.cs    AI 담당: 타깃 선택·상태 머신
    {NN}.{도메인}/Shop/ShopServer.cs     {팀장}: 요청 검증·규칙 호출·결과 전달
  99.Dev/                               Shinshi.Dev
```

- `98.Servers/` 하위 경로는 `Assets/01.Scripts/{NN}.{도메인}/{기능}/`을 그대로 비춘다. 기능 폴더 아래에 `Server/`를 다시 만들지 않는다.
- `98.Servers/`는 {팀장} 소유다. 예외로 AI·웨이브 경로(`98.Servers/03.AI/`, `98.Servers/04.Systems/Wave/`)는 해당 파트 담당과 {팀장}이 함께 소유한다. 경로별 지정은 `.github/CODEOWNERS`가 기준이다.
- 각 도메인의 `Net/`은 {팀장} 소유다. 새 동기화 필드·`[Command]`·Rpc나 처리기 인터페이스 변경이 필요하면 파트 담당이 {팀장}에게 요청한다.
- 서버 클래스는 프리팹·씬에 붙이지 않는다. `ServerBootstrap`이 상태의 `OnStartServer` 알림을 받아 만든다.
- 표현·UI는 상태의 읽기 프로퍼티·이벤트와 공개 요청 창구만 쓴다. `Shinshi.Server` 타입은 `Shinshi.Runtime`에서 보이지 않으므로 컴파일 단계에서 막힌다. 같은 어셈블리의 `[Server]` 상태 변경 메서드를 부르는 것은 리뷰로 막는다.
- 서버와 클라이언트가 함께 쓰는 계산(예고 표시가 쓰는 패턴 명세, UI가 쓰는 비용 조회)은 도메인 `Rules/`에 두고 `98.Servers/`로 옮기지 않는다. 서버만 쓰는 계산을 `Rules/`에 두면 클라이언트 빌드에 실린다.

## 4. 기능 하나를 진행하는 순서

1. 기능 이슈에 사용자 행동, 요청 인자, 읽을 상태, 성공·거절 표시를 적는다. 예: 상품 버튼 → 상품 종류·현재 revision 전송 → 서버 결과 후 잔액 갱신.
2. {팀장}이 상태 클래스와 처리기 인터페이스(최소 API 계약과 동작하는 요청 창구)를 먼저 만든다. 파트 담당은 `Rules/`에 공용 계산을, AI·웨이브 담당은 `98.Servers/`에 서버 두뇌를 작성한다. 판정 PR·네트워크 PR·소비자 PR을 서로 연결한다.
3. 표현·UI 담당은 표시용 샘플 상태로 레이아웃·애니메이션을 병행한다. 샘플은 테스트·개발 표현에만 사용하고 생산 서버 상태에 쓰지 않는다. 실제 요청 경로가 없으면 UI에서 사용 불가로 표시한다.
4. {팀장}은 `98.Servers/`의 처리기에서 호출자·소유권·상태·거리·revision·중복 요청 검증을 하고, 규칙을 호출해 상태를 바꾼 뒤 결과를 전달한다. 판정 규칙 자체는 판정 담당 파트가 작성한다. 서버 자체 행동인 AI·스폰은 네트워크 요청 API를 만들 필요가 없다.
5. 클라이언트·UI 코드는 공개 요청 계약대로 요청하고, 결과·현재 상태로 화면을 갱신한다. 클라이언트 API 안내 문서는 **(작성 예정)** 첫 서버 API가 생길 때 만든다. API나 네트워크 멤버가 부족하면 {팀장}에게 계약 변경을 요청한다.
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

2026-10-01 코드 기준으로 `Assets/01.Scripts/`의 게임플레이 C#은 보스 서버 뼈대(8장)뿐이다. `00.Common/`에 공통 계약, `01.Network/`와 `98.Servers/01.Network/`에 네트워크 매니저와 서버 부트스트랩, `02.Combat/Damage/`와 `98.Servers/`에 파트 간 인터페이스가 있고, 게임용 `Net/` 상태 클래스와 그 구현체는 아직 없다.

- asmdef(`Shinshi.Runtime`·`Shinshi.Server`·`Shinshi.Contracts`·`Shinshi.Dev`·테스트)는 만들어져 있고 첫 `NetworkBehaviour`부터 위 배치를 따른다. `00.Common/` 코드는 `Shinshi.Runtime`에 들어간다.
- `ServerLinks`(`01.Network/`), `ClientView`(`00.Common/Utility/`), `ServerBootstrap`(`98.Servers/01.Network/`)은 있다. `ServerBootstrap`은 서버 시작과 GameDB 확인까지만 하며, 서버 객체를 만들어 `ServerLinks`에 등록하는 부분과 `ServerLinks.Register`를 부르는 곳은 아직 없다.
- 나중에 기존 파일을 분리할 때도 `.cs.meta` GUID와 씬·프리팹 참조를 보존한다.
- GitHub 브랜치 보호 설정 적용 여부는 별도 확인이 필요하다.
- 파트가 확정되고 파일이 생기면 도메인별 상태·서버·규칙 클래스 목록을 이 절에 표로 적는다.

## 7. 일반전 웨이브 서버 경계

웨이브·스폰 작성(기웅)과 세션 연결(민성)이 같은 파일을 고치지 않도록 `98.Servers/`에 인터페이스와 값 타입만 먼저 두었다. 구현 클래스는 아직 없다.

| 파일 | 네임스페이스 | 구현 | 호출 |
|---|---|---|---|
| `98.Servers/03.AI/Spawn/ISpawnDirector.cs` | `Shinshi.AI` | 기웅 | 웨이브 세션이 `TryRequestSpawn(waveIndex, sourceId)`로 출현 요청, 활성 수·대기 수 조회, `Halt` |
| `98.Servers/03.AI/Spawn/ISpawnProgressListener.cs` | `Shinshi.AI` | 웨이브 세션(기웅) | 스폰 디렉터가 출현·처치·소멸을 알림 |
| `98.Servers/04.Systems/Wave/IWaveSession.cs` | `Shinshi.Systems` | 기웅 | 세션 바인딩이 `Begin`·`Tick`(서버 시각)·`Abort`, `Progress` 조회 |
| `98.Servers/04.Systems/Wave/IWaveSessionListener.cs` | `Shinshi.Systems` | 세션 바인딩(민성) | 웨이브 세션이 처치·웨이브 종료·세션 종료(`Success`/`Timeout`)를 알림 |
| `WaveSessionOutcome`·`WaveProgressSnapshot` | `Shinshi.Systems` | 값 타입 | 종료 결과, HUD 동기화 값 |

- 의존은 `Wave → Spawn` 한 방향이다. 세션 바인딩은 `IWaveSession`·`IWaveSessionListener`만 알고 스폰 디렉터를 직접 부르지 않는다.
- 웨이브 쪽은 `WaveConfigSO` 내부 필드를 계약에 노출하지 않고 `int waveIndex`(0부터)와 `string sourceId`만 주고받는다. 참가자 ID는 `string`이다.
- 활성 상한 때문에 바로 나오지 못한 출현은 디렉터가 대기열에 넣고 빈자리에 순서대로 내보낸다. 수락한 요청은 버리지 않는다(지연 스폰 누락 0).
- 적의 퇴장은 활성 수를 줄여야 하므로 반드시 스폰 디렉터를 거쳐 통지한다.
- 개인 드롭은 웨이브에서 판정하지 않는다. 세션 바인딩이 `OnEnemyDefeated`의 `sourceId`를 원인 ID로 개인 판정에 넘긴다. 처치자 ID는 기록용이며 보상 자격은 참가자 명단이 정한다.
- 한 처치가 웨이브를 끝내면 `OnEnemyDefeated → OnWaveCleared → OnWaveSessionEnded` 순서로 부른다. `OnWaveSessionEnded`는 한 번만 오며 여기서 정산 요청을 발행하고, 정산 결과 판정은 U14가 한다.
- 제한 시간은 `Begin`에 넘긴 시각(Running 진입)부터 잰다. 시각은 바인딩이 `NetworkTime`에서 읽어 넘기며 웨이브 코드는 Mirror를 참조하지 않는다.
- `Abort`는 서비스 장애 취소처럼 바깥 원인으로 멈출 때 쓰며 결과 통지를 보내지 않는다.
- 구현 클래스의 생성 위치와 생성자 인자, 소멸 적의 집계, 시간 초과 뒤 남은 적 처리, 마지막 처치와 420초가 같은 틱일 때의 우선순위는 미정이다.

## 8. 보스 서버 경계

보스 AI의 서버 뼈대를 `98.Servers/03.AI/Boss/`(`Shinshi.AI`, 기웅 작성, 민성 공동 리뷰)에 구현했다. 서버 시각을 바깥에서 받아 진행하는 일반 C# 클래스이며 Mirror와 `MonoBehaviour`에 의존하지 않는다. 가짜 의존성으로 EditMode 테스트(`98.Servers/03.AI/Boss/Tests/`의 별도 asmdef `Shinshi.Tests.Server.AI`)를 돌려 검증했다. 전용 서버와 클라이언트를 띄운 실행 검증은 하지 않았다. `Assets/Tests/`가 민성 소유라 테스트를 임시로 이 폴더에 두었으며 옮길지는 민성과 정한다.

구조는 `상위 FSM(BossBrain) → Combat(BossCombat) → PhaseState / AttackState FSM → Combat BT → 상황별 가중치 선택 → 패턴 실행`이다.

| 클래스 | 책임 |
|---|---|
| `BossBrain` | 상위 FSM(`Inactive → Combat → Defeated / Ended`). 사망과 전투 종료를 판단해 취소를 지시한다. 진입점 `Begin`·`Tick(serverTime)`·`RequestCombatEnd` |
| `BossCombat` | 전투 중 한 틱의 순서를 지키고 두 FSM과 BT를 묶는다 |
| `BossPhaseFsm` | `Active → TransitionPending → AwaitingHandoff`. 새 선택은 `Active`일 때만 허용한다 |
| `BossAttackFsm` | `Ready → Telegraph → Execute → Recover → Ready`. 단계 타이머와 직접 제어 구간을 서버 시각으로 계산한다 |
| `BossSelectionTree`, `Bt*` | 경량 BT. 공격 FSM이 `Ready`이고 페이즈가 `Active`일 때만 평가한다 |
| `BossPatternSelector` | 거리·방향·주변 인원 구간과 가중치 곱, 비례 선택. 상태 없는 순수 계산 |
| `IBossPattern`, `BossPatternBase` | 패턴 한 번의 실행. 취소 뒤에는 피해 요청이 `IDamageResolver`까지 가지 않는다 |

- 한 틱의 우선순위는 사망·전투 종료 → 페이즈 전환 → 허용된 직접 제어 → 일반 공격 진행·새 선택이다. 이미 적용된 피해는 되돌리지 않는다.
- 상위 FSM이 중단 이유(`EBossInterruptReason`)를 정하고 취소를 지시한다. 현재 패턴은 자신의 실행과 예약 타격을 정리하고, 지속 장판과 투사체는 `IBossEffectLifetime`이 이유별로 정리한다.
- 예고, 실행, 회복 중에도 사망, 페이즈 전환, 직접 제어 검사는 계속한다. BT는 새 행동을 고를 수 있을 때만 평가한다.
- 체력 0(`IBossVitals.IsHealthZero`)과 사망 확정(`IBossDeathPolicy`)은 구분한다. 기본 정책 `ImmediateBossDeathPolicy`는 체력 0이면 바로 확정하는 임시 구현이며 최종 발악이 도입되면 교체한다.
- 오프닝과 강제 후속 공격은 구현하지 않았다. 일반 가중 선택 앞에 평가되는 `IBossSelectionRule` 자리만 있고 등록된 규칙은 없다. 기존 필드 `OpeningDelaySeconds`는 첫 선택 가능 시각에만 반영한다.

연결 계약(모두 `Shinshi.AI`, 인터페이스만 두었고 아래 구현은 아직 없다).

| 인터페이스 | 역할 | 구현 담당 |
|---|---|---|
| `IBossVitals` | 보스 체력 비율과 체력 0 여부 읽기. `ICombatTarget`에 체력 읽기가 없어 새로 둠 | 민성 (U05) |
| `IBossControlSource` | 도착한 직접 제어를 한 건씩 넘김. 플레이어 기술이 보스에 제어를 거는 계약이 없어 **실제 제어 연결은 미구현** | 민성 (U05, U07) |
| `IBossPatternFactory` | 패턴 정의로 시전 실행 객체 생성. 월백 6패턴 구현 | 기웅 (U10) |
| `IBossEffectLifetime` | 지속 장판·투사체 수명, 이유별 취소, 동시 장판 수 조회 | 기웅 (U10) |
| `IBossPatternCatalog` | 보스·패턴 정의 조회. 어댑터 `BossPatternCatalogSource`가 `BossPatternCatalogSO`를 감싼다 | 기웅 (구현됨) |
| `IBossRandom` | 타깃·패턴 선택 난수. 기본 `SeededBossRandom` | 기웅 (구현됨) |

기존 계약 `ICombatTargetQuery`, `ICombatTarget`, `IDamageResolver`, `DamageRequest`를 그대로 쓴다. 이 계약들의 구현체는 아직 없다.

연결되지 않은 것.

- `BossBrain.Tick`을 누가 어떤 주기로 부르는지와 `BossBrain`을 만들어 `ServerLinks`에 연결하는 서버 부트스트랩은 없다(민성).
- 보스 상태와 예고를 클라이언트에 보내는 복제(`NetworkBehaviour`, Rpc)는 없다(민성).
- 월백 6패턴 실행, 지속물 수명 관리자 구현은 없다(기웅).

미정인 게임 규칙.

- 최종 발악 도입 여부와 방식
- 직접 제어가 걸린 동안 보스의 동작, 취소형이 아닌 제어는 면역 시작만 표시한다
- 같은 틱에 사망과 전투 종료가 겹칠 때 기록할 이유(뼈대는 사망을 먼저 기록하며 승패는 세션이 판정한다)
- 보스 사망 때 남은 장판과 투사체를 지울지
- 2페이즈 인계(`AwaitingHandoff`에서 멈춘다)
