namespace Shinshi.Network
{
    /// <summary> 1:1 거래의 거리, 시간, 개수 제안값. 기획서에 거래 규칙이 없어 잠정이고, 확정되면 밸런스 데이터로 옮긴다 </summary>
    public static class TradeSettings
    {
        /****************************************
        *                Fields
        ****************************************/

        // 요청과 수락 때의 최대 거리(m)와 거래 중 이 거리를 넘으면 취소하는 거리(m)
        public const float  REQUEST_DISTANCE = 8f;
        public const float  CANCEL_DISTANCE  = 20f;

        // 양쪽이 확정한 뒤 수락이 열릴 때까지의 시간(초)
        public const double COUNTDOWN_SECONDS = 5.0;

        // 요청을 받은 쪽이 수락할 수 있는 시간(초)
        public const double REQUEST_EXPIRE_SECONDS = 30.0;

        // 제안이 바뀌거나 확정이 바뀌지 않은 채 이 시간이 지나면 거래를 취소한다(초)
        public const double IDLE_CANCEL_SECONDS = 300.0;

        // 거리와 출정 시작을 확인하는 간격(초)
        public const double CHECK_INTERVAL_SECONDS = 1.0;

        // 한 사람이 동시에 보낼 수 있는 요청 수와 마을 전체 요청 표 크기
        public const int    MAX_SENT_REQUESTS   = 3;
        public const int    REQUEST_TABLE_SIZE  = 256;

        // 한 쪽이 올릴 수 있는 품목 칸 수는 TradeCodes.TRADE_MAX_ITEMS를 쓴다

        // 결과를 모르는 prepare를 같은 operationId로 다시 보내는 간격(초)과 포기하는 시간(초, API 준비 유효 시간과 같다)
        public const double PREPARE_RETRY_SECONDS = 1.0;
        public const double PREPARE_GIVE_UP_SECONDS = 120.0;

        // commit 응답이 없을 때 resolve를 부르는 간격(초). 앞 세 번은 1, 2, 4초 뒤이고 이후에는 계속 5초 간격이다
        public const double RESOLVE_FIRST_SECONDS  = 1.0;
        public const double RESOLVE_SECOND_SECONDS = 2.0;
        public const double RESOLVE_THIRD_SECONDS  = 4.0;
        public const double RESOLVE_REPEAT_SECONDS = 5.0;

        // resolve가 연달아 이 횟수(약 2분) 실패하면 거래를 실패로 마친다
        public const int    RESOLVE_MAX_FAILURES = 24;

        // 같은 요청자가 같은 상대에게 거절당하거나 만료된 뒤 다시 요청하기까지의 시간(초)
        public const double REQUEST_COOLDOWN_SECONDS = 10.0;

        // 한 쪽이 올릴 수 있는 엽전 상한. 오입력과 오버플로 방지용 제안값이다
        public const long   MAX_CURRENCY = 1000000000000L;

        // 한 번 시도하는 API 호출 제한 시간은 Constants.Network.API_TIMEOUT_SECONDS를 쓴다
    }
}
