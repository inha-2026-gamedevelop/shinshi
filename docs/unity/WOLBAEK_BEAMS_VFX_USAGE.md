# 월백 요력파·지속빔 VFX 사용 안내

2026-10-14. **Unity 재사용 프리팹 1차 구현 검토본, 시각 판정 CONDITIONAL**이다. 컨셉의 넓은 전진 파면·반투명 청백 안개·중앙 코어·분홍 꽃잎·별 5선·격자 현재/다음 선을 구현했다. 원화의 풍성한 부피와 넓은 빛 퍼짐에는 차이가 남아 있으며 수치 유사도 달성을 주장하지 않는다. 실제 보스/네트워크 연결·빌드·최소 사양 성능은 미검증이다.

## 에셋과 미리보기

프리팹 폴더: `Assets/02.Prefab/VFX/Wolbaek/Beams/`. 각 프리팹은 독립 `WolbaekBeamPlayer`와 SO를 사용한다.

| 프리팹 | 용도 | 동시 선 슬롯 | 설정 SO |
|---|---|---:|---|
| `PF_WolbaekBeam_Release.prefab` | 직사각 단발 전진 파면 | 1 | `SO_WolbaekBeam_Release.asset` |
| `PF_WolbaekBeam_Sustain.prefab` | 이지 평행 2빔 등 지속 선 | 2 | `SO_WolbaekBeam_Sustain.asset` |
| `PF_WolbaekBeam_Star.prefab` | 확정 5선과 다음 단계 5선 ghost | 10 | `SO_WolbaekBeam_Star.asset` |
| `PF_WolbaekBeam_Grid.prefab` | 개별 선 예고·확정·즉시 안전 전환 | 12 | `SO_WolbaekBeam_Grid.asset` |

SO·메시·재질·텍스처·런타임은 `Assets/01.Scripts/05.Art/VFX/Wolbaek/Beams/`에 있다. 전용 씬 [Dev_Wolbaek_Beams.unity](../../Assets/00.Scenes/Dev_VFX/Dev_Wolbaek_Beams.unity)를 열어 Play하면 8초 로컬 예시가 반복된다. 예시 배치·회전·시간 압축은 [WolbaekBeamPreview.cs](../../Assets/01.Scripts/99.Dev/WolbaekBeams/WolbaekBeamPreview.cs)에만 있고 배치용 프리팹에 개발 스크립트가 포함되지 않는다. 실제 전장 치수와 서버 스케줄로 사용하지 않는다.

`Shinshi/VFX/Particle` 공용 셰이더를 수정 없이 재사용했다. 원근 카메라와 URP depth texture가 필요하다. 개발 씬의 Unlit 슬레이트 바닥·HDR Bloom/ACES는 비교 지원용이다. AO/PBR/Collider는 반투명 Unlit 표현 프리팹에 적용하지 않는다. 서버 판정을 계산하는 Collider·네트워크 컴포넌트는 없다. 별도 LOD/거리별 장식 품질 단계는 미구현이다.

## 좌표와 Inspector

`WolbaekBeamShape(start, end, width, arenaCenter, arenaRadius)`는 **월드 좌표**를 받는다. 시작 중심부터 끝 중심까지 XZ 진행 방향, 중심선 양옆 `width/2`가 경계다. 끝점 Y는 시작점 Y로 평탄화한다. 프리팹 루트/상위 Transform은 위치 0·회전 identity·스케일 1로 사용한다. 크기는 루트 스케일 대신 shape의 폭과 끝점으로 전달한다.

Release는 직사각형이다. Sustain/Grid는 서버가 준 **전장 중심·양수 반경을 필수로 전달**하여 폭 띠와 원 전장의 교집합을 표현한다. 중심선 chord만 전달하고 반경을 생략하면 모서리가 원 밖에 표시될 수 있다. 반경 0은 원 clipping 없는 직사각형 모드다. clipping은 표현 정점만 바꾸며 판정 계산이 아니다. 테두리 획 중심이 판정 경계이고 획 두께는 약 1cm다. Bloom/꽃잎의 장식 밝기를 판정으로 읽지 않는다.

SO의 `AppearSeconds`, `FadeSeconds`, `FlowCycle`, `Height`, `VeilOpacity`, `CoreOpacity`, `ConfirmationGrace`를 Inspector에서 조정한다. 값은 `BeginCast`에서 스냅샷하므로 진행 중 SO 편집은 다음 cast부터 반영된다. 짧은 활성 수명에서는 등장/소멸 구간을 수명의 20%/30% 이내로 줄여 짧은 확정 선도 보이게 한다.

`PreviewWarning/Active/Width/Length`는 호출자가 손으로 미리보기를 만들 때 선택할 기본값이다. 자동 스케줄이나 전투 값이 아니다. 전용 개발 씬의 압축 패턴은 별도 예시 값을 사용한다. 외부 예고·활성·종료 시각과 폭/길이는 아래 API 인수가 우선한다.

## 외부 재생 API

모든 시각은 동일한 외부 `double` 시계로 전달한다. 각 인스턴스의 현재 `castId` 라우팅은 호출자 책임이다. 반환된 `playbackId`는 재사용 세대이며 모든 후속 호출에 보관한다. `lineId`는 cast 안에서 선을 식별하고 `revision`은 같은 선의 단조 증가 버전이다.

```csharp
uint playbackId = player.BeginCast(castId, serverNow);
WolbaekBeamShape shape = new WolbaekBeamShape(start, end, width, arenaCenter, arenaRadius);
bool warned = player.Warn(playbackId, lineId, revision, shape,
    warningStart, expectedActive, expectedEnd, serverNow);

// 실제 서버 활성/타격 확정 이벤트에서만 호출한다
bool activated = player.ConfirmActive(playbackId, lineId, revision,
    confirmedTime, confirmedEnd, serverNow);

player.Tick(playbackId, serverNow);
```

- `Warn`은 예정 시각이 지나도 자동 활성하지 않는다. 예고만 남고 예정 활성 시각+`ConfirmationGrace`를 넘으면 숨긴다. 해당 유예를 지난 확정은 거절한다.
- `ConfirmActive`는 이미 받은 선·같은 revision·예고 이후의 확정·현재 이하 시각만 허용한다. 종료 시각부터 선을 즉시 숨긴다. 피해/주기 타격을 로컬에서 만들지 않는다.
- `UpdateGeometry(playbackId, lineId, revision, shape, now)`는 외부 확정 끝점을 즉시 적용한다. 예고 중에는 Release 조준 추적만 허용하고 Sustain/Star/Grid 예고는 고정한다. 활성 후에는 끝점 갱신을 허용한다. 각도 보간·자동 회전은 없다.
- `Tick`은 호출자 프레임에서 진행한다. 역행 `now`, NaN/Infinity, 잘못된 폭/길이, 오래된 playback/revision은 거절한다. 같은 시각의 여러 호출은 허용한다. `UpdateGeometry`도 시계 기준을 갱신한다.
- `StopLine(playbackId, lineId, revision)`은 해당 선을 즉시 숨기고 같은 revision의 재경고를 거절한다. 새 단계는 더 높은 revision을 사용한다. `VisibleLineCount`는 취소 즉시 갱신된다.
- 전체 취소/풀 반환은 `Stop(playbackId)` 또는 `Stop()` 후 비활성화한다. `OnDisable`도 정리한다. 다음 `BeginCast`는 예전 선·알파·취소 이력을 지우고 세대를 바꾼다.

`IsRunning`은 cast가 입력을 받을 수 있다는 의미이고 가시 선이 없어도 유지될 수 있다. cast 종료와 풀 반환은 호출자가 명시적으로 `Stop`한다. 한 cast당 서로 다른 lineId의 취소/만료 이력을 32개까지 보관한다. 슬롯 또는 이력 한도를 넘으면 `Warn`은 false다. false를 무시해 재생 성공으로 처리하지 않는다. 네트워크 역전 순서를 복원하는 큐는 없으며 호출자가 Warn→Confirm 순서로 정렬한다. 늦은 복원은 현재 서버 상태를 확인해 새 playback/higher revision으로 재구성하고 이미 지난 타격을 새 공격으로 만들지 않는다.

## 패턴별 호출 계약과 확인한 근거

| 패턴 | 외부 입력으로 표현할 것 |
|---|---|
| P1P06 `wolbaek_p06` / `wolbaek_hard_p06` | 길이 14m, 폭 이지 4m/하드 4.5m, 예고 1.6초·추적 0.6초 현행 값. 원점/방향/폭/길이를 별도 전달하고 서버 발동 확정부터 단발 파면 재생 |
| 이지 P2P01 `wolbaek_ph2_p01` | 전장 중심, 별 반경 14m, 5 vertices·step 2·폭 1.5m, 예고 2초·활성 6초. 예고 방향 고정. 확정 활성 시각부터 서버 부호의 ±20°/초를 호출자가 끝점 갱신으로 전달 |
| 하드 P2P01 `wolbaek_hard_ph2_p01` | 0°→30°→60°의 세 정확한 끝점 집합. 현행 타격 T+2/2.8/3.6초. `OnStarStageStruck` 확정마다 해당 stage revision을 Warn→Confirm. 연속 회전 입력을 만들지 않음 |
| 이지 P2P02 `wolbaek_ph2_p02` | 실제 평행 2선, 중심 ±5m·폭 2m·예고 1.5초·활성 4초. 두 lineId의 활성/종료를 독립 전달. 0.5초 주기 피해는 서버 소유이며 재생기가 추정하지 않음 |
| 하드 P2P02 `wolbaek_hard_ph2_p02` | 가로 12선 이후 세로 12선. 선 ID/축/끝점/폭/예고·타격·종료를 외부 전달. 현재 활성과 가장 이른 다음 예고만 강조. 타격이 끝난 선은 즉시 숨김 |

격자 현행 폭·간격 3m, 타격 간격 0.5초·축 간격 1.5초다. 가로 i는 T+1+0.5i, 세로 i는 T+8+0.5i, 각 예고는 자기 타격 1초 전이다. 이 숫자를 런타임에서 스케줄하지 않는다. 주기 확정에 별도 시각 펄스가 필요하면 호출자가 같은 lineId의 더 높은 revision을 실제 확정 시각에서 Warn→Confirm하고 서버 종료 시각을 유지할 수 있다.

P1P06 액체 손(2026-10-25)은 위 Release 빔을 대신하는 선택 표현이다. `WolbaekPatternVfxView`의 `_liquidHand`가 연결되고 `_liquidHandReplacesBeam`이 켜져 있으면(기본값) P06은 Release 빔을 쓰지 않고, 다른 패턴과 같은 기본 붉은 지면 예고와 `WolbaekLiquidHandView`만 낸다. 서버 타격 확정에서 `Play(origin, direction, length, width)`를 부르고, 재접속 복원 중의 확정은 이미 지난 타격이라 그리지 않는다. `_liquidHand`가 비어 있거나 옵션을 끄면 위 Release 빔 계약 그대로다. 프리팹·FBX·재질·노이즈 텍스처는 Git 제외인 `Assets/99.ThirdParty/VFX/WolbaekP06LiquidHand/`에 있어 공유 에셋으로 따로 받아야 한다. 정점 5,171·삼각형 9,976이고 Release 빔은 삼각형 4,716이다. 확인한 범위: 시험 씬 PlayMode에서 서버 P06 발동마다 `Play`가 길이 14·폭 4로 불리는 것과 시험 씬 정지 장면의 화면. 확인하지 못한 범위: 실제 발동 중 화면, 기본 붉은 예고 화면, 하드 P06, 재접속 복원, 프로파일러 실측.

읽어 확인한 원본은 아래와 같으며 변경하지 않았다.

- [BossLineBlastPattern](../../Assets/01.Scripts/98.Servers/03.AI/Boss/BossLineBlastPattern.cs), [BossTelegraphStart](../../Assets/01.Scripts/03.AI/Boss/Rules/BossTelegraphStart.cs), [BossPatternCatalog.asset](../../Assets/09.Data/AI/BossPatternCatalog.asset).
- [BossStarGeometry.GetBeam](../../Assets/01.Scripts/03.AI/Boss/Rules/BossStarGeometry.cs), [BossStarStagePattern](../../Assets/01.Scripts/98.Servers/03.AI/Boss/BossStarStagePattern.cs), [BossStarStageStart](../../Assets/01.Scripts/03.AI/Boss/Net/BossStarStageStart.cs).
- [BossBeamGeometry.Contains / TryGetCenterLineChord](../../Assets/01.Scripts/03.AI/Boss/Rules/BossBeamGeometry.cs), [BossBeamPattern](../../Assets/01.Scripts/98.Servers/03.AI/Boss/BossBeamPattern.cs).
- [BossGridBeamLayout](../../Assets/01.Scripts/98.Servers/03.AI/Boss/BossGridBeamLayout.cs), [BossGridBeamPattern](../../Assets/01.Scripts/98.Servers/03.AI/Boss/BossGridBeamPattern.cs), [BossGridBeamView](../../Assets/01.Scripts/03.AI/Boss/View/BossGridBeamView.cs).

## 제작·검증·준비 범위

원본과 재현 스크립트는 Git 제외 `.claude/docs/2026-10-14/wolbaek-beams-blender/`에 있다. `build_beams.py`와 `wolbaek_beams_v02.blend`, `beam_geometry.json`을 보존했다. Blender node 자동 변환을 가정하지 않고 [WolbaekBeamBuilder.cs](../../Assets/Editor/WolbaekBeams/WolbaekBeamBuilder.cs)가 native Mesh·구간별 2-bone weights·Release 폭 방향 파면·Unity mask/material을 생성한다. 재생은 짧은 안개 면의 구간별 위상/혼합 유동과 개별 꽃잎 회전을 사용하며 지면 경계는 고정한다. `Tick`은 메시 복사/새 메시 생성을 하지 않고 `SetGeometry`의 clipping 버퍼/메시는 최초 사용에 캐시한다. GC·성능 실측 통과를 의미하지 않는다.

생성 메뉴 `Shinshi/VFX/Wolbaek/Build Beams`는 해당 전용 에셋/씬을 재생성한다. 사용자 SO는 보존한다. 검수 씬이 dirty면 먼저 보존하고 실행한다. 다른 전장 씬에서 사용하려면 그 씬 소유권과 통합 계약을 별도로 확인한다.

실행 근거: `verification_v09.txt` **PlayMode 70/70 PASS**(4PF 공통 반복 포함), `asset_validation_v09.txt` 누락 참조 0, `dependencies_v09.tsv` 프리팹별 재귀 의존성 16개/전체 고유 23개. 정상·외부 확정 누락·취소·세대/revision·풀 재사용·폭/길이·고정 경계/내부 유동·Sustain/Grid 원 교집합·Grid 다음 강조/지난 선 안전·32개 제한을 확인했다.

실제 Unity `beams_unity_v09.png` 1920×1080과 `beams_unity_v09_24fps.mp4` H.264 960×540, 24fps, 144프레임, 6초를 확인했다. 영상은 실제 PlayMode API를 시간 샘플하여 HDR Camera.Render한 보드의 각 패널을 crop한 Release→Sustain→Star→Grid 순차 검토 영상이다. 실시간 FPS 측정이 아니다. 부모 시각 검수는 목표 방향 개선을 수용했고 원화 부피·빛 퍼짐 차이를 조건부로 남겼다.

한 활성 선은 4 Renderer/4,716 triangles다. 예고는 Ground 1 Renderer/132 triangles만 표시한다. 통상 Star 활성 5+ghost 5는 25 Renderer/24,240 triangles, Grid 활성 1+다음 1은 5 Renderer/4,848 triangles다. 프리팹의 전체 슬롯 합은 실제 동시 가시 비용과 다르며 상주 Transform·skinning·오버드로/GC·첫 사용·최소 사양 Player 비용은 미측정이다.

프리워밍 목록은 [MAP_PERFORMANCE_PREPARATION.md의 월백 빔 등록절](MAP_PERFORMANCE_PREPARATION.md#등록된-필수-대상--월백-요력파지속빔)을 따른다. 문서 등록이며 이 빔의 자동 로더 소비 연결·GPU/PSO 워밍업은 이 작업에서 검증하지 않았다.
