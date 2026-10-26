// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 구역 수와 간격과 전체 각도와 반경과 피해 배수와 시간은 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    //
    // 시작 순간 중심(보스의 서버 위치)과 기준 방향(유효 타깃을 향한 수평 방향)과 좌우를 고정하고 이후 타깃이 움직이거나 죽어도, 보스가 돌아도 바꾸지 않는다
    //   유효 타깃을 조회하지 못하면 시작을 거절한다(보스 위치나 임의 방향으로 대신 공격하지 않는다). 거절하면 두뇌가 기록 없이 기존 재조회로 다시 고른다
    //   타깃과 보스가 같은 수평 위치라 방향을 구할 수 없으면 기존 월백 조준 규칙대로 보스의 유효 정면을 쓴다. 좌우는 두뇌와 같은 난수 의존성으로 각각 50%다
    // 선택한 쪽의 전체 각도를 구역 수로 균등하게 나눈다. 구역 k의 예약 시각은 실행 시작 경계 + k x 간격이고 그때 그 구역을 한 번 폭발한다
    //   첫 구역은 실행 단계에 들어가는 순간, 나머지는 실행 중 틱에서, 실행이 끝나는 경계에 놓인 마지막 구역은 회복 단계에 들어가는 순간 놓치지 않고 낸다
    //   한 틱에 도래한 구역이 둘 이상이면 건너뛰지 않고 순서대로 한 번씩 처리한다. 폭발 시각은 틱 시각이 아니라 예약 시각이다
    //   과거 위치를 기록하지 않으므로 틱이 거칠면 늦게 처리된 구역은 예약 시각의 대상 위치가 아니라 처리 시점의 위치로 판정한다
    // 판정은 1페이즈 부채꼴과 같은 중심점, 경계 포함 규칙(BossFanShape)이고 폭발 시점의 현재 대상을 구역마다 다시 조회한다
    //   피격 기록은 시전 전체에서 하나다. 이미 처리한 대상은 방어, 반격, 무적 결과와 상관없이 다시 판정하지 않는다
    //   그래서 이웃 구역이 공유하는 경계와 보스 중심점의 대상이 두 번 맞지 않고, 밀린 구역이 몰려도 대상당 한 번이다
    // 구역마다 새 동작 번호(구역 번호)를 쓰고 시전 번호는 함께 쓴다. 피해는 게이트로만 낸다
    /// <summary> 월백 반월 연쇄 폭발, 선택한 쪽 반원을 같은 크기의 구역으로 나눠 일정한 간격으로 차례로 폭발한다 </summary>
    public sealed class BossSweepBlastPattern : BossPatternBase, IBossPatternStartResult
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  MIN_DIRECTION_SQR = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const double FIRE_EPSILON      = 0.000001;  // 초, 예약 시각 비교의 부동소수 오차 흡수
        private const int    SIDE_COUNT        = 2;
        private const int    RIGHT_SIDE        = 1;
        private const float  HALF              = 0.5f;

        // 한 시전의 구역 수의 기술적 상한, 실제 수는 정의의 SweepZoneCount가 정한다
        private const int    MAX_ZONES         = 8;

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                      _referenceHealth;
        private readonly IBossSweepTelegraphListener _listener;
        private readonly List<ICombatTarget>        _buffer = new List<ICombatTarget>();
        private readonly HashSet<int>               _processed = new HashSet<int>();
        private readonly int[]                      _zoneIds = new int[MAX_ZONES];

        private Vector3 _origin;
        private Vector3 _base;
        private bool    _isRight;
        private int     _zoneCount;
        private int     _blastCount;
        private double  _executeStart;
        private bool    _isStarted;
        private bool    _isRejected;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 시작 순간 고정한 공격 중심 </summary>
        public Vector3 Origin => _origin;

        /// <summary> 시작 순간 고정한 기준 방향, 수평 단위 벡터 </summary>
        public Vector3 BaseDirection => _base;

        /// <summary> 서버가 추첨한 쪽, 참이면 오른쪽이다 </summary>
        public bool IsRight => _isRight;

        public int ZoneCount => _zoneCount;

        /// <summary> 지금까지 폭발한 구역 수 </summary>
        public int BlastCount => _blastCount;

        /// <summary> 시전 전체에서 이미 처리한 대상 수, 방어와 무적으로 막힌 대상도 센다 </summary>
        public int ProcessedCount => _processed.Count;

        /// <summary> 유효 타깃이 없어 시작하지 못했다 </summary>
        public bool IsStartRejected => _isRejected;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        public BossSweepBlastPattern(float referenceHealth, IBossSweepTelegraphListener listener = null)
        {
            _referenceHealth = referenceHealth;
            _listener        = listener;
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _isStarted  = false;
            _isRejected = false;
            _blastCount = 0;
            _processed.Clear();

            // 난수가 없으면 좌우를 정할 수 없어 임의로 대신하지 않고 거절한다
            if ((!TryGetValidTarget(out ICombatTarget target)) || (Context.Random == null))
            {
                _isRejected = true;
                return;
            }

            _origin = Context.Self.Position;

            Vector3 toTarget = target.Position - _origin;
            toTarget.y = 0f;
            _base = (toTarget.sqrMagnitude >= MIN_DIRECTION_SQR)
                ? toTarget.normalized
                : Flatten(Context.Self.Forward, Vector3.forward);

            _isRight   = Context.Random.NextInt(SIDE_COUNT) == RIGHT_SIDE;
            _zoneCount = Mathf.Clamp(definition.SweepZoneCount, 1, MAX_ZONES);
            for (int i = 0; i < _zoneCount; ++i)
            {
                _zoneIds[i] = Context.Damage.IssueInstanceId();
            }

            _isStarted = true;

            float totalArc = definition.Shape.ArcAngle;
            float zoneArc  = totalArc / _zoneCount;
            _listener?.OnSweepStarted(Context.CastInstanceId, _origin, _base, _isRight, definition.Shape.Range, totalArc,
                _zoneCount, definition.TelegraphSeconds, (_zoneCount - 1) * definition.SweepIntervalSeconds);

            for (int i = 0; i < _zoneCount; ++i)
            {
                _listener?.OnZoneScheduled(Context.CastInstanceId, _zoneIds[i], i, ZoneCenterDirection(i, zoneArc), zoneArc,
                    definition.TelegraphSeconds + (i * definition.SweepIntervalSeconds));
            }
        }

        protected override void OnTick(EBossAttackState state, double serverTime)
        {
            if (state != EBossAttackState.Telegraph)
            {
                BlastDue(serverTime);
            }
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Execute)
            {
                // serverTime은 FSM이 넘기는 예고 끝 경계다
                _executeStart = serverTime;
                BlastDue(serverTime);
                return;
            }

            if (state == EBossAttackState.Recover)
            {
                // 실행이 끝나는 경계에 놓인 마지막 구역이 틱 순서 때문에 빠지지 않게 한 번 더 확인한다. serverTime은 실행 끝 경계다
                BlastDue(serverTime);
            }
        }

        // 시작하지 못한 시전은 알릴 것이 없다. 시작한 시전은 폭발하지 않은 구역을 정리하라고 알린다
        protected override void OnCancelled(EBossInterruptReason reason)
        {
            if (_isStarted)
            {
                _listener?.OnCancelled(Context.CastInstanceId, reason);
            }
        }

        // 예약 시각이 지금까지인 구역을 순서대로 한 번씩 폭발한다
        private void BlastDue(double serverTime)
        {
            if (!_isStarted)
            {
                return;
            }

            PatternDefinition definition = Context.Definition;
            double interval = definition.SweepIntervalSeconds;

            while (_blastCount < _zoneCount)
            {
                double scheduled = _executeStart + (_blastCount * interval);
                if (scheduled > (serverTime + FIRE_EPSILON))
                {
                    return;
                }

                Blast(_blastCount, scheduled);
                ++_blastCount;
            }
        }

        private void Blast(int zoneIndex, double blastTime)
        {
            PatternDefinition definition = Context.Definition;
            float   radius  = definition.Shape.Range;
            float   zoneArc = definition.Shape.ArcAngle / _zoneCount;
            Vector3 center  = ZoneCenterDirection(zoneIndex, zoneArc);

            // 소환물도 범위 피해를 받는다. 폭발 시점의 현재 대상을 구역마다 다시 조회한다
            Context.Targets.GetLivingInRadius(_origin, radius, ECombatFaction.Player, true, _buffer);

            int          actionInstanceId = _zoneIds[zoneIndex];
            float        rawDamage        = definition.DamageRef * _referenceHealth;
            EDamageFlags flags            = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;
            int          hitCount         = 0;

            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget target = _buffer[i];
                if (_processed.Contains(target.CombatId))
                {
                    continue;
                }

                if (!BossFanShape.IsInsideSector(_origin, center, target.Position, radius, zoneArc))
                {
                    continue;
                }

                // 방어, 반격, 무적으로 막혀도 처리한 대상으로 기록해 같은 시전에서 다시 맞지 않게 한다
                _processed.Add(target.CombatId);

                DamageRequest request = new DamageRequest(Context.Self.CombatId, target.CombatId, Context.CastInstanceId,
                    actionInstanceId, rawDamage, flags, _origin, definition.DefinitionId);

                if (TryApplyHit(in request, out DamageResult _))
                {
                    ++hitCount;
                    ReportTargetHit(_listener, target, _origin);
                }
            }

            _buffer.Clear();
            _listener?.OnZoneBlasted(Context.CastInstanceId, actionInstanceId, zoneIndex, hitCount, blastTime);
        }

        // 선택한 쪽으로 기준 방향에서 k번째 구역의 가운데 방향, 오른쪽은 위에서 볼 때 시계 방향이다
        private Vector3 ZoneCenterDirection(int zoneIndex, float zoneArc)
        {
            float side = _isRight ? 1f : -1f;
            return (Quaternion.AngleAxis(side * zoneArc * (zoneIndex + HALF), Vector3.up) * _base).normalized;
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
