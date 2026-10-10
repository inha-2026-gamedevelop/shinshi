# 마을 HUD 개발 씬

대상은 `Assets/00.Scenes/Dev_HUD/Dev_ShinshiVillageQuarterView.unity`이다. 원본 `Dev_HUD.unity`와 `Assets/09.Data/UI/Layout_HUD.json`은 별개이며, JSON을 다시 적용하면 수동 배치에 영향을 줄 수 있다.

## 이동용 NavMesh

`Navigation` 루트의 NavMeshSurface는 Humanoid, Walkable, Physics Colliders, Volume 수집을 사용한다. 포함 레이어는 Default와 Obstacle(mask 257)이다. 수집 범위는 center `(16.203926, 8.5442295, 5.126976)`, size `(154.71117, 28.383587, 141.47272)`이며 기존 활성 Collider 범위에 여유를 둔 값이다.

`PF_NetPlayer`와 `Test_Player`의 씬 인스턴스에는 자식까지 적용하는 Ignore From Build NavMeshModifier를 붙여 플레이어가 장애물로 베이크되지 않게 했다. 원본 프리팹은 수정하지 않았다. 환경 Collider나 배치를 바꾸면 Navigation의 NavMesh를 다시 베이크하고 아래 파일과 함께 씬을 저장한다.

- 데이터: `Assets/00.Scenes/Dev_HUD/Dev_ShinshiVillageQuarterView/NavMesh-Navigation.asset`
- 씬과 데이터, 데이터 폴더를 각자의 `.meta`와 함께 보관한다.

2026-10-03 에디터 검증에서 베이크된 정점 5259개·삼각형 2393개를 확인했다. 중앙 출발점 주변 7곳의 완전 경로와 Play Mode의 MovePathRules/분리된 PathFollower 도달을 확인했다. 전체 구역 연결성, 소유된 네트워크 플레이어 입력·이동, M2/M3 및 Player 빌드는 미검증이다.

## GroundPaint 씬 NavMesh (2026-10-10)

`Dev_ShinshiVillageQuarterView_GroundPaint.unity`는 전용 데이터 `Assets/00.Scenes/Dev_HUD/Dev_ShinshiVillageQuarterView_GroundPaint/NavMesh-Navigation.asset`를 사용한다. Release의 ShinshiTown 데이터와 분리했다. Humanoid의 반경 0.5m·높이 2m·경사 45도·단차 0.75m를 유지하며, Physics Colliders·Default/Obstacle(mask 257)·Volume center `(5, 6, -5)`·size `(130, 36, 140)`·voxel 0.1m·Height Mesh로 베이크한다.

씬의 NavMeshModifier는 배치 소품과 중앙 성소의 장애물·수면을 Not Walkable로 처리하고 바닥·계단·다리 상판·교각의 상단을 Walkable로 지정한다. 북서 전망대 입구의 낮은 돌 2개도 보행 표면으로 처리한다. `01_CA07_IslandBody`의 이전 외피 충돌체는 Ignore From Build로 제외한다. 이 충돌체는 현재 바닥 바깥의 공중 영역과 남쪽 계단 위의 평면을 생성했기 때문이다. 모델·물리 Collider·배치는 변경하지 않았다. 재베이크할 때 이 Modifier와 전용 데이터 참조를 유지한다.

베이크 결과는 정점 5775개·삼각형 2581개다. 11개 지형 구역 중심·남쪽 착지대·계단 출입점 6개, 총 18개 지점 사이의 방향별 경로 306개가 모두 PathComplete였다. 저장된 데이터를 사용하는 Play Mode에서 반경 0.5m·높이 2m의 임시 NavMeshAgent가 광장과 나머지 11개 지점 사이를 왕복해 22/22회 도착했다. 연못·개울 표면 표본 42개와 섬 바깥 표본 4개에서 이동 영역이 검출되지 않았다. NavMesh 오버레이도 캡처로 확인했다. 최종 Console 오류·경고는 0이며 Edit Mode로 복귀했다.

기존 CharacterController의 북쪽 계단 오르기 충돌과 남쪽 계단 위 이전 외피 Collider는 물리 충돌 문제로 남아 있다. NavMesh 경로 연결은 이 충돌체를 사용하는 실제 플레이어 이동의 통과를 보증하지 않는다. 소유된 네트워크 플레이어 이동·M2/M3·Player 빌드는 미검증이다.

## 씬 플레이어의 스폰 상태

Play Mode에서 미스폰 `PF_NetPlayer` 씬 인스턴스가 비활성화되는 것은 Mirror의 씬 NetworkIdentity 준비 처리다. 오브젝트는 남아 있으며 NavMesh가 삭제한 것이 아니다. 실제 플레이어는 인증된 서버의 스폰과 소유권 부여가 필요하다.

검증 때 기존 Mirror NetworkConnection 생성자의 Time.time 접근으로 PF_NetPlayer 직렬화 예외가 남아 있었다. Mirror 원본은 이번 작업에서 수정하지 않았다.

## 퀘스트·파티 HUD 시안

퀘스트와 파티 UI는 현재 개발용 예시 표시이며 게임의 실제 임무·파티 상태에 연결되지 않았다.

- 퀘스트: 상단 `진행 중인 임무 목록`과 `(n/n)`, 최소화 버튼, 투명한 임무 본문 3개. `HudQuestTrackerView`의 버튼은 본문만 접고 펼치며 상단은 유지한다. 외부에서 `SetCount`로 개수를 전달할 수 있다.
- 파티: 4명 자리마다 원형 직업 아이콘, 위쪽 닉네임, 아래쪽 HP 바와 가운데 흰 수치를 표시한다. 멤버를 감싸는 판은 없으며 기존 MP 자리표시는 비활성화했다. 이름·직업·HP는 예시이고 파티 데이터 바인더는 아직 없다.

| 용도 | 배치 에셋·코드 |
|---|---|
| 퀘스트 헤더 | `Assets/01.Scripts/05.Art/UI/HudQuest/Textures/T_HudQuest_Header.png` |
| 퀘스트 접기·개수 표시 | `Assets/01.Scripts/06.UI/Hud/HudQuestTrackerView.cs` |
| 파티 직업·빈 HP 틀 | `Assets/01.Scripts/05.Art/UI/HudParty/Textures/T_HudParty_Atlas.png`의 Multiple Sprite 4종 |
| 파티 HP 채움 | 기존 `Assets/01.Scripts/05.Art/UI/HudPlayerStatus/Textures/T_HudPlayerStatus_FillHp.png` 재사용 |
| 한글·기호 TMP | `Assets/01.Scripts/06.UI/Hud/Fonts/`의 Korean Regular/Bold와 Symbols |

글자는 텍스처에 포함되지 않으며 TMP로 배치했다. Nanum Gothic 원본과 OFL 라이선스는 Fonts의 NanumGothic 폴더에 있다. TMP 파생 폰트에 예시 글리프를 넣었고 전역 fallback은 바꾸지 않았다. 파티는 Korean Bold를 사용한다. 에셋과 하위 폴더의 `.meta`를 함께 보관한다.

2026-10-03 Play Mode 화면에서 투명 배경·4명 파티 표시·한글과 숫자의 미잘림을 확인했다. 퀘스트 버튼의 EventSystem 포인터 이벤트로 접기·펼치기를 확인했고 `SetCount(2, 3)` 결과도 확인했다. 실제 임무·파티 데이터 연결, 다인 접속과 Player 빌드는 미검증이다.

## 릴리즈 씬 적용 (2026-10-07)

`Assets/00.Scenes/Release/ShinshiTown.unity`에 이 씬의 `HUDCanvas`와 `UiCursor`, UI 담당 시험 씬의 창을 옮겼다. 시험 씬 원본은 바꾸지 않았다.

| 릴리즈 루트 | 원본 씬 | 열기 | 서버 값 |
|---|---|---|---|
| `HUDCanvas` | Dev_ShinshiVillageQuarterView | 항상 | HP·MP·기술(`LocalPlayerHudBinder`), 파티(`PartyHudBinder` → `HudPartyView`) |
| `InventoryCanvas` | Dev_ItemSlot | `I`, HUD 가방 버튼 | 성장·생활 스냅숏(`InventoryBinder`) |
| `AuctionCanvas` | Dev_Auction | `Y` | 없음(유저 경매장 서버 미구현) |
| `SortiePrepCanvas` | Dev_SortiePrep | `P`, 파티 방 입장 | 파티 방(`SortiePrepBinder`) |
| `ResultCanvas` | Dev_Result | 출정 종료 | 출정 상태·정산 단계(`SortieResultBinder`) |
| `PopupCanvas` | Dev_ToastMessage | 필요 시 | 거절·초대·기술 거절(`NetworkFeedbackBinder`) |
| `TradeCanvas`, `DungeonProgressCanvas` | Dev_Trade, Dev_DungeonProcess | 닫힌 채 배치 | 없음(개인거래·웨이브 진행 메시지 미구현) |

단축키는 `Assets/09.Data/UI/HotkeyConfig.asset`(`HotkeyConfigSO`)에 기록하고, 씬의 `UiBinding/HotkeyRouter`가 읽는다. 창은 `HotkeyRouter.Register`로 등록하고, `Esc`는 가장 최근에 연 창부터 닫는다. `CharacterSelect.unity`는 `ChaSelectCanvas`를 Dev_CharSelect 최신본으로 바꾸고 `CharacterSelectScreen` 참조를 같은 위치의 새 오브젝트로 옮겼다.

아직 없는 서버 값: 아이템 표시 이름(번역 표 없음, 정의 ID 표시), 캐릭터 레벨, 퀘스트, 출정 준비의 레벨·전투력, 결과 창의 보상 내역, 던전 웨이브 진행도. 검증은 EditMode 테스트와 오프라인 Play Mode 단축키 확인까지이며 실제 서버 접속 흐름은 미검증이다.

## 캐릭터 선택 개발 씬 아이콘 (2026-10-07)

`Assets/00.Scenes/Dev_HUD/Dev_CharSelect.unity`에 직업 마크 3개와 바랜 은색 기능 아이콘 5개를 연결했다. 직업 마크를 가리던 `Lv.` 예시 글자는 비활성화했으며, 하단 아이콘과 문구의 중심·화면 가장자리 여백을 맞췄다.

| 용도 | 에셋 경로 |
|---|---|
| 무사·도술사·신령사 | `Assets/01.Scripts/05.Art/UI/Icons/Textures/Class/T_Icon_Class_{Warrior,Sorcerer,Summoner}.png` |
| 뒤로·삭제·설정·종료·생성 | `Assets/01.Scripts/05.Art/UI/CharSelect/Textures/T_CharSelect_Icon_{Back,Delete,Settings,Exit,Create}_Silver.png` |

기능 아이콘은 투명 알파·단일 Sprite·Full Rect·밉맵 없음·최대 256px 설정이다. Play Mode의 1211×677 화면에서 표시·문구 미잘림을 확인하고, 직업 마크 중앙을 EventSystem으로 raycast한 뒤 포인터 클릭 이벤트를 전달해 세 슬롯 각각 0/1/2 선택을 확인했다. 검사 후 Edit Mode로 복귀했으며 Console 오류·경고 0이었다. 기능 아이콘의 기존 오브젝트는 Image이며 실제 생성·삭제·설정·종료 동작은 이번 검증에 포함하지 않았다. 릴리즈 `CharacterSelect.unity`는 이 아이콘 교체를 아직 반영하지 않았다.

## 캐릭터 상세 개발 씬 (2026-10-07)

`Assets/00.Scenes/Dev_HUD/Dev_CharDetail.unity`의 `CharDetailCanvas/Layout_캐릭터 상세/캐릭터 정보`에 976×676 상세창을 배치했다. 공통 GameStyle 창·슬롯, 기존 한글 TMP와 무사 직업 마크를 재사용한다. 왼쪽은 무기·머리·몸통·손·발·장신구 6칸, 가운데는 무사 외형, 오른쪽은 닉네임·직업·주요 능력치·수련·부적 요약·정기다. 장비 칸은 실제 아이템 그림 대신 부위명·등급명·HEX·+17 예시를 표시하는 색상 가이드다.

오른쪽 표시값은 희귀 제작 장비 6부위 +17·수련 공격10/생명10/방어7/치명5/기력5(합37)의 배치용 예시다. 공격676·최대 체력3895·방어372.55·치명9.5%·기력105를 표시하며 부적12효과와 정기5000도 예시다. 왼쪽 장비 칸의 여러 등급은 색상 가이드이며 이 수치 계산에는 연결되지 않는다. 캐릭터 레벨·경험치·아이템 레벨과 산식 미정인 종합 전투력은 추가하지 않았다.

장비 6칸의 배경은 불투명 검정 `#000000`이다. 테두리·부위명·등급명·HEX 글자는 인벤토리 `PF_ItemSlot.prefab`이 참조하는 `Assets/01.Scripts/05.Art/UI/Icons/UiIconLibrary.asset`의 `FindGradeColor` 값과 일치한다. 무기·장신구는 전설, 머리는 영웅, 몸통·발은 희귀, 손은 일반 예시다.

| 등급 | HEX |
|---|---|
| 일반 | `#B4B2A9` |
| 희귀 | `#378ADD` |
| 영웅 | `#7F77DD` |
| 전설 | `#EF9F27` |

강화 단계별 폰트 색상 규칙은 UI 코드·문서에서 확인되지 않았다. `InventoryItemRules`는 강화 수치를 툴팁 설명의 `+{0} 강화`로 전달하며 `ItemSlotData`와 `ItemSlotView`에는 강화 단계별 글자색 처리가 없다. 가이드의 +17은 모든 칸에서 고정색 `#E6EBF0`으로 표시한다. Play Mode에서 6칸의 검정 배경·인벤토리 등급색 일치·추가 안내 글자 미잘림과 Console 오류·경고0을 확인했다. 실제 아이템 데이터와 강화 단계별 색상 바인딩은 적용하지 않았다.

미리보기는 기존 `PF_FemaleWarrior_Cute.prefab`의 씬 인스턴스와 `Assets/09.Data/UI/RT_CharDetailPreview.renderTexture`(652×1100, MSAA2, 밉맵 없음)를 사용하는 RawImage다. 프리팹 원본은 수정하지 않고 미리보기 인스턴스의 동작 스크립트·Animator를 비활성화했다. 팔을 내린 고정 자세는 씬 오버라이드다. 실제 장비 착용 외형이나 직업 전환에는 연결되지 않았다.

뷰포트의 별도 Image 배경은 비활성화하고 Preview Camera를 투명색 Solid Color·HDR 끔으로 설정해 상세창 배경 위에 캐릭터만 합성한다. Play Mode에서 RT 양 끝 모서리 alpha0, 전체717200픽셀 중562436픽셀 완전 투명과154764픽셀 불투명을 확인했다. 공용 URP 설정은 변경하지 않았다.

구분선8개는 기존 `Assets/01.Scripts/05.Art/UI/GameStyle/Textures/T_UiLine_Fade.png`를 재사용한다. 능력치5줄은 alpha0.15, 수련·부적·세로선은 alpha0.30이며 세로선은 같은 Sprite를90도 회전해 양 끝 페이드를 유지한다. Play Mode 화면과8/8 Sprite 참조·TMP 잘림0·Console 오류/경고0을 확인했다.

기존 `UiWindowDragHandle`과 Button의 직렬화된 이벤트로 제목줄 드래그·닫기·왼쪽 위 버튼으로 다시 열기를 연결했다. Play Mode 1059×677에서 화면 표시·47개 활성 TMP 미잘림, EventSystem raycast와 포인터 클릭으로 닫기/열기, 드래그 이벤트로 이동과 화면 경계 제한을 확인했다. Console 오류·경고0이며 실제 마우스 조작·다른 화면비·서버 데이터·Player 빌드는 미검증이다. 이 개발 씬은 릴리즈 흐름에 적용하지 않았다.

## 재화 아이콘 (2026-10-07)

흰색 단색·투명 배경의 엽전(사각 구멍 동전)과 정기(기운을 감싼 불꽃) 아이콘을 `Assets/01.Scripts/05.Art/UI/Icons/Textures/Currency/`에 배치했다. 파일은 `T_Icon_Currency_Yeopjeon_White.png`와 `T_Icon_Currency_Essence_White.png`이며 256×256, Single Sprite, Full Rect, Clamp, 밉맵 없음, 비압축이다. UI의 Image.color로 색을 입힐 수 있다.

`UiIconLibrary.asset`의 `item_yeopjeon`·`item_essence`를 새 Sprite로 연결했다. `Dev_ItemSlot.unity` 하단의 재화 이름 자리는 24×24 아이콘으로 표시하고 기존 수치 바인딩을 유지한다. `Dev_CharDetail.unity`의 정기 행에는 26×26 아이콘을 붙이고 정기 문구를 오른쪽으로 이동했다. `정기 배경` Image의 알파는 0으로 설정해 상세창 바탕이 그대로 보인다. 두 개발 씬의 기존 배치를 확인하기 위한 적용이며 릴리즈 씬의 하단 문구는 수정하지 않았다.

Play Mode 화면에서 인벤토리 하단의 두 아이콘·수치 1,250/2,100과 상세창 정기 아이콘·투명 행을 확인했다. Sprite 참조·흰색 틴트·수치 미잘림, Console 오류·경고0을 확인했다. 엽전 중앙 구멍과 이미지 네 모서리는 alpha0이다. 실제 서버 재화 변경·Player 빌드는 미검증이다.

## 은색 HUD 메뉴 아이콘 (2026-10-07)

기존 CharSelect의 뒤로·환경설정·게임 종료 아이콘과 같은 바랜 은색 금속·두꺼운 베벨 표현으로 메뉴 아이콘 5개를 제작했다. 모두 `Assets/01.Scripts/05.Art/UI/HudMenuButton/Textures/`에 둔다.

| 메뉴 | 파일 | 적용 |
|---|---|---|
| 캐릭터 정보 | `T_HudMenuButton_IconProfile.png` | 기존 파일 교체, GUID 유지 |
| 인벤토리 | `T_HudMenuButton_IconInventory.png` | 기존 파일 교체, GUID 유지 |
| 특성 | `T_HudMenuButton_IconTraits.png` | 메뉴 전용 새 Sprite |
| 경매장 | `T_HudMenuButton_IconAuction.png` | 메뉴 전용 새 Sprite |
| 캐릭터 선택 | `T_HudMenuButton_IconCharacterSelect.png` | 꺾쇠·세로선 형태의 메뉴 전용 새 Sprite |
| 우편함 | `T_HudMenuButton_IconMail.png` | 2026-10-08 추가, 은색 봉투의 메뉴 전용 새 Sprite |

캐릭터 정보는 사용자 참고 이미지처럼 왼쪽을 보는 사람 옆얼굴이며 투구 대신 사용한다. 캐릭터 선택은 왼쪽 꺾쇠와 짧은 세로선으로 표시한다. 특성은 4개 능력 노드를 잇는 가지 문양, 경매장은 경매 망치다. 기존 공용 수련 공격·경매 재료·뒤로가기 Sprite는 보존하며 메뉴의 Image.sprite에 전용 파일을 연결한다.

우편함 Sprite는 기존 가방 아이콘과 같은 바랜 은색 금속·두꺼운 베벨의 닫힌 봉투다. 글자·배지 없이 봉투 덮개의 삼각선으로 구분한다. 원본 1254×1254 PNG의 알파를 유지하고 Single Sprite·Full Rect·최대256px·밉맵 없음·비압축·Clamp로 임포트했다. `Dev_HUD/Dev_ShinshiVillageQuarterView.unity` 확장 메뉴의 우편함에 흰색 틴트·비율 유지로 연결하고 기존 선 아이콘을 제거했다. `VillageUiPreviewBuilder`도 같은 Sprite를 연결하므로 메뉴 재생성 시 유지된다. 32/48/64px 비교와 씬 메뉴 사본의 Unity 카메라 캡처에서 형태를 확인했다. 재생성에 사용하는 아이콘 함수를 재호출해 참조 유지와 임시 아이콘 중복 없음, 기존 `OpenMail` 클릭 이벤트 유지도 확인했다. 환경설정 씬의 미저장 작업을 보존하기 위해 Play Mode를 전환하지 않았으며 우편 열기·수령 동작과 Player 빌드는 이번 아이콘 적용의 검증에 포함하지 않았다.

사용자 소유 `Assets/00.Scenes/Dev_HUD/` 개발 씬의 기존 사용처에 적용했다. `Dev_HUD.unity`와 `Dev_ShinshiVillageQuarterView.unity`의 상시 메뉴 캐릭터 정보·인벤토리는 GUID를 유지한 파일 교체로 반영된다. 마을 UI 개발 씬의 확장 메뉴는 특성·경매장·캐릭터 선택을 새 Sprite로 연결했다. `Dev_CharSelect.unity`의 왼쪽 위 뒤로 가기 버튼에도 꺾쇠·세로선 Sprite를 사용한다. `Dev_ItemSlot.unity` 제목 가방과 `Dev_Trade.unity`의 인벤토리·거래창 제목 가방은 흰색 틴트·비율 유지로 설정했다.

원본 PNG는 투명 알파가 있으며 Unity 임포트는 Single Sprite·Full Rect·최대256px·밉맵 없음·비압축·Clamp다. 32/64/96px 비교 화면에서 기존 은색 아이콘과 톤·실루엣을 확인했다. 씬 적용 후 마을 확장 메뉴 Play Mode에서 메뉴 열기·닫기와 새 아이콘5개 표시를 확인하고 캐릭터 선택 화면도 캡처했다. HUD·인벤토리·거래창은 에디터 Game View 캡처로 아이콘 표시를 확인했다. 검증 중 다른 인벤토리 코드 작업의 재컴파일로 Play Mode가 중단되어 인벤토리·거래창 런타임 조작은 이번 검증에서 제외한다. 마지막 Console 확인은 오류0이며 범위 밖 기존 코드·외부 에셋 경고와 MCP 재접속 경고가 남았다. 실제 메뉴 목적지 이동·Player 빌드는 미검증이다.

## 인벤토리 슬롯 이동·정렬 잠금 (2026-10-07)

`Dev_ItemSlot.unity`의 `InventoryView._enableSlotInteraction`을 켜 슬롯 간 왼쪽 드래그 이동·교환과 좌우 Alt + 왼쪽 클릭 잠금 토글을 연결했다. 빈 칸에는 이동하고 아이템이 있는 칸과는 교환한다. 잠금 표시는 기존 `ItemSlotView`를 사용하며, 정렬 시 잠긴 아이템은 현재 위치를 유지한다. 직접 이동하면 잠금 상태도 아이템에 따라 이동한다. 분류 탭의 빈 칸은 실제 빈 칸에 대응하고 다른 분류 아이템을 덮어쓰지 않는다. 슬롯 밖에 놓거나 창 닫기·탭 변경·정렬·데이터 갱신 시 드래그를 취소한다.

입력은 `Assets/01.Scripts/06.UI/Inventory/InventorySlotInteraction.cs`에서 처리한다. 기능 활성화 기본값은 false이고 이번 연결 대상은 `Dev_ItemSlot`이다. 서버로부터 받은 화면용 사본의 로컬 배치이며 서버 저장·재접속 유지·수량 분할·자동 합치기·다른 창으로 이전은 이번 범위에 포함하지 않는다.

Play Mode에서 EventSystem 드래그 이벤트와 실제 정렬 Dropdown/Button 콜백, UI 메서드로 34개 검증 항목이 통과했다. 이동·교환·잠금 표시·정렬 4종과 방향 전환·잠긴 아이템 이동 후 위치 유지·분류 탭 대응·수량 보존·아이콘 추적과 취소를 확인했다. Game View 캡처로 18/100칸 화면 표시를 확인했고 최종 Console 조회는 오류·경고0이었다. 물리 키보드의 Alt + 클릭 조합과 Player 빌드는 미검증이다.
