namespace Shinshi.Contracts
{
    /// <summary> 프롤로그 저장 단계와 한도, EPrologueStage와 같은 값이다. 저장되는 번호라 바꾸거나 재배열하지 않는다 </summary>
    public static class PrologueCodes
    {
        /****************************************
        *                Fields
        ****************************************/

        // 임시 물약 수 상한, 이보다 큰 값은 거절한다
        public const int MAX_POTIONS = 9;

        // 계정이 마칠 수 있는 성장 메뉴 안내 ID 수와 ID 길이 상한
        public const int MAX_MENU_GUIDES    = 64;
        public const int MAX_MENU_ID_LENGTH = 40;

        // 프롤로그 출정 계약. API가 프롤로그 입장 때 game_sessions에 넣고 던전 입장 교환(JoinTokenExchangeResponse)의
        // mapId, battleId, bossId로 돌려준다. 난이도는 GameSessionCodes.Difficulty.EASY다
        public const string MAP_ID    = "prologue_forest";
        public const string BATTLE_ID = "prologue";
        public const string BOSS_ID   = "boss_wolbaek";

        /// <summary> EPrologueStage와 같은 값 </summary>
        public static class Stage
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int INTRO   = 0;
            public const int FOREST  = 1;
            public const int WOLBAEK = 2;
            public const int ENDING  = 3;
            public const int DONE    = 4;
        }
    }
}
