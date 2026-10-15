// System
using System;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Combat
{
    // 노란 여우불은 플레이어 공격이 실제로 맞히는 피격 대상이라 플레이어, 보스와 같은 전투 대상 등록부에 올린다. 번호도 같은 등록부가 발급해야 겹치지 않는다
    /// <summary> 노란 여우불의 피격 대상 등록을 전투 대상 등록부에 그대로 넘긴다 </summary>
    public sealed class BossYellowRegistry : IWolbaekYellowRegistry
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly CombatTargetRegistry _registry;

        /****************************************
        *                Methods
        ****************************************/

        public BossYellowRegistry(CombatTargetRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public int IssueCombatId()
        {
            return _registry.IssueCombatId();
        }

        public bool Register(IDamageable target)
        {
            return _registry.Register(target);
        }

        public bool Unregister(int combatId)
        {
            return _registry.Unregister(combatId);
        }
    }
}
