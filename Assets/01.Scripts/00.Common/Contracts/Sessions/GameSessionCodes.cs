namespace Shinshi.Contracts
{
    /// <summary> 출정 예약, 세션 이벤트, 서버 풀 DTO의 정수 코드, 기존 값을 바꾸거나 재배열하지 않는다 </summary>
    public static class GameSessionCodes
    {
        // TODO(민성): ContractCodes로 합칠지 정한다. 같은 이름 enum이 없는 Mode, EventKind, InstanceState는
        // 여기서 처음 정한 값이고, EndReason은 ESessionEndReason과 값이 같아야 한다

        /// <summary> 출정 종류, 보스전만 티켓 1장을 예약한다 </summary>
        public static class Mode
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int NORMAL = 0;
            public const int BOSS   = 1;
        }

        /// <summary> ESessionEndReason과 같은 값 </summary>
        public static class EndReason
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int SUCCESS            = 0;
            public const int TIMEOUT            = 1;
            public const int ALL_DEAD           = 2;
            public const int ABANDON            = 3;
            public const int DISCONNECT_EXPIRED = 4;
            public const int SERVICE_CANCELLED  = 5;
        }

        /// <summary> 던전 서버가 순번을 붙여 저장하는 세션 이벤트 종류 </summary>
        public static class EventKind
        {
            /****************************************
            *                Fields
            ****************************************/

            // 저장만 하고 잔액을 바꾸지 않는다. 지급은 정산안으로만 한다
            public const int LOOT            = 1;

            // 출정에 예약한 물약 1개를 소비한다. 회복은 이 이벤트의 확정 응답 뒤에 적용한다
            public const int POTION_USED     = 2;

            // 정산 전 추가 정기를 출정 원장에 쌓는다. 서비스 장애 취소의 지급 기준이다
            public const int ESSENCE_ACCRUED = 3;

            // 배치의 마지막에만 둔다. 이후 이 세션에는 이벤트를 받지 않는다
            public const int SESSION_ENDED   = 4;

            // 완료 웨이브의 기본 정기를 출정 원장에 쌓는다. 서비스 장애 취소는 이 합계를 전액 지급한다(사용자 결정 2026-10-01).
            // ESSENCE_ACCRUED에 구분 필드를 붙이지 않고 종류를 나눈 이유: 구분 필드가 없는 이전 서버의 적립이 기본값 0으로
            // 읽혀 지급률이 바뀌는 일을 막고, ESSENCE_ACCRUED(추가 정기, 장애 취소 50%)의 기존 의미를 그대로 둔다
            public const int BASE_ESSENCE_ACCRUED = 5;
        }

        /// <summary> 서버 풀 인스턴스 상태, Idle만 새 출정에 배정한다 </summary>
        public static class InstanceState
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int IDLE     = 0;
            public const int RESERVED = 1;
            public const int ACTIVE   = 2;
            public const int DRAINING = 3;
            public const int OFFLINE  = 4;
        }
    }
}
