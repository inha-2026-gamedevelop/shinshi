// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> IDamageResolver의 서버 구현, 한 전장에 하나 두고 서버 틱 안에서만 부른다 </summary>
    public sealed class DamageResolver : IDamageResolver
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly CombatTargetRegistry _registry;
        private readonly HitLedger            _ledger;
        private readonly float                _defenseConstant;
        private readonly float                _reductionCap;

        private double _now                 = 0.0;
        private int    _lastInstanceId      = CombatIds.NONE;
        private double _nextFullReportTime  = 0.0;
        private long   _reportedFullCount   = 0;
        private long   _reportedForcedCount = 0;

        /****************************************
        *              Properties
        ****************************************/

        // 지금 처리 중인 서버 틱 시각
        public double Now => _now;

        /****************************************
        *                Methods
        ****************************************/

        // 방어 상수와 감소 상한은 PlayerConfigSO 값을 넘긴다. 적에게도 같은 공식을 쓴다
        public DamageResolver(CombatTargetRegistry registry, HitLedger ledger, float defenseConstant, float reductionCap)
        {
            _registry        = registry;
            _ledger          = ledger;
            _defenseConstant = defenseConstant;
            _reductionCap    = reductionCap;
        }

        /// <summary> 틱 시작에 서버 시각을 넣고 오래된 적중 기록을 지운다 </summary>
        public void BeginTick(double serverTime)
        {
            _now = serverTime;
            _ledger.Prune(serverTime);
            ReportLedgerPressure(serverTime);
        }

        public int IssueInstanceId()
        {
            ++_lastInstanceId;

            // int 범위를 넘어 음수로 돌면 1부터 다시 쓴다. 30Hz 전투 한 판에서는 닿지 않는다
            if (_lastInstanceId <= CombatIds.NONE)
            {
                _lastInstanceId = CombatIds.NONE + 1;
            }

            return _lastInstanceId;
        }

        public DamageResult Apply(in DamageRequest request)
        {
            if (!request.HasValidIds)
            {
                return DamageResult.Ignored(EDamageOutcome.TargetNotFound);
            }

            if (!_registry.TryGetDamageable(request.HitTargetId, out IDamageable target))
            {
                return DamageResult.Ignored(EDamageOutcome.TargetNotFound);
            }

            if (target.LifeState != EParticipantLifeState.Alive)
            {
                return DamageResult.Ignored(EDamageOutcome.TargetNotAlive);
            }

            // 공격자가 이미 전장에서 빠졌으면 소속을 확인할 수 없다. 죽은 적이 남긴 투사체도 맞아야 하므로 막지 않는다
            if ((_registry.TryGetDamageable(request.AttackerId, out IDamageable attacker)) && (attacker.Faction == target.Faction))
            {
                return DamageResult.Ignored(EDamageOutcome.SameFaction);
            }

            if (target.IsInvulnerable)
            {
                return DamageResult.Ignored(EDamageOutcome.Invulnerable);
            }

            // 막힌 타격도 기록한다. 같은 동작이 방어 창이 닫힌 뒤 다시 맞히면 안 된다
            EHitRecord record = _ledger.TryRecord(request.ActionInstanceId, request.HitTargetId, _now);
            if (record == EHitRecord.Duplicate)
            {
                return DamageResult.Ignored(EDamageOutcome.DuplicateHit);
            }

            // 거절 횟수는 HitLedger가 세고 BeginTick의 주기 로그가 남긴다
            if (record == EHitRecord.Full)
            {
                return DamageResult.Ignored(EDamageOutcome.DuplicateHit);
            }

            bool isRear = DamageRules.IsRearHit(target.Position, target.Forward, request.SourcePosition,
                Constants.Combat.REAR_HIT_MIN_ANGLE_DEGREES);

            if ((target.IsGuardingFront) && (!isRear) && (!request.IsUnblockable))
            {
                return DamageResult.Guarded();
            }

            float damage   = DamageRules.ApplyDefense(request.RawDamage, target.Defense, _defenseConstant, _reductionCap);
            float health   = target.Health;
            float remained = health - damage;
            bool  isLethal = remained <= Constants.Combat.HEALTH_EPSILON;
            float applied  = isLethal ? health : damage;

            target.ApplyDamage(applied, isLethal, _now);
            return DamageResult.Applied(applied, isLethal, isRear);
        }

        // 가득 참과 강제 회수가 늘었을 때만, 최소 간격을 두고 남긴다. 문자열은 이 로그에서만 만든다
        private void ReportLedgerPressure(double serverTime)
        {
            if (serverTime < _nextFullReportTime)
            {
                return;
            }

            long fullCount   = _ledger.FullCount;
            long forcedCount = _ledger.ForcedEvictionCount;
            if ((fullCount == _reportedFullCount) && (forcedCount == _reportedForcedCount))
            {
                return;
            }

            _nextFullReportTime = serverTime + Constants.Combat.HIT_LEDGER_FULL_LOG_INTERVAL_SECONDS;
            Debug.LogError($"[Combat] 적중 기록 상한 {_ledger.Capacity} 압박, 거절 +{fullCount - _reportedFullCount}(누적 {fullCount}), " +
                $"강제 회수 +{forcedCount - _reportedForcedCount}(누적 {forcedCount}). 상한 또는 동작 번호 발급을 확인한다");
            _reportedFullCount   = fullCount;
            _reportedForcedCount = forcedCount;
        }
    }
}
