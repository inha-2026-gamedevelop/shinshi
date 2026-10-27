// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 슬롯 순서: 예약(TryReserve, 예고 시작) -> 활성(TryActivate, 예약을 장판으로 전환) -> 사라짐(Expired 또는 Cancelled)
    // 장판 한 개는 장판 번호(AreaId)로 구분하고 시전 번호는 같은 시전이 설치한 장판들의 묶음이다. 시전 번호를 넘긴 정리(Cancel)만 그 시전의 장판을 함께 지우고, 장판의 수명 종료는 그 장판 하나만 끝낸다
    // 예약과 활성 장판을 같은 슬롯 하나로 세므로 ActiveAreaCount는 예약 + 활성의 합이고 이중 집계가 없다. 슬롯은 사라지거나 예약을 풀 때 정확히 한 번 돌아온다
    // 이미 상한이면 예약이 실패하고 기존 장판은 지우지 않는다
    //
    // 시간 처리(서버 시각, 과거 위치 기록 없음)
    // 회차 k는 활성 시각 + k x 간격에 도래하며 k x 간격이 지속 시간보다 작은 k만 있다(6초, 1초면 0~5의 6회). 종료 시각의 회차는 없고 종료 때 추가 타격도 없다
    // 활성 순간(k = 0)의 첫 타격은 TryActivate가 아니라 Tick이 처리한다. 패턴 진입과 Tick 두 곳에서 중복해 맞는 일이 없다. BossCombat이 같은 틱 안에서 공격 진행 직후 Tick을 부르므로 지연은 없다
    // 월백 장판 패턴은 모두 SkipMissedRounds로 설치한다(확정 규칙). 지연된 틱에서 장판 하나당 한 틱에 가장 최근 도래한 회차만 판정하고 이전 회차는 건너뛰며,
    // 다음 회차는 원래 활성 시각 기준이고 종료 시각을 지났으면 미처리 회차 없이 종료한다. 정상 진행의 총 회수는 그대로다
    // 아래 두 줄은 SkipMissedRounds가 꺼진 스펙(관리자를 직접 쓰는 호출)의 기본 동작이다
    // 한 틱에 도래한 회차가 둘 이상(틱 간격이 장판 간격보다 길 때)이면 놓친 회차를 건너뛰지 않고 모두 처리한다. 총 회수를 지키기 위해서다
    //   다만 과거 위치를 기록하지 않으므로 놓친 회차도 틱 시각의 현재 위치로 판정한다. 그 시각에 있던 대상을 정확히 복원한다고 주장하지 않는다
    //   회차마다 새 동작 번호를 쓰므로 한 틱에 같은 대상이 여러 회차를 맞을 수 있다
    // 종료 시각을 건너뛴 틱에서는 종료 전에 도래한 회차를 모두 처리한 뒤 종료한다. 종료 이후의 회차는 없다
    // 취소(사망, 페이즈 전환, 전투 종료)는 항상 우선한다. BossBrain이 게이트를 닫고 Cancel을 부른 뒤에는 처리하지 못한 회차를 모두 버리고 추가 피해를 내지 않는다
    // 같은 서버 시각의 순서는 서버 틱이 플레이어 이동과 공격을 먼저 처리한 뒤 보스를 진행하므로, 도래한 회차는 그 틱의 이동이 반영된 현재 위치로 판정한다
    //
    // 판정은 월백 원형 패턴과 같다. 중심에서 수평 거리가 반경 이하(경계 포함)인 살아 있는 플레이어와 소환물이 대상이고 대상 중심점으로 판정하며 BodyRadius는 쓰지 않는다. 표현이 같은 원이다
    /// <summary> 월백 장판의 수명 관리자, 슬롯 상한과 주기 피해와 정리를 맡는다 </summary>
    public sealed class BossAreaManager : IBossEffectLifetime, IBossTickableEffects, IBossHardAreaHost, IBossAreaCapacity
    {
        /****************************************
        *              Constants
        ****************************************/

        // 동시에 가질 수 있는 슬롯 수의 기술적 상한, 실제 동시 개수는 패턴 정의의 MaxConcurrentAreas가 정한다
        private const int    MAX_SLOTS        = 8;
        private const double ROUND_EPSILON    = 0.000001;

        /****************************************
        *                Fields
        ****************************************/

        private enum ESlotState
        {
            Free,
            Reserved,
            Active
        }

        // 장판 하나의 슬롯, 미리 만들어 재사용한다
        private sealed class Slot
        {
            public ESlotState  State;
            public int         CastInstanceId;
            public int         AreaId;
            public string      PatternId;
            public BossAreaSpec Spec;
            public int         HitCount;
            public int         NextHit;
        }

        private readonly ICombatTargetQuery    _targets;
        private readonly IBossAreaListener     _listener;
        private readonly Slot[]                _slots  = new Slot[MAX_SLOTS];
        private readonly List<ICombatTarget>   _buffer = new List<ICombatTarget>();

        private int    _reservedCount;
        private int    _activeCount;
        private double _lastServerTime;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 예고 중이라 예약만 한 슬롯 수 </summary>
        public int ReservedCount => _reservedCount;

        /// <summary> 활성되어 피해를 내고 있는 장판 수 </summary>
        public int ActiveCount => _activeCount;

        /// <summary> 패턴과 상관없는 예약과 활성 장판의 합, 보스 전체 장판 상한 검사가 쓴다 </summary>
        public int TotalAreaCount => _reservedCount + _activeCount;

        /****************************************
        *                Methods
        ****************************************/

        public BossAreaManager(ICombatTargetQuery targets, IBossAreaListener listener = null)
        {
            _targets  = targets ?? throw new ArgumentNullException(nameof(targets));
            _listener = listener;

            for (int i = 0; i < _slots.Length; ++i)
            {
                _slots[i] = new Slot();
            }
        }

        public bool TryReserve(int castInstanceId, string patternId, int maxConcurrent)
        {
            return Reserve(castInstanceId, castInstanceId, patternId, maxConcurrent, false);
        }

        public bool TryReserve(int castInstanceId, int areaId, string patternId, int maxConcurrent)
        {
            return Reserve(castInstanceId, areaId, patternId, maxConcurrent, false);
        }

        public bool TryReserveWithinTotal(int castInstanceId, int areaId, string patternId, int maxTotal)
        {
            return Reserve(castInstanceId, areaId, patternId, maxTotal, true);
        }

        public bool TryReserveReplacing(int castInstanceId, int areaId, string patternId)
        {
            return Reserve(castInstanceId, areaId, patternId, 0, false);
        }

        public bool TryActivateReplacing(in BossAreaSpec spec, int maxActive)
        {
            Slot reserved = Find(spec.AreaId, ESlotState.Reserved);
            if ((maxActive <= 0) || (reserved == null) || (reserved.CastInstanceId != spec.CastInstanceId)
                || (reserved.PatternId != spec.DefinitionId)
                || (spec.Gate == null) || (spec.Radius <= 0f) || (spec.DurationSeconds <= 0f) || (spec.TickInterval <= 0f))
            {
                return false;
            }

            int active = 0;
            Slot oldest = null;
            for (int i = 0; i < _slots.Length; ++i)
            {
                Slot slot = _slots[i];
                if ((slot.State != ESlotState.Active) || (slot.PatternId != spec.DefinitionId))
                {
                    continue;
                }

                ++active;
                if ((oldest == null) || (slot.Spec.ActivateServerTime < oldest.Spec.ActivateServerTime)
                    || ((slot.Spec.ActivateServerTime == oldest.Spec.ActivateServerTime) && (slot.AreaId < oldest.AreaId)))
                {
                    oldest = slot;
                }
            }

            if (active >= maxActive)
            {
                End(oldest, EBossAreaEnd.Cancelled, spec.ActivateServerTime);
            }

            return TryActivate(in spec);
        }

        // limitIsTotal이 참이면 limit을 보스의 모든 장판 수와 비교하고 거짓이면 같은 패턴의 장판 수와 비교한다. 검사와 예약이 한 번에 이뤄진다
        private bool Reserve(int castInstanceId, int areaId, string patternId, int limit, bool limitIsTotal)
        {
            if ((castInstanceId == CombatIds.NONE) || (areaId == CombatIds.NONE) || string.IsNullOrEmpty(patternId))
            {
                return false;
            }

            int samePattern = 0;
            int total = 0;
            Slot free = null;
            for (int i = 0; i < _slots.Length; ++i)
            {
                Slot slot = _slots[i];
                if (slot.State == ESlotState.Free)
                {
                    free = (free == null) ? slot : free;
                    continue;
                }

                // 같은 장판 번호의 예약이 이미 있으면 두 번 세지 않는다. 같은 시전의 다른 장판은 별개다
                if (slot.AreaId == areaId)
                {
                    return false;
                }

                ++total;
                if (slot.PatternId == patternId)
                {
                    ++samePattern;
                }
            }

            int counted = limitIsTotal ? total : samePattern;
            if ((free == null) || ((limit > 0) && (counted >= limit)))
            {
                return false;
            }

            free.State          = ESlotState.Reserved;
            free.AreaId         = areaId;
            free.CastInstanceId = castInstanceId;
            free.PatternId      = patternId;
            ++_reservedCount;
            return true;
        }

        public void ReleaseReservation(int areaId)
        {
            Slot slot = Find(areaId, ESlotState.Reserved);
            if (slot != null)
            {
                Free(slot);
            }
        }

        public bool TryActivate(in BossAreaSpec spec)
        {
            Slot slot = Find(spec.AreaId, ESlotState.Reserved);
            if (slot == null)
            {
                return false;
            }

            if ((spec.Gate == null) || (spec.Radius <= 0f) || (spec.DurationSeconds <= 0f) || (spec.TickInterval <= 0f))
            {
                Free(slot);
                return false;
            }

            slot.State    = ESlotState.Active;
            slot.Spec     = spec;
            slot.HitCount = ComputeHitCount(spec.DurationSeconds, spec.TickInterval);
            slot.NextHit  = 0;
            --_reservedCount;
            ++_activeCount;

            _listener?.OnAreaActivated(spec.CastInstanceId, spec.AreaId, spec.Center, spec.Radius, spec.DurationSeconds,
                spec.TickInterval, spec.ActivateServerTime);
            return true;
        }

        public void Tick(double serverTime)
        {
            _lastServerTime = serverTime;
            if (_activeCount == 0)
            {
                return;
            }

            for (int i = 0; i < _slots.Length; ++i)
            {
                if (_slots[i].State == ESlotState.Active)
                {
                    Advance(_slots[i], serverTime);
                }
            }
        }

        /// <summary> 사유별로 정리한다. 직접 제어는 이미 활성한 장판을 지우지 않으므로 무시하고, 나머지는 시전 번호가 NONE이면 예약과 활성 장판을 모두 즉시 제거한다 </summary>
        public void Cancel(EBossInterruptReason reason, int castInstanceId)
        {
            if ((reason == EBossInterruptReason.DirectControl) || (reason == EBossInterruptReason.None)
                || ((_activeCount + _reservedCount) == 0))
            {
                return;
            }

            for (int i = 0; i < _slots.Length; ++i)
            {
                Slot slot = _slots[i];
                if ((slot.State == ESlotState.Free)
                    || ((castInstanceId != CombatIds.NONE) && (slot.CastInstanceId != castInstanceId)))
                {
                    continue;
                }

                if (slot.State == ESlotState.Active)
                {
                    End(slot, EBossAreaEnd.Cancelled, _lastServerTime);
                }
                else
                {
                    Free(slot);
                }
            }
        }

        /// <summary> 해당 패턴의 예약 + 활성 장판 수, 동시 장판 상한 검사가 쓴다. 예약과 활성은 같은 슬롯이라 이중 집계가 없다 </summary>
        public int ActiveAreaCount(string patternId)
        {
            int count = 0;
            for (int i = 0; i < _slots.Length; ++i)
            {
                if ((_slots[i].State != ESlotState.Free) && (_slots[i].PatternId == patternId))
                {
                    ++count;
                }
            }

            return count;
        }

        /// <summary> 간격마다 회차가 도래하고 k x 간격이 지속 시간보다 작은 회차만 센다, 6초와 1초면 6회다 </summary>
        public static int ComputeHitCount(float durationSeconds, float tickInterval)
        {
            if ((durationSeconds <= 0f) || (tickInterval <= 0f))
            {
                return 0;
            }

            return Math.Max(1, (int)Math.Ceiling(((double)durationSeconds / tickInterval) - ROUND_EPSILON));
        }

        private Slot Find(int areaId, ESlotState state)
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                if ((_slots[i].State == state) && (_slots[i].AreaId == areaId))
                {
                    return _slots[i];
                }
            }

            return null;
        }

        // 도래한 회차를 모두 처리한 뒤 종료 시각이 지났으면 종료한다, 놓친 회차는 건너뛰지 않는다
        private void Advance(Slot slot, double serverTime)
        {
            BossAreaSpec spec = slot.Spec;

            if (spec.SkipMissedRounds)
            {
                AdvanceLatestOnly(slot, serverTime);
                return;
            }

            while (slot.NextHit < slot.HitCount)
            {
                double roundTime = spec.ActivateServerTime + (slot.NextHit * (double)spec.TickInterval);
                if (roundTime > serverTime)
                {
                    break;
                }

                Hit(slot, slot.NextHit, roundTime);
                ++slot.NextHit;
            }

            if (serverTime >= (spec.ActivateServerTime + spec.DurationSeconds))
            {
                End(slot, EBossAreaEnd.Expired, spec.ActivateServerTime + spec.DurationSeconds);
            }
        }

        // 지연된 틱에서 장판 하나당 한 회만 판정한다. 가장 최근 도래한 회차만 현재 위치로 판정하고 이전 회차는 건너뛰며 다음 회차는 원래 활성 시각 기준이다
        // 종료 시각이 지났으면 미처리 회차 없이 종료한다. 이 제한은 장판 하나 안에서만 적용돼 서로 다른 장판의 중첩 피해를 막지 않는다
        private void AdvanceLatestOnly(Slot slot, double serverTime)
        {
            BossAreaSpec spec = slot.Spec;
            double endTime = spec.ActivateServerTime + spec.DurationSeconds;

            if (serverTime >= endTime)
            {
                End(slot, EBossAreaEnd.Expired, endTime);
                return;
            }

            int due = (int)Math.Floor(((serverTime - spec.ActivateServerTime) / spec.TickInterval) + ROUND_EPSILON);
            due = Math.Min(due, slot.HitCount - 1);
            if (due < slot.NextHit)
            {
                return;
            }

            Hit(slot, due, spec.ActivateServerTime + (due * (double)spec.TickInterval));
            slot.NextHit = due + 1;
        }

        // 회차마다 현재 범위 안을 다시 조회하고 대상당 한 번 요청한다, 회차마다 새 동작 번호를 쓰고 시전 번호는 유지한다
        private void Hit(Slot slot, int hitIndex, double roundTime)
        {
            BossAreaSpec spec = slot.Spec;
            _targets.GetLivingInRadius(spec.Center, spec.Radius, ECombatFaction.Player, true, _buffer);

            int actionInstanceId = spec.Gate.IssueInstanceId();
            int hitCount = 0;
            for (int i = 0; i < _buffer.Count; ++i)
            {
                DamageRequest request = new DamageRequest(spec.AttackerId, _buffer[i].CombatId, spec.CastInstanceId,
                    actionInstanceId, spec.RawDamage, spec.Flags, spec.Center, spec.DefinitionId);

                if (spec.Gate.TryApply(in request, out DamageResult _))
                {
                    ++hitCount;
                    BossTargetHitReport.Report(_listener, spec.CastInstanceId, _buffer[i], spec.Center);
                }
            }

            _buffer.Clear();
            _listener?.OnAreaTick(spec.CastInstanceId, spec.AreaId, hitIndex, roundTime, hitCount);
        }

        private void End(Slot slot, EBossAreaEnd end, double endServerTime)
        {
            int castInstanceId = slot.Spec.CastInstanceId;
            int areaId         = slot.Spec.AreaId;
            Free(slot);
            _listener?.OnAreaEnded(castInstanceId, areaId, end, endServerTime);
        }

        // 슬롯을 돌려준다, 상태로 어느 개수에서 뺄지 정하므로 한 번만 돌아온다
        private void Free(Slot slot)
        {
            if (slot.State == ESlotState.Reserved)
            {
                --_reservedCount;
            }
            else if (slot.State == ESlotState.Active)
            {
                --_activeCount;
            }

            slot.State          = ESlotState.Free;
            slot.AreaId         = CombatIds.NONE;
            slot.CastInstanceId = CombatIds.NONE;
            slot.PatternId      = null;
        }
    }
}
