// System
using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 경매장 쓰기 요청의 현재 진행 상태 </summary>
    public enum EMarketWriteState
    {
        None    = 0,
        Pending = 1,
        Unknown = 2,
    }

    /// <summary> 결과 한 건을 미확정 쓰기에 반영한 결과 </summary>
    public enum EMarketWriteResolve
    {
        Ignored   = 0,
        Completed = 1,
        Unknown   = 2,
    }

    // 마켓 서버는 연결당 쓰기를 하나만 받는다(같은 ID는 무시, 다른 ID는 PendingCommit 거절). 클라이언트도 같은 규칙을 지켜
    // 미확정 쓰기를 하나만 들고 있고, ID와 원문을 그대로 보관해 같은 ID로만 다시 확인한다. 시간은 호출자가 넘겨 엔진 없이 검증한다.
    // BackendUnavailable과 응답 시간 초과는 확정 실패가 아니라 결과 불명이다. 자동 재확인은 제한 횟수까지만 하고, 소진된 뒤에는
    // 사용자가 명시적으로 같은 ID 재확인을 요청할 때까지 결과 불명 상태로 남는다
    /// <summary> 연결 하나의 미확정 경매장 쓰기 한 건과 이전 연결에서 결과를 모르게 남은 쓰기의 표식 </summary>
    public sealed class MarketPendingWrite
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int   AUTO_RETRIES             = 2;
        public const float AUTO_RETRY_SECONDS       = 1f;
        public const float RESPONSE_TIMEOUT_SECONDS = 8f;

        private MarketRequestMessage _request;
        private bool                 _isHeld;
        private bool                 _isUnknown;
        private int                  _autoRetries;
        private int                  _sendCount;
        private float                _retryAt;
        private float                _deadline;
        private bool                 _hasStale;
        private EMarketAction        _staleAction;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsHeld => _isHeld;

        public Guid RequestId => _isHeld ? _request.RequestId : Guid.Empty;

        public EMarketAction Action => _isHeld ? _request.Action : EMarketAction.None;

        public MarketRequestMessage Request => _request;

        /// <summary> 같은 ID로 서버에 보낸 횟수, 처음 보낸 뒤 1이고 재확인 전송마다 늘어난다 </summary>
        public int SendCount => _isHeld ? _sendCount : 0;

        public EMarketWriteState State
        {
            get
            {
                if (!_isHeld)
                {
                    return EMarketWriteState.None;
                }

                return _isUnknown ? EMarketWriteState.Unknown : EMarketWriteState.Pending;
            }
        }

        /// <summary> 결과 불명이고 자동 재확인이 남아 있지 않다, 사용자가 같은 ID 재확인을 요청해야 한다 </summary>
        public bool NeedsReconfirm => (_isHeld) && (_isUnknown) && (_retryAt <= 0f);

        /// <summary> 이전 연결에서 결과를 모른 채 끊긴 쓰기가 있다, 권위 있는 목록과 내역으로만 확인한다 </summary>
        public bool HasStale => _hasStale;

        public EMarketAction StaleAction => _hasStale ? _staleAction : EMarketAction.None;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 쓰기 요청 하나를 등록한다, 이미 미확정 쓰기가 있거나 쓰기가 아니면 거절한다 </summary>
        public bool TryBegin(in MarketRequestMessage request, float now)
        {
            if ((_isHeld) || (!request.IsWrite) || (request.RequestId == Guid.Empty))
            {
                return false;
            }

            _request     = request;
            _isHeld      = true;
            _isUnknown   = false;
            _autoRetries = 0;
            _sendCount   = 1;
            _retryAt     = 0f;
            _deadline    = now + RESPONSE_TIMEOUT_SECONDS;
            return true;
        }

        // 서버는 같은 ID가 처리 중인지 보기 전에 요청 한도(RateLimited)를 먼저 검사하고, 그 뒤에 서버 연결과 대상, 다른 쓰기(PendingCommit)를 본다.
        // 이 거절들은 이번 전송을 받아들이지 않았다는 뜻일 뿐 앞선 전송의 결과가 아니다. 처음 보낸 전송이면 서버가 실행하지 않았으니 확정 거절이지만,
        // 재확인 전송이면 앞선 전송이 이미 실행됐을 수 있어 결과 불명으로 붙잡고 원래 응답을 기다린다
        /// <summary> 결과 한 건을 반영한다, 다른 ID나 이미 끝난 요청의 늦은 응답은 무시한다 </summary>
        public EMarketWriteResolve Resolve(Guid requestId, ERejectReason reason, float now)
        {
            if ((!_isHeld) || (requestId != _request.RequestId))
            {
                return EMarketWriteResolve.Ignored;
            }

            if ((reason == ERejectReason.BackendUnavailable) || ((_sendCount > 1) && (IsRelayPreflight(reason))))
            {
                if (!_isUnknown)
                {
                    MarkUnknown(now);
                }

                return EMarketWriteResolve.Unknown;
            }

            Clear();
            return EMarketWriteResolve.Completed;
        }

        /// <summary> 응답 기한이 지난 진행 중 요청을 결과 불명으로 바꾼다, 바뀌었으면 참 </summary>
        public bool Tick(float now)
        {
            if ((!_isHeld) || (_isUnknown) || (now < _deadline))
            {
                return false;
            }

            MarkUnknown(now);
            return true;
        }

        /// <summary> 자동 재확인 시각이 되었으면 같은 원문을 꺼내 다시 진행 중으로 둔다 </summary>
        public bool TryTakeAutoRetry(float now, out MarketRequestMessage request)
        {
            if ((!_isHeld) || (!_isUnknown) || (_retryAt <= 0f) || (now < _retryAt))
            {
                request = default;
                return false;
            }

            ++_autoRetries;
            BeginResend(now);
            request = _request;
            return true;
        }

        /// <summary> 사용자가 요청한 같은 ID 재확인, ID가 다르거나 결과 불명이 아니면 거절한다. 자동 재확인 횟수를 새로 센다 </summary>
        public bool TryReconfirm(Guid requestId, float now, out MarketRequestMessage request)
        {
            if ((!_isHeld) || (!_isUnknown) || (requestId != _request.RequestId))
            {
                request = default;
                return false;
            }

            _autoRetries = 0;
            BeginResend(now);
            request = _request;
            return true;
        }

        /// <summary> 연결이 바뀌었다, 미확정 쓰기는 재전송하지 않고 이전 연결의 결과 불명 표식으로만 남긴다 </summary>
        public void ConnectionReplaced()
        {
            if (_isHeld)
            {
                _hasStale    = true;
                _staleAction = _request.Action;
            }

            Clear();
        }

        /// <summary> 권위 있는 목록과 내역을 다시 받아 사용자가 확인할 수 있게 되었다, 이전 연결의 결과를 단정하지 않고 표식만 내린다 </summary>
        public void ResolveStale()
        {
            _hasStale    = false;
            _staleAction = EMarketAction.None;
        }

        public void Clear()
        {
            _request     = default;
            _isHeld      = false;
            _isUnknown   = false;
            _autoRetries = 0;
            _sendCount   = 0;
            _retryAt     = 0f;
            _deadline    = 0f;
        }

        private static bool IsRelayPreflight(ERejectReason reason)
        {
            return (reason == ERejectReason.RateLimited) || (reason == ERejectReason.PendingCommit)
                || (reason == ERejectReason.SessionMismatch);
        }

        private void MarkUnknown(float now)
        {
            _isUnknown = true;
            _retryAt   = (_autoRetries < AUTO_RETRIES) ? (now + AUTO_RETRY_SECONDS) : 0f;
        }

        private void BeginResend(float now)
        {
            ++_sendCount;
            _isUnknown = false;
            _retryAt   = 0f;
            _deadline  = now + RESPONSE_TIMEOUT_SECONDS;
        }
    }
}
