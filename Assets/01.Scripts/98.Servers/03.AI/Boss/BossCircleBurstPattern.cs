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
    // 시간과 반경과 좌표 크기와 피해 배수는 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    //
    // 이지 P06(BossCircleBlastPattern)은 보스 중심 원 하나만 터뜨린다. 이 패턴은 원 13개를 같은 서버 시각에 터뜨리고
    // 대상 중복 제거와 알림 계약이 달라 하드 전용 ID에만 연결한다
    //
    // 시작 순간 고정: 보스 중심 원은 보스의 서버 위치, 사분면 원 12개는 전장 입력(IBossArenaSource)의 중심 기준 좌표다
    //   처음 타격에도 시전 당시 고정한 중심을 쓴다. 보스 중심 원은 이지 P06과 같은 기존 계약(예고 시작 때 중심 고정)을 따르고,
    //   예고한 곳과 맞는 곳이 달라지지 않게 한다. 전장 정보가 없으면 임의로 대신하지 않고 시작을 거절한다
    //   거절하면 예고도 알리지 않고 두뇌가 기록 없이 기존 재조회로 다시 고른다
    //
    // 시각(시전 시작 기준): 13개 원이 모두 예고 시간이 끝나는 순간 한 번 판정한다. 폭발 뒤에 남는 위험은 없다
    //   큰 틱으로 늦게 처리돼도 폭발은 한 번이고 예정 시각과 실제 처리 시각을 따로 알린다. 대상 위치는 처리 시점의 현재 위치다
    //
    // 판정 모델: 원마다 대상 조회를 하되 대상 번호로 거른다. 여러 원이 겹친 곳의 대상도 피해 요청은 시전당 한 번이다
    //   요청의 기준 위치는 그 대상이 처음 속한 원의 중심이다(보스 중심 원이 먼저, 이어서 사분면 원 순서)
    //   막힌 유효 타격(방어, 반격)도 피격으로 센다. 무적이나 사망 등 피해가 들어가지 않은 요청은 세지 않는다
    //
    // 정리: 취소, 사망, 페이즈 전환, 전투 종료는 BossPatternBase가 취소 뒤 피해 요청을 막는다. 이 패턴은 자기 밖에 남는 지속물이 없다
    /// <summary> 월백 하드 2페이즈 동시 원형 폭발, 보스 중심 원과 사분면 고정 원 12개를 같은 순간에 터뜨린다 </summary>
    public sealed class BossCircleBurstPattern : BossPatternBase, IBossPatternStartResult
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double TIME_EPSILON  = 0.000001; // 초, 예정 시각 비교의 부동소수 오차 흡수
        private const int    SEEN_CAPACITY = 8;        // 대상 번호 기록의 시작 크기, 부족하면 늘어난다

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                    _referenceHealth;
        private readonly IBossArenaSource         _arena;
        private readonly IBossCircleBurstListener _listener;
        private readonly List<ICombatTarget>      _buffer = new List<ICombatTarget>();
        private readonly List<int>                _seen   = new List<int>(SEEN_CAPACITY);

        private BossCircleBurstLayout _layout;
        private double                _castStart;
        private double                _lastTickTime;
        private double                _processedTime;
        private int                   _hitCount;
        private bool                  _isStarted;
        private bool                  _isStruck;
        private bool                  _isRejected;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 시작 순간 고정한 배치 </summary>
        public BossCircleBurstLayout Layout => _layout;

        /// <summary> 폭발했다 </summary>
        public bool IsStruck => _isStruck;

        /// <summary> 폭발에서 확정한 피격 대상 수, 방어와 반격으로 막힌 유효 타격을 포함한다 </summary>
        public int HitCount => _hitCount;

        /// <summary> 전장 정보가 없거나 정의가 맞지 않아 시작하지 못했다 </summary>
        public bool IsStartRejected => _isRejected;

        /// <summary> 폭발 예정 서버 시각, 시전 시작 + 예고 시간이다 </summary>
        public double StrikeTime => _castStart + Context.Definition.TelegraphSeconds;

        /// <summary> 폭발을 실제로 처리한 서버 시각, 폭발 전에는 0이다 </summary>
        public double ProcessedTime => _processedTime;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        public BossCircleBurstPattern(float referenceHealth, IBossArenaSource arena, IBossCircleBurstListener listener)
        {
            _referenceHealth = referenceHealth;
            _arena           = arena ?? throw new ArgumentNullException(nameof(arena));
            _listener        = listener ?? throw new ArgumentNullException(nameof(listener));
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _isStarted     = false;
            _isRejected    = true;
            _isStruck      = false;
            _hitCount      = 0;
            _processedTime = 0.0;
            _castStart     = serverTime;
            _lastTickTime  = serverTime;
            _seen.Clear();

            if ((definition.TelegraphSeconds <= 0f) || (definition.Shape.Range <= 0f) || (definition.BurstSpotRadius <= 0f)
                || (definition.BurstNearOffset <= 0f) || (definition.BurstFarOffset <= definition.BurstNearOffset)
                || (!_arena.TryGetArena(out Vector3 arenaCenter, out float arenaRadius)) || (arenaRadius <= 0f))
            {
                return;
            }

            _layout = new BossCircleBurstLayout(Context.Self.Position, arenaCenter, definition.Shape.Range,
                definition.BurstSpotRadius, definition.BurstNearOffset, definition.BurstFarOffset);

            _isStarted  = true;
            _isRejected = false;
            _listener.OnBurstStarted(Context.CastInstanceId, in _layout, _castStart, definition.TelegraphSeconds);
        }

        protected override void OnTick(EBossAttackState state, double serverTime)
        {
            _lastTickTime = Math.Max(_lastTickTime, serverTime);
            TryStrike(serverTime);
        }

        // FSM은 단계 경계 시각을 넘긴다. 틱이 먼저 처리하므로 보통은 이미 폭발했고, 직접 단계를 넘기는 호출에서도 빠지지 않게 한다
        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Execute)
            {
                TryStrike(Math.Max(serverTime, _lastTickTime));
            }
        }

        // 시작하지 못한 시전은 알릴 것이 없다. 시작한 시전은 남은 예고를 정리하라고 알린다
        protected override void OnCancelled(EBossInterruptReason reason)
        {
            if (_isStarted)
            {
                _listener.OnBurstCancelled(Context.CastInstanceId, _isStruck, reason);
            }
        }

        // processedTime은 이 폭발을 실제로 처리하는 서버 시각이다. 예정 시각보다 이르면 아직 폭발하지 않는다
        private void TryStrike(double processedTime)
        {
            if ((!_isStarted) || _isStruck || IsCancelled)
            {
                return;
            }

            if (processedTime < (StrikeTime - TIME_EPSILON))
            {
                return;
            }

            Strike(processedTime);
        }

        private void Strike(double processedTime)
        {
            PatternDefinition definition = Context.Definition;
            float        rawDamage = definition.DamageRef * _referenceHealth;
            EDamageFlags flags     = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;

            _isStruck      = true;
            _processedTime = processedTime;

            // 13개 원이 한 동작이다. 대상이 여러 원에 속해도 아래에서 대상 번호로 걸러 요청은 한 번만 나가고, 같은 동작 번호라 resolver의 중복 거부도 같은 기준을 쓴다
            int actionInstanceId = Context.Damage.IssueInstanceId();

            for (int i = 0; i < _layout.Count; ++i)
            {
                // 소환물도 범위 피해를 받는다. 조회 계약이 결과 목록을 비운다고 가정하지 않고 직접 비운다
                _buffer.Clear();
                Vector3 center = _layout.GetCenter(i);
                Context.Targets.GetLivingInRadius(center, _layout.GetRadius(i), ECombatFaction.Player, true, _buffer);

                for (int k = 0; k < _buffer.Count; ++k)
                {
                    ICombatTarget target = _buffer[k];
                    if (_seen.Contains(target.CombatId))
                    {
                        continue;
                    }

                    _seen.Add(target.CombatId);

                    DamageRequest request = new DamageRequest(Context.Self.CombatId, target.CombatId, Context.CastInstanceId,
                        actionInstanceId, rawDamage, flags, center, definition.DefinitionId);

                    if (TryApplyHit(in request, out DamageResult result) && CountsAsHit(result.Outcome))
                    {
                        ++_hitCount;
                    }
                }
            }

            _buffer.Clear();
            _seen.Clear();

            if (!IsCancelled)
            {
                _listener.OnBurstStruck(Context.CastInstanceId, StrikeTime, processedTime, _hitCount);
            }
        }

        // 적용과 막힘(방어, 반격)은 유효 타격이다
        private static bool CountsAsHit(EDamageOutcome outcome)
        {
            return (outcome == EDamageOutcome.Applied) || (outcome == EDamageOutcome.Guarded);
        }
    }
}
