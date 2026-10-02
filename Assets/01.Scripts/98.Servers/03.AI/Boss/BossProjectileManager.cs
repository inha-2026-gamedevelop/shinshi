// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 등록, 진행, 정리 순서
    // 등록: 패턴이 발사 예약 시각(첫 발은 예고 끝 경계)마다 TryLaunch를 부른다. 발사 시각은 그 예약 시각이고 틱 시각이 아니다
    // 한 시전이 여러 발을 쏠 수 있어 투사체 한 발은 시전 번호가 아니라 동작 번호(ActionInstanceId)로 구분한다. 시전 번호는 같은 시전의 발들이 함께 쓰는 묶음 키다
    // 진행: BossCombat이 공격 진행 뒤에 매 틱 Tick을 부른다. 이번 틱에 발사된 투사체도 발사 시각부터 지금까지 이어서 진행하므로 틱이 거칠어도 누락이 없다
    // 정리: 상위 FSM이 사망, 페이즈 전환, 전투 종료로 끊으면 BossBrain이 게이트를 먼저 닫고 Cancel(reason, NONE)을 불러 모두 즉시 제거한다
    //       직접 제어는 이미 발사한 투사체를 취소하지 않으므로 DirectControl 사유는 무시한다. 패턴의 정상 종료(Finish)도 투사체를 건드리지 않는다
    //
    // 충돌 규칙(사용자 확정)
    // 매 틱 이전 위치부터 이번 위치까지의 이동 구간을 검사한다. 벽과 캐릭터 중 경로상 먼저 닿는 것만 처리하며, 같은 거리면 벽이 이긴다
    // 같은 거리의 캐릭터끼리는 CombatId가 작은 쪽이다. 첫 유효 플레이어나 소환물에 한 번 판정하고 사라지며 방어, 반격, 무적 결과와 상관없다
    // 판정은 다른 월백 패턴과 같다. 대상은 중심점이고 BodyRadius는 쓰지 않으며, 경로는 폭의 절반까지의 평평한 끝단 직사각형이다. 예고와 표현이 같은 직사각형이다
    // 벽은 폭의 절반을 반경으로 둔 몸체로 미리 한 번 재서 경로 길이로 삼는다. 정적 지형이라 비행 중에 다시 재지 않는다
    /// <summary> 월백 투사체의 수명 관리자, 패턴 종료와 상관없이 발사된 투사체를 진행하고 충돌과 소멸을 처리한다 </summary>
    public sealed class BossProjectileManager : IBossEffectLifetime, IBossProjectileLauncher, IBossTickableEffects
    {
        /****************************************
        *              Constants
        ****************************************/

        // 동시에 날 수 있는 투사체 수, 한 시전이 여러 발을 쏘고 회복이 끝난 뒤에도 남을 수 있어 여유를 둔다. 가득 차면 새 발은 발사되지 않는다
        private const int   MAX_PROJECTILES   = 8;
        private const float MIN_DIRECTION_SQR = 0.000001f;
        private const float EDGE_EPSILON      = 0.001f; // m, 경계의 부동소수 오차 흡수
        private const float TIE_EPSILON       = 0.001f; // m, 이 안이면 같은 거리로 보고 벽 우선과 CombatId 순서를 적용한다
        private const float HALF              = 0.5f;

        /****************************************
        *                Fields
        ****************************************/

        // 날고 있는 한 발, 미리 만들어 재사용한다
        private sealed class Slot
        {
            public bool                Active;
            public BossProjectileSpec  Spec;
            public float               PathLength;
            public float               Travelled;
        }

        private readonly ICombatTargetQuery     _targets;
        private readonly IBossWallQuery         _walls;
        private readonly IBossProjectileListener _listener;
        private readonly Slot[]                 _slots  = new Slot[MAX_PROJECTILES];
        private readonly List<ICombatTarget>    _buffer = new List<ICombatTarget>();

        private int _activeCount;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 지금 날고 있는 투사체 수 </summary>
        public int ActiveCount => _activeCount;

        /****************************************
        *                Methods
        ****************************************/

        // walls가 비어 있으면 벽을 검사하지 않는다. 실제 서버 연결은 보스 몸체의 벽 검사를 넘긴다
        public BossProjectileManager(ICombatTargetQuery targets, IBossWallQuery walls = null, IBossProjectileListener listener = null)
        {
            _targets  = targets ?? throw new System.ArgumentNullException(nameof(targets));
            _walls    = walls;
            _listener = listener;

            for (int i = 0; i < _slots.Length; ++i)
            {
                _slots[i] = new Slot();
            }
        }

        public float MeasurePath(Vector3 origin, Vector3 direction, float maxDistance, float width)
        {
            if (_walls == null)
            {
                return maxDistance;
            }

            return Mathf.Clamp(_walls.GetClearDistance(origin, direction, maxDistance, width * HALF), 0f, maxDistance);
        }

        public bool TryLaunch(in BossProjectileSpec spec)
        {
            // 투사체 한 발은 동작 번호가 가리킨다. 이미 날고 있는 번호의 중복 발사는 받지 않는다
            if (IsActionActive(spec.ActionInstanceId))
            {
                return false;
            }

            if ((spec.Gate == null) || (spec.Speed <= 0f) || (spec.Range <= 0f) || (spec.Width < 0f)
                || (spec.Direction.sqrMagnitude < MIN_DIRECTION_SQR) || (spec.CastInstanceId == CombatIds.NONE))
            {
                return false;
            }

            Slot slot = FindFreeSlot();
            if (slot == null)
            {
                return false;
            }

            slot.Active     = true;
            slot.Spec       = spec;
            slot.Travelled  = 0f;
            slot.PathLength = MeasurePath(spec.Origin, spec.Direction, spec.Range, spec.Width);
            ++_activeCount;

            _listener?.OnProjectileLaunched(spec.CastInstanceId, spec.ActionInstanceId, spec.Origin, spec.Direction, spec.Speed,
                spec.Width, slot.PathLength, spec.LaunchServerTime);
            return true;
        }

        public void Tick(double serverTime)
        {
            if (_activeCount == 0)
            {
                return;
            }

            for (int i = 0; i < _slots.Length; ++i)
            {
                if (_slots[i].Active)
                {
                    Advance(_slots[i], serverTime);
                }
            }
        }

        /// <summary> 사유별로 정리한다. 이미 발사한 투사체는 직접 제어로 취소하지 않으므로 DirectControl은 무시하고, 나머지는 시전 번호가 NONE이면 모두 제거한다 </summary>
        public void Cancel(EBossInterruptReason reason, int castInstanceId)
        {
            if ((reason == EBossInterruptReason.DirectControl) || (reason == EBossInterruptReason.None) || (_activeCount == 0))
            {
                return;
            }

            for (int i = 0; i < _slots.Length; ++i)
            {
                Slot slot = _slots[i];
                if (slot.Active && ((castInstanceId == CombatIds.NONE) || (slot.Spec.CastInstanceId == castInstanceId)))
                {
                    End(slot, EBossProjectileEnd.Cancelled, PositionAt(slot, slot.Travelled), CombatIds.NONE);
                }
            }
        }

        /// <summary> 이 관리자는 장판을 만들지 않는다 </summary>
        public int ActiveAreaCount(string patternId)
        {
            return 0;
        }

        private bool IsActionActive(int actionInstanceId)
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                if (_slots[i].Active && (_slots[i].Spec.ActionInstanceId == actionInstanceId))
                {
                    return true;
                }
            }

            return false;
        }

        private Slot FindFreeSlot()
        {
            for (int i = 0; i < _slots.Length; ++i)
            {
                if (!_slots[i].Active)
                {
                    return _slots[i];
                }
            }

            return null;
        }

        // 이전 위치부터 이번 위치까지의 구간을 한 번에 본다, 구간이 길어도 경로 전체를 빠짐없이 검사한다
        private void Advance(Slot slot, double serverTime)
        {
            BossProjectileSpec spec = slot.Spec;
            double age = serverTime - spec.LaunchServerTime;
            if (age <= 0.0)
            {
                return;
            }

            // 실행 시간이 아니라 정의의 사거리와 속도로 정해진 수명이라 최대 사거리에 닿으면 소멸한다
            float distance = Mathf.Min(spec.Range, (float)(spec.Speed * age));
            float from     = slot.Travelled;
            bool  hasWall  = slot.PathLength < (spec.Range - EDGE_EPSILON);
            bool  wallHit  = hasWall && (distance >= slot.PathLength);

            // 벽이 이 구간 안이면 벽까지가 끝이고, 벽과 같은 거리의 대상은 벽이 이긴다
            float upper      = wallHit ? slot.PathLength : distance;
            float acceptable = wallHit ? (slot.PathLength - TIE_EPSILON) : (distance + EDGE_EPSILON);

            if (FindFirstTarget(slot, from, upper, acceptable, out ICombatTarget target, out float along))
            {
                ApplyHit(slot, target);
                End(slot, EBossProjectileEnd.Hit, PositionAt(slot, Mathf.Clamp(along, 0f, slot.Spec.Range)), target.CombatId);
                return;
            }

            if (wallHit)
            {
                End(slot, EBossProjectileEnd.Wall, PositionAt(slot, slot.PathLength), CombatIds.NONE);
                return;
            }

            if (distance >= spec.Range)
            {
                End(slot, EBossProjectileEnd.Range, PositionAt(slot, spec.Range), CombatIds.NONE);
                return;
            }

            slot.Travelled = distance;
        }

        // 구간 직사각형 안의 살아 있는 플레이어와 소환물 중 경로상 가장 가까운 한 명, 같은 거리면 CombatId가 작은 쪽
        private bool FindFirstTarget(Slot slot, float from, float upper, float acceptable, out ICombatTarget best, out float bestAlong)
        {
            best      = null;
            bestAlong = 0f;

            BossProjectileSpec spec = slot.Spec;
            float halfWidth  = spec.Width * HALF;
            float halfLength = Mathf.Max(0f, upper - from) * HALF;

            // 조회는 원이라 구간 직사각형의 먼 모서리까지 닿는 반경이 필요하다. 안팎은 아래에서 직사각형으로 다시 가른다
            Vector3 center = spec.Origin + (spec.Direction * (from + halfLength));
            float   radius = Mathf.Sqrt((halfLength * halfLength) + (halfWidth * halfWidth)) + EDGE_EPSILON;
            _targets.GetLivingInRadius(center, radius, ECombatFaction.Player, true, _buffer);

            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget candidate = _buffer[i];
                Vector3 offset = candidate.Position - spec.Origin;
                offset.y = 0f;

                float forward = (offset.x * spec.Direction.x) + (offset.z * spec.Direction.z);
                float lateral = (offset.x * spec.Direction.z) - (offset.z * spec.Direction.x);

                if ((forward < (from - EDGE_EPSILON)) || (forward > acceptable) || (Mathf.Abs(lateral) > (halfWidth + EDGE_EPSILON)))
                {
                    continue;
                }

                bool isCloser = (best == null) || (forward < (bestAlong - TIE_EPSILON));
                bool isTieWin = (best != null) && (Mathf.Abs(forward - bestAlong) <= TIE_EPSILON) && (candidate.CombatId < best.CombatId);
                if (isCloser || isTieWin)
                {
                    best      = candidate;
                    bestAlong = forward;
                }
            }

            _buffer.Clear();
            return best != null;
        }

        // 후방 판정 기준은 발사 위치다, 틱 크기와 상관없이 보스 쪽에서 온 공격으로 본다
        private static void ApplyHit(Slot slot, ICombatTarget target)
        {
            BossProjectileSpec spec = slot.Spec;
            DamageRequest request = new DamageRequest(spec.AttackerId, target.CombatId, spec.CastInstanceId, spec.ActionInstanceId,
                spec.RawDamage, spec.Flags, spec.Origin, spec.DefinitionId);

            // 닫힌 게이트라 요청이 나가지 않아도, 방어와 무적으로 막혀도 투사체는 사라진다
            spec.Gate.TryApply(in request, out DamageResult _);
        }

        private void End(Slot slot, EBossProjectileEnd end, Vector3 position, int hitTargetId)
        {
            int castInstanceId = slot.Spec.CastInstanceId;
            int projectileId   = slot.Spec.ActionInstanceId;
            slot.Active = false;
            --_activeCount;
            _listener?.OnProjectileEnded(castInstanceId, projectileId, end, position, hitTargetId);
        }

        private static Vector3 PositionAt(Slot slot, float distance)
        {
            return slot.Spec.Origin + (slot.Spec.Direction * distance);
        }
    }
}
