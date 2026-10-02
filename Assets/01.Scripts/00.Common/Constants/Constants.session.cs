namespace Shinshi.Common
{
    public static partial class Constants
    {
        /// <summary> 출정 인원처럼 세션 규칙의 뼈대가 되는 계약값 </summary>
        public static class Session
        {
            /****************************************
            *                Fields
            ****************************************/

            // 시작 인원별 난이도 표의 배열 길이와 같아야 한다
            public const int MIN_PARTY_SIZE = 1;
            public const int MAX_PARTY_SIZE = 4;

            // 재접속 유예 중 보존하는 참가자 상태 수. 마을 목표 인원에 여유를 둔다. 넘으면 가장 먼저 만료될 기록을 지운다
            public const int MAX_RECONNECT_RECORDS = 128;

            // 한 프로세스가 기억하는 참가자 키 수. 넘으면 새 참가자를 받지 않는다
            public const int MAX_SESSION_PARTICIPANTS = 256;

            // 던전당 미확인 이벤트 수와 한 전송의 이벤트 수, 전송 중인 이벤트도 큐 상한에 포함한다
            public const int EVENT_QUEUE_CAPACITY = 1024;
            public const int EVENT_BATCH_CAPACITY = 128;

            // 초, 저장 배치 주기와 실패 재시도의 지수 백오프 범위
            public const double EVENT_BATCH_INTERVAL_SECONDS         = 1.0;
            public const double EVENT_RETRY_INITIAL_SECONDS          = 1.0;
            public const double EVENT_RETRY_MAX_SECONDS              = 30.0;
            public const double EVENT_QUEUE_WARNING_INTERVAL_SECONDS = 30.0;

            // 초, settlement 호출이 게이트웨이 재시도까지 실패했을 때 같은 operationId로 다시 부르는 간격의 시작과 상한.
            // 횟수 상한은 없다. 확정 거절(Terminal)일 때만 멈추고, 그 뒤는 API 복구 작업이 같은 operationId로 끝낸다
            public const double SETTLEMENT_RETRY_INITIAL_SECONDS = 2.0;
            public const double SETTLEMENT_RETRY_MAX_SECONDS     = 30.0;

            // 출정별 드롭 비밀 salt 길이(바이트). start 확정 때 만들고 프로세스 밖으로 내보내지 않는다
            public const int LOOT_SALT_BYTES = 32;

            // 초, start 호출이 일시 실패했을 때 같은 준비 집합으로 다시 부르는 간격
            public const double START_RETRY_SECONDS = 2.0;

            // 실패, 포기, 유예 만료 정산에서 남기는 추가 정기 비율(%)과 분모. 남는 값은 내림한다(101 -> 50).
            // TODO(민성): 기획 문서에 있는 값이지만 SessionConfigSO에 필드가 없어 여기 둔다. SO 필드를 만들면 옮긴다
            public const long FAILURE_EXTRA_ESSENCE_PERCENT = 50;
            public const long SUCCESS_EXTRA_ESSENCE_PERCENT = 100;
            public const long ESSENCE_PERCENT_BASE          = 100;

            // 백엔드 정산 상한(SettlementSanityLimits, SessionEventValidator)과 같은 값이어야 한다.
            // 넘는 정산안이나 적립은 API가 배치 전체를 확정 거절하고, 그러면 저장 큐가 멈춰 출정 전체가 장애 취소로 끝난다
            public const long   SETTLEMENT_MAX_SESSION_ESSENCE      = 100000; // 정기, 참가자 한 명의 기본, 추가 적립 합계와 정산 정기 각각
            public const long   SETTLEMENT_MAX_ITEM_QUANTITY        = 999;    // 개, 정산안 한 줄과 같은 아이템 합계
            public const int    SETTLEMENT_MAX_GRANT_ITEMS          = 64;     // 줄, 참가자 한 명의 정산 아이템 줄 수
            public const int    SETTLEMENT_MAX_DEFINITION_ID_LENGTH = 64;
            public const string SETTLEMENT_ITEM_DEFINITION_PREFIX   = "item_";

            // 정산 아이템 줄로 지급하면 지갑 잔액 경로를 우회하므로 API가 아이템 줄에서만 거절하는 ID. 정기, 정련편, 티켓, 엽전이다.
            // 정련편, 티켓, 엽전은 정산안의 지갑 칸(refineShards, tickets, currency)으로 지급한다
            public const string SETTLEMENT_FORBIDDEN_ESSENCE_ID  = "item_essence";
            public const string SETTLEMENT_FORBIDDEN_SHARD_ID    = "item_refine_shard";
            public const string SETTLEMENT_FORBIDDEN_TICKET_ID   = "item_boss_ticket";
            public const string SETTLEMENT_FORBIDDEN_CURRENCY_ID = "item_yeopjeon";

            // 정산 아이템과 부적의 귀속 종류. 사용자 결정(2026-10-01)으로 계정 귀속만 받는다
            public const string SETTLEMENT_ITEM_BINDING_KIND = "Account";

            // 정산안 지갑 칸과 부적 칸의 상한, 백엔드 SettlementSanityLimits와 같은 값이어야 한다.
            // TODO(기획 미정): 드롭 표 최대값(정련편 6웨이브 120, 티켓 처치당 최대 1장 180, 엽전 월백 하드 260)에 여유를 둔 임시값이다
            public const long SETTLEMENT_MAX_SESSION_SHARDS    = 999;  // 개, 참가자 한 명의 정산 정련편
            public const long SETTLEMENT_MAX_SESSION_TICKETS   = 999;  // 장, 참가자 한 명의 정산 티켓
            public const long SETTLEMENT_MAX_SESSION_CURRENCY  = 9999; // 엽전, 참가자 한 명의 정산 엽전
            public const long SETTLEMENT_MAX_TALISMAN_QUANTITY = 999;  // 장, 부적 효과 하나의 합계
            public const int  SETTLEMENT_MAX_GRANT_TALISMANS   = 64;   // 줄, 참가자 한 명의 부적 효과 종류 수
        }
    }
}
