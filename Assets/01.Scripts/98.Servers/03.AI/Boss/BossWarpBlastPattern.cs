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
    // 후보 거리, 후보 수, 대상과의 최소 거리, 충격파 반경, 예고와 회복 시간, 피해 배수는 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    //
    // 시전 시작 순간에 한 번만 착지 후보를 검사하고 바로 워프한다. 틱마다 다시 탐색하지 않는다
    //   대상의 서버 정면(ICombatTarget.Forward)을 읽어 대상 뒤를 0도로 두고 후방, 후방 대각, 측면, 전방 대각, 전방 순서로 최대 여덟 곳을 검사한다
    //   후보마다 전장 경계, 거리, 충돌, 경로 순서로 검사하고 앞 검사를 통과한 후보에만 다음 검사를 한다. 경로 검사는 후보당 많아야 한 번이다
    //   유효한 후보가 없으면 워프하지 않고 시작을 거절한다. 거절하면 예고도 알리지 않고 두뇌가 기록 없이 이 정의를 뺀 채 같은 선택에서 한 번 다시 고른다
    // 착지 전에는 착지 위치를 알리지 않는다. 워프가 끝난 뒤 실제로 선 보스 위치를 중심으로 예고를 시작하고, 그 중심은 시전이 끝날 때까지 바뀌지 않는다
    // 예고가 끝나면 충격파를 한 번 쓴다. 피해 요청은 대상마다 내지만 한 동작 번호를 공유한다
    /// <summary> 후방 우선 워프 후 충격파, 착지한 보스 위치를 중심으로 원형 예고를 하고 한 번 타격한다 </summary>
    public sealed class BossWarpBlastPattern : BossPatternBase, IBossPatternStartResult, IBossPatternRetryOnReject
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다

        // 대상 뒤를 0도로 두고 오른쪽이 양수인 후보 각도다. 후방, 후방 대각, 측면, 전방 대각, 전방 순서이고 각 쌍은 오른쪽부터 검사한다
        private static readonly float[] G_CandidateAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                          _referenceHealth;
        private readonly IBossWarp                      _warp;
        private readonly IBossArenaSource               _arena;
        private readonly IBossCircleTelegraphListener   _listener;
        private readonly List<ICombatTarget>            _buffer = new List<ICombatTarget>();

        private Vector3 _center;
        private bool    _isRejected;
        private bool    _isWarped;
        private bool    _hasHit;
        private int     _checkedCandidates;
        private int     _pathChecks;
        private int     _landedIndex;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 착지한 보스 위치, 예고와 타격의 중심이며 시전이 끝날 때까지 바뀌지 않는다 </summary>
        public Vector3 Center => _center;

        /// <summary> 유효한 착지 후보가 없거나 전장, 대상, 창구가 없어 시작하지 못했다 </summary>
        public bool IsStartRejected => _isRejected;

        public bool IsWarped => _isWarped;

        /// <summary> 이번 시전이 위치를 검사한 후보 수, 최대 후보 수를 넘지 않는다 </summary>
        public int CheckedCandidates => _checkedCandidates;

        /// <summary> 이번 시전이 부른 경로 검사 수, 후보당 많아야 한 번이다 </summary>
        public int PathChecks => _pathChecks;

        /// <summary> 착지에 쓴 후보의 검사 순서(0이 후방), 착지하지 못했으면 -1이다 </summary>
        public int LandedIndex => _landedIndex;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        public BossWarpBlastPattern(float referenceHealth, IBossWarp warp, IBossArenaSource arena,
            IBossCircleTelegraphListener listener = null)
        {
            _referenceHealth = referenceHealth;
            _warp            = warp ?? throw new ArgumentNullException(nameof(warp));
            _arena           = arena ?? throw new ArgumentNullException(nameof(arena));
            _listener        = listener;
        }

        /// <summary> 대상 뒤를 기준으로 index번째 후보 위치를 계산한다, 높이는 대상과 같다 </summary>
        public static Vector3 GetCandidate(Vector3 targetPosition, Vector3 targetForward, int index, float distance)
        {
            Vector3 behind = -Flatten(targetForward, Vector3.forward);

            // 유니티는 +Y 회전이 오른쪽에서 왼쪽으로 도는 방향이라 오른쪽을 양수로 두려고 부호를 뒤집는다
            Vector3 offset = Quaternion.AngleAxis(-G_CandidateAngles[index], Vector3.up) * behind;

            return targetPosition + (offset * distance);
        }

        protected override void OnBegin(double serverTime)
        {
            _isRejected        = true;
            _isWarped          = false;
            _hasHit            = false;
            _checkedCandidates = 0;
            _pathChecks        = 0;
            _landedIndex       = -1;

            PatternDefinition definition = Context.Definition;
            ICombatTarget     self       = Context.Self;

            if ((!_arena.TryGetArena(out Vector3 arenaCenter, out float arenaRadius)) || (arenaRadius <= 0f)
                || (!TryGetValidTarget(out ICombatTarget target)))
            {
                return;
            }

            Vector3 toTarget = target.Position - self.Position;
            toTarget.y = 0f;
            if (toTarget.magnitude < definition.WarpMinTargetDistance)
            {
                return;
            }

            // 정면을 정할 수 없으면 대상이 보스를 보고 있다고 본다, 그러면 후방 후보는 보스 반대쪽에 놓인다
            Vector3 facing = Flatten(target.Forward, Flatten(-toTarget, Vector3.forward));

            int   count  = Mathf.Min(definition.WarpCandidateCount, G_CandidateAngles.Length);
            float minGap = self.BodyRadius + target.BodyRadius;

            for (int i = 0; i < count; ++i)
            {
                ++_checkedCandidates;
                Vector3 point = GetCandidate(target.Position, facing, i, definition.WarpLandingDistance);

                // 서버 착지(TryWarp)가 보스 높이를 유지하므로 사전 검사도 같은 높이로 한다
                point.y = self.Position.y;

                // 검사 순서는 전장 경계, 거리, 충돌, 경로다. 앞 검사를 통과한 후보에만 다음 검사를 한다
                if (!IsInsideArena(point, arenaCenter, arenaRadius, self.BodyRadius))
                {
                    continue;
                }

                Vector3 gap = point - target.Position;
                gap.y = 0f;
                if (gap.magnitude < minGap)
                {
                    continue;
                }

                if (!_warp.CanStandAt(point))
                {
                    continue;
                }

                ++_pathChecks;
                if (!_warp.HasPath(point, target.Position))
                {
                    continue;
                }

                // 서버가 최종 권한이라 막으면 이 후보를 쓴 것으로 치고 다음 후보로 간다
                if (!_warp.TryWarp(point, out Vector3 landed))
                {
                    continue;
                }

                _center      = landed;
                _landedIndex = i;
                _isWarped    = true;
                _isRejected  = false;

                // 착지가 끝난 뒤에야 예고를 알린다, 그래서 착지 전 위치는 어디에도 나가지 않는다
                _listener?.OnTelegraphStarted(Context.CastInstanceId, _center, definition.Shape.Range, definition.TelegraphSeconds);
                return;
            }
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Execute)
            {
                Strike();
            }
        }

        // 워프하지 못한 시전은 알린 예고가 없으므로 취소도 알리지 않는다
        protected override void OnCancelled(EBossInterruptReason reason)
        {
            if (_isWarped && (!_hasHit))
            {
                _listener?.OnCancelled(Context.CastInstanceId, reason);
            }
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

        // 몸체 중심이 전장 반지름에서 몸체 반경을 뺀 원 안에 있어야 한다, 경계는 포함한다
        private static bool IsInsideArena(Vector3 point, Vector3 arenaCenter, float arenaRadius, float bodyRadius)
        {
            Vector3 offset = point - arenaCenter;
            offset.y = 0f;

            return offset.magnitude <= Mathf.Max(0f, arenaRadius - bodyRadius);
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? fallback : direction.normalized;
        }

        // 한 번만 쓴다. 대상마다 요청을 내지만 한 동작 번호를 공유하므로 대상당 한 번이다
        private void Strike()
        {
            if (_hasHit || (!_isWarped))
            {
                return;
            }

            _hasHit = true;

            PatternDefinition definition = Context.Definition;

            // 거리 판정은 조회 계약(수평 거리, 경계 포함)에 맡긴다. 소환물도 범위 피해를 받는다
            Context.Targets.GetLivingInRadius(_center, definition.Shape.Range, ECombatFaction.Player, true, _buffer);

            int          actionInstanceId = Context.Damage.IssueInstanceId();
            float        rawDamage        = definition.DamageRef * _referenceHealth;
            EDamageFlags flags            = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;
            int          hitCount         = 0;

            for (int i = 0; i < _buffer.Count; ++i)
            {
                DamageRequest request = new DamageRequest(Context.Self.CombatId, _buffer[i].CombatId,
                    Context.CastInstanceId, actionInstanceId, rawDamage, flags, _center, definition.DefinitionId);

                if (TryApplyHit(in request, out DamageResult _))
                {
                    ++hitCount;
                }
            }

            _buffer.Clear();
            _listener?.OnHit(Context.CastInstanceId, hitCount);
        }
    }
}
