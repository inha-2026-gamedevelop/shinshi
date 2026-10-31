# 무철 VFX 01~07 사용 안내

대상: 보스 클라이언트 표현 담당자. 2026-10-14 현재 구현 기준.
2026-10-22 정적 대조: 프리팹·개발 씬·시간 설정과 `BossNetworkViewBinder`→`MucheolMotionView`/`MucheolPatternVfxView` 연결이 존재한다. 아래 호출 순서는 공개 API 사용 안내이며 실제 보스 전체 전투·다인 수용 검증을 뜻하지 않는다.

## 1. 바로 확인하기

1. Unity 6000.4.7f1 / URP 프로젝트에서 `Assets/00.Scenes/Dev_VFX/Dev_Mucheol_02_07.unity`를 연다.
2. `Shinshi > VFX > Mucheol 02-07 > Select 02`부터 `Select 07`까지 원하는 번호를 고른다.
3. 선택된 루트의 `MucheolBatchVfxPlayer` Inspector에서 **재생 / 중지**, 시간 슬라이더로 확인한다. 에디터 미리보기도 지원한다.
4. 01은 `Assets/00.Scenes/Dev_VFX/Dev_Mucheol_01_ImpactV4.unity`에서 확인한다.

이미 생성된 프리팹은 그대로 사용할 수 있다. `Build All Prefabs`는 외부 Blender 출력으로 다시 만드는 제작용 명령이므로 일반 사용자는 실행하지 않는다. 외부 제작 원본이 없는 PC에서도 커밋된 프리팹·재질·텍스처로 재생할 수 있다.

01~07은 [맵 성능 사전 준비의 필수 프리워밍 대상 목록](MAP_PERFORMANCE_PREPARATION.md#등록된-필수-대상--무철-0107)에 등록되어 있다. 무철 전장 입장 준비용 목록이며 실제 로더의 자동 워밍업 연결은 아직 없다.

## 2. 프리팹 선택과 배치

공통 폴더: `Assets/02.Prefab/VFX/Mucheol/`.

| 번호 | 파일 | 용도·배치 기준 |
|---|---|---|
| 01 | `PF_Mucheol_01_ImpactV4.prefab` | 부채꼴 타격, `MucheolImpactVfxPlayer` 직접 사용 |
| 02 | `PF_Mucheol_02.prefab` | 반경 5m 원형 |
| 03 | `PF_Mucheol_03.prefab` | 폭 3m·길이 18m 직선, 루트에서 로컬 +Z 방향 |
| 04 | `PF_Mucheol_04.prefab` | 반경 5m 소충격파, 개별 1타 |
| 05 | `PF_Mucheol_05.prefab` | 반경 6m 별도 2타 효과. 04를 자동 재생하지 않음 |
| 06 | `PF_Mucheol_06.prefab` | 폭 3m·길이 18m 돌진 경로, +Z 이동 후 끝에서 타격 |
| 07 | `PF_Mucheol_07.prefab` | 반경 44m·315도 영역, 로컬 -Z를 중심으로 후방 45도 안전구역 |

프리팹 루트를 확정된 패턴 원점의 지면에 놓고 Y축 회전으로 방향을 맞춘다. 기본 스케일 `(1,1,1)`을 유지한다. 크기 변경은 시각 범위만 바꾸므로 서버 범위와 별도 대조가 필요하다. 지형 굴곡에 투영하는 데칼 구현은 아니다. 06은 경로 전체가 고정되고 내부 돌진 머리만 이동하므로 보스 몸체에 붙여 함께 이동시키지 않는다.

02~07은 루트 `MucheolBatchVfxPlayer`만 호출한다. 자식 `MucheolImpactVfxPlayer`를 별도로 재생하면 03 충격파 위치와 06 돌진 시계가 어긋날 수 있다.

## 3. Inspector 시간 조절

01 설정: `Assets/01.Scripts/05.Art/VFX/Mucheol/ImpactV4/Timing_01.asset`.
02~07 설정: `Assets/01.Scripts/05.Art/VFX/Mucheol/ImpactBatch/NN/Timing_NN.asset`.

| 설정 | 의미 |
|---|---|
| Telegraph Seconds | 로컬 미리보기에서 전체 예고가 유지되는 시간 |
| Expansion Seconds | 확정 타격 뒤 충격파 확산 시간 |
| Hold Seconds | 확산 이후 지면·균열 유지 시간 |
| Fade Seconds | 지면·균열 소멸 시간 |
| Debris Seconds | 타격부터 파편이 사라지기까지의 독립 시간 |
| Charge Seconds | 06 루트의 돌진 시간, 다른 번호는 0 유지 |
| Repeat Preview / Preview Gap Seconds | 로컬 미리보기 반복 여부와 간격 |
| Play On Start | 로컬 자동 미리보기. 실제 전투용 인스턴스는 끔 |

단위는 초다. SO를 공유한 모든 인스턴스에 변경이 영향을 준다. 난이도별 값을 분리하려면 SO를 복제하고 루트 Profile에 배정한다. 02~07 커스텀 Inspector는 자식 프로파일도 동기화한다. 런타임에서 임의로 private 필드만 변경하는 방식은 지원하지 않는다. 외부 재생은 시작할 때 시간을 스냅샷하므로 설정 변경은 다음 재생부터 확인한다.

미리보기 전체 길이 = 예고 + 돌진 + max(확산 + 유지 + 소멸, 파편). SO의 `Duration`에는 돌진과 더 긴 파편 꼬리가 포함되지 않으므로 02~07 미리보기 길이는 루트 `PreviewDuration`을 사용한다.

**이 설정은 VFX 전용이다. 피해 지속시간·서버 쿨다운·판정 범위는 바뀌지 않는다.** 외부 재생의 예고 종료는 SO 예고 값이 아니라 호출자가 넘긴 `impactTime`이 결정한다. 06 돌진은 `impactTime - ChargeSeconds`부터 시작하며 전체 예고 구간보다 길면 구간 길이로 제한된다.

## 4. 실제 보스 이벤트 연결

네임스페이스는 `Shinshi.Art`다. 연결 담당자는 기존 보스 View/바인더에서 참조를 캐싱하고 아래 순서로 호출한다. 재생기는 네트워크 메시지를 직접 받거나 피해를 계산하지 않는다.

| 시점 | 호출 | 책임 |
|---|---|---|
| 패턴 예고 수신 | `BeginTelegraph(startTime, impactTime, now)` | 원점·방향을 먼저 설정한 활성 인스턴스 사용 |
| 서버 타격 확정 수신 | `ConfirmImpact(hitTime, now)` | 실제 확정 시각 전달. 타이머만으로 타격 추정 금지 |
| 재생 중 프레임 | `Tick(now)` | 외부 모드에서는 호출자가 매 프레임 진행시킴 |
| 취소·보스 사망·씬 이탈·풀 반환 | `Stop()` | 지면·파편·돌진 머리 정리 |

네 인자의 시간은 모두 같은 시계·같은 기준점의 초 단위 `double`이어야 한다. 서버 시각과 `Time.time`을 섞지 않는다. 프로젝트에서 동기화된 시계로 변환한 현재 시각을 사용한다. `Preview()`는 `Time.unscaledTimeAsDouble` 기반 로컬 자동 확정이므로 실제 전투 연결용으로 사용하지 않는다.

다음은 호출부 개념 예시다. 이벤트 이름이나 보스 패턴 ID를 새로 정의하는 코드는 아니다.

```csharp
// 예고 수신 시: vfx는 미리 캐싱한 MucheolBatchVfxPlayer
vfx.BeginTelegraph(serverStartTime, serverImpactTime, synchronizedNow);

// 실제 타격 확정 수신 시
vfx.ConfirmImpact(serverHitTime, synchronizedNow);

// 표현 갱신 중
if (vfx.IsRunning)
{
    vfx.Tick(synchronizedNow);
}

// 취소 또는 인스턴스 반환 시
vfx.Stop();
```

01도 동일한 네 메서드를 `MucheolImpactVfxPlayer`에 호출한다. 02~07은 반드시 루트 래퍼에 호출한다.

주의할 현재 동작:

- 미래의 `hitTime > now`, 시작 이전 타격, 중복 확정은 무시한다.
- 확정 없이 예정 타격 시각을 1초 초과한 `Tick`이 오면 자동 종료한다. 그 뒤 늦게 온 확정은 이미 종료된 인스턴스를 되살리지 않는다. 늦은 접속·이벤트 재정렬 복원은 바인더 담당자가 별도로 처리하고 검증한다.
- 한 인스턴스의 새 `BeginTelegraph`는 이전 재생을 지운다. 중첩 패턴이나 겹치는 04/05 꼬리는 별도 인스턴스/풀 슬롯으로 관리한다.
- `SamplePreview()`는 편집용이며 외부 재생 중 섞어 호출하지 않는다.
- 표시 번호 01~07을 서버 패턴 enum 숫자와 같다고 가정하지 않는다. 서버 패턴·각 타격 이벤트와 위 프리팹의 매핑은 보스 담당자가 확정해야 한다.
- 클라이언트 표현 컴포넌트이므로 전용 서버에서 재생하지 않는다. 네트워크 권위는 [기존 규칙](NETWORK_AUTHORITY.md)을 따른다.

## 5. 연결 후 확인 순서와 검증 범위

1. 개발 씬에서 각 번호의 예고·타격·소멸과 시간 변경을 확인한다.
2. 보스 바인더에 프리팹별 참조와 패턴/타격 매핑을 추가한다.
3. 서버 원점·방향·시각을 사용해 예고→확정→매 프레임 Tick→Stop을 연결한다.
4. 04/05 연속 타격, 06 경로 고정·끝 타격, 07 후방 안전구역을 판정 영역과 겹쳐 확인한다.
5. 취소·사망·중첩·확정 누락·지연·늦은 접속과 M2/M3를 검증한다.

2026-10-14 제작 시 검증: Unity 실제 C# 실행 검사 87개 통과(시간 변경·외부 확정·취소 등), 파티클 8,170표본 경계 침범 0, 개발 씬 Play 진입, 6종 캡처, Inspector 프로파일 동기화와 재생성 시 사용자 설정 보존 확인. 이는 Test Runner 87건이라는 뜻이 아니다. L2/증거 등급2, 조건부 통과다.

실제 보스 연결·서버 판정 일치·다인 접속·빌드·최소 사양 성능은 미검증이며, 시각 품질은 1차 검토본이다. 이번 사용 문서는 API 정적 대조로 확인했다.

## 6. 지면 강타 변형 (02·04·07 `_Slam`)

`PF_Mucheol_02_Slam`, `PF_Mucheol_04_Slam`, `PF_Mucheol_07_Slam`은 02·04·07 원본을 복사해, 예고선(`warning`)은 그대로 두고 확정 타격 때의 지면·충격파·파편 연출을 용암 지면 강타(바위가 가장자리로 튀며 균열 지면이 드러나는 연출)로 바꾼 변형이다. 원본 프리팹은 그대로 남아 있다.

- 02·04는 반경 5m 원형, 07은 반경 44m·315도(후방 45도 비움, 로컬 -Z 중심)이다. 07은 5m 연출을 루트 스케일 약 8.8배로 키웠다.
- `MucheolBatchVfxPlayer`의 선택 필드 `_groundSlam`(`MucheolGroundSlamVfx`)에 연결한다. 비어 있으면 기존 동작과 같다. 확정 타격(`ConfirmImpact`)의 경과 시간부터 재생하고 `Stop()`에서 끈다. 슬램 길이는 약 2.7초이며 `PreviewDuration`에 반영된다.
- `PF_MucheolBoss`의 `MucheolPatternVfxView`가 02·04·07 칸에 이 `_Slam` 프리팹을 쓴다. 되돌리려면 같은 칸에 원본을 다시 넣는다.
- 에셋은 `Assets/01.Scripts/05.Art/VFX/Mucheol/GroundSlam/`에 있다(셰이더 `GroundSlamLayer`, 재질 `GS_*`, 바위 메시, 애니메이션 `AN_GroundSlam`). 70도 부채꼴 슬램은 `PF_Mucheol_FanSlam70.prefab`(01 부채꼴 사본에 슬램을 붙인 것)이며, 템플릿 `FanSlam70`(`EMucheolVfxTemplate` 7번, `MucheolPatternVfxView._templates` 8번째 칸)으로 P1 P02와 P2 P02의 첫 단계만 쓴다. 다른 부채꼴 패턴은 기존 `Fan01`을 그대로 쓴다. 이 프리팹은 루트를 비균일 스케일(폭 x 0.4043)로 쓰므로 `MucheolGroundSlamVfx`의 `_cancelParentScale`이 슬램을 z 스케일 기준 균일 크기로 되돌린다. 하드는 부채꼴 1.4배, 원형 1.3배가 뷰의 크기 배율로 그대로 곱해진다.
- 사전 로딩 프리팹 `PF_Prewarm_MucheolPatternVfx`는 아직 원본 02·04·07을 가리킨다. 슬램 쪽 셰이더·재질은 첫 재생에서 로드된다.

상태: 구현됨·실행 미검증. 에디터에서 시각을 정해 렌더한 정지 확인과 CPU 시간 측정(프레임당 약 0.4~0.5ms, GC 할당 없음)만 했다. Play 모드 연속 재생, 빌드, 서버 확정 타격 연결, 다인 접속, GPU·겹침 비용, 사전 로딩은 미검증이다. 용암 균열이 후반에도 밝게 남는 등 시각 품질은 1차 검토본이다.

구현 위치: `Assets/01.Scripts/05.Art/VFX/Mucheol/Batch/MucheolBatchVfxPlayer.cs`, 공통 재생기 `MucheolImpactVfxPlayer.cs`, 설정 `MucheolPatternVfxSettingsSO.cs`, 편집 도구 `Assets/Editor/MucheolBatch/`.

## 7. 착탄 VFX 교체 (판 조각 크레이터와 고리 충격파)

6장의 용암 지면 강타 연출 중 확정 타격 레이어는 새 착탄 VFX가 대신한다. 서버가 타격을 확정한 순간 `MucheolPatternVfxView`가 아래 프리팹을 그 자리에서 재생한다. 예고선(`warning`)과 재생기 타이밍은 기존 `_Slam`/원본 프리팹이 그대로 맡는다.

| 패턴 | 템플릿 | 새 VFX |
|---|---|---|
| P1 P02, P2 P02 첫 단계 | `FanSlam70` | 부채꼴 크레이터(`MucheolFanImpactView`), 각도 70도, 반경 5m(하드 7m) |
| P1 P04 | `Circle02` | 고리 충격파(`MucheolWaveImpactView`)와 360도 크레이터, 반경 5m(하드 6.5m) |
| P2 P04 | `SmallWave04` | 고리 충격파만 |
| P2 P04 | `LargeWave05` | 고리 충격파와 360도 크레이터 |
| P2 P07 | `Battlefield07` | 서버가 알린 반경(44m)과 각도(315도)로 고리 충격파와 크레이터. 후방 45도는 비운다 |

- 팔 휘두르기와 연타(P1 P01, P1 P05, P2 P01, P2 P05)에는 넣지 않는다. 팔 궤적 파티클과 빨간 예고 번쩍임만 쓴다.
- 재생기는 기준 반경 8m에서 판 조각과 고리를 만들고 반경 비율로 키운다. 반경이 커져도 판 개수는 거의 같고 판이 커진다.
- 프리팹은 `Assets/02.Prefab/VFX/Mucheol/FanImpact/Prefabs/`의 `PF_VFX_MucheolFanImpact`, `PF_VFX_MucheolWaveImpact`다. `PF_MucheolBoss`의 `MucheolPatternVfxView`에 `_fanImpactPrefab`, `_waveImpactPrefab`으로 연결한다. 비우면 새 VFX만 안 나오고 나머지는 같다.
- `PF_Mucheol_FanSlam70`, `PF_Mucheol_02_Slam`, `PF_Mucheol_04_Slam`, `PF_Mucheol_05`, `PF_Mucheol_07_Slam`은 예고 레이어만 남기고 기존 타격 레이어와 지면 강타 연결을 비웠다. 되돌리려면 이 프리팹들을 git에서 복구하고 보스 프리팹의 두 필드를 비운다.
- 프리팹 재생성과 연결은 에디터 메뉴 `Giwoong/VFX/Build Fan Impact`, `Giwoong/VFX/Wire Fan Impact`다(`Assets/Editor/Art/MucheolFanImpactBuilder.cs`, `MucheolFanImpactWiring.cs`).
- 사전 로딩 프리팹 `PF_Prewarm_MucheolPatternVfx`에는 아직 등록하지 않았다. 첫 재생에서 판과 입자를 만들어 한 번 끊길 수 있다.

상태: 구현됨·실행 미검증. EditMode 269건 통과와 개발 씬(`Dev_MucheolPatterns`)의 이벤트 재생기로 P1 P04, P2 P04, P2 P07의 생성과 서버 값 전달(반경 44, 각도 315)을 확인했다. 서버 확정 타격으로 이어진 실전투, 하드 범위, 다인 접속, 빌드, 프레임과 GC 비용(판마다 `GameObject`를 새로 만든다)은 미검증이다.
