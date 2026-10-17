# 장비 성장 UI

강화와 장비 옵션 재설정은 남색 패널, 민트 실행 버튼, 금색 강화·보라색 옵션 장식으로 표시한다. 왼쪽에서 착용 부위를 선택하고 대상 장비, 변경 내용, 필요/보유 재료를 확인한 뒤 실행한다.

## 배치와 재생성

출시 마을 `Release/ShinshiTown`의 HUD 메뉴에 `장비 강화`와 `옵션 재설정` 버튼을 연결했다. 두 버튼은 다른 성장 창을 닫고 선택한 창을 연다. 강화 창은 꺼진 상태로 시작하며 개발 미리보기 바인더를 넣지 않는다. 기존 서버 요청 경로를 사용한다.

2026-10-15 연결 검사: 에디터에서 저장된 버튼 이벤트를 실행해 강화/옵션 창의 상호 전환을 확인했다. `ShinshiBuild.GetClientScenes()`의 에셋 의존성에 Items64 PNG 45개가 모두 포함된다. 이는 빌드 실행 결과가 아니며, 사용자 지시에 따라 이 작업에서는 클라이언트 빌드를 실행하지 않았다. 서버 컴퓨터에서 변경된 씬·프리팹·코드·PNG와 각 `.meta`를 포함해 빌드하고 실제 접속 동작을 확인해야 한다.

- 강화: `Assets/02.Prefab/UI/EquipmentEnhance/PF_EquipmentEnhanceWindow.prefab`
- 옵션 재설정: `Assets/02.Prefab/UI/Windows/PF_EquipmentOptionWindow.prefab`
- 독립 검수 씬: `Assets/00.Scenes/Dev_HUD/Dev_EquipmentGrowthStyle.unity`
- 강화 프리팹만 재생성: `Shinshi.Editor.EquipmentEnhanceWindowBuilder.BuildPrefabOnly()`
- 옵션 프리팹 재생성: 메뉴 `Shinshi/UI/옵션 재설정 창 만들기`
- 검수 씬 생성: 메뉴 `Shinshi/UI/성장 UI 스타일 검수 씬 만들기`

프리팹 재생성은 빌더 배치를 적용하므로 손으로 추가한 프리팹 변경은 먼저 비교한다. 기존 강화 개발 씬 생성 메뉴는 그 개발 씬을 저장한다. 현재 씬을 보존하려면 `BuildPrefabOnly()`를 사용한다. 검수 씬 생성은 현재 씬이 깨끗할 때만 가능하다.

## 게임 규칙과 연결

강화는 기존 `EquipmentEnhanceView`의 서버 요청·결과 처리를 유지한다. 확률, 실패 보정, 단계별 능력치, 비용은 EquipmentCatalog와 서버 스냅숏에서 표시한다. 실패 시 장비와 강화 단계는 유지된다.

옵션 재설정은 기존 `EquipmentOptionBinder`와 `TownGrowthClient`를 사용한다. 옵션 두 줄을 함께 재설정하고 캐릭터·부위별 천장을 표시한다. 정기·정련편의 앞 수는 필요량, 뒤 수는 보유량이다. 옵션별 잠금, 연속 재설정, 소울, 보조 재료 기능을 추가하지 않았다. 장신구도 기존 여섯 부위 중 하나로 선택한다.

검수 씬의 상단 탭은 두 창의 미리보기를 전환한다. 예시 데이터는 개발용 바인더에만 있으며 출시 프리팹에는 포함하지 않는다. 옵션 미리보기에서는 실행 버튼이 비활성화된다. 강화의 기존 미리보기는 실제 재화·DB를 변경하지 않는다. 검수 씬을 출시 빌드 목록에 추가하지 않는다.

장식 PNG는 `Assets/01.Scripts/05.Art/UI/EquipmentGrowth/Textures/`에 있으며 UI Sprite로 사용한다. 결정과 모루는 기능 장식이고 실제 장비나 소비 재료가 아니다. 장비 6부위와 정기·정련편은 `Icons/Textures/Items64/`의 공통 64픽셀 아이콘을 사용한다. 이전 `EquipmentAtlas`와 `RefineShard` 그림은 보존했다. 원형 장식과 둥근 금속 테두리·음영은 UI 메시가 갱신될 때만 생성하며 프레임별 애니메이션은 없다.

2026-10-15 검증: 관련 EditMode 테스트 32개 통과. Play Mode에서 두 창의 6부위 선택 이벤트, 옵션 창 닫기/재열기, 미리보기 재설정 비활성화를 확인했다. 최종 캡처를 검수했고 두 프리팹의 Missing Script와 View 참조 누락은 0이다. 실제 마우스 입력·마을 서버 재화 차감·다인 접속은 미검증이다. 이 UI의 재배치는 서버 다인 접속 검증을 대신하지 않는다.
