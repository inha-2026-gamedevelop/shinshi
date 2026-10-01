# 공용 환경 에셋 사용 목록

> 2026-10-01 · 공용 제작 70종의 Blender 출력과 Unity 프리팹·정적 참조를 확인했다. 70종의 시각 검수 결과는 조건부 통과이며 식생과 목조 교각의 표면 보정이 남아 있다.

[모델 제작 목록](../design/model-production-list.md)의 NAT·ARC·PRP·MOD를 실제 배치 경로에 연결한다. Meshy 원본 가공21종, Blender 정밀 규격부품42종, 기존 바위·벚꽃 파생7종이다. 기존 중앙 거대 벚꽃나무의 형상·재질·프리팹은 보존했고, 텍스처 가져오기 설정만 메모리 최적화 범위에서 조정했다. 지역 LOC 조립물, EXT 후속 장식, 캐릭터·효과·맵 배치는 이 70종의 완료 범위에 포함하지 않는다.

## 경로·재질·LOD

표의 이름을 `{이름}`에 넣는다. 자연물·건축·모듈은 Environment, 독립 소품9종은 Props다.

| 내용 | 배치 경로 |
|---|---|
| 프리팹 | `Assets/02.Prefab/{분류}/{이름}/PF_{이름}.prefab` |
| 3LOD FBX | `Assets/01.Scripts/05.Art/{분류}/{이름}/Models/SM_{이름}_{High,Medium,Low}.fbx` |
| 일반 재질 | 같은 에셋의 `Materials/M_{이름}.mat` |
| 런타임 맵 | 같은 에셋의 `Textures/T_{이름}_{BaseColor,Normal,MetallicSmoothnessAO}.png`; 등·화로는 Emission 추가 |
| 기존 벚꽃 재질 | `Assets/01.Scripts/05.Art/Environment/CherryTree/Materials/M_CherryTree_{Blossom,Trunk}.mat` |

일반68종은 실제 AO·Normal·Roughness를 처리했다. URP Lit은 같은 선형 마스크의 R=Metallic/G=AO/A=Smoothness를 금속·Occlusion 입력에 공유한다. BaseColor/Emission은 sRGB, Normal/마스크는 선형이며 AO를 BaseColor에 다시 곱하지 않는다. 편집본 SourceUV는 남기고 FBX 첫 UV층에는 런타임 맵의 UV0만 내보낸다. 변경된 UV/기하에는 다시 베이크한다. 원본색의 모든 조명이 완전히 제거되었다는 보장은 하지 않으며 실제 근접 프리뷰로 과한 격자·얼룩·노멀을 정리했다.

새 Meshy 식생은 실제 OPAQUE 형상이며 잎이라는 이유로 알파를 발명하지 않았다. Cherry2종은 기존 알파·조정된 노멀·버텍스 컬러 AO를 사용한다. 기존 그래프에는 Normal/AO/Metallic 맵 입력이 없어 일반 PBR 맵을 추가하지 않고 맵 AO를 중복하지 않는다. [아트 기본 과정](ART_ASSET_PRODUCTION_WORKFLOW.md)과 [폴리지 셰이더](VERTEX_COLOR_FOLIAGE_SHADER.md)의 지원 범위를 따른다.

**규격42종과 TiledHouse는 High/Medium/Low가 같은 기하·UV·노멀이다.** 감면으로 생긴 계단·난간·아치·얇은 종이·집 벽의 파손을 막기 위한 현행 정책이다. 3단계 참조는 유지하지만 Medium/Low 삼각형 감면은 없다. 42개 모듈의 High 합은71,300tris이며 TiledHouse는 각59,999tris다. 향후 실제 배치 성능에 맞는 별도 보호 감면이 필요하다. 다른 대상도 표의 숫자만으로 LOD 전환·성능 검증을 완료했다고 판단하지 않는다.

## 자연물15종

| ID | 배치 프리팹 이름 | 제작 규격 높이(m) | High / Medium / Low tris | Collider |
|---|---|---:|---:|---|
| NAT-01 | [BoulderRound](../../Assets/02.Prefab/Environment/BoulderRound/PF_BoulderRound.prefab) | 0.968 | 26126 / 14369 / 7314 | Box |
| NAT-01 | [BoulderAngular](../../Assets/02.Prefab/Environment/BoulderAngular/PF_BoulderAngular.prefab) | 1.320 | 26126 / 14369 / 7314 | Box |
| NAT-01 | [BoulderLong](../../Assets/02.Prefab/Environment/BoulderLong/PF_BoulderLong.prefab) | 0.836 | 26126 / 14369 / 7314 | Box |
| NAT-02 | [CliffRockA](../../Assets/02.Prefab/Environment/CliffRockA/PF_CliffRockA.prefab) | 6.000 | 26126 / 14368 / 7314 | 없음 |
| NAT-02 | [CliffRockB](../../Assets/02.Prefab/Environment/CliffRockB/PF_CliffRockB.prefab) | 5.000 | 26126 / 14369 / 7314 | 없음 |
| NAT-03 | [PineStraight](../../Assets/02.Prefab/Environment/PineStraight/PF_PineStraight.prefab) | 8.000 | 50000 / 42500 / 35000 | 줄기 Capsule |
| NAT-03 | [PineBent](../../Assets/02.Prefab/Environment/PineBent/PF_PineBent.prefab) | 7.500 | 49999 / 42499 / 34999 | 줄기 Capsule |
| NAT-04 | [CherryTreeSmall](../../Assets/02.Prefab/Environment/CherryTreeSmall/PF_CherryTreeSmall.prefab) | 3.500 | 125572 / 111093 / 107990 | 줄기 Capsule |
| NAT-04 | [CherryTreeBent](../../Assets/02.Prefab/Environment/CherryTreeBent/PF_CherryTreeBent.prefab) | 6.000 | 125572 / 111093 / 107990 | 줄기 Capsule |
| NAT-05 | [MapleTree](../../Assets/02.Prefab/Environment/MapleTree/PF_MapleTree.prefab) | 6.000 | 36712 / 31205 / 25698 | 줄기 Capsule |
| NAT-06 | [DeadTree](../../Assets/02.Prefab/Environment/DeadTree/PF_DeadTree.prefab) | 5.000 | 24000 / 20400 / 16800 | 줄기 Capsule |
| NAT-07 | [ShrubLow](../../Assets/02.Prefab/Environment/ShrubLow/PF_ShrubLow.prefab) | 0.650 | 12000 / 10200 / 8400 | 없음 |
| NAT-07 | [ShrubTall](../../Assets/02.Prefab/Environment/ShrubTall/PF_ShrubTall.prefab) | 1.250 | 18000 / 15300 / 12600 | 없음 |
| NAT-08 | [HerbBroad](../../Assets/02.Prefab/Environment/HerbBroad/PF_HerbBroad.prefab) | 0.450 | 8500 / 7225 / 5950 | 없음 |
| NAT-08 | [HerbFlowers](../../Assets/02.Prefab/Environment/HerbFlowers/PF_HerbFlowers.prefab) | 0.550 | 10000 / 8500 / 7000 | 없음 |
바위5종은 기존 FloatingRock 파생이며 절벽A/B는 충돌 없는 배경 장식이다. PineBent는 원본의 돌·흙 받침을 포함하므로 주변 지형과 바닥 높이를 보고 배치한다. 고목의 빈 줄기·뿌리는 오류 구멍으로 일괄 메우지 않았다. 약초는 공용 형태이며 실제 채집 기능·노드 배치·식물 종 확정은 포함하지 않는다.

MapleTree는 붉은 바늘 같은 원본 수관을 넓은7갈래 잎의 불투명 기하로 교체했다. 원래 줄기·가지와 수정 전 편집본을 남겼고 초기High55,000tris와 새High36,712tris를 구분한다. CherrySmall/Bent는 기존 공용FBX의3.5/6m 별도 파생이며 중앙 나무를 대신하지 않는다. 잎 형상을 거의 유지하고 줄기만 감면하여 Low 비용도 크다. 굽힘 노멀은 측정된 재인코딩 한계 안에서 검사했으며 모든 방향의 수학적 정확 보존을 주장하지 않는다.

BoulderLong·CliffRockB의 파생 메시에서 발견한 정의되지 않은 코너 노멀은 해당 정점 주변의 제한된 sharp fan만 의도적으로 보수했다. 좌표·UV·형상과 수정 전 native 메시는 남겼다. 다른 바위와 사용자 원래 FloatingRock에는 적용하지 않았다.

## 건축4종

| ID | 배치 프리팹 이름 | 제작 규격 높이(m) | High / Medium / Low tris | Collider |
|---|---|---:|---:|---|
| ARC-01 | [TiledHouse](../../Assets/02.Prefab/Environment/TiledHouse/PF_TiledHouse.prefab) | 4.200 | 59999 / 59999 / 59999 | 외관 Box |
| ARC-02 | [ThatchedHouse](../../Assets/02.Prefab/Environment/ThatchedHouse/PF_ThatchedHouse.prefab) | 3.300 | 45000 / 38250 / 31497 | 외관 Box |
| ARC-03 | [Pavilion](../../Assets/02.Prefab/Environment/Pavilion/PF_Pavilion.prefab) | 6.058 | 58083 / 31944 / 16262 | 기둥4+바닥 Box |
| ARC-04 | [RedGate](../../Assets/02.Prefab/Environment/RedGate/PF_RedGate.prefab) | 4.600 | 40000 / 22000 / 11200 | 기둥 Box 2개 |

두 집의 외관 Box는 실내 출입용 충돌이 아니다. 진입 가능한 건물로 쓰려면 출입 공간·별도 충돌을 검증한다. RedGate는 기둥Box2개만 사용한다.

Pavilion은 Meshy 지붕·장식과 2m선착장 바닥4개·2.5m기둥4개를 실제로 재조립했다. 보행 바닥4×4m/Y=0.84m, 지붕 폭약4.4m, 전체높이약6.058m다. 바닥의 지지·묶음 부품은 보행면보다0.21m 돌출하여 실제 접촉·진입 계단 검증이 필요하다. 원래 융합 전체 모델과 분리 편집본은 별도로 남겼다.

정자의 두 등은 `PF_HangingLantern`의 별도 중첩 프리팹이며 독립LODGroup/스케일0.55/회전0을 유지한다. 정자 피벗 기준 Unity 위치는 `(-0.65,3.132540,-2.144052)`와 `(-0.65,3.150534,2.144052)`, 실제 사슬 상단Y는3.787039/3.805034m다. 등은4m보행 폭 밖의 처마에 두고 안쪽 주요 보행영역은 머리 여유2.5m를 기준으로 했다. 등 바닥의 바닥면 대비 높이는약2.293/2.311m이므로 안쪽으로 옮기면 같은 여유를 보장하지 않는다. 지붕·등을 옮기면 부착점/AO/LOD/충돌을 다시 확인한다. 재질Emission은 PointLight·실시간 조명 배치를 포함하지 않는다.

## 독립 소품9종

| ID | 배치 프리팹 이름 | 제작 규격 높이(m) | High / Medium / Low tris | Collider |
|---|---|---:|---:|---|
| PRP-02 | [HangingLantern](../../Assets/02.Prefab/Props/HangingLantern/PF_HangingLantern.prefab) | 1.190 | 23000 / 19550 / 16099 | 없음 |
| PRP-03 | [Brazier](../../Assets/02.Prefab/Props/Brazier/PF_Brazier.prefab) | 0.800 | 30000 / 25499 / 20999 | Box |
| PRP-04 | [StoneGuardian](../../Assets/02.Prefab/Props/StoneGuardian/PF_StoneGuardian.prefab) | 1.250 | 24000 / 20400 / 16799 | Box |
| PRP-05 | [OfferingTable](../../Assets/02.Prefab/Props/OfferingTable/PF_OfferingTable.prefab) | 0.983 | 24000 / 20400 / 16799 | Box |
| PRP-06 | [JarWide](../../Assets/02.Prefab/Props/JarWide/PF_JarWide.prefab) | 0.750 | 20606 / 17515 / 14424 | Box |
| PRP-06 | [JarTall](../../Assets/02.Prefab/Props/JarTall/PF_JarTall.prefab) | 1.000 | 24600 / 20909 / 17220 | Box |
| PRP-07 | [WoodCrate](../../Assets/02.Prefab/Props/WoodCrate/PF_WoodCrate.prefab) | 0.800 | 39999 / 33999 / 27998 | Box |
| PRP-08 | [WoodBarrel](../../Assets/02.Prefab/Props/WoodBarrel/PF_WoodBarrel.prefab) | 0.950 | 35000 / 29750 / 24500 | Box |
| PRP-09 | [WovenBasket](../../Assets/02.Prefab/Props/WovenBasket/PF_WovenBasket.prefab) | 0.650 | 51048 / 48495 / 43390 | 없음 |

JarWide/JarTall은 남던 검정 삼각·몸통 파손을 해결하려고 측정한 원형 단면으로 손상 측벽을 재구성했다. 보호한 뚜껑·손잡이·발의 위치와 SourceUV는 유지하고 원본 메시·페인트 재질은 편집 백업에 남겼다. **새 몸통의 페인트·손상 토폴로지는 유지하지 않고 점토 재질로 교체했다.** 등은 사슬6고리를 포함한 Collider 없는 장식이며 화로 발광과 별개인 불꽃 효과는 후속 대상이다.

## 규격부품42종

2m는 부품 조립 간격 제안, 기둥2.5m는 이 키트의 규격이며 맵 규모·게임 경계·NPC 위치를 확정하지 않는다. 루트 스케일1과 기존 국소 피벗을 유지한다. 표의 높이는 외형 높이로 피벗부터 보행면까지의 높이와 다를 수 있다.

| ID | 배치 프리팹 이름 | 제작 규격 높이(m) | High / Medium / Low tris | Collider |
|---|---|---:|---:|---|
| MOD-01 | [ModTimberPost](../../Assets/02.Prefab/Environment/ModTimberPost/PF_ModTimberPost.prefab) | 2.500 | 432 / 432 / 432 | Box |
| MOD-01 | [ModTimberBeam2m](../../Assets/02.Prefab/Environment/ModTimberBeam2m/PF_ModTimberBeam2m.prefab) | 0.260 | 444 / 444 / 444 | 없음 |
| MOD-01 | [ModWallPanel2m](../../Assets/02.Prefab/Environment/ModWallPanel2m/PF_ModWallPanel2m.prefab) | 2.400 | 756 / 756 / 756 | Box |
| MOD-01 | [ModDoorFrame2m](../../Assets/02.Prefab/Environment/ModDoorFrame2m/PF_ModDoorFrame2m.prefab) | 2.500 | 1296 / 1296 / 1296 | 없음 |
| MOD-01 | [ModDoorLeaf](../../Assets/02.Prefab/Environment/ModDoorLeaf/PF_ModDoorLeaf.prefab) | 1.980 | 1980 / 1980 / 1980 | 없음 |
| MOD-02 | [ModRoofTileSlope2m](../../Assets/02.Prefab/Environment/ModRoofTileSlope2m/PF_ModRoofTileSlope2m.prefab) | 1.265 | 3056 / 3056 / 3056 | 없음 |
| MOD-02 | [ModEave2m](../../Assets/02.Prefab/Environment/ModEave2m/PF_ModEave2m.prefab) | 0.400 | 1028 / 1028 / 1028 | 없음 |
| MOD-02 | [ModRidge2m](../../Assets/02.Prefab/Environment/ModRidge2m/PF_ModRidge2m.prefab) | 0.088 | 788 / 788 / 788 | 없음 |
| MOD-02 | [ModRidgeEnd](../../Assets/02.Prefab/Environment/ModRidgeEnd/PF_ModRidgeEnd.prefab) | 0.570 | 164 / 164 / 164 | 없음 |
| MOD-03 | [ModStoneWallStraight2m](../../Assets/02.Prefab/Environment/ModStoneWallStraight2m/PF_ModStoneWallStraight2m.prefab) | 1.300 | 2916 / 2916 / 2916 | Box |
| MOD-03 | [ModStoneWallCorner](../../Assets/02.Prefab/Environment/ModStoneWallCorner/PF_ModStoneWallCorner.prefab) | 1.430 | 6264 / 6264 / 6264 | Box |
| MOD-03 | [ModStoneWallEnd](../../Assets/02.Prefab/Environment/ModStoneWallEnd/PF_ModStoneWallEnd.prefab) | 1.430 | 864 / 864 / 864 | Box |
| MOD-03 | [ModPalisadeStraight2m](../../Assets/02.Prefab/Environment/ModPalisadeStraight2m/PF_ModPalisadeStraight2m.prefab) | 1.484 | 720 / 720 / 720 | 없음 |
| MOD-03 | [ModPalisadeCorner](../../Assets/02.Prefab/Environment/ModPalisadeCorner/PF_ModPalisadeCorner.prefab) | 1.580 | 1764 / 1764 / 1764 | 없음 |
| MOD-03 | [ModPalisadeEnd](../../Assets/02.Prefab/Environment/ModPalisadeEnd/PF_ModPalisadeEnd.prefab) | 1.580 | 432 / 432 / 432 | 없음 |
| MOD-04 | [ModCircularPavingCentre4m](../../Assets/02.Prefab/Environment/ModCircularPavingCentre4m/PF_ModCircularPavingCentre4m.prefab) | 0.180 | 1452 / 1452 / 1452 | 정적 High Mesh |
| MOD-04 | [ModCircularPavingRing30deg](../../Assets/02.Prefab/Environment/ModCircularPavingRing30deg/PF_ModCircularPavingRing30deg.prefab) | 0.180 | 400 / 400 / 400 | 정적 High Mesh |
| MOD-04 | [ModPavingEdge2m](../../Assets/02.Prefab/Environment/ModPavingEdge2m/PF_ModPavingEdge2m.prefab) | 0.220 | 648 / 648 / 648 | 정적 High Mesh |
| MOD-05 | [ModStairNarrow](../../Assets/02.Prefab/Environment/ModStairNarrow/PF_ModStairNarrow.prefab) | 0.900 | 2968 / 2968 / 2968 | 정적 High Mesh |
| MOD-05 | [ModStairWide](../../Assets/02.Prefab/Environment/ModStairWide/PF_ModStairWide.prefab) | 0.900 | 5356 / 5356 / 5356 | 정적 High Mesh |
| MOD-05 | [ModStairStart2m](../../Assets/02.Prefab/Environment/ModStairStart2m/PF_ModStairStart2m.prefab) | 0.150 | 540 / 540 / 540 | 정적 High Mesh |
| MOD-05 | [ModStairEnd2m](../../Assets/02.Prefab/Environment/ModStairEnd2m/PF_ModStairEnd2m.prefab) | 0.150 | 540 / 540 / 540 | 정적 High Mesh |
| MOD-06 | [ModStoneRailStraight2m](../../Assets/02.Prefab/Environment/ModStoneRailStraight2m/PF_ModStoneRailStraight2m.prefab) | 1.060 | 648 / 648 / 648 | Box |
| MOD-06 | [ModStoneRailCorner](../../Assets/02.Prefab/Environment/ModStoneRailCorner/PF_ModStoneRailCorner.prefab) | 1.180 | 2160 / 2160 / 2160 | Box |
| MOD-06 | [ModStoneRailEnd](../../Assets/02.Prefab/Environment/ModStoneRailEnd/PF_ModStoneRailEnd.prefab) | 1.180 | 864 / 864 / 864 | Box |
| MOD-06 | [ModTimberRailStraight2m](../../Assets/02.Prefab/Environment/ModTimberRailStraight2m/PF_ModTimberRailStraight2m.prefab) | 1.100 | 936 / 936 / 936 | Box |
| MOD-06 | [ModTimberRailCorner](../../Assets/02.Prefab/Environment/ModTimberRailCorner/PF_ModTimberRailCorner.prefab) | 1.100 | 1440 / 1440 / 1440 | Box |
| MOD-06 | [ModTimberRailEnd](../../Assets/02.Prefab/Environment/ModTimberRailEnd/PF_ModTimberRailEnd.prefab) | 1.120 | 432 / 432 / 432 | Box |
| MOD-07 | [ModTimberBridgeDeck2m](../../Assets/02.Prefab/Environment/ModTimberBridgeDeck2m/PF_ModTimberBridgeDeck2m.prefab) | 0.489 | 4440 / 4440 / 4440 | 정적 High Mesh |
| MOD-07 | [ModTimberBridgeRail2m](../../Assets/02.Prefab/Environment/ModTimberBridgeRail2m/PF_ModTimberBridgeRail2m.prefab) | 1.480 | 3132 / 3132 / 3132 | Box |
| MOD-07 | [ModTimberBridgePier](../../Assets/02.Prefab/Environment/ModTimberBridgePier/PF_ModTimberBridgePier.prefab) | 1.200 | 1188 / 1188 / 1188 | Box |
| MOD-07 | [ModStoneArchBridgeDeck2m](../../Assets/02.Prefab/Environment/ModStoneArchBridgeDeck2m/PF_ModStoneArchBridgeDeck2m.prefab) | 0.540 | 480 / 480 / 480 | 정적 High Mesh |
| MOD-07 | [ModStoneArchBridgeRail2m](../../Assets/02.Prefab/Environment/ModStoneArchBridgeRail2m/PF_ModStoneArchBridgeRail2m.prefab) | 1.265 | 1728 / 1728 / 1728 | Box |
| MOD-07 | [ModStoneArchBridgePier](../../Assets/02.Prefab/Environment/ModStoneArchBridgePier/PF_ModStoneArchBridgePier.prefab) | 1.120 | 1512 / 1512 / 1512 | Box |
| MOD-08 | [ModHangingBanner](../../Assets/02.Prefab/Environment/ModHangingBanner/PF_ModHangingBanner.prefab) | 1.803 | 1180 / 1180 / 1180 | 없음 |
| MOD-08 | [ModSuspendedLongCloth4m](../../Assets/02.Prefab/Environment/ModSuspendedLongCloth4m/PF_ModSuspendedLongCloth4m.prefab) | 0.801 | 1796 / 1796 / 1796 | 없음 |
| MOD-08 | [ModTalismanPaper](../../Assets/02.Prefab/Environment/ModTalismanPaper/PF_ModTalismanPaper.prefab) | 0.490 | 216 / 216 / 216 | 없음 |
| MOD-09 | [ModFlagpole](../../Assets/02.Prefab/Environment/ModFlagpole/PF_ModFlagpole.prefab) | 2.750 | 568 / 568 / 568 | 없음 |
| MOD-09 | [ModLanternHanger](../../Assets/02.Prefab/Environment/ModLanternHanger/PF_ModLanternHanger.prefab) | 2.450 | 1176 / 1176 / 1176 | 없음 |
| MOD-10 | [ModBlueAwning2m](../../Assets/02.Prefab/Environment/ModBlueAwning2m/PF_ModBlueAwning2m.prefab) | 2.466 | 4428 / 4428 / 4428 | 없음 |
| MOD-10 | [ModMarketStall2m](../../Assets/02.Prefab/Environment/ModMarketStall2m/PF_ModMarketStall2m.prefab) | 1.042 | 3348 / 3348 / 3348 | 없음 |
| MOD-10 | [ModDockFloor2m](../../Assets/02.Prefab/Environment/ModDockFloor2m/PF_ModDockFloor2m.prefab) | 1.050 | 4820 / 4820 / 4820 | 정적 High Mesh |

- 원형 바닥·30도 링은 원 중심 피벗/보행면Y=0을 사용한다. 링12개는 외경8m 키트 조립 예이며 실제 광장·전장 크기 결정이 아니다.
- 계단은0.15m 단높이6개/총0.9m/진행1.8m, 시작·끝은 별도0.15m 부품이다. 계단의 실제 캐릭터 접촉·보행은 별도 확인한다.
- 선착장 지지 바닥 피벗Y=0과 판자 보행면Y=0.84m를 구분한다. 원형·다리·문짝·천의 비중심 피벗을 AABB중심으로 다시 잡지 않는다.
- 문틀·문짝·기와·천·목책은 현행 Collider 없음이다. 벽·난간Box는 단일 부품의 근사형태이며 닫힌 이동 경계나 자동문 기능은 구현하지 않았다.
- 정적 High Mesh 충돌은 계획에 명시한 원본렌더메시 사용이다. 별도 단순 충돌판이나 보행 적합성을 검증했다는 뜻이 아니다.
- 깃발·부적은 승인된 문자·문양이 없는 공용 바탕이다. 차양·가판대·등걸이는 장식이며 시설 상호작용을 구현하지 않는다.

## 런타임 텍스처·검증 범위

[석등](../../Assets/02.Prefab/Props/StoneLantern/PF_StoneLantern.prefab), [6m반복 바닥](../../Assets/02.Prefab/Environment/StoneCourtyard/PF_StoneCourtyardTile.prefab), [부유석](../../Assets/02.Prefab/Environment/FloatingRock/PF_FloatingRock.prefab)은 신규70수에 더하지 않는다. 중앙 거대 벚꽃은 사용자 기존 원본을 사용한다.

| 주장 | 실제 확인 범위 | 상태 |
|---|---|---|
| 제작70·베이크·3LOD 출력 | rendered 보고서70개, 저장 편집본, FBX210개와 실제 베이크 파일 | 출력 확인 |
| 규격모듈 신고결함 | 앞·뒤3LOD252장 직접 검수와 WallPanel6장 후속검수에서 P1 8건/P2접합면 해소 | 해당 이미지 범위 확인 |
| 항아리 몸통 | 최종 앞·뒤3LOD12장 직접 검수에서 검정삼각/격자/넓은 각진 얼룩 해소 | 해당 이미지 범위 확인 |
| Nature8 원본 방향·UV증거 | 동일SHA 재import와 두원본단계 SHA일치·finite/nonzero/unit corner normal·현재High 별도검사 | actual Blender 확인 |
| Unity 프리팹·정적 참조 | 70/70 실제 검증 통과; 모델·재질·UV0·노멀/탄젠트·LOD·GUID·용도별 Collider 참조 확인 | 정적 확인 |
| Unity 중립 이미지 | 앞·뒤3LOD와 일반68종 AO 켬/끔, 총556장 생성·검수; 약초·관목4종과 목조 교각의 표면 보정은 후속 작업 | 조건부 통과 |
| 게임사용 | 이동·접촉·연속자동LOD·게임조명/카메라·다수배치성능·PlayerBuild/OOMstress 미실행 | 미검증 |

현재 70개 프리팹의 중복 없는 텍스처 참조208개 중 신규 소유206개를 에디터에서 읽었다. Unity native loaded memory 합은 신규116.48MiB, 기존 Cherry 텍스처2개를 포함하면119.15MiB다. 신규 CPU 읽기 복사와 밉맵 있는 비스트리밍 텍스처는 각각0개이며 BC7/BC5 임포트를 사용한다. 이는 현재 에디터에 로드된 Texture 메모리 측정으로 전용 GPU VRAM, 최대 임포트 사용량, Player 빌드나 마을 다수 배치 성능을 증명하지 않는다.

중립 PreviewScene의 수동LOD·Collider ray는 게임사용 검증을 대신하지 않는다. [씬·에셋 안전](SCENE_AND_ASSET_SAFETY.md)과 [검증 플레이북](../ai/03_VERIFICATION_PLAYBOOK.md)을 따른다. 개별렌더·프롬프트·원본·상세기록은 Git제외 개인작업 위치에 두며 이 공용문서에는 경로·사용조건·검증범위만 남긴다.
