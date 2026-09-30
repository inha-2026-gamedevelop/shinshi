# 처음 프로젝트를 열 때

> 클론부터 첫 실행까지. 이 문서 하나만 끝내면 개발을 시작할 수 있다.
> 2026-09-30 shinshi 기준으로 이식.

---

## 1. 클론

```bash
git clone https://github.com/inha-2026-gamedevelop/shinshi.git
cd shinshi
git lfs install
git lfs pull
```

**Unity 프로젝트 루트는 저장소 루트다.** `Assets/`, `Packages/`, `ProjectSettings/`가 클론한 폴더 바로 아래에 있다.

이미지 · 모델 · 오디오 같은 대용량 바이너리는 Git LFS로 관리한다(대상은 `.gitattributes`). `git lfs install`은 이 클론에 LFS 훅을 설치하므로 클론마다 한 번 실행한다. 안 하면 이미지와 모델이 텍스트 한 줄로 보이고, 새 바이너리가 LFS 없이 커밋된다. 자세한 내용은 [씬 · 에셋 안전](SCENE_AND_ASSET_SAFETY.md) 3장.

---

## 2. Unity 버전과 여는 폴더

**Unity 6000.4.7f1** (Unity 6.4)로 연다. Unity Hub에서 해당 버전을 설치한다. 기준은 `ProjectSettings/ProjectVersion.txt`다.

Unity Hub에서 `Add > Add project from disk`로 **클론한 저장소 루트 폴더를 고른다.** `Assets/`나 `docs/` 같은 하위 폴더를 고르면 Unity가 그 자리에 빈 프로젝트를 새로 만든다.

다른 버전으로 열면 프로젝트 파일이 자동 갱신되고, 되돌리기 어렵다.
버전이 없으면 열기 전에 먼저 설치한다.

### 주요 패키지

`Packages/manifest.json` 기준.

| 패키지 | 버전 | 비고 |
|---|---|---|
| Universal RP | 17.4 | PC, Mobile RP 에셋이 `Assets/Settings/`에 있다 |
| Input System | 1.19 | 설치만 되어 있다. 팀 입력은 구 Input Manager를 쓰며 Active Input Handling은 Both다 |
| Unity Test Framework | 1.6 | |
| AI Navigation | 2.0 | |
| Rider, Visual Studio 에디터 패키지 | | 쓰는 IDE를 `Edit > Preferences > External Tools`에서 고른다 |
| **Mirror** | 96.11.3 | `Assets/Mirror/`에 Git으로 포함된다. 공식 GitHub 릴리스 `.unitypackage`로 설치했고 동봉 스크립트 템플릿은 제외했다 |

Mirror를 업데이트하는 사람은 같은 방법(공식 릴리스 `.unitypackage` 임포트, `ScriptTemplates/` 제외)을 따르고 이 표와 [에셋 대장](../licenses/ASSET_REGISTRY.md)의 버전을 고친다.

### Asset Store 에셋 설치

유료 플러그인은 Git에서 제외되므로 새 클론에는 들어 있지 않다. [에셋 대장](../licenses/ASSET_REGISTRY.md)의 설치 버전과 분류 경로를 확인하고 라이선스를 보유한 에셋을 임포트한다. 임포트 직후 원래 위치에 생긴 중복 패키지를 정리하고 Unity 에디터에서 `Assets/99.ThirdParty/`의 해당 분류로 옮긴다. 팀 씬·프리팹이 구매 에셋을 참조하면 기존 GUID와 참조 복원을 확인한다. 대장의 경로 보정과 DOTween 모듈 설정도 적용한다.

---

## 3. 필수 프로젝트 설정 확인

처음 연 뒤 아래 두 가지를 확인한다.

```
Edit > Project Settings > Version Control > Mode
  → Visible Meta Files

Edit > Project Settings > Editor > Asset Serialization
  → Force Text
```

이게 안 되어 있으면 `.meta`가 생성되지 않거나 씬이 바이너리로 저장되어 협업이 불가능해진다.

---

## 4. UnityYAMLMerge 설정

한 번만 하면 된다 (Windows 기준).

```bash
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver \
  '"C:/Program Files/Unity/Hub/Editor/6000.4.7f1/Editor/Data/Tools/UnityYAMLMerge.exe" merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"'
```

경로는 실제 설치 위치에 맞춘다. `.gitattributes`가 씬 · 프리팹 · 에셋 등에 `merge=unityyamlmerge`를 지정해 두었으므로, 이 로컬 설정이 있어야 그 드라이버가 실제로 쓰인다.

---

## 5. 자기 씬 열기

자기 파트의 개발 씬을 연다. 소유자가 아닌 씬은 열 수 있지만 저장하지 않는다.

지금은 템플릿 씬 `Assets/00.Scenes/SampleScene.unity` 하나뿐이다. **(도입 예정)** 파트가 정해지면 파트마다 개발 씬을 하나씩 만들고 아래 표를 채운다. 소유 규칙은 [씬 · 에셋 안전](SCENE_AND_ASSET_SAFETY.md) 1장.

| 파트 | 씬 |
|---|---|
| {파트} ({팀원이름}) | `Assets/{씬 폴더}/Dev_{파트}.unity` |

---

## 6. 첫 실행

1. 자기 씬을 연다
2. Play 버튼을 누른다
3. Console에 에러가 없는지 확인한다

에러가 있으면 **고치기 전에 먼저 팀에 공유한다.** 자기 환경 문제인지 코드 문제인지 구분해야 한다.

---

## 7. 여러 인스턴스로 접속 테스트

멀티플레이 게임이므로 혼자서도 여러 클라이언트를 띄워야 한다. 최대 인원은 {최대 인원}(미정)이다.
Mirror를 설치한 뒤부터 해당한다. 방법은 [멀티플레이 테스트 절차](../ai/04_MULTIPLAYER_TEST_PROTOCOL.md) 2절 참조.

---

## 8. 다음에 읽을 것

| 언제 | 문서 |
|---|---|
| C# 코드를 쓸 때 | [코딩 컨벤션](../coding-convention.md) |
| 파일이나 폴더를 만들 때 | [씬 · 에셋 안전](SCENE_AND_ASSET_SAFETY.md) |
| 커밋 · 푸시하기 직전 | [저장소 지침](../../AGENTS.md) |
| 네트워크 코드를 쓸 때 | [네트워크 권위](NETWORK_AUTHORITY.md) |
| 서버 판정 파일(`98.Servers/`)을 건드리거나 누가 작성할지 헷갈릴 때 | [서버·클라이언트 협업](SERVER_CLIENT_WORKFLOW.md) |
| 경계를 넘어 알리는 방법이나 패턴을 고를 때 | [디자인 패턴](DESIGN_PATTERNS.md) |
| 밸런스 수치를 다룰 때 | [밸런스 데이터 작업](BALANCE_DATA_WORKFLOW.md) |
| 성능 문제가 실제로 생겼을 때 | [Unity 엔지니어링 패턴](UNITY_ENGINEERING_PATTERNS.md) |

**처음부터 전부 읽지 않는다.** 필요할 때 해당 문서만 연다.

---

## 자주 겪는 문제

| 증상 | 원인 | 해결 |
|---|---|---|
| Unity Hub가 빈 프로젝트를 만들려 함 | 저장소 루트가 아닌 하위 폴더를 골랐다 | 저장소 루트 폴더를 고른다 |
| 참조가 Missing | `.meta` 누락 | 팀에 공유, 임의로 다시 연결하지 않음 |
| 컴파일 에러가 안 사라짐 | 재컴파일 중이거나 Play Mode | 기다리거나 Play 중지 |
| 씬을 열었는데 비어 있음 | 다른 씬을 연 것 | 경로 확인 |
| 프로젝트가 업그레이드되려 함 | Unity 버전 불일치 | **취소하고** 6000.4.7f1로 다시 연다 |
