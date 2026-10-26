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
    // 구역 수와 간격과 전체 각도와 최소 사거리와 피해 배수와 시간과 잔류 시간과 재피격 간격과 최대 피격 횟수는 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    //
    // 이지 P03(BossSweepBlastPattern)은 반월을 한 번만 터뜨리고 잔류가 없으며 시전 전체에서 대상당 한 번만 맞는다
    // 이 패턴은 폭발한 구역이 마지막 폭발 뒤 잔류 시간까지 위험으로 남고 같은 대상이 간격을 두고 다시 맞을 수 있어 실행 규칙이 달라 하드 전용 ID에만 연결한다
    //
    // 시작 순간 고정: 중심은 보스의 서버 위치, 피난 방향은 서버 난수로 한 번 추첨한 각도, 전장 중심과 반경은 전장 입력(IBossArenaSource)이다
    //   난수나 전장 정보가 없으면 임의로 대신하지 않고 시작을 거절한다. 거절하면 예고도 알리지 않고 두뇌가 기록 없이 기존 재조회로 다시 고른다
    //   피난 방향은 시작 뒤 다시 추첨하지 않는다. 다른 장판이나 투사체가 그 방향에 겹쳐도, 대상이 움직이거나 죽어도, 보스가 움직여도 바꾸지 않는다
    //   대상이 필요하지 않아 선택된 대상은 조회하지 않는다. 판정마다 그 시점의 살아 있는 플레이어를 새로 조회한다
    //
    // 시각(시전 시작 기준): 구역 k의 폭발 = 예고 시간 + k x 간격. 마지막 폭발은 실행 구간이 끝나는 경계에 놓인다
    //   마지막 폭발 + 잔류 시간 = 위험 종료. 잔류 시간은 회복 시간 안이라 패턴이 끝나기 전에 위험이 먼저 사라진다
    //
    // 판정 모델: 판정 시각마다 그 순간 폭발해 있는 구역(0부터 폭발한 수만큼)의 합집합 안에 있는 대상을 본다
    //   판정 시각은 구역 폭발 시각과 틱 시각이다. 틱 사이를 보간하거나 과거 위치를 복원하지 않는다
    //   한 대상의 재피격은 직전 피격 판정 시각에서 재피격 간격 이상 지나야 하고 시전 전체에서 최대 피격 횟수를 넘지 않는다
    //   막힌 유효 타격(방어, 반격)도 피격 횟수와 간격에 센다. 무적이나 사망 등 피해가 들어가지 않은 요청은 세지 않는다
    //   한 틱에 여러 폭발 시각이 도래하면 건너뛰지 않고 시각 순서대로 한 번씩 처리한다. 폭발의 판정은 틱 시각이 아니라 예정 시각을 기준으로 간격을 센다
    //   큰 틱으로 늦게 처리돼도 대상 위치는 처리 시점의 현재 위치다
    //   위험 종료 시각 그 순간까지는 위험이고, 그보다 늦은 틱은 판정 없이 위험을 끝낸다. 서버가 크게 밀려도 종료 뒤에는 피해를 내지 않는다
    //   판정은 전장 원 안에서만 한다(BossChainFanLayout). 피해는 게이트로만 낸다
    //
    // 정리: 취소, 사망, 페이즈 전환, 전투 종료는 BossPatternBase가 취소 뒤 피해 요청을 막는다. 이 패턴은 자기 밖에 남는 지속물이 없다
    /// <summary> 월백 하드 2페이즈 잔류 부채꼴 연쇄, 피난 방향 하나를 남기고 나머지를 구역으로 나눠 차례로 폭발하며 폭발한 구역은 잔류 시간까지 위험하다 </summary>
    public sealed class BossChainFanPattern : BossPatternBase, IBossPatternStartResult
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double TIME_EPSILON      = 0.000001; // 초, 예정 시각 비교의 부동소수 오차 흡수
        private const float  FULL_TURN         = 360f;
        private const int    HIT_CAPACITY      = 8;        // 대상별 피격 기록의 시작 크기, 부족하면 늘어난다

        // 한 시전의 구역 수의 기술적 상한, 실제 수는 정의의 구역 수가 정한다
        private const int    MAX_ZONES         = 8;

        /****************************************
        *                Fields
        ****************************************/

        // 대상 한 명의 피격 기록, 배열 안에서 제자리 수정하는 내부 값 묶음이라 필드를 public으로 둔다
        private struct HitRecord
        {
            public int    CombatId;
            public int    Hits;
            public double LastHitTime;
        }

        private readonly float                _referenceHealth;
        private readonly IBossArenaSource     _arena;
        private readonly IBossChainFanListener _listener;
        private readonly List<ICombatTarget>  _buffer  = new List<ICombatTarget>();
        private readonly List<HitRecord>      _records = new List<HitRecord>(HIT_CAPACITY);

        private BossChainFanLayout _layout;
        private int                _zoneCount;
        private int                _strikeCount;
        private double             _castStart;
        private double             _lastSampleTime;
        private bool               _isDangerEnded;
        private bool               _isStarted;
        private bool               _isRejected;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 시작 순간 고정한 배치 </summary>
        public BossChainFanLayout Layout => _layout;

        public int ZoneCount => _zoneCount;

        /// <summary> 지금까지 폭발을 처리한 구역 수 </summary>
        public int StrikeCount => _strikeCount;

        /// <summary> 잔류 시간이 끝나 더는 위험하지 않다 </summary>
        public bool IsDangerEnded => _isDangerEnded;

        /// <summary> 전장 정보나 난수가 없어 시작하지 못했다 </summary>
        public bool IsStartRejected => _isRejected;

        /// <summary> 위험이 끝나는 서버 시각, 마지막 폭발 시각 + 잔류 시간이다 </summary>
        public double DangerEndTime => GetStrikeTime(_zoneCount - 1) + Context.Definition.ChainLingerSeconds;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        public BossChainFanPattern(float referenceHealth, IBossArenaSource arena, IBossChainFanListener listener)
        {
            _referenceHealth = referenceHealth;
            _arena           = arena ?? throw new ArgumentNullException(nameof(arena));
            _listener        = listener ?? throw new ArgumentNullException(nameof(listener));
        }

        /// <summary> 구역 k의 폭발 예정 시각(서버 시각) </summary>
        public double GetStrikeTime(int zoneIndex)
        {
            PatternDefinition definition = Context.Definition;
            return _castStart + definition.TelegraphSeconds + (zoneIndex * (double)definition.SweepIntervalSeconds);
        }

        /// <summary> 대상이 이 시전에서 지금까지 맞은 횟수, 막힌 유효 타격을 포함한다 </summary>
        public int GetHitCount(int combatId)
        {
            int index = FindRecord(combatId);
            return (index < 0) ? 0 : _records[index].Hits;
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _isStarted      = false;
            _isRejected     = true;
            _strikeCount    = 0;
            _isDangerEnded  = false;
            _castStart      = serverTime;
            _lastSampleTime = double.MinValue;
            _zoneCount      = definition.RawSweepZoneCount;
            _records.Clear();

            float totalArc = definition.Shape.ArcAngle;
            if ((_zoneCount < 1) || (_zoneCount > MAX_ZONES) || (totalArc <= 0f) || (totalArc >= FULL_TURN)
                || (definition.TelegraphSeconds <= 0f) || ((_zoneCount > 1) && (definition.SweepIntervalSeconds <= 0f))
                || (definition.ChainLingerSeconds <= 0f) || (definition.ChainHitCooldownSeconds <= 0f)
                || (definition.ChainMaxHitsPerTarget < 1) || (definition.ChainSafeRevealSeconds <= 0f)
                || (definition.ChainSafeRevealSeconds >= definition.TelegraphSeconds)
                || (Context.Random == null) || (!_arena.TryGetArena(out Vector3 arenaCenter, out float arenaRadius)) || (arenaRadius <= 0f))
            {
                return;
            }

            // 기준 방향은 월드 정면(+Z)에서 시계 방향으로 추첨한 각도다. 대상 방향에 묶지 않아 한 시전의 안전한 곳이 대상 발밑으로 정해지지 않는다
            float safeAngle = Context.Random.NextFloat01() * FULL_TURN;
            Vector3 safeDirection = (Quaternion.AngleAxis(safeAngle, Vector3.up) * Vector3.forward).normalized;

            _layout = new BossChainFanLayout(Context.Self.Position, safeDirection, definition.Shape.Range, _zoneCount,
                totalArc / _zoneCount, arenaCenter, arenaRadius);

            _isStarted  = true;
            _isRejected = false;
            _listener.OnChainFanStarted(Context.CastInstanceId, in _layout, _castStart, definition.ChainSafeRevealSeconds,
                definition.TelegraphSeconds, definition.SweepIntervalSeconds, definition.ChainLingerSeconds);
        }

        protected override void OnTick(EBossAttackState state, double serverTime)
        {
            ProcessDue(serverTime);
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            // serverTime은 FSM이 넘기는 단계 경계라 실행 끝 경계에 놓인 마지막 폭발이 회복 진입에서도 빠지지 않는다
            ProcessDue(serverTime);
        }

        // 시작하지 못한 시전은 알릴 것이 없다. 시작한 시전은 폭발하지 않은 구역과 남은 잔류 위험을 정리하라고 알린다
        protected override void OnCancelled(EBossInterruptReason reason)
        {
            if (_isStarted)
            {
                _listener.OnChainFanCancelled(Context.CastInstanceId, _strikeCount, _isDangerEnded, reason);
            }
        }

        // 예정 시각이 지금까지인 폭발과 위험 종료를 시각 순서대로 한 번씩 처리한 뒤 이 틱 시각의 잔류 판정을 한다
        private void ProcessDue(double serverTime)
        {
            while (_isStarted && (!IsCancelled) && (!_isDangerEnded))
            {
                if (_strikeCount < _zoneCount)
                {
                    double strikeTime = GetStrikeTime(_strikeCount);
                    if (strikeTime > (serverTime + TIME_EPSILON))
                    {
                        break;
                    }

                    int zoneIndex = _strikeCount;
                    ++_strikeCount;
                    Strike(zoneIndex, strikeTime, serverTime);
                    continue;
                }

                // 위험 종료 시각 그 순간까지는 위험이라 그 틱의 판정을 먼저 한다. 그보다 늦은 틱에서만 끝내고 판정은 하지 않는다
                // 종료 이후에 들어온 대상이 늦은 틱 때문에 맞는 일이 없게 하려는 것이다
                double endTime = DangerEndTime;
                if (serverTime <= (endTime + TIME_EPSILON))
                {
                    break;
                }

                _isDangerEnded = true;
                if (!IsCancelled)
                {
                    _listener.OnChainFanDangerEnded(Context.CastInstanceId, endTime);
                }
            }

            if ((!_isStarted) || IsCancelled || _isDangerEnded || (_strikeCount == 0))
            {
                return;
            }

            // FSM이 단계 경계로 되돌아 부르는 낮은 시각은 이미 지난 판정이라 다시 하지 않는다
            if (serverTime > (_lastSampleTime + TIME_EPSILON))
            {
                Sample(serverTime, _strikeCount);
            }
        }

        private void Strike(int zoneIndex, double scheduledTime, double processedTime)
        {
            int hitCount = Sample(scheduledTime, zoneIndex + 1);
            if (!IsCancelled)
            {
                _listener.OnChainFanBlasted(Context.CastInstanceId, zoneIndex, scheduledTime, processedTime, hitCount);
            }
        }

        // sampleTime의 판정 한 번, 폭발해 있는 activeZones개 구역의 합집합 안에서 간격과 횟수 조건을 통과한 대상에게 피해를 낸다
        private int Sample(double sampleTime, int activeZones)
        {
            PatternDefinition definition = Context.Definition;
            int          maxHits   = definition.ChainMaxHitsPerTarget;
            double       cooldown  = definition.ChainHitCooldownSeconds;
            float        rawDamage = definition.DamageRef * _referenceHealth;
            EDamageFlags flags     = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;
            int          actionId  = CombatIds.NONE;
            int          hitCount  = 0;

            _lastSampleTime = Math.Max(_lastSampleTime, sampleTime);

            // 소환물도 범위 피해를 받는다. 판정 시점의 현재 대상을 매번 다시 조회한다
            Context.Targets.GetLivingInRadius(_layout.Origin, _layout.Range, ECombatFaction.Player, true, _buffer);

            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget target = _buffer[i];
                int recordIndex = FindRecord(target.CombatId);
                if (recordIndex >= 0)
                {
                    HitRecord known = _records[recordIndex];
                    if ((known.Hits >= maxHits) || ((sampleTime - known.LastHitTime) < (cooldown - TIME_EPSILON)))
                    {
                        continue;
                    }
                }

                if (!_layout.ContainsAny(activeZones, target.Position))
                {
                    continue;
                }

                // 같은 판정 시각의 요청은 한 동작이다. 판정 시각이 다르면 새 동작 번호라 같은 대상의 재피격이 중복으로 거부되지 않는다
                if (actionId == CombatIds.NONE)
                {
                    actionId = Context.Damage.IssueInstanceId();
                }

                DamageRequest request = new DamageRequest(Context.Self.CombatId, target.CombatId, Context.CastInstanceId,
                    actionId, rawDamage, flags, _layout.Origin, definition.DefinitionId);

                // 게이트가 닫혔거나 피해가 들어가지 않은 요청(무적, 사망, 중복)은 맞은 것으로 기록하지 않는다
                if ((!TryApplyHit(in request, out DamageResult result)) || (!CountsAsHit(result.Outcome)))
                {
                    continue;
                }

                Record(target.CombatId, recordIndex, sampleTime);
                ++hitCount;
                ReportTargetHit(_listener, target, _layout.Origin);
            }

            _buffer.Clear();
            return hitCount;
        }

        // 적용과 막힘(방어, 반격)은 유효 타격이다
        private static bool CountsAsHit(EDamageOutcome outcome)
        {
            return (outcome == EDamageOutcome.Applied) || (outcome == EDamageOutcome.Guarded);
        }

        private void Record(int combatId, int recordIndex, double hitTime)
        {
            if (recordIndex < 0)
            {
                _records.Add(new HitRecord { CombatId = combatId, Hits = 1, LastHitTime = hitTime });
                return;
            }

            HitRecord record = _records[recordIndex];
            ++record.Hits;
            record.LastHitTime = hitTime;
            _records[recordIndex] = record;
        }

        private int FindRecord(int combatId)
        {
            for (int i = 0; i < _records.Count; ++i)
            {
                if (_records[i].CombatId == combatId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
