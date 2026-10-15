# 월백 05 색 결계와 연결선 VFX 사용 안내

2026-10-14. 서버가 확정한 현재 결계와 같은 색 두 참가자의 연결선을 표시하는 Art 전용 프리팹이다. 색 배정, 보호 판정, 거리 피해, 복귀 유예와 다음 지점 선정은 하지 않는다. 현재 `IWolbaekColorWardListener`의 원격 복제와 실보스 클라이언트 Binder는 미구현이다.

## 프리팹과 Inspector

폴더는 `Assets/02.Prefab/VFX/Wolbaek/Wards/`다. 네 Regular 프리팹의 SO는 독립이며 메시와 재질은 공유한다.

| 프리팹 | 기본 표현 | 독립 설정 |
|---|---|---|
| `PF_WolbaekWard_Red.prefab` | 빨강 반구 결계, 마름모 | `SO_WolbaekWard_Red.asset` |
| `PF_WolbaekWard_Blue.prefab` | 파랑 반구 결계, 초승달 | `SO_WolbaekWard_Blue.asset` |
| `PF_WolbaekLink_Red.prefab` | 빨강 곡면 연결선, 마름모 | `SO_WolbaekLink_Red.asset` |
| `PF_WolbaekLink_Blue.prefab` | 파랑 곡면 연결선, 초승달 | `SO_WolbaekLink_Blue.asset` |

스크립트, 설정, 메시, 텍스처와 재질은 `Assets/01.Scripts/05.Art/VFX/Wolbaek/Wards/`에 있다. Player Inspector에 해당 SO 설정을 펼쳐 표시한다. 변경은 다음 `Begin`부터 적용하고 빌더는 기존 SO 조정값과 에셋 GUID를 보존한다. 같은 SO를 여러 인스턴스가 참조하면 다음 재생부터 함께 영향을 받는다.

| 설정 | 역할 |
|---|---|
| Ward `AppearSeconds / SwitchSeconds / FadeSeconds` | 등장, 외부 Mark에서 Active 전환 강조, 종료 감쇠 |
| Ward `DomeHeight / FlowCycle / WaveAmplitude / PetalDrift` | 돔 높이, 흐름 주기와 장식 움직임 |
| Ward `ShellOpacity / RibbonOpacity / PetalOpacity / MoteOpacity` | 반구 막, 리본, 꽃잎과 광점 대비 |
| Ward `GlyphOpacity / GlyphStrength / SwitchWhiteShare` | 문양과 전환 강조 |
| Ward `PetalTint / AssignedPetalTintShare` | 꽃잎 색과 배정 색 혼합 |
| Link `AppearSeconds / SwitchSeconds / FadeSeconds` | 등장, 위험 단계 변경 강조, 종료 감쇠 |
| Link `FlowCycle / WarningCycle / WaveAmplitude / MoteDrift` | 곡면 띠의 흐름과 경고 맥동 |
| Link `LinkHeight / EndpointRadius` | 발밑 앵커에서 띠까지의 높이, 끝점 원 크기 |
| Link `RibbonOpacity / MoteOpacity / GlyphOpacity / EndpointOpacity` | 레이어별 대비 |
| Link `GlyphStrength / WarningStrength / DangerStrength / WarningWhiteShare` | 문양 밝기와 외부 위험 단계 강조 |

## 현재 서버 계약

근거는 `WolbaekColorWard`, `WolbaekWardStart/Member/Stage/Snapshot`, `IWolbaekColorWardListener`다.

- 서버 색은 Red=0, Blue=1이며 현재 `SymbolId=(int)Color`다. Art `EWolbaekWardSign`은 Diamond=0, Moon=1이다. 변환 바인더에서 대응을 명시하고 숫자만으로 의미가 계속 같다고 가정하지 않는다.
- 결계는 현재 `Stage.RedCenter/BlueCenter/Radius`를 사용한다. 같은 지점에서 이동하지 않는다. 이전 지점의 마지막 공격이 끝난 서버 시각에 새 지점이 확정된다.
- 다음 지점 좌표는 미리 공개하지 않는다. 원화의 `NEXT` 패널과 개인 원본의 `Next*` 메시 이름은 현행 게임에서 미래 위치를 보여 줄 근거가 아니다. 이 프리팹의 Mark는 **이미 확정된 현재 지점**을 바닥 원과 문양으로만 표시하는 선택적 표현 상태다. 바인더는 새 Stage 확정 때 바로 Active를 사용할 수 있다.
- 같은 색이 정확히 두 명일 때만 그 두 CombatId를 연결한다. 참가자 2명은 색별 1명씩이므로 연결선이 없다. 사망, 부활, 재접속으로 배정을 다시 뽑지 않는다.
- 현재 임시 시험값은 결계 반경 3m, 연결 경고 XZ 거리 `>=3m`, 거리 피해 `>4m`, 복귀 유예 8초다. 운영 밸런스 확정값이 아니며 프리팹이 이 수치로 피해나 경고 상태를 판정하지 않는다.
- 멀어져도 pair가 해제되는 사건은 없다. Warning/Danger 표현은 유지되는 연결선의 위험 단계다. 한쪽 사망이나 접속 끊김은 외부에서 `EndpointsVisible=false`로 숨긴다. 기존 서버 시험 DevView는 Connected까지 확인하지 않아 이 경우 표시가 남을 수 있다.
- Gather의 중앙 집결 표시, 광역 공격 예고와 피해 연출, 복귀 유예 UI는 이 네 프리팹이 대신하지 않는다.

## 외부 재생 API

Ward와 Link는 각각 `readonly Snapshot`과 아래 API를 제공한다. 시간은 `NetworkTime.time` 등 하나의 공유 시계이며 `AppearedAt <= PhaseStartedAt <= SnapshotAt <= now`를 유지한다. CastId와 Sequence는 0이 아니어야 한다. Sequence는 바인더가 부여하는 재생 내 증가 번호로, 기존 서버 DTO에 새 필드를 추가한 것이 아니다.

```csharp
WolbaekWardPlayer.Snapshot wardInput = new WolbaekWardPlayer.Snapshot(
    castId, sequence, stageIndex, sign, assignedColor, center, radius,
    appearedAt, snapshotAt, WolbaekWardPlayer.EPhase.Active, phaseStartedAt);
uint wardPlayback = ward.Begin(wardInput, serverNow);
ward.Tick(wardPlayback, serverNow);

WolbaekLinkPlayer.Snapshot linkInput = new WolbaekLinkPlayer.Snapshot(
    castId, sequence, sign, assignedColor, firstFeet, secondFeet,
    appearedAt, snapshotAt, WolbaekLinkPlayer.EPhase.Active, phaseStartedAt,
    WolbaekLinkPlayer.ESeverity.Normal, suppliedWarningAmount, endpointsVisible);
uint linkPlayback = link.Begin(linkInput, serverNow);
link.Tick(linkPlayback, serverNow, currentFirstFeet, currentSecondFeet);
```

| API 또는 입력 | 동작과 조건 |
|---|---|
| `Begin(snapshot, now)` | 등장과 늦은 접속 복원. 실패는 0이고 기존 재생을 보존한다. 새 Ward Stage는 새로운 Begin으로 세대를 교체한다. |
| `Reconcile(playbackId, snapshot, now)` | 같은 시전의 증가 Sequence를 받는다. 동일 Ward Stage의 중심, 반경, 문양, 색과 등장 시각은 고정이다. Link는 두 좌표, 위험 단계와 표시 유효성을 갱신할 수 있다. |
| `Tick(playbackId, now)` | 현재 외부 상태의 절대 시각을 샘플링한다. 로컬 시간만으로 Mark를 Active로 바꾸거나 성공, 피해, 새 지점을 생성하지 않는다. |
| Link `Tick(playbackId, now, firstFeet, secondFeet)` | 현재 표시 좌표만 갱신한다. 위험 단계와 표시 유효성은 마지막 외부 입력을 유지한다. |
| Ward `EPhase.Mark` | 현재 확정 중심의 전체 바닥 원과 문양만 표시한다. 미래 목적지 안내가 아니다. |
| Ward `EPhase.Active` | 전체 경계, 낮은 반구, 비대칭 구름 흐름, 문양, 작은 꽃잎과 광점을 표시한다. |
| Link `ESeverity.Normal / Warning / Danger` | 외부 표시 단계와 0~1 WarningAmount를 받는다. Normal의 WarningAmount는 0이다. 실제 거리 피해와 유예는 외부 서버 계약이다. |
| Link `EndpointsVisible=false` | 끝점 원, 문양과 띠를 즉시 숨긴다. 같은 시전의 다음 유효 입력에서 true로 복원한다. |
| `EPhase.Expired` | 외부에서 확정한 종료 시각 이후 감쇠한다. 늦은 종료 입력도 현재 시점으로 복원한다. |
| `Stop(playbackId)` 또는 `Stop()` | 현재 세대 취소와 풀 반환. 속성 블록, 포즈와 소유 Bounds를 복원하고 숨긴다. 완료 이벤트는 만들지 않는다. |
| `Completed` | 종료 감쇠가 끝나 정리된 뒤 한 번 발생한다. 서버 성공이나 피해 이벤트로 사용하지 않는다. |

예전 재생 ID의 Tick/Reconcile/Stop은 새 재생에 영향을 주지 않는다. 뒤로 간 시각, 오래된 Stage/Sequence, 미래 snapshot, 유한하지 않은 입력은 거절한다. Stop 뒤에도 입력 이력을 보존하므로 같은 공유 시계의 새로운 입력을 사용한다. 다른 서버 연결의 시계 원점으로 바뀌면 이전 인스턴스를 폐기하고 새 인스턴스로 시작한다. CastId의 숫자 크기로 시전 순서를 추정하지 않는다.

재접속 때 Search/Strike의 현재 Stage와 두 참가자의 현재 위치, 생존·접속 상태로 복원한다. 같은 색 파트너가 없는 경우 Link를 만들지 않는다. 종료된 상태에는 새 결계를 만들지 않는다. 현재 snapshot에는 다음 지점 좌표, pair id, 다음 거리 피해 시각이 없으므로 바인더가 임의로 복원하지 않는다. 피해·복귀 유예를 시각적인 맥동 주기로 대체하지 않는다.

## 배치와 제작

루트와 상위 Transform의 회전은 identity, 스케일은 1을 유지한다. Ward 피벗은 바닥 원 중심이며 서버의 실제 미터 좌표와 반경을 받는다. 반경은 XZ, `DomeHeight`는 높이만 조절한다. Link 입력은 높이를 버리지 않는 실제 3D 발밑 앵커이며 띠와 문양에는 world up 방향으로 `LinkHeight`를 더한다. 끝점 원은 전달된 발밑 좌표에 놓고 바닥으로 임의 투영하지 않는다. 길이가 0이면 띠·문양을 숨기며 끝점 표시와 포즈가 NaN이 되지 않게 처리한다.

Link의 +Z 정규화 띠만 두 끝점 길이에 맞춰 늘리고, 네 문양은 별도 루트와 뼈로 위치만 이동한다. 따라서 길이가 늘어도 문양 자체의 너비는 늘지 않는다. glyph/mote SkinnedMeshRenderer의 Bounds는 현재 길이로 갱신하고 Stop 때 원본으로 복원한다.

v02는 Blender 5.1의 HIP GPU 렌더 3시점과 실제 Unity 캡처로 제작·검수했다. Unlit 막과 빛, 문양, 꽃잎은 발광·투명도 표현 목적상 AO/PBR 베이크를 생략한다. 원본은 15개 역할, 13,428삼각형이며 배포된 native 메시·재질·프리팹의 실행에는 Blender나 제작 JSON이 필요 없다. 표준 바닥 모델은 전용 개발 씬에서만 재사용하며 공급자 원본을 바꾸지 않는다.

v02는 길고 규칙적인 교차 궤도를 8개의 짧은 비대칭 흐름으로 바꾸고, 작은 곡면 꽃잎 28개와 광점 40개를 배치했다. 결계 막에는 부드러운 구름 알파를 적용했으며 두 Ward SO의 `ShellOpacity` 기본값은 0.3이다. 연결선의 세 띠는 이전 대비 1.65배 넓히고 불규칙한 굴곡과 잔상을 더했다. 반경과 경계, 마름모·초승달의 의미 및 외부 재생 계약은 유지한다.

기존 `Shinshi/VFX/Particle` 셰이더와 선형 알파 `T_Ward_Veil.png`, `T_Ward_Flow.png`, Warp의 광점 텍스처를 사용한다. URP 카메라 depth texture가 필요하다. 결계 막·리본·꽃잎·광점과 연결선 리본은 화면 비율 0.025 아래에서 컬링하는 단일 장식 LOD를 사용한다. 필수 경계, 문양과 끝점 표식은 LOD에서 제외한다. 이미 낮은 삼각형 수의 곡면 알파 VFX라 다단계 감면에 따른 실루엣·스키닝 변경 대신 거리 컬링을 선택했다. 배치 프리팹에는 카메라와 검수 Bloom, Collider, NavMeshObstacle, NetworkBehaviour, 피해 컴포넌트를 넣지 않는다.

`Assets/00.Scenes/Dev_VFX/Dev_Wolbaek_Wards.unity`의 20초 큐는 Mark, Active, Warning, Danger, 숨김, 복귀, 새 지점 확정, Active, 종료를 보여 준다. 개발 큐의 전환 시각은 시각 검수용이며 실제 서버의 탐색·공격 간격이 아니다. 개발 큐와 인체 크기 참고물은 배치 프리팹에 포함되지 않는다. 생성 메뉴 `Shinshi/VFX/Wolbaek/Build Wards and Links`는 원본 JSON과 텍스처가 필요하고, dirty인 개발 씬을 덮지 않으며 통합 씬을 저장하지 않는다.

## 검증과 프리워밍

프리워밍 준비 대상은 [맵 성능 준비](MAP_PERFORMANCE_PREPARATION.md)에 실제 GUID와 의존성을 확인해 등록했다. Ward 각 26개, Link 각 17개, 전체 고유 의존성 44개다. 공용 실행 카탈로그에는 미등록이며 `IPrewarmTarget`과 서버·Net Binder는 미연결이다. 문서 등록과 실제 GPU 워밍업을 구분한다.

| 주장 | 수준 | 증거 등급 | 실행 수단 | 실제 결과 | 상태 |
|---|---|---:|---|---|---|
| 코드, 프리팹과 참조 | L1 | 1 | Unity 컴파일, 참조·GUID·의존성 조회 | 4PF·4독립 SO, GUID 유지, 누락 스크립트/참조 0, 64개 파일·폴더의 .meta 누락 0, 최종 콘솔 오류·경고 0 | PASS |
| 외부 상태와 정리 | L2 | 2 | 전용 PlayMode 사용자 정의 검사 | 78/78 PASS. 늦은 입력 복원, 오래된 입력 거절, 좌표 갱신, 숨김/복원, 종료·Stop·재사용 확인. Unity Test Runner 실행 건수가 아니다. | PASS |
| 원화의 핵심 표현 | L2 | 2 | 실제 Unity 카메라 PNG·MP4와 별도 시각 검수 | v02 PNG 6장·1280×720·24fps·720프레임(30초), 장식 LOD에서 의미 표식 유지. 8개 시각 표본에서 경계 3m, 활성 장식 최대 2.76m. 연속 시각 전체에 대한 증명은 아니다. | 보정 PASS |
| 실보스, 재접속, 다인 통합과 성능 | L3 | 0 | 미실행 | 원격 복제와 Binder, M2/M3, Player, API별 variant/PSO, GPU 비용과 실제 워밍업 미검증 | 검증 대기 |

실제 Unity 영상에서 등장, 위험 단계, 숨김·복원, 새 현재 지점과 종료 감쇠를 확인했다. 검수 뒤 PlayMode를 종료하고 dirty가 아닌 Bootstrap으로 복귀했다. 원본·재현 코드와 캡처의 상세 기록은 개인 제작 기록에 보존한다.

원화의 폐허 환경과 인물 전체 재현이나 주관적 품질 퍼센트를 정량 측정한 결과가 아니다. 실제 전장 카메라·노출·동시 효과와 색각 가독성은 실보스 연결 후 검수한다.
