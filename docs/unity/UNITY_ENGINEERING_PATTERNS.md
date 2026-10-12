# Unity 엔지니어링 패턴

> 실제로 문제가 생겼을 때 해당 절만 읽는다. 처음부터 전부 읽지 않는다.
> 2026-09-30 shinshi 기준으로 이식. 게임 코드와 공용 기반이 존재한다. 아래 예시의 클래스·이벤트는 실제 구현과 이름이 같다고 가정하지 말고, 도입 여부는 해당 소스에서 확인한다.
> 명명, 중괄호, GC 같은 스타일 규칙은 [코딩 컨벤션](../coding-convention.md)이 기준이다. 이 문서는 수명과 성능 패턴만 다룬다.

---

## 1. 상시 Tick을 줄인다

`Update`, `LateUpdate`, `FixedUpdate`는 **매 Tick 처리가 실제로 필요할 때만** 쓴다.

| 상황 | 권장 |
|---|---|
| 상태가 바뀔 때만 반응 | 이벤트 · 콜백 |
| 한정된 기간의 감시 | 코루틴 또는 타이머, 끝나면 중지 |
| 매 프레임 위치 갱신 | `Update` 정당 |
| 물리 연산 | `FixedUpdate` 정당 |

### 자주 걸리는 것

```csharp
// 잘못 - 피해 구역 안에 있는지 매 프레임 판정
private void Update()
{
    if (Vector3.Distance(transform.position, _zoneCenter) < _zoneRadius)
    {
        TakeDamage(_damagePerSecond * Time.deltaTime);
    }
}

// 맞음 - 1초 주기 타이머, 서버에서만
// 초당 1회 판정이 기획 요구사항이면 매 프레임 계산할 이유가 없다
```

**코루틴을 썼다는 사실만으로 Tick 최적화가 됐다고 판정하지 않는다.**

### 같은 판정을 여러 개체가 돌면 프레임을 나눈다

적이 여러 마리 동시에 존재하면, 서버 권위 구조에서는 그 판정을 전부 서버가 돈다.
경로 재계산처럼 무거운 것을 매 프레임 전원이 돌 이유는 없다.

```csharp
// 잘못 - 적 전원이 매 프레임 경로 재계산
private void Update()
{
    _agent.SetDestination(_target.position);
}

// 맞음 - 프레임마다 정해진 수만 갱신하고 나머지는 이전 경로로 이동
// 갱신 주기가 늘어져도 추적 체감은 거의 그대로다
```

리스트를 인덱스로 돌며 프레임당 처리 수를 상한으로 두면, 개체가 늘어도 서버 부하는 상한 안에 머문다.
반응이 즉시 필요한 것(피격, 사망, 짧은 입력 판정 창)은 분산 대상이 아니다. 이벤트로 그 자리에서 처리한다.

---

## 2. 반복 탐색을 캐싱한다

`GetComponent`와 `Find` 계열은 초기화 · 생성 · 바인딩 시점에 한 번 수행하고 캐싱한다.

```csharp
// 잘못
private void Update()
{
    GetComponent<Rigidbody>().AddForce(_force);
}

// 맞음
private Rigidbody _rigidbody;

private void Awake()
{
    _rigidbody = GetComponent<Rigidbody>();
}

private void Update()
{
    _rigidbody.AddForce(_force);
}
```

동적 생성물이나 교체된 참조를 **관계 변경 시점에 한 번** 다시 바인딩하는 것은 위반이 아니다.

`Find`, `FindObjectOfType`, `GameObject.FindWithTag` 는 씬 전체를 순회하므로 Tick 루프에서 절대 쓰지 않는다. 허용하는 경우는 [코딩 컨벤션](../coding-convention.md)의 Find 계열 장을 따른다.

---

## 3. 코루틴 수명

코루틴은 시작보다 **종료**가 어렵다.

- 중복 시작을 막는다 (기존 핸들을 저장하고 `StopCoroutine`)
- 완료 · 취소 · 비활성화 · 파괴 시점의 종료를 명시한다
- 종료 시 상태를 복원한다

```csharp
private Coroutine _spawnRoutine;

private void StartWave()
{
    if (_spawnRoutine != null)
    {
        StopCoroutine(_spawnRoutine);
    }

    _spawnRoutine = StartCoroutine(SpawnOverTime());
}

private void OnDisable()
{
    if (_spawnRoutine != null)
    {
        StopCoroutine(_spawnRoutine);
        _spawnRoutine = null;
    }
}
```

`OnDisable`이나 `OnDestroy`에서 정리하지 않으면 상태가 어긋난 채 남는다.

---

## 4. 이벤트 구독 해제

이벤트 구독에는 명확한 소유권과 해제 시점을 둔다.
해제하지 않으면 파괴된 오브젝트가 계속 호출되어 `MissingReferenceException`이 난다.

씬 수명보다 오래 사는 정적 이벤트는 명시적인 `Bind`/`Unbind` 또는 `Register`/`Unregister`와 리셋 책임이 있을 때만 사용한다. 일반 씬 이벤트는 인스턴스 이벤트나 이벤트 버스를 사용한다. Domain Reload를 껐을 때 구독자가 남지 않는지도 확인한다.

어느 기전을 언제 쓰는지는 [디자인 패턴](DESIGN_PATTERNS.md) 1장이 기준이다.

### 같은 파트 안 - 인스턴스 이벤트를 직접 쓴다

아래 예시는 Mirror 설치 뒤의 서버 전용 `NetworkBehaviour`다.

```csharp
public override void OnStartServer()
{
    _health.Died += HandleDied;
}

public override void OnStopServer()
{
    _health.Died -= HandleDied;
}
```

### 파트 경계를 넘을 때 - 이벤트 버스를 지난다

**(도입 예정)** 파트 경계용 이벤트 버스(`GameEventBus`)는 아직 없다. 만들면 아래 형태로 구독한다.

```csharp
private void OnEnable()
{
    if (!GameEventBus.HasInstance)
    {
        return;
    }

    GameEventBus.Instance.Subscribe<MatchPhaseChangedEvent>(HandlePhaseChanged);
}

private void OnDisable()
{
    // 씬이 끝날 때 버스가 먼저 파괴되면 Instance가 null이다
    if (!GameEventBus.HasInstance)
    {
        return;
    }

    GameEventBus.Instance.Unsubscribe<MatchPhaseChangedEvent>(HandlePhaseChanged);
}
```

다른 파트의 구체 클래스를 참조하면 통합할 때 한 파트를 고칠 때마다 다른 파트가 깨진다.
폴더와 네임스페이스 배치는 [코딩 컨벤션](../coding-convention.md)을 따른다.

### 해제 시점은 컴포넌트 종류로 정한다

| 컴포넌트 | 구독 | 해제 |
|---|---|---|
| 서버 전용 NetworkBehaviour | `OnStartServer` | `OnStopServer` |
| 씬 로컬 MonoBehaviour | `OnEnable` | `OnDisable` |

서버 이벤트를 클라이언트에서 구독하지 않는다. 데디케이티드 서버에서는 서버와 클라이언트가 다른 프로세스라
전달되지 않는다. 에디터 호스트 모드는 한 프로세스가 둘을 겸하므로, 구독 시점을 잘못 잡아도 혼자서는 증상이 드러나지 않는다.

### 람다로 구독하면 해제되지 않는다

```csharp
// 해제되지 않는다, 두 람다가 서로 다른 델리게이트다
bus.Subscribe<MatchPhaseChangedEvent>(e => HandlePhaseChanged(e));
bus.Unsubscribe<MatchPhaseChangedEvent>(e => HandlePhaseChanged(e));

// 메서드 그룹을 넘긴다
bus.Subscribe<MatchPhaseChangedEvent>(HandlePhaseChanged);
bus.Unsubscribe<MatchPhaseChangedEvent>(HandlePhaseChanged);
```

`Unsubscribe`는 델리게이트 비교로 지운다. 이 절이 막으려는 누수를 이 실수가 그대로 만든다.

---

## 5. 직렬화된 enum

`ItemGrade`, `EnemyKind` 같은 직렬화 대상 enum은 **명시값을 유지**하고 기존 값을 재배열하지 않는다.

```csharp
public enum ItemGrade
{
    Common = 0,
    Rare = 1,
    Epic = 2,
    Legendary = 3,
}
```

값을 재배열하면 이미 저장된 ScriptableObject 에셋과 씬의 값이 다른 것을 가리키게 된다.
새 항목은 **끝에 추가**한다.

---

## 6. 오브젝트 풀링

**(도입 예정)** 로컬 풀 구현은 아직 없다. 처음 필요해지는 사람이 공용 로컬 풀 하나를 만들고, 이후 로컬 연출은 모두 그 풀과 그 풀의 계약(예: `IPoolable`)을 따른다. 파트마다 새 풀 구현을 만들지 않는다.

| 대상 | 이유 |
|---|---|
| 히트 이펙트 | 전투 중 지속 생성 |
| 짧은 행동 VFX | 여러 플레이어가 반복 재생하며 수명이 짧음 |

`NetworkServer.Spawn` 대상인 네트워크 개체(투사체, 적 등)는 로컬 풀에 넣지 않는다. 네트워크 개체 풀링은 Mirror 스폰 핸들러를 등록하는 별도 작업이다.

---

## 7. 오류 처리

오류를 조용히 삼키지 않는다.

```csharp
// 잘못
try
{
    DoSomething();
}
catch
{
}

// 맞음
try
{
    DoSomething();
}
catch (Exception e)
{
    Debug.LogError($"[Spawn] 웨이브 생성 실패: {e.Message}");
}
```

로그에 **어느 시스템인지 접두사**를 붙인다. Console 검색창에서 접두사로 걸러 볼 수 있다.

---

## 8. 성능 확인

문제가 있다고 느끼면 추측하지 말고 측정한다.

| 도구 | 용도 |
|---|---|
| Profiler | CPU · GPU · 메모리 |
| Frame Debugger | 드로우콜 |
| Memory Profiler | 할당 추적 |

**서버와 클라이언트를 따로 잰다.** 호스트 방식은 데디케이티드 서버(확정)다. 게임 로직 병목은 서버 빌드에서,
렌더링 병목은 클라이언트에서 잰다. 에디터 호스트 모드에서 잰 값은 둘이 섞여 어느 쪽 병목인지 가릴 수 없다.

---

## 9. UI 작업 - 씬 우선, 코드는 최소한

UI(`{NN}.UI`, 번호는 파트 확정 시 조정)는 {파트} 담당({팀원이름})이 맡는다.

**Unity 에디터에서 직접 만들고 눈으로 확인할 수 있는 것은 전부 씬 · 프리팹에서 만든다.** 코드로는 에디터에서 할 수 없는 것만 작성한다.

| 씬/프리팹에서 한다 | 코드로 한다 |
|---|---|
| 레이아웃 · 앵커 · 정렬 (RectTransform, Layout Group) | 게임 상태 → 텍스트 · 값 · 활성화 여부 바인딩 |
| 색상 · 폰트 · 스타일 (컴포넌트 인스펙터) | 이벤트 구독 (버튼 클릭 → Command 호출 등) |
| 패널 계층 구조, 버튼 · 슬라이더 등 컴포넌트 배치 | 데이터 기반 반복 생성 (인벤토리 슬롯처럼 개수가 가변인 목록) |

```csharp
// 잘못 - 인스펙터로 되는 배치를 코드로 조립
private void Awake()
{
    var panel = new GameObject("HpBar");
    panel.transform.SetParent(_canvas.transform);
    var image = panel.AddComponent<Image>();
    image.color = Color.red; // 이런 값도 인스펙터에서 정한다
}

// 맞음 - 프리팹에 이미 배치된 참조를 코드는 갱신만 한다
[SerializeField] private Image _hpFillImage;

private void OnHpChanged(float ratio)
{
    _hpFillImage.fillAmount = ratio;
}
```

개수가 실행 중에 정해지는 목록(인벤토리 슬롯, 리더보드 행 등)처럼 씬에서 미리 배치할 수 없는 경우에만 코드로 인스턴스화한다. 이때도 슬롯 자체의 레이아웃 · 스타일은 프리팹으로 만들고, 코드는 그 프리팹을 개수만큼 생성 · 바인딩하는 역할만 한다.
