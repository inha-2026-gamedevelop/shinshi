namespace Shinshi.Common
{
    public static partial class Constants
    {
        /// <summary> enum과 배열 크기에 묶이는 전투, 성장 개수와 서버 전투 판정의 계약값 </summary>
        public static class Combat
        {
            /****************************************
            *                Fields
            ****************************************/

            // 직업마다 고정 일반 기술 4개, 기술마다 부적 선택 단계 3개(보조 2, 핵심 1)
            public const int SKILLS_PER_CLASS         = 4;
            public const int TALISMAN_TIERS_PER_SKILL = 3;

            // 일반 기술 Q/W/E/R 입력과 쿨다운 배열의 0부터 시작하는 슬롯 번호
            public const int SKILL_SLOT_Q = 0;
            public const int SKILL_SLOT_W = 1;
            public const int SKILL_SLOT_E = 2;
            public const int SKILL_SLOT_R = 3;

            // 신령사의 강림 종류
            public const int SUMMON_ENHANCEMENT_COUNT = 3;

            // 체력이 이 값 이하로 남으면 0으로 보고 사망 처리한다. float 누적 오차로 0.00001이 남아 살아 있는 것을 막는다
            public const float HEALTH_EPSILON = 0.0001f;

            // 대상 정면과 피해 출발 방향 사이 각도가 이 값을 넘으면 후방이다. 전방 0~60도, 측면 60~120도, 후방 120~180도
            public const float REAR_HIT_MIN_ANGLE_DEGREES = 120f;

            // 기본 공격의 판정 기록용 정의 ID, 기술 카탈로그에 없는 고정 동작이다
            public const string BASIC_ATTACK_SOURCE_ID = "basic_attack";

            // 원거리 기본 공격의 판정 반폭(m). 기획이 투사체 폭과 몸체 크기를 정하기 전 임시값
            // TODO(민성): 기획 결정 뒤 PlayerConfigSO의 직업별 기본 공격 명세로 옮긴다
            public const float BASIC_ATTACK_RAY_HALF_WIDTH = 0.5f;

            // 한 서버 프로세스의 전투 대상 상한, 넘으면 등록을 거절하고 연결을 끊는다.
            // 던전은 플레이어 4 + 소환 동료 8 + 활성 적 24지만 마을 서버도 같은 이동 판정을 쓰므로 마을 목표 100명에 여유를 둔다
            public const int MAX_COMBAT_TARGETS = 128;

            // 같은 동작, 같은 대상 중복 적중 기록. 30Hz에서 4인과 적 24체가 동시에 맞혀도 남는 크기
            public const int HIT_LEDGER_CAPACITY = 2048;

            // 적중 기록 보관 시간(초). 가장 긴 단일 동작(장판 6초)보다 길어야 같은 동작이 다시 맞히지 않는다.
            // 지속 피해는 틱마다 새 동작 번호를 받으므로 이 시간과 무관하다
            public const double HIT_LEDGER_RETENTION_SECONDS = 10.0;

            // 가장 긴 단일 동작(장판 6초)의 지속 시간(초). 적중 기록이 가득 차면 이보다 오래된 기록은 보관 시간 전이라도 회수한다.
            // 이 시간이 지난 동작은 더 맞히지 않으므로 회수해도 중복 적중이 생기지 않는다
            public const double LONGEST_ACTION_SECONDS = 6.0;

            // 적중 기록 가득 참 경고를 남기는 최소 간격(초). 틱마다 문자열을 만들지 않으려고 모아서 남긴다
            public const double HIT_LEDGER_FULL_LOG_INTERVAL_SECONDS = 10.0;

            // 판정 시야를 막는 정적 지형 레이어 이름. 프로젝트에 이 레이어가 없으면 시야 검사를 쓰지 못하고 시작 때 오류를 남긴다
            // TODO(민성): 레이어를 만들고 벽, 바위 콜라이더에 지정한다
            public const string OBSTACLE_LAYER_NAME = "Obstacle";

            // 시야 선을 긋는 높이(m), 발밑 지형 굴곡에 걸리지 않도록 몸통 높이에서 긋는다. 기획 미정 임시값
            public const float LINE_OF_SIGHT_HEIGHT = 1.0f;

            // 공격자와 대상의 높이 차 상한(m). 넘으면 수평 범위 안이어도 맞지 않는다. 기획 미정 임시값
            public const float MAX_ATTACK_HEIGHT_DIFFERENCE = 2.0f;

            // 서버 틱이 밀렸을 때 한 프레임에 따라잡는 최대 틱 수. 넘는 밀림은 버려서 틱 폭주를 막는다
            public const int MAX_CATCHUP_TICKS = 3;

            // 플레이어당 동시에 남는 장판, 투사체 실행 버퍼 상한
            public const int MAX_SKILL_EFFECTS_PER_PLAYER = 12;

            // 관통 투사체 하나가 기억하는 맞힌 대상 상한. 넘는 관통 수는 이 값으로 잘린다
            public const int MAX_PIERCE_HITS_PER_PROJECTILE = 8;
            public const int RESONANCE_LEDGER_CAPACITY    = 256;
            public const int COMPANIONS_PER_PLAYER        = 2;

            // 출정 하나에 동시에 스폰되는 동료 네트워크 개체 상한. 재호출은 같은 틱에 옛 개체를 먼저 내려 상한을 넘지 않는다
            public const int MAX_COMPANION_OBJECTS = Session.MAX_PARTY_SIZE * COMPANIONS_PER_PLAYER;

            // TODO(민성): 기획 미정, 적 몸체 앞에서 멈추는 돌진 판정의 임시 반경(m)
            public const float SKILL_TARGET_BODY_RADIUS = 0.5f;

            // TODO(민성): 기획 미정, 원거리 신령 투사체 속도 임시값(m/s)
            public const float COMPANION_PROJECTILE_SPEED = 16f;

            // TODO(민성): 기획 미정, 불꽃진 첫 틱은 생성 후 한 주기부터 시작
            public const bool FIELD_TICKS_IMMEDIATELY = false;

            // 부활 지점을 죽은 자리 근처 내비 메시에서 찾는 반경(m). 못 찾으면 안전 지점 재평가 간격 뒤 다시 찾는다
            public const float REVIVE_SAFE_POINT_SEARCH_RADIUS = 2.0f;

            // 몸체 반경(m). 회피가 적 몸체 직전에서 멈추는 판정에 쓴다. 플레이어 값은 PF_NetPlayer 캡슐 반경과 같아야 한다
            // TODO(민성): 신령 몸체 크기가 정해지면 바꾼다. 적은 적 정의 데이터에서 받는다
            public const float PLAYER_BODY_RADIUS    = 0.5f;
            public const float COMPANION_BODY_RADIUS = 0.5f;

            // TODO(기웅): 기획 미정, 보스 몸체 반경 임시값(m). 보스 정의 데이터에 몸체 크기가 생기면 그 값을 쓴다
            public const float BOSS_BODY_RADIUS      = 2f;

            // 몸체에 막히는지 볼 대상을 찾을 때 이동 거리에 더하는 여유(m). 가장 큰 몸체(보스) 반경보다 커야 한다
            public const float MAX_BODY_RADIUS = 4f;

            // 제어 허용 구간에 있는 보스에게 적용하는 최대 속박 시간(초)
            public const float BOSS_ROOT_MAX_SECONDS  = 1f;

            // 출정 계약(예약, 입장 토큰)에 보스와 전장 지역이 없어 보스전은 이 값으로 연다. 체험판 기준 Aref, Href는 숲 지역에 있다
            // TODO(민성): 출정 계약에 보스와 지역이 생기면 그 값으로 바꾼다
            public const string DEV_BOSS_ID        = "boss_wolbaek";
            public const string DEV_BOSS_REGION_ID = "region_forest";
            public const float SKILL_HALF_WIDTH_SCALE = 0.5f;
            public const int   WARRIOR_COUNTER_SLOT   = 3;

            // CharacterCombatLoadout 배열 길이. 장비는 EEquipmentSlot, 수련은 ETrainingStat 순서다
            public const int EQUIPMENT_SLOT_COUNT = 6;
            public const int TRAINING_STAT_COUNT  = 5;

            // 부적 효과의 기술당 선택 단계 수만큼 잡는 참가자 한 명의 부적 칸 상한(기술 4개 x 단계 3개)
            public const int MAX_TALISMANS_PER_PARTICIPANT = SKILLS_PER_CLASS * TALISMAN_TIERS_PER_SKILL;

            // 갈라진 빛이 한 동작에서 고르는 서로 다른 대상 수 상한
            public const int MAX_COMPANION_SPLIT_TARGETS = 4;
        }
    }
}
