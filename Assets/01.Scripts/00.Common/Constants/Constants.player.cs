namespace Shinshi.Common
{
    public static partial class Constants
    {
        /// <summary> 입력 이름, 조작 슬롯 개수, 플레이어 입력 계약값 </summary>
        public static class Player
        {
            /****************************************
            *                Fields
            ****************************************/

            // 입력은 구 Input Manager(UnityEngine.Input)를 쓴다. 축, 버튼 이름은 프로젝트 입력 설정에 있는 것만 둔다
            // TODO(민성): 물약 1/2 키를 입력 설정과 같이 넣는다

            // Input.GetMouseButton 번호. 기획 기본 배치는 오른쪽 이동, 왼쪽 공격이다.
            // 지점 기술 지정 중에는 왼쪽이 확정, 오른쪽이 취소다
            // TODO(민성): 마우스 버튼 교환 옵션이 생기면 설정값으로 바꾼다
            public const int MOUSE_BUTTON_ATTACK = 0;
            public const int MOUSE_BUTTON_MOVE   = 1;

            // TODO(민성): 키 변경 옵션이 생기면 InputManager 버튼이나 설정값으로 옮긴다
            public const UnityEngine.KeyCode KEY_DODGE = UnityEngine.KeyCode.Space;

            // 일반 기술 Q/W/E/R, 신령사 강림 Z/X/C, 지점 지정 취소
            public const UnityEngine.KeyCode KEY_SKILL_Q          = UnityEngine.KeyCode.Q;
            public const UnityEngine.KeyCode KEY_SKILL_W          = UnityEngine.KeyCode.W;
            public const UnityEngine.KeyCode KEY_SKILL_E          = UnityEngine.KeyCode.E;
            public const UnityEngine.KeyCode KEY_SKILL_R          = UnityEngine.KeyCode.R;
            public const UnityEngine.KeyCode KEY_ASCENSION_Z      = UnityEngine.KeyCode.Z;
            public const UnityEngine.KeyCode KEY_ASCENSION_X      = UnityEngine.KeyCode.X;
            public const UnityEngine.KeyCode KEY_ASCENSION_C      = UnityEngine.KeyCode.C;
            public const UnityEngine.KeyCode KEY_CANCEL_TARGETING = UnityEngine.KeyCode.Escape;
            public const UnityEngine.KeyCode KEY_POTION_SMALL     = UnityEngine.KeyCode.Alpha1;
            public const UnityEngine.KeyCode KEY_POTION_LARGE     = UnityEngine.KeyCode.Alpha2;

            // 지점 기술 지정점을 최대 사거리보다 이만큼(m) 안쪽으로 제한한다. 서버는 자기 위치에서 거리를 다시 재고
            // 내비 메시 보정도 하므로 사거리 경계에 딱 맞춘 지점은 예측 위치 오차만으로 사거리 밖 거절이 난다
            public const float SKILL_POINT_RANGE_MARGIN = 0.25f;

            // 카메라 휠 줌 축 이름. 위로 굴리면 양수이고 카메라가 가까워진다
            public const string AXIS_CAMERA_ZOOM = "Mouse ScrollWheel";

            // 커서 지면 지점을 찾는 레이캐스트 거리(m)와 한 번에 받는 충돌 수. 자기 몸체를 걸러 내야 해서 여러 개를 받는다
            public const float CURSOR_RAYCAST_MAX_DISTANCE = 500f;
            public const int   CURSOR_RAYCAST_MAX_HITS     = 8;

            // 개, 일반 기술 Q/W/E/R, 강림 Z/X/C, 물약 1/2 입력 슬롯 수
            public const int SKILL_SLOT_COUNT  = 4;
            public const int SUMMON_SLOT_COUNT = 3;
            public const int POTION_SLOT_COUNT = 2;

            // 입력 주기 제한. 초당 Network.INPUT_SEND_RATE_MAX개씩 채워지는 토큰 통이고, 지연 뭉침으로 한꺼번에 도착하는 입력을 이만큼 받아 준다
            // TODO(민성): 입력 계약값이 늘면 네트워크 계약값으로 모은다
            public const int INPUT_RATE_BURST = 10;

            // 토큰 한 개를 충전할 때 이진 소수 오차만 허용한다. 실제 입력 주기보다 이르게 온 입력은 계속 거절한다
            public const double INPUT_RATE_TOKEN_EPSILON = 0.000001;

            // 주기 초과는 서버 시각을 내림한 1초 구간마다 한 번 센다. 최근 10초 안의 서로 다른 구간 5개에서 끊는다
            public const double INPUT_RATE_VIOLATION_WINDOW_SECONDS       = 10.0;
            public const int    INPUT_RATE_VIOLATION_DISCONNECT_THRESHOLD = 5;

            // 순번 역행과 시각 이상은 주기 초과와 별도로 세며 현행 임계값을 유지한다
            public const double INPUT_VIOLATION_WINDOW_SECONDS       = 10.0;
            public const int    INPUT_VIOLATION_DISCONNECT_THRESHOLD = 30;

            // 서버 이동 경로의 모서리 버퍼. 넘는 경로는 앞부분만 따라가고 다음 이동 입력이 이어 준다
            public const int MAX_PATH_CORNERS = 32;

            // 클릭 지점에서 내비 메시를 찾는 반경(m). 벗어나면 이동 입력을 거절한다
            public const float MOVE_DESTINATION_SAMPLE_RADIUS = 1.5f;

            // 현재 위치에서 이동 목적지까지 거리 상한(m). 넘으면 경로를 계산하지 않고 거절한다.
            // 기획 미정 임시값, 화면에서 클릭할 수 있는 거리보다 넉넉하게 잡았다
            public const float MAX_MOVE_DESTINATION_DISTANCE = 40f;

            // 참가자별 경로 재계산 최소 간격(초). 그 사이 온 이동은 마지막 목적지만 남겨 간격이 지나면 계산한다
            public const double MOVE_REPATH_MIN_INTERVAL_SECONDS = 0.2;

            // 진행 중인 경로의 목적지와 이 거리 제곱(m 제곱) 안이면 같은 목적지로 보고 다시 계산하지 않는다
            public const float MOVE_SAME_DESTINATION_EPSILON_SQR = 0.01f;

            // 소유 클라이언트 이동 예측의 위치 기록. 간격(초) x 개수가 되돌아볼 수 있는 시간이며 왕복 지연과 복제 간격을 덮어야 한다.
            // 지연이 1초 넘게 튀어도 같은 시점끼리 비교할 수 있게 약 4초를 남긴다
            public const int    PREDICTION_HISTORY_CAPACITY         = 512;
            public const double PREDICTION_HISTORY_INTERVAL_SECONDS = 1.0 / 120.0;

            // 예측과 서버 확정 위치의 차이(m). 이보다 작으면 무시하고, 스냅 거리보다 크면 보간 없이 바로 맞춘다
            public const float PREDICTION_CORRECTION_DEADZONE = 0.05f;
            public const float PREDICTION_SNAP_DISTANCE       = 3f;

            // 초당 보정 비율. 남은 차이를 매초 이 비율의 지수 감쇠로 줄인다
            public const float PREDICTION_CORRECTION_RATE = 10f;

            // 기술, 사망처럼 서버가 위치를 정하는 동안 서버 위치를 따라가는 초당 비율
            public const float PREDICTION_FOLLOW_RATE = 15f;

            // 보낸 이동이 이 시간(초) 안에 서버 이동 순번으로 돌아오지 않으면 잃은 것으로 보고 멈춘 상태끼리 비교한다.
            // 입력은 신뢰 채널이고 경로 실패와 주기 초과는 통보되므로 시간 초과는 대개 지연이 튈 때 생긴다. 1초였을 때 지연이 튀면
            // 아직 오는 중인 이동을 잃은 것으로 보고 옛 서버 위치로 스냅했다가 응답이 오면 다시 앞으로 스냅해 캐릭터가 되돌아갔다
            public const double PREDICTION_MOVE_ACK_TIMEOUT_SECONDS = 3.0;

            // 측정한 왕복 지연의 이 배수가 위 시간보다 길면 그만큼 더 기다린다
            public const double PREDICTION_MOVE_ACK_RTT_MULTIPLIER = 4.0;

            // 기다리는 시간의 상한(초). 위치 기록이 덮어 쓰이기 전까지만 기다린다
            public const double PREDICTION_MOVE_ACK_TIMEOUT_MAX_SECONDS =
                PREDICTION_HISTORY_CAPACITY * PREDICTION_HISTORY_INTERVAL_SECONDS;

            // 서버 틱 하나에서 계산하는 경로 수 상한. 넘는 요청은 다음 틱으로 미룬다
            public const int PATH_REQUESTS_PER_TICK = 4;

            // 방향을 ushort 하나로 보낸다. 한 칸이 약 0.0055도다
            public const int YAW_QUANTIZATION_STEPS = 65536;

            // 이보다 짧은 수평 벡터는 방향이 없다고 본다(m 제곱)
            public const float DIRECTION_EPSILON_SQR = 0.000001f;
        }
    }
}
