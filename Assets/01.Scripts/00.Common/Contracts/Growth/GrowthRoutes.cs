namespace Shinshi.Contracts
{
    /// <summary> 마을 서버가 부르는 성장 경로와 플레이어의 계정 진행 조회 경로 </summary>
    public static class GrowthRoutes
    {
        // 마을 경로는 마을 서버 임대 Bearer와 인스턴스 헤더가 필요하고, 그 캐릭터 계정의 접속 임대가 호출한 마을에 있어야 한다.
        // string.Format으로 {0}에 characterId, {1}에 itemId를 넣는다. POST 응답은 GrowthOperationResponse이며
        // 같은 operationId 재전송은 처음 결과와 그 시점의 성장 상태를 받는다

        /****************************************
        *                Fields
        ****************************************/

        // GET, CharacterGrowthSnapshot
        public const string GROWTH_FORMAT = "/v1/town/characters/{0}/growth";

        public const string TALISMAN_REGISTER_FORMAT    = "/v1/town/characters/{0}/talismans/register";
        public const string TALISMAN_UPGRADE_FORMAT     = "/v1/town/characters/{0}/talismans/upgrade";
        public const string TALISMAN_LOADOUT_FORMAT     = "/v1/town/characters/{0}/talismans/loadout";
        public const string TALISMAN_DISASSEMBLE_FORMAT = "/v1/town/characters/{0}/talismans/disassemble";

        public const string TRAINING_FORMAT        = "/v1/town/characters/{0}/training";
        public const string TRAINING_RESPEC_FORMAT = "/v1/town/characters/{0}/training/respec";

        public const string EQUIPMENT_CRAFT_FORMAT     = "/v1/town/characters/{0}/equipment/craft";
        public const string EQUIPMENT_INHERIT_FORMAT   = "/v1/town/characters/{0}/equipment/inherit";
        public const string EQUIPMENT_ENHANCE_FORMAT   = "/v1/town/characters/{0}/equipment/{1}/enhance";
        public const string EQUIPMENT_EQUIP_FORMAT     = "/v1/town/characters/{0}/equipment/{1}/equip";
        public const string EQUIPMENT_UNEQUIP_FORMAT   = "/v1/town/characters/{0}/equipment/{1}/unequip";
        public const string EQUIPMENT_LOCK_FORMAT      = "/v1/town/characters/{0}/equipment/{1}/lock";
        public const string EQUIPMENT_DISMANTLE_FORMAT = "/v1/town/characters/{0}/equipment/{1}/dismantle";

        // POST, IntroCompleteRequest -> OperationResult. 마을 서버 임대 Bearer가 필요하고 {0}의 계정 접속 임대가 호출한 마을에 있어야 한다.
        // 신시 안내 완료를 기록한다. 이미 완료한 계정도 확정이다
        public const string INTRO_COMPLETE_FORMAT = "/v1/town/accounts/{0}/progress/intro";

        // GET, 플레이어 접근 토큰 또는 게임 하위 세션. AccountProgressResponse
        public const string ACCOUNT_PROGRESS = "/v1/accounts/me/progress";
    }
}
