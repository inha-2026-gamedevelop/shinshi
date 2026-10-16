namespace Shinshi.Common
{
    public static partial class Constants
    {
        /// <summary> 친구, 귓속말, 길드의 송수신 주기와 메시지 상한 계약값 </summary>
        public static class Social
        {
            // 저장 상한(친구 50, 길드 30, 스냅샷 배열 길이)과 길드 이름 규칙, 이름 입력 상한은 백엔드와 함께 쓰는 SocialLimits,
            // GuildNameRules에 있다. 여기에는 게임 서버와 클라이언트만 쓰는 주기와 메시지 상한을 둔다

            /****************************************
            *                Fields
            ****************************************/

            // 요청과 스냅샷의 ID 문자열(캐릭터, 길드, 가입 ID) 길이 상한. UUID 문자열은 36자다
            public const int MAX_ID_LENGTH = 64;

            // 스냅샷과 알림의 표시 이름 길이 상한, 채팅 보낸 사람 이름 상한과 같다
            public const int MAX_DISPLAY_NAME_LENGTH = Chat.MAX_SENDER_LENGTH;

            // 서버가 연결마다 받는 관리 요청 창. 스냅샷 조회는 따로 2초에 1건이다
            public const int    REQUESTS_PER_WINDOW             = 2;
            public const double REQUEST_WINDOW_SECONDS          = 1.0;
            public const int    SNAPSHOT_REQUESTS_PER_WINDOW    = 1;
            public const double SNAPSHOT_REQUEST_WINDOW_SECONDS = 2.0;

            // 친구, 길드 창이 열려 있는 동안 스냅샷을 다시 받는 간격(초). 접속, 종료 밀어 주기 알림은 없다
            public const float SNAPSHOT_REFRESH_SECONDS = 30f;

            // 서버가 전달함을 꺼내는 간격(초)과 발신자별 채팅 대기열 줄 수(넘으면 RateLimited)
            public const double DELIVERY_PULL_INTERVAL_SECONDS = 1.0;
            public const int    MAX_QUEUED_CHAT_PER_SENDER     = 3;

            // 클라이언트 수신 확인을 묶어 보내는 간격(초)과 한 메시지의 ID 수. 한 메시지가 차면 간격을 기다리지 않는다
            public const double DELIVERY_ACK_INTERVAL_SECONDS = 0.5;
            public const int    MAX_ACK_IDS_PER_MESSAGE       = 64;

            // 서버가 연결마다 받는 수신 확인 메시지 수와 창(초), 넘는 메시지는 안내 없이 버린다
            public const int    ACK_MESSAGES_PER_WINDOW = 4;
            public const double ACK_WINDOW_SECONDS      = 1.0;

            // 서버가 연결마다 기억하는 "보냈고 확인 대기 중인 전달 ID" 수, 넘으면 오래된 것부터 표에서 뺀다(행은 DB에 남아 재전송된다)
            public const int MAX_PENDING_DELIVERIES_PER_CONNECTION = 256;

            // 클라이언트가 쓰기 요청 결과를 기다리는 시간(초). 넘으면 결과 불명으로 보고 스냅샷을 다시 받는다
            public const float REQUEST_TIMEOUT_SECONDS = 10f;
        }
    }
}
