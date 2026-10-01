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
| UMA (Unity Multipurpose Avatar) | 3.1f1 (GitHub 릴리스 `V3.1f1`, 2026-09-28 게시) | UMA Steering Group | `Assets/UMA/` (경로 예외) | 무료, 외부 원본 | MIT(GitHub 저장소)·조건부 | [GitHub 릴리스](https://github.com/umasteeringgroup/UMA/releases/tag/V3.1f1) |

UMA는 2026-10-01 팀장 승인으로 GitHub 공식 릴리스의 `UMA31_f1.unitypackage`(1,170,694,098바이트, SHA-256 `701a87dfd7e8a00a65f5a8066e23e8a3d62327214269992776db2cc7efe48866`)를 가져왔다. Asset Store판(3.0.5, 표준 EULA)이 아니다. UMA 코드 33개 파일(런타임 `Core/Scripts/UMASettings.cs` 포함)이 `Assets/UMA/` 경로를 직접 적고 있어 `99.ThirdParty/`로 옮기지 않고 루트에 둔다. 원본은 고치지 않고 우리 연결 코드는 `Assets/01.Scripts/`에 둔다(아바타 설계 A안). 패키지가 커서 Git에서 제외하고 다른 외부 에셋처럼 따로 공유한다. 가져오기 뒤 Console 오류 0을 확인했다. Unity 6000.4.7f1·URP 실제 표시와 서버 빌드 영향은 미검증이다.

설치 버전은 `.meta`의 `AssetOrigin.productId`, `packageName`, `packageVersion`으로 확인했다. Mesh Baker는 해당 메타데이터가 없고 동봉 문서가 `docs/API-3.37.0.zip`이다. 소스의 버전 문자열은 3.39.0이다. 2026-10-01 팀장이 유료판 결제를 확인해 에디션을 유료로 기록한다. 결제 증빙과 팀원별 사용 좌석은 이 대장에서 검증하지 않았다. 유료 에셋이므로 다른 유료 에셋과 같이 패키지를 Git에서 제외하고 설치할 때마다 받는다. 환경 메시 결합에 쓰려고 `Shinshi.Runtime`이 `MeshBakerCore` 어셈블리를 참조한다. 플레이어 아바타는 UMA가 결합한다.

## 공용 돌바닥·부유석 제작 소재와 가공물

2026-10-01 사용자 제공 참고 시트와 실사 판타지 제작 요청을 기준으로 아래 소재를 취득·가공했다. Asset Store 설치 패키지와 구분하여 기록하며, Unity 임포트·프리팹·에디터 프리뷰 확인과 미검증 항목은 [제작·사용 문서](../unity/STONE_COURTYARD_AND_FLOATING_ROCK.md)에 기록한다.

| 에셋 | 버전·제작 기록 | 제작자 | 보관·사용 경로 | 구분 | 라이선스·상업 이용 | 출처 |
|---|---|---|---|---|---|---|
| Rock 01 석재 PBR | 2026-10-01 취득, 4K Base Color·Roughness·OpenGL Normal·Height | Rob Tuytel / Poly Haven | 원본 `.codex/art/realistic-fantasy-kit/textures/`, 돌바닥 가공 재질에 사용 | 외부 무료 소재 | CC0, 상업 이용·수정·재배포 가능 | [공식 자산](https://polyhaven.com/a/rock_01), [라이선스](https://polyhaven.com/license) |
| 공용 돌바닥 | 2026-10-01 Blender MCP 제작, 6×6m 모듈·3LOD FBX·PBR/AO 출력 | shinshi 프로젝트 제작 | 모델·재질 `Assets/01.Scripts/05.Art/Environment/StoneCourtyard/`, 프리팹 `Assets/02.Prefab/Environment/StoneCourtyard/` | 프로젝트 제작 메시·가공 재질 | 석재 표면 원본은 위 CC0. 자체 메시·베이크와 외부 소재를 구분하여 관리 | [참고 CA-05](../design/concept-art.md#6-ca-05-공용-돌바닥-타일), [제작 기록](../unity/STONE_COURTYARD_AND_FLOATING_ROCK.md) |
| 장식용 부유석 | Meshy CLI 0.3.0, `meshy-7.1`, 작업 `01a0f4de-b032-70ef-923a-05f9a0c97a84`, 2026-10-01 생성·다운로드, 실제 35크레딧·Blender 3LOD/PBR/AO 출력 | Meshy 생성 + shinshi Blender 가공 | 원본 `.codex/art/realistic-fantasy-kit/meshy/output/`, 모델·재질 `Assets/01.Scripts/05.Art/Environment/FloatingRock/`, 프리팹 `Assets/02.Prefab/Environment/FloatingRock/` | AI 생성 원본·프로젝트 가공물 | 생성 당시 계정 플랜 미확인. 플랜별 조건·입력 권리 확인 상태는 아래 기록 | [참고 CA-06](../design/concept-art.md#7-ca-06-부유석), [Meshy 소유 안내](https://help.meshy.ai/en/articles/10137554-what-is-the-ownership-of-the-generated-models), [이용 약관](https://www.meshy.ai/terms-of-use) |

- Rock 01 원본 PNG 네 개는 4096×4096이며 공식 파일 API의 MD5와 모두 일치한다. 시각 확인용 썸네일은 런타임 소재에 포함하지 않았다.
- 참고 그림은 사용자 제공이다. 작가·제작 도구·라이선스와 별도 권리 확인은 미확인으로 기록하고 소유권을 추측하지 않는다.
- Meshy 공식 안내는 유료 플랜 생성물의 비공개 소유와 무료 플랜 생성물의 CC BY 4.0·Meshy 표시 조건을 구분한다. 실제 생성 계정 플랜을 CLI에서 확인하지 못했으므로 이 부유석을 자체 소유 또는 CC0로 확정하지 않는다. 커뮤니티 공개는 요청하거나 수행하지 않았다. [상업 이용 안내](https://help.meshy.ai/en/articles/16102098-can-i-use-meshy-assets-commercially)
- 원본 GLB·FBX, 다운로드 텍스처, Blender `.blend`와 제작 기록은 위 `.codex/art/` 작업 영역에 보존하며 Git에서 제외한다. 런타임 모델·재질·텍스처는 [팀 모델링 에셋 규칙](../unity/SCENE_AND_ASSET_SAFETY.md#팀이-만든-모델링-에셋)에 따라 `05.Art/Environment/`, 배치용 프리팹은 `02.Prefab/Environment/`에서 `.meta`와 함께 공유한다. 모델 제작은 프리팹 생성·참조 검증까지 포함한다.
- 런타임 모델명은 `StoneCourtyard/Models/SM_StoneCourtyardTile_{High,Medium,Low}.fbx`, `FloatingRock/Models/SM_FloatingRock_{High,Medium,Low}.fbx`다. 단계는 LOD0·1·2에 대응하며 Unity 에디터에서 이름을 바꿔 GUID를 보존했다. 공유 제작 렌더는 [사용 문서의 미리보기](../unity/STONE_COURTYARD_AND_FLOATING_ROCK.md#7-최종-blender-베이크-미리보기)에 있다.
- Unity에서 `PF_StoneCourtyardTile.prefab`·`PF_FloatingRock.prefab`, URP Lit 재질·PBR/AO 연결과 프리뷰를 확인했다. `02.Prefab/` 이관 뒤 에셋 20개·폴더 포함 메타 32개에 누락 0, 프리팹 GUID 2개 유지와 에셋 관련 신규 오류·경고 0을 확인했다. Console 이력 변화와 연결 경고는 [검증 기록](../unity/STONE_COURTYARD_AND_FLOATING_ROCK.md#6-실제-검증과-남은-확인)에 따로 적었다. 기존 씬은 변경하지 않았다. 게임플레이·성능·빌드·배포 검증과 Meshy 계정 플랜 확인은 완료하지 않았다.

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
