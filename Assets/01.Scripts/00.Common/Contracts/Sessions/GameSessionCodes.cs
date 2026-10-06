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

            // 참가자 한 명이 포기하거나 접속 만료로 출정을 떠났다. participantId와 endReason(ABANDON, DISCONNECT_EXPIRED)을 쓴다.
            // API는 같은 트랜잭션에서 그 계정의 던전 접속 임대를 풀어 마을 복귀를 허용하고, 이후 복귀 토큰은 ALREADY_SETTLED로 거절한다.
            // 경제 정산은 하지 않는다. 떠난 참가자도 SESSION_ENDED 정산안에 한 번 포함된다
            public const int PARTICIPANT_DEPARTED = 6;

            // 확정된 POTION_USED 한 건을 되돌린다(회복을 적용하지 못한 경우). revertsEventId에 원 이벤트 ID를 싣고,
            // 원 이벤트가 같은 세션, 같은 참가자에 있고 아직 되돌리지 않았을 때만 저장된다. 물약 1개가 예약분으로 돌아간다
            public const int POTION_USE_REVERTED = 7;
        }

        /// <summary> 출정 물약 규칙의 단일 원본. 던전 서버(PotionServer)와 API(세션 이벤트 저장)가 같은 값을 쓰고,
        /// SessionConfigSO._bossPotionUseLimit가 이 값과 다르면 데이터 검증이 실패한다 </summary>
        public static class Potion
        {
            /****************************************
            *                Fields
            ****************************************/

            // 보스전 참가자 한 명의 물약 사용 상한(소형, 대형 합산). 되돌린 사용은 세지 않는다
            public const int BOSS_USE_LIMIT = 9;
        }

        /// <summary> EBossDifficulty와 같은 값. 출정 예약과 입장 교환의 difficulty, 보스전이 아니면 NONE </summary>
        public static class Difficulty
        {
            /****************************************
            *                Fields
            ****************************************/

            public const int NONE = -1;
            public const int EASY = 0;
            public const int HARD = 1;
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
