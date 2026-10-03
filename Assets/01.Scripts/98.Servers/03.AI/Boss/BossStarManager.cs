// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 슬롯 순서: 예약(TryReserve, 예고 시작) -> 활성(TryActivate, 예약을 구조로 전환) -> 사라짐(Expired 또는 Cancelled)
    // 고정 평행 빔 관리자(BossBeamManager)와 같은 시간 규칙을 쓰되, 판정 영역이 시간에 따라 도는 별형 구조라 따로 둔다. 장판 상한(IBossAreaCapacity)에는 들어가지 않는다
    //
    // 시간 처리(서버 시각, 과거 위치 기록 없음)
    // 회차 k는 활성 시각 + k x 간격에 도래하며 k x 간격이 지속 시간보다 작은 k만 있다(6초, 0.25초면 0~23의 24회). 종료 시각의 회차는 없고 종료 때 추가 타격도 없다
    // 활성 순간(k = 0)의 첫 타격은 TryActivate가 아니라 Tick이 처리한다. 패턴 진입과 Tick 두 곳에서 중복해 맞는 일이 없다
    // 지연된 틱에서는 구조 하나당 한 틱에 가장 최근 도래한 회차만 판정하고 이전 회차는 건너뛴다. 다음 회차는 원래 활성 시각 기준이다
    // 판정에 쓰는 별의 회전각은 그 틱의 서버 시각 기준 현재 각도이고 대상 위치도 현재 위치다. 과거 회차 시각의 배치나 위치를 복원했다고 주장하지 않는다
    // 종료 시각이 지났으면 미처리 회차 없이 종료한다. 취소(사망, 페이즈 전환, 전투 종료)는 항상 우선하며 이미 적용된 피해는 되돌리지 않는다
    //
    // 판정은 BossStarGeometry.Contains 하나다. 회전각을 구한 현재 배치에서 전장 안의 빔 다섯 줄 중 하나라도 중심점이 닿는 살아 있는 플레이어와 소환물(미접속 생존자 포함)이 대상이다
    // 빔이 겹치는 교차점도 참 거짓 하나라 같은 회차에 대상당 한 번만 요청한다. 회차 사이에 빔이 지나갔다는 이유로 추가 피해를 만들지 않는다(회전 경로 판정이 아니다)
    /// <summary> 월백 별형 회전 빔의 수명 관리자, 슬롯과 회전과 주기 피해와 정리를 맡는다 </summary>
    public sealed class BossStarManager : IBossEffectLifetime, IBossTickableEffects, IBossStarHost
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int    MAX_SLOTS     = 4;
        private const double ROUND_EPSILON = 0.000001;

        /****************************************
        *                Fields
        ****************************************/

        private enum ESlotState
        {
            Free,
            Reserved,
            Active
        }

        private sealed class Slot
        {
            public ESlotState   State;
            public int          CastInstanceId;
            public int          StarId;
            public string       PatternId;
            public BossStarSpec Spec;
            public int          HitCount;
            public int          NextHit;
        }

        private readonly ICombatTargetQuery  _targets;
        private readonly IBossStarListener   _listener;
        private readonly Slot[]              _slots  = new Slot[MAX_SLOTS];
        private readonly List<ICombatTarget> _buffer = new List<ICombatTarget>();

        private int    _reservedCount;
        private int    _activeCount;
        private double _lastServerTime;

        /****************************************
        *              Properties
        ****************************************/

        public int ReservedCount => _reservedCount;

        public int ActiveCount => _activeCount;

        /****************************************
        *                Methods
        ****************************************/

        public BossStarManager(ICombatTargetQuery targets, IBossStarListener listener = null)
        {
            _targets  = targets ?? throw new ArgumentNullException(nameof(targets));
            _listener = listener;

            for (int i = 0; i < _slots.Length; ++i)
            {
                _slots[i] = new Slot();
            }
        }

        public bool TryReserve(int castInstanceId, int starId, string patternId)
        {
            if ((castInstanceId == CombatIds.NONE) || (starId == CombatIds.NONE) || string.IsNullOrEmpty(patternId))
            {
                return false;
            }

            Slot free = null;
            for (int i = 0; i < _slots.Length; ++i)
            {
                Slot slot = _slots[i];
                if (slot.State == ESlotState.Free)
                {
                    free = (free == null) ? slot : free;
                    continue;
                }

                if (slot.StarId == starId)
                {
                    return false;
                }
            }

            if (free == null)
            {
                return false;
            }

            free.State          = ESlotState.Reserved;
            free.StarId         = starId;
            free.CastInstanceId = castInstanceId;
            free.PatternId      = patternId;
            ++_reservedCount;
            return true;
        }

        public void ReleaseReservation(int starId)
        {
            Slot slot = Find(starId, ESlotState.Reserved);
            if (slot != null)
            {
                Free(slot);
            }
        }

        public bool TryActivate(in BossStarSpec spec)
        {
            Slot slot = Find(spec.StarId, ESlotState.Reserved);
            if (slot == null)
            {
                return false;
            }

            if ((spec.Gate == null) || (spec.Shape.ArenaRadius <= 0f) || (spec.Shape.BeamWidth <= 0f) || (spec.Shape.VertexCount <= 0)
                || (spec.DurationSeconds <= 0f) || (spec.TickInterval <= 0f))
            {
                Free(slot);
                return false;
            }

            slot.State    = ESlotState.Active;
            slot.Spec     = spec;
            slot.HitCount = BossAreaManager.ComputeHitCount(spec.DurationSeconds, spec.TickInterval);
            slot.NextHit  = 0;
            --_reservedCount;
            ++_activeCount;

            _listener?.OnStarActivated(spec.CastInstanceId, spec.StarId, spec.DurationSeconds, spec.TickInterval, spec.ActivateServerTime);
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

        /// <summary> 사유별로 정리한다. 직접 제어는 이미 활성한 구조를 지우지 않으므로 무시하고, 나머지는 시전 번호가 NONE이면 예약과 활성 구조를 모두 즉시 제거한다 </summary>
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

        /// <summary> 해당 패턴의 예약 + 활성 구조 수 </summary>
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

        private Slot Find(int starId, ESlotState state)
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                if ((_slots[i].State == state) && (_slots[i].StarId == starId))
                {
                    return _slots[i];
                }
            }

            return null;
        }

        // 종료 시각이 지났으면 미처리 회차 없이 종료하고, 아니면 가장 최근 도래한 회차 하나만 판정한다
        private void Advance(Slot slot, double serverTime)
        {
            BossStarSpec spec = slot.Spec;
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

            Hit(slot, due, spec.ActivateServerTime + (due * (double)spec.TickInterval), serverTime);
            slot.NextHit = due + 1;
        }

        // 틱 서버 시각의 별 회전각으로 전장 안을 다시 조회해 닿은 대상에게 한 번씩 요청한다. 회차마다 새 동작 번호를 쓰고 시전 번호는 유지한다
        private void Hit(Slot slot, int hitIndex, double roundTime, double serverTime)
        {
            BossStarSpec spec = slot.Spec;
            BossStarShape shape = spec.Shape;
            float rotation = BossStarGeometry.RotationAt(in shape, serverTime - spec.ActivateServerTime, spec.DurationSeconds);

            _targets.GetLivingInRadius(shape.ArenaCenter, shape.ArenaRadius, ECombatFaction.Player, true, _buffer);

            int actionInstanceId = spec.Gate.IssueInstanceId();
            int hitCount = 0;
            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget target = _buffer[i];
                if (!BossStarGeometry.Contains(in shape, target.Position, rotation))
                {
                    continue;
                }

                DamageRequest request = new DamageRequest(spec.AttackerId, target.CombatId, spec.CastInstanceId,
                    actionInstanceId, spec.RawDamage, spec.Flags, shape.ArenaCenter, spec.DefinitionId);

                if (spec.Gate.TryApply(in request, out DamageResult _))
                {
                    ++hitCount;
                }
            }

            _buffer.Clear();
            _listener?.OnStarTick(spec.CastInstanceId, spec.StarId, hitIndex, roundTime, hitCount);
        }

        private void End(Slot slot, EBossAreaEnd end, double endServerTime)
        {
            int castInstanceId = slot.Spec.CastInstanceId;
            int starId         = slot.Spec.StarId;
            Free(slot);
            _listener?.OnStarEnded(castInstanceId, starId, end, endServerTime);
        }

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
            slot.StarId         = CombatIds.NONE;
            slot.CastInstanceId = CombatIds.NONE;
            slot.PatternId      = null;
        }
    }
}
