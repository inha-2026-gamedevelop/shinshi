# 버텍스 컬러 폴리지 셰이더 · 재질 설정

> 버텍스 컬러에 구운 그늘(AO)을 쓰는 꽃 · 잎 메시용 URP 셰이더 그래프와 재질 설정 기준.
> 2026-10-01 작성. 에디터 임포트와 프리뷰 렌더까지 확인했고, 씬 배치 · 플레이 모드 · 빌드는 미검증이다.

---

## 1. 무엇이 있나

| 파일 | 역할 |
|---|---|
| `Assets/01.Scripts/05.Art/Shaders/SG_VertexColorFoliage.shadergraph` | 셰이더 그래프. 셰이더 이름 `Shinshi/Environment/SG_VertexColorFoliage` |
| `Assets/01.Scripts/05.Art/Environment/CherryTree/Models/SM_CherryTree.fbx` | 벚꽃나무 모델 (줄기 · 꽃 · 떨어진 꽃잎 3개 메시) |
| `Assets/01.Scripts/05.Art/Environment/CherryTree/Materials/M_CherryTree_Blossom.mat` | 꽃 · 꽃잎 재질 (이 셰이더 사용) |
| `Assets/01.Scripts/05.Art/Environment/CherryTree/Materials/M_CherryTree_Trunk.mat` | 줄기 재질 (URP Lit) |
| `Assets/01.Scripts/05.Art/Environment/CherryTree/Textures/T_CherryTree_Blossom_BaseColor.png` | 꽃 아틀라스 2048², 알파 포함 |
| `Assets/01.Scripts/05.Art/Environment/CherryTree/Textures/T_CherryTree_Trunk_BaseColor.jpg` | 줄기 베이스컬러 |

기본 URP Lit은 메시의 버텍스 컬러를 읽지 않는다. 버텍스 컬러에 그늘을 구워 둔 메시에 URP Lit을 쓰면 그늘이 사라져 납작하게 보인다. 그래서 이 셰이더를 쓴다.

---

## 2. 셰이더 구조

```
Base Color = Base Map(RGB) × Tint × lerp(1, Vertex Color, Vertex Color Strength)
Alpha      = ShinshiOcclusionFade(Base Map(A), 월드 위치, 화면 픽셀)     → Alpha Clip Threshold = Alpha Cutoff
Smoothness = Smoothness
```

| 속성 (표시 이름) | 참조 이름 | 기본값 | 용도 |
|---|---|---|---|
| Base Map | `_BaseMap` | 없음 | 색 + 알파(잘라낼 모양) |
| Tint | `_BaseColor` | 흰색 | 전체 색조 보정 |
| Vertex Color Strength | `_VertexColorStrength` | 1 | 0이면 버텍스 컬러 무시, 1이면 그대로 곱함 |
| Alpha Cutoff | `_AlphaCutoff` | 0.5 | 알파가 이 값보다 낮은 픽셀을 버림 |
| Smoothness | `_Smoothness` | 0.25 | 반사 정도 |

그래프 설정 (Graph Settings)

| 항목 | 값 | 이유 |
|---|---|---|
| Target | Universal, Material = Lit | URP 조명 · 그림자 사용 |
| Surface Type | Opaque | 알파 클립이라 불투명 패스로 충분하고 정렬 문제가 없다 |
| Alpha Clipping | 켬 | 꽃 모양으로 잘라냄 |
| Render Face | Both | 꽃 · 잎은 뒷면도 보여야 한다 |
| Allow Material Override | 켬 | 재질마다 Render Face, 그림자 등을 바꿀 수 있다 |
| Cast / Receive Shadows | 켬 | |

`ShinshiOcclusionFade` Custom Function 노드(`Shaders/OcclusionFade/ShinshiOcclusionFade.hlsl`)는 게임 카메라의 가림 투명화 범위 안에서 알파를 음수로 내려 알파 클립이 픽셀을 버리게 한다. 그림자 패스는 비우지 않는다. 재질에서 Alpha Clipping을 끄면 투명화도 꺼진다.

그래프를 고치면 Shader Graph 창에서 **Save Asset**을 눌러야 반영된다.

---

## 3. 재질 설정

### 꽃 · 잎 재질 (`M_CherryTree_Blossom` 기준)

| 항목 | 값 |
|---|---|
| Shader | `Shinshi/Environment/SG_VertexColorFoliage` |
| Surface Options > Alpha Clipping | 켬 |
| Surface Options > Render Face | Both |
| Base Map | `T_CherryTree_Blossom_BaseColor` |
| Tint | 흰색 |
| Vertex Color Strength | 1 |
| Alpha Cutoff | 0.5 |
| Smoothness | 0.2 |
| Enable GPU Instancing | 켬 (같은 나무를 여러 그루 둘 때) |

### 줄기 재질 (`M_CherryTree_Trunk`)

줄기 메시에는 버텍스 컬러가 없으므로 URP Lit을 그대로 쓴다. Base Map = `T_CherryTree_Trunk_BaseColor`, Metallic 0, Smoothness 0.15.

### 새 폴리지 재질을 만들 때

1. Project 창에서 `Create > Material`, 이름은 `M_{대상}_{부위}`.
2. Shader를 `Shinshi/Environment/SG_VertexColorFoliage`로 바꾼다.
3. Base Map에 알파가 있는 텍스처를 넣는다. 텍스처 임포트 설정은 4장.
4. Surface Options에서 Alpha Clipping이 켜져 있는지, Render Face가 Both인지 확인한다.
5. 메시에 버텍스 컬러가 없으면 Vertex Color Strength를 0으로 둔다. 버텍스 컬러가 없는 메시는 흰색으로 읽혀 결과는 같지만, 의도를 분명히 하기 위해서다.

---

## 4. 텍스처 임포트 설정

| 항목 | 꽃 아틀라스 | 줄기 베이스컬러 |
|---|---|---|
| Texture Type | Default | Default |
| sRGB (Color Texture) | 켬 | 켬 |
| Alpha Source | Input Texture Alpha | - |
| Alpha Is Transparency | 켬 | - |
| Generate Mip Maps | 켬 | 켬 |
| Mip Maps Preserve Coverage | **켬, Alpha Cutoff Value 0.5** | - |
| Wrap Mode | Clamp | Repeat (기본) |
| Max Size | 2048 | 4096 |

**Mip Maps Preserve Coverage를 끄면 멀리서 꽃이 점점 사라진다.** 밉맵이 작아질수록 알파가 평균화되어 컷오프 아래로 떨어지기 때문이다. 이 값은 재질의 Alpha Cutoff와 같게 맞춘다.

---

## 5. 모델 임포트 설정 (`SM_CherryTree.fbx`)

| 항목 | 값 | 이유 |
|---|---|---|
| Scale Factor / Convert Units | 1 / 켬 | Blender에서 Unity 축(Y-up)과 스케일을 맞춰 내보냈다 |
| Normals | **Import** | 꽃마다 둥글게 잡아 둔 법선이 입체감을 만든다. Calculate로 바꾸면 납작해진다 |
| Tangents | Calculate Mikktspace | |
| Rig > Animation Type | None | |
| Animation > Import Animation | 끔 | |
| Materials | Import via Material Description + 재질 리매핑 | FBX 재질 `CherryTree_Trunk`, `CherryTree_Blossom`을 2장의 재질로 연결 |
| Read/Write | 끔 | 메모리 절약 |

모델 높이는 약 0.87m다. 마을의 신목으로 쓰려면 배치할 때 스케일을 키운다 (예: 10배 → 약 8.7m).

---

## 6. 문제 해결

| 증상 | 원인과 대처 |
|---|---|
| 꽃이 안 보이거나 하늘색으로 보임 | 셰이더 변형이 아직 컴파일 중이다(비동기 컴파일). 잠시 기다리면 나타난다 |
| 그늘이 없고 납작함 | 재질이 URP Lit이거나 Vertex Color Strength가 0이다. 또는 모델 Normals가 Calculate로 바뀌었다 |
| 멀리서 꽃이 사라짐 | 텍스처의 Mip Maps Preserve Coverage가 꺼져 있다 (4장) |
| 꽃 테두리가 검게 보임 | 아틀라스의 투명 영역 색이 검정이다. 투명 영역까지 꽃 색을 번지게 한 텍스처를 쓴다 (현재 아틀라스는 처리됨) |
| 꽃 뒷면이 안 보임 | 재질의 Render Face가 Front다. Both로 바꾼다 |

---

## 7. 성능 메모

- 삼각형 수: 꽃 104,548 + 줄기 20,684 + 꽃잎 340 = 약 12.6만.
- 한 그루를 랜드마크로 두는 용도를 전제로 한다. 여러 그루를 두거나 멀리서도 보인다면 LOD(멀리서는 카드형 저폴리 버전)를 만들어야 한다. 이 문서의 `SM_CherryTree`는 LOD가 없다. 별도 변형 `CherryTreeSmall`·`CherryTreeBent`의 High/Medium/Low와 구분한다.
- 알파 클립은 오버드로와 그림자 패스 비용이 있다. 프레임이 문제가 되면 먼 LOD의 그림자부터 끈다.

---

## 8. 검증 기록

2026-10-01, Unity 6000.4.7f1 · URP 17.4 에디터에서 확인했다.

- 셰이더 그래프 임포트: 컴파일 오류 0건, 경고 0건.
- 모델 임포트: 메시 3개의 회전 0, 스케일 1. 꽃 · 꽃잎 메시에 버텍스 컬러가 있고, 재질 리매핑이 적용됐다.
- 격리된 프리뷰 렌더(씬 변경 없음): Vertex Color Strength를 0에서 1로 바꿨을 때 꽃 픽셀의 평균 색이 (0.736, 0.366, 0.468)에서 (0.666, 0.293, 0.388)로 어두워졌다. 그늘이 셰이더에 반영된다.
- 미검증: 실제 씬 배치, 플레이 모드, 빌드, 저사양 성능.
