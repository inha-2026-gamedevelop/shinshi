namespace Shinshi.Backend.Growth
{
    /// <summary> GrowthRoutes와 같은 경로의 ASP.NET Core 템플릿. 두 값이 어긋나지 않는지 테스트로 묶는다 </summary>
    public static class GrowthRouteTemplates
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string CHARACTER = "/v1/town/characters/{characterId}";
        private const string ITEM      = CHARACTER + "/equipment/{itemId}";

        public const string GROWTH               = CHARACTER + "/growth";
        public const string TALISMAN_REGISTER    = CHARACTER + "/talismans/register";
        public const string TALISMAN_UPGRADE     = CHARACTER + "/talismans/upgrade";
        public const string TALISMAN_LOADOUT     = CHARACTER + "/talismans/loadout";
        public const string TALISMAN_DISASSEMBLE = CHARACTER + "/talismans/disassemble";
        public const string TRAINING             = CHARACTER + "/training";
        public const string TRAINING_RESPEC      = CHARACTER + "/training/respec";
        public const string EQUIPMENT_CRAFT      = CHARACTER + "/equipment/craft";
        public const string EQUIPMENT_INHERIT    = CHARACTER + "/equipment/inherit";
        public const string EQUIPMENT_ENHANCE    = ITEM + "/enhance";
        public const string EQUIPMENT_EQUIP      = ITEM + "/equip";
        public const string EQUIPMENT_UNEQUIP    = ITEM + "/unequip";
        public const string EQUIPMENT_LOCK       = ITEM + "/lock";
        public const string EQUIPMENT_DISMANTLE  = ITEM + "/dismantle";

        public const string EQUIPMENT_REROLL_OPTIONS = ITEM + "/reroll-options";

        // GrowthRoutes.INTRO_COMPLETE_FORMAT
        public const string INTRO_COMPLETE = "/v1/town/accounts/{accountId}/progress/intro";
    }
}
