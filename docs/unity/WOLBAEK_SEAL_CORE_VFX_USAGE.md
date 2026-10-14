# 월백 봉인핵·낙하 VFX 사용 안내

월백 06 원화의 봉인된 석편·각인 띠, 열린 청백 핵, 고정 지점 낙하, 짧은 파쇄를 표시하는 독립 아트 프리팹이다. 실보스·Net Binder 연결과 실행용 프리워밍 카탈로그 등록은 별도 작업이다.

## 프리팹과 배치

| 대상 | 경로 | 배치 기준 |
|---|---|---|
| 봉인핵 | `Assets/02.Prefab/VFX/Wolbaek/SealCore/PF_WolbaekSealCore.prefab` | 루트는 외부 `Core.Center`, 핵 높이는 SO의 `CoreHeight` |
| 낙하 여우불 | `Assets/02.Prefab/VFX/Wolbaek/SealCore/PF_WolbaekSealDrop.prefab` | 루트는 외부 낙하 중심, 지면 경계 반경은 외부 `BlastRadius` |

재질·native 메시·텍스처·표현 SO와 자체 `SH_WolbaekSpectral.shader`는 `Assets/01.Scripts/05.Art/VFX/Wolbaek/SealCore/`에 있다. 루트와 모든 부모의 localScale은 `(1, 1, 1)`로 사용한다. 낙하 재생기는 이 조건을 검사하며 실행 중 부모·스케일이 바뀌면 정확한 경계가 남지 않도록 Stop한다. 고정된 핵과 여우불은 서로 독립된 인스턴스이며 같은 Index가 같은 플레이어 또는 서로의 짝이라는 의미는 없다. 물리 충돌체·피해 판정은 포함하지 않는다.

## 서버 계약과 표현 경계

현행 `WolbaekSealTurn`은 Prepare/Track/Escape/Cleanup 시간표와 핵·여우불 목록을 제공한다. `WolbaekSealCore`는 Index/Center만 가지며 HP·취약·일반 공격 수용 상태는 없다. **Opened는 껍질을 벌리는 시각 상태이며 공격 가능 또는 피해 증가를 뜻하지 않는다.** 외부 표현 어댑터가 확정한 상태를 입력한다.

`BlastRadius`는 여우불의 플레이어 폭발 경계다. `CoreHitRadius`는 낙하 중심에서 핵 중심까지의 서버 파괴 조건이며 핵 크기나 장식 문양의 반경이 아니다. 핵 바닥 문양은 장식이고 낙하 바닥 원만 외부 폭발 반경에 맞춘다.

`OnTurnResolved`는 destroyedCores/totalCores 집계만 전달한다. `WolbaekSealSnapshot`에는 개별 파괴 핵 목록이 없다. 따라서 실패 차례의 집계나 거리로 어느 핵을 파쇄할지 클라이언트가 계산하면 안 된다. `Destroyed`는 해당 핵의 외부 확정 결과를 확보했을 때만 입력한다. 현행 서버 `IsCoreDestroyed(index)`를 원격에 전달할 계약은 서버·네트워크 담당 후속 범위다. Cleanup 재접속 시 개별 결과가 없으면 기존 표시를 정리하며 파괴를 추측하지 않는다.

## 외부 API

두 플레이어 모두 `Begin(snapshot, now)`가 반환한 0이 아닌 `uint` 재생 ID로 `Reconcile(id, snapshot, now)`, `Tick(id, now)`, `Stop(id)`를 호출한다. `Begin`의 0 또는 `Reconcile`의 false는 입력 거부이며 기존 유효 재생을 변경하지 않는다. `Stop()`은 현재 재생의 무조건 정리와 풀 반환용이다.

| 입력 | 핵 Snapshot | 낙하 Snapshot |
|---|---|---|
| 식별 | CastId, Sequence, TurnIndex, CoreIndex | CastId, Sequence, TurnIndex, DropIndex |
| 배치 | WorldCenter | WorldTarget, BlastRadius |
| 시각 | AppearedAt, SnapshotAt, PhaseStartedAt | AppearedAt, LockAt, ResolveAt, SnapshotAt, PhaseStartedAt |
| 외부 상태 | Dormant, Opened, Destroyed, Expired | Tracking, Locked, Resolved, Expired |

- 모든 시각은 같은 단조 시계를 사용한다. `Sequence`는 전달 창구가 부여하는 **인스턴스 수명 전체의 증가 값**이다. CastId의 숫자 크기는 시전 순서가 아니다.
- 같은 핵의 중심·등장 시각·식별자는 바뀌지 않는다. 낙하의 반경·등장 시각·LockAt·ResolveAt은 처음부터 고정이며 WorldTarget만 Tracking에서 외부 갱신을 받는다. Locked 이후에는 WorldTarget도 고정이다. 같은 상태의 PhaseStartedAt을 바꾸거나 상태를 역행하는 입력은 거부한다.
- `Tick`은 시간에 맞는 외형만 샘플링한다. Dormant의 시간 경과로 Opened/Destroyed를 만들지 않으며, ResolveAt이 지나도 외부 Resolved 입력 없이는 충돌 효과를 만들지 않는다.
- 늦은 Snapshot은 PhaseStartedAt 기준 현재 포즈로 시작한다. 이미 끝난 파쇄·충돌 잔상은 새로 처음부터 재생하지 않는다. 이 API 복원은 실제 원격 복제·재접속 검증과 다르다.
- 핵의 Destroyed/Expired, 낙하의 Resolved/Expired는 terminal 상태다. terminal에서 다른 상태로 전환하는 입력은 거부하며 즉시 정리에는 Stop을 사용한다. Destroyed 순간 핵·Flow·보조광을 끄고 석편·띠·꽃잎만 짧게 파쇄한다.
- `Completed(playbackId)`는 terminal 상태의 시각 효과가 끝났을 때 발생한다. 게임 성공·피해 콜백이 아니다. Stop·비활성화는 완료 알림 없이 정리한다. 이미 끝난 terminal Snapshot으로 Begin하면 즉시 완료할 수 있으므로 Begin 전에 완료 이벤트를 구독하거나 반환 후 IsRunning을 확인한다.
- Stop 후에도 입력 순서 이력은 유지한다. 다른 서버 clock epoch나 새 전달 stream은 새 인스턴스를 사용한다. 재사용 시 이전 ID의 Tick·취소를 새 재생에 적용하지 않는다.
- Stop 후 같은 차례의 다른 CoreIndex/DropIndex에 풀 슬롯을 재배치할 수 있지만 더 큰 Sequence와 기존보다 과거가 아닌 입력 시각을 사용해야 한다. 자동 Update는 없으며 소유 클라이언트 View가 같은 외부 시계의 Tick을 호출한다.

## Inspector와 표현 조정

각 프리팹은 별도 `WolbaekSealCoreSettingsSO`, `WolbaekSealDropSettingsSO`를 참조한다. 같은 SO를 공유한 인스턴스는 같은 설정을 사용하며 재생 시작 때 값을 캐시한다. 실행 중 SO 변경은 다음 Begin부터 적용한다.

핵은 등장·개방·파쇄·소멸 시간, 높이, 석편 이동 거리·회전, 열림 기울기·상승·조각 크기, 흐름 주기·진폭, 꽃잎 움직임, 핵·흐름·문양 대비를 조정한다. Stone/Band의 `ShellDrift`/`ShellTiltDegrees` 기본값은 0.035m/3.2도이며 절대 시각의 작은 부유·기울임을 표시한다. 파쇄 때 이 움직임은 감쇠하고 전체 핵을 크게 회전시키지 않는다. 꽃잎은 조각별 seed·독립 주기·자기축 회전으로 움직인다. v04 배치 SO의 파쇄 이동/상승은 0.65m/0.24m이며 새 SO의 코드 기본값과 구분한다.

핵 안쪽의 선택형 Point Light는 범위와 닫힘/열림 강도를 SO로 조절하며 Destroyed·Stop·비활성화 때 꺼진다. Stop은 원본 포즈·속성 블록과 광원 색·범위를 복원하고 광원 intensity를 0으로 만든다. 낙하는 등장·확정 잔상·소멸, 낙하 높이와 흐름·문양 대비를 조정한다. 낙하의 LockAt/ResolveAt·폭발 반경은 외부 입력이며 연출 SO로 서버 시간표나 판정을 바꾸지 않는다.

Inspector의 PlayMode 미리보기와 전용 `Assets/00.Scenes/Dev_VFX/Dev_Wolbaek_SealCore.unity`를 사용한다. 스크럽은 새 미리보기 시전·재생 세대를 만들어 과거 포즈를 복원하며 기존 외부 clock을 역행시키지 않는다. 선택 해제는 Inspector가 시작한 미리보기만 정리하고 외부 재생을 끊지 않는다. 개발 씬의 입력 시각표는 기능·외형 검수용 예시이며 실제 보스 판정이 아니다. 프리팹 재생성은 `Shinshi/VFX/Wolbaek/Build Seal Core`, 전용 씬 재생성은 `Build Seal Core Preview Scene`이다. 재생성은 기존 SO 조정값과 GUID를 보존한다. 원본 Blender JSON·텍스처는 재생성할 때만 필요하며 게임 실행 의존성이 아니다.

## 재질·LOD·준비

Stone/Band는 URP Lit, 고유 UV0와 BaseColor·Normal·AO·MetallicSmoothness를 사용한다. 움직이는 조각의 AO는 각 조각 자체를 실제 CPU 베이크하며 봉인 상태의 이웃 접촉 음영을 영구히 넣지 않는다. 돌의 광물·균열·마모와 종이의 국소 얼룩·섬유·가장자리를 PBR 맵에 반영한다. 발광 핵·흐름·꽃잎·문양·낙하·플래시는 투명 Unlit 표현으로 AO를 생략한다. AO의 CPU 베이크와 HIP GPU 렌더는 별도 작업이며 GPU 렌더 실행을 AO 베이크의 증거로 대체하지 않는다.

| 표현 | 셰이더와 준비 조건 |
|---|---|
| Core, Flow, FlowVeil, DropFlame, DropVeil, Flash | 자체 `Shinshi/VFX/WolbaekSpectral`, 256×256 RG Repeat 노이즈, URP depth texture |
| Ground, Petal | 기존 `Shinshi/VFX/Particle`; Ground의 정확한 원과 꽃잎의 원래 색 보존 |
| Stone, Band | `Universal Render Pipeline/Lit`, Normal·AO·MetallicSmoothness |

Core의 shape UV는 Repeat, Flow/DropFlame의 진행 V는 Repeat로 사용한다. 플레이어는 외부 절대 시각의 순환 phase를 MaterialPropertyBlock `_BaseMap_ST.w`에 넣고 원본 offset을 보존한다. 셰이더의 구름·침식·왜곡도 이 phase를 사용하며 독립 `_Time`으로 서버 clock과 분리하지 않는다. Flow/FlowVeil은 premultiplied `One/OneMinusSrcAlpha`, 핵·낙하 불꽃·플래시는 additive 블렌드를 사용한다. 순환 phase가 돌아오는 지점의 UV·노이즈 연결을 재질을 바꿀 때 다시 확인한다.

낙하 불꽃은 이동하는 shape 마스크와 별도로 원본 UV의 V=0.58부터 끝 V=1까지 alpha를 0으로 감쇠해 geometry 끝이 잘리지 않게 한다. Flash 구체는 카메라를 향한 면의 연속 falloff로 감쇠한다. 이 감쇠는 외부 Resolved 순간광의 시각 표현이며 공격·파괴 판정을 추가하지 않는다. 투명 재질의 현재 활성 로컬 키워드는 0개이며 전역·품질·그래픽스 API variant 전체의 검증과 구분한다.

Flow는 짧은 S형 15개와 별도 FlowVeil 15개로 구성한다. veil opacity는 0.24, 펼침 폭은 0.24m이며 셰이더 변형을 담도록 메시 Bounds의 Y 크기를 총 0.24m 확장한다. DropVeil의 opacity는 0.24, 법선 펼침은 0.09m이며 Bounds 크기는 XYZ 각 0.18m 확장한다. 펼침 값을 바꾸면 Builder의 Bounds 여유도 함께 맞춰 카메라 컬링으로 잘리지 않게 한다. Ground의 폭발 경계 반경과 장식 veil의 범위를 혼동하지 않는다.

핵 Renderer는 원본 63개와 FlowVeil 15개를 합쳐 78개이며 낙하는 4개다. 장식 LOD는 Flow/FlowVeil 30개와 Petal 12개, 총 42개 Renderer만 컬링하며 석편·띠·핵·지면 표시를 유지한다. 석편·부적은 동적 그림자를 주고받으며 핵의 보조광은 그림자를 생성하지 않는다. 동적 그림자·추가 Point Light·투명 veil 중첩 비용을 포함한 실제 전장 성능 예산·동시 수는 별도 실측해야 한다. 공급자 Spirit Blossom 원본은 품질 비교용이며 직접 수정하지 않는다.

프리워밍 대상과 실행 연결 상태는 [맵 성능 사전 준비](MAP_PERFORMANCE_PREPARATION.md)에 기록한다. 문서 등록은 실제 GPU 워밍업이나 첫 사용 끊김 개선 완료를 뜻하지 않는다.

## 검증 범위

2026-10-15 v04 독립 아트 범위 **조건부 통과(L1+L2, 증거 등급 2)**다. 최종 Flame 끝 감쇠·Flash falloff 보완 뒤 Build와 기능·셰이더·콘솔 검사를 다시 실행하고 최종 영상을 직접 확인했다. 아래 확인을 실보스 연결·Player 빌드·전장 성능 완료로 확대하지 않는다.

| 확인 항목 | 수준·증거 등급 | 실제 결과 | 상태 |
|---|---|---|---|
| v04 핵/낙하 재귀 의존성 | L1·1 | 각각 93/17개, 전체 고유 101개 | 완료(검증됨) |
| 원본/native 메시와 삼각형 | L1·1 | 고유 메시 64개, 69,793삼각형 | 완료(검증됨) |
| PNG·메시 Editor 메모리 | L1·1 | PNG 15개 23,607,391bytes, 메시 28,575,384bytes | 완료(검증됨) |
| 외부 상태·순서·유한수·terminal·풀·조명 | L2·2 | 사용자 정의 실제 PlayMode 91/91 | 완료(검증됨) |
| Inspector 미리보기·외부 재생 보호·실제 폭발 반경 | L2·2 | 실제 검사 11/11, 외곽 2.75m 포함 | 완료(검증됨) |
| 최종 셰이더·컴파일·콘솔 | L1·1 | ShaderUtil messages=[]·compilationFailed=false·ConsoleErrors=0·Seal 신규 경고=0 | 완료(검증됨) |
| 실제 최종 영상·LOD·순환 경계 | L2·2 | H.264 1280×720·24fps·720프레임·30.000000초, decode contact sheet·LOD 42개·2.59/2.61초 확인 | 완료(검증됨) |

실제 PlayMode 검사 합계는 **102/102**이며 Unity Test Runner의 테스트 건수가 아니다. 외부 입력·중복/역순/비유한 값 거부·늦은 상태·개별 파괴·terminal 재등장 차단·원본 복원·보조광 정리·재사용·Inspector의 외부 재생 보호를 포함한다. 삼각형은 고유 메시 합계이며 veil 중복 렌더를 포함한 프레임의 제출 삼각형 수가 아니다. 메모리는 Editor `Profiler.GetRuntimeMemorySizeLong` 조회이며 PNG 약 22.51MiB, 메시 약 27.25MiB다. GPU VRAM·Player 상주 예산이나 실제 전장 성능 통과를 뜻하지 않는다.

실제 조각별 AO 베이크와 HIP GPU 렌더를 확인했고 최종 Build 뒤 v04 기능 검사를 재실행했다. 실제 Unity 카메라를 외부 절대 시각으로 샘플링한 **1280×720, 24fps, 720프레임/30초** 최종 영상을 저장하고 디코딩한 contact sheet를 직접 확인했다. overview·wide·장식 LOD 42개·흐름 주기 경계 2.59/2.61초, 불꽃 끝 감쇠와 Flash의 연속 falloff도 확인했다. 영상 프레임률은 성능 측정값이 아니다. 원화 느낌 90%·Spirit Blossom 이상은 사용자 품질 목표이며 정량 측정으로 달성했다고 주장하지 않는다.

개인 재현 스크립트·로그·캡처·영상과 상세 미적 평가는 첫 작업일을 유지한 Git 제외 경로 `.claude/docs/2026-10-14/wolbaek-seal-core-blender/`에 있다. 확인한 최종 v04 영상은 `v04-unity-shader/wolbaek_seal_core_v04.mp4`다. 실보스·Net·M2/M3·Player 빌드·Profiler 성능과 실제 GPU 프리워밍은 미검증이다.
