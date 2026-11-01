namespace Shinshi.Common
{
    public static partial class Constants
    {
        /// <summary> 마을 서버의 파티, 성장 중계, 생활 요청 계약값 </summary>
        public static class Town
        {
            /****************************************
            *                Fields
            ****************************************/

            // 연결 하나가 1초에 보낼 수 있는 요청 수, 파티, 성장, 생활을 따로 센다. 넘는 요청은 RateLimited로 거절한다
            public const int    REQUESTS_PER_SECOND    = 5;
            public const double REQUEST_WINDOW_SECONDS = 1.0;

            // 경매장 조회(검색, 등록 상세, 내 등록, 기록)는 쓰기와 따로 센다. 창을 열면 내 등록과 검색이 나가고 곧바로 다른 검색과
            // 상세가 이어지므로 쓰기 한도와 함께 세면 정상 사용도 막힌다. 조회는 종류마다 하나씩만 진행되어 API 부하는 동시 진행 수로도 묶인다
            public const int    MARKET_READS_PER_SECOND = 10;

            // 마을 프로세스가 동시에 여는 방 수와 방 목록 한 번에 보내는 방 수. 넘으면 새 방을 만들지 않는다
            public const int MAX_PARTY_ROOMS  = 256;
            public const int MAX_LISTED_ROOMS = 64;

            // 방 이름과 비밀번호 길이, 클라이언트 입력을 그대로 담으므로 상한으로 자른 뒤 거절한다
            public const int MAX_ROOM_NAME_LENGTH     = 24;
            public const int MAX_ROOM_PASSWORD_LENGTH = 16;

            // 직접 초대가 유효한 시간(초)과 동시에 들고 있는 초대 수
            public const double INVITE_EXPIRE_SECONDS = 30.0;
            public const int    MAX_PENDING_INVITES   = 512;

            // 한 초대자가 동시에 들고 있는 초대 수. 한 명이 전체 초대 표(MAX_PENDING_INVITES)를 채워 다른 사람의 초대를 막지 못하게 한다.
            // TODO(민성): 기획 수치 미정(협동 기획에 초대 상한이 정해져 있지 않다). 파티 빈자리 3석에 거절 몇 번을 더한 임시값이다
            public const int    MAX_INVITES_PER_INVITER = 8;

            // 던전 풀이 비었을 때(NO_CAPACITY) 대기열 맨 앞 방이 새 operationId로 다시 예약하는 간격(초)
            public const double SORTIE_QUEUE_RETRY_SECONDS = 5.0;

            // 예약 응답을 받지 못한 채 끝난 요청(시간 초과, 네트워크 실패)을 같은 operationId로 다시 보내는 간격과 횟수
            public const double SORTIE_RETRY_SECONDS  = 2.0;
            public const int    SORTIE_MAX_RETRIES    = 3;

            // 참가자 한 명이 출정에 지참할 수 있는 물약 종류별 상한, 완제품 스택 상한과 같다
            public const int MAX_POTION_CARRY = 999;

            // 성장 요청 문자열 인자(효과, 정의, 장비 ID) 길이 상한, API 검증 상한과 같다
            public const int MAX_GROWTH_ID_LENGTH = 64;

            // 성장 요청의 수련 재분배 배열 길이(수련 항목 수)와 부적 조합 칸 상한
            public const int TRAINING_STAT_COUNT      = 5;
            public const int MAX_GROWTH_LOADOUT_SLOTS = 12;

            // 같은 operationId 재전송에 같은 roll을 쓰려고 연결마다 기억하는 최근 요청 수
            public const int GROWTH_ROLL_MEMORY = 16;

            // 물약 제작 한 번의 최대 횟수, 완제품 스택 상한 안이다
            public const int MAX_POTION_CRAFT_COUNT = 999;

            // 마을 프로세스가 상태를 들고 있는 채집 노드 수
            public const int MAX_GATHER_NODES = 256;

            // 채집 선점 뒤 이 거리(m) 넘게 움직이면 취소한다. 서버 위치 보간의 떨림은 취소로 보지 않는다
            public const float GATHER_MOVE_CANCEL_DISTANCE = 0.3f;

            // 결과를 받지 못한 채집 완료 저장을 같은 operationId로 다시 보내는 간격(초)
            public const double GATHER_RETRY_SECONDS = 2.0;

            // 마을 관심 영역(AOI), 접속자마다 시야 반경 안에서 가까운 순으로 AOI_MAX_VISIBLE_PLAYERS명까지만 보인다.
            // 본인과 파티원은 반경과 상한에 상관없이 항상 보이고, 상한은 이 둘을 포함한 총원이다.
            // 셀 크기는 반경 이상이어야 3x3 셀 조회가 반경 전체를 덮는다
            // TODO(민성): 기획 수치 미정. 시야 반경, 상한, 갱신 주기는 부하 측정 뒤 정한다
            public const float  AOI_VIEW_RADIUS      = 40f;
            public const float  AOI_CELL_SIZE        = 40f;
            public const int    AOI_MAX_VISIBLE      = 30;
            public const double AOI_REFRESH_SECONDS  = 0.5;

            // 시야 풀이의 후보 정렬 버퍼 초기 용량. 모자라면 두 배로 늘린다
            public const int    AOI_SOLVER_INITIAL_CANDIDATES = 64;
        }
    }
}
