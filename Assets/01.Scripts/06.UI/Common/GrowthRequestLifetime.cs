// System
using System;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 결과 한 건을 창의 요청에 반영한 결과 </summary>
    public enum EGrowthRequestResolve
    {
        Ignored = 0,
        Unknown = 1,
        Settled = 2,
    }

    // 공용 잠금을 유지하고 같은 번호로 다시 확인하는 창(스킬, 옵션)이 같은 수명 규칙을 쓰게 한다.
    // 결과를 모르는 요청은 확정 실패로 바꾸지 않고, 연결이 바뀌어 공용 잠금이 비워지면 이전 요청을 놓고 서버 상태를 다시 읽게 한다.
    // 시간은 호출자가 넘겨 엔진 없이 검증한다
    /// <summary> 성장창 하나의 미확정 쓰기 요청 한 건의 수명 (번호, 종류, 응답 기한, 결과 불명) </summary>
    public sealed class GrowthRequestLifetime
    {
        /****************************************
        *                Fields
        ****************************************/

        private Guid          _operationId = Guid.Empty;
        private EGrowthAction _action;
        private bool          _isUnknown;
        private float         _deadline;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsActive => _operationId != Guid.Empty;

        public Guid OperationId => _operationId;

        public EGrowthAction Action => _action;

        public bool IsOutcomeUnknown => (IsActive) && (_isUnknown);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 보낸 쓰기 요청을 등록한다, 번호가 비었으면 거짓 </summary>
        public bool Begin(Guid operationId, EGrowthAction action, float now, float timeoutSeconds)
        {
            if (operationId == Guid.Empty)
            {
                return false;
            }

            _operationId = operationId;
            _action      = action;
            _isUnknown   = false;
            _deadline    = now + timeoutSeconds;
            return true;
        }

        /// <summary> 결과 한 건을 반영한다. 다른 번호의 결과는 무시하고, BackendUnavailable은 결과 불명으로 붙잡으며, 그 밖에는 끝난다 </summary>
        public EGrowthRequestResolve Resolve(Guid operationId, ERejectReason reason, out EGrowthAction action)
        {
            action = _action;
            if ((!IsActive) || (operationId != _operationId))
            {
                return EGrowthRequestResolve.Ignored;
            }

            if (reason == ERejectReason.BackendUnavailable)
            {
                _isUnknown = true;
                return EGrowthRequestResolve.Unknown;
            }

            Clear();
            return EGrowthRequestResolve.Settled;
        }

        /// <summary> 응답 기한이 지난 요청을 결과 불명으로 바꾼다, 바뀌었으면 참 </summary>
        public bool Expire(float now)
        {
            if ((!IsActive) || (_isUnknown) || (now < _deadline))
            {
                return false;
            }

            _isUnknown = true;
            return true;
        }

        // 다시 접속하면 TownGrowthClient가 잠금을 비운다(결과를 받은 뒤에도 비워진다). 창이 비활성인 사이 일어났다면 그 사이 다른 창이
        // 새 잠금을 잡았을 수 있다. 잠금이 있다는 사실만 보면 남의 잠금을 내 요청으로 착각하므로 번호까지 맞춰 본다.
        // 이전 연결의 요청은 같은 번호로 확인할 수 없으니 놓고, 확정 성공이나 실패로 단정하지 않은 채 서버 상태를 새로 읽게 한다.
        // 공용 잠금은 건드리지 않는다
        /// <summary> 요청이 남아 있는데 공용 잠금이 비었거나 다른 요청에 걸려 있으면 자기 요청만 놓고 참 </summary>
        public bool DropIfLockLost(bool isLockHeld, Guid lockOperation)
        {
            if ((!IsActive) || ((isLockHeld) && (lockOperation == _operationId)))
            {
                return false;
            }

            Clear();
            return true;
        }

        /// <summary> 공용 잠금이 이 창의 결과 불명 요청을 들고 있어 같은 번호로 다시 확인할 수 있으면 참 </summary>
        public bool CanRetry(bool isLockHeld, bool isLockOutcomeUnknown, EGrowthAction lockAction, Guid lockOperation,
            EGrowthAction[] ownActions)
        {
            return (IsOutcomeUnknown) && (GrowthWriteGuard.CanRetry(isLockHeld, (isLockOutcomeUnknown) || (_isUnknown), lockAction,
                lockOperation, _operationId, ownActions));
        }

        /// <summary> 같은 요청을 다시 보냈다, 결과를 다시 기다린다 </summary>
        public void MarkRetrying(float now, float timeoutSeconds)
        {
            if (!IsActive)
            {
                return;
            }

            _isUnknown = false;
            _deadline  = now + timeoutSeconds;
        }

        public void Clear()
        {
            _operationId = Guid.Empty;
            _action      = default;
            _isUnknown   = false;
            _deadline    = 0f;
        }
    }
}
