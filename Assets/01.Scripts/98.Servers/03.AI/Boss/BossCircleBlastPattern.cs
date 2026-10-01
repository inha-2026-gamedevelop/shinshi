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
    // 시간과 반경과 피해 배수는 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    /// <summary> 보스 중심 원형 여우불 폭발, 예고 시작 때 중심을 고정하고 예고가 끝나면 한 번 타격한다 </summary>
    public sealed class BossCircleBlastPattern : BossPatternBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly float                          _referenceHealth;
        private readonly IBossCircleTelegraphListener   _listener;
        private readonly List<ICombatTarget>            _buffer = new List<ICombatTarget>();

        private Vector3 _center;
        private bool    _hasHit;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 예고 시작 때 저장한 공격 중심, 시전이 끝날 때까지 바뀌지 않는다 </summary>
        public Vector3 Center => _center;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        public BossCircleBlastPattern(float referenceHealth, IBossCircleTelegraphListener listener = null)
        {
            _referenceHealth = referenceHealth;
            _listener        = listener;
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _center = Context.Self.Position;
            _hasHit = false;

            _listener?.OnTelegraphStarted(Context.CastInstanceId, _center, definition.Shape.Range, definition.TelegraphSeconds);
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

            // 거리 판정은 조회 계약(수평 거리, 경계 포함)에 맡긴다. 소환물도 범위 피해를 받는다
            Context.Targets.GetLivingInRadius(_center, definition.Shape.Range, ECombatFaction.Player, true, _buffer);

            int   actionInstanceId = Context.Damage.IssueInstanceId();
            float rawDamage        = definition.DamageRef * _referenceHealth;
            EDamageFlags flags     = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;
            int   hitCount         = 0;

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
