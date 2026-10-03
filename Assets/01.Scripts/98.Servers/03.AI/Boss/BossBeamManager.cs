// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 슬롯 순서: 예약(TryReserve, 예고 시작) -> 활성(TryActivate, 예약을 빔으로 전환) -> 사라짐(Expired 또는 Cancelled)
    // 빔 한 줄은 빔 번호(BeamId)로 구분하고 시전 번호는 같은 시전이 설치한 빔들의 묶음이다. 시전 번호를 넘긴 정리(Cancel)만 그 시전의 빔을 함께 지우고, 빔의 수명 종료는 그 빔 하나만 끝낸다
    // 장판 관리자와 별개의 슬롯과 개수를 쓴다. 장판 상한(IBossAreaCapacity)에 빔을 더하지 않으므로 기존 장판의 설치와 피해에 영향이 없고 서로의 피해는 독립이다
    //
    // 시간 처리(서버 시각, 과거 위치 기록 없음)
    // 회차 k는 활성 시각 + k x 간격에 도래하며 k x 간격이 지속 시간보다 작은 k만 있다(4초, 0.5초면 0~7의 8회). 종료 시각의 회차는 없고 종료 때 추가 타격도 없다
    // 활성 순간(k = 0)의 첫 타격은 TryActivate가 아니라 Tick이 처리한다. 패턴 진입과 Tick 두 곳에서 중복해 맞는 일이 없다. BossCombat이 같은 틱 안에서 공격 진행 직후 Tick을 부르므로 지연은 없다
    // 지연된 틱에서는 빔 하나당 한 틱에 가장 최근 도래한 회차만 현재 위치로 판정하고 이전 회차는 건너뛴다. 다음 회차는 원래 활성 시각 기준이다
    // 종료 시각이 지났으면 미처리 회차 없이 종료한다. 종료 뒤에 밀린 피해는 없다. 이 제한은 빔 하나 안에서만 적용돼 서로 다른 빔의 피해를 막지 않는다
    // 취소(사망, 페이즈 전환, 전투 종료)는 항상 우선한다. BossBrain이 게이트를 닫고 Cancel을 부른 뒤에는 처리하지 못한 회차를 모두 버리고 추가 피해를 내지 않는다. 이미 적용된 피해는 되돌리지 않는다
    //
    // 판정은 BossBeamGeometry.Contains 하나다. 전장 원 안에서 폭 띠에 중심점이 든 살아 있는 플레이어와 소환물(미접속 생존자 포함)이 대상이고 BodyRadius는 쓰지 않는다
    // 표현이 같은 기하(BossBeamShape)를 쓰므로 예고와 판정의 경계와 끝이 일치한다. 전장 원 후보 조회 뒤 Contains로 거르므로 후보 목록은 재사용 버퍼 하나다
    //
    // 수명 관리자 인터페이스의 ActiveAreaCount는 패턴별 빔 슬롯 수를 돌려준다(동시 상한 검사 호환). 이 클래스는 IBossAreaCapacity를 구현하지 않는다
    /// <summary> 월백 고정 평행 빔의 수명 관리자, 슬롯과 주기 피해와 정리를 맡는다 </summary>
    public sealed class BossBeamManager : IBossEffectLifetime, IBossTickableEffects, IBossBeamHost
    {
        /****************************************
        *              Constants
        ****************************************/

        // 동시에 가질 수 있는 슬롯 수의 기술적 상한
        private const int    MAX_SLOTS     = 8;
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

        // 빔 한 줄의 슬롯, 미리 만들어 재사용한다
        private sealed class Slot
        {
            public ESlotState  State;
            public int         CastInstanceId;
            public int         BeamId;
            public string      PatternId;
            public BossBeamSpec Spec;
            public int         HitCount;
            public int         NextHit;
        }

        private readonly ICombatTargetQuery  _targets;
        private readonly IBossBeamListener   _listener;
        private readonly Slot[]              _slots  = new Slot[MAX_SLOTS];
        private readonly List<ICombatTarget> _buffer = new List<ICombatTarget>();

        private int    _reservedCount;
        private int    _activeCount;
        private double _lastServerTime;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 예고 중이라 예약만 한 슬롯 수 </summary>
        public int ReservedCount => _reservedCount;

        /// <summary> 활성되어 피해를 내고 있는 빔 수 </summary>
        public int ActiveCount => _activeCount;

        /****************************************
        *                Methods
        ****************************************/

        public BossBeamManager(ICombatTargetQuery targets, IBossBeamListener listener = null)
        {
            _targets  = targets ?? throw new ArgumentNullException(nameof(targets));
            _listener = listener;

            for (int i = 0; i < _slots.Length; ++i)
            {
                _slots[i] = new Slot();
            }
        }

        public bool TryReserve(int castInstanceId, int beamId, string patternId)
        {
            if ((castInstanceId == CombatIds.NONE) || (beamId == CombatIds.NONE) || string.IsNullOrEmpty(patternId))
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

                // 같은 빔 번호의 예약이 이미 있으면 두 번 세지 않는다. 같은 시전의 다른 빔은 별개다
                if (slot.BeamId == beamId)
                {
                    return false;
                }
            }

            if (free == null)
            {
                return false;
            }

            free.State          = ESlotState.Reserved;
            free.BeamId         = beamId;
            free.CastInstanceId = castInstanceId;
            free.PatternId      = patternId;
            ++_reservedCount;
            return true;
        }

        public void ReleaseReservation(int beamId)
        {
            Slot slot = Find(beamId, ESlotState.Reserved);
            if (slot != null)
            {
                Free(slot);
            }
        }

        public bool TryActivate(in BossBeamSpec spec)
        {
            Slot slot = Find(spec.BeamId, ESlotState.Reserved);
            if (slot == null)
            {
                return false;
            }

            if ((spec.Gate == null) || (spec.Shape.ArenaRadius <= 0f) || (spec.Shape.Width <= 0f)
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

            _listener?.OnBeamActivated(spec.CastInstanceId, spec.BeamId, spec.DurationSeconds, spec.TickInterval, spec.ActivateServerTime);
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

        /// <summary> 사유별로 정리한다. 직접 제어는 이미 활성한 빔을 지우지 않으므로 무시하고, 나머지는 시전 번호가 NONE이면 예약과 활성 빔을 모두 즉시 제거한다 </summary>
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

        /// <summary> 해당 패턴의 예약 + 활성 빔 수 </summary>
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

        private Slot Find(int beamId, ESlotState state)
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                if ((_slots[i].State == state) && (_slots[i].BeamId == beamId))
                {
                    return _slots[i];
                }
            }

            return null;
        }

        // 종료 시각이 지났으면 미처리 회차 없이 종료하고, 아니면 가장 최근 도래한 회차 하나만 판정한다
        private void Advance(Slot slot, double serverTime)
        {
            BossBeamSpec spec = slot.Spec;
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

        // 회차마다 전장 안을 다시 조회해 폭 띠 안의 대상에게 한 번씩 요청한다. 회차마다 새 동작 번호를 쓰고 시전 번호는 유지한다
        private void Hit(Slot slot, int hitIndex, double roundTime)
        {
            BossBeamSpec spec = slot.Spec;
            BossBeamShape shape = spec.Shape;
            _targets.GetLivingInRadius(shape.ArenaCenter, shape.ArenaRadius, ECombatFaction.Player, true, _buffer);

            int actionInstanceId = spec.Gate.IssueInstanceId();
            int hitCount = 0;
            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget target = _buffer[i];
                if (!BossBeamGeometry.Contains(in shape, target.Position))
                {
                    continue;
                }

                DamageRequest request = new DamageRequest(spec.AttackerId, target.CombatId, spec.CastInstanceId,
                    actionInstanceId, spec.RawDamage, spec.Flags, shape.LineCenter, spec.DefinitionId);

                if (spec.Gate.TryApply(in request, out DamageResult _))
                {
                    ++hitCount;
                }
            }

            _buffer.Clear();
            _listener?.OnBeamTick(spec.CastInstanceId, spec.BeamId, hitIndex, roundTime, hitCount);
        }

        private void End(Slot slot, EBossAreaEnd end, double endServerTime)
        {
            int castInstanceId = slot.Spec.CastInstanceId;
            int beamId         = slot.Spec.BeamId;
            Free(slot);
            _listener?.OnBeamEnded(castInstanceId, beamId, end, endServerTime);
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
            slot.BeamId         = CombatIds.NONE;
            slot.CastInstanceId = CombatIds.NONE;
            slot.PatternId      = null;
        }
    }
}
