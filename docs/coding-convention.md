# shinshi — 코딩 컨벤션

> 기준: C# / Unity 6000.4 (Unity 6) / URP / Mirror
> 팀이 작성하는 모든 C# 코드는 이 문서를 따른다. 다른 문서는 스타일 규칙을 복제하지 않고 이 문서를 링크한다.
> 네임스페이스는 파트(도메인) 기준으로 나눈다. 팀원 개인 이름이 아니라 담당 시스템을 따른다.

저장소 루트가 곧 Unity 프로젝트 루트다. 이 문서의 `Assets/...` 경로는 저장소 루트 기준이다.

**이 문서를 강제하는 파일**
- `.editorconfig` — 중괄호 위치(Allman), 멤버 변수/상수/인터페이스 명명 규칙을 IDE 경고로 표시한다. VS/Rider 모두 자동 인식.
- `Assets/ScriptTemplates/1-Scripting__MonoBehaviour Script-NewMonoBehaviourScript.cs.txt` — Unity `Create > MonoBehaviour Script`의 기본 템플릿을 9장 파일 구조로 덮어쓴다.
- `Assets/ScriptTemplates/2-Scripting__ScriptableObject Script-NewScriptableObjectScript.cs.txt` — `Create > ScriptableObject Script` 템플릿을 16장 GameDB `*SO` 규칙(메뉴 경로 `Shinshi/...`)에 맞춘다. 클래스 이름의 `SO` 접미사는 만들 때 직접 붙인다.
- `Assets/Editor/ScriptTemplateNamespaceProcessor.cs` — 템플릿의 `#NAMESPACE#`를 만든 위치에 맞는 1장 네임스페이스로 바꾼다 (`01.Scripts/{NN}.{도메인}/` -> `Shinshi.{도메인}`, `00.Common/Data/` -> `Shinshi.Data`, `98.Servers/{NN}.{도메인}/` -> `Shinshi.{도메인}`, `Assets/Editor/` -> `Shinshi.Editor`, 그 밖 -> `Shinshi`).

모두 프로젝트 내장 파일이므로 각자 에디터를 새로 열면 바로 적용된다 (에디터별 전역 설정이 아니다). 스크립트 템플릿은 Unity를 다시 시작해야 메뉴에 반영된다.

---

## 0. 폴더 구조

```
Assets/
├ 00.Scenes/          씬
├ 01.Scripts/
│ ├ 00.Common/        공통 (Shinshi.Common) - Constants, Events, Interfaces, Pooling, Sound, Types, Utility
│ │ ├ Contracts/      Unity·백엔드 공용 엔진 비의존 DTO (Shinshi.Contracts)
│ │ └ Data/           GameDB ScriptableObject 정의 (Shinshi.Data) - 16장
│ ├ 01.Network/       세션, 동기화 (Shinshi.Network) - Lobby, Session, Town(마을 요청 메시지·클라이언트 송신 창구)
│ ├ 02.Combat/        플레이어 전투 (Shinshi.Combat)
│ │ ├ Player/         입력·이동·생사 상태와 공용 Rules, 클라이언트 View
│ │ ├ Damage/         피해 계약·공용 Rules
│ │ └ Skill/          기술 입력 계약·동료 동기화 상태
│ ├ 03.AI/            산귀·보스 AI, 웨이브 스폰 (Shinshi.AI) - Enemy, Boss, Spawn
│ ├ 04.Systems/       게임플레이 시스템 (Shinshi.Systems) - Wave, Outpost, Reward, BossTicket, Essence,
│ │                   Talisman, Equipment, Inventory, Growth, FoxTrace, Interaction,
│ │                   Life, Crafting, Auction
│ ├ 05.Art/           표현 (Shinshi.Art) - Camera, Environment, Telegraph, VFX
│ │                   + 팀 제작 모델링 에셋 - Characters, Environment, Props, Shaders, UI
│ │                     (규칙: docs/unity/SCENE_AND_ASSET_SAFETY.md 4장)
│ ├ 06.UI/            UI (Shinshi.UI) - Combat, Hub, Hud, Lobby, Systems
│ ├ 98.Servers/       서버 전용 코드 (Shinshi.Server 어셈블리) - 9장
│ │ ├ 01.Network/     서버 프로세스·접속·백엔드 연결
│ │ │ ├ Bootstrap/    부트스트랩·실행 설정·프로세스 제어·종료
│ │ │ ├ Presence/     서버 풀 등록·존재 신호·공개 주소 검사
│ │ │ ├ Backend/      백엔드 게이트웨이·HTTP 전송·차단기·서버 자격
│ │ │ ├ Auth/         입장 토큰 검증·인증 대기·참가 명단·계정 임대 해제
│ │ │ └ Session/      출정 진행(DungeonSession·명단)
│ │ │   ├ Events/     이벤트 저장 큐·백엔드 저장 경로
│ │ │   ├ Settlement/ 정산 요청·원장·규칙
│ │ │   └ Reward/     보스 보상·적 드롭 판정
│ │ ├ 02.Combat/      서버 전투 틱·Player/·Damage/·Skill/·Boss/
│ │ ├ 03.AI/          Boss/(보스 두뇌·패턴), Enemy/(근접·돌진 산귀 두뇌 프로토타입), Spawn/(스폰 경계 계약)
│ │ ├ 04.Systems/Wave/ 웨이브 경계 계약 (실행 구현 예정)
│ │ └ 05.Town/        마을 역할 서버 - Party/(방·초대·준비·출정 대기열), Growth/(성장 API 중계), Life/(채집·물약 제작)
│ └ 99.Dev/           개발·디버그 도구 (Shinshi.Dev 어셈블리) - 출시 빌드 제외 대상
├ 09.Data/            GameDB ScriptableObject 에셋 - 16장
├ Editor/             에디터 전용 코드 (Shinshi.Editor)
├ Resources/          프로젝트 소유 Resources 로드 대상 (필요할 때 생성)
├ ScriptTemplates/    스크립트 템플릿
├ 99.ThirdParty/      외부 에셋 (Animation/, Tools/, VFX/, Model/) - docs/licenses/ASSET_REGISTRY.md
└ Tests/
  ├ EditMode/         로직 테스트 (Unity Test Framework)
  └ PlayMode/         런타임 동작 테스트
```

Unity 프로젝트 밖의 도구도 저장소 루트에 있다.

```text
Backend/              ASP.NET Core API·PostgreSQL 데이터 계층·SQL migration·테스트
deploy/               API 컨테이너·게임 서버 systemd 배포 템플릿과 안내
tools/                Start-M2.ps1 등 실행 보조 스크립트
```

전투 구현 경로는 `02.Combat/Player/`·`Damage/`·`Skill/`이고, 서버의 보스 몸체는 `98.Servers/02.Combat/Boss/`, 보스 두뇌와 패턴은 `98.Servers/03.AI/Boss/`다. 일반 적(산귀)은 근접·돌진 두뇌 프로토타입만 `98.Servers/03.AI/Enemy/`에 있고, 원거리·정예 산귀와 웨이브 실행 클래스는 아직 없다. 배포 템플릿과 실행 보조 스크립트의 존재는 서버 빌드·M2·배포 실행 성공을 뜻하지 않는다.

외부 Asset Store 에셋은 `99.ThirdParty/{분류}/{에셋}/`에 둔다. Unity 에디터에서 `.meta`와 GUID를 유지하며 옮기고, 내부의 `Editor/`와 `Resources/` 구조를 보존한다. `Plugins/TextMesh Pro/` 필수 리소스와 `Gizmos/`의 위치 예외, 설치 버전과 승인된 경로 보정은 [에셋 대장](licenses/ASSET_REGISTRY.md)을 따른다. 업데이트 후에는 중복 패키지와 대장의 경로 보정을 다시 확인한다. 어떤 플러그인을 무엇에 쓰는지는 23장.


- 도메인 폴더 번호와 목록은 이 표가 기준이다. 표에 없는 최상위 폴더를 만들 때는 먼저 이 표를 고친다. 하위 기능 폴더는 기획서(웨이브·토벌 거점·보상·보스 입장권·정기·부적·여우의 흔적)에 맞춘 초기안이며, 파트와 기획이 확정되면 조정한다.
- 팀 코드는 아래 실제 어셈블리(asmdef)로 나뉜다. 폴더에 asmdef를 두면 그 아래 전체가 해당 어셈블리가 되고, 하위 폴더에 다른 asmdef가 있으면 그 폴더는 빠진다. 실제 참조 목록은 asmdef 파일이 기준이다.

| 어셈블리 | asmdef 위치 | 포함 | 참조 | 빌드 포함 조건 |
|---|---|---|---|---|
| `Shinshi.Runtime` | `01.Scripts/` | `00.Common/`~`06.UI/` (`Contracts/` 제외) | `Shinshi.Contracts`, `Mirror`, `Mirror.Components`, `Mirror.Transports`, `kcp2k`, `Unity.Cinemachine`, `Unity.AI.Navigation`, `Unity.TextMeshPro`, `DOTween.Modules`, `DOTweenPro.Scripts`, `PixelCrushers`, `DialogueSystem`, `MeshBakerCore` | 항상 |
| `Shinshi.Contracts` | `01.Scripts/00.Common/Contracts/` | Unity와 백엔드가 함께 쓰는 엔진 비의존 API DTO (`noEngineReferences`). `Shinshi.Runtime`에서 빠진다 | 없음. `Shinshi.Runtime`, `Shinshi.Server`, 테스트가 참조한다 | 항상 |
| `Shinshi.Server` | `01.Scripts/98.Servers/` | `98.Servers/` | `Shinshi.Runtime`, `Shinshi.Contracts`, `Mirror`, `Mirror.Transports`, `kcp2k` | define 제약 `UNITY_SERVER \|\| UNITY_EDITOR` |
| `Shinshi.Dev` | `01.Scripts/99.Dev/` | `99.Dev/` | `Shinshi.Runtime` | 항상 컴파일하되 파일 안을 `#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD`로 비운다 |
| `Shinshi.Tests.EditMode` · `Shinshi.Tests.PlayMode` | `Tests/EditMode/` · `Tests/PlayMode/` | 테스트 | `Shinshi.Runtime`, `Shinshi.Server`, `Shinshi.Contracts`, `Mirror`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner` | 테스트 전용 (`UNITY_INCLUDE_TESTS`), EditMode는 Editor만 포함 |

현재 참조의 원본: [Runtime asmdef](../Assets/01.Scripts/Shinshi.Runtime.asmdef), [Contracts asmdef](../Assets/01.Scripts/00.Common/Contracts/Shinshi.Contracts.asmdef), [Server asmdef](../Assets/01.Scripts/98.Servers/Shinshi.Server.asmdef), [Dev asmdef](../Assets/01.Scripts/99.Dev/Shinshi.Dev.asmdef). 백엔드는 [Shinshi.Contracts.csproj](../Backend/src/Shinshi.Contracts/Shinshi.Contracts.csproj)에서 `Assets/01.Scripts/00.Common/Contracts/`의 DTO 소스를 링크한다. 같은 계약을 백엔드 폴더에 복제하지 않는다.

  - 에디터에서는 `UNITY_EDITOR`가 정의되어 서버 코드도 컴파일되므로 에디터 한 개로 서버·클라를 함께 실행해 시험할 수 있다. Dedicated Server 빌드 대상은 `UNITY_SERVER`를 정의한다.
  - 로그·기즈모 호출 API처럼 도메인 코드가 부르는 개발 기능은 `Shinshi.Runtime`(`00.Common/`)에 `[Conditional]`로 두고, `Shinshi.Dev`에는 패널·웹 서버처럼 도메인이 부르지 않는 것만 둔다. `Shinshi.Runtime`은 `Shinshi.Dev`를 참조하지 않는다.
  - asmdef는 `Assembly-CSharp`·`Assembly-CSharp-firstpass`를 참조할 수 없다. 팀 코드가 쓰는 외부 에셋은 asmdef가 있어야 한다. DOTween은 `DOTween.Modules`·`DOTweenPro.Scripts` asmdef가 있다. Dialogue System은 동봉된 `CommonAssemblyDefinitions.unitypackage`와 `DialogueSystemAssemblyDefinitions.unitypackage`를 가져와야 한다. 두 유료 에셋은 Git에서 제외되므로 설치할 때마다 이 단계를 반복한다 ([에셋 대장](licenses/ASSET_REGISTRY.md)).
  - `Assets/Editor/`의 에디터 코드는 기본 에디터 어셈블리에 둔다. `Shinshi.Runtime`은 자동 참조(`autoReferenced`)를 켜 두어 에디터 코드가 읽을 수 있게 한다.
- 비어 있는 폴더는 Git에 남도록 `.gitkeep`을 둔다. 첫 스크립트를 넣을 때 `.gitkeep`을 지운다.
- 파일 이름은 주 클래스 이름과 같게 한다 (`PlayerController.cs`).
- 에디터 전용 코드는 런타임 코드와 섞지 않는다. `Editor/` 또는 `#if UNITY_EDITOR` 블록에 둔다.
- 테스트 파일은 `<Type>Tests.cs`, 테스트 메서드는 `<Method>_<Condition>_<ExpectedResult>`로 이름 짓는다.

---

## 1. 네임스페이스 (파트 기준)

루트 네임스페이스는 `Shinshi`, 그 아래 기능 도메인으로 나눈다. `Assets/01.Scripts/` 하위 폴더 구조(0장)를 따르며, 구현 소유는 파트 구분을 따른다(`Net/` 상태 클래스와 `98.Servers/`는 {팀장}, `98.Servers/`의 AI·웨이브는 해당 파트 - [서버·클라이언트 협업](unity/SERVER_CLIENT_WORKFLOW.md)). 하위 폴더를 만들어도 기존 도메인 네임스페이스는 유지한다.

```csharp
namespace Shinshi.Common       // 공통 유틸리티, Constants
namespace Shinshi.Data         // ScriptableObject 정의 (GameDB)
namespace Shinshi.Contracts    // Unity와 백엔드가 함께 쓰는 API DTO, UnityEngine 참조 금지
namespace Shinshi.Network      // 세션, 동기화
namespace Shinshi.Combat       // 플레이어, 피해 판정, 기술
namespace Shinshi.AI           // 산귀, 보스, 웨이브 스폰 (AI·패턴 판정 포함)
namespace Shinshi.Systems      // 웨이브, 거점, 보상, 입장권, 정기, 부적, 장비, 성장, 여우의 흔적
namespace Shinshi.Art          // 카메라, 환경, 예고, VFX
namespace Shinshi.UI           // UI
namespace Shinshi.Dev          // 개발·디버그 도구
namespace Shinshi.Editor       // 에디터 전용
// 도메인과 일치시킨다. 98.Servers/{NN}.{도메인}/ 아래 서버 클래스도 원래 도메인 namespace를 쓴다 (9장).
```

---

## 2. 명명 규칙

| 대상 | 규칙 | 예시 |
|---|---|---|
| 클래스 / 구조체 | PascalCase | `PlayerController`, `EnemySpawner` |
| 인터페이스 | I + PascalCase | `IDamageable`, `IInteractable` |
| public 메서드 | PascalCase | `TakeDamage()`, `SpawnEnemy()` |
| private 메서드 | PascalCase | `CheckGrounded()`, `ApplyVisual()` |
| 로컬 변수 | camelCase | `hitPoint`, `frameCount` |
| 멤버 변수 | 언더스코어 prefix + camelCase | `_health`, `_moveSpeed` |
| 상수 | UPPER_SNAKE_CASE | `MAX_PARTY_SIZE`, `BASE_DAMAGE` |
| 정적 배열 | G_ prefix | `G_SpawnTable` |
| SerializeField | 멤버 변수와 동일 | `_jumpForce` |
| enum 타입 | E 접두사 + PascalCase | `EItemGrade`, `EPlayerState` |
| enum 값 | PascalCase (E 접두사 없음) | `EItemGrade.Epic`, `EPlayerState.Idle` |
| ScriptableObject 클래스 | PascalCase + **SO 접미사** — 16장 GameDB 대상 표를 따른다 | `ItemSO`, `EnemySO`, `GameDatabaseSO` |
| ScriptableObject 캐시 변수 | 멤버 변수 규칙과 동일 (`_camelCase`), 접미사는 붙이지 않는다 | `_item`, `_enemy` |

모든 팀 소유 enum 타입 이름 앞에 `E`를 붙인다. 중첩 enum과 `[Flags]` enum에도 같은 규칙을 적용하며, enum 값 이름에는 접두사를 붙이지 않는다.

```csharp
public enum EItemGrade
{
    Common,
    Rare,
    Epic
}
```

---

## 3. 매직넘버 절대 사용 금지

```csharp
// ❌ 절대 금지
if (_health > 100) { }
_rb.linearVelocity = new Vector2(5f, 9f);
for (int i = 0; i < 3; ++i) { }

// ✅ 반드시 이렇게
private const float MAX_HEALTH      = 100f;
private const float MOVE_SPEED      = 5f;
private const float JUMP_FORCE      = 9f;
private const int   MAX_PARTY_SIZE  = 3;

if (_health > MAX_HEALTH) { }
_rb.linearVelocity = new Vector2(MOVE_SPEED, JUMP_FORCE);
for (int i = 0; i < MAX_PARTY_SIZE; ++i) { }
```

**Inspector에서 조정할 값은 `[SerializeField]`로, 코드 내부 고정값은 `const`로.**

```csharp
// Inspector 조정용 → SerializeField
[SerializeField] private float _moveSpeed = 5f;

// 코드 내부 고정값 → const
private const float GROUND_CHECK_EXTRA = 0.05f;
private const float ATTACK_FLASH_TIME  = 0.1f;
```

밸런스 수치(체력, 피해량, 쿨다운 등)는 여기 대상이 아니다 — 코드 상수로 박지 말고 [밸런스 데이터 작업](unity/BALANCE_DATA_WORKFLOW.md) 절차를 따라 GameDB로 뺀다.

---

## 4. 중괄호 스타일 (Allman / BSD)

여는 중괄호는 **항상 다음 줄**에 온다.
**모든 제어문(if/for/while/foreach)은 중괄호 필수 — 단일 문장이라도 예외 없음.**

```csharp
// ✅
private void Move()
{
    if (_grounded)
    {
        _rb.linearVelocity = new Vector2(_moveSpeed, 0f);
    }
}

// ❌ 중괄호 생략 금지 (단일 문장이어도)
if (_isDead) return;
for (int i = 0; i < n; ++i) DoThing(i);

// ✅ 이렇게
if (_isDead)
{
    return;
}
```

**한 줄짜리 함수는 예외적으로 한 줄 허용:**

```csharp
public void Reset() { _health = MAX_HEALTH; }
public bool IsAlive() { return _health > 0f; }
```

---

## 5. 증감 연산자

**전위 증가만 사용 (`++i`, `--i`)**

```csharp
// ✅
for (int i = 0; i < MAX_PARTY_SIZE; ++i) { }
++_frameIndex;
--_health;

// ❌
i++;
_health--;
```

---

## 6. 조건문 괄호

**복합 조건은 각 조건을 괄호로 묶는다.**

```csharp
// ✅
if ((a > b) && (c > d)) { }
if ((_health <= 0f) || (_isDead)) { }

// ❌
if (a > b && c > d) { }
```

---

## 7. 필드 정렬

**`=` 기준 정렬. 가독성 최우선.**

```csharp
// ✅ const 정렬
private const float MAX_HEALTH      = 100f;
private const float MOVE_SPEED      = 5f;
private const float JUMP_FORCE      = 9f;
private const int   MAX_PARTY_SIZE  = 3;

// ✅ SerializeField 정렬
[SerializeField] private float _moveSpeed = 5f;
[SerializeField] private float _jumpForce = 9f;
[SerializeField] private int   _maxTargets = 3;

// ✅ 일반 멤버 정렬
private float _health   = 0f;
private bool  _isDead   = false;
private int   _frameIdx = 0;
```

---

## 8. using 그룹 순서

```csharp
// System 계열
using System;
using System.Collections;
using System.Collections.Generic;

// Unity 계열
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using DG.Tweening;
using Mirror;

// 프로젝트 내부 (파트 네임스페이스)
using Shinshi.Common;
```

---

## 9. 파일 구조 (배너 주석)

### 기본 구조

```csharp
// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Player
{
    public class ClassName : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MAX_HEALTH = 100f;

        [Header("이동")]
        [SerializeField] private float _moveSpeed = 5f;

        private float _health;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake() { }
        private void Start()  { }
        private void Update() { }

        /****************************************
        *                Methods
        ****************************************/

        public void TakeDamage(float damage) { }
        private void Die() { }
    }
}
```

### 네트워크 · 서버 · 표현 분리 (`NetworkBehaviour`)

> 한 클래스에 서버 판정, 복제 상태, 표현을 함께 두면 클라이언트 빌드에 서버 규칙이 실리고, 헤드리스 서버에 표현 코드가 실리며, 규칙을 `NetworkBehaviour` 없이 테스트할 수 없다.
> 그래서 개체 하나를 실행 위치별 클래스로 나누고, 서버에서만 쓰는 코드는 어셈블리 단위로 클라이언트 빌드에서 뺀다. partial로 한 클래스를 여러 파일에 나누는 방식은 쓰지 않는다.

**네 층으로 나눈다.** 어셈블리 구성은 0장 표를 따른다.

| 층 | 위치 | 어셈블리 | 담는 것 | 작성 |
|---|---|---|---|---|
| 규칙 | `{NN}.{도메인}/{기능}/Rules/{이름}Rules.cs` | `Shinshi.Runtime` | 서버와 클라가 **둘 다** 알아야 하는 순수 계산. 비용·쿨다운·미리보기·예고 형태·입력 사전 검사. `MonoBehaviour`·Mirror 타입을 쓰지 않는다 | 파트 담당 |
| 상태 | `{NN}.{도메인}/{기능}/Net/{이름}State.cs` | `Shinshi.Runtime` | `NetworkBehaviour`. 동기화 필드, `[Command]`, Rpc, hook을 C# 이벤트로 바꾸는 코드, `[Server]` 상태 변경 메서드. **판정을 두지 않는다** | {팀장} |
| 서버 | `98.Servers/{NN}.{도메인}/{기능}/{이름}Server.cs` 등 | `Shinshi.Server` | 서버만 필요한 판단과 실행. 요청 검증 처리기, AI 결정·상태 머신, 스폰, 보상 추첨, 정산, 백엔드 호출 | {팀장}. AI·웨이브는 해당 파트 담당 |
| 표현 | `{NN}.{도메인}/{기능}/View/{이름}View.cs`, UI는 `{NN}.UI/` | `Shinshi.Runtime` | 상태를 읽어 애니메이션·VFX·UI로 보여 준다 | 파트 담당 |

서버만 쓰는 순수 판정 클래스(엔진·네트워크 상태를 갖지 않는 정적 계산)는 `98.Servers/` 안에서 `{이름}Rules`라는 이름을 써도 된다. 예: `JoinHandshakeRules`, `SettlementRules`. 위치가 `98.Servers/`이므로 클라이언트 빌드에 들어가지 않으며, 위 표의 규칙 층(Runtime 공용)과는 위치로 구분한다.

```
Assets/01.Scripts/
├ 03.AI/Enemy/Rules/EnemyTelegraphRules.cs       예고 형태 계산 (서버·클라 공용)
├ 03.AI/Enemy/Net/EnemyState.cs                  동기화 상태·Rpc
├ 03.AI/Enemy/View/EnemyView.cs                  애니메이션·VFX
└ 98.Servers/03.AI/Enemy/EnemyBrain.cs           타깃 선택·상태 머신 (서버 전용)
```

**규칙과 서버 중 어디에 둘지는 한 가지로 판단한다.** "클라이언트가 이 계산을 알아야 화면을 그릴 수 있는가?" 그렇다면 규칙, 아니라면 서버다. AI 결정, 보상 추첨, 정산, 서버 자격 증명을 쓰는 백엔드 호출은 항상 서버다.

**의존 방향.** `Shinshi.Server`는 `Shinshi.Runtime`을 참조하고, 반대 방향은 금지한다. 상태 클래스는 서버 클래스를 모른다.

- 서버 클래스는 프리팹·씬에 붙이지 않는다. 클라이언트 빌드에는 그 타입이 없어 프리팹에 빈 스크립트 참조가 남는다. 서버 부트스트랩(`98.Servers/01.Network/`)이 역할(`town`/`dungeon`)에 맞춰 만들고 상태에 연결한다.
- `NetworkBehaviour`는 전부 `Shinshi.Runtime`에 둔다. Mirror는 프리팹의 `NetworkBehaviour` 순번으로 직렬화하므로 서버와 클라 빌드의 구성이 같아야 한다. `[Command]`·Rpc도 양쪽 빌드에 있어야 한다.
- 연결 방법: 상태의 `OnStartServer`·`OnStopServer`가 [ServerLinks](../Assets/01.Scripts/01.Network/ServerLinks.cs)에 알린다. 현재 [ServerBootstrap](../Assets/01.Scripts/98.Servers/01.Network/Bootstrap/ServerBootstrap.cs)이 서버를 열기 전에 [CombatServerInstaller](../Assets/01.Scripts/98.Servers/02.Combat/CombatServerInstaller.cs)를 부르고, 설치기가 `PlayerInputState`의 시작·종료 처리기를 정확한 타입으로 등록한다. 알림을 받은 `CombatServerRuntime`이 `PlayerServer`를 생성·정리한다. 중복 타입 등록은 거절되며 플레이 시작 시 정적 등록을 비운다.
- `[Command]`는 검증 없이 상태가 가진 처리기 인터페이스(`I{이름}ServerHandler`, 상태와 같은 `Net/`)로 넘긴다. 호출자·소유권·순번·중복 검증은 서버 처리기가 한다.
- 전용 서버에서는 SyncVar hook이 불리지 않는다. 서버 로직을 hook에 걸지 않는다.
- 표현은 [ClientView](../Assets/01.Scripts/00.Common/Utility/ClientView.cs)를 상속하고 서버 빌드(`UNITY_SERVER`)에서 스스로 꺼진다. 표현과 UI는 상태의 읽기 프로퍼티·이벤트와 공개 요청 창구만 쓰며, `[Server]` 메서드나 서버 클래스를 호출하지 않는다.

아래 코드는 역할 분리를 설명하는 예시다. 현재 전투 요청 창구는 `02.Combat/Player/Net/PlayerInputState.cs`이며, 실제 타입·연결 흐름은 [서버·클라이언트 협업](unity/SERVER_CLIENT_WORKFLOW.md) 6·9장을 따른다. 서버 틱·인증·게이트웨이의 `MonoBehaviour` 드라이버도 서버 부트스트랩이 실행 중에 생성·부착하며 씬·프리팹에는 직렬화하지 않는다.

```csharp
// 02.Combat/Skill/Net/PlayerSkillState.cs - Shinshi.Runtime 예시
public class PlayerSkillState : NetworkBehaviour
{
    /****************************************
    *                Fields
    ****************************************/

    [SyncVar(hook = nameof(OnStaminaChanged))]
    private int _stamina = 0;

    /****************************************
    *              Properties
    ****************************************/

    public int Stamina => _stamina;

    // 서버 부트스트랩이 OnStartServer 알림을 받고 넣는다. 클라 빌드에는 구현 타입이 없다
    public IPlayerSkillServerHandler ServerHandler { private get; set; } = null;

    public event Action<int> StaminaChanged;

    /****************************************
    *            Server Callbacks
    ****************************************/

    public override void OnStartServer()
    {
        ServerLinks.NotifyStarted(this);
    }

    public override void OnStopServer()
    {
        ServerLinks.NotifyStopped(this);
    }

    /****************************************
    *               Commands
    ****************************************/

    [Command]
    private void CmdCast(SkillRequest request)
    {
        ServerHandler?.HandleCast(this, request);
    }

    /****************************************
    *                Server
    ****************************************/

    [Server]
    public void SetStamina(int value)
    {
        _stamina = value;
    }

    /****************************************
    *              SyncVar Hooks
    ****************************************/

    private void OnStaminaChanged(int previous, int next)
    {
        StaminaChanged?.Invoke(next);
    }
}

// 98.Servers/02.Combat/Skill/PlayerSkillServer.cs - Shinshi.Server 예시
public class PlayerSkillServer : IPlayerSkillServerHandler
{
    public void HandleCast(PlayerSkillState state, SkillRequest request)
    {
        // 헤더, 소유권, 순번 검증 -> SkillRules로 비용 계산 -> 수락하면 state.SetStamina
    }
}
```

- 상태 클래스 배너 순서: `Fields` → `Properties` → `Server Callbacks` → `Client Callbacks` → `Commands` → `Server` → `ClientRpc` → `TargetRpc` → `SyncVar Hooks` → `Serialization`. 빈 섹션은 넣지 않는다.
- 새 동기화 필드·`[Command]`·Rpc나 처리기 인터페이스 변경은 파트 담당이 {팀장}에게 요청한다. 상태 클래스는 `Net/`에 있어 {팀장} 리뷰가 걸린다.
- 이름: 상태 `{이름}State`, 서버 `{이름}Server`(요청 처리)·`{이름}Brain`(AI)·`{이름}Director`(진행), 표현 `{이름}View`, 규칙 `{이름}Rules`. 도메인 네임스페이스는 층과 무관하게 1장을 따른다.
- 기존 컴포넌트를 층으로 나눌 때 원본 `.cs.meta`의 GUID는 프리팹·씬 참조를 붙들고 있으므로 프리팹에 남는 쪽(상태 또는 표현)이 원본 파일을 이어받는다.
- 이 구조는 어셈블리로 **컴파일 경계**를 만든다. `Shinshi.Runtime` 코드에서 서버 클래스를 부르면 컴파일이 실패한다. 반대로 상태의 `[Server]` 메서드는 같은 어셈블리의 표현·UI가 부를 수 있으므로 리뷰로 막는다.

## 10. 참조 타입 매개변수

```csharp
// null 가능성 없으면 참조
private void SetData(ItemData data) { }

// null 가능성 있으면 nullable 명시
private void SetTarget(Transform target) { }

// 값 자체 수정이 필요하면 ref
private void ModifySnapshot(ref Snapshot snap) { }
```

---

## 11. switch 들여쓰기

```csharp
switch (_state)
{
    case EState.Idle:
        HandleIdle();
        break;
    case EState.Attack:
        HandleAttack();
        break;
    default:
        break;
}
```

---

## 12. 주석

**꼭 필요한 곳에만, 무엇을 위해 만든 것인지만 적는다. 코드를 읽어서 알 수 있는 내용이면 주석을 달지 않는다.**

아래 예시의 좋음/나쁨 표시는 이 문서의 표기일 뿐이다. 실제 주석에는 쓰지 않는다.

좋음

```csharp
_rb.isKinematic = true; // 넉백 중 물리 끄기
```

나쁨 (코드가 말하는 것을 반복)

```csharp
_health -= damage; // 체력 감소
```

### 짧게 쓰되, "왜"는 길어도 된다

**"무엇을 한다"는 한 줄을 넘기지 않는다** (위 예시). 그 이상 길어지면 주석을 늘리지 말고 이름을 고치거나 메서드를 쪼갠다.

**"왜 이렇게 했는가"는 길어도 된다.** 특히 아래 넷은 여러 줄을 써서라도 남긴다. 코드를 읽어서 복원할 수 없고, 모르면 다음 사람이 되돌려 놓기 때문이다.

| 남길 것 | 예 |
|---|---|
| **되돌리면 깨지는 이유** | 리그가 `LateUpdate`인 이유, 위치를 상태보다 먼저 옮기는 이유 |
| **다른 안을 버린 이유** | 로케이터 대신 값 하나를 쓴 이유, 별도 클래스를 만들지 않은 이유 |
| **실측으로 뒤집힌 전제** | 레이어 이름이 물리 크기가 아니었다, 마스크가 0이라 조기 반환하고 있었다 |
| **경계값의 근거** | 12도가 탐색 반각 35도와 다른 이유 |

```csharp
// 이 프레임의 이동이 전부 끝난 뒤에 붙는다. 원격은 NetworkTransform이,
// 자기 캐릭터는 PlayerPrediction이 Update에서 위치를 옮기므로
// 같은 Update에서 돌면 실행 순서에 따라 한 프레임 뒤처져 떨림으로 보인다
private void LateUpdate()
```

**반대로, 코드가 이미 말하는 것을 여러 줄로 늘어놓지 않는다.** 길이의 근거는 언제나 *"이걸 모르면 잘못 고친다"*이지 친절함이 아니다.

한 줄 주석은 마침표 없이 짧은 구절로 쓰고, 여러 줄 설명은 문장으로 쓴다.

### 문서 위치를 적지 않는다

기획서 절 번호, 이 문서의 장 번호, 다른 파일 경로를 주석에 적지 않는다.
문서가 개정되면 번호가 어긋나는데 주석은 따라 고쳐지지 않아, 남는 것은 틀린 안내뿐이다.
근거가 필요하면 번호 대신 이유 자체를 한 줄로 적는다.

나쁨

```csharp
// 웨이브 단계별 강화 배수 (게임 디자인 5.2)
// 판정 수식 분리 원칙에 따른 순수 C# 레이어, 기술 설계 1.2 참조
// 코딩 컨벤션 22장 예외
```

좋음

```csharp
// 스폰 시점 값으로 확정, 교전 중 최대 HP가 변하면 체력바가 어긋난다
// Mirror에 의존하지 않아야 데디케이티드로 옮길 때 그대로 쓴다
// 씬 배치본을 붙잡는 1회 폴백, 결과는 캐싱된다
```

### 특수 기호를 쓰지 않는다

**키보드로 바로 치는 ASCII만 쓴다.** 별표, 이모지, 장식 문자는 물론이고 화살표(`→`), 엠대시(`—`), 가운뎃점(`·`), 수학 기호(`≠`)처럼 별도 입력기가 필요한 활자 기호도 쓰지 않는다.
사람마다 입력 방법이 달라 다시 타이핑하기 어렵고, 편집기와 터미널과 diff에서 폭이 제각각으로 깨진다.

적용 범위는 주석뿐 아니라 **코드 안의 모든 한국어 문자열**이다. `Debug.Log` 메시지, `[Header]`, `[Tooltip]`, 예외 메시지, 화면 오버레이 텍스트가 모두 포함된다.

| 쓰지 않는다 | 대신 쓴다 | 예시 |
|---|---|---|
| `→` `⇒` `=>` | `->` | `StopHost -> StartHost` |
| `—` `–` (대시류) | `-` | `빌드 성공 - 12.3초` |
| `·` (가운뎃점) | `,` | `판정 창, 피해량` |
| `≠` `≤` `≥` | `!=` `<=` `>=` | `{a} != {b}` |
| `★` `✅` `❌` 이모지 | 쓰지 않는다 | 문장으로 쓴다 |
| `─` `《》` 장식, 괄호 | `-` `"` | `[ 무기 보정 ]`, `"상점 목록"` |
| 한자 (`白` `靑` `金`) | 한글 | `일반, 흰색` |

**XML 문서 주석의 `<b>` 강조 태그도 쓰지 않는다.** ASCII이긴 하지만 하는 일은 장식과 같고, 강조가 늘어날수록 정작 중요한 줄이 묻힌다. 중요하면 문장을 앞으로 옮기거나 줄을 나눈다. `<see cref="..."/>`, `<c>...</c>` 등도 아래 XML 문서 주석 규칙에 따라 쓰지 않는다. XML 문서 태그는 `<summary>`만 남긴다.

```csharp
// 나쁨
// ★권위 판정 레이어
// 서버 판정 → 클라 표현
// 이동 · 구르기 · 공격 순서로 처리
// ✅ 반드시 캐싱할 것

// 좋음
// 권위 판정 레이어
// 서버가 판정하고 클라는 표현만 한다
// 이동, 구르기, 공격 순서로 처리
```

### 적용 범위와 점검

이 규칙은 팀이 소유한 소스에 적용한다. Mirror·유료/무료 외부 에셋·공급된 스킬 번들·생성 코드는 일괄 주석 정리에서 제외한다. 소스의 저작권·라이선스 고지와 생성기/린터/컴파일러 지시문은 보존한다.

주석 누락 여부는 줄 수가 아니라 외부 계약과 숨은 제약을 기준으로 판단한다. 이름만으로 명확한 접근자·상속 구현에 같은 설명을 반복하지 않는다. 반대로 서버 권위·세션 수명·재시도/중복 방지·스냅샷 확정 시점처럼 잘못 쓰기 쉬운 경계는 짧게 설명한다.

C#은 공개 계약에 summary, 내부 이유에 일반 주석을 쓴다. Python 공개 계약은 docstring, 스크립트는 해당 언어의 일반 주석을 사용하며 C# XML 문법을 복사하지 않는다. Python docstring은 런타임 메타데이터이므로 일반 주석과 구분해 검증한다.

주석만 바꾸는 작업에서는 문자열·정규식·URL·셰이더/웹 코드가 포함된 문자열을 주석으로 오인하지 않는다. 실행 토큰, 전처리 지시문, 직렬화 필드와 기존 작업 변경을 보존하고 수정 전후 비교로 확인한다. 화면 문구·로그 문자열 정리는 별도 변경으로 구분한다.

### XML 문서 주석

**쓰는 것은 `<summary>` 하나뿐이다.**

> `<remarks>`, `<see cref>`, `<c>`, `<inheritdoc/>`, `<param>`, `<returns>`는 쓰지 않는다. 태그를 늘릴수록 **어디에 무엇을 적어야 하는지가 사람마다 갈리고**, 주석을 읽는 데 태그를 걸러내는 품이 든다. 판단 근거가 길면 일반 주석으로 쓰면 된다.

| 무엇 | 어떻게 |
|---|---|
| public API 설명 | **`/// <summary>`** |
| 그 외 전부 (내부 로직, private 멤버, 판단 근거) | **일반 주석 `//`** |
| 남은 작업 | **`// TODO(이름):`** |

**다른 타입이나 멤버를 가리킬 때는 그냥 이름을 쓴다.** `<see cref="PlayerStatus"/>`가 아니라 `PlayerStatus`다. IDE가 링크를 못 만들지만, 이름만 있어도 검색으로 찾아간다.

**인터페이스 구현에는 `<inheritdoc/>`를 붙이지 않는다.** 계약 쪽에 설명이 있으면 구현 쪽은 비워 두고, 구현에만 있는 사정이 있으면 그것만 일반 주석으로 적는다.

```csharp
// 나쁨
/// <inheritdoc/>
/// <remarks>순서를 바꾸면 거부된 요청이 스태미나를 먹는다</remarks>
/// <see cref="PlayerActionGate"/>가 <c>false</c>를 돌려주면 여기서 멈춘다
public bool TrySpendResource(in PlayerActionContext context)

// 좋음
// PlayerActionGate를 통과한 뒤에만 부른다. 순서를 바꾸면 거부된 요청이 스태미나를 먹는다
public bool TrySpendResource(in PlayerActionContext context)
```

`<summary>`에도 위의 특수 기호 규칙이 그대로 적용된다.

```csharp
/// <summary> 피해를 입힌다, 체력이 0이 되면 사망 처리 </summary>
public void TakeDamage(float damage) { }

private void ApplyKnockback()
{
    _rb.isKinematic = true; // 넉백 중 물리 끄기
}
```

**남은 작업은 TODO 주석으로 적극 남긴다.** 담당자를 알 수 있게 이름을 붙인다.

```csharp
// TODO({팀원이름}): 서버 재접속 시 스냅샷 복구 처리
```

---

## 13. null 체크

```csharp
// Unity 오브젝트는 == null 사용 (?. / ?? 금지 - fake null 미탐지)
if (_renderer == null)
{
    return;
}

// 컴포넌트 취득 + null 체크는 TryGetComponent로
if (other.TryGetComponent(out PlayerHealth health))
{
    health.TakeDamage(damage);
}

// 일반 C# 객체(델리게이트/콜백)는 ?. 사용 가능
_callback?.Invoke();
onSuccess?.Invoke();
```

**검사를 어디에 쓰는지는 [21장 가드 절](#21-가드-절--null-체크와-예외-처리는-본문-맨-위에)을 따른다.**

---

## 14. SerializeField vs public

```csharp
// Inspector 노출은 SerializeField. public 필드는 사용하지 않는다.
// ✅
[SerializeField] private float _moveSpeed = 5f;

// ❌
public float moveSpeed = 5f;

// 외부에서 읽기만 필요하면 프로퍼티
public float MoveSpeed => _moveSpeed;
```

예외: 클래스 안에 숨긴 `private struct` 값 묶음(배열에 담아 `ref`로 제자리 수정하는 내부 기록)은 할당 없이 다루려고 필드를 `public`으로 둘 수 있다. 이름은 `PascalCase`로 통일하고, 그 struct를 클래스 밖으로 노출하지 않는다.

---

## 15. FixedUpdate 지양, 코루틴 지향

**물리 동기화가 반드시 필요한 경우가 아니면 `FixedUpdate` 사용을 지양하고, 코루틴(`IEnumerator` + `StartCoroutine`)으로 대체한다.**

```csharp
// ❌ 지양
private void FixedUpdate()
{
    _elapsed += Time.fixedDeltaTime;
    if (_elapsed >= _duration)
    {
        ApplyEffect();
    }
}

// ✅ 지향
private IEnumerator CoApplyEffect()
{
    yield return new WaitForSeconds(_duration);
    ApplyEffect();
}
```

- `Rigidbody`/`Rigidbody2D` 물리 연산(힘, 속도 적용 등)처럼 물리 스텝과 동기화가 필수인 경우에만 `FixedUpdate` 예외 허용.
- 타이머, 딜레이, 상태 전환 등 시간 기반 로직은 코루틴으로 작성한다.
- 값을 시간에 따라 보간하는 연출(이동, 페이드, 크기, 색)은 코루틴의 Lerp 루프가 아니라 DOTween으로 만든다 (23장).
- Tick 수명 관리와 캐싱 규칙은 [Unity 엔지니어링 패턴](unity/UNITY_ENGINEERING_PATTERNS.md)을 따른다.

---

## 16. 데이터 관리 (GameDB / Constants)

코드에 숫자를 직접 쓰지 않는 것은 동일하다. 값의 성격에 따라 두 곳으로 나눠 관리한다.

- **밸런스/기획 수치** (속도, 피해, 쿨다운, 패턴 간격, 연출 값 등) → **GameDB (ScriptableObject DB)**
- **코드 계약값** (입력 키/축, 태그/레이어/씬 이름, 판정 epsilon, enum 연동 개수, 배칭 상수) → **Constants**

### GameDB — ScriptableObject 기반 밸런스 DB

GameDB 루트(`GameDatabaseSO`, `GameDB`, `GameDBBootstrap`)와 아래 표의 SO 14개는 클래스와 에셋이 있다(EditMode 검증 테스트 통과, 플레이 모드와 서버 부트는 미검증). `AvatarCatalogSO`는 2026-10-01 UMA 아바타 연동과 함께 추가했고, 같은 날 UMA를 쓰지 않기로 하면서 UMA 적용 뷰(`AvatarView`)는 지우고 카탈로그는 남겼다. 2026-10-01에 넣은 도메인 SO 10개의 값은 기획 부록에서 옮긴 **초안**이며, 문서가 정하지 않은 값은 `TODO(담당자)`로 표시한 자리값이다. 값과 필드는 담당 파트가 조정한다. **SO 대상 목록은 아래 표가 기준이다** - 새 데이터 종류를 추가할 때는 이 표부터 갱신한다. 기술 설계 문서가 생기면 표를 그쪽으로 옮기고 여기서는 링크한다.

```
스크립트: Assets/01.Scripts/00.Common/Data/     (namespace Shinshi.Data)
에셋:     Assets/09.Data/Resources/GameDB.asset      <- 루트 GameDatabaseSO (Resources 아래 로드)
          Assets/09.Data/<도메인>/<이름>SO.asset       <- 도메인별 SO 에셋
```

| SO 클래스 | 에셋 경로 | 담는 값 | 담당 파트 |
|---|---|---|---|
| `GameDatabaseSO` | `09.Data/Resources/GameDB.asset` | 도메인 SO 참조 모음 (루트), 콘텐츠 버전 | {팀장} |
| `PlayerConfigSO` | `09.Data/Combat/PlayerConfig.asset` | 이동, 회피, 기력, 기본 능력치, 입력 보관, 직업별 기본 공격 | {팀장} |
| `SessionConfigSO` | `09.Data/Session/SessionConfig.asset` | 시작 인원별 배율, 부활, 재접속, 준비, 보스 물약 횟수, 파티 경매, 장애 취소 정산 정책 | {팀장} |
| `SkillCatalogSO` | `09.Data/Combat/SkillCatalog.asset` | 기술 12종, 강림 3종, 공명 규칙, 계약 동료 공통값 | {팀장} |
| `TalismanCatalogSO` | `09.Data/Combat/TalismanCatalog.asset` | 부적 효과 45종 정의와 변형 수치, 효과 레벨 보너스 배율 | {팀장} |
| `EnemyCatalogSO` | `09.Data/AI/EnemyCatalog.asset` | 일반 적 4역할의 체력, 공격, 제어, 예고 | 기웅 |
| `BossPatternCatalogSO` | `09.Data/AI/BossPatternCatalog.asset` | 보스 정의(등장 컷씬 길이 `AppearanceSeconds` 포함, 0이면 등장 대기 없이 시작), 월백 1페이즈 6패턴(`wolbaek_p01`~`06`)의 가중치, 문맥 배율, 예고와 회복, 제어 창. 수치는 0(미지정)으로 비워 두고 기웅이 지정한다. 미지정 보스는 `IsTuned`가 거짓이라 전투를 열지 않는다 | 기웅 |
| `WaveConfigSO` | `09.Data/AI/WaveConfig.asset` | 숲 6웨이브 배치, source ID, 활성 상한, 제한 시간 | 기웅 (세션 연결은 {팀장}) |
| `EquipmentCatalogSO` | `09.Data/Growth/EquipmentCatalog.asset` | 장비 6부위 4등급, 제작, 25강, 계승표 | 진욱 |
| `GrowthConfigSO` | `09.Data/Growth/GrowthConfig.asset` | 수련 5항목, 부적 등록 확률과 보정, 효과 강화 비용 | 진욱 |
| `RewardCatalogSO` | `09.Data/Reward/RewardCatalog.asset` | 재화와 재료 아이템, 개인 드롭, 웨이브 기본 보상, 추가 정기, 보스 보상 | 진욱 (정산 규칙 수치는 `SessionConfigSO`) |
| `LifeConfigSO` | `09.Data/Life/LifeConfig.asset` | 약초, 채집 선점과 재생, 행동력, 물약 레시피 | 진욱 |
| `ProgressionCatalogSO` | `09.Data/Progression/ProgressionCatalog.asset` | 지역, 보스전 해금, 여우의 흔적, 최초 지원, 체험판 플래그 | 기웅 |
| `AvatarCatalogSO` | `09.Data/Avatar/AvatarCatalog.asset` | 외형 슬라이더와 UMA DNA 대응, 색 팔레트, 직업별 고정 헤어, 장비 부위의 UMA 의상 슬롯, UMA 생성기 설정. 지금 값은 UMA 기본 종족 기준 임시값이다. UMA를 뺐으므로 이 값을 적용하는 코드가 없고, 새 아바타 방식이 정해지면 필드와 값을 다시 정한다 | {팀장} |
| `CameraConfigSO` | `09.Data/Combat/CameraConfig.asset` | 쿼터뷰 카메라 줌 단계별 시점(각도, 시야각, 거리, 초점 높이), 전환 시간, 방위, 따라가기 감쇠, 가림 투명화 범위. 로컬 표현 전용이며 서버 판정에 쓰지 않는다 | {팀장} |
| {이름}SO | `09.Data/{도메인}/{이름}.asset` | {값 설명} | {파트} |

- 도메인 SO는 `GameDataSO`를 상속하고 `ValidateValues`를 구현한다. 다른 카탈로그 ID를 참조하면 `ValidateReferences`에서 상대 카탈로그의 `Contains*` 메서드로 검사한다. 참조 검사는 모든 SO의 값 검사가 통과한 뒤에만 돈다.
- 카탈로그 항목은 `IDefinition`(`DefinitionId`, `DisplayKey`)을 구현하고 `DefinitionLookup<T>`로 찾는다. ID는 저장 데이터와 서버 판정이 참조하므로 표시 이름을 바꿔도 바꾸지 않는다.
- 한 SO는 한 파트가 소유한다. 클래스 파일은 `00.Common/Data/`에 있어도 담당 파트가 필드를 고치며, 리뷰 담당은 `.github/CODEOWNERS`에 파일 단위로 적는다.

```csharp
// 읽기 - 정적 접근은 GameDB (예시 이름)
float spawnInterval = GameDB.Spawn.Interval;
_waitSpawn = new WaitForSeconds(GameDB.Spawn.Interval);

// 매 프레임 접근 코드는 Start에서 SO 참조를 캐싱 (Awake 아님 - 아래 규칙 참조)
private EnemySO _enemyData;
private void Start() { _enemyData = GameDB.Enemy; }

// NetworkBehaviour는 OnStartServer / OnStartClient에서 캐싱해도 된다
public override void OnStartServer() { _enemyData = GameDB.Enemy; }
```

**GameDB 규칙**
- SO 필드는 `[SerializeField] private` + 읽기 전용 프로퍼티. 필드에 단위/용도 주석
- **GameDB를 초기화하는 것은 `GameDBBootstrap` 하나뿐이다.** `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`에서 `Resources/GameDB.asset`을 로드해 `GameDB.Initialize()`를 호출한다. `Awake`도 같은 멱등 초기화 경로를 호출한다. Mirror가 서버 시작 전 매니저를 비활성화할 수 있으므로 `Awake`만을 부트 순서의 근거로 삼지 않는다
- **소비 컴포넌트는 `Awake`와 필드 초기화식에서 GameDB를 읽지 않는다.** `Start` · `OnStartServer` · `OnStartClient` 이후에 읽는다 — 같은 씬에 있는 컴포넌트들의 `Awake` 실행 순서는 보장되지 않으므로, `Awake`에서 읽으면 `GameDBBootstrap`보다 먼저 도는 컴포넌트가 초기화 전 DB를 잡는다
- 루트 DB나 필수 참조가 없으면 조용히 기본값으로 진행하지 말고 초기화 오류로 중단한다
- 컴포넌트에 밸런스 값을 SerializeField 미러로 두지 않는다 — DB 원본이 단일 소스
- 새 도메인은 위 표에 항목 추가 → `*SO` 클래스 생성 → `GameDatabaseSO`에 참조 추가 → `GameDB` 프로퍼티 추가 → `09.Data/<도메인>/`에 에셋 생성
- 밸런스 수치 변경은 DB 에셋 인스턴스에서 — 코드는 건드리지 않는다. 절차는 [밸런스 데이터 작업](unity/BALANCE_DATA_WORKFLOW.md)
- 네트워크를 타지 않는 로컬 표현용 SO(조명 프리셋 등)는 표의 "담는 값"에 로컬 전용임을 적는다

### Constants — 코드 계약값 (partial class)

```
Constants.cs             — 공통 (레이어, 태그, 씬 이름)
Constants.player.cs      — Input Manager 축, 버튼 이름, 판정 epsilon, ANIM_DIR_*
Constants.combat.cs      — 히트스탑, 적 SerializeField 기본값, enum 연동 개수
Constants.network.cs     — 틱 레이트, 동기화 관련 상수 (Mirror)
Constants.ui/audio/...   — 각 시스템 계약값
```

```csharp
bool attack = Input.GetButtonDown(Constants.Player.BUTTON_ATTACK); // Input Manager 버튼 이름
float dist = _col.bounds.extents.y + Constants.Player.GROUND_CHECK_EXTRA;
```

**Constants 규칙**
- 해당 시스템 파일에만 추가, UPPER_SNAKE_CASE, 반드시 단위/용도 주석
- 파일 필드가 전부 밸런스 값이 되면 GameDB의 `*SO`로 승격을 검토한다

---

## 17. 런타임 GC 최소화 (프레임 드랍 방지)

**게임 루프에서 도는 코드는 프레임당 할당 0을 목표로 한다.**

```csharp
// ✅ WaitForSeconds 캐싱 — 코루틴이 반복 실행되면 필드로 1회 생성
private WaitForSeconds _waitInvincible;
private void Awake() { _waitInvincible = new WaitForSeconds(_invincibleDuration); }
// 코루틴 안에서: yield return _waitInvincible;

// ❌ 반복 코루틴 안에서 매번 new
yield return new WaitForSeconds(1f);

// ✅ renderer.material 캐싱 — Awake 1회 취득 후 재사용
private Material _material;
private void Awake() { _material = _renderer.material; }

// ❌ 매 프레임 접근 (프로퍼티 호출 비용 + 최초 인스턴스 생성 위험 불명확)
_renderer.material.color = Color.white;

// ✅ 물리 쿼리는 재사용 버퍼 + ContactFilter2D
private static readonly Collider2D[] _results = new Collider2D[16];
int count = Physics2D.OverlapCircle(origin, radius, _filter, _results);

// ❌ 호출마다 배열 할당
Collider2D[] hits = Physics2D.OverlapCircleAll(origin, radius);
```

**추가 규칙**
- 컴포넌트/참조 취득은 Awake/Start 1회 캐싱. Update/공격 루프에서 `GetComponent`/`Find` 금지 → [22장](#22-find-계열-api-지양)
- 람다/클로저 캡처가 있으면 힙 할당 → 루프 안에서 만들지 말고, 필요 시 대상에 대해 1회만 생성
- 반복 사용하는 List는 필드로 두고 `Clear()` 후 재사용
- 오브젝트 탐색 타입은 O(1) 자료구조(Stack/Queue) 사용, `List.Contains` 루프 금지

---

## 18. 싱글턴

`PersistentSingleton<T>`, `SceneSingleton<T>`는 **도입 예정**이다. 첫 싱글턴이 필요할 때 `00.Common/Utility/`에 만들고, 이후 모든 싱글턴이 이를 상속한다.

**DontDestroyOnLoad 싱글턴은 `Utility/PersistentSingleton<T>`, 씬 로컬 싱글턴은
`Utility/SceneSingleton<T>`를 상속한다.** Awake를 직접 만들지 말고
`OnSingletonAwake()`를 오버라이드한다.

```csharp
// ✅
public class GameManager : PersistentSingleton<GameManager>
{
    protected override void OnSingletonAwake()
    {
        // 최초 인스턴스 확정 후 1회 초기화
    }
}

public class RoundManager : SceneSingleton<RoundManager>
{
    protected override void OnSingletonAwake()
    {
        // 현재 씬 최초 인스턴스 확정 후 초기화
    }
}

// ❌ 클래스마다 Instance/Awake 중복 구현 금지
```

- 씬 배치 없이도 동작해야 하면 대상 클래스에 `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 아래서 `EnsureCreated("이름")` 호출
- 도메인 리로드 OFF 대비: 대상 클래스에 `SubsystemRegistration` 아래서 `ResetStatic()` 호출
- 클래스 고유 정리는 `OnSingletonDestroy()`에서 수행한다

멀티플레이 세션에서 `NetworkManager` 등 Mirror가 자체적으로 DontDestroyOnLoad를 관리하는 오브젝트는 이 패턴을 강제하지 않는다 — [네트워크 권위](unity/NETWORK_AUTHORITY.md) 문서를 우선한다.

---

## 19. 네트워크 코드 규칙

서버 권위 판정, `[Command]`/`[ClientRpc]` 사용법, Mirror API 매핑은 이 문서에서 중복하지 않는다 — [네트워크 권위](unity/NETWORK_AUTHORITY.md)를 기준으로 한다. 이 문서의 스타일 규칙(네이밍 · 중괄호 · null 체크 등)은 네트워크 코드에도 동일하게 적용된다.

---

## 20. 여러 줄 시그니처/호출의 들여쓰기 (TABWIDTH 4)

**매개변수가 길어 줄바꿈하는 메서드 시그니처/호출은, 여는 괄호 뒤 컬럼에 맞춰 정렬하지 않는다.
이어지는 줄은 원본 줄의 들여쓰기에서 TABWIDTH(4) 만큼만 추가로 들여쓴다.**

```csharp
// ❌ 여는 괄호 뒤에 맞춤 (TABWIDTH 배수가 아닌 임의 칸수)
public ProjectilePool(int count, string namePrefix, Sprite customSprite = null,
                      bool attachParticle = false, float particleSize = 0.2f)

// ✅ 원본 줄 들여쓰기 + TABWIDTH(4)
public ProjectilePool(int count, string namePrefix, Sprite customSprite = null,
    bool attachParticle = false, float particleSize = 0.2f)
```

메서드 호출도 동일하게 적용한다.

```csharp
// ✅
Configure(i, rec.Position, rec.Scale, rec.RotationDeg, rec.Color,
    rec.HasCollider, rec.DamageHalves, rec.StunDuration, rec.InstantKill);
```

---

## 21. 가드 절 — null 체크와 예외 처리는 본문 맨 위에

**null 체크 · 인자 검증 · 상태 검증은 메서드 본문 맨 위에 모아서 먼저 하고, 조건에 맞지 않으면 즉시 빠져나온다.**
본문 중간이나 아래에 흩어놓지 않는다.

```csharp
// ✅ 검사는 위에, 본문은 아래에
public void TakeDamage(float damage, GameObject attacker)
{
    if (attacker == null)
    {
        return;
    }

    if (_isDead)
    {
        return;
    }

    if (damage <= Constants.Combat.DAMAGE_EPSILON)
    {
        return;
    }

    _health -= damage;      // 여기서부터는 유효한 상태가 보장된다
    ApplyHitFeedback();
}

// ❌ 검사가 본문 중간에 섞여 있다
public void TakeDamage(float damage, GameObject attacker)
{
    _health -= damage;
    if (_isDead)            // 이미 깎은 뒤에 검사한다
    {
        return;
    }
    ApplyHitFeedback();
    if (attacker == null)   // 여기까지 와서야 검사한다
    {
        return;
    }
}
```

**중첩 대신 조기 반환을 쓴다.** 들여쓰기가 깊어질수록 어떤 조건에서 도달하는 코드인지 읽기 어려워진다.

```csharp
// ✅
if (!isServer)
{
    return;
}

if (!TryGetComponent(out PlayerStatus status))
{
    return;
}

status.Revive();

// ❌ 중첩
if (isServer)
{
    if (TryGetComponent(out PlayerStatus status))
    {
        status.Revive();
    }
}
```

**try/catch도 같다.** 예외가 날 수 있는 구간만 감싸고, 정상 경로를 catch 안에 넣지 않는다.

아래 ItemInstance/Deserialize는 예외 범위를 설명하는 가상 예시다.

```csharp
// ✅ 예외 구간만 감싼다
ItemInstance parsed;
try
{
    parsed = ItemInstance.Deserialize(raw);
}
catch (FormatException e)
{
    Debug.LogError($"[Drop] 장비 역직렬화 실패: {e.Message}");
    return;
}

ApplyToSlot(parsed);   // 정상 경로는 catch 밖에

// ❌ 본문 전체를 감싼다 — 어디서 터졌는지 알 수 없고 오류를 조용히 삼키게 된다
try
{
    var parsed = ItemInstance.Deserialize(raw);
    ApplyToSlot(parsed);
    RefreshInventoryUI();
}
catch { }
```

- **`catch { }`로 비우지 않는다.** 최소한 `Debug.LogError`로 남긴다 — 오류를 조용히 삼키면 여러 명이 접속했을 때만 나는 결함을 추적할 수 없다
- `[Command]` 수신부의 서버 검증(호출자 · 상태 · 거리)도 가드 절이다. 판정 코드보다 먼저 온다 → [네트워크 권위](unity/NETWORK_AUTHORITY.md)

---

## 22. Find 계열 API 지양

**`Find`로 시작하는 탐색 API는 쓰지 않는 것을 원칙으로 한다.**

```csharp
// ❌ 금지
GameObject.Find("Player");
FindAnyObjectByType<GameModeController>();
FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
transform.Find("Model/Weapon");     // 매 프레임 호출 시

// ✅ 인스펙터 주입
[SerializeField] private Transform _weaponSocket;

// ✅ 초기화 1회 캐싱
private PlayerStatus _status;
private void Start() { _status = GetComponent<PlayerStatus>(); }

// ✅ 싱글턴 · 로케이터 · 이벤트로 받는다
_hitFeedback = HitFeedbackLocator.Current;
```

**이유**: 씬에 있는 오브젝트를 전부 순회하므로 씬이 커질수록 비용이 선형으로 늘어난다.
데디케이티드 서버 한 대가 한 판의 모든 판정을 진다. 서버 틱이 떨어지면 접속한 전원의 판이 흔들리므로 서버 틱을 잡아먹는 탐색은 피한다.
문자열 기반 `GameObject.Find`는 여기에 더해 오브젝트 이름을 바꾸는 순간 컴파일 에러 없이 런타임에 null이 되는 문제까지 있다.

### 허용하는 경우

아래 두 가지만 예외로 두고, **그 외에는 리뷰에서 막는다.**

| 허용 | 조건 |
|---|---|
| 에디터 전용 코드 | `#if UNITY_EDITOR` 블록, 에디터 툴, 테스트 코드 |
| 초기화 1회 폴백 | `Awake`/`Start`/`OnStartServer`에서 단 한 번, 결과를 필드에 캐싱. **이유를 주석으로 남긴다** |

**어떤 경우에도 `Update`·`FixedUpdate`·코루틴 루프·공격 판정 루프 안에서는 호출하지 않는다.**

`InputActionAsset.FindActionMap` · `FindAction`은 이름은 Find지만 씬을 순회하지 않는 에셋 내부 조회라 이 규칙의 대상이 아니다. 다만 이것도 매 프레임 호출하지 말고 초기화 시 캐싱한다.

---

## 23. 설치된 플러그인 우선 사용

**같은 일을 하는 플러그인이 프로젝트에 이미 있으면 직접 만들지 않고 그 플러그인을 쓴다.** 직접 만든 대체품은 버그와 유지보수를 팀이 떠안고, 플러그인과 섞이면 같은 일을 두 방식으로 하게 된다. 새 플러그인 · 패키지를 들이는 것은 사전 승인 대상이다 ([에이전트 실행 정책](ai/01_AGENT_EXECUTION_POLICY.md)).

| 용도 | 쓸 것 | 직접 만들지 않는 것 |
|---|---|---|
| 트윈 · 보간 연출 (이동, 회전, 크기, 페이드, 색, UI 애니메이션, 흔들림, 숫자 카운트) | **DOTween** (Pro 포함, `DG.Tweening`) | `Update`나 코루틴 안의 Lerp 루프, 직접 만든 이징 함수 |
| 대화, 퀘스트, 바크, 대사 텍스트 테이블 | **Dialogue System for Unity** (Pixel Crushers, `PixelCrushers.DialogueSystem`) | 자체 대화 트리, 퀘스트 상태 머신 |
| 재사용하는 레이캐스트 감지 컴포넌트 (시야, 센서, 탄도) | **RaycastPro** (`RaycastPro`) | 같은 기능의 자체 감지 컴포넌트 |
| 카메라 추적 · 전환 블렌드 · 카메라 흔들림 | **Cinemachine 3** (`Unity.Cinemachine`) | 직접 만든 카메라 추적 스크립트 |
| 입력 | **구 Input Manager** (`UnityEngine.Input`, 축과 버튼은 `ProjectSettings/InputManager.asset`). Active Input Handling은 Both | Input System 패키지 API (`UnityEngine.InputSystem`) - 설치는 되어 있지만 팀 코드에서 쓰지 않는다 |
| UI · 월드 텍스트 | **TextMesh Pro** (`TMPro`) | 레거시 `UnityEngine.UI.Text` |
| 커스텀 셰이더 | **Amplify Shader Editor** | 손으로 쓴 셰이더 (ASE로 안 되는 경우만, 이유를 주석으로) |
| 메시 · 머티리얼 결합 (드로우콜 절감) | **MeshBaker** | 자체 메시 결합 코드 |

- 단순한 1회 물리 쿼리(접지 확인 등)는 RaycastPro 없이 `Physics` NonAlloc API를 직접 써도 된다 (17장).
- 에디터 전용 도구(Console Pro, Hierarchy Designer, Maintainer, Wingman, MCP for Unity)는 런타임 코드에서 참조하지 않는다.
- 플러그인 기능으로 안 되는 이유가 있어 직접 만들면, 그 이유를 주석으로 남긴다 (12장 "다른 안을 버린 이유").

### DOTween 규칙

```csharp
using DG.Tweening;

[SerializeField] private float _fadeDuration = 0.3f;

private Tween _fadeTween;

private void Awake()
{
    // Hide가 반복 호출되므로 한 번 만들어 재사용한다
    _fadeTween = _canvasGroup.DOFade(0f, _fadeDuration)
        .SetAutoKill(false)
        .Pause()
        .SetLink(gameObject);
}

public void Hide() { _fadeTween.Restart(); }
```

- **트윈은 소유 오브젝트에 묶는다.** `SetLink(gameObject)`를 붙이거나 `OnDisable`/`OnDestroy`에서 `Kill()`한다. 파괴된 오브젝트를 붙잡은 트윈이 남으면 null 참조 경고와 누수가 난다.
- **반복 재생하는 트윈은 필드에 캐싱해 재사용한다** (`SetAutoKill(false)` + `Restart()`). 매번 새로 만들면 할당이 쌓인다.
- 시간 · 거리 · 이징 값에 매직넘버를 쓰지 않는다 (3장). 밸런스에 닿는 값이면 GameDB, 연출 값이면 `[SerializeField]`.
- **트윈은 표현 전용이다.** 서버 판정(피해 적용, 위치 권위, 판정 타이밍)을 트윈 진행이나 완료 콜백에 걸지 않는다. 클라이언트마다 프레임이 달라 끝나는 시점이 어긋난다 - [네트워크 권위](unity/NETWORK_AUTHORITY.md).
- 연출 시퀀스 안의 대기는 `Sequence.AppendInterval`, 게임 논리의 대기는 코루틴을 쓴다 (15장).
- asmdef를 만들어 DOTween을 참조할 때는 `DOTween.Modules`(Pro 기능은 `DOTweenPro.Scripts`)를 참조에 추가한다.
- DOTween 전역 설정(`Assets/99.ThirdParty/Animation/Demigiant/DOTween/Resources/DOTweenSettings.asset`)과 DOTween Utility Panel의 Setup(스크립팅 심볼 변경)은 공용 설정이라 {팀장} 확인 후 바꾼다.

---

## 요약 카드 (팀 공유용)

```
✅ 클래스/메서드       PascalCase
✅ enum 타입           E + PascalCase (EItemGrade), 값은 PascalCase (Epic)
✅ 멤버 변수           _camelCase
✅ 상수                UPPER_SNAKE_CASE
✅ 중괄호              다음 줄 (Allman) + 모든 제어문 필수
✅ 증감                ++i, --i (전위)
✅ 복합 조건           ((a > b) && (c < d))
✅ 매직넘버            밸런스는 GameDB(SO DB), 계약값은 Constants.*.cs
✅ SO 네이밍           클래스 *SO (16장 대상 표 기준), 캐시 변수는 일반 _camelCase
✅ 필드 정렬           = 기준 정렬
✅ 여러 줄 시그니처    여는 괄호 정렬 금지, 원본 줄 + TABWIDTH(4)만 추가 들여쓰기
✅ Inspector 노출      [SerializeField] private
✅ 폴더               Assets/01.Scripts/{NN}.{도메인}/, 서버 전용은 98.Servers/ (Shinshi.Server)
✅ 네트워크 분리       Rules(공용 계산) / Net/*State(동기화) / 98.Servers(서버 전용) / View(표현), NetworkBehaviour partial 분할 금지
✅ 네임스페이스        파트 기준 (Shinshi.Common/Data/Network/Combat/AI/Systems/Art/UI/Dev/Editor)
✅ FixedUpdate         지양, 코루틴 지향 (물리 동기화 예외)
✅ GC                  WaitForSeconds/material/참조 캐싱, NonAlloc 쿼리
✅ null 체크           Unity 오브젝트 == null, 컴포넌트는 TryGetComponent
✅ 주석                "무엇"은 한 줄, "왜"는 길어도 됨. 문서 절 번호 금지, ASCII 기호만
✅ XML 주석            <summary> 하나만. remarks/see/c/inheritdoc 금지, 나머지는 //
✅ 가드 절             null/상태 검사는 본문 맨 위 + 조기 반환, catch {} 금지
✅ Find 계열           원칙 금지, 인스펙터 주입·초기화 1회 캐싱으로 대체
✅ 싱글턴              DDOL=PersistentSingleton<T>, 씬 로컬=SceneSingleton<T>
✅ 네트워크            스타일은 이 문서, 권위 판정은 네트워크 권위 문서
✅ 플러그인            설치된 것 우선. 트윈=DOTween, 대화=Dialogue System, 카메라=Cinemachine, 입력=구 Input Manager, 텍스트=TMP
```
