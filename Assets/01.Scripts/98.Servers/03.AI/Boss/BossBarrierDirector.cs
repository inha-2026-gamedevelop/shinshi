// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 최초 50% 도달의 판정은 이미 있는 보스 두뇌의 페이즈 전환이 한다. 두뇌는 그때 진행 중인 공격과 지속물을 정리하고 새 선택을 멈춘
    // 인계 대기 상태로 들어간다. 이 클래스는 그 상태를 읽기만 하고 보스 두뇌를 고치지 않는다. 시작 조건은 Tick 인자로 받는다
    // 2페이즈 일반 전투 재개는 두뇌의 인계 경로가 필요한데 아직 없다. 완료 알림까지만 내고 인계는 알림을 받는 쪽이 맡는다
    //
    // 피해는 활성 시각부터만 나간다. 예고 중에는 어떤 경우에도 나가지 않으며, 활성 순간이 첫 타격이다
    // 한 틱에 도래한 회차가 둘 이상이면 건너뛰지 않고 모두 처리한다. 과거 위치를 기록하지 않아 놓친 회차도 현재 위치로 판정한다
    // 피해는 이 클래스가 만든 게이트로만 내고 정리하면 영구히 닫는다. 정리 뒤에는 추가 피해가 없다
    /// <summary> 월백 결계 전개 시퀀스, 이동과 예고와 활성과 완료와 정리를 서버 시각으로 진행하고 지속 피해를 낸다 </summary>
    public sealed class BossBarrierDirector
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double ROUND_EPSILON = 0.000001;
        private const float  ARRIVE_EPSILON = 0.05f;
        private const string DEFINITION_ID = "wolbaek_barrier";

        /****************************************
        *                Fields
        ****************************************/

        private readonly BossBarrierSettings    _settings;
        private readonly ICombatTarget          _self;
        private readonly IBossMovement          _movement;
        private readonly ICombatTargetQuery     _targets;
        private readonly IDamageResolver        _resolver;
        private readonly IBossBarrierListener   _listener;
        private readonly Vector3                _arenaCenter;
        private readonly float                  _arenaRadius;
        private readonly float                  _referenceHealth;
        private readonly List<ICombatTarget>    _buffer = new List<ICombatTarget>();

        private BossDamageGate    _gate;
        private BossBarrierStart  _start;
        private bool              _hasStart;
        private bool              _hasStarted;
        private double            _movingSince;
        private double            _lastTickTime;
        private int               _nextHit;
        private int               _castInstanceId;

        /****************************************
        *              Properties
        ****************************************/

        public EBossBarrierState State { get; private set; } = EBossBarrierState.Idle;

        /// <summary> 위험 구역이 피해를 내고 있다, 활성 뒤 정리 전까지만 참이다 </summary>
        public bool IsHazardActive => (State == EBossBarrierState.Deploying) || (State == EBossBarrierState.Completed);

        /// <summary> 전개 시퀀스가 끝났다, 정리돼도 이 전투에서는 참으로 남는다 </summary>
        public bool IsCompleted { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        // arenaCenter와 arenaRadius는 서버 전장 기준이다. referenceHealth는 패턴과 같은 지역 기준 체력(Href)이다
        public BossBarrierDirector(BossBarrierSettings settings, ICombatTarget self, IBossMovement movement,
            ICombatTargetQuery targets, IDamageResolver resolver, Vector3 arenaCenter, float arenaRadius,
            float referenceHealth, IBossBarrierListener listener = null)
        {
            _settings        = settings ?? throw new ArgumentNullException(nameof(settings));
            _self            = self ?? throw new ArgumentNullException(nameof(self));
            _movement        = movement ?? throw new ArgumentNullException(nameof(movement));
            _targets         = targets ?? throw new ArgumentNullException(nameof(targets));
            _resolver        = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _arenaCenter     = arenaCenter;
            _arenaRadius     = arenaRadius;
            _referenceHealth = referenceHealth;
            _listener        = listener;
        }

        /// <summary> 서버 시각으로 한 틱 진행한다. 인계 대기가 처음 참이 되는 틱에 한 번만 시작하고, 이미 시작했거나 정리됐으면 다시 시작하지 않는다 </summary>
        public void Tick(double serverTime, bool isHandoffPending)
        {
            if (State == EBossBarrierState.Idle)
            {
                if ((!isHandoffPending) || _hasStarted)
                {
                    return;
                }

                BeginMoving(serverTime);
            }

            AdvanceSequence(serverTime);

            if (IsHazardActive)
            {
                AdvanceHits(serverTime);
            }

            _lastTickTime = serverTime;
        }

        /// <summary> 사망과 전투 종료로 끊는다, 진행 중이든 완료 뒤든 위험 구역과 피해를 모두 정리한다 </summary>
        public void Cancel(EBossInterruptReason reason)
        {
            if ((State == EBossBarrierState.Idle) || (State == EBossBarrierState.Cleared))
            {
                return;
            }

            if (_gate != null)
            {
                _gate.CloseForGood();
            }

            int barrierId = _hasStart ? _start.BarrierId : 0;
            State = EBossBarrierState.Cleared;
            _listener?.OnBarrierCleared(barrierId, reason);
        }

        /// <summary> 개발용 리셋, 진행 중인 것을 정리하고 처음 상태로 돌려 다시 시작할 수 있게 한다 </summary>
        public void Reset()
        {
            Cancel(EBossInterruptReason.CombatEnd);

            State           = EBossBarrierState.Idle;
            IsCompleted     = false;
            _gate           = null;
            _hasStart       = false;
            _hasStarted     = false;
            _nextHit        = 0;
            _castInstanceId = CombatIds.NONE;
        }

        /// <summary> 늦게 접속한 클라이언트에 줄 현재 값, 예고 전이나 정리된 뒤에는 거짓이다 </summary>
        public bool TryGetSnapshot(out BossBarrierStart start)
        {
            start = _start;
            return _hasStart && (State != EBossBarrierState.Idle) && (State != EBossBarrierState.Cleared);
        }

        private void BeginMoving(double serverTime)
        {
            _hasStarted  = true;
            _movingSince = serverTime;
            _lastTickTime = serverTime;
            State        = EBossBarrierState.Moving;

            // 인계 대기에 들어올 때 두뇌가 이미 공격을 정리했다. 게이트는 이 시퀀스 전용이다
            _gate = new BossDamageGate(_resolver);
            _gate.Open();
        }

        private void AdvanceSequence(double serverTime)
        {
            if (State == EBossBarrierState.Moving)
            {
                MoveBoss(serverTime);
            }

            if ((State == EBossBarrierState.Telegraph) && (serverTime >= _start.ActivateServerTime))
            {
                State    = EBossBarrierState.Deploying;
                _nextHit = 0;
                _listener?.OnBarrierActivated(_start.BarrierId, _start.ActivateServerTime);
            }

            if ((State == EBossBarrierState.Deploying) && (serverTime >= _start.CompleteServerTime))
            {
                State       = EBossBarrierState.Completed;
                IsCompleted = true;
                _listener?.OnBarrierCompleted(_start.BarrierId, _start.CompleteServerTime);
            }
        }

        // 도착하거나 이동 제한 시간이 지나면 예고를 시작한다. 벽에 막혀도 시퀀스가 멈추지 않게 한다
        private void MoveBoss(double serverTime)
        {
            Vector3 position = _self.Position;
            float   distance = Vector3.Distance(Flat(position), Flat(_arenaCenter));

            if (distance > ARRIVE_EPSILON)
            {
                float step = _settings.MoveSpeed * (float)Math.Max(0d, serverTime - _lastTickTime);
                Vector3 next = Vector3.MoveTowards(position, new Vector3(_arenaCenter.x, position.y, _arenaCenter.z), step);
                _movement.Move(position, next, _self.BodyRadius);
            }

            bool arrived  = Vector3.Distance(Flat(_self.Position), Flat(_arenaCenter)) <= ARRIVE_EPSILON;
            bool timedOut = (serverTime - _movingSince) >= _settings.MoveTimeoutSeconds;
            if ((!arrived) && (!timedOut))
            {
                return;
            }

            BeginTelegraph(serverTime);
        }

        private void BeginTelegraph(double serverTime)
        {
            _castInstanceId = _gate.IssueInstanceId();

            float  outer    = _arenaRadius;
            float  inner    = _settings.GetInnerRadius(outer);
            double activate = serverTime + _settings.TelegraphSeconds;
            double complete = activate + _settings.DeploySeconds;

            _start    = new BossBarrierStart(_self.CombatId, _castInstanceId, _arenaCenter, outer, inner,
                _settings.TickInterval, serverTime, activate, complete);
            _hasStart = true;
            State     = EBossBarrierState.Telegraph;
            _listener?.OnBarrierStarted(in _start);
        }

        private void AdvanceHits(double serverTime)
        {
            // 회차 k는 활성 시각 + k x 간격이다. 지금까지 도래한 회차 수만큼 처리한다
            int dueCount = (int)Math.Floor(((serverTime - _start.ActivateServerTime) / _start.TickInterval) + ROUND_EPSILON) + 1;
            while (_nextHit < dueCount)
            {
                Hit();
                ++_nextHit;
            }
        }

        // 바깥 원 안이면서 안쪽 반경 밖인 살아 있는 플레이어와 소환물이 대상이다, 대상 중심점으로 판정한다
        private void Hit()
        {
            _targets.GetLivingInRadius(_start.Center, _start.OuterRadius, ECombatFaction.Player, true, _buffer);

            int actionInstanceId = _gate.IssueInstanceId();
            float rawDamage = _settings.DamageRef * _referenceHealth;
            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget target = _buffer[i];
                if (Vector3.Distance(Flat(target.Position), Flat(_start.Center)) < _start.InnerRadius)
                {
                    continue;
                }

                DamageRequest request = new DamageRequest(_self.CombatId, target.CombatId, _castInstanceId,
                    actionInstanceId, rawDamage, EDamageFlags.Unblockable, _start.Center, DEFINITION_ID);
                _gate.TryApply(in request, out DamageResult _);
            }

            _buffer.Clear();
        }

        private static Vector3 Flat(Vector3 value)
        {
            value.y = 0f;
            return value;
        }
    }
}
