// System
using System;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.UI
{
    // 강화와 수련은 같은 번호로 다시 묻지 않는다. 시간 초과나 연결 끊김이면 자기 요청의 공용 잠금만 풀고 서버 상태를 다시 읽은 뒤
    // 새 요청으로 시작한다. 연출, 묶음 중단, 예상 스냅샷 확인은 각 화면이 맡고, 여기는 요청 번호, 종류, 기한, 소유 판정만 가진다.
    // 시간은 호출자가 넘겨 엔진 없이 검증한다
    /// <summary> 재조회 해제 정책을 쓰는 성장 화면 하나의 진행 중 쓰기 요청 한 건 </summary>
    public sealed class GrowthRefreshReleaseRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        private bool          _isInFlight;
        private Guid          _operationId = Guid.Empty;
        private EGrowthAction _action;
        private float         _deadline;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsInFlight => _isInFlight;

        public Guid OperationId => _operationId;

        public EGrowthAction Action => _action;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 요청을 보내기 전에 진행 중으로 둔다, 보내는 동안 오는 잠금 변경 알림에 화면이 끼어들지 않게 한다 </summary>
        public void Prepare(EGrowthAction action)
        {
            _isInFlight  = true;
            _action      = action;
            _operationId = Guid.Empty;
            _deadline    = 0f;
        }

        /// <summary> 보낸 요청의 번호와 응답 기한을 붙인다, 번호가 비었으면(보내지 못했다) 거짓이며 상태는 그대로다 </summary>
        public bool Attach(Guid operationId, float now, float timeoutSeconds)
        {
            if ((!_isInFlight) || (operationId == Guid.Empty))
            {
                return false;
            }

            _operationId = operationId;
            _deadline    = now + timeoutSeconds;
            return true;
        }

        /// <summary> Prepare와 Attach를 한 번에 한다 </summary>
        public bool Begin(Guid operationId, EGrowthAction action, float now, float timeoutSeconds)
        {
            Prepare(action);
            return Attach(operationId, now, timeoutSeconds);
        }

        /// <summary> 이 화면이 보낸 진행 중 요청의 결과인지 번호와 종류로 가린다 </summary>
        public bool Matches(Guid operationId, EGrowthAction action)
        {
            return (_isInFlight) && (_operationId != Guid.Empty) && (operationId == _operationId) && (action == _action);
        }

        /// <summary> 번호가 붙은 요청이 연결이 끊겼거나 응답 기한을 넘겼으면 참 </summary>
        public bool IsExpired(float now, bool isConnected)
        {
            return (_isInFlight) && (_operationId != Guid.Empty) && ((!isConnected) || (now >= _deadline));
        }

        /// <summary> 요청을 끝내고, 공용 잠금에서 풀어야 할 자기 요청 번호를 돌려준다(없으면 Guid.Empty) </summary>
        public Guid Clear()
        {
            Guid own     = _operationId;
            _isInFlight  = false;
            _operationId = Guid.Empty;
            _action      = default;
            _deadline    = 0f;
            return own;
        }

        /// <summary> 요청을 끝내고 자기 요청의 공용 잠금만 푼다, 이미 결과가 와서 잠금이 풀렸거나 다른 요청이 잡고 있으면 아무 일도 없다 </summary>
        public bool ClearAndRelease()
        {
            return TownGrowthClient.ReleaseWrite(Clear());
        }
    }
}
