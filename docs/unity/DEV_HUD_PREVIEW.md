# 마을 HUD 개발 씬

대상은 `Assets/00.Scenes/Dev_HUD/Dev_ShinshiVillageQuarterView.unity`이다. 원본 `Dev_HUD.unity`와 `Assets/09.Data/UI/Layout_HUD.json`은 별개이며, JSON을 다시 적용하면 수동 배치에 영향을 줄 수 있다.

## 이동용 NavMesh

`Navigation` 루트의 NavMeshSurface는 Humanoid, Walkable, Physics Colliders, Volume 수집을 사용한다. 포함 레이어는 Default와 Obstacle(mask 257)이다. 수집 범위는 center `(16.203926, 8.5442295, 5.126976)`, size `(154.71117, 28.383587, 141.47272)`이며 기존 활성 Collider 범위에 여유를 둔 값이다.

`PF_NetPlayer`와 `Test_Player`의 씬 인스턴스에는 자식까지 적용하는 Ignore From Build NavMeshModifier를 붙여 플레이어가 장애물로 베이크되지 않게 했다. 원본 프리팹은 수정하지 않았다. 환경 Collider나 배치를 바꾸면 Navigation의 NavMesh를 다시 베이크하고 아래 파일과 함께 씬을 저장한다.

- 데이터: `Assets/00.Scenes/Dev_HUD/Dev_ShinshiVillageQuarterView/NavMesh-Navigation.asset`
- 씬과 데이터, 데이터 폴더를 각자의 `.meta`와 함께 보관한다.

2026-10-03 에디터 검증에서 베이크된 정점 5259개·삼각형 2393개를 확인했다. 중앙 출발점 주변 7곳의 완전 경로와 Play Mode의 MovePathRules/분리된 PathFollower 도달을 확인했다. 전체 구역 연결성, 소유된 네트워크 플레이어 입력·이동, M2/M3 및 Player 빌드는 미검증이다.

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

단축키는 `Assets/09.Data/UI/HotkeyConfig.asset`(`HotkeyConfigSO`)에 기록하고, 씬의 `UiBinding/HotkeyRouter`가 읽는다. 창은 `HotkeyRouter.Register`로 등록하고, `Esc`는 가장 최근에 연 창부터 닫는다. `CharacterSelect.unity`는 `ChaSelectCanvas`를 Dev_ChaSelect 최신본으로 바꾸고 `CharacterSelectScreen` 참조를 같은 위치의 새 오브젝트로 옮겼다.

아직 없는 서버 값: 아이템 표시 이름(번역 표 없음, 정의 ID 표시), 캐릭터 레벨, 퀘스트, 출정 준비의 레벨·전투력, 결과 창의 보상 내역, 던전 웨이브 진행도. 검증은 EditMode 테스트와 오프라인 Play Mode 단축키 확인까지이며 실제 서버 접속 흐름은 미검증이다.

## 캐릭터 선택 개발 씬 아이콘 (2026-10-07)

`Assets/00.Scenes/Dev_HUD/Dev_ChaSelect.unity`에 직업 마크 3개와 바랜 은색 기능 아이콘 5개를 연결했다. 직업 마크를 가리던 `Lv.` 예시 글자는 비활성화했으며, 하단 아이콘과 문구의 중심·화면 가장자리 여백을 맞췄다.

| 용도 | 에셋 경로 |
|---|---|
| 무사·도술사·신령사 | `Assets/01.Scripts/05.Art/UI/Icons/Textures/Class/T_Icon_Class_{Warrior,Sorcerer,Summoner}.png` |
| 뒤로·삭제·설정·종료·생성 | `Assets/01.Scripts/05.Art/UI/CharSelect/Textures/T_CharSelect_Icon_{Back,Delete,Settings,Exit,Create}_Silver.png` |

기능 아이콘은 투명 알파·단일 Sprite·Full Rect·밉맵 없음·최대 256px 설정이다. Play Mode의 1211×677 화면에서 표시·문구 미잘림을 확인하고, 직업 마크 중앙을 EventSystem으로 raycast한 뒤 포인터 클릭 이벤트를 전달해 세 슬롯 각각 0/1/2 선택을 확인했다. 검사 후 Edit Mode로 복귀했으며 Console 오류·경고 0이었다. 기능 아이콘의 기존 오브젝트는 Image이며 실제 생성·삭제·설정·종료 동작은 이번 검증에 포함하지 않았다. 릴리즈 `CharacterSelect.unity`는 이 아이콘 교체를 아직 반영하지 않았다.

## 캐릭터 상세 개발 씬 (2026-10-07)

`Assets/00.Scenes/Dev_HUD/Dev_CharDetail.unity`의 `CharDetailCanvas/Layout_캐릭터 상세/캐릭터 정보`에 976×676 상세창을 배치했다. 공통 GameStyle 창·슬롯, 기존 한글 TMP와 무사 직업 마크를 재사용한다. 왼쪽은 무기·머리·몸통·손·발·장신구 6칸, 가운데는 무사 외형, 오른쪽은 닉네임·직업·주요 능력치·수련·부적 요약·정기다. 장비 칸은 실제 아이템 그림 대신 부위명과 +17 예시를 표시한다.

표시값은 희귀 제작 장비 6부위 +17·수련 공격10/생명10/방어7/치명5/기력5(합37)의 배치용 예시다. 공격676·최대 체력3895·방어372.55·치명9.5%·기력105를 표시하며 부적12효과와 정기5000도 예시다. 캐릭터 레벨·경험치·아이템 레벨과 산식 미정인 종합 전투력은 추가하지 않았다.

미리보기는 기존 `PF_FemaleWarrior_Cute.prefab`의 씬 인스턴스와 `Assets/09.Data/UI/RT_CharDetailPreview.renderTexture`(652×1100, MSAA2, 밉맵 없음)를 사용하는 RawImage다. 프리팹 원본은 수정하지 않고 미리보기 인스턴스의 동작 스크립트·Animator를 비활성화했다. 팔을 내린 고정 자세는 씬 오버라이드다. 실제 장비 착용 외형이나 직업 전환에는 연결되지 않았다.

뷰포트의 별도 Image 배경은 비활성화하고 Preview Camera를 투명색 Solid Color·HDR 끔으로 설정해 상세창 배경 위에 캐릭터만 합성한다. Play Mode에서 RT 양 끝 모서리 alpha0, 전체717200픽셀 중562436픽셀 완전 투명과154764픽셀 불투명을 확인했다. 공용 URP 설정은 변경하지 않았다.

구분선8개는 기존 `Assets/01.Scripts/05.Art/UI/GameStyle/Textures/T_UiLine_Fade.png`를 재사용한다. 능력치5줄은 alpha0.15, 수련·부적·세로선은 alpha0.30이며 세로선은 같은 Sprite를90도 회전해 양 끝 페이드를 유지한다. Play Mode 화면과8/8 Sprite 참조·TMP 잘림0·Console 오류/경고0을 확인했다.

기존 `UiWindowDragHandle`과 Button의 직렬화된 이벤트로 제목줄 드래그·닫기·왼쪽 위 버튼으로 다시 열기를 연결했다. Play Mode 1059×677에서 화면 표시·47개 활성 TMP 미잘림, EventSystem raycast와 포인터 클릭으로 닫기/열기, 드래그 이벤트로 이동과 화면 경계 제한을 확인했다. Console 오류·경고0이며 실제 마우스 조작·다른 화면비·서버 데이터·Player 빌드는 미검증이다. 이 개발 씬은 릴리즈 흐름에 적용하지 않았다.

## 재화 아이콘 (2026-10-07)

흰색 단색·투명 배경의 엽전(사각 구멍 동전)과 정기(기운을 감싼 불꽃) 아이콘을 `Assets/01.Scripts/05.Art/UI/Icons/Textures/Currency/`에 배치했다. 파일은 `T_Icon_Currency_Yeopjeon_White.png`와 `T_Icon_Currency_Essence_White.png`이며 256×256, Single Sprite, Full Rect, Clamp, 밉맵 없음, 비압축이다. UI의 Image.color로 색을 입힐 수 있다.

`UiIconLibrary.asset`의 `item_yeopjeon`·`item_essence`를 새 Sprite로 연결했다. `Dev_ItemSlot.unity` 하단의 재화 이름 자리는 24×24 아이콘으로 표시하고 기존 수치 바인딩을 유지한다. 릴리즈 씬의 하단 문구는 수정하지 않았다.

Play Mode 화면에서 인벤토리 하단의 두 아이콘·수치 1,250/2,100을 확인했다. Sprite 참조·흰색 틴트·수치 미잘림, Console 오류·경고0을 확인했다. 엽전 중앙 구멍과 이미지 네 모서리는 alpha0이다. 실제 서버 재화 변경·Player 빌드는 미검증이다.
