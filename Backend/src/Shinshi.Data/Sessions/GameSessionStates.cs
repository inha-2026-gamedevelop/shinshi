namespace Shinshi.Data.Sessions
{
    /// <summary> 0004 마이그레이션의 CHECK 제약과 같은 DB 문자열 상태값 </summary>
    public static class GameSessionStates
    {
        /****************************************
        *                Fields
        ****************************************/

        // game_sessions.state, API가 쓰는 부분 집합만 둔다
        public const string RESERVED = "Reserved";
        public const string RUNNING  = "Running";
        public const string SETTLING = "Settling";
        public const string CLOSED   = "Closed";

        // game_sessions.result
        public const string RESULT_RELEASED          = "Released";
        public const string RESULT_EXPIRED           = "Expired";
        public const string RESULT_SETTLED           = "Settled";
        public const string RESULT_SERVICE_CANCELLED = "ServiceCancelled";

        // session_participants.state
        public const string PARTICIPANT_RESERVED = "Reserved";
        public const string PARTICIPANT_ACTIVE   = "Active";
        public const string PARTICIPANT_SETTLED  = "Settled";
        public const string PARTICIPANT_RELEASED = "Released";

        // game_sessions.mode
        public const string MODE_NORMAL = "Normal";
        public const string MODE_BOSS   = "Boss";

        // server_instances.state
        public const string INSTANCE_IDLE     = "Idle";
        public const string INSTANCE_RESERVED = "Reserved";
        public const string INSTANCE_ACTIVE   = "Active";
        public const string INSTANCE_DRAINING = "Draining";
        public const string INSTANCE_OFFLINE  = "Offline";
    }
}
