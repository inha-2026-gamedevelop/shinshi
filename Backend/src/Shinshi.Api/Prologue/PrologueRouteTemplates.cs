// Project
using Shinshi.Contracts;

namespace Shinshi.Backend.Prologue
{
    /// <summary> PrologueRoutes와 같은 경로의 ASP.NET Core 템플릿. 두 값이 어긋나지 않는지 테스트로 묶는다 </summary>
    public static class PrologueRouteTemplates
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string STATE               = "/v1/characters/{characterId}/prologue";
        public const string ENTRY               = PrologueRoutes.ENTRY;
        public const string SKIP                = PrologueRoutes.SKIP;
        public const string STAGE               = "/v1/game-sessions/{sessionId}/prologue/stage";
        public const string COMPLETE            = "/v1/game-sessions/{sessionId}/prologue/complete";
        public const string MENU_GUIDES         = PrologueRoutes.MENU_GUIDES;
        public const string MENU_GUIDE_COMPLETE = "/v1/accounts/me/menu-guides/{menuId}/complete";
    }
}
