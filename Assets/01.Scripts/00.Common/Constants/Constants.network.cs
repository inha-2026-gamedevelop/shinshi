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

            // 클라이언트와 서버 빌드가 같아야 접속할 수 있다.
            // 2: 플레이어 이름 SyncVar, 보스 예고 묶음 번호와 2페이즈 빔, 별형 빔, 반월 연쇄, 50% 결계 사건 추가(2026-10-03)
            // 3: 캐릭터 닉네임·외형 SyncVar와 공개 이동 속도 추가, 머리 위 이름을 닉네임 SyncVar로 합침(2026-10-06)
            // 4: 외형 v3(P09, 48바이트)와 기본 공격·회피 표현 시퀀스 SyncVar 추가(2026-10-06)
            // 5: 캐릭터 누적 경험치·성장 입장 계약과 플레이어 경험치 SyncVar 추가(2026-10-11)
            // 6: 파티 참가자 누적 경험치와 소유자 데미지 표시 TargetRpc 추가(2026-10-11)
            // 7: 무철, 월백 하드 2페이즈 전용 보스 사건과 복원 기록 추가, 투사체와 장판 시작 값에 시전 번호 추가(2026-10-13)
            // 8: 장비 옵션 필드와 재설정 누계 추가, 성장 요청 종류 장비 옵션 재설정 추가(2026-10-13)
            // 9: 궁수 속성, 화살 적중, 직업 스택, 기본 공격 콤보 단계와 공격, 이동 시작 시각 SyncVar 추가(2026-10-16)
            // 10: 소셜 메시지, 길드, 귓속말 채팅 채널 추가(2026-10-16)
            // 11: 보스 돌진 정지 서버 시각, 빔, 별, 추적 장판 취소의 시전 번호, 무철 돌진 유도 그로기 사건과 복원 추가(2026-10-17)
            // 12: 월백 체력 기믹 4종과 워프 출발 사건, 복원, 등장, 처치, 페이즈 세대 SyncVar, 격자 첫 줄 번호 추가(2026-10-17)
            // 13: 무철 돌진 구조물 배치, 파괴 사건과 복원 추가(2026-10-17)
            public const int PROTOCOL_VERSION = 13;

            // 복제 주기, 플레이어 복제 주기는 대역폭 측정으로 15Hz 또는 30Hz 중에 정한다
            public const int SERVER_TICK_RATE             = 30;
            public const int MIRROR_SEND_RATE             = SERVER_TICK_RATE;
            public const int INPUT_SEND_RATE_MAX          = 30;
            public const int PLAYER_REPLICATION_RATE      = 15;
            public const int ENEMY_REPLICATION_RATE       = 10;
            public const int TOWN_REMOTE_REPLICATION_RATE = 10;

            // 플레이어 주기는 위 한 값으로 15/30Hz를 고른다. NetworkTransform은 전역 간격의 정수배만 지원한다
            public const int PLAYER_SEND_INTERVAL_MULTIPLIER      = MIRROR_SEND_RATE / PLAYER_REPLICATION_RATE;
            public const int ENEMY_SEND_INTERVAL_MULTIPLIER       = MIRROR_SEND_RATE / ENEMY_REPLICATION_RATE;
            public const int TOWN_REMOTE_SEND_INTERVAL_MULTIPLIER = MIRROR_SEND_RATE / TOWN_REMOTE_REPLICATION_RATE;

            // 초, Mirror 전송과 플레이어, 적, 마을 원격 개체의 상태 복제 간격
            public const float MIRROR_SEND_INTERVAL_SECONDS      = 1f / MIRROR_SEND_RATE;
            public const float PLAYER_SYNC_INTERVAL_SECONDS      = MIRROR_SEND_INTERVAL_SECONDS * PLAYER_SEND_INTERVAL_MULTIPLIER;
            public const float ENEMY_SYNC_INTERVAL_SECONDS       = MIRROR_SEND_INTERVAL_SECONDS * ENEMY_SEND_INTERVAL_MULTIPLIER;
            public const float TOWN_REMOTE_SYNC_INTERVAL_SECONDS = MIRROR_SEND_INTERVAL_SECONDS * TOWN_REMOTE_SEND_INTERVAL_MULTIPLIER;

            // 보스 상태(위치, 방향, 체력, 흐름) 복제 주기. 돌진 중 몸체는 클라이언트가 돌진 사건 값으로 그리고 예고 방향은
            // 조준 사건이 틱마다 오므로 상태 값은 적과 같은 주기로 충분하다. 대역폭과 체감을 측정한 뒤 조정한다
            public const int   BOSS_REPLICATION_RATE         = ENEMY_REPLICATION_RATE;
            public const int   BOSS_SEND_INTERVAL_MULTIPLIER = MIRROR_SEND_RATE / BOSS_REPLICATION_RATE;
            public const float BOSS_SYNC_INTERVAL_SECONDS    = MIRROR_SEND_INTERVAL_SECONDS * BOSS_SEND_INTERVAL_MULTIPLIER;

            // 늦은 접속, 재접속 복원에 싣는 진행 중 투사체, 장판, 빔, 별형 빔 수의 상한. 서버 관리자의 동시 상한보다 크게 둔다
            public const int BOSS_RESTORE_MAX_PROJECTILES = 16;
            public const int BOSS_RESTORE_MAX_AREAS       = 16;
            public const int BOSS_RESTORE_MAX_BEAMS       = 16;
            public const int BOSS_RESTORE_MAX_STARS       = 8;

            // 전용 사건(무철, 월백 하드 2페이즈)이 한 메시지에 싣는 목록의 상한. 데이터의 실제 수(타 3, 원 13, 발 8)보다 크게 두고
            // 넘는 값은 프로토콜 불일치로 읽기를 멈춘다. 추적 장판은 파티 인원만큼 동시에 예고될 수 있다
            public const int BOSS_STRIKE_CHAIN_MAX_STEPS     = 8;
            public const int BOSS_CIRCLE_BURST_MAX_CIRCLES   = 16;
            public const int BOSS_BARRAGE_MAX_SHOTS          = 16;
            public const int BOSS_RESTORE_MAX_TRACKING_AREAS = 8;

            // 월백 체력 기믹 사건이 한 메시지에 싣는 목록의 상한. 파티 최대 4명 기준 실제 수(구조물 4, 결계 명단 4, 핵과 여우불 4,
            // 노란 여우불 4, 빨간 여우불 4)보다 크게 두고 넘는 값은 프로토콜 불일치로 읽기를 멈춘다.
            // 봉인 핵은 파괴 여부를 바이트 비트 마스크로 보내므로 8을 넘길 수 없다
            public const int BOSS_WOLBAEK_MAX_STRUCTURES = 8;
            public const int BOSS_WOLBAEK_MAX_MEMBERS    = 8;
            public const int BOSS_WOLBAEK_MAX_CORES      = 8;
            public const int BOSS_WOLBAEK_MAX_FOXFIRES   = 8;
            public const int BOSS_WOLBAEK_MAX_YELLOWS    = 16;
            public const int BOSS_WOLBAEK_MAX_REDS       = 8;

            // 무철 돌진 구조물 배치 복원이 싣는 구조물 수의 상한. 임시 배치(4)와 기믹 필요 수(3)보다 크게 두고
            // 넘는 배치는 서버가 싣지 않으며 받은 값이 넘으면 프로토콜 불일치로 읽기를 멈춘다
            public const int BOSS_CHARGE_STRUCTURE_MAX_COUNT = 16;

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

            // 초, 서버 임대를 만료 전에 다시 받는 여유. 임대 수명이 이 값의 두 배보다 짧으면 수명의 절반에서 다시 받는다
            public const double SERVER_LEASE_RENEW_MARGIN_SECONDS = 60.0;

            // 초, 임대 발급과 풀 등록이 일시 실패했을 때 다시 부르는 간격
            public const double SERVER_PRESENCE_RETRY_SECONDS = 5.0;

            // 초, API가 준 임대 수명을 이보다 길게 믿지 않는다. 시계 오류나 잘못된 응답으로 갱신이 끝없이 밀리지 않게 한다
            public const double MAX_SERVER_LEASE_SECONDS = 86400.0;

            // 서버 풀에 등록할 접속 주소. 클라이언트가 이 주소로 던전에 붙는다. 개발 실행은 없으면 루프백을 쓴다
            public const string ENV_PUBLIC_ADDRESS        = "PUBLIC_ADDRESS";
            public const string ARG_PUBLIC_ADDRESS        = "-public-address";
            public const string DEV_PUBLIC_ADDRESS        = "127.0.0.1";
            public const int    MAX_PUBLIC_ADDRESS_LENGTH = 253;

            // 마을 계정 접속 임대 해제. 연결이 끊길 때 한 번 보내고 일시 실패면 간격을 두고 몇 번 더 보낸다.
            // 끝내 실패해도 계정 임대는 heartbeat 만료로 풀린다(백엔드 계약)
            public const int    ACCOUNT_LEASE_RELEASE_MAX_ATTEMPTS  = 3;
            public const double ACCOUNT_LEASE_RELEASE_RETRY_SECONDS = 2.0;
            public const int    MAX_PENDING_ACCOUNT_LEASE_RELEASES  = 256;

            // 던전 이관을 기다리며 미뤄 둔 임대 해제 목록 상한. 가득 차면 미루지 않고 heartbeat 만료에 맡긴다.
            // 입장권에 만료 시각이 없을 때는 이 시간 뒤에 푼다
            public const int    MAX_DEFERRED_ACCOUNT_LEASE_RELEASES  = 256;
            public const double DEFERRED_RELEASE_NO_DEADLINE_SECONDS = 60.0;

            // 초, 참가자 접속 임대 이관이 일시 실패했을 때 같은 newLeaseId로 다시 부르는 간격
            public const double LEASE_TRANSFER_RETRY_SECONDS = 2.0;

            // 프로세스 종료 코드. supervisor는 코드와 무관하게 새 bootId로 다시 띄운다. 코드는 운영 기록에서 원인을 가르는 데 쓴다
            public const int EXIT_CODE_RECYCLED      = 0;
            public const int EXIT_CODE_INVARIANT     = 70;
            public const int EXIT_CODE_PRESENCE_LOST = 75;

            // 초, 출정이 끝난 뒤 참가자가 결과를 보고 스스로 나갈 시간. 지나면 남은 연결을 서버가 끊는다
            public const double RECYCLE_RETURN_GRACE_SECONDS = 30.0;

            // 초, 장애나 거절로 끝난 출정은 기다릴 결과 화면이 없어 짧게 준다
            public const double RECYCLE_FAULT_GRACE_SECONDS = 3.0;

            // 초, 연결을 끊은 뒤 끊김 통지가 전송 배치에 실려 나갈 시간
            public const double RECYCLE_DISCONNECT_FLUSH_SECONDS = 1.0;

            // 초, 드레인 응답을 기다리는 상한. 응답이 없어도 종료한다(heartbeat 부재로 API가 같은 결론을 낸다)
            public const double RECYCLE_DRAIN_WAIT_SECONDS = 5.0;

            // API 호출은 같은 operationId로 재시도한다
            public const float API_TIMEOUT_SECONDS = 3f;
            public static readonly float[] G_ApiRetryDelaysSeconds = { 1f, 2f, 4f };

            // 초, 진행 중인 출정의 진단용 체크포인트 전송 주기
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
            public const string ARG_ROLE              = "-role";
            public const string ARG_PORT              = "-port";
            public const string ARG_INSTANCE          = "-instance";
            public const string ARG_BACKEND_BASE_URL  = "-backend-base-url";
            public const string ARG_CONTENT_VERSION   = "-content-version";
            public const string ARG_DUNGEON_POOL_SIZE = "-dungeon-pool-size";

            // 보스전 계약이 없는 출정(개발)에서 던전 서버가 여는 보스 정의 ID. 계약이 있으면 계약 보스를 쓴다.
            // 없으면 Constants.Combat.DEV_BOSS_ID이고 보스 카탈로그에 있는 ID만 받는다
            public const string ENV_BOSS_ID = "BOSS_ID";
            public const string ARG_BOSS_ID = "-boss-id";

            // 보스 체력 기믹을 켠다(환경 변수는 1이면 켬, 인자는 값 없이 있으면 켬). 기믹 표시가 없는 동안 기본은 꺼짐이다
            public const string ENV_BOSS_GIMMICKS = "BOSS_GIMMICKS";
            public const string ARG_BOSS_GIMMICKS = "-boss-gimmicks";

            // Prometheus가 긁어 가는 지표 내보내기. 포트가 없거나 0이면 끈다. 수신 주소 기본값은 루프백이며
            // 수집기가 다른 호스트에 있을 때만 내부망 주소를 준다. 게임 포트와 달리 외부에 열지 않는다
            public const string ENV_METRICS_PORT             = "METRICS_PORT";
            public const string ARG_METRICS_PORT             = "-metrics-port";
            public const string ENV_METRICS_BIND_ADDRESS     = "METRICS_BIND_ADDRESS";
            public const string ARG_METRICS_BIND_ADDRESS     = "-metrics-bind";
            public const string METRICS_DEFAULT_BIND_ADDRESS = "127.0.0.1";

            // 메시지 종류별 대역폭을 켠다(1이면 켬). 보낼 때마다 메시지를 박싱해 할당이 생기므로 프로파일링할 때만 켠다
            public const string ENV_METRICS_MESSAGE_DETAIL = "METRICS_MESSAGE_DETAIL";
            public const string ARG_METRICS_MESSAGE_DETAIL = "-metrics-message-detail";

            // 개발 클라이언트의 자동 접속 주소, 포트는 ARG_PORT를 함께 쓴다
            public const string ARG_CONNECT      = "-connect";
            public const string ARG_CONNECT_ROLE = "-connect-role";

            // 개발 빌드 전용 시험 인자. 이 프로세스가 보내는 메시지에 넣을 지연(ms)과 소유 플레이어 자동 조작.
            // 왕복 지연을 재려면 서버와 클라이언트에 같은 값(왕복의 절반)을 준다
            public const string ARG_DEV_LATENCY_MS = "-dev-latency-ms";
            public const string ARG_DEV_AUTOPILOT  = "-dev-autopilot";

            // 개발 클라이언트가 보낼 접속 토큰. 없으면 개발 빌드는 프로세스마다 임의 개발 토큰을 만든다
            public const string ARG_JOIN_TOKEN = "-join-token";

            // 연결 뒤 이 시간 안에 인증 요청과 토큰 검증이 끝나지 않으면 끊는다
            public const double AUTH_TIMEOUT_SECONDS = 5.0;

            // 연결 뒤 인증 요청이 이 시간 안에 오지 않으면 끊는다. 정상 클라이언트는 연결 직후 바로 보낸다.
            // 요청을 받은 뒤의 토큰 검증 마감은 AUTH_TIMEOUT_SECONDS를 따로 준다
            public const double AUTH_REQUEST_TIMEOUT_SECONDS = 1.0;

            // 한 연결 주소가 동시에 들고 있을 수 있는 인증 대기 연결 수. NAT 뒤 여러 사람을 고려해 1보다 크게 둔다.
            // 주소는 IPv4 그대로, IPv4-mapped IPv6는 IPv4로, 그 밖의 IPv6는 /64 접두사로 묶어 센다
            public const int MAX_PENDING_AUTH_PER_ADDRESS = 4;

            // 던전의 주소별 인증 대기 상한. 받을 사람이 파티 인원뿐이라 한 주소가 대기 자리를 나눠 갖지 못하게 더 좁힌다
            public const int DUNGEON_MAX_PENDING_AUTH_PER_ADDRESS = 2;

            // 던전의 인증 대기 상한은 파티 최대 인원에 이 배수를 곱한다. 재시도와 늦은 끊김 감지 몫을 남긴다
            public const int DUNGEON_PENDING_AUTH_PER_PARTICIPANT = 2;

            // 거절 응답이 전송 배치에 실려 나갈 시간을 준 뒤 끊는다. 바로 끊으면 응답이 버려진다
            public const double AUTH_REJECT_DISCONNECT_DELAY_SECONDS = 0.5;

            // 인증 대기 중인 연결 상한. 넘으면 요청을 기다리지 않고 바로 끊어 인증 전 연결로 메모리를 채우지 못하게 한다.
            // 서버 매니저의 Mirror maxConnections(100)보다 크게 두지 않는다. 연결이 그 수를 넘으면 Mirror가 인증기에 닿기 전에
            // 사유 없이 끊으므로, 이보다 큰 대기 상한은 쓸모없이 메모리만 잡는다
            public const int MAX_PENDING_AUTH_CONNECTIONS = 100;

            // 출정 입장권 만료 뒤에도 던전 이관을 기다려 주는 여유(초). 이 시각까지 마을 연결이 끊겨도 임대를 풀지 않는다.
            // 그 뒤에도 이관이 없으면 입장권은 이미 쓸 수 없으므로 마을 끊김이 임대를 푼다
            public const double DEPARTING_TRANSFER_GRACE_SECONDS = 10.0;

            // 인증 요청 문자열 상한(UTF-16 글자 수). 넘으면 검증기에 넘기지 않고 거절한다
            public const int MAX_JOIN_TOKEN_LENGTH = 512;

            // SHA256 소문자 16진수 콘텐츠 해시 길이
            public const int CONTENT_HASH_LENGTH = 64;

            // 개발 빌드, 에디터 전용 토큰 형식 "dev-{참가자 키}". 키는 영문, 숫자, '-', '_'만 쓴다
            public const string DEV_JOIN_TOKEN_PREFIX          = "dev-";
            public const int    DEV_PARTICIPANT_KEY_MAX_LENGTH = 32;

            // 서버가 백엔드를 부를 때 쓰는 서비스 자격. 실행 인자는 프로세스 목록에 보이므로 값 대신 파일 경로만 받는다
            public const string ENV_BACKEND_SERVICE_TOKEN        = "BACKEND_SERVICE_TOKEN";
            public const string ARG_BACKEND_SERVICE_TOKEN_FILE   = "-backend-service-token-file";
            public const int    MAX_BACKEND_SERVICE_TOKEN_LENGTH = 4096;

            // 접속 토큰 검증기 선택. 출시 빌드는 api만 받고, 에디터와 개발 빌드는 지정하지 않으면 dev다
            public const string ENV_JOIN_TOKEN_VERIFIER = "JOIN_TOKEN_VERIFIER";
            public const string ARG_JOIN_TOKEN_VERIFIER = "-join-verifier";
            public const string JOIN_TOKEN_VERIFIER_DEV = "dev";
            public const string JOIN_TOKEN_VERIFIER_API = "api";

            // 연속 일시 실패가 이 횟수에 닿으면 열린 시간 동안 호출하지 않고 바로 실패시킨다. 그 뒤 한 건만 시험한다
            public const int    BACKEND_CIRCUIT_FAILURE_THRESHOLD = 5;
            public const double BACKEND_CIRCUIT_OPEN_SECONDS      = 10.0;

            // 응답 대기 중인 요청 상한과 프레임당 전달 상한. 백엔드가 느려져도 서버 틱과 메모리가 함께 밀리지 않게 한다
            public const int MAX_BACKEND_IN_FLIGHT_REQUESTS   = 256;
            public const int MAX_BACKEND_COMPLETIONS_PER_PUMP = 64;

            // 초, 429, 503의 Retry-After를 이 값까지만 따른다. 게이트웨이는 Retry-After가 오면 안에서 다시 보내지 않고
            // 호출자가 이 시간 뒤로 다시 부른다
            public const double MAX_BACKEND_RETRY_AFTER_SECONDS = 300.0;

            // 응답 본문 상한(바이트)과 요청 하나에 줄 수 있는 시간 제한 상한
            public const int   MAX_BACKEND_RESPONSE_BYTES  = 1024 * 1024;
            public const float MAX_BACKEND_TIMEOUT_SECONDS = 30f;

            // 접속 토큰 교환. 인증 마감 안에 한 번만 시도한다(교환은 1회용이라 재시도하면 이미 쓴 토큰이 된다)
            public const float  JOIN_TOKEN_EXCHANGE_TIMEOUT_SECONDS = API_TIMEOUT_SECONDS;
            public const double JOIN_TOKEN_VERIFY_DEADLINE_SECONDS  = AUTH_TIMEOUT_SECONDS;

            // 교환에 성공했지만 입장이 거절된 토큰(이미 접속 중)을 같은 서버가 다시 받아 주는 시간과 기억 상한.
            // 클라이언트 재시도 전체(간격 x 횟수 + 접속 시간)보다 길고 토큰 수명보다 짧아야 한다
            public const double JOIN_EXCHANGE_REUSE_SECONDS     = 20.0;
            public const int    MAX_JOIN_EXCHANGE_REUSE_ENTRIES = MAX_PENDING_AUTH_CONNECTIONS;

            // 교환 응답의 캐릭터, 참가자, 출정, 인스턴스 ID 길이 상한(UUID 36자를 넉넉히 덮는다)
            public const int MAX_JOIN_CLAIM_ID_LENGTH = 64;

            // 참가자 키 이름공간. 던전은 출정 참가자 ID, 마을은 캐릭터 ID로 키를 만들고 접두사로 서로 겹치지 않게 한다.
            // 개발 토큰 키는 ':'를 쓸 수 없어 이 두 이름공간과도 겹치지 않는다
            public const string PARTICIPANT_KEY_DUNGEON_PREFIX = "p:";
            public const string PARTICIPANT_KEY_TOWN_PREFIX    = "c:";

            // 이전 연결의 끊김을 서버가 아직 모르면 이미 접속 중으로 거절된다. KCP 기본 Timeout 10초를 넘길 때까지
            // 같은 토큰으로 다시 접속한다(2초 x 6회 = 12초)
            public const float JOIN_ALREADY_CONNECTED_RETRY_INTERVAL_SECONDS = 2f;
            public const int   JOIN_ALREADY_CONNECTED_MAX_RETRIES            = 6;

            // KCP 기본 포트, 던전 풀은 인자로 프로세스마다 다른 포트를 준다
            public const ushort DEFAULT_GAME_PORT = 7777;

            // 에디터에서 서버 역할로 플레이할 때 메뉴가 넣고 서버 부트스트랩이 읽은 뒤 지운다
            public const string EDITOR_SERVER_ROLE_KEY = "Shinshi.EditorServerRole";
        }
    }
}
