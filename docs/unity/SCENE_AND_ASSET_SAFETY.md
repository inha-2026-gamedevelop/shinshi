# 씬 · 에셋 안전 규칙

> Unity 프로젝트에서 되돌릴 수 없는 사고는 대부분 씬 충돌과 `.meta` 손실에서 나온다.
> 이 문서의 규칙은 협상 대상이 아니다.
> 2026-09-30 shinshi 기준으로 이식.

---

## 1. 씬 소유권

**Unity 씬 파일은 머지가 사실상 불가능하다.** 두 사람이 같은 씬을 고치면 한쪽 작업이 통째로 사라진다.

템플릿 씬 `Assets/00.Scenes/SampleScene.unity`와 서버·클라이언트 빌드 진입 씬 `Assets/00.Scenes/Bootstrap.unity`가 있다. 개발 씬은 파트별로 만들면서 아래 표에 소유자를 등록한다. 씬 하나에 소유자는 한 명이다.

| 씬 | 수정 권한 |
|---|---|
| `Assets/00.Scenes/Bootstrap.unity` | **{팀장}** |
| `Assets/00.Scenes/Dev_PlayerMovement.unity` (이동 예측, 회피 시험) | **{팀장}** |
| `Assets/00.Scenes/Dev_ShinshiVillageQuarterView.unity`, `Dev_ShinshiVillageCA07.unity` (마을 원화 배치·대기 연출 검수) | **{팀장}** |
| `Assets/00.Scenes/Dev_VFX/Dev_VFX_WideSlash.unity` (무사 넓게 베기 VFX 반복 재생 확인) | **{팀장}** |
| `Assets/00.Scenes/Dev_HUD/Dev_Setting.unity` (환경설정 UI·저장·취소 복원 검수) | **UI (Jinwook)** |
| `Assets/{씬 폴더}/Dev_{파트}.unity` | {파트} ({팀원이름}) |
| `Assets/00.Scenes/Release/` 실제 실행 흐름 씬: `Lobby`, `CharacterSelect`, `CharacterCreate`, `ShinshiTown` (Bootstrap에서 로비, 캐릭터 선택·생성을 거쳐 마을 서버의 마을로 접속) | **{팀장}** (통합 담당) |
| `Assets/00.Scenes/Release/DungeonForest.unity` (숲 일반전 던전 전장, 네트워크 연결 전) | **{팀장}** (통합 담당) |
| 그 밖의 실제 런타임 흐름 씬 (게임 등) | 통합 담당자 ({팀원이름}) |
| 메인 씬 (이름 미정) | **통합 담당자만** |

### 규칙

- 남의 씬은 **열어도 되지만 저장하지 않는다.** 열기만 해도 Unity가 미세한 변경을 기록할 수 있다
- 실수로 저장했으면 먼저 diff와 기존 작업 유무를 확인하고 자기 변경만 복구한다. 다른 사람의 변경까지 파일 단위로 되돌리지 않는다.
- 통합은 정해진 주기(미정)에 통합 담당자가 진행한다
- 한 PR에 씬 파일이 2개 이상 들어가면 이유를 PR에 적는다. [PR 검사 워크플로](../../.github/workflows/pr-guard.yml)가 이 경우 경고를 띄운다

### AI에게

씬 파일을 수정하기 전에 **어느 씬인지, 그 씬의 소유자가 누구인지** 확인한다.
메인 씬을 건드려야 하면 반드시 사전 승인을 받는다.

---

## 2. `.meta` 파일

Unity는 모든 에셋에 `.meta`를 만들고, 그 안에 GUID를 넣는다.
프리팹과 씬은 **경로가 아니라 GUID로** 에셋을 참조한다.

`.meta`를 빼고 커밋하면:
- 다른 사람의 에디터에서 GUID가 새로 발급된다
- 프리팹과 씬의 참조가 전부 끊긴다 (Missing 표시)
- 원인이 드러나기까지 시간이 오래 걸린다

### 규칙

- 에셋을 추가하면 `.meta`를 **함께** 커밋한다
- 에디터 밖에서 파일을 옮길 때는 `.meta`도 같이 옮긴다
- 에셋을 지우면 `.meta`도 지운다
- Unity 에디터 안에서 이동 · 삭제하면 자동으로 처리된다. **가능하면 에디터 안에서 한다**

### 필수 프로젝트 설정

```
Edit > Project Settings > Version Control > Mode
  → Visible Meta Files

Edit > Project Settings > Editor > Asset Serialization
  → Force Text
```

이 두 설정이 없으면 `.meta`가 생성되지 않거나 씬이 바이너리로 저장되어 diff가 불가능해진다.

### 자동 검사

커밋 전에 `git status`로 새 에셋마다 `.meta`가 같이 올라가는지 직접 확인한다. main으로 가는 PR마다 [PR 검사 워크플로](../../.github/workflows/pr-guard.yml)가 `.meta` 누락을 검사하고(`.github/scripts/check_meta.py`), 누락이 있으면 실패한다. 실패하면 병합하지 않는다.

---

## 3. Git LFS

LFS를 쓴다. 대상 확장자는 `.gitattributes`에 지정되어 있다. `git lfs install`은 클론마다 LFS 훅을 설치하므로 사람마다, 클론마다 한 번씩 실행한다.

### 클론 직후 반드시

```bash
git lfs install
git lfs pull
```

이걸 안 하면 이미지와 모델이 텍스트 한 줄로 보인다.

### 왜 중요한가

한 번 일반 파일로 커밋된 대용량 에셋은 **히스토리에 영구히 남는다.**
나중에 LFS로 옮겨도 리포 용량은 줄지 않는다. 커밋 시점에 막아야 한다.

### 대상

`.gitattributes`에 지정한다. 이미지, 3D 모델, 오디오, 영상, 지형 데이터, 폰트, 압축(`.7z`).

새 확장자를 추가할 일이 생기면 **`.gitattributes`를 고치기 전에 팀에 알린다.** 이미 커밋된 파일에는 소급 적용되지 않는다.

### 자동 검사

main으로 가는 PR마다 [PR 검사 워크플로](../../.github/workflows/pr-guard.yml)가 LFS 대상 확장자가 LFS 포인터가 아닌 일반 파일로 들어오면 실패시키고, LFS 대상이 아닌 5MB 초과 파일은 경고한다(`.github/scripts/check_lfs.py`).

---

## 4. 외부 에셋

외부에서 가져온 에셋은 원본을 **수정하지 않는다.** 2026-09-30부터 외부 Asset Store 에셋은 `Assets/99.ThirdParty/{분류}/{에셋}/`로 배치한다. 분류는 `Animation`, `Tools`, `VFX`, `Model`이며 실제 설치 목록과 경로 예외는 [에셋 대장](../licenses/ASSET_REGISTRY.md)에 기록한다. 원본 코드의 경로 보정이 필요하면 별도 승인을 받고 변경 내용을 대장에 남긴다.

| 폴더 | 내용 | git |
|---|---|---|
| `Assets/99.ThirdParty/{분류}/{에셋}/` | 유료 에셋과 동봉 의존 라이브러리 | 패키지 폴더와 `.meta`를 `.gitignore`에서 제외 |
| `Assets/99.ThirdParty/{분류}/{에셋}/` | 무료로 확인된 독립 에셋 | **커밋한다** (패키지 단위 제외 규칙에 넣지 않음) |
| `Packages/`, `Assets/Plugins/TextMesh Pro/` | Unity 기본 패키지와 TMP 필수 리소스 | 기존 위치와 버전 관리 유지. TMP는 `Resources/` 구조 유지 |

Mirror처럼 팀 전원이 같은 버전을 써야 하는 라이브러리는 설치 방법을 정한 뒤 [처음 프로젝트를 열 때](UNITY_START_HERE.md) 2장에 적는다.

고쳐야 하면:
1. 필요한 파일만 프로젝트 폴더로 복사
2. 복사본을 수정
3. 원본은 그대로 유지

원본을 고치면 에셋 업데이트 시 변경분이 날아간다. 그리고 무엇을 고쳤는지 추적할 수 없다.

### 무료 에셋을 커밋할 때

무료 에셋 폴더는 구매 에셋 폴더와 달리 저장소에 올라간다. 팀원이 각자 임포트하면 GUID가 갈려 참조가 끊어지기 때문이다. 넣기 전에 확인할 것:

- `.meta`를 **하나도 빠짐없이** 같이 커밋한다. 하나라도 빠지면 2장의 사고가 그대로 재현된다.
- 바이너리가 LFS 대상 확장자인지 확인한다 (3장). 대상이 아닌 대용량 바이너리면 `.gitattributes`를 고치기 전에 팀에 알린다.
- 에셋 하나당 폴더 하나로 담는다.
- 딸려 온 데모 씬을 `EditorBuildSettings`에 추가하지 않는다.

### 라이선스

구매 · 무료 에셋은 상업 이용 가능 범위로 관리한다.
근거와 설치 버전, 이동 경로, 취득일 확인 상태를 [에셋 대장](../licenses/ASSET_REGISTRY.md)에 보관한다.

| 기록할 것 |
|---|
| 에셋명, 제작자 |
| 출처 URL |
| 라이선스 종류 |
| 상업 이용 가능 여부 |
| 취득일 |

### 팀이 만든 모델링 에셋

2026-10-01부터 팀이 직접 만들거나 가공한 모델과 그 재질 · 텍스처 · 셰이더는 모두 `Assets/01.Scripts/05.Art/` 아래에서 관리한다. 외부 에셋은 위 규칙대로 `99.ThirdParty/`에 두고 이 폴더로 옮기지 않는다. 외부 모델을 가공해 쓰면 필요한 파일만 이 폴더로 복사해 고친다.

```
Assets/01.Scripts/05.Art/
├ Characters/{에셋}/   플레이어 · 산귀 · 보스 등 캐릭터
├ Environment/{에셋}/  나무 · 건물 · 지형 소품 등 배경
├ Props/{에셋}/        상호작용 물체 · 무기 · 아이템 등 소품
├ UI/{에셋}/           HUD 등 UI 스프라이트 (Textures/만 둔다)
└ Shaders/             여러 에셋이 함께 쓰는 셰이더 그래프
```

모델 에셋 폴더 안은 `Models/`, `Materials/`, `Textures/`로 나눈다. 씬에 배치할 프리팹은 사용자 지정에 따라 `Assets/02.Prefab/{분류}/{에셋}/`에 둔다. 모델의 `Characters/`, `Environment/`, `Props/` 분류를 프리팹에도 적용한다. `Camera/`, `Telegraph/`, `VFX/`는 표현 스크립트 폴더이므로 모델을 넣지 않는다.

**모델 제작은 프리팹 생성과 참조 검증까지가 기본 과정이다.** FBX 임포트 뒤 재질·노멀·AO 등 해당 모델의 표면 표현과 LOD를 연결하고, 용도에 맞게 충돌체를 설정한 뒤 `02.Prefab/`에 `PF_` 프리팹을 만든다. 장식용 모델은 Collider를 생략할 수 있다. 프리팹의 모델·재질·LOD 참조, `.meta`, 임포트·Console과 시각 결과를 확인하고 실제로 하지 못한 게임플레이·성능 검증은 별도로 표시한다.

사용자 지정 기본 과정에 따라 **AO·노멀·거칠기 등 실사에 가까운 3D 스타일화 판타지 재질 작업은 매번 별도 요청을 기다리지 않고 수행한다.** 모델과 셰이더에 맞는 실제 AO 베이크 또는 검증된 버텍스 AO를 사용하고, 색에 섞인 조명·중복 차폐·과한 검은 틈을 점검한다. 물·발광 효과처럼 AO를 생략할 합리적 이유가 있으면 작업 기록에 남긴다. 원화 분석·ChatGPT 목표 이미지·Blender·URP 재질·LOD·프리팹·완료 증거는 [아트 제작 기본 과정](ART_ASSET_PRODUCTION_WORKFLOW.md)을 따른다.

| 종류 | 접두사 | 예 |
|---|---|---|
| 정적 메시 | `SM_` | `SM_CherryTree.fbx` |
| 스킨드 메시 (리그 있음) | `SK_` | `SK_{캐릭터}.fbx` |
| 재질 | `M_` | `M_CherryTree_Blossom.mat` |
| 텍스처 | `T_{에셋}_{용도}` | `T_CherryTree_Trunk_BaseColor.jpg` (`BaseColor`, `Normal`, `AO`, `MetallicSmoothness`, `Mask`) |
| 셰이더 그래프 | `SG_` | `SG_VertexColorFoliage.shadergraph` |
| 프리팹 | `PF_` | `PF_CherryTree.prefab` |

- 모델은 FBX로 넣는다. Blender 원본(`.blend`)은 `Assets/`에 넣지 않는다. Unity가 `.blend`를 임포트하려면 팀원 PC마다 Blender가 설치되어 있어야 하기 때문이다. 원본 보관 위치는 아직 정하지 않았다.
- 폴더를 옮기거나 이름을 바꿀 때는 Unity 에디터 안에서 해 GUID를 유지한다 (2장).
- 폴리지처럼 버텍스 컬러를 쓰는 재질 설정은 [버텍스 컬러 폴리지 셰이더](VERTEX_COLOR_FOLIAGE_SHADER.md)를 따른다.

---

## 5. Terrain 데이터

Terrain을 쓰게 되면 **담당자 한 명({팀원이름})만 수정한다.**

여러 명이 건드리면 머지 충돌 시 복구가 불가능하다. Terrain 데이터는 diff를 읽을 수 없다.

---

## 6. UnityYAMLMerge

`.gitattributes`에는 씬, 프리팹, 에셋 등 Unity 직렬화 확장자와 `.meta`에 `merge=unityyamlmerge`가 지정되어 있다. 하지만 **로컬 git config가 없으면 무시된다.**

각자 한 번씩 설정한다 (Windows 기준):

```bash
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver \
  '"C:/Program Files/Unity/Hub/Editor/6000.4.7f1/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"'
```

경로는 실제 설치 위치에 맞춘다.

**주의**: SmartMerge가 있어도 씬 동시 편집은 여전히 위험하다. 이건 최후의 안전망이지 허가증이 아니다.

---

## 7. 사고가 났을 때

### 씬을 덮어썼다

1. 즉시 작업을 멈춘다. 추가 커밋을 하지 않는다
2. `git reflog`로 덮어쓰기 이전 커밋을 찾는다
3. 해당 커밋의 씬 파일만 꺼낸다: `git checkout <해시> -- Assets/<씬 경로>.unity`
4. 팀에 알린다. 상대의 작업이 사라졌을 수 있다

### `.meta`가 날아가 참조가 끊겼다

1. `git log --diff-filter=D -- "*.meta"` 로 삭제된 `.meta`를 찾는다
2. 복구: `git checkout <해시>^ -- <경로>.meta`
3. 복구가 안 되면 프리팹과 씬에서 참조를 수동으로 다시 연결해야 한다

### 대용량 파일이 LFS 없이 들어갔다

LFS 대상 확장자가 포인터가 아닌 일반 파일로 커밋된 경우다. 대개 `git lfs install`을 하지 않은 클론에서 생긴다. **커밋 직후라면** `git lfs install` 후 amend로 고칠 수 있다:

```bash
git rm --cached <파일>
git add <파일>
git commit --amend
```

**이미 푸시했다면** 히스토리 재작성이 필요하다. 혼자 하지 말고 {팀장}과 상의한다.

---

## 8. AI 작업 시 추가 규칙

- 씬 · 프리팹 · 에셋을 수정한 뒤 **임포트와 Console 오류를 확인**한다
- 파일을 되돌리기 전에 diff를 확인한다. 변경 전체가 자기 작업임을 확인하지 못하면 파일 단위 복원을 실행하지 않는다
- 삭제 · 대량 이동 · 강제 덮어쓰기는 대상을 명시하고 사전 승인을 받는다
- 시각 결과는 캡처나 사용자 확인 없이는 `미검증`으로 표시한다
