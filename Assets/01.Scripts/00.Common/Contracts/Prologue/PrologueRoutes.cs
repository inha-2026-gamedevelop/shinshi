namespace Shinshi.Contracts
{
    /// <summary> 프롤로그(캐릭터 생성 직후 1인 튜토리얼 출정)와 성장 메뉴 안내 완료의 API 경로 </summary>
    public static class PrologueRoutes
    {
        // 플레이어 경로는 게임 세션 토큰을 쓴다. 서버 경로는 던전 서버 임대 Bearer와 인스턴스 헤더가 필요하고,
        // {0}의 세션이 그 던전 인스턴스에 배정된 프롤로그 출정이어야 한다

        /****************************************
        *                Fields
        ****************************************/

        // GET, string.Format으로 characterId를 넣는다. PrologueStateResponse. 로비에서 선택창을 띄울지 정하는 데 쓴다
        public const string STATE_FORMAT = "/v1/characters/{0}/prologue";

        // POST, PrologueEntryRequest -> JoinTicket(던전 role). 마을을 거치지 않고 프롤로그 던전으로 바로 들어가는 입장권을 받는다.
        // 캐릭터 단계가 완료가 아닐 때만 발급한다
        public const string ENTRY = "/v1/prologue/entry";

        // POST, PrologueSkipRequest -> OperationResult. 이미 프롤로그를 마친 계정이 건너뛰고 마을로 간다.
        // 완료 기록과 기본 지급품은 직접 완료와 같다. 계정의 첫 프롤로그는 건너뛸 수 없다(PROLOGUE_REQUIRED)
        public const string SKIP = "/v1/prologue/skip";

        // POST, PrologueStageRequest -> OperationResult. 던전 서버가 구간이 바뀔 때마다 캐릭터의 저장 단계를 갱신한다
        public const string STAGE_FORMAT = "/v1/game-sessions/{0}/prologue/stage";

        // POST, PrologueCompleteRequest -> OperationResult. 던전 서버가 마무리 구간이 끝났을 때 완료와 기본 지급품을 기록한다.
        // 같은 캐릭터를 다시 보내도 지급은 한 번이다
        public const string COMPLETE_FORMAT = "/v1/game-sessions/{0}/prologue/complete";

        // GET, MenuGuideListResponse. 계정이 마친 성장 메뉴 안내 ID 목록
        public const string MENU_GUIDES = "/v1/accounts/me/menu-guides";

        // POST(본문 없음), string.Format으로 menuId를 넣는다. MenuGuideListResponse. 같은 메뉴를 다시 보내도 같은 결과다
        public const string MENU_GUIDE_COMPLETE_FORMAT = "/v1/accounts/me/menu-guides/{0}/complete";
    }
}
