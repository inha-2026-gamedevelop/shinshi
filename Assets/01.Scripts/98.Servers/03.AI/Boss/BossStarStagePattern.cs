// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 단계 수와 단계 각도와 간격과 폭과 피해 배수와 시간은 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    //
    // 기존 회전형 BossStarPattern(별형 관리자의 슬롯과 주기 피해)과 달리 단계마다 한 번 즉발 타격하고 지속 피해를 남기지 않아 관리자를 쓰지 않는다
    // 배치 기하는 BossStarGeometry를 그대로 쓰고 단계 각도만 회전각으로 넘긴다
    //
    // 시작 순간 고정(이후 타깃이 움직이거나 죽어도, 보스가 움직이거나 돌아도 바꾸지 않는다)
    //   전장 중심과 반지름은 전장 입력(IBossArenaSource)에서 얻는다. 얻지 못하거나 배치 반경이 전장보다 크면 시작을 거절한다
    //   기준 방향은 보스에서 유효 타깃을 향한 수평 방향이다. 같은 수평 위치면 보스의 유효 정면을 쓴다. 유효 타깃이 없으면 시작을 거절한다
    //   단계 각도는 0, +간격, +2 x 간격으로 고정이다(기획의 0도, 30도, 60도). 좌우 추첨은 하지 않아 이 패턴은 난수에 의존하지 않는다
    // 거절하면 예고도 알리지 않는다
    //
    // 단계 k의 예정 시각은 시전 시작 + 첫 타격 시간 + k x 간격이다. 첫 타격 시간은 예고 시간(TelegraphSeconds)이고 마지막 단계는 실행 구간이 끝나는 경계에 놓인다
    //   틱 시각이 아니라 예정 시각으로 센다. 한 틱에 둘 이상 도래하면 건너뛰지 않고 순서대로 한 번씩 처리하고, 처리한 단계는 다시 처리하지 않는다
    //   마지막 단계가 실행 끝 경계에 놓이므로 실행 중 틱과 회복 진입 순간에 모두 확인해 틱 순서 때문에 빠지지 않게 한다
    //   과거 위치를 기록하지 않으므로 늦게 처리된 단계는 예정 시각의 대상 위치가 아니라 처리 시점의 현재 위치로 판정한다
    // 판정은 BossStarGeometry.Contains 하나다. 빔이 겹치는 교차점도 참 거짓 하나라 같은 단계에 대상당 한 번만 요청한다
    // 한 단계에 한 번이므로 한 시전에서 대상당 최대 단계 수만큼 맞는다. 단계마다 새 동작 번호를 쓰고 시전 번호는 함께 쓴다
    /// <summary> 월백 하드 2페이즈 단계형 별 모양 빔, 같은 별을 단계 각도만큼 돌려 가며 예고하고 단계마다 즉발 타격한다 </summary>
    public sealed class BossStarStagePattern : BossPatternBase, IBossPatternStartResult
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  MIN_DIRECTION_SQR = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const double STRIKE_EPSILON    = 0.000001;  // 초, 예정 시각 비교의 부동소수 오차 흡수

        // 한 시전의 단계 수의 기술적 상한, 실제 수는 정의의 단계 수가 정한다
        private const int    MAX_STAGES        = 4;

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                _referenceHealth;
        private readonly IBossArenaSource     _arena;
        private readonly IBossStarStageListener _listener;
        private readonly List<ICombatTarget>  _buffer   = new List<ICombatTarget>();
        private readonly int[]                _stageIds = new int[MAX_STAGES];

        private BossStarShape _shape;
        private int           _starId;
        private float         _stepDegrees;
        private int           _stageCount;
        private int           _strikeCount;
        private double        _castStart;
        private bool          _isStarted;
        private bool          _isRejected;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 시작 순간 고정한 배치, 회전각은 단계마다 따로 넘기므로 회전 속도는 쓰지 않는다 </summary>
        public BossStarShape Shape => _shape;

        public int StarId => _starId;

        /// <summary> 단계 하나만큼 돌리는 각도, 항상 양수(오른쪽)다 </summary>
        public float StepDegrees => _stepDegrees;

        public int StageCount => _stageCount;

        /// <summary> 지금까지 처리한 단계 수 </summary>
        public int StrikeCount => _strikeCount;

        /// <summary> 전장 정보나 유효 타깃이 없어 시작하지 못했다 </summary>
        public bool IsStartRejected => _isRejected;

        /****************************************
        *                Methods
        ****************************************/

        public BossStarStagePattern(float referenceHealth, IBossArenaSource arena, IBossStarStageListener listener = null)
        {
            _referenceHealth = referenceHealth;
            _arena           = arena ?? throw new ArgumentNullException(nameof(arena));
            _listener        = listener;
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _isStarted   = false;
            _isRejected  = true;
            _strikeCount = 0;
            _castStart   = serverTime;
            _stageCount  = definition.RawStarStageCount;

            if ((_stageCount < 1) || (_stageCount > MAX_STAGES) || (!_arena.TryGetArena(out Vector3 arenaCenter, out float arenaRadius))
                || (arenaRadius <= 0f) || (definition.StarRingRadius > arenaRadius)
                || (!TryGetValidTarget(out ICombatTarget target)))
            {
                return;
            }

            Vector3 toTarget = target.Position - Context.Self.Position;
            toTarget.y = 0f;
            Vector3 baseDirection = (toTarget.sqrMagnitude >= MIN_DIRECTION_SQR)
                ? toTarget.normalized
                : Flatten(Context.Self.Forward, Vector3.forward);

            _stepDegrees = definition.StarStageDegrees;
            _shape       = BossStarGeometry.Place(arenaCenter, arenaRadius, baseDirection, definition.StarRingRadius,
                definition.StarVertexCount, definition.StarConnectStep, definition.Shape.Width, 0f, true);

            _starId = Context.Damage.IssueInstanceId();
            for (int i = 0; i < _stageCount; ++i)
            {
                _stageIds[i] = Context.Damage.IssueInstanceId();
            }

            _isStarted  = true;
            _isRejected = false;
            _listener?.OnStageSequenceStarted(Context.CastInstanceId, _starId, in _shape, _stepDegrees, _stageCount, _castStart,
                definition.TelegraphSeconds, definition.StarStageIntervalSeconds);
        }

        protected override void OnTick(EBossAttackState state, double serverTime)
        {
            StrikeDue(serverTime);
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            // serverTime은 FSM이 넘기는 단계 경계라 실행 끝 경계에 놓인 마지막 단계가 회복 진입에서도 빠지지 않는다
            StrikeDue(serverTime);
        }

        // 시작하지 못한 시전은 알릴 것이 없다. 시작한 시전은 타격하지 않은 단계의 예고를 정리하라고 알린다
        protected override void OnCancelled(EBossInterruptReason reason)
        {
            if (_isStarted)
            {
                _listener?.OnStageSequenceCancelled(Context.CastInstanceId, _starId, _strikeCount, reason);
            }
        }

        // 예정 시각이 지금까지인 단계를 순서대로 한 번씩 처리한다
        private void StrikeDue(double serverTime)
        {
            while (_isStarted && (!IsCancelled) && (_strikeCount < _stageCount))
            {
                double scheduled = ScheduledTime(_strikeCount);
                if (scheduled > (serverTime + STRIKE_EPSILON))
                {
                    return;
                }

                int stageIndex = _strikeCount;
                ++_strikeCount;
                Strike(stageIndex, scheduled, serverTime);
            }
        }

        private double ScheduledTime(int stageIndex)
        {
            PatternDefinition definition = Context.Definition;
            return _castStart + definition.TelegraphSeconds + (stageIndex * (double)definition.StarStageIntervalSeconds);
        }

        private void Strike(int stageIndex, double scheduledTime, double processedTime)
        {
            PatternDefinition definition = Context.Definition;
            float            rotation    = _stepDegrees * stageIndex;
            float            rawDamage   = definition.DamageRef * _referenceHealth;
            EDamageFlags     flags       = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;
            int              hitCount    = 0;

            // 소환물도 범위 피해를 받는다. 처리 시점의 현재 대상을 단계마다 다시 조회한다
            Context.Targets.GetLivingInRadius(_shape.ArenaCenter, _shape.ArenaRadius, ECombatFaction.Player, true, _buffer);

            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget target = _buffer[i];
                if (!BossStarGeometry.Contains(in _shape, target.Position, rotation))
                {
                    continue;
                }

                DamageRequest request = new DamageRequest(Context.Self.CombatId, target.CombatId, Context.CastInstanceId,
                    _stageIds[stageIndex], rawDamage, flags, _shape.ArenaCenter, definition.DefinitionId);

                if (TryApplyHit(in request, out DamageResult _))
                {
                    ++hitCount;
                    ReportTargetHit(_listener, target, _shape.ArenaCenter);
                }
            }

            _buffer.Clear();
            _listener?.OnStageStruck(Context.CastInstanceId, _starId, stageIndex, rotation, scheduledTime, processedTime, hitCount);
        }

        private bool TryGetValidTarget(out ICombatTarget target)
        {
            if (Context.Targets.TryGet(Context.TargetCombatId, out target)
                && (target.LifeState == EParticipantLifeState.Alive) && (!target.IsSummon)
                && (target.Faction == ECombatFaction.Player))
            {
                return true;
            }

            target = null;
            return false;
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? fallback : direction.normalized;
        }
    }
}
