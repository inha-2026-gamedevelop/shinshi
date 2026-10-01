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

연결 계약(모두 `Shinshi.AI`, 인터페이스만 두었고 아래 구현은 아직 없다).

| 인터페이스 | 역할 | 구현 담당 |
|---|---|---|
| `IBossVitals` | 보스 체력 비율과 체력 0 여부 읽기. `ICombatTarget`에 체력 읽기가 없어 새로 둠 | 민성 (U05) |
| `IBossControlSource` | 도착한 직접 제어를 `BossDirectControl`(출처 ID, 요청 시간) 한 건씩 넘김. 플레이어 기술이 보스에 제어를 거는 계약이 없어 **실제 제어 연결은 미구현** | 민성 (U05, U07) |
| `IBossPatternFactory` | 패턴 정의로 시전 실행 객체 생성. 구현 `WolbaekPatternFactory`는 P02와 P03과 P06만 만들고 나머지는 null. 생성자에 Href를 받으며 기본값은 없고, 부채꼴·원형·직사각형 알림 리스너는 선택 인자다 | 기웅 (U10, P01과 P04, P05는 미구현) |
| `IBossEffectLifetime` | 지속 장판·투사체 수명, 이유별 취소, 동시 장판 수 조회 | 기웅 (U10) |
| `IBossPatternCatalog` | 보스·패턴 정의 조회. 어댑터 `BossPatternCatalogSource`가 `BossPatternCatalogSO`를 감싼다 | 기웅 (구현됨) |
| `IBossRandom` | 타깃·패턴 선택 난수. 기본 `SeededBossRandom` | 기웅 (구현됨) |

기존 계약 `ICombatTargetQuery`, `ICombatTarget`, `IDamageResolver`, `DamageRequest`를 그대로 쓴다. 이 계약들의 구현체는 아직 없다.

연결되지 않은 것.

- `BossBrain.Tick`을 누가 어떤 주기로 부르는지와 `BossBrain`을 만들어 `ServerLinks`에 연결하는 서버 부트스트랩은 없다(민성).
- 보스 상태와 예고를 클라이언트에 보내는 복제(`NetworkBehaviour`, Rpc)는 없다(민성).
- 등장 대기 중 보스가 받는 피해를 막는 것은 `IDamageResolver` 쪽이 `FlowState`를 읽어 `Invulnerable`로 처리해야 한다. 뼈대는 상태만 노출하고 이 연결은 없다(민성).
- 월백 P02와 P03과 P06만 실행이 구현됐고(EditMode 검증, 실제 서버 실행 미검증) P01과 P04, P05, 지속물 수명 관리자 구현은 없다(기웅). P02, P03, P06의 회복 0.5초는 임시값이며 확정 밸런스가 아니다. `BossPatternCatalog.asset`의 `wolbaek_p02`, `wolbaek_p03`, `wolbaek_p06` 데이터에서 조정한다. P03과 P06은 실행 수치만 입력했고 선택 가중치와 배율은 미입력이며(기획 문서에는 있다), 월백은 `IsTuned`가 거짓인 채다.
- P06(`BossLineBlastPattern`): 1.6초 예고, 처음 0.6초 동안 90도/초로 타깃을 추적한 뒤 방향을 고정하고, 예고가 끝나면 한 번 타격한다(`ActiveSeconds` 0). 판정은 타격 시점의 보스 서버 위치를 시작점으로 고정된 조준 방향의 수평 직사각형(전방 0~14m, 좌우 각 2m, 네 경계 포함, 뒤쪽 제외)이며, 데이터의 `Shape.Range`가 길이, `Shape.Width`가 전체 폭이다. 조회는 원형 반경 `sqrt(길이² + (폭/2)²)`에 1mm 여유를 더해 먼 모서리까지 후보에 넣고, 안팎은 직사각형으로 다시 가른다. 방어 불가(`Unblockable`)이며 RawDamage는 `DamageRef(0.45) × Href`다. 예고의 시작점·방향·길이·폭·시간은 `IBossLineTelegraphListener`(예고 시작, 추적 방향 변경, 방향 고정, 타격, 취소)로 내보낸다. 예고 시작 알림의 시작점은 그때의 보스 위치이고 타격은 그 시점의 보스 위치에서 판정하므로, 보스가 예고 중 움직이면 표현 쪽이 보스를 따라가야 한다(보스 이동은 미정).
- P06 직접 제어: `wolbaek_p06` 데이터에 예고 단계, 마지막 0.4초, 취소형으로 입력했고 월백 보스 정의에 최대 1초와 면역 2초를 입력했다. 취소는 새 경로 없이 기존 `BossCombat`→`BossAttackFsm` 흐름을 쓴다. 구간 시작 직전은 취소되지 않고(그 제어는 버려지며 같은 시전의 구간 안 제어는 받는다), 시작 경계와 구간 안은 취소되며, 취소 뒤에는 타격이 나가지 않는다. 구간 계산은 `(double)ControlWindowSeconds`와 `(double)TelegraphSeconds`를 쓰므로 0.4, 1.6이 아니라 float 값 기준이다.
- 직접 제어 구간의 끝 경계(예고 종료 시각) 현재 동작: `BossAttackFsm.IsControlWindowOpen`이 끝 경계를 포함(`<=`)하지만 한 틱의 순서가 직접 제어 → 공격 진행이라, 예고 종료 시각과 정확히 같은 틱에 제어가 먼저 도착해 아직 예고 단계일 때만 취소된다. 틱이 그 시각을 넘어서 도착하면 단계가 아직 예고여도 `serverTime > 종료`라 버려진다. 포함 여부는 기획 미정이며 이번 작업에서 바꾸지 않았고 이 경계에 기대는 테스트도 두지 않았다.
- P02, P03, P06에 넘길 Href(지역과 난이도로 결정)와 시작 인원 피해 배율을 곱할 위치는 서버 연결이 정한다(민성). `IBossFanTelegraphListener`, `IBossCircleTelegraphListener`, `IBossLineTelegraphListener`를 받아 클라이언트 VFX로 전달하는 복제와 보스 몸체 이동·회전도 없다. 원형·직사각형 VFX 모듈과 P03·P06 예고 시각 일치, P06 예고 방향 고정과 몸체 연출은 미검증이다. 회복 중 취소가 와도 패턴은 취소 알림을 낸다(P02, P03, P06 공통, 타격 이후라 피해에는 영향 없다). 표현 쪽은 타격 알림을 받은 시전의 취소 알림을 무시해야 한다.
- 컷씬 길이를 담는 데이터 필드가 없어 `BeginAppearance`의 길이는 호출자가 넘긴다.
- 속박 등 취소형이 아닌 제어가 보스를 움직이는 동작은 구현하지 않았다. 알림만 나간다.

미정·보류인 것.

- 월백 2페이즈의 세부 설계와 구현 순서(`AwaitingHandoff`에서 멈춘다)
- 그로기 도입과 세부 규칙
- 직접 제어 구간의 끝 경계 포함 여부. 지금 코드는 포함하지만 확정이 아니며 이 값에 기대는 테스트는 없다
- 속박 등 직접 제어가 보스를 어떻게 움직이는지(기획 문서는 허용 구간에서만, 끌어오지 않고 이동 없이 최대 1초까지만 정했다. 속박 중 공격과 회전을 멈추는지는 없다)
- 같은 시각 보스 사망과 전원 사망의 결과(기획의 초기 기준은 전멸 우선이며 실제 전투 피드백 뒤 조정)
- 등장 컷씬의 시작 조건과 길이, 길이 데이터의 위치
