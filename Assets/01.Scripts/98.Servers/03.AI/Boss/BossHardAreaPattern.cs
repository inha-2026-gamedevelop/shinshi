using System;
using System.Collections.Generic;

using UnityEngine;

using Shinshi.Combat;
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 시간 흐름은 추적(TrackingSeconds) 뒤 중심 고정, 예고(TelegraphSeconds) 끝에 실행 진입, 실행(ActiveSeconds) 끝에 장판 활성, 회복 순이다.
    // 추적이 끝나는 순간 중심을 고정해 알리고 그 뒤 예고가 끝나고 실행이 끝날 때까지 위치는 바뀌지 않는다.
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
            public bool IsFixed;
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
            _freezeServerTime = serverTime + Math.Min(definition.TrackingSeconds, definition.TelegraphSeconds);
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
            if (state != EBossAttackState.Telegraph)
            {
                return;
            }

            // 경계를 건너뛴 틱에는 추적이 끝난 뒤의 현재 좌표를 과거 추적 결과로 쓰지 않는다.
            if (serverTime <= _freezeServerTime)
            {
                Follow(serverTime);
            }

            if (serverTime >= _freezeServerTime)
            {
                FixCenters();
            }
        }

        private void Follow(double serverTime)
        {
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
                FixCenters();
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

        // 추적이 끝나는 시각에 한 번만 알린다. 틱이 거칠어 실행 진입에서 처음 불려도 알림 시각은 추적 종료 시각이다
        private void FixCenters()
        {
            for (int i = 0; i < _planCount; ++i)
            {
                if (_plans[i].IsFixed)
                {
                    continue;
                }

                _plans[i].IsFollowing = false;
                _plans[i].IsFixed     = true;
                _listener.OnCenterFixed(Context.CastInstanceId, _plans[i].AreaId, _plans[i].Center, _freezeServerTime);
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
            // 추적 시간 뒤 예고의 남은 시간과 실행 시간이 모두 고정 구간이다. 둘을 더하면 장판 활성 시각과 같다
            PatternDefinition definition = Context.Definition;
            float followSeconds = Math.Min(definition.TrackingSeconds, definition.TelegraphSeconds);
            float fixedSeconds  = (definition.TelegraphSeconds - followSeconds) + definition.ActiveSeconds;
            _listener.OnMovingStarted(Context.CastInstanceId, areaId, target.CombatId, target.Position,
                Context.Definition.Shape.Range, serverTime, followSeconds, fixedSeconds);
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
