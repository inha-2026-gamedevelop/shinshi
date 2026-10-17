namespace Shinshi.Systems
{
    /// <summary> 단축키로 부르는 창과 동작 </summary>
    // 단축키 설정 에셋에 번호로 저장되므로 값을 바꾸거나 중간에 끼워 넣지 않고 끝에 붙인다
    public enum EHotkeyAction
    {
        None         = 0,
        Inventory    = 1,
        AuctionHouse = 2,
        CloseWindow  = 3,
        SortiePrep   = 4,
        Chat         = 5,
        CombatMeter  = 6,
        MailBox       = 7,
        CharacterInfo = 8,
        Trade         = 9,
        EquipmentOption = 10,
        SkillWindow     = 11,
        Friends         = 12,
        Guild           = 13
    }
}
