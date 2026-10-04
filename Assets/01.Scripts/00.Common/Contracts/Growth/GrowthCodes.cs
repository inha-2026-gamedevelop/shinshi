namespace Shinshi.Contracts
{
    /// <summary> 성장 DTO의 코드 값과 요청 한도 </summary>
    public static class GrowthCodes
    {
        /****************************************
        *                Fields
        ****************************************/

        // 확률 판정 입력. 마을 서버가 0 이상 BASIS_POINTS 미만을 뽑고, 성공률(만분율)보다 작으면 성공이다
        public const int BASIS_POINTS          = 10000;
        public const int MAX_ROLL_BASIS_POINTS = BASIS_POINTS - 1;

        // 한 캐릭터가 동시에 끼우는 부적 효과 수, 일반 기술 4개 x 3단계
        public const int MAX_LOADOUT_ENTRIES = 12;

        /// <summary> GrowthOperationResponse.outcome </summary>
        public static class Outcome
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int NONE      = 0;
            public const int SUCCEEDED = 1;
            public const int FAILED    = 2;
        }

        /// <summary> EquipmentView.origin, DB equipment_instances.origin과 같은 문자열 </summary>
        public static class EquipmentOrigin
        {
            /****************************************
            *                Fields
            ****************************************/

            public const string CRAFT         = "Craft";
            public const string INITIAL_GRANT = "InitialGrant";
            public const string TRIAL_GRANT   = "TrialGrant";
        }
    }
}
