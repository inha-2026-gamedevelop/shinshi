// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 거리, 폭, 예고, 추적 시간, 실행, 일반 회복, 피해, 방어 가능 여부는 패턴 정의에서, 충돌 회복은 충돌 설정에서 읽는다.
    // 이 클래스에는 밸런스 값이 없다. 돌진 자체는 월백 P01과 같은 BossDashPattern을 쓰되, 이지 조준 규칙대로 예고를 시작하는 순간
    // 선택된 대상 쪽으로 각도 제한 없이 조준하고 고정 시각까지 따라가도록 snapAimToTarget을 켠다. 정의의 회전 속도는 이 패턴에서 쓰이지 않는다.
    // 벽과 구조물에 막힌 결과는 돌진 실행이 스스로 가진 상태(멈췄는지, 정의의 거리를 채웠는지)로 정한다.
    // 돌진 실행이 내보내는 표현용 알림(OnDashStopped)은 읽지 않는다. 알림은 표현을 위한 것이라 판정의 입력으로 삼지 않는다.
    // 충돌 회복은 지금 공격 FSM이 읽지 않는다. 패턴은 결과와 회복 시간을 결정해 IBossRecoveryDuration으로 내보내고,
    // FSM이 그것을 읽도록 바뀌어야 실제 회복 시간에 반영된다
    /// <summary> 무철 P06 뿔 들이받기, 돌진하며 지나간 대상을 한 번씩 타격하고 벽이나 구조물에 막히면 즉시 멈추며 긴 회복을 요청한다 </summary>
    public sealed class MucheolChargePattern : IBossPattern, IBossRecoveryDuration
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int NO_STRUCTURE_ID = 0; // 구조물이 아닌 충돌의 구조물 식별자

        /****************************************
        *                Fields
        ****************************************/

        private readonly MucheolChargeMovement      _movement;
        private readonly MucheolChargeSpec          _spec;
        private readonly BossDashPattern            _dash;
        private readonly IBossChargeOutcomeListener _outcomeListener;
        private readonly IBossChargeCastTracker     _castTracker;

        private BossPatternContext _context;
        private bool               _isCancelled;
        private bool               _isFinished;
        private bool               _isEnded;

        private EBossChargeEnd     _endKind;
        private BossChargeCollision _collision;
        private float              _recoverySeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 현재 조준 방향, 수평 단위 벡터 </summary>
        public Vector3 AimDirection => _dash.AimDirection;

        public bool IsDirectionLocked => _dash.IsDirectionLocked;

        /// <summary> 돌진 시작점에서 지금까지 실제로 간 거리 </summary>
        public float TraveledDistance => _dash.TraveledDistance;

        /// <summary> 더 이상 가지 않는다, 정해진 거리에 닿았거나 막혔다 </summary>
        public bool IsStopped => _dash.IsStopped;

        public bool IsCancelled => _isCancelled;

        /// <summary> 일반 종료인지 충돌 종료인지, 돌진이 아직 안 끝났거나 취소됐으면 None이다 </summary>
        public EBossChargeEnd EndKind => _endKind;

        /// <summary> 충돌 종료일 때의 충돌 정보, 아니면 종류가 None이다 </summary>
        public BossChargeCollision Collision => _collision;

        /// <summary> 실행 종료 경계부터 재는 회복 시간, 결과가 정해지기 전에는 0이다 </summary>
        public float RecoverySeconds => _recoverySeconds;

        /****************************************
        *                Methods
        ****************************************/

        // movement는 돌진이 보스 몸체를 옮기는 창구이고 structures는 구조물 조회다. 구조물 조회가 없으면 벽에만 막힌다
        // castTracker는 시전의 실제 시작과 취소를 받는 쪽(기믹)이고 비어 있으면 알리지 않는다
        public MucheolChargePattern(float referenceHealth, IBossMovement movement, MucheolChargeSpec spec,
            IBossChargeStructureQuery structures = null, IBossDashTelegraphListener dashListener = null,
            IBossChargeOutcomeListener outcomeListener = null, IBossChargeCastTracker castTracker = null)
        {
            if (movement == null)
            {
                throw new ArgumentNullException(nameof(movement));
            }

            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            _movement        = new MucheolChargeMovement(movement, structures);
            _spec            = spec;
            _dash            = new BossDashPattern(referenceHealth, _movement, dashListener, snapAimToTarget: true);
            _outcomeListener = outcomeListener;
            _castTracker     = castTracker;
        }

        public void Begin(in BossPatternContext context, double serverTime)
        {
            _context         = context;
            _isCancelled     = false;
            _isFinished      = false;
            _isEnded         = false;
            _endKind         = EBossChargeEnd.None;
            _collision       = default;
            _recoverySeconds = 0f;

            _movement.ResetLimit();
            _dash.Begin(in context, serverTime);
            ResolveEnd();

            // 패턴을 고른 것이 아니라 시전이 실제로 시작된 이 순간에 알린다
            _castTracker?.OnChargeCastStarted(context.CastInstanceId);
        }

        public void EnterState(EBossAttackState state, double serverTime)
        {
            if (_isCancelled)
            {
                return;
            }

            _dash.EnterState(state, serverTime);
            ResolveEnd();
        }

        public void Tick(EBossAttackState state, double serverTime)
        {
            if (_isCancelled)
            {
                return;
            }

            _dash.Tick(state, serverTime);
            ResolveEnd();
        }

        // 취소된 시전은 끝 종류를 정하지 않는다. 이미 낸 피해는 되돌리지 않고 이후 이동과 타격은 돌진 실행이 막는다.
        // 정상 종료한 뒤의 취소 호출은 이미 끝난 시전이라 알리지 않는다
        public void Cancel(EBossInterruptReason reason)
        {
            if ((_isCancelled) || (_isFinished))
            {
                return;
            }

            _isCancelled = true;
            _dash.Cancel(reason);
            _castTracker?.OnChargeCastCancelled(_context.CastInstanceId, reason);
        }

        public void Finish()
        {
            if (_isCancelled)
            {
                return;
            }

            _isFinished = true;
            _dash.Finish();
        }

        public float GetRecoverySeconds(float definitionSeconds)
        {
            return (_endKind == EBossChargeEnd.Collision) ? _recoverySeconds : definitionSeconds;
        }

        // 돌진이 멈추는 순간 한 번만 결과를 정한다. 틱이 거칠어 실행과 회복 경계를 한꺼번에 넘어도
        // 돌진 실행이 실행 끝 시각까지의 이동을 마무리한 뒤이므로 결과가 같다
        private void ResolveEnd()
        {
            if ((_isEnded) || (_isCancelled) || (!_dash.IsStopped))
            {
                return;
            }

            _isEnded = true;

            float   range    = _context.Definition.Shape.Range;
            float   traveled = _dash.TraveledDistance;
            Vector3 stopAt   = _context.Self.Position;

            if (MucheolChargeMovement.ReachedFullRange(traveled, range))
            {
                _endKind         = EBossChargeEnd.Normal;
                _recoverySeconds = _context.Definition.RecoverySeconds;
            }
            else
            {
                _endKind         = EBossChargeEnd.Collision;
                _recoverySeconds = _spec.CollisionRecoverySeconds;

                // 멈춘 뒤 다시 조회하지 않고 이동을 제한한 순간에 기록한 원인을 쓴다. 구조물이 그 사이 사라져도 구조물과 식별자가 남는다.
                // 거리를 못 채웠는데 기록된 원인이 없으면(알 수 없는 막힘) 벽으로 본다
                bool byStructure = _movement.LimitKind == EBossChargeObstacleKind.Structure;
                _collision = new BossChargeCollision(byStructure ? EBossChargeObstacleKind.Structure : EBossChargeObstacleKind.Wall,
                    byStructure ? _movement.LimitStructureId : NO_STRUCTURE_ID, stopAt, traveled);
            }

            _outcomeListener?.OnChargeEnded(_context.CastInstanceId, _endKind, _collision, _recoverySeconds);
        }
    }
}
