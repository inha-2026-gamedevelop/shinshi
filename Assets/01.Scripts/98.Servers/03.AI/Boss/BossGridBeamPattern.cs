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
    // 줄 수와 폭과 줄 간격과 타격 간격과 방향 전환 간격과 피해 배수와 시간은 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    //
    // 빔 관리자(슬롯, 활성 수명, 주기 피해)를 쓰지 않는다. 줄 하나는 한 번 즉발 타격하고 곧바로 안전해져 남는 수명이 없다
    // 이지 P02의 고정 평행 빔(BossBeamPattern, 4줄 예약, 지속 피해)과 실행이 달라 하드 전용 ID에만 이 실행을 연결한다
    //
    // 시작 순간 고정: 전장 중심과 반경은 전장 입력(IBossArenaSource)에서 얻는다. 얻지 못하거나 줄이 전장 지름을 덮지 못하거나 줄 사이에 틈이 있으면 시작을 거절한다
    //   줄은 월드 축 기준이라 보스와 대상의 방향, 위치에 따라 바뀌지 않는다(대상이 필요하지 않아 대상 조회도 하지 않는다)
    //   가로줄 N개를 먼저, 세로줄 N개를 뒤에 놓는다. 거절하면 예고도 알리지 않는다
    //
    // 줄 k의 타격 예정 시각은 가로줄(k < N)이 시전 시작 + 첫 타격 시간 + k x 타격 간격이다
    //   세로줄(k >= N)은 가로 마지막 줄의 타격 시각 + 방향 전환 간격 + (k - N) x 타격 간격이다. 방향 전환 간격은 가로 마지막 타격에서 세로 첫 타격까지의 시간이다
    //   첫 타격 시간은 예고 시간(TelegraphSeconds)이다. 줄의 강조 예고는 타격 예정 시각보다 첫 타격 시간만큼 앞서 시작한다
    //   그래서 첫 줄의 예고는 시전 시작 순간이고, 마지막 줄의 타격은 실행 구간이 끝나는 경계에 놓인다
    //   틱 시각이 아니라 예정 시각으로 센다. 한 틱에 둘 이상 도래하면 건너뛰지 않고 예정 시각 순서대로 한 번씩 처리하고, 처리한 예고와 타격은 다시 처리하지 않는다
    //   마지막 줄이 실행 끝 경계에 놓이므로 실행 중 틱과 회복 진입 순간에 모두 확인해 틱 순서 때문에 빠지지 않게 한다
    //   과거 위치를 기록하지 않으므로 늦게 처리된 줄은 예정 시각의 대상 위치가 아니라 처리 시점의 현재 위치로 판정한다
    //
    // 피해: 줄마다 BossBeamGeometry.Contains 하나로 판정하고 줄 사이 경계는 양쪽 줄이 모두 포함한다. 그래서 대상 번호를 방향별로 기록해 한 시전에서 가로 최대 1회, 세로 최대 1회로 막는다
    //   같은 방향의 이웃한 줄이 경계에서 겹쳐도, 큰 틱으로 한꺼번에 처리돼도, 대상이 줄 사이를 움직여도 방향당 한 번이다. 가로와 세로는 서로 따로 세므로 교차점에서도 총 2회까지다
    //   줄마다 새 동작 번호(줄 번호)를 쓰고 시전 번호는 함께 쓴다
    /// <summary> 월백 하드 2페이즈 격자 순차 빔, 가로 N줄과 세로 N줄을 차례로 예고하고 즉발 타격한다 </summary>
    public sealed class BossGridBeamPattern : BossPatternBase, IBossPatternStartResult
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double STRIKE_EPSILON    = 0.000001; // 초, 예정 시각 비교의 부동소수 오차 흡수
        private const int    AXIS_COUNT        = 2;
        private const int    HIT_CAPACITY      = 8;        // 방향별 피격 기록의 시작 크기, 부족하면 늘어난다

        // 한 방향의 줄 수의 기술적 상한, 실제 수는 정의의 줄 수가 정한다
        private const int    MAX_LINES_PER_AXIS = 16;

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                 _referenceHealth;
        private readonly IBossArenaSource      _arena;
        private readonly IBossGridBeamListener _listener;
        private readonly List<ICombatTarget>   _buffer         = new List<ICombatTarget>();
        private readonly int[]                 _lineIds        = new int[MAX_LINES_PER_AXIS * AXIS_COUNT];
        private readonly List<int>             _horizontalHits = new List<int>(HIT_CAPACITY);
        private readonly List<int>             _verticalHits   = new List<int>(HIT_CAPACITY);

        private BossGridBeamLayout _layout;
        private int                _lineCount;
        private int                _totalLines;
        private int                _telegraphCount;
        private int                _strikeCount;
        private double             _castStart;
        private bool               _isStarted;
        private bool               _isRejected;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 시작 순간 고정한 배치 </summary>
        public BossGridBeamLayout Layout => _layout;

        /// <summary> 한 방향의 줄 수 </summary>
        public int LineCount => _lineCount;

        /// <summary> 지금까지 강조 예고를 처리한 줄 수 </summary>
        public int TelegraphCount => _telegraphCount;

        /// <summary> 지금까지 타격을 처리한 줄 수 </summary>
        public int StrikeCount => _strikeCount;

        /// <summary> 전장 정보가 없거나 줄이 전장을 덮지 못해 시작하지 못했다 </summary>
        public bool IsStartRejected => _isRejected;

        /****************************************
        *                Methods
        ****************************************/

        public BossGridBeamPattern(float referenceHealth, IBossArenaSource arena, IBossGridBeamListener listener = null)
        {
            _referenceHealth = referenceHealth;
            _arena           = arena ?? throw new ArgumentNullException(nameof(arena));
            _listener        = listener;
        }

        /// <summary> 이 시전이 발급한 i번째 줄의 번호, 발사 순서다 </summary>
        public int GetLineId(int sequenceIndex)
        {
            return _lineIds[sequenceIndex];
        }

        /// <summary> 발사 순서 k의 줄 방향, 앞의 N줄은 가로이고 뒤의 N줄은 세로다 </summary>
        public EBossGridBeamAxis GetAxis(int sequenceIndex)
        {
            return (sequenceIndex < _lineCount) ? EBossGridBeamAxis.Horizontal : EBossGridBeamAxis.Vertical;
        }

        /// <summary> 발사 순서 k의 줄 기하, 시작 때 고정한 값이다 </summary>
        public BossBeamShape GetLineShape(int sequenceIndex)
        {
            return _layout.GetLine(GetAxis(sequenceIndex), sequenceIndex % _lineCount);
        }

        /// <summary> 발사 순서 k의 타격 예정 시각(서버 시각) </summary>
        public double GetStrikeTime(int sequenceIndex)
        {
            PatternDefinition definition = Context.Definition;
            double interval = definition.GridStrikeIntervalSeconds;
            double offset;
            if (sequenceIndex < _lineCount)
            {
                offset = sequenceIndex * interval;
            }
            else
            {
                // 세로 첫 줄은 가로 마지막 줄에서 방향 전환 간격만큼 뒤이고, 타격 간격은 같은 방향 안에서만 더한다
                offset = ((_lineCount - 1) * interval) + definition.GridAxisGapSeconds + ((sequenceIndex - _lineCount) * interval);
            }

            return _castStart + definition.TelegraphSeconds + offset;
        }

        /// <summary> 발사 순서 k의 강조 예고 시작 시각(서버 시각), 타격 예정 시각보다 첫 타격 시간만큼 앞선다 </summary>
        public double GetTelegraphTime(int sequenceIndex)
        {
            return GetStrikeTime(sequenceIndex) - Context.Definition.TelegraphSeconds;
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _isStarted      = false;
            _isRejected     = true;
            _telegraphCount = 0;
            _strikeCount    = 0;
            _castStart      = serverTime;
            _lineCount      = definition.RawGridLineCount;
            _totalLines     = _lineCount * AXIS_COUNT;
            _horizontalHits.Clear();
            _verticalHits.Clear();

            if ((_lineCount < 1) || (_lineCount > MAX_LINES_PER_AXIS) || (definition.TelegraphSeconds <= 0f)
                || ((_lineCount > 1) && (definition.GridStrikeIntervalSeconds <= 0f))
                || (!_arena.TryGetArena(out Vector3 arenaCenter, out float arenaRadius)) || (arenaRadius <= 0f))
            {
                return;
            }

            _layout = new BossGridBeamLayout(arenaCenter, arenaRadius, _lineCount, definition.Shape.Width, definition.GridLineSpacing);
            if ((!_layout.IsGapFree) || (!_layout.CoversArena))
            {
                return;
            }

            for (int i = 0; i < _totalLines; ++i)
            {
                _lineIds[i] = Context.Damage.IssueInstanceId();
            }

            _isStarted  = true;
            _isRejected = false;
            // 번호는 위 반복에서 한 계수기로 연달아 발급해 발사 순서대로 1씩 이어진다. 클라이언트 복원은 첫 번호로 나머지를 만든다
            _listener?.OnGridStarted(Context.CastInstanceId, in _layout, _castStart, definition.TelegraphSeconds,
                definition.GridStrikeIntervalSeconds, definition.GridAxisGapSeconds, _lineIds[0]);

            // 첫 줄의 예고 예정 시각이 시전 시작이라 시작 순간에 알린다
            ProcessDue(serverTime);
        }

        protected override void OnTick(EBossAttackState state, double serverTime)
        {
            ProcessDue(serverTime);
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            // serverTime은 FSM이 넘기는 단계 경계라 실행 끝 경계에 놓인 마지막 줄이 회복 진입에서도 빠지지 않는다
            ProcessDue(serverTime);
        }

        // 시작하지 못한 시전은 알릴 것이 없다. 시작한 시전은 타격하지 않은 줄의 예고를 정리하라고 알린다
        protected override void OnCancelled(EBossInterruptReason reason)
        {
            if (_isStarted)
            {
                _listener?.OnGridCancelled(Context.CastInstanceId, _telegraphCount, _strikeCount, reason);
            }
        }

        // 예정 시각이 지금까지인 예고와 타격을 예정 시각 순서대로 한 번씩 처리한다
        // 줄 k의 예고는 항상 타격보다 앞서므로 같은 시각이면 예고를 먼저 처리해 타격 순서가 예고 순서를 앞지르지 않는다
        private void ProcessDue(double serverTime)
        {
            while (_isStarted && (!IsCancelled) && (_strikeCount < _totalLines))
            {
                double strikeTime      = GetStrikeTime(_strikeCount);
                bool   hasTelegraph    = _telegraphCount < _totalLines;
                double telegraphTime   = hasTelegraph ? GetTelegraphTime(_telegraphCount) : double.MaxValue;
                bool   isTelegraphNext = hasTelegraph && (telegraphTime <= (strikeTime + STRIKE_EPSILON));
                double nextTime        = isTelegraphNext ? telegraphTime : strikeTime;

                if (nextTime > (serverTime + STRIKE_EPSILON))
                {
                    return;
                }

                if (isTelegraphNext)
                {
                    int telegraphIndex = _telegraphCount;
                    ++_telegraphCount;
                    Telegraph(telegraphIndex);
                    continue;
                }

                int strikeIndex = _strikeCount;
                ++_strikeCount;
                Strike(strikeIndex, serverTime);
            }
        }

        private void Telegraph(int sequenceIndex)
        {
            _listener?.OnLineTelegraphed(Context.CastInstanceId, _lineIds[sequenceIndex], GetAxis(sequenceIndex),
                sequenceIndex % _lineCount, GetTelegraphTime(sequenceIndex), GetStrikeTime(sequenceIndex));
        }

        private void Strike(int sequenceIndex, double processedTime)
        {
            PatternDefinition definition = Context.Definition;
            EBossGridBeamAxis axis       = GetAxis(sequenceIndex);
            BossBeamShape     shape      = GetLineShape(sequenceIndex);
            List<int>         hits       = (axis == EBossGridBeamAxis.Horizontal) ? _horizontalHits : _verticalHits;
            float             rawDamage  = definition.DamageRef * _referenceHealth;
            EDamageFlags      flags      = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;
            int               hitCount   = 0;

            // 소환물도 범위 피해를 받는다. 처리 시점의 현재 대상을 줄마다 다시 조회한다
            Context.Targets.GetLivingInRadius(shape.ArenaCenter, shape.ArenaRadius, ECombatFaction.Player, true, _buffer);

            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget target = _buffer[i];
                if (hits.Contains(target.CombatId) || (!BossBeamGeometry.Contains(in shape, target.Position)))
                {
                    continue;
                }

                DamageRequest request = new DamageRequest(Context.Self.CombatId, target.CombatId, Context.CastInstanceId,
                    _lineIds[sequenceIndex], rawDamage, flags, shape.LineCenter, definition.DefinitionId);

                // 게이트가 닫혀 요청이 나가지 않았으면 맞은 것으로 기록하지 않는다
                if (TryApplyHit(in request, out DamageResult _))
                {
                    hits.Add(target.CombatId);
                    ++hitCount;
                    ReportTargetHit(_listener, target, shape.LineCenter);
                }
            }

            _buffer.Clear();
            _listener?.OnLineStruck(Context.CastInstanceId, _lineIds[sequenceIndex], axis, sequenceIndex % _lineCount,
                GetStrikeTime(sequenceIndex), processedTime, hitCount);
        }
    }
}
