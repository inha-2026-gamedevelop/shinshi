# 월백 워프 VFX 사용 안내

분해·재결합·착지 충격파를 독립 프리팹으로 제공한다. 서버 이동·피해 판정과 보스 네트워크 연결은 포함하지 않는다. Absent는 몸체가 사라지고 소수 꽃잎·광점이 감쇠하는 상태이며 별도 프리팹이 없다.

| 역할 | 프리팹 (`Assets/02.Prefab/VFX/Wolbaek/Warp/`) | 설정 (`Assets/01.Scripts/05.Art/VFX/Wolbaek/Warp/`) |
|---|---|---|
| 출발 분해 | `PF_WolbaekWarp_Breakup.prefab` | `SO_WolbaekWarp_Breakup.asset` |
| 착지 재결합 | `PF_WolbaekWarp_Reform.prefab` | `SO_WolbaekWarp_Reform.asset` |
| 착지 후 원형 예고·확정 충격파 | `PF_WolbaekWarp_Landing.prefab` | `SO_WolbaekWarp_Landing.asset` |

루트의 `Shinshi.Art.WolbaekWarpPlayer`를 사용한다. 루트 부모는 회전 0·스케일 1을 유지한다. 입력 위치와 Bounds는 월드 좌표이며, 인체 소스는 지면 피벗·높이 1.92m·폭 0.98m·깊이 0.5m다. 프리팹마다 SO가 독립적이다. 생성기는 기존 SO 값을 덮어쓰지 않는다.

```csharp
uint bodyId = breakup.PlayBody(castId, bodyBounds, started, now);
breakup.Tick(bodyId, now);

uint landingId = landing.PlayLandedWarning(castId, actualLanding, radius,
    warningStart, expectedHit, now);
landing.Tick(landingId, now);
landing.ConfirmStrike(landingId, confirmedHitTime, now);
landing.Stop(landingId);
```

`PlayBody`는 Breakup/Reform 전용이고 `PlayLandedWarning`·`ConfirmStrike`는 Landing 전용이다. 잘못된 종류·유한하지 않은 값·잘못된 시간·크기는 0/false로 거절한다. 재생마다 새 `PlaybackId`가 나오며 오래된 ID와 역행하는 `now`는 거절한다. 외부의 `castId`와 해당 인스턴스·PlaybackId 매핑은 호출자가 보관한다. 시작·예정·확정·현재 시각은 같은 외부 시계를 사용하고, 매 프레임 `Tick`을 호출한다. 취소·풀 반환 전 `Stop()`을 호출하며 비활성화도 렌더러와 입자를 정리한다. 정상 종료만 `Completed(id)`를 발생시킨다.

Landing은 **실제 착지 성공 이후**에만 호출한다. 후보 목적지나 착지 전 표식을 표시하는 API는 없다. P2P07의 현재 서버 값은 반경 5m·착지 후 예고 1.5초다. 반경은 원 중심선 기준이며 표식의 얇은 획은 양쪽으로 반경의 0.003배만큼 걸친다. 예고 만료만으로 충격파가 발생하지 않으며 예정 시각 + `ConfirmationGrace` 이후에는 숨긴다. 늦은 확정은 거절하므로 지연 복원은 호출자가 새 인스턴스/재생을 구성한다. 확정 입력 없이 로컬 피해나 타격 이벤트를 만들지 않는다.

몸체 표현은 외부 `Bounds`에 맞춘 꽃잎·안개뿐이며 **실제 캐릭터의 메시·렌더러·재질은 변경하지 않는다**. `PlayBody(..., neutralProxy: true)`는 개발용 중성 인체의 높이별 dissolve를 명시적으로 켠다. 실제 보스 모델의 dissolve·숨김·재결합 인계는 미연결이다. 호출자는 캐릭터 표현을 직접 소유하고 중첩 효과의 복원 경합을 관리해야 한다. 현재 서버에는 출발/착지 전 표현 이벤트가 없어 Breakup은 개발 시연용 외부 신호로만 재생한다.

SO의 `BodySeconds`(기본 0.95초), `ResidualSeconds`(1.4초), `ImpactSeconds`(0.65초), `FlowCycle`(1.5초), `PetalDrift`(0.65m), `MistOpacity`(0.8), `ConfirmationGrace`(0.5초)를 조절한다. 값은 재생 시작에 고정되므로 실행 중 변경은 다음 재생에 적용된다. 꽃잎은 몸체 크기를 바꿔도 원래 월드 크기를 유지하도록 보정한다. Body 전환이 끝나면 proxy와 키 큰 리본은 숨기고 소수 잔류 꽃잎·광점만 `ResidualSeconds` 동안 감쇠한다. Body의 `Completed`는 이 잔류까지 끝나는 기본 2.35초 시점에 발생한다. 캐릭터 표현 인계는 Body 전환 완료 시점에 호출자가 처리한다. `Stop`·비활성화·재사용은 잔류도 즉시 정리한다.

개발 씬은 `Assets/00.Scenes/Dev_VFX/Dev_Wolbaek_Warp.unity`다. `WolbaekWarpPreview`의 확정 신호와 중성 인체 인계는 개발 전용이며 서버 연결 증거가 아니다. `Assets/Editor/WolbaekWarp/WolbaekWarpBuilder.cs`는 보존된 Blender 내보내기 자료를 읽어 메시·프리팹을 재생성한다. 원본과 제작 스크립트는 Git 제외 `.claude/docs/2026-10-14/wolbaek-warp-blender/`에 있다.

중성 인체는 연결된 부드러운 메시로 만들고 Blender BVH 반구 광선으로 버텍스 AO를 베이크했다. 발광 안개·꽃잎·충격파는 Unlit 표현으로 AO를 생략한다. 개발 인체는 깊이를 기록하는 반투명 `SH_WarpProxyDissolve.shader`로 회백 음영과 밝은 dissolve 가장자리를 표시한다. 기존 Particle에는 해당 깊이·3D clip 기능이 없어 전용 셰이더를 작성했으며 공유 셰이더는 보존했다. 꽃잎은 4·6.5·8.5cm 계열의 곡면/갈라진 끝, 리본은 서로 다른 길이의 하체 S곡선과 짧은 어깨층, 발밑은 청백 core·얇은 안개·광점이다. 충격파는 `SB_Rainbow_1_Shockwave_add_soft`의 메시/UV/시스템을 재사용하고 Warp 복제재질의 주 파동·희미한 보조 2층으로 구성한다. 원본 전체 21개 시스템과 공유 재질은 수정하지 않는다.

개발 씬은 전용 `VP_WarpPreview.asset`의 Bloom을 사용한다. 공유 Beams 프로필은 보존했다. 실제 HDR 캡처 ON/OFF 차이로 Bloom 적용을 확인했고, 2560×1440 렌더를 1280×720으로 내려 꽃잎 윤곽 계단을 줄였다. Collider·피해 판정은 없으며 LOD·중첩 성능·Player 빌드는 미검증이다. 전체 메시 상한은 Body 프리팹당 46,586삼각형/23렌더러, Landing 메시 7,854삼각형/8렌더러와 PS 3개이며 동시에 모두 표시되는 수나 성능 통과 근거는 아니다.

프리워밍 대상과 GUID·의존성은 [맵 준비 문서](MAP_PERFORMANCE_PREPARATION.md#등록된-필수-대상--월백-워프)에 등록한다. 현재 공용 `PrewarmCatalog.asset`/`ClientPrewarmRunner`는 존재하지만 이 3PF는 자동 소비 목록에 미등록이며 `IPrewarmTarget` 연결도 별도 담당 작업이다. 대상 문서 등록은 GPU 워밍업 완료를 뜻하지 않는다.

2026-10-14 아트 보완 후 Unity Editor에서 3PF와 독립 SO를 확인했다(L1). 실제 PlayMode 검사 28/28 통과: 기존 확정/취소/세대 계약, 정상 잔류→완료, 잔류 중 취소·비활성화·재사용 정리, winding/법선 일치(L2). Breakup→Absent→Reform→Landing 근접→전체 반경을 실제 PlayMode 시간 샘플/Camera.Render로 1280×720·24fps·192프레임·8초 영상으로 검수했다. 실시간 FPS 성능 측정은 아니다.

현재 형태는 원화 방향의 조건부 구현 검토본이다. 개발 인체의 해부학적 세부·광산란·충격파 부피는 원화와 차이가 있으며 유사도 90% 달성 수치는 검증하지 않았다. 실제 보스 dissolve·네트워크 연결·M2/M3·Player 빌드·성능·런타임 프리워밍은 미검증/미연결이다.
