// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.AI
{
    /// <summary> 패턴 구현의 공통 기반, 취소나 종료 뒤에는 피해 요청이 나가지 않도록 막는다 </summary>
    public abstract class BossPatternBase : IBossPattern
    {
        /****************************************
        *                Fields
        ****************************************/

        private BossPatternContext      _context;
        private bool                    _isCancelled;
        private bool                    _isFinished;
        private EBossInterruptReason    _cancelReason;

        /****************************************
        *              Properties
        ****************************************/

        protected BossPatternContext Context => _context;

        public bool IsCancelled => _isCancelled;
        public bool IsFinished  => _isFinished;

        public EBossInterruptReason CancelReason => _cancelReason;

        /****************************************
        *                Methods
        ****************************************/

        public void Begin(in BossPatternContext context, double serverTime)
        {
            _context      = context;
            _isCancelled  = false;
            _isFinished   = false;
            _cancelReason = EBossInterruptReason.None;
            OnBegin(serverTime);
        }

        public void EnterState(EBossAttackState state, double serverTime)
        {
            if (!IsActive())
            {
                return;
            }

            OnEnterState(state, serverTime);
        }

        public void Tick(EBossAttackState state, double serverTime)
        {
            if (!IsActive())
            {
                return;
            }

            OnTick(state, serverTime);
        }

        public void Cancel(EBossInterruptReason reason)
        {
            if (!IsActive())
            {
                return;
            }

            _isCancelled  = true;
            _cancelReason = reason;
            OnCancelled(reason);
        }

        public void Finish()
        {
            if (!IsActive())
            {
                return;
            }

            _isFinished = true;
            OnFinished();
        }

        protected virtual void OnBegin(double serverTime) { }
        protected virtual void OnEnterState(EBossAttackState state, double serverTime) { }
        protected virtual void OnTick(EBossAttackState state, double serverTime) { }

        // 예약해 둔 타격 목록을 여기서 비운다
        protected virtual void OnCancelled(EBossInterruptReason reason) { }
        protected virtual void OnFinished() { }

        // 모든 피해 요청은 이 창구로 낸다. 취소 뒤에 예약된 타격이 남아 있어도 resolver까지 가지 않는다
        protected bool TryApplyHit(in DamageRequest request, out DamageResult result)
        {
            result = default;

            if (!IsActive())
            {
                return false;
            }

            // 패턴 자신이 취소되지 않았어도 게이트가 닫혔으면(사망, 전투 종료) 나가지 않는다
            return _context.Damage.TryApply(in request, out result);
        }

        private bool IsActive()
        {
            return (!_isCancelled) && (!_isFinished);
        }
    }
}
