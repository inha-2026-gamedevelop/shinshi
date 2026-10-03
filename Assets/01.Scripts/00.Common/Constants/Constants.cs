namespace Shinshi.Common
{
    /// <summary> 코드 계약값 모음, 밸런스 수치는 GameDB에 둔다 </summary>
    public static partial class Constants
    {
        /// <summary> 씬 이름, 작업명이므로 씬을 만들 때 이 이름과 맞춘다. 씬은 이름으로 불러오므로 이름이 겹치지 않게 한다 </summary>
        public static class Scene
        {
            /****************************************
            *                Fields
            ****************************************/

            // Bootstrap은 FOLDER에, 실제 실행 흐름 씬은 RELEASE_FOLDER에 둔다
            public const string FOLDER         = "Assets/00.Scenes/";
            public const string RELEASE_FOLDER = FOLDER + "Release/";
            public const string EXTENSION      = ".unity";

            // 클라이언트 진입 흐름. Bootstrap에서 로비, 캐릭터 선택과 생성을 거쳐 마을 서버가 안내하는 마을로 간다
            public const string BOOTSTRAP        = "Bootstrap";
            public const string LOBBY            = "Lobby";
            public const string CHARACTER_SELECT = "CharacterSelect";
            public const string CHARACTER_CREATE = "CharacterCreate";
            public const string SHINSHI_TOWN     = "ShinshiTown";

            // 아직 만들지 않은 씬
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
