// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.AI
{
    // 패턴이 끝나도 남는 장판과 투사체는 패턴 실행과 따로 수명을 가진다
    // 지속물이 피해를 낼 때는 반드시 BossPatternContext.Damage 게이트로 낸다. 게이트는 사망과 전투 종료 뒤에 닫혀 있다
    // 사유별 정리
    // Death, CombatEnd: 남은 장판과 투사체를 즉시 모두 제거하고 예약 타격을 포함한 추가 피해를 내지 않는다. 이미 적용된 피해는 되돌리지 않는다
    // PhaseTransition: 진행 중인 공격의 잔여 장판, 투사체, 예정 피해를 정리한다
    // DirectControl: 취소된 시전(castInstanceId)이 만든 지속물만 정리한다
    /// <summary> 지속 장판과 투사체의 수명 관리자 </summary>
    public interface IBossEffectLifetime
    {
        /// <summary> 사유별로 정리한다, castInstanceId가 CombatIds.NONE이면 이 보스의 모든 지속물이 대상이다 </summary>
        void Cancel(EBossInterruptReason reason, int castInstanceId);

        /// <summary> 아직 남아 있는 해당 패턴의 장판 수, 동시 장판 상한 검사에 쓴다 </summary>
        int ActiveAreaCount(string patternId);
    }
}
