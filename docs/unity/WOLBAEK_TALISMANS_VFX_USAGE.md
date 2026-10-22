# 월백 HP75% 사방 부적진 VFX 사용 안내

2026-10-14. 부적진의 등장/대기, 공용 점유 진행, 초기화, 서버 성공 후 상승/소멸을 표시하는 Art 전용 프리팹이다. 피해, 참가자 배정, 점유, 성공 판정은 하지 않는다. 2026-10-22 정적 대조: 서버 DTO·리스너, `BossState`의 사건·복원과 `BossNetworkViewBinder`→`WolbaekTalismanView` 연결이 존재한다. 실제 다인 기믹 수용은 별도 검증이다.

## 프리팹과 설정

폴더: `Assets/02.Prefab/VFX/Wolbaek/Talismans/`. 네 프리팹은 Regular 에셋이며 각각 독립 `WolbaekTalismanSettingsSO`를 사용한다. 메시와 재질은 공유한다. 문양은 외부 입력으로 교체할 수 있다.

| 프리팹 | 기본 문양 | 기본 시안 색 | 독립 SO |
|---|---|---|---|
| `PF_WolbaekTalisman_Moon.prefab` | 초승달 | 청백 | `SO_WolbaekTalisman_Moon.asset` |
| `PF_WolbaekTalisman_Flower.prefab` | 꽃 | 연분홍 | `SO_WolbaekTalisman_Flower.asset` |
| `PF_WolbaekTalisman_Star.prefab` | 별 | 연금색 | `SO_WolbaekTalisman_Star.asset` |
| `PF_WolbaekTalisman_Diamond.prefab` | 마름모 | 연보라 | `SO_WolbaekTalisman_Diamond.asset` |

코드, SO, 메시, 텍스처, 재질은 `Assets/01.Scripts/05.Art/VFX/Wolbaek/Talismans/`에 있다. `WolbaekTalismanPlayer` Inspector에서 SO의 등장/초기화/성공 상승/소멸 시간, 안개 주기, 꽃잎 주기와 이동량, 안개 투명도를 직접 조절한다. 변경은 다음 `Begin`부터 적용한다. 생성 메뉴는 기존 SO 값을 보존한다. 같은 SO를 여러 인스턴스가 참조하면 다음 재생부터 모두 영향을 받는다.

v04는 작은 분홍 꽃잎, 종이 섬유와 해진 가장자리, 필획 대비, 부드러운 연무, 성공 상승 잔상과 발밑 광원을 보완했다. 추가 표현 설정 11개는 아래와 같다. 아래 선언 기본값을 네 SO의 실제 로드값과 대조하고 Unity에서 직렬화해 저장했다. 기존 에셋의 Inspector 조정값은 재빌드로 덮지 않는다. 시간이나 진행 판정 대신 로컬 표현만 바꾼다.

| SO 읽기 프로퍼티 | 선언 기본값 | 조절할 표현 |
|---|---|---|
| `PaperOpacity` | 0.62 | 부적 종이의 투명도 |
| `RuneStrength` | 1.4 | 종이 필획과 떠 있는 문양의 발광 강도 |
| `PetalTint` | HDR RGBA (1.5, 0.85, 1, 1) | 꽃잎의 기본 연분홍 색과 알파 |
| `AssignedPetalTintShare` | 0.25 | 꽃잎 색에 외부 배정 색을 섞는 비율 |
| `GroundLightStrength` | 0.7 | 지면 꽃잎, core, 진행 띠의 발광 강도 |
| `GroundLightIntensity` | 0.6 | 선택적 Point 광원의 강도. 0이면 광원을 끈다 |
| `GroundLightRange` | 2.3m | Point 광원의 영향 반경. 점유 반경과 별개다 |
| `AirMistOpacity` | 0.12 | 대기 중 공중 잔상의 투명도 |
| `SuccessMistOpacity` | 0.45 | 외부 Success 이후 펼쳐지는 잔상의 투명도 |
| `SuccessRise` | 2.4m | 성공 때 꽃잎과 광점이 올라가는 거리 |
| `PaperSuccessRise` | 1.4m | 성공 때 종이, 필획, 떠 있는 문양이 올라가는 거리 |

구조물당 `Talisman_FootLight` Point 광원 하나가 돌기둥과 주변 바닥에 배정 색을 비춘다. 한 기믹의 네 구조물 기준 최대 4개이며 그림자는 사용하지 않는다. 초기 상태와 `Stop()` 뒤에는 비활성이고 강도는 0이다. 재생 중 초기화/소멸에 맞춰 감쇠하며, `GroundLightIntensity = 0`으로 추가 광원 없이 사용할 수 있다. `GroundLightStrength`는 발광 메시 설정이므로 이 광원 끄기와 혼동하지 않는다.

`Assets/00.Scenes/Dev_VFX/Dev_Wolbaek_Talismans.unity`를 열고 Play하면 16초 개발 큐가 반복된다. 네 구조물, 고정 반경 3m, 등장/대기, 40% 진행 후 초기화, 재점유, 명시 성공 큐를 보여준다. 개발 큐는 `99.Dev/WolbaekTalismans/`에만 있고 배치용 프리팹에는 포함되지 않는다. 시안의 연보라를 현행 서버 Green으로 해석하지 않는다.

## 현행 서버 계약과 연결 경계

확인 근거는 `WolbaekTalismanFormation`, `WolbaekTalismanDevCatalog`, `WolbaekTalismanStart/Structure/Snapshot`, `IWolbaekTalismanListener`다.

- 원점은 기믹 진입 시 확정한 전장 중심이다. 보스 위치나 복제 이동을 따라가지 않는다. 현재 구조물 거리는 10m, 점유 반경은 3m지만 실제 재생은 각 `Structure.Center/Radius`를 받는다.
- `DirectionIndex`는 0 동/+X, 1 북/+Z, 2 서/-X, 3 남/-Z다. 색과 방위는 고정 대응하지 않는다. 서버가 색과 참가자를 섞으며 `AssignedCombatId`는 사망/재접속 이후에도 유지된다.
- 실제 구조물 수는 1~4다. `Start.Structures.Count`만큼 사용한다. 2인일 때는 반대편 2곳이다.
- 현행 서버 색은 Red=0, Blue=1, Yellow=2, Green=3이고 `SymbolId=(int)Color`다. Art 문양 enum의 순서는 Moon, Flower, Star, Diamond이므로 숫자를 직접 캐스팅하지 않는다. 권장 아트 대응은 0 Flower, 1 Moon, 2 Star, 3 Diamond이다. 서버 Green의 Diamond는 초록/민트 계열로 전달한다. 프리팹 기본 색은 컨셉 검수용 값이다.
- 점유는 참가자 전원이 동시에 자기 원 안에 살아 있고 접속 중인 공용 유지 상태다. 개별 기둥마다 다른 점유 비율이 제공되는 계약이 아니다. 현재 유지 5초, 전환 3초, 반격 6초 값은 서버 입력을 사용한다.
- 공용 유지 시작/초기화/성공은 서버 알림이 원본이다. 단순 피격 자체는 초기화 사유가 아니다. 이탈, 사망, 연결 끊김은 초기화된다.
- 고리/직선 공격은 별도 `WolbaekTalismanAttack`의 중심, 방향, 치수와 예고/판정 시각을 사용한다. 이 구조물 VFX는 공격 예고나 피해 판정을 대체하지 않는다.

전송 계층은 서버 `CastInstanceId`, 사건 시각, 배치 DTO와 진행 상태를 아래 표현 입력으로 변환한다. 공유 시계는 기존 클라이언트 계약처럼 `NetworkTime.time`이다. 현재 서버 DTO에는 패킷 sequence가 없으므로, 이 API의 `Sequence`는 바인더가 순서를 보장해 부여하는 재생 내 증가 번호이며 새로운 서버 필드가 아니다.

## 재생 API

```csharp
using Shinshi.Art;

WolbaekTalismanPlayer.Snapshot input = new WolbaekTalismanPlayer.Snapshot(
    castId, sequence, sign, assignedColor, structure.Center, structure.Radius,
    appearedAt, snapshotAt, WolbaekTalismanPlayer.EPhase.Holding,
    holdStartedAt, suppliedProgress);
uint playbackId = player.Begin(input, serverNow);
player.FaceGlyph(playbackId, cameraPosition);
player.Tick(playbackId, serverNow);
```

`Snapshot`은 표현 전용 불변 값이다. `AppearedAt`, `SnapshotAt`, `PhaseStartedAt`, `now`는 같은 시계이며 `AppearedAt <= PhaseStartedAt <= SnapshotAt <= now`여야 한다. `CastId/Sequence`는 0이 아니어야 하고 반경은 양수, 위치/색/시간/진행도는 유한수여야 한다. 진행도는 0~1이다. `Begin` 실패는 0을 반환하고 기존 재생을 보존한다.

| API/입력 | 사용 조건 |
|---|---|
| `Begin(snapshot, now)` | 등장 또는 늦은 접속의 현재 표현을 시작한다. 성공 상태도 해당 시점부터 재현한다. |
| `Reconcile(playbackId, snapshot, now)` | 같은 시전의 다음 snapshot을 적용한다. 중심/반경/문양/색/등장 시각은 고정, Sequence는 증가해야 한다. 거절 시 표현과 시계는 바뀌지 않는다. |
| `Tick(playbackId, now)` | 활성 재생에만 단조 증가 시각을 공급한다. 진행도는 마지막 공급값을 유지한다. 로컬 시간만으로 점유나 성공을 확정하지 않는다. |
| `FaceGlyph(playbackId, cameraPosition)` | 떠 있는 문양만 카메라 방위로 돌린다. 구조물과 점유 경계는 이동하지 않는다. |
| `EPhase.Reset` | 서버 초기화 시각, 진행도 0을 전달한다. 이전 공급 진행도의 빛이 빠지고 종이가 가라앉은 상태를 다음 Holding까지 유지한다. |
| `EPhase.Success` | 서버 성공 알림의 시각과 진행도 1을 전달한다. 부적/문양/꽃잎이 상승하고 소멸한다. Holding의 진행도 1은 성공으로 바뀌지 않는다. |
| `EPhase.Expired` | 실패/정지의 외부 확정 종료 시각을 전달해 소멸한다. 이미 성공한 상태를 다른 종료 상태로 바꾸지 않는다. 즉시 취소는 Stop을 쓴다. |
| `Stop(playbackId)` / `Stop()` | 세대가 맞는 취소 또는 즉시 풀 반환. 속성 블록과 소유 포즈를 복원하고 숨긴다. 완료 이벤트는 발생하지 않는다. |
| `Completed` | 공급된 종료 상태의 연출이 끝났을 때만 발생한다. 게임 성공 이벤트로 사용하지 않는다. |

전송 바인더가 `HasHold/HoldStartServerTime/Start.HoldSeconds`를 확인해 `Clamp01((serverNow - holdStart) / HoldSeconds)`를 공급할 수 있다. 분모는 서버 값이며, 계산된 1은 표시만 한다. `OnFormationSucceeded`가 올 때까지 Holding을 유지한다. 초기화는 새 `PhaseStartedAt`을 사용하고, 재점유는 새 유지 구간의 시작 시각으로 되돌린다. 입력 누락이나 지연 동안 로컬로 성공을 생성하지 않는다.

재접속 때 Transition/Active snapshot은 해당 진행값으로 복원한다. Counter snapshot에서는 구조물이 이미 정리된 계약이므로 새 부적을 만들지 않는다. 진행 중 효과에 실패/정지/전투 종료가 오면 종료 입력 또는 Stop으로 정리한다. 예전 재생 ID의 Tick/Reconcile/Stop은 새 재생에 영향을 주지 않는다. 한 프리팹 인스턴스는 한 구조물의 한 재생만 표시하므로 중첩 시 별도 인스턴스가 필요하다.

## 배치와 제작 조건

루트 피벗은 구조물 바닥 중심, Unity 미터 기준이다. 루트와 상위 Transform은 회전 identity, 스케일 1을 유지하고, 위치와 반경은 API로 전달한다. 반경은 지면 그룹의 XZ 크기만 조절하므로 석주와 부적 높이가 점유 반경에 따라 커지지 않는다. 문양은 약 2.4m 높이, 부적은 약 1~2m 높이에 있다. 경계 전체는 처음부터 표시되고 안쪽 진행 띠만 차오른다.

석주는 새 Blender 원본에서 직접 제작한 3 LOD와 PBR 맵을 쓴다. 크기 1.06×1.649×1.06m, LOD0/1/2는 10,152/4,872/2,232삼각형이다. 1024px BaseColor/Normal/AO/MetallicSmoothness를 실제 연결했다. Roughness는 `1-Roughness`로 MetallicSmoothness의 A에 패킹하고 별도 원본 맵과 SpectralSource FBX는 Assets 밖 개인 원본 폴더에 보존한다. AO는 실제 Cycles CPU 베이크(64 rays, 거리 0.22m, Only Local, 여백 12px)이며 GPU 베이크가 정지한 기록과 CPU 전환을 보존했다. 별도 Blender 미리보기는 HIP GPU 설정으로 실제 렌더 완료했으며 Unity 실행 결과와 구분한다. 불투명 석주는 성공 상승 동안 남고 효과 종료 시 숨긴다. 이미 배치된 맵 석주를 사용할 경우 이 프리팹의 `Decorative_StonePedestal` 자식을 중복 배치하지 않도록 별도 표현 변형을 만들고 소유권을 확인한다. 배치용 프리팹에는 Collider, NavMeshObstacle, NetworkBehaviour, 피해 컴포넌트가 없다.

Unlit 빛/종이/안개/꽃잎은 발광과 알파 표현 목적상 AO를 생략한다. 기존 `Shinshi/VFX/Particle` 셰이더를 사용하며 URP 카메라 depth texture가 필요하다. 프리팹에 검수용 Bloom이나 카메라는 포함하지 않는다. 실제 전장 노출/Bloom은 별도로 대조한다.

v04의 `Textures/T_Talisman_PaperVeil.png`는 종이 면에, `Textures/T_Talisman_WispVeil.png`는 지면의 짧은 Wisps와 새 AirWisps/SuccessWisps에 쓰는 알파 마스크다. 필획과 문양은 별도 메시의 발광을 유지한다. 새 AirWisps는 석주 주변의 얇은 공중 흐름이고 SuccessWisps는 외부 `EPhase.Success`에서만 표시되는 상승 잔상이다. 둘은 실제 미터 좌표와 길이 방향 9개 뼈를 사용하며 점유 반경 그룹의 XZ 스케일을 받지 않는다. 성공 잔상 때문에 전체 경계가 줄거나 커지지 않는다. Holding의 진행도 1과 로컬 시간 경과로 성공 잔상을 켜지 않는다.

반투명 레이어와 Point 광원은 추가 스키닝, 투명 오버드로, URP 추가 조명 비용을 갖는다. 실제 전장 품질/API와 동시 인스턴스에서 GPU 시간, 드로우콜, 첫 사용 지연을 실측하지 않았으므로 성능 개선이나 예산 통과로 해석하지 않는다. 발밑 광원은 위 설정으로 끌 수 있다.

생성 메뉴 `Shinshi/VFX/Wolbaek/Build Talismans`는 v04 제작 JSON과 FBX/맵을 필요로 하며 기존 SO를 보존한다. 원본·재현 스크립트·캡처와 이전 원본은 개인 제작 기록에 보존한다. 배포된 native 메시/재질/프리팹의 실행에는 Blender나 제작 원본 폴더가 필요 없다. 개발 씬이 dirty일 때 생성 도구는 저장된 변경을 덮지 않는다. 통합 씬은 생성/검수 중 저장하지 않는다.

## 검증과 프리워밍

프리워밍 대상은 [맵 성능 준비](MAP_PERFORMANCE_PREPARATION.md)의 월백 사방 부적진 목록을 따른다. 공용 실행 카탈로그와 Net 바인더는 이번 쓰기 범위 밖이며 문서 등록과 실제 GPU 워밍업을 구분한다.

아래는 v04의 실제 재빌드·PlayMode 검사·영상 검수 결과다. 이전 v03의 55/55 검사와 영상은 개인 근거 폴더에 보존했다. 사용자 정의 검사는 Unity Test Runner 실행 건수와 구분한다.

| 주장 | 수준 | 증거 등급 | 실행 수단 | 실제 결과 | 상태 |
|---|---|---:|---|---|---|
| 코드와 프리팹 참조 유효 | L1 | 1 | Unity 컴파일/Console, GUID/의존성 조회 | 컴파일 실패 false, 최종 Console error/warning 0, 4PF 각 45개/전체 51개 의존성, 56개 파일과 폴더 포함 63항목의 .meta 누락 0 | 완료(검증됨) |
| 외부 진행/초기화/성공과 취소/세대/풀 복원 | L2 | 2 | 전용 씬 PlayMode에서 실제 프리팹 대상 사용자 정의 검사 | 169/169 PASS(설정 기본값·유한값 검사 포함), Holding 1과 200초 경과에도 성공 잔상 숨김, Completed 재진입·새 광원 정리·늦은 성공 포즈 재현 검증 포함 | 완료(검증됨) |
| 원화의 핵심 레이어와 단계 표현 | L2 | 2 | 실제 PlayMode Camera.Render, 인코딩 영상 및 핵심 프레임 직접 검수 | 1280×720, 24fps, 576프레임, 24초; 네 문양/색 식별, 초기화 하강/감쇠, 명시 성공 상승/완전 정리 | 완료(검증됨) |
| 점유 경계와 장식 범위 | L2 | 2 | 경계 메시 및 실제 bone/bindpose skinning 월드 좌표, 5시점 표본 | 경계 3m, 장식 최대 2.65~2.88m, 해당 표본에서 경계 안; 성공 표본 높이 최대 4.47m | 완료(검증됨), 전 시각 연속 검사 아님 |
| 실보스/재접속/다인 통합과 성능 | L3 | 0 | 미실행 | 부적진 클라이언트 복제/실보스 연결, M2/M3, Player, API별 variant/PSO, 성능 미검증 | 검증 대기 |

v04 독립 검증은 외부 입력 기반 Art 표현 L2 PASS, 통합 범위를 포함하면 CONDITIONAL이다. 원화보다 규칙적인 경계 링은 남는다. 원화의 폐허 환경/식생 전체 재현이나 품질 “90%”를 정량 측정한 결과가 아니다. 실제 전장 카메라/노출/색각 가독성은 연결 후 추가 검수한다.

v04 개인 근거는 원본 폴더의 `talisman_playmode_checks_v04.txt`, `verify_playmode_v04.cs.txt`, `prewarm_dependencies_v04.txt`, `boundary_samples_v04.txt`, `ffprobe_v04.json`, `talismans_unity_v04.mp4`, 디코딩한 `video_contact_v04.png`/초기화·성공 프레임과 `unity-sequence-v04/`에 보존했다. 영상은 0~8초 사방 단계, 8~14초 달 근접 등장/초기화, 14~20초 꽃/별/마름모 근접, 20~24초 사방 성공 상승/소멸 순서다. 근접 Holding은 v03과 같은 카메라로 비교했다. v04 사방 캡처는 네 경계가 잘리지 않는 범위에서 더 가까이 촬영했다. 이전 v03 근거 파일은 보존했다.
