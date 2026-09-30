namespace Shinshi.Common
{
    /// <summary> 코드 계약값 모음, 밸런스 수치는 GameDB에 둔다 </summary>
    public static partial class Constants
    {
        /// <summary> 씬 이름, 작업명이므로 씬을 만들 때 이 이름과 맞춘다 </summary>
        public static class Scene
        {
            /****************************************
            *                Fields
            ****************************************/

            public const string BOOTSTRAP    = "Bootstrap";
            public const string LOGIN        = "Login";
            public const string SHINSHI_TOWN = "ShinshiTown";
            public const string FOREST_WAVE  = "ForestWave";
            public const string WOLBAEK_EASY = "WolbaekEasy";
            public const string DEV_COMBAT   = "DevCombat";
            public const string DEV_GROWTH   = "DevGrowth";
        }

        /// <summary> Resources로 로드하는 프로젝트 소유 에셋의 경로 </summary>
        public static class Resource
        {
            /****************************************
            *                Fields
            ****************************************/

            // Bootstrap이 읽는 필수 루트, 이 에셋 하나만 Resources에 둔다
            public const string GAME_DB_PATH = "GameDB";
        }
    }
}
