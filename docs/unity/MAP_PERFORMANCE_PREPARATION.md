# 맵 성능 사전 준비와 베이크

> 2026-10-01 · 제작·측정 절차 제안. 최소 사양 PC의 60 FPS를 우선한다. 이 문서는 맵·로더·프리워밍 시스템의 구현 완료나 성능 개선 실측을 뜻하지 않는다.

## 1. 목표와 현재 상태

맵을 만들 때 반복 계산을 자산으로 옮기고, 첫 사용 비용을 로딩 단계로 옮긴다. 지속적인 프레임 저하와 첫 실행 순간의 끊김은 따로 해결한다. **VFX를 미리 재생한다고 효과 중첩의 GPU 비용이 줄지는 않고, AO를 굽는다고 메시 삼각형이 줄지는 않는다.**

### 목표 기기와 측정 기준

| 항목 | 기준 | 상태 |
|---|---|---|
| 최소 사양 CPU | Intel Core i5-11500 | 사용자 지정, 실기기 확인 미실행 |
| 시스템 RAM | 32GB | 사용자 지정; 게임이 전부 사용할 수 있다는 뜻이 아님 |
| GPU | GeForce RTX 2060 계열로 추정 | 사용자 표기는 `GTX 2060`; 실측 전 실제 모델·VRAM·드라이버 확인 필수 |
| 클라이언트 목표 | 1920×1080, 평균 60 FPS 이상, 프레임 시간 p95 25ms 이하, 메모리 최대 4GB | [기술설계서 11장](../03-technical-design.md)의 기존 목표 유지 |
| 로컬 신시→전장 전환 | 필수 로드·활성화·프리워밍을 포함해 15초 이하 | [기획서 12장](../02-game-design.md)의 초기 목표; 네트워크 준비 제한과 별개 |
| 서버 | 초기 제안 30Hz의 틱 비용·메모리 별도 측정 | 클라이언트 FPS와 별개; [기술설계서 4.2·11장](../03-technical-design.md) |

60 FPS의 프레임 간격은 약 16.67ms다. 기존 p95 25ms 목표는 모든 프레임이 16.67ms 이내라는 보장이 아니다. 평균 FPS 외에 p99·최대 프레임 시간·첫 스킬 지연을 보조 지표로 기록하고, 새 합격 상한은 실측과 팀 결정 전까지 정하지 않는다. CPU와 GPU가 겹쳐 실행되므로 각 시간의 단순 합을 전체 프레임 시간으로 쓰지 않는다.

### 2026-10-01 작업 트리에서 정적으로 확인한 것

| 대상 | 확인 결과 | 해석 |
|---|---|---|
| 엔진·렌더링 | Unity 6000.4.7f1, URP 17.4.0, VFX Graph 17.4.0 | [버전 파일](../../ProjectSettings/ProjectVersion.txt), [manifest](../../Packages/manifest.json) |
| PC 렌더러 | Forward+, SRP Batcher 켬, GPU Resident Drawer 끔; SSAO 있음 | [PC RPAsset](../../Assets/Settings/PC_RPAsset.asset), [PC Renderer](../../Assets/Settings/PC_Renderer.asset)와 설치 URP enum 대조 |
| 품질 에셋 | PC가 Standalone 기본값; Mobile은 Standalone 제외·Forward·SSAO 없음 | [QualitySettings](../../ProjectSettings/QualitySettings.asset), [Mobile Renderer](../../Assets/Settings/Mobile_Renderer.asset); PC 저사양 프로필은 별도 제안 |
| 프로브·셰이더 | Legacy Light Probes 설정, Preloaded Shaders 목록 비어 있음 | 프로브는 [PC RPAsset](../../Assets/Settings/PC_RPAsset.asset)의 설정; 목록은 [GraphicsSettings](../../ProjectSettings/GraphicsSettings.asset). APV 설치와 사용은 다름 |
| Mixed 그림자 | PC 품질 `shadowmaskMode=1`(Distance Shadowmask), 그림자 거리 50·4캐스케이드 | [QualitySettings](../../ProjectSettings/QualitySettings.asset), [PC RPAsset](../../Assets/Settings/PC_RPAsset.asset); 맵 베이크·Forward+ 실제 동작은 미검증 |
| 전용 서버 빌드 | `dedicatedServerOptimizations=1` | [Player 설정](../../ProjectSettings/ProjectSettings.asset); 실제 빌드에서 제거된 자산 목록은 미검증 |
| 기존 아트 | 돌바닥·부유석·석등에 실제 AO/PBR·3LOD·배치 프리팹이 있음 | [돌바닥 PF](../../Assets/02.Prefab/Environment/StoneCourtyard/PF_StoneCourtyardTile.prefab), [부유석 PF](../../Assets/02.Prefab/Environment/FloatingRock/PF_FloatingRock.prefab), [석등 PF](../../Assets/02.Prefab/Props/StoneLantern/PF_StoneLantern.prefab); 전체 맵의 조명 베이크·성능 완료는 아님 |
| VFX | 외부 Piloto 패키지에 ParticleSystem 프리팹이 있음 | 프로젝트 게임에서 사용할 목록·풀·동시 사용량은 아직 미확정 |
| 맵·로더 | 완성된 게임 맵의 라이트맵·프리워밍·풀링 검증 증거를 확인하지 못함 | 외부 데모 씬의 LightingData를 프로젝트 맵의 베이크 증거로 쓰지 않음 |

이 표는 조사 시점의 스냅샷이다. 실제 게임 품질 레벨이 어느 RPAsset을 선택하는지는 빌드에서 다시 기록한다. 설정값의 존재를 실제 배칭·조명·성능 성공으로 바꾸어 쓰지 않는다.

## 2. 먼저 할 일과 적용 순서

| 우선순위 | 방법 | 주로 줄이는 비용 | 대가·적용 조건 |
|---|---|---|---|
| 1 | 기준 빌드·카메라 경로·효과 중첩 장면 측정 | 병목을 구분함 | 측정 없이 풀 크기·광원 수·텍스처 크기를 확정하지 않음 |
| 2 | 렌더 메시 감면·LOD·단순 충돌·재질 공유 | 정점·그림자·드로 제출·물리·메모리 | 노멀/AO 베이크만으로 대체하지 않음 |
| 3 | 고정 환경의 Baked 조명·정적 반사, 동적 개체용 프로브 | 반복 조명·그림자·반사 갱신 | 라이트맵/프로브 메모리와 베이크·재베이크 비용 |
| 4 | 필수 예고·스킬 VFX의 제한된 풀과 첫 사용 준비 | Instantiate·초기화·셰이더 첫 사용 끊김 | 로딩 시간·상주 메모리 증가; 정상 재사용 초기화 필요 |
| 5 | 투명 효과·실시간 광원·그림자·후처리 품질 예산 | GPU 픽셀·패스 비용 | 적 공격 예고와 판정 가독성 보존 |
| 6 | 배칭 방식·GPU Resident Drawer·오클루전·APV 비교 | 실제 확인한 CPU/GPU 병목 | 자산·셰이더·설정 호환성 검증 후 조건부 채택 |

초기 권장안은 **기존 URP/SRP Batcher를 유지한 채 자산 비용을 줄이고, 고정 조명 베이크와 필수 VFX 준비부터 비교**하는 것이다. 효과가 측정된 항목만 맵 제작 기본값으로 승격한다. 모든 기능을 한꺼번에 켜는 일은 피한다.

## 3. 제작 단계에서 굽는 것

### 모델·재질

[아트 제작 기본 과정](ART_ASSET_PRODUCTION_WORKFLOW.md)을 정본으로 따른다. 고밀도 원본은 제작용으로 보존하고 런타임용 낮은 메시로 요철을 옮긴다. 실제 삼각형·재질 슬롯·그림자용 메시가 줄어야 기하 처리 비용 감소를 기대할 수 있다.

- Normal은 작은 표면 요철, AO는 국소 가림을 옮긴다. 실루엣·문턱·계단·충돌은 텍스처로 대체하지 않는다.
- LOD마다 UV·노멀·탄젠트·버텍스 색 호환을 확인한다. UV를 바꾼 감면·리메시는 기존 베이크를 그대로 재사용하지 않는다. 알파 카드의 빈 픽셀을 줄이고 원거리 식생·불필요한 그림자도 함께 점검한다.
- 기존 바닥 LOD0은 71,920삼각형, 부유석 LOD0은 72,573삼각형인 제작 사례다. 반복 배치에 적합한 확정 예산은 아니므로 바닥의 작은 균열·마모를 텍스처로 옮기는 추가 감면을 우선 비교한다. 현재 PC `lodBias=2`와 실제 게임 카메라가 선택하는 LOD를 함께 본다. Cross-fade는 두 LOD를 동시에 그리는 전환 비용도 측정한다.
- 반복 모듈은 메시·재질을 공유한다. `renderer.material`로 개별 복제를 늘리지 않는다. 아틀라스·메시 병합은 재질 수를 줄일 수 있지만 거대한 병합으로 가시성 판정을 잃거나 텍스처 품질이 낮아지지 않는지 비교한다.
- AO는 품질 기준이지 SSAO 제거 명령이 아니다. 자산 AO·라이트맵·화면 공간 AO의 적용 범위와 중복 어두워짐을 실제 셰이더에서 확인한다.
- 텍스처의 원본 해상도와 빌드의 실제 임포트 크기·압축·밉맵·VRAM 점유는 다르다. `.meta`의 최상위 숫자 하나만 보고 PC 사용 크기를 단정하지 않는다. Normal·AO 등 데이터 색 공간과 채널 패킹은 기존 과정대로 유지한다.

충돌은 장식에 불필요하면 생략하고, 이동 바닥은 단순 프리미티브 또는 별도 저밀도 충돌 메시를 쓴다. `Physics.BakeMesh`와 임포트의 충돌 사전 처리 후보는 MeshCollider cooking·시작 비용이 실제 병목일 때만 검토한다. 실시간 물리·판정 비용 전체를 없애는 기능이 아니며, 옵션·스케일·메시 변경 뒤 재사용 조건과 해당 Unity 버전 API를 검증한다. 서버와 클라이언트의 권위 충돌 계약을 렌더 LOD와 함께 바꾸지 않는다.

정적 임포트 MeshCollider는 빌드 시 `PlayerSettings.bakeCollisionMeshes`를 먼저 비교하고, 런타임 생성·변형 메시의 cooking만 별도 `Physics.BakeMesh` 후보로 둔다. 기존 돌바닥·부유석 두 자산군의 6개 LOD FBX는 Read/Write가 꺼져 있으므로 런타임 베이크를 위해 일괄 켜지 않는다. Player에서 읽기 가능한 메시·동일 cooking 조건이 필요하며 CPU 사본 메모리가 증가할 수 있다. 같은 메시를 여러 작업에서 동시에 베이크하지 않고, 작업 완료 전에 자원을 해제하지 않는다. Unity 6000.4의 `EntityId` 계열과 이전 obsolete 오버로드를 확인해 구버전 예제를 그대로 복사하지 않는다.

### 맵 조명·반사

| 대상 | 초기 권장 | 남는 비용·주의 |
|---|---|---|
| 움직이지 않는 건물·바닥·석등 주변 조명 | Baked 환경 조명부터 시험 | 고정된 조명·그림자만 굽는다. 해당 광원의 직접 specular가 빠지므로 반사 프로브나 중요 Mixed/Realtime 광원과 PBR 가독성을 비교. 문·파괴물·이동 광원 변화는 대응되지 않음 |
| 플레이어·적·동료 | 프로브로 간접광을 받도록 설계 | 동적 직접광·그림자는 별도. 라이트맵에 캐릭터를 영구히 굽지 않음 |
| 주 광원과 중요 동적 그림자 | 필요한 Mixed/Realtime 경로 비교 | Mixed 모드도 실시간 작업이 남는다. Shadowmask·거리·동적 수광/그림자 조합을 검증 |
| 석등·창·장식 불빛 | Emission과 구운 환경광, 필요할 때만 근접 동적 광원 | Emission/Bloom은 동적 물체 조명의 대체가 아님. 정적 GI 반영은 베이크 설정과 셰이더 지원 확인 |
| 고정 반사 | Baked Reflection Probe 또는 Skybox | 반사 텍스처 해상도·개수·메모리 예산. 움직이는 물체는 구운 반사에 새로 나타나지 않음 |
| 움직이는 물·특수 반사 | 필요한 범위만 Realtime/별도 기법 시험 | 갱신 주기·레이어·해상도 비용; 모든 프로브를 매 프레임 갱신하지 않음 |

PC는 Forward+이므로 `Additional Lights Per Object = 4`를 전체 추가 광원 비용의 상한으로 믿지 않는다. 석등마다 실시간 Point Light·그림자를 붙인 결과를 먼저 측정하고, 주변 정적 조명은 베이크로 옮긴다. 이동·공격 예고의 시각적 의미를 바꾸는 품질 축소는 별도 가독성 확인이 필요하다.

Mixed는 모드별로 비교한다. Baked Indirect에도 직접광·그림자 계산은 남고, Shadowmask는 텍스처·샘플링 비용과 한 지점 최대 4채널의 Mixed 광원 겹침을 점검해야 한다. 현재 PC 설정인 Distance Shadowmask는 근거리 정적 그림자도 실시간 작업에 포함될 수 있다. Shadowmask와 Distance Shadowmask를 비교하되 공식 지원표의 Forward 경로 표기를 Forward+ 실측 결과로 확대하지 않는다. 설치 URP 코드 경로와 실제 빌드의 동적 수광·그림자를 함께 확인한다. Directional 라이트맵은 노멀맵 표현과 추가 데이터 비용을 비교한다. URP에서 지원하지 않는 LPPV나 HDRP 전용 기능을 설치 완료 대안으로 넣지 않는다.

최소 사양 품질은 SSAO Off/현재/Downsample, 주광 그림자 거리·캐스케이드, 반사 갱신과 Depth/Opaque Texture 필요성을 한 항목씩 비교해 정한다. 화면 복사 텍스처를 끄면 왜곡·물 등 의존 셰이더가 깨질 수 있다. 현재 SSAO는 `AfterOpaque=0`이며 설치 URP의 간접광 `min(SSAO, materialAO)` 결합을 모든 모드에 일반화하지 않는다. After Opaque 등 모드 변화에는 라이트맵·버텍스 AO·재질 AO 중복 암화를 다시 확인한다.

베이크 순서는 다음과 같다.

1. 맵 배치·스케일·재질·정적/동적 경계를 정한다. `Contribute GI`와 조명 수신 모드는 대상별로 지정한다.
2. 라이트맵용 두 번째 UV 채널(`Mesh.uv2`, Unity UV1)의 비중첩·여백·차트·실제 텍셀 밀도를 확인한다. 재질 UV0·타일링 AO UV와 혼동하지 않는다. 화면에서 작은 소품까지 같은 고밀도 라이트맵을 강제하지 않는다.
3. 해당 맵의 Lighting Settings와 Baked/Mixed 모드를 정하고 라이트맵·선택한 프로브·정적 반사를 굽는다. Light Probes와 APV는 동적 개체 간접광의 비교 대안이며, 현재는 Legacy Light Probes가 활성 설정이다. APV는 메모리·누광·베이킹/스트리밍 비용을 비교한 뒤 도입한다.
4. 캐릭터 이동·실내외 경계·단차·벚꽃 그늘·석등 가까이에서 누광·이음·번짐·검은 동적 개체·중복 그림자를 확인한다. Baked 조명에서도 캐릭터 접지 그림자가 필요한지 확인한다.
5. 씬·Lighting Settings·LightingData·라이트맵·프로브 출력과 `.meta`의 실제 참조를 확인하고 빌드에 포함되는지 검증한다. 베이크 결과는 `Library/` 캐시만으로 인계하지 않는다.

배치·GI 기여 메시·UV·재질/Emission·광원·프로브 구성·조명 시나리오가 바뀌면 영향 범위를 재베이크하고 기록한다. Additive 씬은 함께 쓰는 조명·프로브·가시성 데이터의 베이크 단위와 로드 조합을 정해야 한다. 열린 하늘·물·시간대 변화가 있는 맵은 고정 조명을 굽는 범위부터 좁힌다. 베이크 취소·실패 시 이전 성공 결과와 섞인 데이터를 배포하지 않고, 유효 결과로 복원하거나 미완료로 둔다.

기존 돌바닥·부유석 LOD FBX의 importer는 `generateSecondaryUV=0`이다. 이것만으로 원본 FBX에 두 번째 UV가 없다고 단정할 수는 없으며 실제 채널·차트는 미확인이다. 자동 생성 또는 수동 제작한 라이트맵 UV를 검증하거나, 작은 대상은 프로브 수광을 선택한다. 기존 배치 프리팹의 루트 static flags가 0인 상태에서 맵 GI 준비가 끝났다고 보지 않는다. 담당 배치 씬에서 대상별 GI 기여를 정하며 모든 프리팹을 일괄 Static으로 바꾸지 않는다. 아트의 Normal/AO 베이크와 이 맵 조명 준비는 별도 단계다.

베이크 출력의 크기·형식을 확인한다. `LightingData.asset`·오클루전 데이터처럼 큰 바이너리 `.asset`은 확장자만으로 LFS 적용이 보장되지 않는다. [씬·에셋 안전 3장](SCENE_AND_ASSET_SAFETY.md)과 [.gitattributes](../../.gitattributes)에 따라 필요하면 담당 승인 후 경로별 LFS 규칙을 준비한다. 공유·커밋 시 새 문서와 참조한 아트 문서·프리팹·모델·재질·텍스처 및 `.meta`가 제공되는지도 검사하며, 관심사별 커밋 원칙을 지킨다.

추가로 확인한 기존 석등 LOD FBX 3개도 Read/Write·자동 두 번째 UV 생성이 꺼져 있고 프리팹 루트 static flags는 0이다. [석등 재질](../../Assets/01.Scripts/05.Art/Props/StoneLantern/Materials/M_StoneLantern.mat)에 `_EMISSION`과 `m_LightmapFlags=2`(BakedEmissive)가 있어도 주변 GI 베이크 완료 증거는 아니다. 배치 대상의 GI 기여·셰이더 지원·베이크 출력, 라이트맵 수광이면 해당 UV 또는 프로브 수광 선택을 함께 검증한다.

Legacy 프로브의 Additive 로드/언로드에는 tetrahedralization 갱신 필요성과 완료 시점을 검증한다. `TetrahedralizeAsync` 등을 로딩 중 묶어 처리하는 후보로 보고 전투 중 반복 비용을 측정한다. 라이트맵·프로브·반사 데이터를 함께 갱신해야 할 변경을 구분한다. 석등을 런타임에 끄거나 제거해도 구운 주변광은 남으므로 그런 변화가 필요한 대상은 기여 범위를 달리 설계한다. APV 조명 시나리오만으로 라이트맵·반사·그림자가 함께 바뀌는 낮밤 시스템이 완성된다고 하지 않는다.

## 4. VFX 프리워밍은 네 가지로 나눈다

| 종류 | 준비하는 것 | 해결하지 못하는 것 |
|---|---|---|
| 인스턴스 풀 준비 | 프리팹 생성, 컴포넌트 참조·버퍼 초기화 | 렌더되지 않은 셰이더/PSO 생성, 입자 오버드로 |
| ParticleSystem `prewarm` | 루프 효과를 한 주기 진행한 시각 상태로 시작 | 범용 로딩 워밍업·첫 스킬 PSO 준비. 일회성 효과에는 루프 전제와 목적이 다름 |
| ParticleSystem `Simulate` / VFX Graph 초기화·시뮬레이션 | 선택한 시스템의 상태·시간 진행 | 실제 GPU 렌더 패스·드로 상태 전체. ParticleSystem API를 VFX Graph에 그대로 적용할 수 없음 |
| 셰이더/실제 렌더 경로 준비 | 필요한 variant·GPU 프로그램·파이프라인 상태 | 다른 품질·키워드·패스·그래픽스 API까지의 완전한 준비 |

필수 목록은 **그 맵의 공격 예고, 현재 파티의 장착 스킬·동료, 등장할 적·보스의 효과**로 좁힌다. 실제 프리팹 GUID·재질·텍스처·셰이더 키워드·사용 품질·예상 동시 인스턴스·풀 반환 조건을 기록한다. `maxParticles` 합계를 실제 활성 입자나 필요 풀 수로 쓰지 않는다.

풀 수는 동시 최대값과 재생/잔상 수명을 측정해 정한다. 빈 풀 정책은 중요 예고 보존 → 저우선 장식 축소를 기본 제안으로 하고, 어떤 효과를 생략할 수 있는지는 기획 기준으로 확정한다. 필수 예고에는 예약된 최소 표현을 마련해 정상 효과 고갈 시에도 범위·시작/종료 시각을 표시한다. 대체 표현까지 실패하면 클라이언트 표현 결함으로 기록한다. 표현 실패가 서버의 공격 시각·피해 판정을 멈추거나 취소하는 권한이 되지 않는다. 예산이 검증된 제한적 추가 생성은 별도 후보이며 무제한 Instantiate를 기본으로 두지 않는다. 최소 표현과 풀 고갈 처리는 아직 구현할 제안이다.

`ObjectPool<T>.defaultCapacity`는 내부 용량이지 미리 생성된 인스턴스 수가 아니며, `maxSize`는 유휴 객체 보존 제한이지 `Get()`의 동시 활성 상한이 아니다. 초기 생성 수·최대 활성 수·유휴 보존 수·메모리 예산을 별도로 정의한다. 공통 풀 구현은 아직 확인되지 않았으므로 이 계약이 이미 적용됐다고 쓰지 않는다.

### 안전한 준비·반환 순서

1. 자산 로드가 끝난 뒤 해당 씬 소유의 풀을 만든다. 생성은 로딩 화면 안에서 프레임별로 나누고 총량·상주 메모리·시간을 측정한다. 씬이나 스킬 목록이 바뀌면 목록 버전을 갱신한다.
2. 프리워밍 인스턴스는 표현 전용으로 격리한다. 네트워크 스폰·피해·히트 이벤트·경제 요청·오디오·카메라 흔들림·사용 기록을 발생시키지 않는다. 외부 프리팹의 `OnEnable`, 이벤트·서브이미터·ParticleSystem `stopAction`도 검사한다.
3. 시뮬레이션과 실제 렌더 준비를 구분한다. 비활성 오브젝트·꺼진 Renderer·카메라 밖 배치만으로 GPU 경로가 준비됐다고 보지 않는다. 필요하면 로딩 중 실제 렌더 구성과 맞는 제한된 워밍업 경로를 만들고, 컬링·렌더 타깃·패스·라이트·키워드가 실제 실행됐는지 확인한다.
4. 준비 후 입자·트레일·잔상·라이트·랜덤 시드·시간·변형·부모·알파·이벤트 구독·오디오·목표 참조를 실제 원본의 재생 계약에 맞게 초기화한다. 월드 공간 입자나 서브이미터가 이전 위치에 남지 않아야 한다. 풀 반환 후 콜백이 이중 반환하지 않도록 수명을 검사한다.
5. 로딩 중 다른 맵으로 바뀌거나 연결이 종료되면 새 작업 시작을 중단한다. 이미 완료된 자원만 안전하게 반환하고 공유 자산을 중복 해제하지 않는다. 동기 작업을 실행 중간에 강제 취소할 수 있다고 가정하지 않는다.

VFX Graph 패키지는 설치됐지만 실제 게임 `.vfx` 자산·사용 여부는 따로 확인한다. GPU 시뮬레이션·컴퓨트 경로와 이벤트 재초기화에는 전용 검증이 필요하다. 외부 구매 프리팹 원본을 자동으로 고쳐 프리워밍 정책을 넣지 않는다.

`ParticleSystem.Simulate`는 진행 뒤 일시 정지하며 기본 `restart=true`다. 분할 진행 때 매 호출을 다시 시작하지 않는지 확인한다. `VisualEffect.Reinit`는 initial event를 보낼 수 있으므로 무해한 단순 버퍼 준비 함수로 취급하지 않는다. VFX Graph의 bounds/culling 정책이 보이지 않는 준비를 생략하거나 실제 효과를 잘라내지 않는지도 점검한다. 공급자 스크립트 자동 재생은 프로젝트 래퍼·파생 구성에서 검토하고 원본은 보존한다.

### 셰이더 준비·변형 관리

필수 재질의 `ShaderVariantCollection` 등 제한된 후보부터 비교한다. `Shader.WarmupAllShaders`나 전 패키지 프리팹의 일괄 생성은 시작 시간·메모리·불필요한 변형을 늘릴 수 있어 기본안으로 삼지 않는다. 빌드의 stripping 설정에서 필요한 variant가 남는지도 확인한다.

DX12·Vulkan·Metal은 vertex layout·렌더 타깃 포맷·블렌드/깊이 상태 등이 다르면 일반 variant 워밍업 뒤에도 PSO 생성이 다시 일어날 수 있다. 실험적 `GraphicsStateCollection`의 실제 빌드 trace와 점진적 준비는 이 병목이 남을 때의 후보다. API별 데이터를 구분하고 엔진·셰이더·렌더 경로·품질 변화 뒤 다시 수집한다. 실험적 API의 호환성 검증 없이 제작 기본 도구로 고정하지 않는다. GPU/드라이버 변화 후에도 별도 시험한다.

완료 판정은 `WarmUp` 반환만으로 하지 않는다. 준비 전후 첫 스킬과 정상 중첩 재생에서 셰이더·PSO 생성 관련 Profiler 마커, 실제 프레임 시간, 누락 효과를 비교한다. 품질 전환·새 장착 스킬·재접속에서도 필요한 준비가 유지되는지 확인한다.

## 5. 프리워밍 뒤에도 남는 런타임 비용

| 병목 | 먼저 비교할 것 | 주의 |
|---|---|---|
| GPU 픽셀·효과 중첩 | 입자 수·화면 점유·수명·겹침·투명 패스·후처리·왜곡 | 화면 전체를 덮는 큰 카드의 빈 영역도 비용. 낮은 품질에서도 예고 범위·색·타이밍 유지 |
| 조명·그림자 | 추가 실시간 광원/그림자 수, 그림자 거리·캐스케이드·해상도 | PC 기본 설정을 저사양에서 검증 없이 고정하지 않음 |
| CPU 제출·상태 변경 | 공유 재질·variant, SRP Batcher, 선택적 GRD 비교 | 드로 콜 숫자만으로 CPU/GPU 개선을 판단하지 않음 |
| 정점·가시성 | LOD·거리 컬링·충분히 작은 공간 단위 | 표현을 숨겨도 서버의 AI·피격·원격 판정은 유지 |
| 메모리·업로드 | 텍스처 실제 압축·밉맵·선택적 mip streaming·풀 규모 | 메모리 절약과 선명도/스트리밍 끊김을 함께 비교 |
| 물리·GC·반복 작업 | 저밀도 충돌·이벤트 기반 갱신·참조/버퍼 재사용 | 네트워크/판정 주기 변경은 별도 계약·실측 |

배칭은 SRP Batcher, 일반 GPU Instancing, Static Batching, GPU Resident Drawer를 독립적인 만능 스위치로 함께 켜지 않는다. Material의 instancing 체크는 실제 instanced draw의 증거가 아니다. 현 상태에서는 SRP Batcher를 유지하고 Frame Debugger/Profiler로 실제 경로를 확인한다. Forward+가 GRD의 렌더 경로 전제에 맞더라도 메시·셰이더·플랫폼·배칭 설정 등 다른 조건도 확인해야 한다. GRD 시험은 Static Batching과의 비호환, variant 증가, 메모리 비용을 포함해 별도 비교한다.

GRD 후보는 `BatchRendererGroup Variants=Keep All` 요구와 현재 `m_BrgStripping=0`(Strip Unused)의 차이를 확인한다. Animator/Animation 계층의 지원 범위, MaterialPropertyBlock·`OnBecameVisible/Invisible` 의존 등에 따른 일반 렌더 경로 전환 조건, 애니메이션 LOD cross-fade 미지원, Enlighten Realtime GI 비활성 조건을 점검한다. Static Batching·공용 Graphics Settings 변경은 담당 승인 후 별도 실험한다. 현재 GRD가 꺼져 있으므로 이 조건이 이미 충족됐다고 쓰지 않는다.

CPU 오클루전 베이크는 건물 내부·골목처럼 큰 고정 가림이 있는 구간에서 먼저 시험한다. 열린 부유섬·하늘·드문 장식에서는 조회/데이터 비용보다 이득이 작을 수 있다. 움직이는 문·파괴물은 고정 occluder로 취급하지 않는다. 메시를 큰 덩어리로 합친 뒤 숨길 수 있는 공간을 잃지 않는지 확인한다. GPU 오클루전은 CPU 베이크와 다른 경로이며 현재 활성이라고 가정하지 않는다.

[기획서](../02-game-design.md)의 플레이어를 가리는 나무·구조물 투명화와 충돌하지 않아야 한다. 투명해질 대상은 고정 occluder 후보에서 제외하거나 별도로 검증한 가시성 정책을 쓴다. 구운 고정 가림은 재질 알파 변경에 맞춰 자동 재베이크되지 않는다는 점을 전제로, 뒤의 플레이어·적 예고가 잘못 컬링되는지 실제 카메라에서 확인한다. 필요한 동적 Renderer의 Dynamic Occlusion 제외를 비교하되 GRD는 다른 처리 조건이 있으므로 범용 해결책으로 삼지 않는다. 투명화에 따른 오버드로도 목표 부하에 포함한다.

함께 보일 Additive 씬은 오클루전 데이터를 공동 베이크할 조합을 정한다. 동시에 사용하는 오클루전 데이터 제약 때문에 각 청크의 독립 베이크를 무작정 합치지 않는다. Mip streaming은 메모리/화질 비교 후보이며 설정 예산은 비스트리밍 텍스처와 최소 밉 유지 등으로 초과될 수 있어 절대 메모리 상한이 아니다. 전투 중 `Resources.UnloadUnusedAssets`나 전체 GC를 반복해 끊김을 만드는 방안은 기본안으로 삼지 않는다.

## 6. 로딩·서버 준비와 연결하기

[기술설계서 13장 자산 로딩](../03-technical-design.md)의 씬 단위 비동기 로드·직렬화 참조·첫 전투 전 예고/VFX 준비를 따른다. Addressables가 의존성으로 설치된 것과 맵용 그룹·원격 카탈로그·게임 로더가 구현된 것은 별개다. 초기 원격 카탈로그 미도입 결정을 성능 문서에서 바꾸지 않는다.

권장 수명은 **맵 요청 → 씬/필수 자산 로드 → 씬 활성화·초기화 → 필수 풀/셰이더 준비 → 로컬 표현 준비 확인 → 제안할 준비 신호 전달 → 서버가 기존 출정 계약에 따라 시작 결정**이다. 선택적 장식 준비는 플레이 중에도 프레임 예산 안에서 분산할 수 있으나 필수 예고는 전투 시작 전에 준비한다. 이 흐름은 아직 구현할 계약이며 완성 로더나 준비 API가 있다는 뜻이 아니다.

설치 Mirror의 [NetworkManager.OnClientSceneChanged](../../Assets/Mirror/Core/NetworkManager.cs)는 기본적으로 씬 로드 뒤 `NetworkClient.Ready()`와 조건부 `AddPlayer()`를 호출한다. `OnClientConnect`도 `clientLoadedScene=false`인 경로에서는 Ready와 조건부 AddPlayer를 호출한다. 이는 프리워밍 완료 증거가 아니다. 기존 기술설계서는 연결/스폰 준비를 다루며 로컬 표현 준비 신호는 별도 제안이다. 네트워크 담당 민성과 준비 의미·실패/시간 제한을 합의한 뒤 프로젝트의 두 파생 콜백 제어 또는 별도 준비 메시지를 비교한다. 공급자 Core 원본은 수정하지 않으며 준비 신호가 이미 정의됐다고 가정하지 않는다.

로컬 신시→전장의 **로드+활성화+필수 준비를 포함한 전체 전환 15초 이하**가 초기 목표다. 단계별 시간과 총시간을 함께 기록한다. 기술설계서의 일회용 토큰 60초·로딩 준비 제한 120초 제안은 네트워크/출정 실패 처리용이며 15초 성능 목표를 대체하지 않는다. 첫 효과 지연의 별도 합격 상한은 아직 미정이다.

측정 구간은 **클라이언트의 맵 전환 처리 시작 → 대상 씬 활성화·필수 준비를 마쳐 로컬 표현이 준비된 시점**으로 제안한다. 파티원 대기·토큰 교환·서버 `/start`·전투 시계 시작의 타임스탬프와 사용자 출정 요청부터 플레이 가능까지의 전체 시간도 별도 저장한다. 기획의 “로컬” 의미와 최종 합격 구간은 담당과 확정해야 하며, 별도 대기 시간을 누락해 사용자 체감 전환이 15초를 만족했다고 주장하지 않는다.

`LoadSceneAsync`는 활성화 시 초기화·GPU 업로드 비용까지 모두 없애지 않는다. `allowSceneActivation=false`의 0.9 상태를 준비 완료로 보지 않는다. 활성화를 기다리게 하면 다른 AsyncOperation의 진행도 막힐 수 있으므로, 0.9 상태에서 필요한 언로드 작업 완료를 기다리는 교착 흐름을 만들지 않는다. 씬 안의 인스턴스에 의존한 워밍업은 적절한 활성화/생성 뒤에 한다.

| 실패·변화 | 처리 원칙 |
|---|---|
| 필수 자산 로드 실패·최소 예고 표현 사용 불가 | 로드 오류나 필수 최소 표현의 생성/렌더 유효성 검사 실패를 기록하고 로컬 준비 성공을 보내지 않음. 담당이 합의할 준비 제한·기존 출정 실패 계약으로 연결 |
| 셰이더 워밍업 미완·지연 | 관찰 가능한 작업/단계 시간 초과와 첫 사용 끊김을 기록. `WarmUp`의 반환을 성공/실패 결과로 해석하지 않음. 최소 표현이 유효한 경우의 진입 정책·시간 상한은 새 준비 계약에서 정함 |
| 선택적 장식 실패 | 중요 예고·실루엣이 보존되는 검증된 대체만 사용 |
| 긴 로딩 | 단계별 소요와 마지막 정상 단계를 표시. 인증 토큰 만료·예약/준비 제한을 임의로 연장하지 않음 |
| 씬 전환·연결 종료·오래된 완료 콜백 | 맵 요청 세대/현재 세션 확인 후 오래된 결과 폐기. 전환 중복·이중 반환 방지 |
| 재접속 | 서버 상태를 새 게임으로 초기화하지 않음. 진행 중 예고 스냅샷을 표현하기 전에 필수 효과를 준비하거나 안전한 최소 표현을 즉시 사용. 서버 시각을 기준으로 남은 수명을 복원하고 재접속 준비 시간을 별도 측정 |
| 품질·장착·콘텐츠 버전 변화 | 바뀐 렌더 경로/효과 목록의 준비 필요성을 다시 확인 |
| 메모리 상한 초과 | 선택적 풀·고해상도 자원을 줄여 재측정. 필수 예고의 안전한 최소 표현은 유지 |

로딩 제한·접속 토큰·예약 전이는 기존 기술설계서 6·8·10장을 정본으로 쓴다. 프리워밍이 클라이언트 피해·쿨다운·확률·보상을 실행하거나 서버 시간을 멈추는 근거가 되어서는 안 된다. 전용 서버는 렌더 VFX·라이트맵·GPU 워밍업 없이 필요한 권위 충돌·NavMesh·판정 데이터만 준비하는 방향을 검토한다. 빌드가 실제로 제거하는 자산과 코드 경계는 별도로 확인한다.

서버 빌드 최적화 플래그가 켜져 있어도 렌더 자산·워밍업 코드 전체가 제거됐다는 증거는 아니다. 자동 최적화는 Read/Write가 꺼진 텍스처 픽셀·메시 정점의 불필요한 데이터를 제거하되 물리 등 CPU 시스템에 필요한 메시 데이터는 보존한다. 플래그로 켜는 추가 최적화의 셰이더/폰트 제거, Scriptable Build Pipeline·Addressables 자산 처리의 차이, Read/Write 메시의 CPU 데이터 유지 조건을 실제 빌드로 확인한다. 실행 역할로 분기해 전용 서버에서 클라이언트 VFX·SVC·GPU 준비를 실행하지 않는다. 재접속 준비 동안에도 기존 서버 시뮬레이션·피해 규칙은 계속 적용한다.

## 7. 최소 사양 PC에서의 검증

먼저 실제 i5-11500/32GB/2060 GPU의 모델·VRAM·드라이버·OS·저장 장치·그래픽스 API를 기록한다. 한 PC에서 서버·여러 클라이언트를 함께 실행한 M2는 기능 검증에 쓰고, 최소 사양 클라이언트의 최종 성능은 별도 서버 PC를 둔 M3에서 판단한다. 에디터 수치나 같은 PC 서버의 자원 경쟁을 최종 성능으로 대신하지 않는다.

1. 동일한 콘텐츠·카메라 이동 경로·스킬/난수 조건·해상도·품질·렌더 스케일·FPS 상한·VSync·전체화면 조건을 고정한다. 기존 멀티플레이 문서의 720p 실행 예시는 이번 1080p 합격 측정에 그대로 쓰지 않는다.
2. 병목 분석은 Development Player+Profiler로 하되 Deep Profiling 비용을 구분한다. 최종 수치는 불필요한 프로파일링 부담을 뺀 빌드로 확인하고 빌드 종류·수집 방식을 함께 적는다. CPU Main/Render·GPU 시간이 어디서 병목인지 보고, 대기 마커를 연산 비용과 혼동하지 않는다.
3. 최초 프로세스 실행/맵 진입/첫 스킬의 구간, 프리워밍 이후 반복 사용 구간, 정상 플레이 구간을 따로 저장한다. 프로세스 재시작만으로 OS·드라이버 캐시가 비었다고 단정하지 않고 캐시 조건을 기록한다. 단순 반복 스킬 시험을 최초 실행 시험으로 부르지 않는다.
4. [기술설계서 성능 시나리오](../03-technical-design.md)를 따른다: 4인 신령사·동료 8체·적 24체 10분, 마을 10→50→100명은 각각 목표 부하로 시험한다. 장면이 없으면 필요한 제작 조건을 남기고 결과를 미실행으로 표시한다.
5. 정상 이동·최대 VFX 중첩·보스 예고·실내외 경계·LOD 전환·급회전·재접속·풀 고갈·품질 전환을 본다. 타이밍 결함은 최소 10회 반복하며 관련 서버/클라이언트 로그와 실제 가독성을 비교한다.
6. 한 번에 한 방법만 바꿔 전후를 비교한다. GPU 시간 감소와 로딩 증가·메모리 피크·누락 효과를 함께 판단한다. GPU 병목과 CPU 병목 결과를 섞어 평균 FPS 한 숫자로 결론내지 않는다.

평균 FPS는 구간 프레임 수/경과 시간으로 계산하고, p95/p99는 해당 구간의 개별 프레임 시간 분포로 계산한다. 로딩 프레임을 정상 플레이에 섞거나, 끊김 프레임을 임의로 빼지 않는다. FPS 제한/VSync가 걸린 합격 조건과 병목 확인용 별도 조건을 구분해 기록한다.

| 시험/빌드·기기·품질/API | 준비 방식 | 전체 전환/단계별 시간(로컬 목표≤15s) | 첫 효과 최대 지연 | 평균 FPS·p95/p99(ms) | CPU/GPU 병목 | 메모리 피크/VRAM·GC | 시각/권위 결과 | 판정 |
|---|---|---|---|---|---|---|---|---|
| 실제 값 기입 | 미적용 기준 | — | — | — | — | — | — | 미실행 |
| 같은 조건 | 단일 방법 적용 | — | — | — | — | — | — | 미실행 |

4GB 목표는 게임 메모리 목표이며 RAM 32GB·GPU VRAM 용량과 같은 값이 아니다. 프로세스 사용량·Unity 메모리·그래픽스 메모리를 어떤 도구로 판정했는지 기록하고, 공용 문서에서 메모리 판정 지표를 확정해야 한다. VRAM을 프로세스 RAM에 기계적으로 더해 4GB 통과 여부를 정하지 않는다. 로딩·씬 교체 시 두 씬/풀/텍스처가 동시에 남는 피크도 포함한다.

## 8. 맵 제작자가 넘길 기록

- [ ] 맵 ID·콘텐츠/빌드 버전·사용 품질·목표 최소 사양을 기록했다.
- [ ] 렌더 메시/LOD·재질/variant·텍스처 실제 임포트 크기·단순 충돌·기본 참조를 확인했다.
- [ ] 정적/동적 경계·Lighting Settings·라이트맵 UV·프로브·반사 출력과 재베이크 조건을 기록했다.
- [ ] 베이크한 씬/자산·출력 경로·`.meta`·누락 참조·빌드 포함 상태를 확인했다.
- [ ] 필수 VFX 목록·풀 크기 근거·초기화/반환·고갈 정책·첫 사용 준비 범위를 기록했다.
- [ ] 준비 실패·취소·전환·재접속·품질 변경 때의 처리와 서버 준비 계약을 구분했다.
- [ ] 기준/적용 빌드의 정상 플레이·최초 진입·효과 중첩·메모리 피크를 비교했다.
- [ ] 실제 실행한 검증과 미실행 항목·가독성/LOD/충돌/네트워크 회귀를 구분했다.

씬·프리팹·베이크 자산 작업은 [씬·에셋 안전](SCENE_AND_ASSET_SAFETY.md), 다른 파트·공용 설정·서버 경계는 실제 소유와 기존 승인을 따른다. 이 문서 작성으로 통합 씬 저장·구매 에셋 원본 수정·패키지 도입·기존 설정 변경이 수행되거나 승인된 것은 아니다. 이번 산출물의 검증은 문서 정적 확인과 독립 검토이며, 구현 후에는 [검증 플레이북](../ai/03_VERIFICATION_PLAYBOOK.md)의 L1~L3와 [멀티플레이 절차](../ai/04_MULTIPLAYER_TEST_PROTOCOL.md)를 적용한다.

## 9. 도입 단계와 아직 결정할 것

| 단계 | 산출물 | 다음 단계 조건 |
|---|---|---|
| 기준 확립 | 최소 사양 빌드·동일 경로·기준 캡처·품질 프로필 기록 | GPU/CPU/메모리/첫 실행 병목을 구분 |
| 작은 맵 검증 | 고정 조명 베이크 + 대표 모델/LOD + 필수 효과 소수의 풀/준비 비교 | 누광·예고·풀 수명·로딩/메모리 회귀 없음; 로컬 전환≤15s 목표 확인 |
| 목표 부하 확대 | 전투/마을 목표 시나리오·M3·씬 전환 피크 | 기존 FPS/p95/메모리·로컬 전환 목표 확인 |
| 조건부 확장 | 남은 병목에 한해 GRD/APV/오클루전/PSO trace/스트리밍 시험 | 채택 이유·호환성·실측·복구 방법 기록 |

미정: 정확한 GPU/VRAM·OS/드라이버/API·저장 장치, 최소 사양 품질 프로필, 게임 메모리 4GB 판정 지표, 맵별 광원/텍스처/풀 예산, 15초 전체 전환 안의 단계별 준비 예산과 첫 스킬 지연 상한, 로컬 표현 준비 신호, 베이크/로더/검증 실행 도구와 담당 범위. 이 항목은 구현 시 계약으로 채우며 이 문서에서 임의 수치로 확정하지 않는다.

## 10. 공식 기술 근거

공식 문서의 API·지원 조건은 Unity 6000.4/설치 URP 17.4와 대조하고, 기능 설치와 사용 검증을 구분한다. 아래 자료는 방법의 근거이며 이 프로젝트의 성능 통과 증거가 아니다.

- [Unity 비동기 씬 로드](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/SceneManagement.SceneManager.LoadSceneAsync.html), [활성화 보류와 작업 큐](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/AsyncOperation-allowSceneActivation.html): 6장의 로딩·0.9·교착 주의.
- [Unity 애플리케이션 프로파일링](https://docs.unity3d.com/6000.4/Documentation/Manual/profiler-profiling-applications.html): 7장의 실제 Player 측정.
- [ParticleSystem prewarm](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/ParticleSystem.MainModule-prewarm.html), [Simulate](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/ParticleSystem.Simulate.html), [VFX Graph 자산](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.4/manual/VisualEffectGraphAsset.html), [VisualEffect.Reinit](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/VFX.VisualEffect.Reinit.html), [ObjectPool](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Pool.ObjectPool_1.html): 4장의 서로 다른 준비 작업·수명·용량.
- [셰이더 프리워밍](https://docs.unity3d.com/6000.4/Documentation/Manual/shader-prewarm.html), [ShaderVariantCollection 한계](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/ShaderVariantCollection.WarmUp.html): 4장의 API·PSO·실험적 후보와 실제 렌더 검증.
- [Light Modes](https://docs.unity3d.com/6000.4/Documentation/Manual/LightModes-introduction.html), [Mixed Lighting](https://docs.unity3d.com/6000.4/Documentation/Manual/lighting-mode.html), [라이트맵 UV](https://docs.unity3d.com/6000.4/Documentation/Manual/LightingGiUvs-GeneratingLightmappingUVs.html), [다중 씬 프로브](https://docs.unity3d.com/6000.4/Documentation/Manual/light-probes-and-scene-loading.html), [Reflection Probe](https://docs.unity3d.com/6000.4/Documentation/Manual/class-ReflectionProbe.html): 3장의 조명·반사·로드 조합.
- [APV](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/probevolumes-use.html), [SSAO 설정](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/ssao-renderer-feature-reference.html), [렌더 파이프라인 지원 비교](https://docs.unity3d.com/6000.4/Documentation/Manual/render-pipelines-feature-comparison.html): 설치와 활성 구분·품질 실험·URP 지원 범위.
- [배칭 방법 선택](https://docs.unity3d.com/6000.4/Documentation/Manual/optimizing-draw-calls-choose-method.html), [SRP Batcher](https://docs.unity3d.com/6000.4/Documentation/Manual/SRPBatcher.html), [GRD 요구 조건](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/gpu-resident-drawer.html), [다중 씬 오클루전](https://docs.unity3d.com/6000.4/Documentation/Manual/occlusion-culling-scene-loading.html), [Mip streaming](https://docs.unity3d.com/6000.4/Documentation/Manual/TextureStreaming-configure.html): 5장의 조건부 렌더링 대안.
- [정적 오클루전 대상 선정](https://docs.unity3d.com/6000.4/Documentation/Manual/occlusion-culling-getting-started.html), [동적 대상 컬링과 GRD 차이](https://docs.unity3d.com/6000.4/Documentation/Manual/occlusion-culling-dynamic-gameobjects.html): 5장의 투명화 대상·뒤의 캐릭터 가시성 정책 검증.
- [충돌 빌드 베이크](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/PlayerSettings-bakeCollisionMeshes.html), [Physics.BakeMesh](https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Physics.BakeMesh.html), [Dedicated Server 최적화](https://docs.unity3d.com/6000.4/Documentation/Manual/dedicated-server-optimizations.html): 3·6장의 cooking과 권위 데이터 보존.

### 이번 문서의 검증 상태

현재 맵에 새 베이크·프리워밍·품질 변경을 적용하거나 Player 성능을 측정하지 않았다. 세 에이전트의 정적 조사와 공식 기술 근거를 통합한 제안이다. Claude Opus 5.5 `xhigh` 읽기 전용 검토에서 지적 15건 수용·1건 부분 수용 후 재검증 1회로 문서 검토 통과를 받았다. 재검증의 경미한 보완 5건도 본문에 반영했으며 이 최종 문장 수정분은 추가 Claude 검토 없이 정적 확인했다. 문서 검토 통과를 베이크·FPS 통과로 사용하지 않는다.
