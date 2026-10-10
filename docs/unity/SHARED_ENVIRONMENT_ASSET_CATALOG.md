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

| 확인 범위 | 상태 |
|---|---|
| 70종 제작 출력과 Unity 프리팹의 모델·재질·UV·노멀·LOD·GUID·용도별 Collider 참조 | 정적 확인 |
| 중립 조명·강제 정지 LOD 이미지 | 조건부 통과. 약초·관목4종과 목조 교각의 표면 보정 필요 |
| 이동·접촉·연속 LOD·게임 조명/카메라·다수 배치 성능·Player 빌드/OOM 스트레스 | 미검증 |

신규 텍스처는 BC7/BC5 임포트를 사용하며 CPU 읽기 복사를 끄고 밉맵 있는 텍스처에 스트리밍을 적용했다. 에디터 확인은 GPU VRAM·Player 메모리·다수 배치 성능 예산의 통과를 뜻하지 않는다.

중립 PreviewScene의 수동LOD·Collider ray는 게임사용 검증을 대신하지 않는다. [씬·에셋 안전](SCENE_AND_ASSET_SAFETY.md)과 [검증 플레이북](../ai/03_VERIFICATION_PLAYBOOK.md)을 따른다. 개별렌더·프롬프트·원본·상세기록은 Git제외 개인작업 위치에 두며 이 공용문서에는 경로·사용조건·검증범위만 남긴다.

## 마을 쿼터뷰 배치 씬

[Dev_ShinshiVillageQuarterView](../../Assets/00.Scenes/Dev_ShinshiVillageQuarterView.unity)는 기존 환경 프리팹과 native 지형 청크를 배치한 별도 개발 씬이다. 중앙 성소·전각·공방·주거·시장·약초밭·정자·위성 문섬·다리를 구분한다. 구름·폭포·연못 수면 효과는 사용자 요청에 따라 제외했다.

- 전체 검수 카메라 `QuarterView_Overview`는 orthographic74, 확대 검수용 `QuarterView_Gameplay`는 orthographic21이며 둘 다 Euler(42,-36,0)이다. 후자는 비활성이고 추적 로직은 없다.
- [VillageWatchtower](../../Assets/02.Prefab/Environment/VillageAssembly/PF_VillageWatchtower.prefab)는 기존 모듈을 재조합한 망루다. 모듈의 local AO를 재사용하며 조립 접촉 AO/GI는 새로 베이크하지 않았다. 원본 모듈의 용도별 충돌을 유지한다.
- 씬은 기존 바닥·벚꽃 재질을 복제한 `Assets/01.Scripts/05.Art/Environment/VillageAssembly/Materials/`의 `M_VillageAssembly_PaleStone.mat`·`M_VillageAssembly_Blossom.mat`를 인스턴스에 적용한다. 원본 재질은 유지한다.
- 씬 인스턴스에서 활성 LOD의 마지막 cull을0.001로 조정했다. 망루의 비활성 자식 LOD는 aggregate 루트 LOD가 대신 제어한다. 이 조정은 다수 배치 성능 검증이 아니다.
- 최초 연결길은 `ModPavingEdge2m`1203개와 연결 계단12개·석축22개로 구성했다. 아래 원화 대조 후속 작업에서 길 메시와 지형 높이·착지부를 교체했다. 최초34접점/최대7.6cm 검증은 교체 전 이력이며 현재 높이 연결의 통과 근거로 재사용하지 않는다.
- 씬 인스턴스의 누락 프리팹·메시·재질 슬롯·스크립트·LOD renderer 검증과 전체/확대 쿼터뷰 카메라 렌더를 수행했다. 현재 후속 검증 범위와 남은 항목은 아래를 따른다. 임시 지형 표본 Collider는 제거했다.
- 전체 보행·게임 통합·PlayerBuild·성능·유사도90%는 미검증이다. 상세 배치/렌더/검사 원료는 Git 제외 개인 제작 기록에 둔다.

## 원화 대조 마을 — 2026-10-01~02

CA04의 이중 지붕·붉은 목재·기와·회벽·초가·시장 차양을 별도 모델로 제작하고 위 개발 씬에 배치했다. 개별 물체 참조→Meshy8건→실제 Blender5.1 가공→Unity 재질·Collider·프리팹 순서이며 출처·사용 조건은 [에셋 대장](../licenses/ASSET_REGISTRY.md)을 따른다. 원본 모델·기존 프리팹·다른 개발 씬은 보존한다.

| 배치 프리팹 | 기본 크기 X×Y×Z(m) | High / Medium / Low tris | 충돌 사용 조건 |
|---|---|---:|---|
| [ReferenceGrandHall](../../Assets/02.Prefab/Environment/ReferenceGrandHall/PF_ReferenceGrandHall.prefab) | 17.998×12.989×13.986 | 99998 / 99998 / 99998 | 외부 건물 몸체 근사Box; 내부 이동 기능 없음 |
| [ReferencePavilion](../../Assets/02.Prefab/Environment/ReferencePavilion/PF_ReferencePavilion.prefab) | 5.003×5.499×5.003 | 80000 / 80000 / 80000 | 기둥4Box·바닥Box; 실제 바닥 높이 약0.529m |
| [ReferenceCeremonialGate](../../Assets/02.Prefab/Environment/ReferenceCeremonialGate/PF_ReferenceCeremonialGate.prefab) | 7.002×5.998×2.001 | 80000 / 80000 / 80000 | 기둥X±2.035m의2Box, 가운데 통로 개방 |
| [ReferenceWorkshop](../../Assets/02.Prefab/Environment/ReferenceWorkshop/PF_ReferenceWorkshop.prefab) | 7.5×5×5.5 | 195000 / 195000 / 195000 | 외부 건물 몸체 근사Box |
| [ReferenceTiledResidence](../../Assets/02.Prefab/Environment/ReferenceTiledResidence/PF_ReferenceTiledResidence.prefab) | 6×4.5×4.5 | 194999 / 194999 / 194999 | 외부 건물 몸체 근사Box |
| [ReferenceThatchedCottage](../../Assets/02.Prefab/Environment/ReferenceThatchedCottage/PF_ReferenceThatchedCottage.prefab) | 4.497×3.5×3.8 | 194999 / 194999 / 194999 | 외부 건물 몸체 근사Box |
| [ReferenceMarketCanopy](../../Assets/02.Prefab/Environment/ReferenceMarketCanopy/PF_ReferenceMarketCanopy.prefab) | 4×3×3 | 195000 / 195000 / 195000 | 기둥4Box, 판매대 포함; 시설 상호작용 없음 |
| [ReferenceStonePath](../../Assets/02.Prefab/Environment/ReferenceStonePath/PF_ReferenceStonePath.prefab) | High5.008×0.268×1.806 | 12000 / 6000 / 2206 | 표면 근사Box, 루트Y=0 부근이 보행면 |
| [ReferenceShrineDetails](../../Assets/02.Prefab/Environment/ReferenceShrineDetails/PF_ReferenceShrineDetails.prefab) | 7.958×3.721×7.843 | 16000 / 16000 / 16000 | 돌 링11Box·앞 출입구; 천은 장식 |

- 생활 건물4종의 Unity native 모델 정면은+Z로 확인해 프리팹의 LOD 자식에Y180도를 적용했다. 루트 피벗과 충돌 기준을 보존한 정면−Z 프리팹이며 각 모델의 축·치수는 실제 Unity 임포트로 확인했다.
- 건물7종·성소는 축소 후보에서 지붕·열린 부분·천이 파손돼 최종 High를3LOD에 유지했다. 이들은 삼각형 감면이 없다. 길 Low는 연결된 별도 토폴로지와 전용1K BaseColor/Normal/AO·Smoothness 맵을 사용한다. 나머지 새 건물·길 High/Medium은2K, 성소는1K이며 URP Lit에 실제 AO의G·Metal의R·Smoothness의A를 연결했다.
- `ReferenceIsland/Chunks/PF_ReferenceIsland_{Core,NE,NW,W,SW,SE,E}`7종은 기존 FloatingRock의 닫힌 고밀도 원본을 연결감면해 가공한 native 청크다. UV분리 메시의 독립감면으로 생겼던 삼각형 틈을 실제 토폴로지에서 수리했다. 최고점0~−0.06m의 얕은 윗면 두께를 유지하며 별도cap은 없다. 각 High60000/Medium30000tris, Low는 Core21302/NE21966/NW22438/W21564/SW22864/SE22236/E23172tris다. 원본 atlas의 per-corner UV를 감면 중 보간하며 원본 loop 배열·polygon 순서의 완전 동일성을 주장하지 않는다. CoreBase2K 공유·청크별 실제 AO1K/Normal2K 재베이크·High 상부 정확 subset 충돌 메시를 사용한다. Low 근접 faceting/국소 질감 늘어짐은 먼 거리 사용 조건이다. 원본은 보존했고 원본 색에서 기존 조명/AO 제거가 완전히 증명된 것은 아니다.
- [ReferenceSanctuaryLantern](../../Assets/02.Prefab/Environment/VillageAssembly/PF_ReferenceSanctuaryLantern.prefab)은 기존 등걸이·처마·매다는 등을 조립했다. 원본의 실제 local AO를 재사용하며, 새 조립 접촉AO/GI는 베이크하지 않았다.
- 현재 연결길은 불규칙 석판226개/27개 경로·착지 그룹이다. 별도 높이 연결 계단20개와 정자 출입계단을 추가하고 전각의 계단 진행을12m→9m로 줄여 상부 착지부를 넓혔다. 길·계단·다리·선착장 표본 검증은 전체 캐릭터 보행이나 가파른 연결부의 이동 적합성을 증명하지 않는다.
- 건물 마당·문섬의 반복 원형무늬 StoneCourtyard97인스턴스를 불규칙 석판275줄로 교체했다. 기존 외곽 크기와 Collider 상단높이를 유지했고 중앙성소의 원형포장·약초밭4기단은 유지했다. 각마당3표본 총291ray에서 누락0·높이차이최대약6e−8m다. 이 표본은 전체 보행·모든모서리 연속성을 증명하지 않는다. 연결길과 합쳐 석판501개이며 원본 프리팹은 보존했다.
- 정자를(30,1.05,20)에 배치하고 접근 길을 수정했다. 중앙 꽃 수관만 수평1.22배로 넓혔으며 줄기·성소 피벗은 유지한다. 벚꽃 수관·흰 꽃 변형은 기존 지원 셰이더·버텍스AO를 보존한다. 소나무·관목과 추가한 HerbBroad/HerbFlowers/ShrubLow445개·114식생 주머니는 기존 URP Lit의 실제 local AO/Normal을 유지하며, 조립 접촉AO나 전체맵GI 재베이크 결과가 아니다. 구름·수면·폭포 효과는 추가하지 않았다.
- 건물 외부 소품7개는 기존 WoodCrate/WoodBarrel/WovenBasket 프리팹을 재사용하며 기존 local AO/Normal과 충돌을 유지한다. 시장의 기존 Basket/Barrel/Jar15개는 지형·StoneCourtyard 상부 Collider 기준으로 높이를 다시 맞췄다. 건물·연결길·다른 소품과 footprint가 겹치는 후보는 제외했다. 새 조립접촉AO는 베이크하지 않았다.
- 미세 재질·기와의 faceting, 성소 돌의 작은 검은 대시·천 닫힘 이음은 잔여 검수 항목이다. 정지 LOD·전체/확대 쿼터뷰·메타/참조 검증과 실제 플레이·연속LOD·성능·빌드·원화 유사도 수치 판정을 구분한다. 최종 검사 원료는 Git 제외 개인 제작 기록에 보존한다.
- 전각 아래처마의 밝은띠·대문의 흰기둥은 실제 Blender 표면색 보정으로 각각 기와색·붉은목재색에 맞췄다. BaseColor2장만 바뀌고 회벽·석기단·천·금색장식 및 형상·UV·AO·Normal·Mask·FBX는 보존했다. 두맵을 실제 Unity2K/BC7/sRGB로 재임포트하고 앞뒤·확대 렌더를 확인했다. 작은 기와끝 밝은패치와 문기둥-돌받침 경계는 잔여 항목이다.
- 최종 저장 씬의 정적 검사는9154오브젝트/1852외부프리팹/1963LODGroup/964Collider에서 누락Script·Prefab·Mesh·Material·LOD renderer0, 잘못된bounds0, 파티클0·임시 지형검사용Collider0이다. 신규49Texture의 에디터 native loaded memory 합은441,190,962bytes(420.75MiB), readable0·streaming0이다. 이 수치는 전체씬 GPU VRAM·Player peak·다수배치 성능이 아니다. 건물7종·성소는 High 복제LOD 예외이므로 감면 성능을 주장하지 않는다.

## CA-07 추가 에셋 — 2026-10-03

CA-07 원화에 있지만 기존 Assets에 없던 4종이다. 단일 물체 시안 → Meshy 생성 1회씩 → Blender 5.1 가공 → Unity 재질·LOD·충돌·프리팹 순서로 만들었다. 출처·사용 조건은 [에셋 대장](../licenses/ASSET_REGISTRY.md#ca-07-추가-환경-모델--2026-10-03)을 따른다. 네 프리팹 모두 루트 스케일 1, LOD 3단계, 정면 −Z 기준이다.

| 배치 프리팹 | 크기 X×Y×Z(m) | 피벗 | High / Medium / Low tris | 충돌 사용 조건 |
|---|---|---|---:|---|
| [Waterwheel](../../Assets/02.Prefab/Environment/Waterwheel/PF_Waterwheel.prefab) | 4.60×4.81×4.27 | 바닥 중앙 | 59999 / 29999 / 11999 | 양쪽 받침틀 근사 Box 2개(Z ±1.44m). 바퀴·축은 충돌 없음, 회전 없음 |
| [WheatFieldPlot](../../Assets/02.Prefab/Environment/WheatFieldPlot/PF_WheatFieldPlot.prefab) | 6.00×1.29×4.10(흙 둔덕 높이 0.27) | 바닥 중앙 | 9800 / 9080 / 8540 | 없음. 장식 밭이며 이동 차단 여부는 배치에서 별도 결정 |
| [LotusCluster](../../Assets/02.Prefab/Environment/LotusCluster/PF_LotusCluster.prefab) | 2.50×0.44×2.53(수면 위 최고 0.38) | 수면 높이 중앙(Y=0) | 23999 / 11999 / 5279 | 없음. 수면 장식 |
| [ArchedTimberBridge](../../Assets/02.Prefab/Environment/ArchedTimberBridge/PF_ArchedTimberBridge.prefab) | 10.00×2.48×2.61 | 경간 중앙, Y=0이 양 끝 데크 착지면 | 40000 / 20000 / 8000 | 보행 데크 MeshCollider(`COL_ArchedTimberBridge_Deck`, 폭 2.1m 띠, 원본 렌더 메시가 아님)와 양쪽 난간 Box 10개씩 |

- 재질은 물레방아·다리가 `Lit Occlusion Fade`, 연꽃과 밀밭 흙이 URP Lit, 밀 카드가 `SG_VertexColorFoliage`(알파 클립 0.5, 양면, 버텍스 AO 강도 0.6)다. Lit 계열은 `T_*_Mask`의 R=Metallic, G=실제 AO, A=1−Roughness를 쓰며 금속 값은 모두 0이다. 다리 데크도 기존 목조 다리 모듈처럼 투명화 셰이더를 쓴다.
- AO는 모두 Cycles에서 최종 High 형상으로 실제 베이크했다. 거리는 물레방아 0.5m, 다리 0.45m, 연꽃 0.15m, 밀밭 흙 0.35m(밀 카드 포함)이고, 밀 카드는 버텍스 AO 0.25m다. BaseColor·Normal·거칠기는 Meshy 원본에서 감면 메시로 전사했고, 빈 UV 영역과 광선 누락 텍셀은 주변 색으로 채웠다.
- 밀밭의 Meshy 원본 줄기는 감면하면 사라져 원본 줄기를 렌더한 2K 알파 아틀라스와 세로 카드(300/180/90장)로 바꿨다. 흙 둔덕은 원본에 구멍이 있어 높이장으로 다시 만들었고 세 LOD가 같은 흙 메시를 쓴다. 따라서 LOD 감면 효과는 카드 수와 overdraw에만 있다.
- 다리 데크 중심선의 최대 경사는 약 29~30도다. 아치를 키우면서 다리 아래 기둥 두 쌍이 데크와 함께 올라가 하단이 지면에 닿지 않을 수 있으므로 배치할 때 높이를 확인한다. 연꽃은 줄기 하단이 수면 아래 0.065m까지 내려간다.
- Unity 확인 범위: 실제 임포트 치수, 루트 스케일, 재질·텍스처 참조 누락 0, LOD 3단계 참조, 텍스처 임포트(BaseColor sRGB BC7, Normal BC5, Mask·Normal 선형, CPU 읽기 끔), 저장하지 않는 PreviewScene의 중립 조명 앞·뒤·사선·쿼터뷰 42도·근거리·LOD별·AO 켬/끔 렌더, 신규 Console 오류·경고 0이다.
- 미검증: 실제 씬 배치·게임 카메라·조명, 캐릭터 보행(다리 데크 경사·난간 접촉), 연속 LOD 전환, 다수 배치 성능, Player 빌드. 원화 유사도는 수치로 측정하지 않았다. 근거리에서 밀 이삭은 뭉툭한 형태와 일부 어두운 얼룩이 보이고, 물레방아·다리 원본 색에는 작은 밝은 얼룩이 남아 있다.

## 던전 에셋·조립 프리팹 — 2026-10-07

던전용 신규 파일을 다음 경로에 추가했다. 파일 존재·직렬화 참조를 확인한 상태이며, 실제 전장에 배치하거나 게임 동작을 검증했다는 뜻은 아니다.

| 배치 프리팹 경로 (`Assets/02.Prefab/` 기준) | 수량 | 모델·재질·텍스처 경로 (`Assets/01.Scripts/05.Art/` 기준) |
|---|---:|---|
| `Environment/DungeonAssemblies/` | 11 | `Environment/DungeonAssemblies/` 및 조립 대상 기존 에셋 |
| `Environment/DungeonReuseOptimized/` | 35 | `Environment/DungeonReuseOptimized/` 및 기존 공용 재질 |
| `Environment/DungeonTripo/` | 30 | `Environment/DungeonTripo/` |
| `Props/DungeonTripo/` | 6 | `Props/DungeonTripo/` |

- 총 82개 프리팹이며 조립 프리팹 6개는 `Environment/DungeonAssemblies/Layouts/`에 있다. 숲 입구·바닥, 고대 비석 제단, 월백 발판·소형 사당·6기둥 제안을 포함한다. `Proposal`은 제안 배치이며 보스 기믹이나 최종 배치 확정으로 해석하지 않는다.
- 이번 커밋 준비의 L1 검사: 신규 에셋·메타 715개 파일, FBX 82개, PNG 71개, 재질 37개, `.asset` 6개, 프리팹 82개. 메타 누락·신규 GUID 중복·외부 GUID 참조 누락·Git 제외 에셋 의존은 0건이다. GUID 존재 검사는 임포트된 메시의 하위 fileID 유효성이나 시각 품질을 보증하지 않는다.
- 프리팹 파일 76개에 LODGroup, 31개에 Collider가 직접 직렬화되어 있다. 중첩 프리팹까지 합산한 충돌체 수나 실제 보행 가능 여부를 뜻하지 않는다. 배치 시 지면 높이·충돌·LOD 전환과 참조를 Unity에서 확인한다.
- 이번 검토에서 Unity 임포트·화면·AO/PBR 품질·실제 보행·연속 LOD·다수 배치 성능·Player 빌드는 미검증이다. 제작 상세 기록과 렌더는 Git 제외 개인 기록에 보존한다.

## 숲 던전 전장 씬 — 2026-10-08

> 아래는 진행형 6구역 결정 이전의 단일 전장 제작 상태다. 현재 기획의 28m×28m 6구역·연결 통로·8방향 출현 후보는 이 씬에 미반영이다. 일반전 부활은 사망 위치를 사용하므로 아래 복귀 지점 4개를 새 기획의 부활 위치로 사용하지 않는다. 씬·에셋은 이번 기획 작업에서 변경하지 않았다.

[DungeonForest](../../Assets/00.Scenes/Release/DungeonForest.unity)는 [CA-02 숲](../design/concept-art.md#4-ca-02-숲-맵)을 참고해 [숲 일반전](../design/combat-design.md#숲-일반전)의 32m×32m 전장을 위 던전 프리팹으로 배치한 씬이다. 위쪽(+Z)이 북쪽이다.

- 전장: 원점 중심 `PF_DA_ForestGround32m`, 경계 바로 바깥의 보이지 않는 BoxCollider 벽 4개(`02_CombatBoundary_32m`), 안쪽만 굽은 NavMesh(`DungeonForest/NavMesh-Navigation.asset`, 물리 충돌체 기준). 전장 안에는 충돌 장애물을 두지 않았다.
- 게임플레이 지점(`90_Gameplay`): `PF_GameCamera`, 플레이어 시작점 4개(`NetworkStartPosition`, 중앙 남쪽), 복귀 지점 4개(중심에서 ±6m), 적 출현 지점 4개(네 변 중앙, 중심에서 15m). 출현 방향마다 바깥 통로·화로 2개·깃대로 표시했다. 복귀 지점과 출현 지점 사이 거리는 약 10.8m 이상이다.
- 주변(전장 밖, 연출용): 남쪽 입구 문·모닥불, 북쪽 폐허 한옥 3채·잔해·무너진 돌담, 북동 비석 제단·붉은 문, 먼 북동 흰 고목·청록 돌기둥, 서쪽 계류·아치 다리 2개·폭포, 소나무·단풍·고목 150그루, 외곽 절벽 테두리, 원경 산·안개. 카메라 쪽(남쪽) 큰 나무는 전장에서 떨어뜨렸다.
- 씬 전용 에셋은 `Assets/01.Scripts/05.Art/Environment/DungeonForestAtmosphere/`(숲 바닥 잔디 재질, 얕은 계류 재질, 밤 하늘, 볼륨 프로필)에 있고 원본 재질은 바꾸지 않았다. 계류 재질은 평지 위 얕은 물이라 기슭 거품 거리를 줄인 변형이다.
- 확인 범위: 저장 씬의 누락 프리팹·재질·스크립트 0, NavMesh 범위 ±15.3m, 피치55·FOV30 카메라 렌더 검수. 미검증: 플레이 모드·실제 플레이어 보행·적 출현, 네트워크 연결(던전 서버 역할에서 이 씬으로 전환하는 코드는 아직 없음), 성능·LOD 전환·Player 빌드, 화로·횃불 불꽃 파티클의 실제 표시. 전장 바닥 텍스처는 기존 32m 조립물 그대로라 근거리에서 흐리다.
- 원화의 숲→폐허→제단→끝 광장 동선은 배경 배치일 뿐 이동형 던전이나 진행 순서를 확정하지 않는다.

## CA-07 마을 배치 씬 — 2026-10-03

[Dev_ShinshiVillageCA07](../../Assets/00.Scenes/Dev_ShinshiVillageCA07.unity)은 [CA-07 배치도](../design/concept-art.md#8-ca-07-신시-마을-배치도-2)를 게임 쿼터뷰 기준으로 배치한 별도 개발 씬이다. CA-04 기반 `Dev_ShinshiVillageQuarterView`는 그대로 둔다.

- 기준 카메라는 [CameraConfig](../../Assets/09.Data/Combat/CameraConfig.asset) 기본 단계(원근 FOV30, 피치55, 요0, 거리14.5)다. 원화 위쪽을 북쪽(+Z)으로 보고, 피치55에서 원화와 같은 화면 비율이 되도록 깊이 축을 맞췄다. 씬의 `QuarterView_Gameplay`는 같은 값의 원근 카메라, `QuarterView_Overview`·`CA07_CompareCamera`는 피치55·요0 직교 검수 카메라다.
- 섬 몸체는 기존 ReferenceIsland 청크를 재사용하고, 위에 원화 윤곽대로 만든 단 11개(잔디·판석·바위 절벽 테두리)를 높이별로 얹었다. 단·포장·개울·연못·폭포 메시와 타일 텍스처는 `Assets/01.Scripts/05.Art/Environment/VillageCA07/`, 물 셰이더는 `Assets/01.Scripts/05.Art/Shaders/Atmosphere/`(`SH_StreamWater`, `SH_Waterfall`)에 있다. 단 메시는 보행 MeshCollider를 가진다.
- 건물·나무·소품은 기존 프리팹과 위 CA-07 추가 에셋 4종을 배치했다. 소나무 2종·광장 원형 포장은 이 씬 전용 재질 변형(`M_CA07_*`)으로 색만 바꿨고 원본 재질은 유지한다. 대기·구름·후처리는 [Dev_ShinshiVillageQuarterView와 같은 구성](#마을-쿼터뷰-배치-씬)이다.
- 확인 범위: 저장 씬의 누락 프리팹·메시·재질·스크립트 0, 플레이 모드에서 기본 줌 게임 카메라 화면, 피치55 전체 비교 렌더, Console 오류 0. 미검증: 전체 보행·계단/다리 연결 높이, 연속 LOD·성능·Player 빌드, 원화 유사도 수치. 단 바닥·포장·절벽 텍스처는 절차 생성이며 AO는 높이장 오목함으로 계산했다(형상 베이크 아님).
- 2026-10-03 3차(로스트아크풍 보정): 단 높이차를 1.6배로 키우고 섬 청크를 단보다 3.5m 낮췄다. 광장의 회색 사각 판석(`Plaza_Ring_*`, `Plaza_Centre`)은 이 씬에서만 비활성화하고 동심원 판석(`SM_CA07_PlazaDisc`)과 테두리돌(`SM_CA07_PlazaRings`)로 바꿨다. 잔디·판석·절벽 텍스처는 2048 2차본(같은 높이장에서 Normal/AO 계산)이다. 소나무·관목·단풍·약초 잎은 [Lit Occlusion Fade Soft Foliage](../../Assets/01.Scripts/05.Art/Shaders/OcclusionFade/LitOcclusionFadeSoftFoliage.shader) 변형 재질(`M_CA07_*`)로 꼭짓점 노멀만 둥글게 굽혀 면이 각져 보이는 느낌을 줄였다. 원본 메시·재질·셰이더는 바꾸지 않았고, 변형 재질은 추가 속성 때문에 SRP Batcher 대상이 아니다.
