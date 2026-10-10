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
    // 반경, 각도, 피해, 예고와 회복 시간은 패턴 정의에서, 밀림 거리는 전장 공격 데이터에서 읽는다. 이 클래스에는 밸런스 값이 없다.
    // 기존 부채꼴 폭발(BossFanBlastPattern)은 타격 때 보스의 현재 위치를 읽고 예고 중에 조준을 갱신한다. 이 패턴은 예고 시작 순간의
    // 위치와 방향을 저장해 예고 표시와 타격 판정이 둘 다 그 값을 쓰게 해야 해서 그 클래스를 재사용하지 않고 따로 둔다.
    // 타깃을 따라 도는 조준이 없고 안전지대 방향도 저장한 정면의 반대쪽으로 고정이다. 보스가 전장 가장자리에 있어 안전지대가 전장 밖이어도
    // 시전을 취소하거나 중앙으로 옮기지 않는다. 그 위치는 타깃이 보스를 유도한 결과로 본다.
    // 부채꼴 안팎 판정은 BossFanShape 하나를 쓴다. 각도 경계는 안쪽(공격 영역)에 포함이라 예고 메시의 경계선 위는 맞는다.
    // 보스와 같은 위치의 대상은 방향이 없어 맞고, 밀 방향은 저장한 정면으로 한다
    /// <summary> 무철 하드 2페이즈 전장 공격, 저장한 위치와 방향으로 예고가 끝나면 한 번 타격하고 맞은 대상을 바깥으로 민다 </summary>
    public sealed class MucheolBattlefieldBlastPattern : BossPatternBase
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                         _referenceHealth;
        private readonly MucheolBattlefieldSpec        _spec;
        private readonly IBossHitDisplacer             _displacer;
        private readonly IMucheolBattlefieldListener   _listener;
        private readonly List<ICombatTarget>           _buffer = new List<ICombatTarget>();

        private Vector3 _origin;
        private Vector3 _aim;
        private bool    _hasHit;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 예고 시작 순간에 저장한 보스 위치, 시전이 끝날 때까지 바뀌지 않는다 </summary>
        public Vector3 Origin => _origin;

        /// <summary> 예고 시작 순간에 저장한 정면 방향, 수평 단위 벡터. 안전지대는 이 방향의 반대쪽이다 </summary>
        public Vector3 AimDirection => _aim;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        public MucheolBattlefieldBlastPattern(float referenceHealth, MucheolBattlefieldSpec spec, IBossHitDisplacer displacer,
            IMucheolBattlefieldListener listener = null)
        {
            _referenceHealth = referenceHealth;
            _spec            = spec ?? throw new ArgumentNullException(nameof(spec));
            _displacer       = displacer ?? throw new ArgumentNullException(nameof(displacer));
            _listener        = listener;
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _origin = Context.Self.Position;
            _aim    = Flatten(Context.Self.Forward, Vector3.forward);
            _hasHit = false;

            _listener?.OnTelegraphStarted(Context.CastInstanceId, _origin, _aim, definition.Shape.Range, definition.Shape.ArcAngle,
                definition.TelegraphSeconds);
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Execute)
            {
                Strike();
            }
        }

        protected override void OnCancelled(EBossInterruptReason reason)
        {
            _listener?.OnCancelled(Context.CastInstanceId, reason);
        }

        // 한 번만 쓴다. 대상마다 요청을 내지만 한 동작 번호를 공유하므로 대상당 한 번이다
        private void Strike()
        {
            if (_hasHit)
            {
                return;
            }

            _hasHit = true;

            PatternDefinition definition = Context.Definition;
            float radius = definition.Shape.Range;
            float arc    = definition.Shape.ArcAngle;

            // 소환물과 동료도 범위 피해를 받는다
            Context.Targets.GetLivingInRadius(_origin, radius, ECombatFaction.Player, true, _buffer);

            int          actionInstanceId = Context.Damage.IssueInstanceId();
            float        rawDamage        = definition.DamageRef * _referenceHealth;
            EDamageFlags flags            = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;
            int          hitCount         = 0;

            for (int i = 0; i < _buffer.Count; ++i)
            {
                ICombatTarget target   = _buffer[i];
                Vector3       position = target.Position;
                if (!BossFanShape.IsInsideSector(_origin, _aim, position, radius, arc))
                {
                    continue;
                }

                DamageRequest request = new DamageRequest(Context.Self.CombatId, target.CombatId,
                    Context.CastInstanceId, actionInstanceId, rawDamage, flags, _origin, definition.DefinitionId);

                if ((!TryApplyHit(in request, out DamageResult result)) || (!result.IsApplied))
                {
                    continue;
                }

                ++hitCount;

                // 쓰러뜨린 피해에도 밀림을 시도한다. 피해가 적용되지 않은 대상은 밀지 않는다
                Push(target.CombatId, position);
            }

            _buffer.Clear();
            _listener?.OnHit(Context.CastInstanceId, hitCount);
        }

        private void Push(int combatId, Vector3 position)
        {
            if (_spec.PushDistance <= 0f)
            {
                return;
            }

            float distance = _displacer.TryPush(combatId, Flatten(position - _origin, _aim), _spec.PushDistance);
            _listener?.OnPushed(Context.CastInstanceId, combatId, distance);
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? fallback : direction.normalized;
        }
    }
}
