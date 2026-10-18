namespace Shinshi.Common
{
    public static partial class Constants
    {
        /// <summary> 채팅 송신, 수신, 표시 계약값 </summary>
        public static class Chat
        {
            /****************************************
            *                Fields
            ****************************************/

            // 본문 최대 길이(UTF-16 단위), HUD 입력 칸의 글자 수 제한과 같게 둔다
            public const int MAX_MESSAGE_LENGTH = 100;

            // 보낸 사람 이름 최대 길이(UTF-16 단위), 넘으면 자른다
            public const int MAX_SENDER_LENGTH = 24;

            // 연결 하나가 창 하나에 보낼 수 있는 채팅 수와 창 길이(초). 넘는 요청은 RateLimited로 거절한다
            public const int    MESSAGES_PER_WINDOW = 1;
            public const double SEND_WINDOW_SECONDS = 1.0;

            // 서버 전체가 창 하나에 방송할 수 있는 전체 채팅 수와 창 길이(초). 넘으면 발신자에게 RateLimited로 거절한다
            public const int    BROADCASTS_PER_WINDOW   = 20;
            public const double BROADCAST_WINDOW_SECONDS = 1.0;

            // 거절 안내를 다시 보내기까지 최소 간격(초), 변조 클라이언트가 안내를 폭주시키지 못하게 한다
            public const double REJECT_NOTICE_INTERVAL_SECONDS = 1.0;

            // 클라이언트가 보관하는 최근 줄 수
            public const int LOG_CAPACITY = 100;

            // 같은 시스템 안내를 이 시간(초) 안에 다시 받으면 한 줄로 합친다
            public const double SYSTEM_REPEAT_SECONDS = 1.0;

            // 게임 안 공지 조회 간격(초). 공지 노출은 분 단위라 이보다 촘촘히 읽을 이유가 없고, 실패해도 같은 간격으로 다시 읽는다
            public const float NOTICE_POLL_INTERVAL_SECONDS = 60f;

            // 중요도가 가장 높은 공지를 노출 중에 채팅에 다시 넣는 간격(초), 그 아래 중요도는 새 공지일 때 한 번만 넣는다
            public const double NOTICE_CRITICAL_REPEAT_SECONDS = 600.0;

            // 공지 한 줄의 최대 길이(UTF-16 단위), 넘으면 자르고 말줄임표를 붙인다. 채팅 본문 한도와 별개다
            public const int NOTICE_MAX_LENGTH = 200;

            // 클라이언트가 기억하는 최근 전달 ID 수. 같은 전달 ID의 귓속말, 길드 채팅, 소셜 알림이 다시 오면 버린다
            public const int DELIVERY_DEDUPE_CAPACITY = 256;
        }
    }
}
