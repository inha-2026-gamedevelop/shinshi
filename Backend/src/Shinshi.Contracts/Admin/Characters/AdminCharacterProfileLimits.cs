namespace Shinshi.Contracts.Admin
{
    /// <summary> 캐릭터 프로필의 고정 조회 한도와 character_training CHECK 계약 </summary>
    public static class AdminCharacterProfileLimits
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int TRAINING_ITEM_CAP    = 40;
        public const int TRAINING_TOTAL_CAP   = 100;
        public const int RECENT_SESSION_LIMIT = 5;
    }
}
