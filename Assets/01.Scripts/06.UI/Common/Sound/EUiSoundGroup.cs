// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> 소리를 내는 UI 종류, 소리 이름의 첫 단계 </summary>
    // 값은 카탈로그와 프리팹에 숫자로 저장되므로 순서를 바꾸거나 중간에 끼우지 말고 끝에 새 번호로 추가한다
    public enum EUiSoundGroup
    {
        [InspectorName("공통")]         Common           = 0,
        [InspectorName("알림창")]       Alert            = 1,
        [InspectorName("확인창")]       Confirm          = 2,
        [InspectorName("토스트")]       Toast            = 3,
        [InspectorName("HUD")]          Hud              = 4,
        [InspectorName("메뉴")]         Menu             = 5,
        [InspectorName("인벤토리")]     Inventory        = 6,
        [InspectorName("캐릭터 정보")]  CharacterDetail  = 7,
        [InspectorName("스킬창")]       SkillWindow      = 8,
        [InspectorName("장비 강화")]    EquipmentEnhance = 9,
        [InspectorName("옵션 재설정")]  EquipmentOption  = 10,
        [InspectorName("수련")]         Training         = 11,
        [InspectorName("설정")]         Settings         = 12,
        [InspectorName("우편함")]       MailBox          = 13,
        [InspectorName("경매장")]       Auction          = 14,
        [InspectorName("거래")]         Trade            = 15,
        [InspectorName("친구")]         Friends          = 16,
        [InspectorName("길드")]         Guild            = 17,
        [InspectorName("파티 모집")]    PartyRecruit     = 18,
        [InspectorName("출정 준비")]    SortiePrep       = 19,
        [InspectorName("던전 입장")]    DungeonEntry     = 20,
        [InspectorName("결과")]         Result           = 21,
        [InspectorName("로비")]         Lobby            = 22,
        [InspectorName("미니맵")]       Minimap          = 23,
        [InspectorName("상호작용")]     Interaction      = 24,
    }
}
