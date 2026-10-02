# 외부 에셋 대장

2026-09-30 실제 설치 파일과 Unity 6000.4.7f1 에디터에서 확인했다. 저장소 루트가 Unity 프로젝트 루트다. Asset Store의 최신 버전과 이 프로젝트에 설치된 버전은 구분한다. 취득일·구매 내역·좌석 보유 현황은 확인하지 않았으며 설치 확인일을 취득일로 대신 적지 않는다.

## 분류와 설치 목록

외부 Asset Store 에셋은 `Assets/99.ThirdParty/{분류}/{에셋}/`에 둔다. 패키지 내부의 `Editor/`, `Resources/`, DLL, asmdef, 라이선스와 사용 문서는 보존한다.

| 분류 | 내용 | 현재 상태 |
|---|---|---|
| `Animation` | 트윈, 애니메이션 도구·클립 | DOTween Pro와 동봉 DOTween·DemiLib |
| `Tools` | 에디터, 대화, 물리 감지, 최적화, 셰이더 제작 도구 | 아래 8개 패키지 |
| `VFX` | 완성된 이펙트, 파티클, 이펙트용 소재 | Realistic Spirit Blossom Spells VFX |
| `Model` | 게임에서 사용할 모델·환경·소품 | 독립 패키지 없음, 분류 폴더만 준비 |

아래 경로는 `Assets/99.ThirdParty/` 기준이다. 상업 이용 상태의 “조건부”는 해당 라이선스와 사용자의 보유 권한을 충족해야 한다는 뜻이며, 이번 정리에서 구매 증빙까지 검증했다는 의미는 아니다. 모든 항목의 취득일은 미확인이다.

| 에셋 | 설치 버전 | 제작자 | 경로 | 구분 | 라이선스·상업 이용 | 출처 |
|---|---|---|---|---|---|---|
| DOTween Pro | 1.0.430 | Demigiant | `Animation/Demigiant/` | 유료, DOTween·DemiLib 동봉 | Pro 라이선스·조건부 | [공식 제품·설치 안내](https://dotween.demigiant.com/pro.php) |
| Realistic Spirit Blossom Spells VFX | 1.0 | Piloto Studio | `VFX/Piloto Studio/` | 유료 | Standard Unity Asset Store EULA·조건부 | [Asset Store 399376](https://assetstore-fallback.unity.com/packages/vfx/particles/spells/realistic-spirit-blossom-spells-vfx-399376) |
| Dialogue System for Unity | 2.2.73.2 | Pixel Crushers | `Tools/Pixel Crushers/` | 유료, Common 동봉 | Standard Unity Asset Store EULA / Extension Asset·조건부 | [Asset Store 11672](https://assetstore.unity.com/packages/tools/behavior-ai/dialogue-system-for-unity-11672) |
| RayCast Pro | 1.1.5A | Philosophers | `Tools/RaycastPro/` | 유료 | Standard Unity Asset Store EULA / Extension Asset·조건부 | [Asset Store 214714](https://marketplace.unity.com/packages/tools/physics/raycastpro-214714) |
| Editor Console Pro | 3.983 | FlyingWorm | `Tools/ConsolePro/` | 유료 | Standard Unity Asset Store EULA / Extension Asset·조건부 | [Asset Store 11889](https://assetstore.unity.com/packages/tools/utilities/editor-console-pro-11889) |
| Hierarchy Designer | 3.2.2 | Pedro Verpha | `Tools/Hierarchy Designer/` | 유료 | Standard Unity Asset Store EULA / Extension Asset·조건부 | [Asset Store 273928](https://assetstore.unity.com/packages/tools/utilities/hierarchy-designer-273928) |
| Wingman - Your Inspector's Best Friend | 1.1.0 | Kyle Rhoads | `Tools/Wingman/` | 유료 | Standard Unity Asset Store EULA / Extension Asset·조건부 | [Asset Store 303181](https://assetstore.unity.com/packages/tools/utilities/wingman-your-inspector-s-best-friend-303181) |
| Amplify Shader Editor | 1.9.9.12 | Amplify Creations | `Tools/AmplifyShaderEditor/` | 유료 | Standard Unity Asset Store EULA·조건부 | [Asset Store 68570](https://assetstore.unity.com/packages/tools/visual-scripting/amplify-shader-editor-68570) |
| Maintainer 2 | 2.3.3 | Code Stage | `Tools/CodeStage/Maintainer/` | 유료 | Standard Unity Asset Store EULA·조건부 | [Asset Store 310179](https://assetstore.unity.com/packages/tools/utilities/maintainer-2-310179) |
| Mesh Baker | 소스 버전 문자열 3.39.0(`MB3_MBVersionConcrete`), 동봉 API 문서 3.37.0 | Ian Deane / Digital Opus | `Tools/MeshBaker/` | 유료 | Standard Unity Asset Store EULA / Extension Asset·조건부 | [Asset Store 5017](https://assetstore.unity.com/packages/tools/modeling/mesh-baker-5017) |

UMA 3.1f1(GitHub 릴리스 `V3.1f1`)은 2026-10-01 들였다가 같은 날 쓰지 않기로 하고 뺐다. 연결 코드 `AvatarView`, `Shinshi.Runtime`의 `UMA_Core` 참조, `.gitignore`의 `Assets/UMA/` 예외, UMA 태그(`UMAKeepChain`, `UMAIgnore`), `PlayerFemaleModular/UMA/` 연동 에셋을 함께 지웠다. `Assets/UMA/`를 받아 둔 작업자는 로컬에서 직접 지운다.

설치 버전은 `.meta`의 `AssetOrigin.productId`, `packageName`, `packageVersion`으로 확인했다. Mesh Baker는 해당 메타데이터가 없고 동봉 문서가 `docs/API-3.37.0.zip`이다. 소스의 버전 문자열은 3.39.0이다. 2026-10-01 팀장이 유료판 결제를 확인해 에디션을 유료로 기록한다. 결제 증빙과 팀원별 사용 좌석은 이 대장에서 검증하지 않았다. 유료 에셋이므로 다른 유료 에셋과 같이 패키지를 Git에서 제외하고 설치할 때마다 받는다. 환경 메시 결합에 쓰려고 `Shinshi.Runtime`이 `MeshBakerCore` 어셈블리를 참조한다.

## 공용 돌바닥·부유석 제작 소재와 가공물

2026-10-01 사용자 제공 참고 시트와 실사 판타지 제작 요청을 기준으로 아래 소재를 취득·가공했다. Asset Store 설치 패키지와 구분하여 출처·사용조건과 배치용 경로를 기록한다. 후속 제작은 [아트 제작 기본 과정](../unity/ART_ASSET_PRODUCTION_WORKFLOW.md)을 따른다.

| 에셋 | 버전·제작 기록 | 제작자 | 보관·사용 경로 | 구분 | 라이선스·상업 이용 | 출처 |
|---|---|---|---|---|---|---|
| Rock 01 석재 PBR | 2026-10-01 취득, 4K Base Color·Roughness·OpenGL Normal·Height | Rob Tuytel / Poly Haven | 원본은 Git 제외 개인 작업 영역에 보존, 돌바닥 가공 재질에 사용 | 외부 무료 소재 | CC0, 상업 이용·수정·재배포 가능 | [공식 자산](https://polyhaven.com/a/rock_01), [라이선스](https://polyhaven.com/license) |
| 공용 돌바닥 | 2026-10-01 Blender MCP 제작, 6×6m 모듈·3LOD FBX·PBR/AO 출력 | shinshi 프로젝트 제작 | 모델·재질 `Assets/01.Scripts/05.Art/Environment/StoneCourtyard/`, 프리팹 `Assets/02.Prefab/Environment/StoneCourtyard/` | 프로젝트 제작 메시·가공 재질 | 석재 표면 원본은 위 CC0. 자체 메시·베이크와 외부 소재를 구분하여 관리 | [참고 CA-05](../design/concept-art.md#6-ca-05-공용-돌바닥-타일), [배치 프리팹](../../Assets/02.Prefab/Environment/StoneCourtyard/PF_StoneCourtyardTile.prefab) |
| 장식용 부유석 | Meshy CLI 0.3.0, `meshy-7.1`, 작업 `01a0f4de-b032-70ef-923a-05f9a0c97a84`, 2026-10-01 생성·다운로드, 실제 35크레딧·Blender 3LOD/PBR/AO 출력 | Meshy 생성 + shinshi Blender 가공 | 원본은 Git 제외 개인 작업 영역에 보존, 모델·재질 `Assets/01.Scripts/05.Art/Environment/FloatingRock/`, 프리팹 `Assets/02.Prefab/Environment/FloatingRock/` | AI 생성 원본·프로젝트 가공물 | 생성 당시 계정 플랜 미확인. 플랜별 조건·입력 권리 확인 상태는 아래 기록 | [참고 CA-06](../design/concept-art.md#7-ca-06-부유석), [Meshy 소유 안내](https://help.meshy.ai/en/articles/10137554-what-is-the-ownership-of-the-generated-models), [이용 약관](https://www.meshy.ai/terms-of-use) |

- Rock 01 원본 PNG 네 개는 4096×4096이며 공식 파일 API의 MD5와 모두 일치한다. 시각 확인용 썸네일은 런타임 소재에 포함하지 않았다.
- 참고 그림은 사용자 제공이다. 작가·제작 도구·라이선스와 별도 권리 확인은 미확인으로 기록하고 소유권을 추측하지 않는다.
- Meshy 공식 안내는 유료 플랜 생성물의 비공개 소유와 무료 플랜 생성물의 CC BY 4.0·Meshy 표시 조건을 구분한다. 실제 생성 계정 플랜을 CLI에서 확인하지 못했으므로 이 부유석을 자체 소유 또는 CC0로 확정하지 않는다. 커뮤니티 공개는 요청하거나 수행하지 않았다. [상업 이용 안내](https://help.meshy.ai/en/articles/16102098-can-i-use-meshy-assets-commercially)
- 원본 GLB·FBX, 다운로드 텍스처, Blender `.blend`와 제작 기록은 Git 제외 개인 작업 영역에 보존한다. 런타임 모델·재질·텍스처는 [팀 모델링 에셋 규칙](../unity/SCENE_AND_ASSET_SAFETY.md#팀이-만든-모델링-에셋)에 따라 `05.Art/Environment/`, 배치용 프리팹은 `02.Prefab/Environment/`에서 `.meta`와 함께 공유한다. 모델 제작은 프리팹 생성·참조 검증까지 포함한다.
- 런타임 모델명은 `StoneCourtyard/Models/SM_StoneCourtyardTile_{High,Medium,Low}.fbx`, `FloatingRock/Models/SM_FloatingRock_{High,Medium,Low}.fbx`다. 단계는 LOD0·1·2에 대응한다. [돌바닥 프리팹](../../Assets/02.Prefab/Environment/StoneCourtyard/PF_StoneCourtyardTile.prefab)과 [부유석 프리팹](../../Assets/02.Prefab/Environment/FloatingRock/PF_FloatingRock.prefab)을 배치하고, 상세 디자인·제작 기록·렌더는 개인 docs에 보관한다.
- Unity 임포트·URP Lit PBR/AO 연결·LOD·메타와 에디터 프리뷰를 확인했다. 게임플레이·성능·빌드·배포 검증과 Meshy 계정 플랜 확인은 완료하지 않았다.

## 공용 석등 생성·가공물

| 에셋 | 생성·가공 | 보관·사용 | 권리 확인 상태 | 근거 |
|---|---|---|---|---|
| StoneLantern | 2026-10-01 Meshy CLI 0.3.0·Meshy 7.1, 작업 `01a0f547-3b81-70a0-9f76-1c4c0dc96ebe`, SUCCEEDED·실제 30크레딧. Blender MCP 3LOD·실제 AO·창 Emission 베이크 | 원본은 Git 제외 개인 작업 영역에 보존, 런타임 `Assets/01.Scripts/05.Art/Props/StoneLantern/`, 배치 프리팹 `Assets/02.Prefab/Props/StoneLantern/` | ChatGPT 생성 단독 석등 입력·Meshy 출력·프로젝트 가공물. Meshy 생성 계정 플랜 미확인, CC0·독점 소유로 확정하지 않음 | [배치 프리팹](../../Assets/02.Prefab/Props/StoneLantern/PF_StoneLantern.prefab), [Meshy 소유 조건](https://help.meshy.ai/en/articles/10137554-what-is-the-ownership-of-the-generated-models), [약관](https://www.meshy.ai/terms-of-use) |

디자인 컨셉·생성 프롬프트는 개인 docs에 보관한다. 에셋 대장은 실제 사용 모델의 출처·조건을 기록하며 개인 시안 이미지를 게시하지 않는다. 외부 폴리헤이븐 석재를 이 석등 재질에 추가로 사용하지 않았다.

## 건축·매다는 등 시제품

| 에셋 | 생성·가공 | 런타임 보관·사용 조건 |
|---|---|---|
| TiledHouse | Meshy 작업 `01a0f5f0-8d3e-717a-9331-18e74132e8b6`, 성공 출력·30크레딧. 가공 3LOD는 59,999 / 32,998 / 16,799삼각형 | `Assets/01.Scripts/05.Art/Environment/TiledHouse/`. 3FBX·7PNG(PBR/AO·Alpha), 재질·프리팹 미구성 |
| RedGate | Meshy 작업 `01a0f5fb-2b43-763f-894a-179d71ee1eea`, 성공 출력·30크레딧. 가공 3LOD는 40,000 / 22,000 / 11,200삼각형 | `Assets/01.Scripts/05.Art/Environment/RedGate/`. 3FBX·6PNG(PBR/AO), 재질·프리팹 미구성 |
| HangingLantern | Meshy 작업 `01a0f5ec-73bb-759b-89c8-5f4e535102cd`, 성공 출력·30크레딧. 가공 3LOD는 23,000 / 19,550 / 16,099삼각형 | `Assets/01.Scripts/05.Art/Props/HangingLantern/`의 3FBX·8PNG·재질, [장식용 프리팹](../../Assets/02.Prefab/Props/HangingLantern/PF_HangingLantern.prefab). Collider 없음 |

2026-10-01 커밋 전 정적 확인에서 세 모델의 FBX는 가공 출력과 SHA-256이 일치했고 메타·텍스처 임포트 설정을 확인했다. 매다는 등은 Emission 맵·색이 연결됐으나 `_EMISSION` 키워드가 없어 현행 URP Lit에서 발광하지 않는다. 최신 LOD의 시각·전환·게임플레이·성능·빌드는 이번 검토에서 미실행이다. 건축 두 모델은 배치용 프리팹이 완성되기 전 중간 산출물이다.

생성 원본·입력·상세 기록은 Git 제외 개인 작업 영역에 보존한다. Meshy 생성 계정 플랜·입력 권리·배포 조건은 미확인으로 유지하며 CC0·독점 소유를 주장하지 않는다. [Meshy 소유 조건](https://help.meshy.ai/en/articles/10137554-what-is-the-ownership-of-the-generated-models)과 [약관](https://www.meshy.ai/terms-of-use)을 실제 계정 조건과 대조해야 한다.

## 여성 베이스 외형 시제품

| 에셋 | 생성·가공 | 보관·사용 | 권리 확인 상태 | 근거 |
|---|---|---|---|---|
| PlayerFemaleBasePreview | 2026-10-01 Meshy CLI 0.3.0·Meshy 7.1, 전신 작업 `01a0f5d5-b3d5-71eb-a2ed-6c6fa93f7140`·머리 작업 `01a0f5d5-dac6-73b9-870c-07a196f0f4cf` 모두 SUCCEEDED, 각 30·총 60크레딧. 전신을 Blender MCP에서 정리하고 3LOD·PBR·실제 국소 AO 제작 | 원본·입력·상세 기록은 Git 제외 개인 작업 영역에 보존, 런타임은 `Assets/01.Scripts/05.Art/Characters/PlayerFemaleBasePreview/`, 프리팹은 `Assets/02.Prefab/Characters/PlayerFemaleBasePreview/`. 별도 머리는 미채택·미조립으로 원본만 보존 | ChatGPT 생성 참조 입력·Meshy 생성 출력·프로젝트 가공물. 생성 계정 플랜과 권리 조건 확인 미완료, CC0·독점 소유로 확정하지 않음. 커뮤니티 공개·배포 수행 안 함 | [외형 프리팹](../../Assets/02.Prefab/Characters/PlayerFemaleBasePreview/PF_PlayerFemaleBasePreview.prefab), [Meshy 소유 조건](https://help.meshy.ai/en/articles/10137554-what-is-the-ownership-of-the-generated-models), [약관](https://www.meshy.ai/terms-of-use) |
| PlayerFemaleModular | 2026-10-01 기존 전신을 Meshy CLI 0.3.0 자동 리깅, 작업 `01a0f617-3ae7-7795-9712-21cfc3ac16c7` SUCCEEDED·추가5크레딧(전신·머리 생성 포함 여성 시제품 누적65). 목 분리·3LOD·Blink 후 얼굴·손 국소 표면 정리, 실제 AO/노멀 재베이크, 눈꺼풀 아틀라스·모프 노멀·걷기 교정과 UMA3 연동 에셋49개 제작(같은 날 UMA 미사용 결정으로 삭제). 이번 교정에는 추가 생성 과금 없음 | 런타임 모델·애니메이션·재질·교정 맵은 `Assets/01.Scripts/05.Art/Characters/PlayerFemaleModular/`, PF는 `Assets/02.Prefab/Characters/PlayerFemaleModular/`. 원본 BaseColor·거칠기·MetallicSmoothness 의존 `PlayerFemaleBasePreview/` 함께 보존. 원본·상세 기록은 Git 제외 개인 작업 영역 | 위 입력·생성물의 확인 상태를 승계하며 새로운 독점 권리·CC0를 주장하지 않음. 자동 리깅·원본 클립 출처는 Meshy, 메시·눈꺼풀·걷기 교정은 프로젝트 작업. 계정 플랜·배포 조건 확인 미완료 | [모듈형 프리팹](../../Assets/02.Prefab/Characters/PlayerFemaleModular/PF_PlayerFemaleModular.prefab), [Meshy 리깅 API](https://docs.meshy.ai/en/api/rigging), [Meshy 소유 조건](https://help.meshy.ai/en/articles/10137554-what-is-the-ownership-of-the-generated-models) |

`PlayerFemaleBasePreview`는 리깅·애니메이션·얼굴 모프·Collider가 없는 정적 외형 시제품이다. 후속 `PlayerFemaleModular`는 공통24뼈·머리/몸 분리·Blink·검토용 걷기/달리기와 Humanoid 임포트를 포함한다. 표면 정리 후3LOD는56,044 / 29,997 / 16,941삼각형이며 마지막 눈꺼풀·걷기 적용 후 Unity CPU 목15표본·Blink 초기화·33캡처를 확인했다. 걷기는24뼈 local rest 기준을 공급해 source/model/PF humanScale1.01658213과 실제1.2초 클립을 맞췄다. source/target 공통 최저 발바닥 약−2.10mm와 달리기 손/몸 겹침은 남아 있다. 수정된 몸·머리 쌍의 실제 AO/노멀을 재베이크했으며 다른 부품 조합에는 AO 재검증이 필요하다. 눈꺼풀의 별도 노멀 맵·고정 AO 생략은 얇은 보조 면의 시제품 예외다.

**사용자 요청으로 작업을 종료했고 외형 판정은 사용자 거절(REJECT), 신규 UMA 외형 채택·연동은 보류**다. 기존 PF와 카탈로그·런타임 기본 선택은 유지했다. 같은 날 UMA를 쓰지 않기로 해 아래 UMA3 에셋과 검토 종족은 삭제했고, 이 문단의 UMA 검증은 당시 기록으로만 남긴다. UMA3 native229뼈 기반49에셋과 검토종족 `Shinshi Female UMA3 Review`를 저장했고,이전 바인딩의3LOD DNA30표본·Skin/Eyes/Brows/Lips 색상12검사를 통과했다. 마지막 눈꺼풀 shape normal 델타만 수정한 뒤 High10DNA/4색은 재통과했으며 Medium/Low는 마지막 변경 후 재실행하지 않았다. 현재 비어 있지 않은26 DNA 이름이 live62 DNA에 존재하며 native 정의 로드값55개를 보존했다.26키 존재와5키 변형 표본은 전체슬라이더·조합 검증과 구분하고 빈6개슬라이더는 미지원으로 유지한다. 합성용 원본의 streaming mip 선택으로 발생한 광범위 삼각형 패치는 관련13텍스처의 streaming을 끄고 mipmaps·압축을 유지하는 저장 설정으로 세 LOD에서 제거했다. 전신BaseColor/마스크2048·눈꺼풀512·AO/Normal/MetallicSmoothness1024 최대크기이며 편집기 텍스처 object bytes는 Player 메모리/성능 측정이 아니다. 기존 UMA 패키지·전역 indexer·런타임 코드·카탈로그는 이 작업에서 수정하지 않았다. native High걷기16phase는 목틈0·유한정점·복원오차0과 바닥실패9/16·최대19.9607mm침투를 함께 확인했다. 임시IK도1phase의25mm 초과 발들뜸으로 전체실패했고 저장 에셋/Controller 변경은 없었다. 교정된 native 전용Walk는 저장하지 않았다. 정식 에셋 등록·코드연동, 수용될 최종 외형과 native 보행교정, 최종 얼굴 제작·극한DNA/전체커스터마이징·GPU/player loop·게임플레이·성능·Windows빌드·상업배포는 미완료다. Collider는 두 시제품 모두 없다. 개인 이미지·프롬프트·렌더는 공용 문서에 게시하지 않고 사용 조건과 검증 범위는 [모델 제작 목록](../design/model-production-list.md)을 따른다.

## 신시 마을 지형 시제품

| 에셋 | 제작·소재 | 런타임 보관·사용 | 권리 구분 |
|---|---|---|---|
| Aerial Grass Rock 지면 PBR — 이전 시제품 이력 | 2026-10-01 취득·2K JPG 원본 보존. 이전 상부 지면에서 Diffuse/Height/Roughness를 사용했고 OpenGL Normal 원본은 보존만 함 | 원본·상세 제작 이력은 개인 작업 영역. 현행 native 부유석 키트의 표면에는 추가 지면 맵을 사용하지 않음 | Rob Tuytel / Poly Haven, 외부 CC0 소재. [공식 자산](https://polyhaven.com/a/aerial_grass_rock), [CC0 라이선스](https://polyhaven.com/license) |
| VillageTerrain v2 — 이전 통합 시제품 | 기존 Meshy FloatingRock 암석 조립 + Blender 상부·3LOD·접합 AO. Aerial Grass Rock/Rock 01 사용, 새 Meshy 생성·비용 없음 | `Assets/01.Scripts/05.Art/Environment/VillageTerrain/`의 기존 `PF_VillageTerrain`·`Dev_ShinshiVillageTerrain`는 이력이며 새 키트 배치 대상으로 사용하지 않음 | 프로젝트 제작 상부·가공 재질, 위 CC0 소재와 기존 Meshy 원본 조건을 구분 |
| VillageTerrain 독립 부유석 키트 | 같은 Meshy 원본의 Core/E/NE/NW/W/SW/SE 변형7개, 원래 암석·이끼 표면·공유PBR·자체AO7·렌더3LOD·숨은 상단충돌7. 추가 Meshy 비용 없음 | `Assets/01.Scripts/05.Art/Environment/VillageTerrain/{Models,Textures,Materials}/Chunks/`의28FBX·7AO·7재질, [조립용7PF](../../Assets/02.Prefab/Environment/VillageTerrain/Chunks/), [검수 갤러리](../../Assets/00.Scenes/Dev_FloatingIslandKit.unity) | 기존 FloatingRock의 Meshy 계정 플랜 미확인 상태를 승계. 가공·AO 제작 기록과 원본 조건을 구분하며 전체를 CC0·독점 소유로 확정하지 않음 |

설계·Blender 원본·상세 제작 기록·렌더와 취소된 시제품은 개인 docs에 보관한다. 현행 키트는 추가 가시 상부판 없이 개별 조각을 제작하고 사용자가 Unity에서 조립한다. 자체 AO는 고정 조립의 접합 AO와 구분한다. Unity7PF·임포트/참조/메타·지정 ray/강제LOD·고정 카메라 시각검사를 완료했고 기존 FloatingRock·공유표면 맵·보호64파일을 보존했다. 갤러리는 완성맵이 아니다. 숨은 상단충돌은 선택면용 정적 Non-Convex로 모든 가시경사/선반의 이동을 보장하지 않는다. 실제 맵조립·보행·다리/물/건물·게임플레이·연속LOD·성능·빌드·서버 통합은 미검증이다.

## 기본 패키지와 위치 예외

- `Packages/`의 Unity 기본 패키지와 MCP for Unity는 UPM이 관리하므로 Asset Store 폴더로 옮기지 않는다. 실제 버전·Git 의존성은 `Packages/manifest.json`, 고정 해시는 `Packages/packages-lock.json`이 기준이다.
- 주요 설치 버전: URP 17.4.0, Input System 1.19.0, Cinemachine 3.1.7, AI Navigation 2.0.12, Unity Test Framework 1.6.0, Timeline 1.8.12, uGUI 2.0.0, Visual Scripting 1.9.11. 각 패키지의 동봉 LICENSE를 따른다.
- Mirror 96.11.3은 무료 오픈소스(MIT, `Assets/Mirror/LICENSE`)이며 [공식 GitHub 릴리스](https://github.com/MirrorNetworking/Mirror/releases/tag/v96.11.3)의 `.unitypackage`로 2026-09-30 설치했다. 팀 전원이 같은 버전을 써야 하므로 Git에 포함하고, Mirror 기본 위치인 `Assets/Mirror/`를 유지한다(재임포트·업데이트 경로와 같음). 동봉 스크립트 템플릿(`ScriptTemplates/`)은 팀 템플릿과 섞이지 않도록 설치하지 않았다. 동봉 트랜스포트·예제 에셋의 개별 라이선스(`Transports/*/LICENSE`, `Examples/_Common/` 내 고지)는 원본대로 보존한다.
- `Assets/Plugins/TextMesh Pro/`는 TMP 필수 폰트·리소스·셰이더이며 데모가 아니다. 2026-10-01 Unity 에디터에서 `Assets/TextMesh Pro/`에서 옮겼다(GUID 유지). `TMP Settings`는 `Resources.Load`로 찾으므로 `Resources/` 구조와 파일명, 동봉 라이선스를 유지한다. TMP 필수 리소스를 다시 임포트하면 `Assets/TextMesh Pro/`에 새로 풀리니 중복을 지우거나 이 위치로 옮긴다.
- `Assets/Gizmos/`의 `DialogueDatabase Icon.png`, `StringAsset Icon.png`, `TextTable Icon.png`는 Dialogue System의 에디터 아이콘이다. 플러그인 코드와 Unity Gizmos가 이 위치를 사용하므로 유지하고 유료 패키지와 함께 Git에서 제외한다.
- DOTween 설정은 `Assets/99.ThirdParty/Animation/Demigiant/DOTween/Resources/DOTweenSettings.asset`에 있다. `Resources.Load("DOTweenSettings")`가 계속 동작하도록 파일명과 `Resources/` 구조를 유지했다.

## 정리 내역과 유지한 의존 에셋

Unity `AssetDatabase.GetDependencies`로 삭제 후보 밖에서 들어오는 참조를 검사한 뒤 에디터 API로 이동·삭제했다.

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

정리 전 EPO Outline 미설치 상태에서 DOTween EPO 연동이 켜져 컴파일 오류 18개가 있었다. `modules.epoOutlineEnabled=false`로 바꾸고 DOTween 공식 Utility의 `ApplyModulesSettings`를 실행해 연동 컴파일 조건을 비활성화하고 해결했다. 해당 연동 스크립트는 삭제하지 않았다. [공식 설치 안내](https://dotween.demigiant.com/pro.php)는 설치 후 사용할 모듈을 설정하도록 안내한다.

## Git와 재설치

- `.gitignore`는 유료 패키지의 폴더·폴더 메타 및 위 Gizmos 예외만 명시적으로 제외한다. 분류 폴더의 `.meta`와 이 대장은 공유한다. 미래의 무료 에셋까지 `99.ThirdParty/` 전체를 일괄 제외하지 않는다.
- 재설치할 때 이 대장의 설치 버전을 임포트하고 Unity 에디터에서 위 경로로 옮긴다. 구 위치에 중복 DLL·소스가 남지 않게 확인한다.
- 구매 에셋을 참조하는 팀 씬·프리팹을 공유하기 전 기존 GUID를 유지하는 설치·참조 복원 절차를 확인한다. 이번 SampleScene은 구매 에셋을 참조하지 않는다.
- 원본 복구 백업은 Git에서 제외한 개인 작업 영역에 보존했다. 공유 문서를 읽기 위해 개인 백업을 열 필요는 없다. 팀 재설치 기준은 이 대장과 각 공급자의 패키지다.

## 실제 검증

최종 검사 결과는 아래에 기록한다. 게임·네트워크 코드는 변경하지 않았으며 플러그인 전체 기능의 플레이 테스트와 다인 접속 테스트는 실시하지 않았다.

| 주장 | 실행 수단 | 실제 결과 | 상태 |
|---|---|---|---|
| 설치 에셋 분류·삭제 | Unity AssetDatabase 이동·삭제, 파일 목록 대조 | 10개 패키지 분류, 1,286개 파일(메타 포함) 제거, 약 109.6 MiB 감소 | 정적 검증 통과 |
| 참조·메타 보존 | 이동 전 메타 GUID 목록과 이동 후 목록 대조, 직렬화 파일의 삭제 GUID 검사 | 보존 대상 GUID 3,746개 일치, 중복·누락·고아 메타·삭제 GUID 참조 모두 0 | 정적 검증 통과 |
| 기존 프로젝트 씬 보존 | `Assets/00.Scenes/SampleScene.unity` SHA-256 대조 및 에디터 dirty 상태 | 파일 내용 동일, dirty=false | 정적 검증 통과 |
| 컴파일 | Unity 재컴파일 및 Console 오류 조회 | 오류 0. 외부 코드의 구 API 등 경고 150개(Mesh Baker 108, RaycastPro 26, Wingman 15, Hierarchy Designer 1)는 별도 유지보수 대상 | 조건부 통과 |
| DOTween 설정 로드 | 에디터에서 `Resources.Load("DOTweenSettings")` | 이동한 설정 에셋 로드 성공 | 기능 확인 |
| 보정된 도구 경로 | Dialogue Manager·SceneFaderCanvas 프리팹 로드, Mesh Baker 작업 폴더 API 호출 | 프리팹 둘 다 로드 성공, 작업 폴더가 새 경로 반환 | 기능 확인 |
| VFX 시각 결과·플러그인 전체 기능·다인 접속 | 미실행 | 폴더 정리 범위에서 수행하지 않음 | 미검증 |

원본 백업 대비 남은 파일의 변경도 확인했다. 수동 코드 수정은 승인된 3개 파일의 경로 7곳이다. 설치 과정에서 추가된 Pixel Crushers 소스 65개의 주석만 있는 변경과 DOTween 설정 변경은 보존했으며 게임 로직 변화로 기록하지 않았다. 문서 링크·경로와 공백 검사 결과도 함께 확인한다.

## 공용 배경·소품 제작 — 2026-10-01 체크포인트

| 구성 | 출처·가공 | 공유 경로·사용 조건 |
|---|---|---|
| Meshy 원본 가공21종 | Meshy CLI 0.3.0로 소품9·건축4·자연물8 원본을 생성·다운로드하고 Blender MCP로 수정. 원본 생성630크레딧, 자연물8종 추가 리메시40크레딧을 실행했다. 실제 AO·PBR과 3LOD를 제작 | `Assets/01.Scripts/05.Art/{Environment,Props}/{이름}/`, `Assets/02.Prefab/{Environment,Props}/{이름}/`. 모델별 이름·사용 조건은 [공용 환경 에셋 사용 목록](../unity/SHARED_ENVIRONMENT_ASSET_CATALOG.md)을 따른다 |
| 규격 모듈42종 | Blender에서 프로젝트 규격에 맞춰 직접 제작하고 실제 AO·PBR 처리 | 같은 Environment 경로. 3LOD의 기하가 같아 삼각형 감면은 없다 |
| 기존 원본 파생7종 | 기존 FloatingRock 파생 바위5종과 CherryTree 파생2종. 원본 모델·재질·프리팹 보존, 파생형 크기·피벗·용도별 Collider 조정 | 파생형은 공용 목록 경로를 따른다. 중앙 거대 벚꽃나무는 신규 제작에 포함하지 않는다 |

생성 입력·작업 ID·원본·편집본·상세 제작 기록은 Git 제외 개인 작업 영역에 보존했다. 인증값은 에셋과 문서에 넣지 않는다. 생성 계정 플랜과 입력·출력 권리 조건은 기존 Meshy 기록과 같이 별도 확인 상태이며, 이번 정적·시각 검증을 상업 이용 권리 확인으로 취급하지 않는다. 현재70종은 조건부 통과이고 식생·목조 교각 표면 보정과 게임 사용·성능·빌드 검증이 남아 있다.

## 원화 대조 마을 제작 — 2026-10-01~02

사용자의 원화 대조·세부 건물 제작 요청으로 CA04의 개별 물체 참조를 준비하고 Meshy 원본 8건을 생성했다. 받은 원본은 Blender 5.1에서 형상·치수·UV·노멀·재질·실제 AO를 수정한 뒤 Unity에 가져온다. Blender MCP의 연결 불가 상태에서 실제 Blender 실행 파일의 background Python을 사용했으며, Blender 가공을 생략하지 않았다. 각 작업의 실제 비용은30, 합계240크레딧이며 추가 유료 재생성은 없다.

| 제작물 | Meshy 작업 ID | 공유 이름 |
|---|---|---|
| 이중 지붕 전각 | `01a0f7d5-7e61-7458-bc2a-487534893da2` | `ReferenceGrandHall` |
| 열린 정자 | `01a0f7d5-bea5-70b1-b2bf-60eebc3a572a` | `ReferencePavilion` |
| 석주·붉은 문 | `01a0f7d5-bf5d-72ba-bca9-4536f261de4e` | `ReferenceCeremonialGate` |
| 공방 | `01a0f7d5-87a3-731b-91e2-22728104dcc0` | `ReferenceWorkshop` |
| 기와 주거 | `01a0f7d5-87a0-749c-8ddc-ae70a4b53f01` | `ReferenceTiledResidence` |
| 초가 | `01a0f7d5-87cb-772d-9b21-2904ae47f3e9` | `ReferenceThatchedCottage` |
| 크림색 시장 차양 | `01a0f7d5-877d-752a-9d2a-d8ee2b46a9a9` | `ReferenceMarketCanopy` |
| 불규칙 석판 길 | `01a0f7d5-a6f9-73e5-9aeb-5c8ac24f7b5b` | `ReferenceStonePath` |

런타임은 `Assets/01.Scripts/05.Art/Environment/{공유 이름}/`, 배치용 프리팹은 `Assets/02.Prefab/Environment/{공유 이름}/`이다. 기존 FloatingRock 작업 `01a0f4de-b032-70ef-923a-05f9a0c97a84`의 Blender 청크 원본에서 파생한 `ReferenceIsland` 7종은 추가 Meshy 비용0이며 원본 UV·아랫면을 보존하고 윗면과 색·AO를 다시 처리했다. `ReferenceShrineDetails`는 기존 링 원본을 바탕으로 Blender에서 천·기둥·돌 기단을 제작했고, `VillageAssembly/PF_ReferenceSanctuaryLantern`은 기존 모듈·등을 조립한 것이다. 신규 Meshy 생성 수에 이 파생·직접 제작·조립물을 더하지 않는다.

원본 GLB·편집 blend·입력 이미지·반려된 축소 후보·상세 검수는 Git 제외 개인 제작 기록에 보존한다. 권리 확인 상태는 위 기존 Meshy 기록과 같다. 최종 경로·LOD 예외·시각 검증·사용 제한은 [공용 환경 에셋 목록](../unity/SHARED_ENVIRONMENT_ASSET_CATALOG.md)의 원화 대조 마을 항목을 따른다.

## Tripo 여성 공통 몸체·얼굴·직업 참조 — 2026-10-02~03

사용자가 승인한 생성 이미지 입력을 열린 Tripo Studio에 업로드하고 High/리토폴로지 원본을 실제 다운로드했다. 입력은 AI 생성 캐릭터 원화이며 실존 인물 사진 텍스처를 사용하지 않았다. 모델별 원본·프롬프트·파일 해시·차감 내역은 Git 제외 개인 docs에 보존한다. 아래 직업 전신은 분리 의상·헤어 제작의 참조 원본이며 최종 게임 모델이 아니다.

| 원본 | Tripo 작업 ID | 가공·공유 범위 |
|---|---|---|
| 공통 여성 몸체 | `14c30fc6-18e6-4a72-a423-7969ee88298f` | 새 52뼈 골격·스킨·3LOD·AO/PBR. `Assets/01.Scripts/05.Art/Characters/PlayerFemaleTripo/CommonBody/` |
| 귀여운 여성 얼굴 | `b2214a4c-64e3-4087-a2ef-47cc0836659b` | High 기반 얼굴 정리·공통 목 연결·3LOD·AO/PBR. 같은 루트의 `Heads/Cute/` |
| 무사 전신 참조 | `6a518a8a-11f6-4858-8c71-b64564cd23cc` | 원본 보존, 종전 옷 스킨 후보는 변형 실패로 기각 |
| 도술사 전신 참조 | `ac01bb7d-eb79-4968-947c-1bb54af6a8b6` | High/리토폴로지 원본 보존, 분리 의상·헤어 제작 대기 |
| 신령사 전신 참조 | `e9fd2649-debe-483b-adde-86f14801a6a9` | High/리토폴로지 원본 보존, 분리 의상·헤어 제작 대기 |

관찰한 생성·리토폴로지·리깅 차감은 합계 415크레딧이며 추가 결제나 구독 변경은 하지 않았다. Tripo 리깅/애니메이션 내보내기 일부는 Chrome 차단으로 받지 못했으므로 성공으로 기록하지 않는다. 공통 몸체의 최종 골격은 Blender에서 별도로 제작·검증했다. 검토용 프리팹은 `Assets/02.Prefab/Characters/PlayerFemaleTripo/`의 `PF_FemaleCommonBody`, `PF_FemaleCommonBody_Cute`, `PF_FemaleHeadCute`다.

**상업 이용 권리 확인은 보류**다. [Tripo 공식 상업 이용 안내](https://www.tripo3d.ai/help/privacy-policy/how-to-use-tripo-models-commercially)는 유료 플랜과 무료 플랜의 권리 조건을 구분하지만, 생성 당시 계정 플랜은 확인하지 못했다. 크레딧 잔액과 다운로드 성공만으로 유료 플랜·권리 보유를 추정하지 않는다. 상업 배포 전 생성 당시 플랜과 적용 약관·입력 권리를 확인해야 하며, 현재 외형·리깅 검증은 그 확인을 대신하지 않는다. 확인한 제작 범위와 남은 게임 사용 검증은 [모델 제작 목록](../design/model-production-list.md#tripo-공통-여성-몸체귀여운-얼굴-첫-검토-묶음--2026-10-03)을 따른다.

## CA-07 추가 환경 모델 — 2026-10-03

CA-07(`docs/design/references/concept-art/village-layout-02.webp`)에서 기존 Assets에 없던 4종을 사용자 승인으로 제작했다. 입력 이미지는 원화 크롭을 스타일 참조로 첨부해 Codex CLI 0.159.3(`gpt-6.1-sol`)의 내장 이미지 생성으로 만든 단일 물체 시안이다. Meshy CLI 0.3.0 image-to-3d(`meshy-7.1`, PBR, 2K, `remove_lighting=true`, 리메시 없음)로 각 1회만 생성했고 재시도는 없다. 각 작업의 실제 차감은 30, 합계 120크레딧이다. 받은 원본은 실제 Blender 5.1 background Python으로 가공했다.

| 공유 이름 | Meshy 작업 ID | 가공 요약 |
|---|---|---|
| `Waterwheel` | `01a0fd83-e275-7601-af22-7dd75dfd6eff` | 감면·새 UV·원본→감면 전사 베이크·실제 AO |
| `WheatFieldPlot` | `01a0fd83-ec06-7464-b129-ae4477e5981d` | 흙 둔덕은 원본 높이장 재구성 후 전사 베이크·실제 AO, 밀은 원본 밀 줄기를 렌더한 알파 카드 아틀라스와 카드 버텍스 AO |
| `LotusCluster` | `01a0fd83-f47c-73b4-816c-1c2dced15083` | 높이 0.6배 압축·수면 피벗·감면·전사 베이크·실제 AO |
| `ArchedTimberBridge` | `01a0fd83-fd3a-77ca-ac64-458ddb1ac906` | 폭 1.06배, 아치 높이를 포물선 변위로 약 0.78→1.47m로 키움, 감면·전사 베이크·실제 AO |

런타임은 `Assets/01.Scripts/05.Art/Environment/{공유 이름}/`, 배치용 프리팹은 `Assets/02.Prefab/Environment/{공유 이름}/`이다. 입력 시안·원본 GLB·편집 blend·베이크 원본·검수 렌더는 Git 제외 개인 제작 기록에 보존한다. 인증값은 기록하지 않았다.

권리 확인 상태는 위 기존 Meshy 기록과 같다. 생성 계정 플랜과 입력 이미지(Codex 생성)·출력 권리 조건은 확인하지 않았으며, CC0나 독점 소유로 확정하지 않는다. 사용 조건과 검증 범위는 [공용 환경 에셋 목록](../unity/SHARED_ENVIRONMENT_ASSET_CATALOG.md#ca-07-추가-에셋--2026-10-03)을 따른다.
