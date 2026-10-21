// System
using System;

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
        private readonly float                _playerDefenseConstant;
        private readonly float                _reductionCap;
        private readonly float                _criticalMultiplier;
        private readonly Func<float>          _roll;
        private readonly Func<int, IDamageable> _retainedSource;

        private double _now                 = 0.0;
        private int    _lastInstanceId      = CombatIds.NONE;
        private double _nextFullReportTime  = 0.0;
        private long   _reportedFullCount   = 0;
        private long   _reportedForcedCount = 0;
        private double _nextFeedbackErrorTime = 0.0;

        /****************************************
        *              Properties
        ****************************************/

        // 지금 처리 중인 서버 틱 시각
        public double Now => _now;

        // 전투 결과는 적용 뒤에만 알린다. 표현 수신자의 실패가 체력 판정을 되돌리지 않는다
        public event Action<AppliedDamageNotice> DamageApplied;

        // 생명력 흡수로 실제 오른 체력, 주인 전투 번호와 회복량. 전투 기록용이며 수신 실패가 회복을 되돌리지 않는다
        public event Action<int, float> LifeStealApplied;

        /****************************************
        *                Methods
        ****************************************/

        // 방어 상수와 감소 상한은 PlayerConfigSO 값을 넘긴다. 공식은 같고 상수만 맞는 쪽 진영으로 고른다.
        // playerDefenseConstant를 넘기지 않으면 양쪽이 같은 상수를 쓴다. 상수 하나만 쓰던 시험 rig의 호출을 그대로 둔다
        public DamageResolver(CombatTargetRegistry registry, HitLedger ledger, float defenseConstant, float reductionCap,
            float criticalMultiplier = 1f, Func<float> roll = null, Func<int, IDamageable> retainedSource = null,
            float playerDefenseConstant = 0f)
        {
            _registry        = registry;
            _ledger          = ledger;
            _defenseConstant = defenseConstant;
            _playerDefenseConstant = (playerDefenseConstant > 0f) ? playerDefenseConstant : defenseConstant;
            _reductionCap    = reductionCap;
            _criticalMultiplier = ((DamageFeedbackRules.IsFinite(criticalMultiplier)) && (criticalMultiplier >= 1f))
                ? criticalMultiplier : 1f;
            _roll = roll ?? Roll;
            _retainedSource = retainedSource;
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

            if ((!DamageFeedbackRules.IsFinite(request.RawDamage)) || (request.RawDamage <= 0f)
                || (!DamageFeedbackRules.IsFinite(request.SourcePosition)) || (!DamageFeedbackRules.IsFinite(target.Position))
                || (!DamageFeedbackRules.IsFinite(target.Forward)) || (!DamageFeedbackRules.IsFinite(target.Health))
                || (target.Health <= 0f) || (!DamageFeedbackRules.IsFinite(target.Defense))
                || (!DamageFeedbackRules.IsFinite(_defenseConstant)) || (!DamageFeedbackRules.IsFinite(_playerDefenseConstant))
                || (!DamageFeedbackRules.IsFinite(_reductionCap)))
            {
                return DamageResult.Ignored(EDamageOutcome.InvalidRequest);
            }

            // 공격자가 이미 전장에서 빠졌으면 소속을 확인할 수 없다. 죽은 적이 남긴 투사체도 맞아야 하므로 막지 않는다
            IDamageable attacker = ResolveSource(request.AttackerId);
            if ((attacker != null) && (attacker.Faction == target.Faction))
            {
                return DamageResult.Ignored(EDamageOutcome.SameFaction);
            }

            if (target.IsInvulnerable)
            {
                return DamageResult.Ignored(EDamageOutcome.Invulnerable);
            }

            bool isRear = DamageRules.IsRearHit(target.Position, target.Forward, request.SourcePosition,
                Constants.Combat.REAR_HIT_MIN_ANGLE_DEGREES);
            bool guarded = (target.IsGuardingFront) && (!isRear) && (!request.IsUnblockable);

            // 플레이어와 플레이어의 소환물이 맞을 때만 플레이어용 상수를 쓴다. 적이 맞는 쪽은 바꾸지 않는다
            float defenseConstant = (target.Faction == ECombatFaction.Player) ? _playerDefenseConstant : _defenseConstant;
            float damage = guarded ? 0f : DamageRules.ApplyDefense(request.RawDamage, target.Defense, defenseConstant, _reductionCap);
            if ((!DamageFeedbackRules.IsFinite(damage)) || (damage < 0f))
            {
                // 유한 입력의 중간 계산도 넘칠 수 있다. 무효 요청은 같은 동작의 정상 재시도를 막지 않는다
                return DamageResult.Ignored(EDamageOutcome.InvalidRequest);
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

            if (guarded)
            {
                return DamageResult.Guarded();
            }

            // 소비 여부와 체력 하한은 정상 적중이 확정된 뒤, 치명과 상한 전에 받는다. 소비한 타격은 치명 확률도 굴리지 않는다
            float healthFloor = 0f;
            if (target is IDamageHitScreenTarget screenTarget)
            {
                bool isSummonAttack = (attacker != null) && (attacker.IsSummon);
                if (screenTarget.ScreenHit(in request, isSummonAttack, out healthFloor))
                {
                    return DamageResult.Ignored(EDamageOutcome.Invulnerable);
                }
            }

            // 주인이 없어진 기술도 보존된 anchor에서 확률을 읽는다. 소환물 자체의 확률은 사용하지 않는다
            IDamageable owner = ((attacker != null) && (attacker.IsSummon)) ? ResolveSource(attacker.OwnerCombatId) : attacker;
            int ownerId = ((owner != null) && (owner.Faction == ECombatFaction.Player) && (!owner.IsSummon))
                ? owner.CombatId : CombatIds.NONE;
            bool isCritical = (damage > 0f) && (ownerId != CombatIds.NONE) && (owner is ICriticalDamageSource critical)
                && IsCritical(critical.CritChance);
            if (isCritical)
            {
                // 방어와 치명 배율은 곱셈이라 동일한 공식이다. 중간 float 넘침은 실제 피해를 구한 뒤에만 포화시킨다
                damage = (float)Math.Min(float.MaxValue, (double)damage * _criticalMultiplier);
            }
            float health = target.Health;

            // 상한은 방어와 치명 배율 뒤, 치명 여부와 적용량을 정하기 전에 건다. 그래야 경계를 넘는 초과분이 미터와 흡혈과 피해 숫자에 들어가지 않는다.
            // 상한은 피해를 줄이기만 한다. 비유한, 음수, 원래보다 큰 값은 대상 쪽 결함이므로 자르지 않고 무시해 원래 피해를 쓴다
            if (target is IDamageCapTarget capTarget)
            {
                float capped = capTarget.CapIncomingDamage(health, damage);
                if ((DamageFeedbackRules.IsFinite(capped)) && (capped >= 0f) && (capped <= damage))
                {
                    // 전부 깎였으면 없던 피해다. 0짜리 적용으로 남기면 피해 숫자와 적중 판정이 무적 구간의 타격에도 나간다.
                    // 적중 기록은 이미 남았으므로 같은 동작이 이 대상을 다시 맞히지는 못한다(막힌 타격과 같다)
                    if ((damage > 0f) && (!(capped > 0f)))
                    {
                        return DamageResult.Ignored(EDamageOutcome.Invulnerable);
                    }

                    damage = capped;
                }
            }

            // 하한은 상한 뒤에 건다. 하한에서 막힌 피해는 치명이나 처치로 알리지 않고, 남는 피해가 없으면 없던 피해다
            if ((DamageFeedbackRules.IsFinite(healthFloor)) && (healthFloor > 0f))
            {
                float allowed = health - healthFloor;
                if (!(allowed > 0f))
                {
                    return DamageResult.Ignored(EDamageOutcome.Invulnerable);
                }

                if (damage > allowed)
                {
                    damage     = allowed;
                    isCritical = false;
                }
            }

            float remained = health - damage;
            bool  isLethal = (remained <= Constants.Combat.HEALTH_EPSILON) && (!(healthFloor > 0f));
            float applied  = isLethal ? health : damage;

            // 사망이 registry 해제와 Transform 파괴를 일으킬 수 있어 좌표와 귀속은 ApplyDamage 전에 고정한다
            Vector3 position = target.Position;
            DamageResult result = DamageResult.Applied(applied, isLethal, isRear, isCritical);
            var notice = new AppliedDamageNotice(ownerId, request.HitTargetId, position, result, _now);
            target.ApplyDamage(applied, isLethal, _now);
            ApplyLifeSteal(owner, ownerId, applied);
            Publish(in notice);
            return result;
        }

        // 체력을 바꾸는 판정이라 표시용 이벤트 구독이 아니라 여기서 직접 부른다. 주인이 플레이어일 때만이고,
        // 연결이 사라져 보존된 anchor는 받을 대상이 없어 스스로 0을 돌려준다
        private void ApplyLifeSteal(IDamageable owner, int ownerId, float applied)
        {
            if ((ownerId == CombatIds.NONE) || (!(applied > 0f)) || (!(owner is ILifeStealSource source)))
            {
                return;
            }

            float healed = source.ReceiveLifeSteal(applied, _now);
            if (!(healed > 0f))
            {
                return;
            }

            try
            {
                LifeStealApplied?.Invoke(ownerId, healed);
            }
            catch (Exception exception)
            {
                if (_now >= _nextFeedbackErrorTime)
                {
                    _nextFeedbackErrorTime = _now + Constants.Combat.HIT_LEDGER_FULL_LOG_INTERVAL_SECONDS;
                    Debug.LogError($"[Combat] 생명력 흡수 기록 전달 실패: {exception.Message}");
                }
            }
        }

        private IDamageable ResolveSource(int combatId)
        {
            return _registry.TryGetDamageable(combatId, out IDamageable source) ? source : _retainedSource?.Invoke(combatId);
        }

        private bool IsCritical(float chance)
        {
            if ((!DamageFeedbackRules.IsFinite(chance)) || (chance <= 0f))
            {
                return false;
            }
            if (chance >= 1f)
            {
                return true;
            }
            float roll = _roll();
            return (DamageFeedbackRules.IsFinite(roll)) && (roll >= 0f) && (roll < chance);
        }

        private static float Roll() => UnityEngine.Random.value;

        private void Publish(in AppliedDamageNotice notice)
        {
            try
            {
                DamageApplied?.Invoke(notice);
            }
            catch (Exception exception)
            {
                // 전송 장애 반복 로그도 서버 틱을 압박하지 않게 기존 원장 로그와 같은 최소 간격을 둔다
                if (_now >= _nextFeedbackErrorTime)
                {
                    _nextFeedbackErrorTime = _now + Constants.Combat.HIT_LEDGER_FULL_LOG_INTERVAL_SECONDS;
                    Debug.LogError($"[Combat] 피해 적용 후 숫자 전달 실패: {exception.Message}");
                }
            }
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
