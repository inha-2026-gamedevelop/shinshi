// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Combat
{
    // 패턴 정의(PatternDefinition)에는 타별 형태, 충돌 회복, 기믹 설정을 담을 자리가 없어 무철 팩토리가 실행 데이터를 따로 받는다.
    // 여기 값은 모두 임시 시작값이며 확정 밸런스가 아니다. 콤보와 충돌 회복은 무철 시험 구동기가 쓰는 값과, 기믹 설정은 기믹 시험이 쓰는 값과 같게 두었고
    // 2페이즈 실행 데이터는 정의의 실행 시간과 맞아야 해서 시험 카탈로그가 만든 것을 그대로 받는다.
    // TODO(기웅): 실행 데이터가 카탈로그로 옮겨지면 이 클래스를 지우고 카탈로그에서 읽는다
    /// <summary> 운영 서버가 무철 패턴 팩토리와 돌진 기믹에 넘기는 실행 데이터, 임시 시작값이다 </summary>
    public static class MucheolSortieData
    {
        /****************************************
        *                Fields
        ****************************************/

        // P05 콤보 첫 타
        private const float COMBO_FIRST_RANGE       = 5f;    // m
        private const float COMBO_FIRST_ARC_ANGLE   = 120f;  // 도
        private const float COMBO_FIRST_DAMAGE_REF  = 0.14f; // 기준 체력의 배수
        private const float COMBO_FIRST_DELAY       = 0f;    // 초, 실행 시작부터

        // P05 콤보 둘째 타
        private const float COMBO_SECOND_RANGE      = 5f;    // m
        private const float COMBO_SECOND_ARC_ANGLE  = 70f;   // 도
        private const float COMBO_SECOND_DAMAGE_REF = 0.24f; // 기준 체력의 배수
        private const float COMBO_SECOND_DELAY      = 0.5f;  // 초, 실행 시작부터

        // P06 돌진이 벽이나 구조물에 막혀 끝났을 때의 회복
        private const float CHARGE_COLLISION_RECOVERY = 5.0f; // 초

        // 돌진 유도 기믹
        private const float GIMMICK_TRIGGER_HEALTH_RATE = 0.70f; // 체력 비율이 이 값 이하면 발동
        private const int   GIMMICK_REQUIRED_STRUCTURES = 3;     // 부서져야 하는 서로 다른 지정 구조물 수
        private const float GIMMICK_GROGGY_SECONDS      = 3f;    // 초

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> P05 콤보의 타 목록 </summary>
        public static MucheolComboSpec CreateComboSpec()
        {
            return new MucheolComboSpec(
                new MucheolComboHit(COMBO_FIRST_RANGE, COMBO_FIRST_ARC_ANGLE, COMBO_FIRST_DAMAGE_REF, COMBO_FIRST_DELAY),
                new MucheolComboHit(COMBO_SECOND_RANGE, COMBO_SECOND_ARC_ANGLE, COMBO_SECOND_DAMAGE_REF, COMBO_SECOND_DELAY));
        }

        /// <summary> P06 돌진의 충돌 회복 설정 </summary>
        public static MucheolChargeSpec CreateChargeSpec()
        {
            return new MucheolChargeSpec(CHARGE_COLLISION_RECOVERY);
        }

        /// <summary> 2페이즈 일곱 패턴의 실행 데이터 </summary>
        public static MucheolPhase2Spec CreatePhaseTwoSpec()
        {
            return MucheolDevCatalog.CreatePhaseTwoSpec();
        }

        /// <summary> 돌진 유도 기믹의 발동 비율, 필요한 구조물 수, 그로기 시간 </summary>
        public static MucheolChargeGimmickSpec CreateChargeGimmickSpec()
        {
            return new MucheolChargeGimmickSpec(GIMMICK_TRIGGER_HEALTH_RATE, GIMMICK_REQUIRED_STRUCTURES,
                GIMMICK_GROGGY_SECONDS);
        }
    }
}
