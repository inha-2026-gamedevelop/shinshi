// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 기존 피해 판정에 반격 방어 창과 기본 공격 공명을 연결한다 </summary>
    public sealed class SkillDamageResolver : IDamageResolver
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly DamageResolver       _inner;
        private readonly CombatTargetRegistry _registry;
        private readonly HitLedger            _ledger;

        /****************************************
        *                Methods
        ****************************************/

        public SkillDamageResolver(DamageResolver inner, CombatTargetRegistry registry, HitLedger ledger)
        {
            _inner    = inner;
            _registry = registry;
            _ledger   = ledger;
        }

        public int IssueInstanceId() => _inner.IssueInstanceId();

        public DamageResult Apply(in DamageRequest request)
        {
            if ((request.HasValidIds) && (_registry.TryGetDamageable(request.HitTargetId, out IDamageable target))
                && (target.LifeState == EParticipantLifeState.Alive) && (!target.IsInvulnerable)
                && (target is ISkillCombatant combatant) && (combatant.Skills != null)
                && (combatant.Skills.CanGuard(in request, _inner.Now)))
            {
                if ((_registry.TryGetDamageable(request.AttackerId, out IDamageable source)) && (source.Faction == target.Faction))
                {
                    return DamageResult.Ignored(EDamageOutcome.SameFaction);
                }
                if (_ledger.TryRecord(request.ActionInstanceId, request.HitTargetId, _inner.Now) != EHitRecord.Recorded)
                {
                    return DamageResult.Ignored(EDamageOutcome.DuplicateHit);
                }
                combatant.Skills.MarkGuardSuccess();
                return DamageResult.Guarded();
            }
            DamageResult result = _inner.Apply(in request);
            if ((request.SourceDefinitionId == Constants.Combat.BASIC_ATTACK_SOURCE_ID)
                && (_registry.TryGetDamageable(request.AttackerId, out IDamageable attacker))
                && (attacker is ISkillCombatant player) && (player.Skills != null))
            {
                player.Skills.OnBasicHit(in request, in result);
            }
            return result;
        }
    }
}
