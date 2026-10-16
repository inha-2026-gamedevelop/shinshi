namespace Shinshi.Contracts
{
    /// <summary> 소셜 저장 계약의 상한과 기한(2026-10-16 사용자 결정, 기획 근거 없는 초안 값). DB CHECK의 같은 숫자는 시험으로 묶는다 </summary>
    public static class SocialLimits
    {
        /****************************************
        *                Fields
        ****************************************/

        // 친구
        public const int MAX_FRIENDS                  = 50;
        public const int MAX_OUTGOING_FRIEND_REQUESTS = 20;
        public const int MAX_INCOMING_FRIEND_REQUESTS = 50;
        public const int FRIEND_REQUEST_DAYS          = 7;

        // 길드. 정원은 길드장 포함이며 guilds.member_count CHECK(30)가 마지막 방어선이다
        public const int MAX_GUILD_MEMBERS               = 30;
        public const int MAX_PENDING_APPLICATIONS        = 5;
        public const int MAX_PENDING_GUILD_INVITES       = 5;
        public const int MAX_GUILD_PENDING               = 50;
        public const int GUILD_REQUEST_DAYS              = 7;

        // 채팅 본문 길이. Constants.Chat.MAX_MESSAGE_LENGTH와 같다(API는 길이만 다시 본다)
        public const int MAX_CHAT_LENGTH = 100;

        // 이름 입력 상한. CharacterNameRules.MAX_INPUT_LENGTH와 같다
        public const int MAX_NAME_INPUT_LENGTH = CharacterNameRules.MAX_INPUT_LENGTH;

        // 전달함. 행은 기한 안에 클라이언트 수신 확인을 받으면 지운다. 꺼내거나 보낸 행은 재전송 간격만큼 다음 꺼내기에서 빠진다
        public const int DELIVERY_TTL_SECONDS                = 60;
        public const int DELIVERY_RESEND_SECONDS             = 5;
        public const int MAX_DELIVERIES_PER_PULL             = 200;
        public const int MAX_DELIVERIES_PER_RECIPIENT        = 20;
        public const int MAX_ACKS_PER_PULL                   = 1000;

        // 꺼내기 롱폴 대기 자리. 1차는 0만 받는다
        public const int PULL_WAIT_MS = 0;

        // 스냅샷 배열 상한. 직렬화 읽기 검사와 같은 값이다
        public const int MAX_SNAPSHOT_FRIENDS      = MAX_FRIENDS;
        public const int MAX_SNAPSHOT_REQUESTS     = MAX_INCOMING_FRIEND_REQUESTS;
        public const int MAX_SNAPSHOT_MEMBERS      = MAX_GUILD_MEMBERS;
        public const int MAX_SNAPSHOT_APPLICATIONS = MAX_GUILD_PENDING;
        public const int MAX_SNAPSHOT_INVITES      = MAX_PENDING_GUILD_INVITES;
    }
}
