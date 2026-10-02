// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 시간과 반경과 지속 시간과 간격과 동시 상한과 피해 배수는 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    // 이 객체는 위치 고정과 예고와 설치만 맡는다. 활성된 장판의 수명과 주기 피해는 패턴이 끝나도 장판 관리자가 이어서 처리하므로
    // 지속 시간(6초)은 공격 FSM의 실행 시간이 아니고, 취소와 정상 종료(Finish)는 이미 활성된 장판을 건드리지 않는다
    /// <summary> 월백 P05 지정 위치 여우불 장판, 예고 시작 순간 타깃의 위치를 고정하고 예고가 끝나면 장판 하나를 활성한다 </summary>
    public sealed class BossAreaPattern : BossPatternBase
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly float                     _referenceHealth;
        private readonly IBossAreaHost             _host;
        private readonly IBossAreaTelegraphListener _listener;

        private Vector3 _center;
        private bool    _isReserved;
        private bool    _isActivated;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 예고 시작 때 고정한 중심, 이후 바뀌지 않는다 </summary>
        public Vector3 Center => _center;

        /// <summary> 슬롯을 예약했다, 예약이 실패하면 이 시전은 아무것도 하지 않는다 </summary>
        public bool IsReserved => _isReserved;

        /// <summary> 장판을 활성했다, 한 시전에 한 번만 참이 된다 </summary>
        public bool IsActivated => _isActivated;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        public BossAreaPattern(float referenceHealth, IBossAreaHost host, IBossAreaTelegraphListener listener = null)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            _referenceHealth = referenceHealth;
            _host            = host;
            _listener        = listener;
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _isReserved  = false;
            _isActivated = false;

            // 예고 시작 순간 선택된 타깃의 서버 위치, 타깃이 조회되지 않으면 보스 위치를 쓴다
            _center = Context.Targets.TryGet(Context.TargetCombatId, out ICombatTarget target)
                ? target.Position
                : Context.Self.Position;

            // 슬롯 예약, 선택 때 빈자리를 확인했어도 시작 순간에 다시 예약해 둔다. 실패하면 예고도 설치도 하지 않는다
            _isReserved = _host.TryReserve(Context.CastInstanceId, definition.DefinitionId, definition.MaxConcurrentAreas);
            if (!_isReserved)
            {
                return;
            }

            _listener?.OnTelegraphStarted(Context.CastInstanceId, _center, definition.Shape.Range, definition.TelegraphSeconds);
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Execute)
            {
                Activate(serverTime);
            }
        }

        // 활성 전에 끊긴 경우만 예약을 돌려주고 알린다. 활성한 뒤의 장판 소멸은 장판 관리자가 맡는다
        protected override void OnCancelled(EBossInterruptReason reason)
        {
            if (_isActivated || (!_isReserved))
            {
                return;
            }

            _host.ReleaseReservation(Context.CastInstanceId);
            _isReserved = false;
            _listener?.OnCancelled(Context.CastInstanceId, reason);
        }

        // 한 시전에 한 번만 설치한다. 활성 시각은 예고가 끝난 경계(FSM 단계 경계)다
        private void Activate(double serverTime)
        {
            if (_isActivated || (!_isReserved))
            {
                return;
            }

            PatternDefinition definition = Context.Definition;
            EDamageFlags flags = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;

            BossAreaSpec spec = new BossAreaSpec(Context.CastInstanceId, Context.Self.CombatId, definition.DefinitionId, _center,
                definition.Shape.Range, definition.AreaDurationSeconds, definition.AreaTickInterval,
                definition.DamageRef * _referenceHealth, flags, serverTime, Context.Damage);

            _isActivated = _host.TryActivate(in spec);
            if (!_isActivated)
            {
                _host.ReleaseReservation(Context.CastInstanceId);
                _isReserved = false;
            }
        }
    }
}
