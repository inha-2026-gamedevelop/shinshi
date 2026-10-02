// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> SessionEnded 저장 확인 뒤 출정 하나를 한 번 정산한다, 모든 호출은 서버 메인 스레드에서 한다 </summary>
    public sealed class SessionSettlement : IBackendResponseListener
    {
        // 정산 효과는 API가 operationId로 한 번만 적용한다. 그래서 다시 부를 때도 SessionEnded에 적은 같은 ID를 쓰고,
        // 새 ID를 만들지 않는다. 다른 ID로 부르면 API는 그 ID를 받지 않는다.
        // 이미 닫힌 출정(ALREADY_SETTLED)은 API 복구 작업이 먼저 같은 정산안을 적용한 경우라 성공으로 본다.
        // 일시 실패와 확정이 아닌 거절은 횟수 상한 없이 백오프(최대 30초)로 계속 부른다. 출정 프로세스는 정산이 끝나야
        // 재활용되므로 중간에 포기하면 정산 확인 없이 내려가게 된다. 확정 거절(400, 저장된 거절)만 멈추고, 그 뒤는
        // 종료 이벤트가 저장돼 있으므로 API 복구 작업이 같은 operationId로 정산을 끝낸다

        /****************************************
        *                Fields
        ****************************************/

        private const double RETRY_MULTIPLIER = 2.0;

        private readonly string         _sessionId;
        private readonly string         _operationId;
        private readonly BackendGateway _gateway;
        private readonly Action<string> _warn;

        private ESessionSettlementState _state             = ESessionSettlementState.Idle;
        private int                     _activeRequestId   = 0;
        private int                     _requestCount      = 0;
        private double                  _serverTime        = 0.0;
        private double                  _nextRetryTime     = 0.0;
        private double                  _retryDelaySeconds = Constants.Session.SETTLEMENT_RETRY_INITIAL_SECONDS;

        /****************************************
        *              Properties
        ****************************************/

        public ESessionSettlementState State       => _state;
        public string                  OperationId => _operationId;

        // 실제로 게이트웨이에 넘긴 횟수, 게이트웨이 안의 재시도는 세지 않는다
        public int                     RequestCount => _requestCount;

        public bool IsFinished => (_state == ESessionSettlementState.Settled) || (_state == ESessionSettlementState.Abandoned);

        // 다음 재시도까지의 간격(초), 시험과 진단에 쓴다
        public double RetryDelaySeconds => _retryDelaySeconds;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> gateway가 null이면 개발 빌드에서만 기록만 남기고 성공으로 처리한다, 출시 빌드는 예외 </summary>
        public SessionSettlement(string sessionId, BackendGateway gateway, Action<string> warn)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                throw new ArgumentException("출정 ID가 필요하다", nameof(sessionId));
            }

#if !(UNITY_EDITOR || DEVELOPMENT_BUILD)
            if (gateway == null)
            {
                throw new ArgumentNullException(nameof(gateway));
            }
#endif

            _sessionId   = sessionId;
            _operationId = SessionContractIds.CreateSettlementOperationId(sessionId);
            _gateway     = gateway;
            _warn        = warn;
        }

        /// <summary> SessionEnded 저장 확인 뒤 부른다, 처음 한 번만 요청하고 이후 호출은 거짓 </summary>
        public bool TryBegin(double serverTime)
        {
            if (_state != ESessionSettlementState.Idle)
            {
                return false;
            }

            _serverTime = serverTime;
            if (_gateway == null)
            {
                _warn?.Invoke($"[Settlement] 개발 정산 - 백엔드 없이 정산 완료로 처리한다, session={_sessionId}");
                _state = ESessionSettlementState.Settled;
                return true;
            }

            Send();
            return true;
        }

        /// <summary> 서버 틱에서 부른다, 재시도 시각이 되면 같은 operationId로 다시 보낸다 </summary>
        public void Tick(double serverTime)
        {
            if (serverTime > _serverTime)
            {
                _serverTime = serverTime;
            }

            if ((_state != ESessionSettlementState.Retrying) || (_serverTime < _nextRetryTime))
            {
                return;
            }

            Send();
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            if ((_state != ESessionSettlementState.InFlight) || (response.RequestId != _activeRequestId))
            {
                return;
            }

            _activeRequestId = 0;
            switch (Classify(in response))
            {
                case ESessionSettlementState.Settled:
                    _state = ESessionSettlementState.Settled;
                    break;
                case ESessionSettlementState.Abandoned:
                    Abandon(in response);
                    break;
                default:
                    ScheduleRetry(in response);
                    break;
            }
        }

        private void Send()
        {
            string path = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.SETTLEMENT_FORMAT, _sessionId);
            BackendRequest request = BackendRequest.Post(path, new SessionSettlementRequest(_operationId),
                typeof(OperationResult), _operationId, Constants.Network.API_TIMEOUT_SECONDS);
            ++_requestCount;
            _state           = ESessionSettlementState.InFlight;
            _activeRequestId = _gateway.Send(request, this);
        }

        private static ESessionSettlementState Classify(in BackendResponse response)
        {
            switch (response.Outcome)
            {
                case EBackendOutcome.Succeeded:
                    // PENDING은 같은 작업이 처리 중이라는 뜻이다. 다시 물으면 확정 결과를 받는다
                    return ((response.Payload is OperationResult result) && (result.Status == ContractCodes.OperationStatus.COMMITTED))
                        ? ESessionSettlementState.Settled
                        : ESessionSettlementState.Retrying;
                case EBackendOutcome.Rejected:
                    if (response.ReasonCode == ContractCodes.RejectReason.ALREADY_SETTLED)
                    {
                        return ESessionSettlementState.Settled;
                    }

                    // 종료 이벤트보다 먼저 닿은 정산은 API가 저장하지 않고 단계 오류로 돌려준다. 같은 ID로 다시 부를 수 있다
                    if (response.ReasonCode == ContractCodes.RejectReason.WRONG_PHASE)
                    {
                        return ESessionSettlementState.Retrying;
                    }

                    return BackendFailureRules.IsTerminalRejection(in response)
                        ? ESessionSettlementState.Abandoned
                        : ESessionSettlementState.Retrying;
                default:
                    return ESessionSettlementState.Retrying;
            }
        }

        private void ScheduleRetry(in BackendResponse response)
        {
            _state             = ESessionSettlementState.Retrying;
            _nextRetryTime     = _serverTime + Math.Max(_retryDelaySeconds, response.RetryAfterSeconds);
            _retryDelaySeconds = Math.Min(_retryDelaySeconds * RETRY_MULTIPLIER, Constants.Session.SETTLEMENT_RETRY_MAX_SECONDS);
            _warn?.Invoke($"[Settlement] 정산 일시 실패, 같은 operationId로 다시 부른다, session={_sessionId}, " +
                $"outcome={response.Outcome}, status={response.StatusCode}, reason={response.ReasonCode}, requests={_requestCount}");
        }

        private void Abandon(in BackendResponse response)
        {
            _state = ESessionSettlementState.Abandoned;
            _warn?.Invoke($"[Settlement] 정산 확정 거절로 호출을 멈춘다, API 복구 작업이 같은 operationId로 끝낸다, session={_sessionId}, " +
                $"outcome={response.Outcome}, status={response.StatusCode}, reason={response.ReasonCode}, requests={_requestCount}");
        }
    }
}
