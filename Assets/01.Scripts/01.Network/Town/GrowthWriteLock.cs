// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 성장 쓰기의 재시도 보관 칸은 하나뿐이다. 결과를 모르는 요청이 남아 있는데 다른 창이 새 쓰기를 보내면
    // 그 칸이 덮여 처음 창의 재시도가 엉뚱한 요청을 다시 보낸다. 그래서 결과가 정해질 때까지 모든 창의 새 쓰기를 막는다.
    // 조회는 멱등이라 잠그지 않는다
    /// <summary> 결과가 정해지지 않은 성장 쓰기 요청 하나를 붙잡는 공용 잠금, TownGrowthClient가 쓴다 </summary>
    public sealed class GrowthWriteLock
    {
        /****************************************
        *                Fields
        ****************************************/

        private Guid          _operationId;
        private EGrowthAction _action;
        private bool          _isHeld;
        private bool          _isOutcomeUnknown;

        /****************************************
        *              Properties
        ****************************************/

        public bool          IsHeld           => _isHeld;
        public Guid          OperationId      => _isHeld ? _operationId : Guid.Empty;
        public EGrowthAction Action           => _isHeld ? _action : EGrowthAction.Snapshot;

        // 응답이 BackendUnavailable로 와서 확정도 거절도 아닌 상태. 같은 번호로 다시 보내야 풀린다
        public bool          IsOutcomeUnknown => (_isHeld) && (_isOutcomeUnknown);

        /****************************************
        *                Methods
        ****************************************/

        public static bool IsWrite(EGrowthAction action)
        {
            return action != EGrowthAction.Snapshot;
        }

        /// <summary> 쓰기 요청을 잠금에 건다, 이미 잠겼거나 조회면 거짓 </summary>
        public bool TryBegin(Guid operationId, EGrowthAction action)
        {
            if ((_isHeld) || (operationId == Guid.Empty) || (!IsWrite(action)))
            {
                return false;
            }

            _operationId      = operationId;
            _action           = action;
            _isHeld           = true;
            _isOutcomeUnknown = false;
            return true;
        }

        /// <summary> 결과를 반영한다. 확정이나 확정 거절이면 풀고, 백엔드 장애면 결과 불명으로 남긴다. 상태가 바뀌면 참 </summary>
        public bool Resolve(Guid operationId, ERejectReason reason)
        {
            if ((!_isHeld) || (operationId != _operationId))
            {
                return false;
            }

            if (reason == ERejectReason.BackendUnavailable)
            {
                if (_isOutcomeUnknown)
                {
                    return false;
                }

                _isOutcomeUnknown = true;
                return true;
            }

            Clear();
            return true;
        }

        /// <summary> 같은 요청을 다시 보냈다, 결과를 다시 기다린다 </summary>
        public bool MarkRetrying(Guid operationId)
        {
            if ((!_isHeld) || (operationId != _operationId) || (!_isOutcomeUnknown))
            {
                return false;
            }

            _isOutcomeUnknown = false;
            return true;
        }

        // 요청을 보낸 창이 시간 초과 뒤 서버 상태를 다시 읽기로 정했을 때만 부른다. 다른 창의 요청은 풀지 않는다
        /// <summary> 자기 요청을 포기하고 잠금을 푼다, 번호가 다르면 거짓 </summary>
        public bool Release(Guid operationId)
        {
            if ((!_isHeld) || (operationId == Guid.Empty) || (operationId != _operationId))
            {
                return false;
            }

            Clear();
            return true;
        }

        public void Clear()
        {
            _operationId      = Guid.Empty;
            _action           = EGrowthAction.Snapshot;
            _isHeld           = false;
            _isOutcomeUnknown = false;
        }
    }
}
