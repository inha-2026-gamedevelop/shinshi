namespace Shinshi.Common
{
    public static partial class Constants
    {
        /// <summary> 통신, 틱, 토큰, 서버 실행 설정의 계약값, 초기값은 측정 후 조정한다 </summary>
        public static class Network
        {
            /****************************************
            *                Fields
            ****************************************/

            // 클라이언트와 서버 빌드가 같아야 접속할 수 있다
            public const int PROTOCOL_VERSION = 1;

            // 복제 주기, 플레이어 복제 주기는 대역폭 측정으로 15Hz 또는 30Hz 중에 정한다
            public const int SERVER_TICK_RATE             = 30;
            public const int INPUT_SEND_RATE_MAX          = 30;
            public const int ENEMY_REPLICATION_RATE       = 10;
            public const int TOWN_REMOTE_REPLICATION_RATE = 10;

            // 서버 시각 기준으로 입력을 받아 주는 창
            public const float INPUT_PAST_TOLERANCE_SECONDS   = 0.15f;
            public const float INPUT_FUTURE_TOLERANCE_SECONDS = 0.05f;

            // TODO(민성): 입력 주기 초과 위반 횟수의 연결 종료 임계값

            // 일회용 접속 토큰은 한 번만 교환하고, 로딩 제한과 별개로 만료된다
            public const float JOIN_TOKEN_LIFETIME_SECONDS       = 60f;
            public const float LOADING_TIMEOUT_SECONDS           = 120f;
            public const float RESERVATION_EXPIRE_MARGIN_SECONDS = 30f;

            // 서버 임대 heartbeat, 연속 부재 시 신규 배정을 멈춘다
            public const float SERVER_HEARTBEAT_INTERVAL_SECONDS = 5f;
            public const float SERVER_HEARTBEAT_TIMEOUT_SECONDS  = 15f;

            // API 호출은 같은 operationId로 재시도한다
            public const float API_TIMEOUT_SECONDS = 3f;
            public static readonly float[] G_ApiRetryDelaysSeconds = { 1f, 2f, 4f };

            public const float CHECKPOINT_INTERVAL_SECONDS = 30f;

            // 서버 빌드 하나가 실행 인자로 역할을 고른다
            public const string SERVER_ROLE_TOWN    = "town";
            public const string SERVER_ROLE_DUNGEON = "dungeon";

            // 외부 설정으로 주입하는 값의 이름, 구현 계약에서 확정하기 전까지 임시
            public const string ENV_SERVER_ROLE       = "SERVER_ROLE";
            public const string ENV_GAME_PORT         = "GAME_PORT";
            public const string ENV_BACKEND_BASE_URL  = "BACKEND_BASE_URL";
            public const string ENV_CONTENT_VERSION   = "CONTENT_VERSION";
            public const string ENV_INSTANCE_ID       = "INSTANCE_ID";
            public const string ENV_DUNGEON_POOL_SIZE = "DUNGEON_POOL_SIZE";

            // 실행 인자가 환경 변수보다 우선한다. 예: -role dungeon -port 7780 -instance dungeon-01
            public const string ARG_ROLE     = "-role";
            public const string ARG_PORT     = "-port";
            public const string ARG_INSTANCE = "-instance";

            // KCP 기본 포트, 던전 풀은 인자로 프로세스마다 다른 포트를 준다
            public const ushort DEFAULT_GAME_PORT = 7777;

            // 에디터에서 서버 역할로 플레이할 때 메뉴가 넣고 서버 부트스트랩이 읽은 뒤 지운다
            public const string EDITOR_SERVER_ROLE_KEY = "Shinshi.EditorServerRole";
        }
    }
}
