// 프로젝트 내부
using Shinshi.Combat;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    /// <summary> 패턴 한 번의 시전이 쓰는 입력 묶음 </summary>
    public readonly struct BossPatternContext
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PatternDefinition   _definition;
        private readonly int                 _castInstanceId;
        private readonly int                 _targetCombatId;
        private readonly ICombatTarget       _self;
        private readonly ICombatTargetQuery  _targets;
        private readonly IDamageResolver     _damage;

        /****************************************
        *              Properties
        ****************************************/

        public PatternDefinition  Definition     => _definition;

        /// <summary> 예고부터 회복까지 한 번의 시전 번호, DamageRequest.CastInstanceId에 넣는다 </summary>
        public int                CastInstanceId => _castInstanceId;

        /// <summary> 선택 때 고른 타깃, 보관용 참조 대신 번호만 넘기고 필요하면 Targets.TryGet으로 다시 찾는다 </summary>
        public int                TargetCombatId => _targetCombatId;

        public ICombatTarget      Self           => _self;
        public ICombatTargetQuery Targets        => _targets;
        public IDamageResolver    Damage         => _damage;

        /****************************************
        *                Methods
        ****************************************/

        public BossPatternContext(PatternDefinition definition, int castInstanceId, int targetCombatId,
            ICombatTarget self, ICombatTargetQuery targets, IDamageResolver damage)
        {
            _definition     = definition;
            _castInstanceId = castInstanceId;
            _targetCombatId = targetCombatId;
            _self           = self;
            _targets        = targets;
            _damage         = damage;
        }
    }
}
