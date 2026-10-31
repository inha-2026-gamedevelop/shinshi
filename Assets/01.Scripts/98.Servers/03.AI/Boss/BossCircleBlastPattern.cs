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
    // 시간과 반경과 피해 배수는 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    // 무철 몸 이동(bodyShift)이 있으면 보스가 예고 중에 움직이므로 판정 중심은 타격 순간의 보스 위치이고, 예고는 미리 계산한 그 위치에 그린다
    /// <summary> 보스 중심 원형 여우불 폭발, 예고 시작 때 중심을 정하고 예고가 끝나면 한 번 타격한다 </summary>
    public sealed class BossCircleBlastPattern : BossPatternBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly float                          _referenceHealth;
        private readonly IBossCircleTelegraphListener   _listener;
        private readonly MucheolBodyShift               _bodyShift;
        private readonly List<ICombatTarget>            _buffer = new List<ICombatTarget>();

        private Vector3 _center;
        private bool    _hasHit;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 공격 중심, 예고 시작 때 정하고 몸 이동이 있으면 타격 순간의 보스 위치로 바뀐다 </summary>
        public Vector3 Center => _center;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        public BossCircleBlastPattern(float referenceHealth, IBossCircleTelegraphListener listener = null,
            MucheolBodyShift bodyShift = null)
        {
            _referenceHealth = referenceHealth;
            _listener        = listener;
            _bodyShift       = bodyShift;
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _center = Context.Self.Position;
            _hasHit = false;

            if (_bodyShift != null)
            {
                _bodyShift.Begin(Context.Self, MucheolBodyShift.ResolveForward(Context, false), serverTime,
                    serverTime + definition.TelegraphSeconds);
                _center = _bodyShift.PredictStrikePosition(0);
            }

            _listener?.OnTelegraphStarted(Context.CastInstanceId, _center, definition.Shape.Range, definition.TelegraphSeconds);
        }

        protected override void OnTick(EBossAttackState state, double serverTime)
        {
            AdvanceBody(serverTime);
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            AdvanceBody(serverTime);

            if (state == EBossAttackState.Execute)
            {
                Strike();
            }
        }

        protected override void OnCancelled(EBossInterruptReason reason)
        {
            _listener?.OnCancelled(Context.CastInstanceId, reason);
        }

        protected override void OnFinished()
        {
            _bodyShift?.Complete();
        }

        // 타격 전에는 타 확정 시각을 넘지 않는다. 거친 틱에서 몸이 타격 위치를 지나친 뒤에 판정하지 않게 한다
        private void AdvanceBody(double serverTime)
        {
            if (_bodyShift == null)
            {
                return;
            }

            _bodyShift.Advance(_hasHit ? serverTime : Math.Min(serverTime, _bodyShift.GetStrikeTime(0)));
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
            if (_bodyShift != null)
            {
                _center = Context.Self.Position;
            }

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
                    ReportTargetHit(_listener, _buffer[i], _center);
                }
            }

            _buffer.Clear();
            _listener?.OnHit(Context.CastInstanceId, hitCount);
        }
    }
}
