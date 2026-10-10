using System;
using System.Collections.Generic;

using UnityEngine;

using Shinshi.Combat;
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    /// <summary> 월백 하드 2페이즈 P04, 대상 추적 예고와 고정 예고 뒤 장시간 장판을 남긴다 </summary>
    public sealed class BossHardAreaPattern : BossPatternBase, IBossPatternStartResult
    {
        private const int MAX_AREAS_PER_CAST = 2;

        private struct AreaPlan
        {
            public int TargetId;
            public int AreaId;
            public Vector3 Center;
            public bool IsFollowing;
            public bool IsReserved;
            public bool IsActivated;
        }

        private readonly float _referenceHealth;
        private readonly IBossHardAreaHost _host;
        private readonly IBossHardAreaListener _listener;
        private readonly IBossParticipantConnection _connections;
        private readonly AreaPlan[] _plans = new AreaPlan[MAX_AREAS_PER_CAST];
        private readonly List<ICombatTarget> _players = new List<ICombatTarget>();

        private int _planCount;
        private double _freezeServerTime;

        public bool IsStartRejected => _planCount == 0;
        public int AreaCount => _planCount;

        public BossHardAreaPattern(float referenceHealth, IBossHardAreaHost host, IBossHardAreaListener listener,
            IBossParticipantConnection connections)
        {
            if (referenceHealth <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(referenceHealth));
            }

            _host = host ?? throw new ArgumentNullException(nameof(host));
            _listener = listener ?? throw new ArgumentNullException(nameof(listener));
            _connections = connections ?? throw new ArgumentNullException(nameof(connections));
            _referenceHealth = referenceHealth;
        }

        public int GetAreaId(int index) => _plans[index].AreaId;
        public Vector3 GetAreaCenter(int index) => _plans[index].Center;

        protected override void OnBegin(double serverTime)
        {
            _planCount = 0;
            PatternDefinition definition = Context.Definition;
            _freezeServerTime = serverTime + definition.TelegraphSeconds;
            int wanted = Math.Min(definition.AreaTargetCount, MAX_AREAS_PER_CAST);

            if (Context.Targets.TryGet(Context.TargetCombatId, out ICombatTarget first) && IsTrackable(first))
            {
                TryPlan(first, serverTime);
            }

            if ((_planCount < wanted) && (Context.Random != null))
            {
                Context.Targets.GetLivingPlayers(_players);
                for (int i = _players.Count - 1; i >= 0; --i)
                {
                    if ((!IsTrackable(_players[i])) || ((_planCount > 0) && (_players[i].CombatId == _plans[0].TargetId)))
                    {
                        _players.RemoveAt(i);
                    }
                }

                while ((_planCount < wanted) && (_players.Count > 0))
                {
                    int index = Context.Random.NextInt(_players.Count);
                    ICombatTarget target = _players[index];
                    _players[index] = _players[_players.Count - 1];
                    _players.RemoveAt(_players.Count - 1);
                    if (!TryPlan(target, serverTime))
                    {
                        break;
                    }
                }
            }

            _players.Clear();
        }

        protected override void OnTick(EBossAttackState state, double serverTime)
        {
            // 경계를 건너뛴 틱에는 t=4 뒤의 현재 좌표를 과거 추적 결과로 쓰지 않는다.
            if ((state != EBossAttackState.Telegraph) || (serverTime > _freezeServerTime))
            {
                return;
            }

            for (int i = 0; i < _planCount; ++i)
            {
                AreaPlan plan = _plans[i];
                if (!plan.IsFollowing)
                {
                    continue;
                }

                if ((!Context.Targets.TryGet(plan.TargetId, out ICombatTarget target)) || (!IsTrackable(target)))
                {
                    plan.IsFollowing = false;
                }
                else if (plan.Center != target.Position)
                {
                    plan.Center = target.Position;
                    _listener.OnMovingUpdated(Context.CastInstanceId, plan.AreaId, plan.Center, serverTime);
                }

                _plans[i] = plan;
            }
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Execute)
            {
                for (int i = 0; i < _planCount; ++i)
                {
                    _plans[i].IsFollowing = false;
                    _listener.OnCenterFixed(Context.CastInstanceId, _plans[i].AreaId, _plans[i].Center, serverTime);
                }
            }
            else if (state == EBossAttackState.Recover)
            {
                Activate(serverTime);
            }
        }

        protected override void OnCancelled(EBossInterruptReason reason)
        {
            for (int i = 0; i < _planCount; ++i)
            {
                if ((!_plans[i].IsReserved) || _plans[i].IsActivated)
                {
                    continue;
                }

                _host.ReleaseReservation(_plans[i].AreaId);
                _plans[i].IsReserved = false;
                _listener.OnCancelled(Context.CastInstanceId, _plans[i].AreaId, reason);
            }
        }

        private bool IsTrackable(ICombatTarget target)
        {
            return (target != null) && (target.LifeState == EParticipantLifeState.Alive) && (!target.IsSummon)
                && (target.Faction == ECombatFaction.Player) && _connections.IsConnected(target.CombatId);
        }

        private bool TryPlan(ICombatTarget target, double serverTime)
        {
            int areaId = Context.Damage.IssueInstanceId();
            if (!_host.TryReserveReplacing(Context.CastInstanceId, areaId, Context.Definition.DefinitionId))
            {
                return false;
            }

            _plans[_planCount] = new AreaPlan
            {
                TargetId = target.CombatId,
                AreaId = areaId,
                Center = target.Position,
                IsFollowing = true,
                IsReserved = true
            };
            ++_planCount;
            _listener.OnMovingStarted(Context.CastInstanceId, areaId, target.CombatId, target.Position,
                Context.Definition.Shape.Range, serverTime, Context.Definition.TelegraphSeconds, Context.Definition.ActiveSeconds);
            return true;
        }

        private void Activate(double serverTime)
        {
            PatternDefinition definition = Context.Definition;
            for (int i = 0; i < _planCount; ++i)
            {
                AreaPlan plan = _plans[i];
                if ((!plan.IsReserved) || plan.IsActivated)
                {
                    continue;
                }

                BossAreaSpec spec = new BossAreaSpec(Context.CastInstanceId, plan.AreaId, Context.Self.CombatId,
                    definition.DefinitionId, plan.Center, definition.Shape.Range, definition.AreaDurationSeconds,
                    definition.AreaTickInterval, definition.DamageRef * _referenceHealth, EDamageFlags.Unblockable,
                    serverTime, Context.Damage, definition.AreaSkipsMissedRounds);

                plan.IsActivated = _host.TryActivateReplacing(in spec, definition.MaxConcurrentAreas);
                if (!plan.IsActivated)
                {
                    _host.ReleaseReservation(plan.AreaId);
                    plan.IsReserved = false;
                    _listener.OnCancelled(Context.CastInstanceId, plan.AreaId, EBossInterruptReason.None);
                }

                _plans[i] = plan;
            }
        }
    }
}
