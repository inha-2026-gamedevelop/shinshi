# 월백 여우불 VFX 사용 안내

2026-10-14. 승인한 Blender v03 형상을 재사용한 URP 클라이언트 표현이다. 서버 피해 판정·보스 연결·물리 충돌은 포함하지 않는다.

## 에셋

- 기존 통합 프리팹: `Assets/02.Prefab/VFX/Wolbaek/Foxfire/PF_WolbaekFoxfire.prefab`
- 재생기: `Shinshi.Art.WolbaekFoxfirePlayer`
- 설정: `Assets/01.Scripts/05.Art/VFX/Wolbaek/Foxfire/SO_WolbaekFoxfire.asset`
- 메시·재질·마스크: `Assets/01.Scripts/05.Art/VFX/Wolbaek/Foxfire/{Models,Materials,Textures}/`
- 개발 씬: `Assets/00.Scenes/Dev_VFX/Dev_Wolbaek_Foxfire.unity`. Play하면 아래 다섯 개별 프리팹이 병렬 재생된다. `99.Dev` 드라이버는 실전 프리팹에 없다.

개별 프리팹 폴더는 `Assets/02.Prefab/VFX/Wolbaek/Foxfire/`다. 일반(Regular) 프리팹이며 필요한 hierarchy만 포함하고 메시·재질은 공유한다. 기존 통합 프리팹을 의존하는 Variant가 아니다.

| 프리팹 파일 | 포함 상태 | 기본 로컬 Preview | 독립 SO 파일 |
|---|---|---|---|
| `PF_WolbaekFoxfire_Flight.prefab` | Flight | Flight만 | `SO_WolbaekFoxfire_Flight.asset` |
| `PF_WolbaekFoxfire_Locked.prefab` | Locked | Blue Locked만 | `SO_WolbaekFoxfire_Locked.asset` |
| `PF_WolbaekFoxfire_Impact.prefab` | Blue/Gold/Red Impact | Blue Impact만 | `SO_WolbaekFoxfire_Impact.asset` |
| `PF_WolbaekFoxfire_GoldTarget.prefab` | GoldTarget | GoldTarget만 | `SO_WolbaekFoxfire_GoldTarget.asset` |
| `PF_WolbaekFoxfire_RedHazard.prefab` | RedHazard | RedHazard만 | `SO_WolbaekFoxfire_RedHazard.asset` |

개별 SO는 `Assets/01.Scripts/05.Art/VFX/Wolbaek/Foxfire/`에 있다. 최초 생성 때 통합 SO 값을 복사하며 이후 개별 시간 변경과 재생성이 다른 SO를 덮어쓰지 않는다. Impact의 세 색상은 Impact SO 하나를 공유한다. 자동 재생은 없으며 PlayMode의 `Preview Local Default`로 본인 상태만 검수한다. 통합 프리팹은 기존 Flight → Locked → Impact 미리보기와 API를 유지한다.

기존 `Shinshi/VFX/Particle` 셰이더를 재사용한다. HDR vertex color·alpha 마스크와 원본 조각별 형상을 보존했다. 실제 움직임은 구간별 GPU skinning이며 단일 Airflow blendshape는 재생에 쓰지 않는다. Blender 노드의 자동 변환을 가정하지 않았다. 기존 셰이더의 깊이 계산은 **원근 카메라와 URP depth texture를 요구**한다. Orthographic 카메라에서 효과 소멸을 실제 재현했다. `UniversalAdditionalCameraData.requiresDepthTexture`를 켠다. HDR/Bloom은 검수 씬의 별도 Volume이며 프리팹 의존성이 아니다.

Flight 머리는 로컬 `+Z`, 꼬리는 `-Z`다. Locked/Impact 원점은 지면 원의 중심이다. 잠금 이후 Tick이 고정 월드 anchor와 회전을 복원한다. 루트·부모 스케일 `1`을 미터 기준으로 사용한다. Collider/Rigidbody/NetworkIdentity가 없다.

## 외부 호출

시간은 **같은 기준점의 double 초 시계**다. effect/cast 식별과 인스턴스 선택은 호출자 책임이다. 시작이 반환한 `uint playbackId`를 보관한다. 풀 재사용 때 ID가 바뀌며 이전 ID의 Tick·확정·취소는 새 효과를 바꾸지 않는다.

아래 연속 전환 예시는 통합 프리팹에 해당한다. 개별 Flight는 `PlayFlight`/`SetFlightPose`, 개별 예고는 본인 variant의 `BeginTelegraph`, 개별 Impact는 **확정 이벤트에서만** `PlayImpact(anchor, variant, confirmedHitTime, now)`를 호출한다. 개별 예고에는 타격 메시가 없으므로 `ConfirmImpact`는 false를 반환한다. 실제 확정 때 예고 인스턴스의 `Stop(warningId)` 후 별도 Impact 인스턴스의 `PlayImpact`를 호출한다. Flight 종료도 호출자가 `Stop(flightId)` 후 예고 인스턴스를 시작한다. 미래 확정과 없는 상태/variant는 0 또는 false로 거절하며 진행 중인 유효 재생을 취소하지 않는다. 같은 Impact 프리팹에서 Blue/GoldTarget/RedHazard 타격을 선택할 수 있다.

```csharp
uint id = player.PlayFlight(position, rotation, startTime, now);
player.SetFlightPose(id, position, rotation); // Flight에서만 수용
player.LockAt(id, fixedAnchor, EWolbaekFoxfireVariant.Blue, warningStart, expectedHit, now);
player.SetGroundRadius(id, serverRadius);
player.Tick(id, now);
// 실제 확정 이벤트가 도착했을 때만 호출한다.
player.ConfirmImpact(id, confirmedHitTime, now);
player.Tick(id, now);
player.Stop(id);
```

고정 상태부터 시작하려면 `BeginTelegraph(anchor, variant, startTime, impactTime, now)`의 반환 ID를 사용한다. variant는 `Blue`, `GoldTarget`, `RedHazard`이며 타격도 해당 색상 레이어를 사용한다. 미래·시작 이전·중복 확정과 역행 Tick을 무시한다. **예정 시간만으로 자동 폭발하지 않는다.** 확정 누락은 `impactTime + ConfirmationGraceSeconds` 이후 정리한다. 창이 끝난 뒤의 확정은 기존 ID로 복원하지 않는다. 늦은 이벤트의 표시 유효성과 새 cue/인스턴스 재구성은 호출자가 결정하며 서버 판정 시각을 바꾸지 않는다.

`SetGroundRadius(id, radius)`는 원/4표식의 XZ 반경만 조절한다. 코어 높이·공중 꽃문양·마름모는 유지한다. 원 중심선 기본 반경은 Blue Locked `0.7865m`, Impact `0.88m`, Gold `0.55m`, Red `0.528m`다. 표식·꽃잎·불꽃은 중심선 밖으로 나갈 수 있으므로 이를 피해 경계로 판정하지 않는다.

`Completed(id)`는 정상 수명 또는 확정 누락 대기 종료 때 발생한다. `Stop()`은 전체 정리이며 풀 반환·비활성화 직전에 호출한다. 취소·OnDisable은 완료 이벤트를 보내지 않는다. `Stop(id)`는 오래된 취소를 거절한다. 반환할 때 호출자가 보관한 ID도 비운다.

## Inspector와 미리보기

등장, 흐름 주기/UV 속도·구간 변형 폭, 꼬리 지연 시간·순간이동 초기화 거리, 꽃잎 drift/상향 속도, 타격 조각 확산 속도, 이동 최대 대기 시간, 확정 grace, 확산/유지/소멸, 로컬 이동/잠금 시간·거리를 SO에서 조절한다. 시작 시 스냅샷하므로 재생 중 변경은 다음 재생부터 적용된다. SO를 공유하는 인스턴스는 다음 시작 때 같은 변경을 읽는다.

Flight는 `SetFlightPose` 후 `Tick(id, now)`가 최종 머리 위치를 256개 고정 크기 월드 경로 버퍼에 기록한다. 꼬리 각 구간은 다른 과거 시각의 위치와 경로 접선 frame을 따르며, 첫 이동 전의 정지 이력은 첫 접선으로 맞춘다. 낮은 pose 주기에서도 최근 경로 길이의 평균 속도로 추종량을 유지한다. 꼬리 지연 기본값은 `1.05s`, 순간이동 초기화 거리는 `4m`다. 초기 이력 부족은 같은 세대 첫 접선을 연장하며, 설정 거리 초과 이동·지연 시간 두 배를 넘긴 Tick 공백·잠금 전환·Stop·재사용은 이전 경로를 지운다. 이 경로는 시각 표현이며 네트워크 보간이나 서버 이동 판정을 대신하지 않는다.

Locked/Gold/Red의 지면 표식과 타깃 문양은 고정이다. 불꽃은 높이·원본 strand마다 위상이 다른 상향파로 변형된다. 꽃잎/sparks는 각 조각의 서로 다른 속도와 회전을 사용하고 꽃잎은 약한 시간차 잔상을 가진다. Impact는 중심 코어가 먼저 상승하고 주변 조각이 서로 다른 지연·방향·수명으로 분출·감쇠한다. 원 전체를 확대하는 방식은 사용하지 않는다. 메시 배열의 매 프레임 복사/생성을 하지 않지만 실제 GC·전장 중첩 성능은 아직 측정하지 않았다.

통합 프리팹의 `Preview()`와 `SamplePreview(elapsedSeconds, variant)`는 로컬 Flight → Locked → Impact → 소멸 검수 전용이다. Preview는 PlayMode 코루틴을 사용한다. SamplePreview는 진행 중인 외부 재생을 덮어쓰지 않는다. 실전 프리팹은 자동 재생하지 않으며 호출자가 Tick을 공급한다. DOTween 트윈은 캐싱하고 수동 시계로 샘플하며 취소/파괴 때 정리한다. 꽃잎·sparks는 저폴리 메시 표현이며 물리 입자가 아니다.

개별 프리팹의 Preview/SamplePreview는 직렬화한 기본 상태만 재생하고 설정 수명 뒤 정리한다. Locked/GoldTarget/RedHazard Preview에서 자동 폭발하지 않는다. `Shinshi/VFX/Wolbaek/Build Foxfire Individual Prefabs`는 보존된 통합 프리팹 형상을 사용해 개별 프리팹만 재생성한다. 이 메뉴는 독립 SO 값을 보존한다.

재생성 메뉴는 `Shinshi/VFX/Wolbaek/Build Foxfire`다. `.claude/docs/2026-10-14/wolbaek-foxfire-blender/export_unity.py`가 만든 메시 JSON이 필요하다. 원본 `.blend`와 재현 스크립트는 같은 `Assets/` 밖 폴더에 보존된다. 생성 hierarchy/mesh는 재생성되므로 수작업 수정은 사본에서 한다. 기존 SO 시간 값은 보존한다. 같은 경로의 미저장 검수 씬이 있으면 저장을 거절한다. 기존 통합/다른 담당자 씬을 저장하지 않는다.

## 실제 검증

19건 계약/8건 모션 검사는 v05 유동 구현 때 실행한 결과다. 이번 개별 프리팹 분리는 변경에 해당하는 66건과 실제 5개 프리팹 비교 캡처를 추가 실행했다.

| 주장 | 수준 | 증거 등급 | 실행 수단 | 실제 결과 | 상태 |
|---|---|---:|---|---|---|
| 외부 확정 누락·취소·이전 ID·시간 변경·반경 제어 | L2 | 2 | 실제 PlayMode API 검증 함수 | 19/19 PASS | 완료(검증됨) |
| sparse pose·곡선 지연·정지·순간이동·재사용 초기화 | L2 | 2 | 실제 20Hz pose/60Hz Tick 및 경로 검사 | 8/8 PASS | 완료(검증됨) |
| 5개 개별 PF 재생/Preview·미지원 API·취소·통합 호환 | L2 | 2 | 실제 PlayMode 개별 인스턴스 검사 | 66/66 PASS | 완료(검증됨) |
| 5상태 형태·실선 원·캡션 | L2 | 2 | 실제 Unity 원근 카메라 캡처 직접 확인 | 정상 표시 | 완료(검증됨) |
| 보스/네트워크 연결·M2/M3·Player 빌드·최소 사양 성능 | — | 0 | 미실행 | 요청 범위 밖/실행 미검증 | 미검증 |

최종 모션은 **PlayMode에서 24fps API 시간을 샘플링한 실제 Unity 카메라 렌더 프레임**이며 실시간 FPS 성능 영상이 아니다. 에디터 검수는 `WolbaekFoxfireMotionCapture.CaptureTour()`로 5상태를 크게 보여 준다. 같은 Editor 프레임의 반복 Camera.Render에는 촬영 인스턴스의 skin matrix 재계산을 켠다. Unlit 반투명 효과라 AO를 생략했다. 삼각형/renderer 수만으로 성능 통과를 주장하지 않는다. LOD·풀 상주 예산·최대 중첩 수는 실제 전장 화면 크기와 성능 측정 후 확정한다.

프리워밍 목록과 자동 소비 여부는 [맵 성능 준비](MAP_PERFORMANCE_PREPARATION.md#등록된-필수-대상--월백-여우불)를 따른다.
