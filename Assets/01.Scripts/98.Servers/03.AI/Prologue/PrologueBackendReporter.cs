// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.AI
{
    // 단계 저장은 가장 최근 값만 의미가 있다. 한 번에 한 요청만 보내고, 응답을 잃거나 일시 장애면 같은 operationId로 다시 보낸다.
    // 확정 거절이면 그 값은 버린다(다음 단계 보고가 덮어쓴다). 완료 기록은 단계 저장과 별개 요청이고 확정되기 전에는 세션을 닫지 않는다.
    // 백엔드 창구가 없는 개발 출정은 아무것도 보내지 않고 모두 확정으로 본다
    /// <summary> 프롤로그 단계 저장과 완료 기록을 API로 보낸다, 서버 틱(메인 스레드)에서만 부른다 </summary>
    public sealed class PrologueBackendReporter : IBackendResponseListener
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string GUID_FORMAT = "D";

        // 일시 장애 뒤 다시 보내기까지 기다리는 시간(초)
        private const double RETRY_DELAY_SECONDS = 1.0;

        /****************************************
        *                Types
        ****************************************/

        private enum EKind
        {
            None,
            Stage,
            Complete
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly BackendGateway _gateway;
        private readonly string         _sessionId;
        private readonly string         _characterId;

        // 보내야 할 최신 단계 값과 그 operationId
        private bool   _hasStage        = false;
        private int    _stage           = 0;
        private int    _wolbaekPotions  = 0;
        private bool   _talismanChecked = false;
        private string _stageOperation  = null;

        // 완료 기록
        private bool   _completeRequested = false;
        private bool   _completeCommitted = false;
        private bool   _completeDropped   = false;
        private string _completeOperation = null;

        private int    _inFlightRequestId = 0;
        private EKind  _inFlightKind      = EKind.None;
        private string _inFlightOperation = null;
        private double _nextSendTime      = 0.0;
        private double _lastTickTime      = 0.0;

        /****************************************
        *              Properties
        ****************************************/

        // 백엔드가 없으면 보낼 곳이 없어 항상 확정으로 본다
        public bool IsDisabled => (_gateway == null) || (string.IsNullOrEmpty(_sessionId)) || (string.IsNullOrEmpty(_characterId));

        // 완료 기록이 확정됐거나(또는 확정 거절로 더 보내지 않거나) 보낼 곳이 없다
        public bool IsCompleteSettled => IsDisabled || _completeCommitted || _completeDropped;

        /****************************************
        *                Methods
        ****************************************/

        public PrologueBackendReporter(BackendGateway gateway, string sessionId, string characterId)
        {
            _gateway     = gateway;
            _sessionId   = sessionId;
            _characterId = characterId;
        }

        /// <summary> 최신 단계 값을 보낼 값으로 올린다, 같은 값이면 새 요청을 만들지 않는다 </summary>
        public void ReportStage(int stage, int wolbaekPotions, bool talismanChecked)
        {
            if ((_hasStage) && (_stage == stage) && (_wolbaekPotions == wolbaekPotions) && (_talismanChecked == talismanChecked))
            {
                return;
            }

            _hasStage        = true;
            _stage           = stage;
            _wolbaekPotions  = wolbaekPotions;
            _talismanChecked = talismanChecked;
            _stageOperation  = Guid.NewGuid().ToString(GUID_FORMAT);
        }

        /// <summary> 완료 기록을 요청한다, 한 번만 만든다 </summary>
        public void ReportComplete()
        {
            if (_completeRequested)
            {
                return;
            }

            _completeRequested = true;
            _completeOperation = Guid.NewGuid().ToString(GUID_FORMAT);
        }

        /// <summary> 서버 틱마다 부른다, 보낼 것이 있고 비어 있으면 하나 보낸다. 완료 기록이 단계 저장보다 먼저다 </summary>
        public void Tick(double serverTime)
        {
            _lastTickTime = serverTime;
            if ((IsDisabled) || (_inFlightKind != EKind.None) || (serverTime < _nextSendTime))
            {
                return;
            }

            if ((_completeRequested) && (!_completeCommitted) && (!_completeDropped))
            {
                string path = string.Format(CultureInfo.InvariantCulture, PrologueRoutes.COMPLETE_FORMAT, _sessionId);
                BackendRequest request = BackendRequest.Post(path, new PrologueCompleteRequest(_completeOperation, _characterId),
                    typeof(OperationResult), _completeOperation, Constants.Network.API_TIMEOUT_SECONDS);
                _inFlightRequestId = _gateway.Send(request, this);
                _inFlightKind      = EKind.Complete;
                _inFlightOperation = _completeOperation;
                return;
            }

            // 완료 기록 뒤에는 더 낮은 단계 저장을 보내지 않는다(API도 되돌리는 단계를 거절한다)
            if ((_hasStage) && (!_completeRequested) && (_stageOperation != null))
            {
                string path = string.Format(CultureInfo.InvariantCulture, PrologueRoutes.STAGE_FORMAT, _sessionId);
                BackendRequest request = BackendRequest.Post(path,
                    new PrologueStageRequest(_stageOperation, _characterId, _stage, _wolbaekPotions, _talismanChecked),
                    typeof(OperationResult), _stageOperation, Constants.Network.API_TIMEOUT_SECONDS);
                _inFlightRequestId = _gateway.Send(request, this);
                _inFlightKind      = EKind.Stage;
                _inFlightOperation = _stageOperation;
            }
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            if ((_inFlightKind == EKind.None) || (response.RequestId != _inFlightRequestId))
            {
                return;
            }

            EKind kind    = _inFlightKind;
            _inFlightKind = EKind.None;

            var  result    = response.Payload as OperationResult;
            bool committed = (response.IsSucceeded) && (result != null) && (result.Status == ContractCodes.OperationStatus.COMMITTED);
            bool definite  = (!committed) && ((TownRejectRules.IsDefiniteRejection(in response))
                || ((result != null) && (result.Status == ContractCodes.OperationStatus.REJECTED)));

            if (kind == EKind.Complete)
            {
                if (committed)
                {
                    _completeCommitted = true;
                }
                else if (definite)
                {
                    UnityEngine.Debug.LogError($"[Prologue] 완료 기록이 거절됐다, session={_sessionId}, character={_characterId}, " +
                        $"status={response.StatusCode}, reason={response.ReasonCode}");
                    _completeDropped = true;
                }
                else
                {
                    ScheduleRetry(in response);
                }

                return;
            }

            // 단계 저장: 확정이면 그 값이 최신일 때만 보낼 값을 비운다. 그 사이 더 새로운 값이 올라왔으면 다음 틱에 다시 보낸다
            if ((committed) || (definite))
            {
                if (definite)
                {
                    UnityEngine.Debug.LogError($"[Prologue] 단계 저장이 거절됐다, session={_sessionId}, stage={_stage}, " +
                        $"status={response.StatusCode}, reason={response.ReasonCode}");
                }

                if (string.Equals(_stageOperation, _inFlightOperation, StringComparison.Ordinal))
                {
                    _stageOperation = null;
                }

                return;
            }

            ScheduleRetry(in response);
        }

        // 확정도 확정 거절도 아니면 일시 장애다. 같은 operationId로 잠시 뒤 다시 보낸다
        private void ScheduleRetry(in BackendResponse response)
        {
            _nextSendTime = _lastTickTime + Math.Max(RETRY_DELAY_SECONDS, response.RetryAfterSeconds);
        }
    }
}
