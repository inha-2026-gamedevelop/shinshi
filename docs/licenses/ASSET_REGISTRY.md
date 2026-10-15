# 외부 에셋 대장

2026-09-30 실제 설치 파일과 Unity 6000.4.7f1 에디터에서 처음 확인했고, 2026-10-08 `Assets/99.ThirdParty/` 전체 파일 목록과 `.meta`의 설치 정보를 정적으로 재대조했다. 이번 갱신에서 Unity 재생·컴파일·빌드는 실행하지 않았다. 저장소 루트가 Unity 프로젝트 루트다. Asset Store의 최신 버전과 이 프로젝트에 설치된 버전은 구분한다. 취득일·구매 내역·좌석 보유 현황은 확인하지 않았으며 설치 확인일을 취득일로 대신 적지 않는다.

## 분류와 설치 목록

외부 Asset Store 에셋은 `Assets/99.ThirdParty/{분류}/{에셋}/`에 둔다. 패키지 내부의 `Editor/`, `Resources/`, DLL, asmdef, 라이선스와 사용 문서는 보존한다.

| 분류 | 내용 | 현재 상태 |
|---|---|---|
| `Animation` | 캐릭터 애니메이션·클립 | CombatGirlsCharacterPack, Grruzam Archer Animation, Magical-Knight_Set, Succubus_Anim_Set 4종. 트윈 도구는 `Tools/Demigiant/` 참조 |
| `Tools` | 에디터, 대화, 물리 감지, 최적화, 셰이더 제작·트윈 도구 | 아래 9개 패키지(DOTween Pro와 동봉 DOTween·DemiLib 포함) |
| `VFX` | 완성된 이펙트, 파티클, 이펙트용 소재·물 셰이더 | 7개 패키지. PixPlays 6계열은 Elemental Spells Full Pack VFX 1개 패키지에 포함 |
| `Model` | 게임에서 사용할 모델·환경·소품 | P09 Modular Humanoid, Kitsune Yuna 2종 |

아래 경로는 `Assets/99.ThirdParty/` 기준이다. 상업 이용 상태의 “조건부”는 해당 라이선스와 사용자의 보유 권한을 충족해야 한다는 뜻이며, 이번 정리에서 구매 증빙까지 검증했다는 의미는 아니다. 모든 항목의 취득일은 미확인이다.

| 에셋 | 설치 버전 | 제작자 | 경로 | 구분 | 라이선스·상업 이용 | 출처 |
|---|---|---|---|---|---|---|
| DOTween Pro | 1.0.430 | Demigiant | `Tools/Demigiant/` | 유료, DOTween·DemiLib 동봉 | Pro 라이선스·조건부 | [공식 제품·설치 안내](https://dotween.demigiant.com/pro.php) |
| Realistic Spirit Blossom Spells VFX | 1.0 | Piloto Studio | `VFX/Piloto Studio/` | 유료 | Standard Unity Asset Store EULA·조건부 | [Asset Store 399376](https://assetstore-fallback.unity.com/packages/vfx/particles/spells/realistic-spirit-blossom-spells-vfx-399376) |
| Dialogue System for Unity | 2.2.73.2 | Pixel Crushers | `Tools/Pixel Crushers/` | 유료, Common 동봉 | Standard Unity Asset Store EULA / Extension Asset·조건부 | [Asset Store 11672](https://assetstore.unity.com/packages/tools/behavior-ai/dialogue-system-for-unity-11672) |
| RayCast Pro | 1.1.5B | Philosophers | `Tools/RaycastPro/` | 유료 | Standard Unity Asset Store EULA / Extension Asset·조건부 | [Asset Store 214714](https://marketplace.unity.com/packages/tools/physics/raycastpro-214714) |
| Editor Console Pro | 3.983 | FlyingWorm | `Tools/ConsolePro/` | 유료 | Standard Unity Asset Store EULA / Extension Asset·조건부 | [Asset Store 11889](https://assetstore.unity.com/packages/tools/utilities/editor-console-pro-11889) |
| Hierarchy Designer | 3.2.3 | Pedro Verpha | `Tools/Hierarchy Designer/` | 유료 | Standard Unity Asset Store EULA / Extension Asset·조건부 | [Asset Store 273928](https://assetstore.unity.com/packages/tools/utilities/hierarchy-designer-273928) |
| Wingman - Your Inspector's Best Friend | 1.4.0 | Kyle Rhoads | `Tools/Wingman/` | 유료 | Standard Unity Asset Store EULA / Extension Asset·조건부 | [Asset Store 303181](https://assetstore.unity.com/packages/tools/utilities/wingman-your-inspector-s-best-friend-303181) |
| Amplify Shader Editor | 1.9.9.12 | Amplify Creations | `Tools/AmplifyShaderEditor/` | 유료 | Standard Unity Asset Store EULA·조건부 | [Asset Store 68570](https://assetstore.unity.com/packages/tools/visual-scripting/amplify-shader-editor-68570) |
| Maintainer 2 | 2.3.3 | Code Stage | `Tools/CodeStage/Maintainer/` | 유료 | Standard Unity Asset Store EULA·조건부 | [Asset Store 310179](https://assetstore.unity.com/packages/tools/utilities/maintainer-2-310179) |
| Mesh Baker | 소스 버전 문자열 3.39.0(`MB3_MBVersionConcrete`), 동봉 API 문서 3.37.0 | Ian Deane / Digital Opus | `Tools/MeshBaker/` | 유료 | Standard Unity Asset Store EULA / Extension Asset·조건부 | [Asset Store 5017](https://assetstore.unity.com/packages/tools/modeling/mesh-baker-5017) |
| CombatGirls Sword Character Pack | 1.3.1(`CombatGirls_SwordCharacterPack`) | 미확인 | `Animation/CombatGirlsCharacterPack/` | 유료 | Standard Unity Asset Store EULA·조건부 | Asset Store productId 345454 |
| Magical-Knight AnimSet | 1.23(`Magical-Knight AnimSet`) | 미확인 | `Animation/Magical-Knight_Set/` | 미확인(`AssetOrigin` productId 180708, Asset Store 패키지) | 미확인 | Asset Store productId 180708, 설치 FBX `.meta` |
| P09 Modular Humanoid | 미확인(`AssetOrigin` 없음) | 미확인 | `Model/P09_Modular_Humanoid/` | 미확인 | 미확인 | 미확인 |
| Succubus Anim Set | 1.21(`Succubus Anim Set`) | 미확인 | `Animation/Succubus_Anim_Set/` | 미확인(`AssetOrigin` productId 179271, Asset Store 패키지) | 미확인 | Asset Store productId 179271 |
| Kitsune Yuna (월백 외형) | 1.0(`Kitsune Yuna`) | 미확인(폴더명 `Ida Faber`) | `Model/Ida Faber/Kitsune Yuna/` | 미확인(`AssetOrigin` productId 265718, Asset Store 패키지) | 미확인 | Asset Store productId 265718 |
| Stylish Archer Assets Pack | 1.4 | 미확인(설치 폴더명 `Grruzam Archer Animation`) | `Animation/Grruzam Archer Animation/` | Asset Store 패키지, 유·무료 미확인 | 미확인 | 설치 `.meta`: productId 249931 |
| Sword slashes PRO | 3.0 | 미확인(설치 상위 폴더명 `Hovl Studio`) | `VFX/Hovl Studio/` | Asset Store 패키지, 유·무료 미확인 | 미확인 | 설치 `.meta`: productId 173450 |
| Item Pickup VFX - URP | 1.0.20232 | 미확인 | `VFX/Item Pickup VFX URP/` | Asset Store 패키지, 유·무료 미확인 | 미확인 | 설치 `.meta`: productId 241404 |
| Magic Slashes FX | 1.0 | 미확인 | `VFX/Magic Slashes FX/` | Asset Store 패키지, 유·무료 미확인 | 미확인 | 설치 `.meta`: productId 114452 |
| Master Stylized Projectile | 1.0 | 미확인 | `VFX/MasterStylizedProjectiles/`, 일부 공용 파일 `VFX/Hovl Studio/HSFiles/` | Asset Store 패키지, 유·무료 미확인 | 미확인 | 설치 `.meta`: productId 255608 |
| Elemental Spells Full Pack VFX | 1.0.1 | 미확인(설치 상위 폴더명 `PixPlays`) | `VFX/PixPlays/` | Asset Store 패키지, 유·무료 미확인 | 미확인 | 설치 `.meta`: productId 297318 |
| Simple Water Shader URP | 1.0.0 | 미확인 | `VFX/Simple Water Shader/` | Asset Store 패키지, 유·무료 미확인 | 미확인 | 설치 `.meta`: productId 191449 |

### 추가 설치·버전 대조 — 2026-10-08

4개 분류의 패키지 폴더 22개와 파일 6,985개(`.meta` 제외)를 전수 열거했다. 대장에 없던 궁수 애니메이션 1종과 VFX·셰이더 6종을 추가했다. 아래 숫자는 재귀 파일 수로, 폴더 `.meta`·파일 `.meta`는 제외하며 프리팹 수에 데모·공용 부품·렌더 파이프라인 변형이 포함될 수 있다. 독립 이펙트 수나 실제 전투에서 사용할 기술 수를 뜻하지 않는다.

| 신규 패키지 경로 (`Assets/99.ThirdParty/` 기준) | 파일 | FBX | 프리팹 | 정적 확인한 구성 |
|---|---:|---:|---:|---|
| `Animation/Grruzam Archer Animation/` | 474 | 466 | 5 | Animation 462 FBX, Modeling 4 FBX, 컨트롤러 1·마스크 1·씬 1 |
| `VFX/Hovl Studio/` | 253 | 26 | 35 | `Sword slash VFX/` 및 공용 `HSFiles/` |
| `VFX/Item Pickup VFX URP/` | 137 | 5 | 20 | 픽업 VFX·오디오·데모·PDF 3개·추가 unitypackage 1개 |
| `VFX/Magic Slashes FX/` | 430 | 13 | 94 | PC/Mobile 데모·참격 프리팹·캐릭터·셰이더 |
| `VFX/MasterStylizedProjectiles/` | 556 | 37 | 104 | 투사체 프리팹·Shader Graph·데모 |
| `VFX/PixPlays/` | 261 | 19 | 62 | AOE·Auras·Beams·Blast·Projectiles·Shields 6계열과 Components |
| `VFX/Simple Water Shader/` | 17 | 0 | 2 | Water Shader Graph·반사 카메라·수면 메시·데모 |

- 위 22개 폴더는 Animation 4, Model 2, Tools 9, VFX 7이다. 공급자 상위 폴더와 동봉 의존 라이브러리는 표의 패키지 단위로 묶었다. `Hovl Studio/HSFiles/Materials/Flash21bcg.mat`와 `HSFiles/Textures/Circle116.png`는 productId 255608로 기록되어 있어 Master Stylized Projectile과 Sword slashes PRO의 공용 디렉터리 출처가 섞여 있다. 이 두 파일은 Hovl 폴더의 253개에만 세며 중복 설치로 단정하지 않는다.
- Raycast Pro는 기존 기록 `1.1.5A`에서 현재 설치 `1.1.5B`, Wingman은 `1.1.0`에서 `1.4.0`, Hierarchy Designer는 `3.2.2`에서 `3.2.3`으로 정정했다. 설치 `.meta` 기준이며 업데이트 시각·최신 판매 버전·동작 호환성은 확인하지 않았다.
- 과거 제거 기록과 달리 현재 `Tools/RaycastPro/_Demo/`와 `Tools/Hierarchy Designer/Demo/`가 존재한다. 과거 정리 결과는 이력으로 보존하고 현재 설치 상태와 구분한다. 이번에 데모를 삭제하거나 재배치하지 않았다.
- 신규 7종의 제작자 공식 표기·구매 증빙·취득일·좌석·적용 EULA·상업 이용 권리는 미확인이다. 폴더명과 AssetOrigin은 설치 식별 근거일 뿐 권리 보유 근거가 아니다. 외부 원본은 읽기 전용으로 조사했다.
- 검증: 파일/확장자 집계, 전체 `.meta`의 productId·packageName·packageVersion 대조, 현재 Git 제외 규칙 확인(L1, 증거 등급 1). Unity 임포트·URP 셰이더 표시·애니메이션 리타게팅·이펙트 성능·게임 및 서버 연결·빌드는 미검증이다.

### 신규 VFX의 렌더 파이프라인 주의 — 2026-10-08

PixPlays의 Elemental Spells Full Pack VFX 설치 트리에는 `Version_BuiltIn`·`Components_BuiltIn`·`*_BuiltIn.unity`가 전개되어 있고, AOE/Auras/Beams/Blast/Projectiles/Shields 각 계열의 URP 버전은 `Elemental*_URP.unitypackage` 6개로 포함되어 있다. 별도 URP 버전 폴더가 전개된 것은 확인되지 않았다. 패키지 포함과 URP 적용 완료를 구분한다.

Magic Slashes FX의 `.shader` 6개에는 URP 파이프라인 태그가 없다. 이는 호환 검토가 필요한 정적 신호이며 실제 URP 표시 실패를 재현했다는 뜻은 아니다. 이번에는 unitypackage를 추가 임포트하거나 셰이더를 변경하지 않았다. 두 팩 모두 프로젝트 URP에서 실제 표시·재질·성능 검증이 필요하다.

### 궁수 애니메이션 설치 조사 — 2026-10-08

`Stylish Archer Assets Pack`의 Animation 폴더에는 이동 252, 공격 109, 스킬 46, 피격 23, 넘어짐 29, 사망 3, 합계 462개 FBX가 있다. 모델 4개를 더한 전체 FBX는 466개다. 원본/Root/Inplace·방향·연결 구간 변형을 독립 기술로 세지 않는다. 기술과 부적 설계는 [캐릭터 애니메이션 설계](../design/character-animation-design.md)를 따른다.

전체 FBX의 `ModelImporter.animationType`은 Humanoid 456개, Generic 10개다. Animation 소속은 Humanoid 454개, Generic 8개이며 Generic 8개는 `Animation/Archer/1_Movements/2__Walk/Inplace/Archer@Walk_{BL45,BR45,Bwd,FL45,FR45,Fwd,L,R}_Root.FBX`다. 나머지 Generic 2개는 Modeling의 Bow·Arrow다. `Inplace` 폴더명만으로 Humanoid 호환이나 실제 루트 정지를 확정하지 않는다. 프로젝트 사본의 리타게팅·활과 화살 소켓·시위 동기화·발 미끄러짐·실제 발사 시점은 Unity 재생 검증이 필요하며 이번 조사에서는 수행하지 않았다.

### 캐릭터 애니메이션 설치 조사 — 2026-10-07

`Assets/99.ThirdParty/Animation/`의 실제 파일과 FBX `.meta`, 동봉 PDF를 읽기 전용으로 확인했다. 아래 수는 설치 파일 수이며 독립 기술 수나 재생 품질을 뜻하지 않는다. 클립 배정·기술·부적 개편은 [캐릭터 애니메이션 설계](../design/character-animation-design.md)를 따른다.

| 패키지 | 전체 FBX | 애니메이션 FBX·명시 클립 | 구성과 적용 조건 |
|---|---:|---:|---|
| CombatGirlsCharacterPack | 49 | 36 | 감정 7, Normal 17, Special 12. 나머지 FBX는 캐릭터·분리 부품·환경 모델이며 기술로 세지 않는다 |
| Magical-Knight_Set | 240 | 239 | T-pose 1, 원본 119, Inplace 119. 전체 콤보·분절, 시전 시작·유지·종료, Z 변형을 포함한다 |
| Succubus_Anim_Set | 152 | 151 | T-pose 1, 원본 75, Inplace 75. 비행 이동과 흡수 공격·피격자 클립을 포함한다 |

애니메이션 FBX 426개의 `ModelImporter.animationType: 3`(Humanoid)과 Avatar 참조 대상을 정적으로 확인했다. CombatGirls 36개는 `Humanoid_Bot/Models/Humanoid_F.fbx`를, Magical 238개와 Succubus 150개는 각 패키지의 T-pose FBX를 참조하며 해당 GUID의 파일이 존재한다. 두 T-pose는 자체 Avatar 생성 설정이다. 이 확인은 Avatar의 런타임 유효성이나 P09에서의 리타게팅 성공을 증명하지 않는다.

Magical과 Succubus의 동봉 `Animation Asset Description.pdf`는 각각 일반 설명 1쪽으로 Root Motion·In-Place 제공을 명시하지만 기술별 동작·타격 시점은 설명하지 않는다. 실제 `.meta`에서는 원본과 Inplace 양쪽의 비 T-pose 클립에 `loopTime: 1`, `loopBlendPositionXZ/Y: 1`이 설정되어 있다. 파일명만으로 현재 루트 이동이나 원샷 종료를 확정하지 않고, 프로젝트 사본에서 이동·시전 유지의 반복과 공격·사망·시전 시작/종료의 단발 재생을 구분해 검증한다. 원본/Inplace와 분절·변형을 별도 신규 기술로 계산하지 않는다.

CombatGirls 원본의 `SwitchSocket` 이벤트 39개는 무기 소켓 전환용이다. Magical·Succubus의 명시 이벤트는 0개이며 이 이벤트 목록은 서버 타격 판정 자료가 아니다. 기존 PlayerP09 프로젝트 사본 13개는 `m_Events: []`, 대기·걷기·달리기 반복과 공격·기술·회피·피격·사망 단발 설정을 정적으로 확인했다. 이번 조사에서 원본 패키지·사본 클립·컨트롤러·프리팹은 변경하지 않았다.

검증 범위는 파일 목록·메타·기존 사본 설정·PDF 전체 페이지의 정적 확인(L1, 증거 등급 1)이다. 이번 Unity 재생, 동작별 손·검 궤적·발 미끄러짐·루트 이동, 실제 기술 타격 시점과 게임·서버·빌드 검증은 미실행·미검증이다. Magical의 제작자·구매 증빙·좌석·적용 라이선스는 확인하지 않았으며 동봉 저작권 안내를 상업 이용 권리 확인으로 취급하지 않는다. DOTween의 현재 설치는 `Tools/Demigiant/{DemiLib,DOTween,DOTweenPro}/`와 그 아래 설정 파일 존재를 확인했으며, 이번 조사가 과거 Unity 설정 로드 성공을 현재 재실행한 것은 아니다.

### 플레이어 캐릭터 파생물 — 2026-10-06

플레이어 캐릭터는 P09 Modular Humanoid 모델에 CombatGirls 애니메이션을 입혀 쓴다. 두 패키지는 Git에서 제외되므로 팀원은 공유 드라이브에서 받아 위 경로에 설치해야 캐릭터가 보인다. 다음 파생물은 원본 패키지에서 복사·가공해 Git에 커밋한다. 원본 패키지는 수정하지 않았다.

- `Assets/01.Scripts/05.Art/Characters/PlayerP09/Materials/`: 원본 lilToon 재질을 URP Lit로 옮긴 사본. 원본 텍스처를 GUID로 참조한다.
- `Assets/01.Scripts/05.Art/Characters/PlayerP09/Textures/`: 원본 HDRP 마스크 PNG를 복사해 선형으로 다시 임포트한 사본.
- `Assets/01.Scripts/05.Art/Characters/PlayerP09/Animations/Clips/`: CombatGirls FBX 클립을 `.anim`으로 복사하고 이벤트를 지운 사본.
- `Assets/02.Prefab/Characters/PlayerP09/PF_PlayerP09.prefab`: 원본 `P09_Human` 프리팹 변형.

### 월백 대기 모션 파생물 — 2026-10-06

월백은 Kitsune Yuna 모델에 Succubus Anim Set의 `idle_inplace` 클립을 입혀 쓴다. 두 패키지는 Git에서 제외되며, 다음 파생물은 원본 패키지에서 복사·가공해 Git에 커밋한다. 원본 패키지는 수정하지 않았다.

- `Assets/01.Scripts/03.AI/Boss/Animation/AN_Wolbaek_Idle.anim`: `idle_inplace.fbx` 클립을 `.anim`으로 복사하고 이벤트를 지운 사본. 월백에 맞게 손목·손가락·허리·다리·목 근육값을 줄이거나 고정했다.
- `Assets/01.Scripts/03.AI/Boss/Animation/AV_Wolbaek.asset`: 원본 모델의 휴먼 설정을 읽어 팔 뼈 4개의 기준 자세만 T-포즈로 바꾼 Avatar. 원본 FBX의 import 설정은 바꾸지 않았다.
- `Assets/01.Scripts/03.AI/Boss/Animation/AC_Wolbaek.controller`: `Idle` 상태 하나뿐인 프로젝트 소유 컨트롤러.

Succubus Anim Set과 Kitsune Yuna의 제작자·라이선스 조건·구매 증빙은 이 대장에서 확인하지 못했다. 위 복사본을 저장소에 두는 것이 두 패키지의 사용 조건에 맞는지는 미확인이다.

P09의 원본 판매처·버전·라이선스와 두 패키지의 구매 증빙은 이 대장에서 확인하지 못했다. 원본 패키지의 ReadMe는 lilToon 셰이더를 요구하지만 프로젝트는 URP Lit 사본을 써 lilToon을 설치하지 않는다.

UMA 3.1f1(GitHub 릴리스 `V3.1f1`)은 2026-10-01 들였다가 같은 날 쓰지 않기로 하고 뺐다. 연결 코드 `AvatarView`, `Shinshi.Runtime`의 `UMA_Core` 참조, `.gitignore`의 `Assets/UMA/` 예외, UMA 태그(`UMAKeepChain`, `UMAIgnore`), `PlayerFemaleModular/UMA/` 연동 에셋을 함께 지웠다. `Assets/UMA/`를 받아 둔 작업자는 로컬에서 직접 지운다.

설치 버전은 `.meta`의 `AssetOrigin.productId`, `packageName`, `packageVersion`으로 확인했다. Mesh Baker는 해당 메타데이터가 없고 동봉 문서가 `docs/API-3.37.0.zip`이다. 소스의 버전 문자열은 3.39.0이다. 2026-10-01 팀장이 유료판 결제를 확인해 에디션을 유료로 기록한다. 결제 증빙과 팀원별 사용 좌석은 이 대장에서 검증하지 않았다. 유료 에셋이므로 다른 유료 에셋과 같이 패키지를 Git에서 제외하고 설치할 때마다 받는다. 환경 메시 결합에 쓰려고 `Shinshi.Runtime`이 `MeshBakerCore` 어셈블리를 참조한다.

## 공용 돌바닥·부유석 제작 소재와 가공물

2026-10-01 사용자 제공 참고 시트와 실사 판타지 제작 요청을 기준으로 아래 소재를 취득·가공했다. Asset Store 설치 패키지와 구분하여 출처·사용조건과 배치용 경로를 기록한다. 후속 제작은 [아트 제작 기본 과정](../unity/ART_ASSET_PRODUCTION_WORKFLOW.md)을 따른다.

| 에셋 | 버전·제작 기록 | 제작자 | 보관·사용 경로 | 구분 | 라이선스·상업 이용 | 출처 |
|---|---|---|---|---|---|---|
| Rock 01 석재 PBR | 2026-10-01 취득, 4K Base Color·Roughness·OpenGL Normal·Height | Rob Tuytel / Poly Haven | 원본은 Git 제외 개인 작업 영역에 보존, 돌바닥 가공 재질에 사용 | 외부 무료 소재 | CC0, 상업 이용·수정·재배포 가능 | [공식 자산](https://polyhaven.com/a/rock_01), [라이선스](https://polyhaven.com/license) |
| 공용 돌바닥 | 2026-10-01 Blender MCP 제작, 6×6m 모듈·3LOD FBX·PBR/AO 출력 | shinshi 프로젝트 제작 | 모델·재질 `Assets/01.Scripts/05.Art/Environment/StoneCourtyard/`, 프리팹 `Assets/02.Prefab/Environment/StoneCourtyard/` | 프로젝트 제작 메시·가공 재질 | 석재 표면 원본은 위 CC0. 자체 메시·베이크와 외부 소재를 구분하여 관리 | [참고 CA-05](../design/concept-art.md#6-ca-05-공용-돌바닥-타일), [배치 프리팹](../../Assets/02.Prefab/Environment/StoneCourtyard/PF_StoneCourtyardTile.prefab) |
| 장식용 부유석 | 2026-10-01 Meshy 7.1 생성·Blender 3LOD/PBR/AO 가공 | Meshy 생성 + shinshi Blender 가공 | 원본은 Git 제외 개인 작업 영역에 보존, 모델·재질 `Assets/01.Scripts/05.Art/Environment/FloatingRock/`, 프리팹 `Assets/02.Prefab/Environment/FloatingRock/` | AI 생성 원본·프로젝트 가공물 | 생성 당시 계정 플랜 미확인. 플랜별 조건·입력 권리 확인 상태는 아래 기록 | [참고 CA-06](../design/concept-art.md#7-ca-06-부유석), [Meshy 소유 안내](https://help.meshy.ai/en/articles/10137554-what-is-the-ownership-of-the-generated-models), [이용 약관](https://www.meshy.ai/terms-of-use) |

- Rock 01 원본 PNG 네 개는 4096×4096이며 공식 파일 API의 MD5와 모두 일치한다. 시각 확인용 썸네일은 런타임 소재에 포함하지 않았다.
- 참고 그림은 사용자 제공이다. 작가·제작 도구·라이선스와 별도 권리 확인은 미확인으로 기록하고 소유권을 추측하지 않는다.
- Meshy 공식 안내는 유료 플랜 생성물의 비공개 소유와 무료 플랜 생성물의 CC BY 4.0·Meshy 표시 조건을 구분한다. 실제 생성 계정 플랜을 CLI에서 확인하지 못했으므로 이 부유석을 자체 소유 또는 CC0로 확정하지 않는다. 커뮤니티 공개는 요청하거나 수행하지 않았다. [상업 이용 안내](https://help.meshy.ai/en/articles/16102098-can-i-use-meshy-assets-commercially)
- 원본 GLB·FBX, 다운로드 텍스처, Blender `.blend`와 제작 기록은 Git 제외 개인 작업 영역에 보존한다. 런타임 모델·재질·텍스처는 [팀 모델링 에셋 규칙](../unity/SCENE_AND_ASSET_SAFETY.md#팀이-만든-모델링-에셋)에 따라 `05.Art/Environment/`, 배치용 프리팹은 `02.Prefab/Environment/`에서 `.meta`와 함께 공유한다. 모델 제작은 프리팹 생성·참조 검증까지 포함한다.
- 런타임 모델명은 `StoneCourtyard/Models/SM_StoneCourtyardTile_{High,Medium,Low}.fbx`, `FloatingRock/Models/SM_FloatingRock_{High,Medium,Low}.fbx`다. 단계는 LOD0·1·2에 대응한다. [돌바닥 프리팹](../../Assets/02.Prefab/Environment/StoneCourtyard/PF_StoneCourtyardTile.prefab)과 [부유석 프리팹](../../Assets/02.Prefab/Environment/FloatingRock/PF_FloatingRock.prefab)을 배치하고, 상세 디자인·제작 기록·렌더는 개인 docs에 보관한다.
- Unity 임포트·URP Lit PBR/AO 연결·LOD·메타와 에디터 프리뷰를 확인했다. 게임플레이·성능·빌드·배포 검증과 Meshy 계정 플랜 확인은 완료하지 않았다.

## 공용 석등 생성·가공물

| 에셋 | 생성·가공 | 보관·사용 | 권리 확인 상태 | 근거 |
|---|---|---|---|---|
| StoneLantern | 2026-10-01 Meshy 7.1 생성·Blender 3LOD·실제 AO·창 Emission 베이크 | 원본은 Git 제외 개인 작업 영역에 보존, 런타임 `Assets/01.Scripts/05.Art/Props/StoneLantern/`, 배치 프리팹 `Assets/02.Prefab/Props/StoneLantern/` | ChatGPT 생성 단독 석등 입력·Meshy 출력·프로젝트 가공물. Meshy 생성 계정 플랜 미확인, CC0·독점 소유로 확정하지 않음 | [배치 프리팹](../../Assets/02.Prefab/Props/StoneLantern/PF_StoneLantern.prefab), [Meshy 소유 조건](https://help.meshy.ai/en/articles/10137554-what-is-the-ownership-of-the-generated-models), [약관](https://www.meshy.ai/terms-of-use) |

디자인 컨셉·생성 프롬프트는 개인 docs에 보관한다. 에셋 대장은 실제 사용 모델의 출처·조건을 기록하며 개인 시안 이미지를 게시하지 않는다. 외부 폴리헤이븐 석재를 이 석등 재질에 추가로 사용하지 않았다.

## 건축·매다는 등 시제품

| 에셋 | 생성·가공 | 런타임 보관·사용 조건 |
|---|---|---|
| TiledHouse | Meshy 생성·Blender 가공. 가공 3LOD는 59,999 / 32,998 / 16,799삼각형 | `Assets/01.Scripts/05.Art/Environment/TiledHouse/`. 3FBX·7PNG(PBR/AO·Alpha), 재질·프리팹 미구성 |
| RedGate | Meshy 생성·Blender 가공. 가공 3LOD는 40,000 / 22,000 / 11,200삼각형 | `Assets/01.Scripts/05.Art/Environment/RedGate/`. 3FBX·6PNG(PBR/AO), 재질·프리팹 미구성 |
| HangingLantern | Meshy 생성·Blender 가공. 가공 3LOD는 23,000 / 19,550 / 16,099삼각형 | `Assets/01.Scripts/05.Art/Props/HangingLantern/`의 3FBX·8PNG·재질, [장식용 프리팹](../../Assets/02.Prefab/Props/HangingLantern/PF_HangingLantern.prefab). Collider 없음 |

2026-10-01 커밋 전 정적 확인에서 세 모델의 FBX는 가공 출력과 SHA-256이 일치했고 메타·텍스처 임포트 설정을 확인했다. 매다는 등은 Emission 맵·색이 연결됐으나 `_EMISSION` 키워드가 없어 현행 URP Lit에서 발광하지 않는다. 최신 LOD의 시각·전환·게임플레이·성능·빌드는 이번 검토에서 미실행이다. 건축 두 모델은 배치용 프리팹이 완성되기 전 중간 산출물이다.

생성 원본·입력·상세 기록은 Git 제외 개인 작업 영역에 보존한다. Meshy 생성 계정 플랜·입력 권리·배포 조건은 미확인으로 유지하며 CC0·독점 소유를 주장하지 않는다. [Meshy 소유 조건](https://help.meshy.ai/en/articles/10137554-what-is-the-ownership-of-the-generated-models)과 [약관](https://www.meshy.ai/terms-of-use)을 실제 계정 조건과 대조해야 한다.

## 여성 베이스 외형 시제품

| 에셋 | 생성·가공 | 보관·사용 | 권리 확인 상태 | 근거 |
|---|---|---|---|---|
| PlayerFemaleBasePreview | 2026-10-01 Meshy 7.1 전신·머리 생성. 전신을 Blender에서 정리하고 3LOD·PBR·실제 국소 AO 제작 | 원본·입력·상세 기록은 Git 제외 개인 작업 영역에 보존, 런타임은 `Assets/01.Scripts/05.Art/Characters/PlayerFemaleBasePreview/`, 프리팹은 `Assets/02.Prefab/Characters/PlayerFemaleBasePreview/`. 별도 머리는 미채택·미조립으로 원본만 보존 | ChatGPT 생성 참조 입력·Meshy 생성 출력·프로젝트 가공물. 생성 계정 플랜과 권리 조건 확인 미완료, CC0·독점 소유로 확정하지 않음. 커뮤니티 공개·배포 수행 안 함 | [외형 프리팹](../../Assets/02.Prefab/Characters/PlayerFemaleBasePreview/PF_PlayerFemaleBasePreview.prefab), [Meshy 소유 조건](https://help.meshy.ai/en/articles/10137554-what-is-the-ownership-of-the-generated-models), [약관](https://www.meshy.ai/terms-of-use) |
| PlayerFemaleModular | 2026-10-01 Meshy 자동 리깅·원본 클립에 프로젝트 목 분리·3LOD·Blink·표면·AO/노멀·걷기 교정 적용. UMA 연동물은 미사용 결정으로 삭제 | 런타임 모델·애니메이션·재질·교정 맵은 `Assets/01.Scripts/05.Art/Characters/PlayerFemaleModular/`, PF는 `Assets/02.Prefab/Characters/PlayerFemaleModular/`. 원본 BaseColor·거칠기·MetallicSmoothness 의존 `PlayerFemaleBasePreview/` 함께 보존. 원본·상세 기록은 Git 제외 개인 작업 영역 | 위 입력·생성물의 확인 상태를 승계하며 새로운 독점 권리·CC0를 주장하지 않음. 자동 리깅·원본 클립 출처는 Meshy, 메시·눈꺼풀·걷기 교정은 프로젝트 작업. 계정 플랜·배포 조건 확인 미완료 | [모듈형 프리팹](../../Assets/02.Prefab/Characters/PlayerFemaleModular/PF_PlayerFemaleModular.prefab), [Meshy 리깅 API](https://docs.meshy.ai/en/api/rigging), [Meshy 소유 조건](https://help.meshy.ai/en/articles/10137554-what-is-the-ownership-of-the-generated-models) |

`PlayerFemaleBasePreview`는 리깅·애니메이션·얼굴 모프·Collider가 없는 정적 시제품이다. 후속 `PlayerFemaleModular`는 24뼈·머리/몸 분리·Blink·검토용 걷기/달리기와 Humanoid 임포트를 포함한다. 보행 침투·달리기 손/몸 겹침은 남아 있다. 수정된 몸·머리 쌍의 AO/노멀은 다른 부품 조합에서 재검증해야 한다. 얇은 보조 눈꺼풀의 별도 노멀 맵·고정 AO 생략은 시제품 예외다.

**외형 판정은 REJECT, 신규 채택·연동은 보류**다. UMA 미사용 결정으로 UMA3 연동 에셋과 검토 종족을 삭제했다. 기존 PF와 카탈로그·런타임 기본 선택은 유지했다. 두 시제품은 Collider가 없으며 최종 얼굴·전체 커스터마이징·보행·GPU 재생·게임플레이·성능·빌드·상업 배포는 미완료다. 의존 경로와 사용 조건은 [모델 제작 목록](../design/model-production-list.md)을 따른다.

## 신시 마을 지형 시제품

| 에셋 | 제작·소재 | 런타임 보관·사용 | 권리 구분 |
|---|---|---|---|
| Aerial Grass Rock 지면 PBR — 이전 시제품 이력 | 2026-10-01 취득·2K JPG 원본 보존. 이전 상부 지면에서 Diffuse/Height/Roughness를 사용했고 OpenGL Normal 원본은 보존만 함 | 원본·상세 제작 이력은 개인 작업 영역. 현행 native 부유석 키트의 표면에는 추가 지면 맵을 사용하지 않음 | Rob Tuytel / Poly Haven, 외부 CC0 소재. [공식 자산](https://polyhaven.com/a/aerial_grass_rock), [CC0 라이선스](https://polyhaven.com/license) |
| VillageTerrain v2 — 이전 통합 시제품 | 기존 Meshy FloatingRock 암석 조립 + Blender 상부·3LOD·접합 AO. Aerial Grass Rock/Rock 01 사용 | `Assets/01.Scripts/05.Art/Environment/VillageTerrain/`의 기존 `PF_VillageTerrain`·`Dev_ShinshiVillageTerrain`는 이력이며 새 키트 배치 대상으로 사용하지 않음 | 프로젝트 제작 상부·가공 재질, 위 CC0 소재와 기존 Meshy 원본 조건을 구분 |
| VillageTerrain 독립 부유석 키트 | 같은 Meshy 원본의 Core/E/NE/NW/W/SW/SE 변형7개, 원래 암석·이끼 표면·공유PBR·자체AO7·렌더3LOD·숨은 상단충돌7 | `Assets/01.Scripts/05.Art/Environment/VillageTerrain/{Models,Textures,Materials}/Chunks/`의28FBX·7AO·7재질, [조립용7PF](../../Assets/02.Prefab/Environment/VillageTerrain/Chunks/), [검수 갤러리](../../Assets/00.Scenes/Dev_FloatingIslandKit.unity) | 기존 FloatingRock의 Meshy 계정 플랜 미확인 상태를 승계. 가공·AO 제작 기록과 원본 조건을 구분하며 전체를 CC0·독점 소유로 확정하지 않음 |

설계·Blender 원본·상세 제작 기록·렌더와 취소된 시제품은 개인 docs에 보관한다. 현행 키트는 추가 가시 상부판 없이 개별 조각을 제작하고 사용자가 Unity에서 조립한다. 자체 AO는 고정 조립의 접합 AO와 구분한다. Unity7PF·임포트/참조/메타·지정 ray/강제LOD·고정 카메라 시각검사를 완료했고 기존 FloatingRock·공유표면 맵·보호64파일을 보존했다. 갤러리는 완성맵이 아니다. 숨은 상단충돌은 선택면용 정적 Non-Convex로 모든 가시경사/선반의 이동을 보장하지 않는다. 실제 맵조립·보행·다리/물/건물·게임플레이·연속LOD·성능·빌드·서버 통합은 미검증이다.

## 기본 패키지와 위치 예외

- `Packages/`의 Unity 기본 패키지와 MCP for Unity는 UPM이 관리하므로 Asset Store 폴더로 옮기지 않는다. 실제 버전·Git 의존성은 `Packages/manifest.json`, 고정 해시는 `Packages/packages-lock.json`이 기준이다.
- 주요 설치 버전: URP 17.4.0, Input System 1.19.0, Cinemachine 3.1.7, AI Navigation 2.0.12, Unity Test Framework 1.6.0, Timeline 1.8.12, uGUI 2.0.0, Visual Scripting 1.9.11. 각 패키지의 동봉 LICENSE를 따른다.
- Mirror 96.11.3은 무료 오픈소스(MIT, `Assets/Mirror/LICENSE`)이며 [공식 GitHub 릴리스](https://github.com/MirrorNetworking/Mirror/releases/tag/v96.11.3)의 `.unitypackage`로 2026-09-30 설치했다. 팀 전원이 같은 버전을 써야 하므로 Git에 포함하고, Mirror 기본 위치인 `Assets/Mirror/`를 유지한다(재임포트·업데이트 경로와 같음). 동봉 스크립트 템플릿(`ScriptTemplates/`)은 팀 템플릿과 섞이지 않도록 설치하지 않았다. 동봉 트랜스포트·예제 에셋의 개별 라이선스(`Transports/*/LICENSE`, `Examples/_Common/` 내 고지)는 원본대로 보존한다.
- `Assets/Plugins/TextMesh Pro/`는 TMP 필수 폰트·리소스·셰이더이며 데모가 아니다. 2026-10-01 Unity 에디터에서 `Assets/TextMesh Pro/`에서 옮겼다(GUID 유지). `TMP Settings`는 `Resources.Load`로 찾으므로 `Resources/` 구조와 파일명, 동봉 라이선스를 유지한다. TMP 필수 리소스를 다시 임포트하면 `Assets/TextMesh Pro/`에 새로 풀리니 중복을 지우거나 이 위치로 옮긴다.
- `Assets/Gizmos/`의 `DialogueDatabase Icon.png`, `StringAsset Icon.png`, `TextTable Icon.png`는 Dialogue System의 에디터 아이콘이다. 플러그인 코드와 Unity Gizmos가 이 위치를 사용하므로 유지하고 유료 패키지와 함께 Git에서 제외한다.
- DOTween 설정의 2026-10-07 확인 경로는 `Assets/99.ThirdParty/Tools/Demigiant/DOTween/Resources/DOTweenSettings.asset`이다. `Resources.Load("DOTweenSettings")`가 참조하는 파일명과 `Resources/` 구조를 유지한다. 아래 실제 검증 표의 로드 성공은 2026-09-30 정리 당시 결과이며 이번에 재실행하지 않았다.

## 정리 내역과 유지한 의존 에셋

아래는 2026-09-30 정리 당시 이력이다. Unity `AssetDatabase.GetDependencies`로 삭제 후보 밖에서 들어오는 참조를 검사한 뒤 에디터 API로 이동·삭제했다. 현재 재설치된 데모 유무는 2026-10-08 대조 결과를 우선한다.

| 원래 위치 | 처리 |
|---|---|
| `Assets/TutorialInfo/`, `Assets/Readme.asset` | URP 템플릿 안내·튜토리얼 제거 |
| `Hierarchy Designer/Demo/` | 예제 씬 제거 |
| `MeshBaker/Examples/`, `MeshBaker/Temp/` | 예제·임시 산출물 제거. `MeshBakerWorkingFolder/DONT_DELETE.txt`와 도구 본체는 보존 |
| `Demigiant/DOTweenPro Examples/` | 예제 제거 |
| `Pixel Crushers/Dialogue System/Demo/` | 아래 필요한 모델 의존성을 보존한 뒤 데모 제거 |
| `RaycastPro/_Demo/` | 아래 필요한 텍스처를 보존한 뒤 데모 제거 |
| `CodeStage/Maintainer/Demo/` | 데모 제거 |
| 빈 `Assets/Plugins/`, `Assets/Resources/` | 모든 내용 이동 후 제거 |

Dialogue System의 `Prefabs/Camera Angle Studio/Camera Angle Studio.unity`가 데모의 Recon Troop 모델과 머티리얼·텍스처를 참조했다. `Demo/Art/Recon Troop/`를 `Prefabs/Camera Angle Studio/Recon Troop/`로 옮겨 GUID를 보존했다. RaycastPro의 `Gizmos/Stamp.mat`가 쓰던 `_Demo/_Resources/Stamp.psd`는 `Gizmos/Stamp.psd`로 옮겼다. 외부 연동용 `.unitypackage`, 에디터 UI 리소스와 Console Pro의 Unity 버전별 DLL은 필요 시 사용되는 구성이라 보존했다.

## 승인된 경로 보정과 DOTween 설정

2026-09-30 사용자가 외부 원본의 **경로 문자열 7곳만 보정**하도록 승인했다. 동작 로직과 타입·필드·메서드 이름은 바꾸지 않았다. 에셋 업데이트 시 덮어써질 수 있으므로 다음 보정을 다시 확인한다.

| 파일 (`Assets/99.ThirdParty/` 기준) | 이전 접두 경로 | 현재 접두 경로 | 치환 수 |
|---|---|---|---:|
| `Tools/MeshBaker/Editor/core/MB_EditorUtil.cs` | `Assets/MeshBaker` | `Assets/99.ThirdParty/Tools/MeshBaker` | 3 |
| `Tools/Pixel Crushers/Dialogue System/Scripts/Editor/Tools/Setup Wizards/DialogueManagerWizard.cs` | `Assets/Plugins/Pixel Crushers/Dialogue System` | `Assets/99.ThirdParty/Tools/Pixel Crushers/Dialogue System` | 2 |
| `Tools/Pixel Crushers/Dialogue System/Scripts/Editor/Utility/EditorTools.cs` | `Assets/Plugins/Pixel Crushers/Dialogue System` | `Assets/99.ThirdParty/Tools/Pixel Crushers/Dialogue System` | 2 |

2026-10-01 사용자가 Pixel Crushers asmdef의 **참조 추가만** 승인했다. 프로젝트에 `TMP_PRESENT`·`USE_NEW_INPUT`이 정의되어 있어 동봉 asmdef의 빈 참조로는 TMP·Input System 코드가 컴파일되지 않고, 이를 참조하는 `Shinshi.Runtime`도 막혔다. 소스와 다른 asmdef 항목은 바꾸지 않았다. 동봉 `CommonAssemblyDefinitions.unitypackage`를 다시 가져오거나 에셋을 업데이트하면 덮어써지므로 아래 참조를 다시 확인한다.

| 파일 (`Assets/99.ThirdParty/` 기준) | 추가한 참조 |
|---|---|
| `Tools/Pixel Crushers/Common/PixelCrushers.asmdef` | `Unity.TextMeshPro`, `Unity.InputSystem` |
| `Tools/Pixel Crushers/Common/Scripts/Editor/PixelCrushersEditor.asmdef` | `Unity.TextMeshPro` |

2026-10-03 사용자가 에러 수정을 요청해 Mirror 96.11.3 원본의 **필드 특성 1곳만** 보정했다. `Assets/Mirror/Core/NetworkIdentity.cs`의 `_connectionToClient`에 `[NonSerialized]`를 붙였다. 도메인 리로드 백업이 이 private 필드를 직렬화하며 `NetworkConnection` 생성자의 `Time.time`에서 `get_time is not allowed to be called during serialization` 예외를 냈다(Bootstrap의 네트워크 프리팹이 열려 있으면 컴파일마다 3건). 동작 로직은 바꾸지 않았다. Mirror를 업데이트하면 다시 확인한다.

정리 전 EPO Outline 미설치 상태에서 DOTween EPO 연동이 켜져 컴파일 오류 18개가 있었다. `modules.epoOutlineEnabled=false`로 바꾸고 DOTween 공식 Utility의 `ApplyModulesSettings`를 실행해 연동 컴파일 조건을 비활성화하고 해결했다. 해당 연동 스크립트는 삭제하지 않았다. [공식 설치 안내](https://dotween.demigiant.com/pro.php)는 설치 후 사용할 모듈을 설정하도록 안내한다.

## 외부 모델 텍스처 임포트 상한

`Assets/Editor/ThirdPartyModelTextureLimiter.cs`가 `Assets/99.ThirdParty/Model/` 아래 텍스처를 최대 2048, 압축(CompressedHQ), 밉 스트리밍으로 가져온다. 원본 파일은 바꾸지 않고 임포트 설정만 낮추며 올리지는 않는다. 99.ThirdParty의 `.meta`는 Git에서 제외되므로 규칙이 각자의 에디터에서 적용되고, 이미 가져온 텍스처는 에디터를 열 때 한 번 다시 가져온다. 2026-10-03 기준 Kitsune Yuna(월백 외형)가 4096 무압축이었고, Bootstrap 네트워크 프리팹 목록 → `PF_NetWolbaekBoss` → 월백 외형 참조로 Bootstrap만 열어도 올라오던 텍스처 메모리가 에디터 측정(`Profiler.GetRuntimeMemorySizeLong`) 2,226MB에서 194MB로 줄었다. 근접 화면의 화질 확인은 하지 않았다.

## Git와 재설치

- 2026-10-08 현재 `.gitignore`에는 개별 패키지 규칙 외에 `/Assets/99.ThirdParty/*`가 있어 해당 하위 트리의 미추적 파일을 포괄 제외한다. 신규 7종도 `git check-ignore`로 제외됨을 확인했다. 기존 추적 파일은 이 규칙만으로 추적 해제되지 않는다. 이번 갱신은 현재 상태를 기록하며 Git 제외 규칙 자체는 변경하지 않았다. 재설치는 공유 드라이브와 동일 버전 원본 패키지를 사용한다.
- 재설치할 때 이 대장의 설치 버전을 임포트하고 Unity 에디터에서 위 경로로 옮긴다. 구 위치에 중복 DLL·소스가 남지 않게 확인한다.
- 구매 에셋을 참조하는 팀 씬·프리팹을 공유하기 전 기존 GUID를 유지하는 설치·참조 복원 절차를 확인한다. 이번 SampleScene은 구매 에셋을 참조하지 않는다.
- 원본 복구 백업은 Git에서 제외한 개인 작업 영역에 보존했다. 공유 문서를 읽기 위해 개인 백업을 열 필요는 없다. 팀 재설치 기준은 이 대장과 각 공급자의 패키지다.

## 실제 검증

2026-09-30 정리 당시 에셋 이동의 GUID·참조 보존, DOTween 설정과 경로 보정된 도구 프리팹의 로드를 확인했다. 컴파일 오류는 없었으나 외부 코드의 구 API 등 경고는 유지보수 대상이다(Mesh Baker 108, RaycastPro 26, Wingman 15, Hierarchy Designer 1).

이 결과는 2026-10-08 추가 설치 이후 재실행한 결과가 아니다. VFX 시각 결과·플러그인 전체 기능의 Play 테스트·다인 접속은 미검증이다. 승인된 경로 보정과 재설치 조건은 위 항목을 따른다.

## 공용 배경·소품 제작 — 2026-10-01 체크포인트

| 구성 | 출처·가공 | 공유 경로·사용 조건 |
|---|---|---|
| Meshy 원본 가공21종 | Meshy 소품9·건축4·자연물8 원본을 Blender에서 가공하고 실제 AO·PBR·3LOD 제작 | `Assets/01.Scripts/05.Art/{Environment,Props}/{이름}/`, `Assets/02.Prefab/{Environment,Props}/{이름}/`. 모델별 이름·사용 조건은 [공용 환경 에셋 사용 목록](../unity/SHARED_ENVIRONMENT_ASSET_CATALOG.md)을 따른다 |
| 규격 모듈42종 | Blender에서 프로젝트 규격에 맞춰 직접 제작하고 실제 AO·PBR 처리 | 같은 Environment 경로. 3LOD의 기하가 같아 삼각형 감면은 없다 |
| 기존 원본 파생7종 | 기존 FloatingRock 파생 바위5종과 CherryTree 파생2종. 원본 모델·재질·프리팹 보존, 파생형 크기·피벗·용도별 Collider 조정 | 파생형은 공용 목록 경로를 따른다. 중앙 거대 벚꽃나무는 신규 제작에 포함하지 않는다 |

생성 입력·작업 ID·원본·편집본·상세 제작 기록은 Git 제외 개인 작업 영역에 보존했다. 인증값은 에셋과 문서에 넣지 않는다. 생성 계정 플랜과 입력·출력 권리 조건은 기존 Meshy 기록과 같이 별도 확인 상태이며, 이번 정적·시각 검증을 상업 이용 권리 확인으로 취급하지 않는다. 현재70종은 조건부 통과이고 식생·목조 교각 표면 보정과 게임 사용·성능·빌드 검증이 남아 있다.

## 원화 대조 마을 제작 — 2026-10-01~02

CA04의 개별 물체 참조로 Meshy 원본 8종을 생성하고 Blender 5.1에서 형상·치수·UV·노멀·재질·실제 AO를 가공했다.

| 제작물 | 생성 도구 | 공유 이름 |
|---|---|---|
| 이중 지붕 전각 | Meshy | `ReferenceGrandHall` |
| 열린 정자 | Meshy | `ReferencePavilion` |
| 석주·붉은 문 | Meshy | `ReferenceCeremonialGate` |
| 공방 | Meshy | `ReferenceWorkshop` |
| 기와 주거 | Meshy | `ReferenceTiledResidence` |
| 초가 | Meshy | `ReferenceThatchedCottage` |
| 크림색 시장 차양 | Meshy | `ReferenceMarketCanopy` |
| 불규칙 석판 길 | Meshy | `ReferenceStonePath` |

런타임은 `Assets/01.Scripts/05.Art/Environment/{공유 이름}/`, 배치용 프리팹은 `Assets/02.Prefab/Environment/{공유 이름}/`이다. 기존 FloatingRock의 Blender 청크 원본에서 파생한 `ReferenceIsland` 7종은 원본 UV·아랫면을 보존하고 윗면과 색·AO를 다시 처리했다. `ReferenceShrineDetails`는 기존 링 원본을 바탕으로 Blender에서 천·기둥·돌 기단을 제작했고, `VillageAssembly/PF_ReferenceSanctuaryLantern`은 기존 모듈·등을 조립한 것이다. 신규 Meshy 생성 수에 이 파생·직접 제작·조립물을 더하지 않는다.

원본 GLB·편집 blend·입력 이미지·반려된 축소 후보·상세 검수는 Git 제외 개인 제작 기록에 보존한다. 권리 확인 상태는 위 기존 Meshy 기록과 같다. 최종 경로·LOD 예외·시각 검증·사용 제한은 [공용 환경 에셋 목록](../unity/SHARED_ENVIRONMENT_ASSET_CATALOG.md)의 원화 대조 마을 항목을 따른다.

## Tripo 여성 공통 몸체·얼굴·직업 참조 — 2026-10-02~03

사용자가 승인한 생성 이미지 입력을 열린 Tripo Studio에 업로드하고 High/리토폴로지 원본을 실제 다운로드했다. 입력은 AI 생성 캐릭터 원화이며 실존 인물 사진 텍스처를 사용하지 않았다. 모델별 원본·프롬프트·파일 해시·차감 내역은 Git 제외 개인 docs에 보존한다. 아래 직업 전신은 분리 의상·헤어 제작의 참조 원본이며 최종 게임 모델이 아니다.

| 원본 | 생성 도구 | 가공·공유 범위 |
|---|---|---|
| 공통 여성 몸체 | Tripo | 새 52뼈 골격·스킨·3LOD·AO/PBR. `Assets/01.Scripts/05.Art/Characters/PlayerFemaleTripo/CommonBody/` |
| 귀여운 여성 얼굴 | Tripo | High 기반 얼굴 정리·공통 목 연결·3LOD·AO/PBR. 같은 루트의 `Heads/Cute/` |
| 무사 전신 참조 | Tripo | 원본 보존, 종전 옷 스킨 후보는 변형 실패로 기각 |
| 도술사 전신 참조 | Tripo | High/리토폴로지 원본 보존, 분리 의상·헤어 제작 대기 |
| 신령사 전신 참조 | Tripo | High/리토폴로지 원본 보존, 분리 의상·헤어 제작 대기 |

공통 몸체의 최종 골격은 Blender에서 별도로 제작·검증했다. Tripo 리깅/애니메이션 내보내기의 전체 확보를 뜻하지 않는다. 검토용 프리팹은 `Assets/02.Prefab/Characters/PlayerFemaleTripo/`의 `PF_FemaleCommonBody`, `PF_FemaleCommonBody_Cute`, `PF_FemaleHeadCute`다.

**상업 이용 권리 확인은 보류**다. [Tripo 공식 상업 이용 안내](https://www.tripo3d.ai/help/privacy-policy/how-to-use-tripo-models-commercially)는 유료 플랜과 무료 플랜의 권리 조건을 구분하지만, 생성 당시 계정 플랜은 확인하지 못했다. 다운로드 성공만으로 유료 플랜·권리 보유를 추정하지 않는다. 상업 배포 전 생성 당시 플랜과 적용 약관·입력 권리를 확인해야 하며, 현재 외형·리깅 검증은 그 확인을 대신하지 않는다. 확인한 제작 범위와 남은 게임 사용 검증은 [모델 제작 목록](../design/model-production-list.md#tripo-공통-여성-몸체귀여운-얼굴-첫-검토-묶음--2026-10-03)을 따른다.

## CA-07 추가 환경 모델 — 2026-10-03

CA-07(`docs/design/references/concept-art/village-layout-02.webp`)에서 기존 Assets에 없던 4종이다. 입력 이미지는 원화 크롭을 스타일 참조로 사용해 Codex 이미지 생성으로 만든 단일 물체 시안이다. Meshy 7.1의 PBR·2K 출력을 Blender 5.1에서 가공했다.

| 공유 이름 | 생성 도구 | 가공 요약 |
|---|---|---|
| `Waterwheel` | Meshy | 감면·새 UV·원본→감면 전사 베이크·실제 AO |
| `WheatFieldPlot` | Meshy | 흙 둔덕은 원본 높이장 재구성 후 전사 베이크·실제 AO, 밀은 원본 밀 줄기를 렌더한 알파 카드 아틀라스와 카드 버텍스 AO |
| `LotusCluster` | Meshy | 높이 0.6배 압축·수면 피벗·감면·전사 베이크·실제 AO |
| `ArchedTimberBridge` | Meshy | 폭 1.06배, 아치 높이를 포물선 변위로 약 0.78→1.47m로 키움, 감면·전사 베이크·실제 AO |

런타임은 `Assets/01.Scripts/05.Art/Environment/{공유 이름}/`, 배치용 프리팹은 `Assets/02.Prefab/Environment/{공유 이름}/`이다. 입력 시안·원본 GLB·편집 blend·베이크 원본·검수 렌더는 Git 제외 개인 제작 기록에 보존한다. 인증값은 기록하지 않았다.

권리 확인 상태는 위 기존 Meshy 기록과 같다. 생성 계정 플랜과 입력 이미지(Codex 생성)·출력 권리 조건은 확인하지 않았으며, CC0나 독점 소유로 확정하지 않는다. 사용 조건과 검증 범위는 [공용 환경 에셋 목록](../unity/SHARED_ENVIRONMENT_ASSET_CATALOG.md#ca-07-추가-에셋--2026-10-03)을 따른다.

## 직업용 VFX 사본의 출처와 사용 조건 (2026-10-08)

| 게임용 사본 | 외부 출처 | 사용 조건 |
|---|---|---|
| `Assets/02.Prefab/VFX/SwordsmanMagicSlash/` | `Magic Slashes FX`의 `Eff1_Left`, `Eff1_Right`, `Eff7_Slash` | 원본 메시·텍스처·재질 의존성을 유지한다. 동일 버전 패키지와 공급자 라이선스가 필요하다. |
| `Assets/02.Prefab/VFX/Mageknight/` | 2026-10-15 스택형 개편부터 Hovl Studio `Sword slash VFX`의 `Sword Slash Combo 6`, `Slash wave`, `Spatial section`; PixPlays `ElementalShields`의 `WaterShield`(Version_BuiltIn); `Magic Slashes FX`의 `Eff11_Jump` | 원본 재질·텍스처·메시 의존성을 유지한다(출시 발광 재질로 바꾸지 않는다). 해당 패키지 설치와 공급자 라이선스가 필요하다. 원본 프리팹은 수정하지 않고 `MageknightVfxBuilder`가 사본을 만든다. |
| `Assets/02.Prefab/VFX/Archer/` | `MasterStylizedProjectiles`의 `ArrowBullet`, `ArrowMuzzle`, `WindBullet`, `WindStart`, `WindHit` | 원본 재질·텍스처·셰이더 의존성을 유지한다. 해당 패키지 설치와 공급자 라이선스가 필요하다. |
| `Assets/02.Prefab/VFX/Archer/PF_ArcherVfx_{IceArrow,IceMuzzle,IceHit,WindArrow,WindStart,WindHit,FireArrow,FireMuzzle,FireHit,Charge,Burst}` (2026-10-15 궁수 속성형) | `MasterStylizedProjectiles`의 `SmallIceBullet`(`SmallIceBullet`, `SmallIceMuzzle`, `SmallIceBulletHit`), `WindShoot`(`WindBullet`, `WindStart`, `WindHit`), `SmallFireBullet`(`SmallFireBullet`, `SmallFireMuzzle`, `SmallFireBulletHit`), `EnergyExplosion`(`EnergyExplosionCharge`, `EnergyExplosionHit`) | 원본 재질·색을 유지한 `ArcherVfxBuilder` 사본(스크립트·조명·충돌체 제거, URP 미지원 내장 셰이더 입자만 끔). 해당 패키지 설치와 공급자 라이선스가 필요하다. 기획서의 불 "FireShoot"은 팩에 없어 `SmallFireBullet`을 썼다(대안 `Fireball`). 속성 없는 `Arrow`/`Muzzle`/`Impact`는 `ReleaseVfxBuilder`의 프로젝트 소유 사본을 그대로 쓴다. |

외부 원본은 `Assets/99.ThirdParty/VFX/`에 보존하며 팀 독자 제작 에셋으로 취급하지 않는다.

## 캐릭터 선택 UI 생성 이미지 (2026-10-07)

프로젝트 세계관·직업 기획과 기존 UI의 바랜 은색 재질을 기준으로 Codex의 built-in `image_gen`으로 생성했다. 각 이미지별 독립 생성이며 투명 PNG 원본을 픽셀 변경 없이 복사했다. 상세 프롬프트·원본·비교 캡처는 Git 제외 개인 제작 기록에 보존한다.

| 에셋 | 제작·출처 | 보관·사용 조건 | 검증 |
|---|---|---|---|
| 무사·도술사·신령사 직업 마크 3개 | built-in image_gen 생성, 2026-10-07 | `Assets/01.Scripts/05.Art/UI/Icons/Textures/Class/T_Icon_Class_{Warrior,Sorcerer,Summoner}.png`, UI 단일 Sprite | Dev_CharSelect 표시·마크 위치에서 세 슬롯 선택 확인 |
| 뒤로·삭제·설정·종료·생성 아이콘 5개 | built-in image_gen 생성, 2026-10-07 | `Assets/01.Scripts/05.Art/UI/CharSelect/Textures/T_CharSelect_Icon_{Back,Delete,Settings,Exit,Create}_Silver.png`, 알파·밉맵 없음·최대 256px | Unity 임포트·Play Mode 표시 확인 |

출력의 별도 라이선스 검토는 미완료다. 세부 사용 위치·실행 검증 한계는 [개발 씬 아이콘 기록](../unity/DEV_HUD_PREVIEW.md#캐릭터-선택-개발-씬-아이콘-2026-10-07)을 따른다.

## 장비 성장 UI 장식 (2026-10-15)

같은 경로의 `T_EquipmentGrowth_EquipmentAtlas.png`와 `T_EquipmentGrowth_RefineShard.png`도 built-in `image_gen`으로 생성했다. Atlas는 3×2 셀의 무기·머리·몸통·손·발·장신구 대표 그림이며 Unity Multiple Sprite로 분할한다. RefineShard는 이 두 성장 창의 정련편 표시용이다. 모두 원본 PNG를 보존하고 픽셀 수정 없이 복사했다. 외부 에셋 의존성이 없으며 별도 라이선스 검토는 미완료다.

`Assets/01.Scripts/05.Art/UI/EquipmentGrowth/Textures/T_EquipmentGrowth_{Crystal,Forge}.png` 두 이미지는 built-in `image_gen`으로 독립 생성한 투명 PNG다. 원본 픽셀을 바꾸지 않고 프로젝트에 복사했으며 기존 외부 에셋 의존성이 없다. 옵션 재설정과 강화 창의 장식으로 사용하며 실제 장비·소비 재료 아이콘으로 취급하지 않는다. 별도 라이선스 검토는 미완료다. 프롬프트와 원본 경로는 Git 제외 개인 제작 기록에 보존한다. 배치·사용 조건은 [장비 성장 UI](../unity/EQUIPMENT_GROWTH_UI.md)를 따른다.

## 아이템 64픽셀 아이콘 (2026-10-15)

`Assets/01.Scripts/05.Art/UI/Icons/Textures/Items64/`의 PNG 45종은 프로젝트의 아이템·기술 정의를 바탕으로 built-in `image_gen`에서 각각 생성했다. 장비 6부위, 재화·재료·소모품 15종, 부적 24기술이다. 고해상도 원본과 프롬프트는 개인 제작 기록에 보존하며, 런타임용 파일은 알파를 유지한 64×64 축소본이다. 기존 외부 패키지 의존성이 없고 별도 라이선스 검토는 미완료다.

PNG 크기·알파·전체 시각 검수, Unity Sprite 등록과 135개 정의 연결·프리팹 적용을 완료했다. 두 성장 창 Play Mode 화면과 실제 아이템 칸 렌더를 확인했고 관련 EditMode 테스트 55개를 통과했다. 적용 메뉴와 검증 범위는 [아이템 아이콘 적용 문서](../unity/ITEM_ICONS_64.md)에 적었다.
