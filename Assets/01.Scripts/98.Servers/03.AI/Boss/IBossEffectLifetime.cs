// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.AI
{
    // 패턴이 끝나도 남는 장판과 투사체는 패턴 실행과 따로 수명을 가진다
    // 사유별로 무엇을 지울지는 이 쪽이 정한다. 사망 때 지속물을 지울지는 기획 미정이다
    /// <summary> 지속 장판과 투사체의 수명 관리자 </summary>
    public interface IBossEffectLifetime
    {
        /// <summary> 사유별로 정리한다, castInstanceId가 CombatIds.NONE이면 이 보스의 모든 지속물이 대상이다 </summary>
        void Cancel(EBossInterruptReason reason, int castInstanceId);

        /// <summary> 아직 남아 있는 해당 패턴의 장판 수, 동시 장판 상한 검사에 쓴다 </summary>
        int ActiveAreaCount(string patternId);
    }
}
