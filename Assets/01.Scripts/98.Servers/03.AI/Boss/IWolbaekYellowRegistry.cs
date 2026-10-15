// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.AI
{
    // 노란 여우불은 플레이어 공격이 실제로 맞히는 피격 대상이어야 한다. 대상 목록과 번호 발급은 전투 대상 등록부(CombatTargetRegistry)의 일이라
    // 기믹은 이 창구로만 번호를 받고 등록한다. 광역 공격이 여러 노란 여우불에 닿으면 판정기가 각각 피해를 주므로 노란 여우불마다 따로 등록한다.
    // 구현은 CombatTargetRegistry를 감싸는 연결이며 아직 없다
    /// <summary> 노란 여우불의 피격 대상 등록 창구 </summary>
    public interface IWolbaekYellowRegistry
    {
        /// <summary> 새 전투 번호를 발급한다, 발급하지 못하면 CombatIds.NONE이다 </summary>
        int IssueCombatId();

        /// <summary> 피격 대상으로 등록한다, 상한을 넘거나 번호가 겹치면 거짓이다 </summary>
        bool Register(IDamageable target);

        /// <summary> 등록을 해제한다, 없던 번호면 거짓이다 </summary>
        bool Unregister(int combatId);
    }
}
