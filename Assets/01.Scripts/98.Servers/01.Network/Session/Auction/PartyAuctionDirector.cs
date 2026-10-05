// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Network
{
    // 보스 성공 정산 뒤 같은 던전 프로세스가 파티 경매를 진행하고 출정을 닫는다(API finish). 마감 판정(기본 시간, 막판 연장,
    // 품목 상한)은 이 서버 시계가 하고, API는 예약, 낙찰, 분배와 소유 관계를 확정한다.
    //
    // 입찰은 한 번에 하나만 API로 보낸다. 입찰 순번(expectedBidSeq)을 API와 맞추기 위해서다. 보내는 중에 온 입찰은 PendingCommit으로
    // 돌려준다. 일시 실패는 같은 operationId와 같은 본문으로 다시 보낸다. 응답을 잃은 입찰을 새 ID로 보내면 API에는 이미 예약이
    // 있는데 이 서버는 모르는 상태가 된다. 마감과 finish도 처음 정한 본문(접속 집합, 종료 시각)을 재시도 내내 그대로 쓴다.
    //
    // 백엔드가 없는 개발 출정은 API 대신 즉시 확정으로 흉내 낸다. 잔액은 모르므로 금액 규칙만 본다
    /// <summary> 던전 서버의 파티 경매 진행기, 모든 호출은 서버 메인 스레드에서 한다 </summary>
    public sealed class PartyAuctionDirector : IBackendResponseListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private const double RETRY_MULTIPLIER = 2.0;
        private const string GUID_FORMAT      = "D";

        private readonly string           _sessionId;
        private readonly SessionConfigSO  _config;
        private readonly BackendGateway   _gateway;
        private readonly IAuctionAudience _audience;
        private readonly Func<long>       _unixMilliseconds;
        private readonly Action<string>   _warn;

        // 출정 참가자(원래 참가자) 번호와 API 참가자 ID. 자격은 품목을 열 때 접속한 사람으로 고정한다
        private readonly int[]    _participantIds = new int[Constants.Session.MAX_PARTY_SIZE];
        private readonly string[] _apiIds         = new string[Constants.Session.MAX_PARTY_SIZE];
        private readonly bool[]   _eligible       = new bool[Constants.Session.MAX_PARTY_SIZE];

        private int           _participantCount = 0;
        private int           _eligibleCount    = 0;
        private EAuctionStep  _step             = EAuctionStep.Idle;
        private EAuctionPhase _phase            = EAuctionPhase.None;
        private EAuctionMode  _mode             = EAuctionMode.Bidding;
        private string        _itemId           = null;
        private string        _auctionId        = null;
        private string        _openOperationId  = null;
        private string        _closeOperationId = null;
        private string        _finishOperationId;
        private string[]      _closeConnected   = null;
        private long          _closeEndedAtUnixMs = 0;

        private double _serverTime      = 0.0;
        private double _endsAt          = 0.0;
        private double _capAt           = 0.0;
        private long   _bidSeq          = 0;
        private long   _highBid         = 0;
        private int    _highParticipant = 0;
        private long   _payoutEach      = 0;

        // 보내는 중인 입찰. 일시 실패면 같은 값으로 다시 보낸다
        private bool   _hasPendingBid           = false;
        private int    _pendingParticipant      = 0;
        private long   _pendingAmount           = 0;
        private string _pendingOperationId      = null;
        private long   _pendingAcceptedAtUnixMs = 0;
        private double _pendingAcceptedAt       = 0.0;

        private int    _activeRequestId   = 0;
        private bool   _retryPending      = false;
        private double _nextRetryTime     = 0.0;
        private double _retryDelaySeconds = Constants.Session.SETTLEMENT_RETRY_INITIAL_SECONDS;

        /****************************************
        *              Properties
        ****************************************/

        public EAuctionStep  Step              => _step;
        public EAuctionPhase Phase             => _phase;
        public EAuctionMode  Mode              => _mode;
        public bool          IsFinished        => _step == EAuctionStep.Finished;
        public long          HighBid           => _highBid;
        public int           HighParticipantId => _highParticipant;
        public double        EndsAt            => _endsAt;
        public long          BidSeq            => _bidSeq;
        public long          PayoutEach        => _payoutEach;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> gateway가 null이면 API 없이 개발용으로 흉내 낸다, unixMilliseconds는 API에 싣는 벽시계다 </summary>
        public PartyAuctionDirector(string sessionId, SessionConfigSO config, BackendGateway gateway, IAuctionAudience audience,
            Func<long> unixMilliseconds, Action<string> warn)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                throw new ArgumentException("출정 ID가 필요하다", nameof(sessionId));
            }

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (audience == null)
            {
                throw new ArgumentNullException(nameof(audience));
            }

            if (unixMilliseconds == null)
            {
                throw new ArgumentNullException(nameof(unixMilliseconds));
            }

            _sessionId         = sessionId;
            _config            = config;
            _gateway           = gateway;
            _audience          = audience;
            _unixMilliseconds  = unixMilliseconds;
            _warn              = warn;
            _finishOperationId = Guid.NewGuid().ToString(GUID_FORMAT);
        }

        /// <summary> 출정 참가자 한 명을 더한다, Begin 전에만 받는다. apiParticipantId는 개발 출정이면 참가자 키다 </summary>
        public bool AddParticipant(int participantId, string apiParticipantId)
        {
            if ((_step != EAuctionStep.Idle) || (_participantCount >= _participantIds.Length)
                || (string.IsNullOrEmpty(apiParticipantId)))
            {
                return false;
            }

            _participantIds[_participantCount] = participantId;
            _apiIds[_participantCount]         = apiParticipantId;
            ++_participantCount;
            return true;
        }

        /// <summary> 정산 확정 직후 한 번 부른다. 품목이 없거나 접속 자격자가 없으면 바로 출정 닫기로 간다 </summary>
        public void Begin(double serverTime, string itemDefinitionId)
        {
            if (_step != EAuctionStep.Idle)
            {
                return;
            }

            _serverTime = serverTime;
            _itemId     = itemDefinitionId;
            for (int i = 0; i < _participantCount; ++i)
            {
                _eligible[i] = _audience.IsConnected(_participantIds[i]);
                _eligibleCount += _eligible[i] ? 1 : 0;
            }

            if ((string.IsNullOrEmpty(_itemId)) || (_eligibleCount == 0))
            {
                StartFinishing();
                return;
            }

            _mode            = (_eligibleCount == 1) ? EAuctionMode.Solo : EAuctionMode.Bidding;
            _auctionId       = Guid.NewGuid().ToString(GUID_FORMAT);
            _openOperationId = Guid.NewGuid().ToString(GUID_FORMAT);
            _step            = EAuctionStep.Opening;
            _phase           = EAuctionPhase.Opening;
            Broadcast();
            SendOpen();
        }

        /// <summary> 서버 틱에서 부른다, 재시도와 마감 시각을 처리한다 </summary>
        public void Tick(double serverTime)
        {
            if (serverTime > _serverTime)
            {
                _serverTime = serverTime;
            }

            if ((_retryPending) && (_serverTime >= _nextRetryTime))
            {
                _retryPending = false;
                Resend();
            }

            // 보내는 중인 입찰이 있으면 그 결과(접수 시각 기준 연장)를 본 뒤 마감한다
            if ((_step == EAuctionStep.Open) && (!_hasPendingBid) && (_serverTime >= _endsAt))
            {
                StartClosing();
            }
        }

        /// <summary> 접속 참가자의 입찰, 결과는 그 참가자에게 AuctionBidResultMessage로 보낸다 </summary>
        public void HandleBid(int participantId, long amount)
        {
            ERejectReason reason = CheckBid(participantId, amount);
            if (reason != ERejectReason.None)
            {
                _audience.SendBidResult(participantId, new AuctionBidResultMessage(amount, reason));
                return;
            }

            _hasPendingBid           = true;
            _pendingParticipant      = participantId;
            _pendingAmount           = amount;
            _pendingOperationId      = Guid.NewGuid().ToString(GUID_FORMAT);
            _pendingAcceptedAt       = _serverTime;
            _pendingAcceptedAtUnixMs = _unixMilliseconds();
            SendBid();
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            if ((_activeRequestId == 0) || (response.RequestId != _activeRequestId))
            {
                return;
            }

            _activeRequestId = 0;
            EAuctionReply reply = Classify(in response);
            if (reply == EAuctionReply.Retry)
            {
                ScheduleRetry(in response);
                return;
            }

            ResetRetry();
            long revision = (response.Payload is OperationResult result) ? result.CommittedRevision : 0;
            Apply(reply == EAuctionReply.Committed, response.ReasonCode, revision);
        }

        // 마감은 보내는 중인 입찰이 없을 때만 시작하므로 응답은 입찰 또는 현재 단계의 요청 하나다
        private void Apply(bool committed, int reasonCode, long revision)
        {
            if (_hasPendingBid)
            {
                OnBidReplied(committed, reasonCode, revision);
                return;
            }

            switch (_step)
            {
                case EAuctionStep.Opening:
                    OnOpened(committed, reasonCode);
                    break;
                case EAuctionStep.Closing:
                    OnClosed(committed, reasonCode);
                    break;
                case EAuctionStep.Finishing:
                    OnFinished(committed, reasonCode);
                    break;
            }
        }

        private ERejectReason CheckBid(int participantId, long amount)
        {
            // 응답 대기 중에는 마감 처리를 미룬다. 대기 입찰이 끝나도 다음 틱 전의 늦은 새 입찰은 받지 않는다
            if ((_step != EAuctionStep.Open) || (_serverTime >= _endsAt))
            {
                return ERejectReason.Closed;
            }

            int index = IndexOf(participantId);
            if ((index < 0) || (!_eligible[index]) || (!_audience.IsConnected(participantId)))
            {
                return ERejectReason.NotEligible;
            }

            if (_hasPendingBid)
            {
                return ERejectReason.PendingCommit;
            }

            if ((_highBid > 0) && (_highBid > long.MaxValue - _config.AuctionMinIncrement))
            {
                return ERejectReason.BidTooLow;
            }

            long minimum = MinimumBid();
            bool accepted = (_mode == EAuctionMode.Solo) ? (amount == minimum) : (amount >= minimum);
            return accepted ? ERejectReason.None : ERejectReason.BidTooLow;
        }

        // 단독 모드는 최저가 한 번만 받는다. 경쟁 모드는 최저가 또는 직전 최고가 + 최소 증액 이상이다
        private long MinimumBid()
        {
            if (_highBid <= 0)
            {
                return _config.AuctionMinBid;
            }

            return ((_mode == EAuctionMode.Solo) || (_highBid > long.MaxValue - _config.AuctionMinIncrement))
                ? long.MaxValue
                : _highBid + _config.AuctionMinIncrement;
        }

        private void OnOpened(bool committed, int reasonCode)
        {
            if (!committed)
            {
                // 품목을 열지 못하면 재료는 소멸한다. 기본 보상은 이미 정산됐다
                _warn?.Invoke($"[Auction] 품목 개시 거절로 경매 없이 출정을 닫는다, session={_sessionId}, reason={reasonCode}");
                _phase = EAuctionPhase.Cancelled;
                Broadcast();
                StartFinishing();
                return;
            }

            _endsAt = _serverTime + _config.AuctionBaseSeconds;
            _capAt  = _serverTime + _config.AuctionMaxSeconds;
            _step   = EAuctionStep.Open;
            _phase  = EAuctionPhase.Open;
            Broadcast();
        }

        private void OnBidReplied(bool committed, int reasonCode, long revision)
        {
            int  participant = _pendingParticipant;
            long amount      = _pendingAmount;
            _hasPendingBid = false;
            if (!committed)
            {
                _audience.SendBidResult(participant, new AuctionBidResultMessage(amount, (ERejectReason)reasonCode));
                return;
            }

            _bidSeq          = (revision > 0) ? revision : _bidSeq + 1;
            _highBid         = amount;
            _highParticipant = participant;

            // 남은 시간이 연장 기준보다 짧을 때 접수한 유효 입찰은 남은 시간을 늘린다. 품목 시작부터 상한을 넘지 않는다
            if ((_endsAt - _pendingAcceptedAt) < _config.AuctionExtendThreshold)
            {
                _endsAt = Math.Min(_pendingAcceptedAt + _config.AuctionExtendTo, _capAt);
            }

            _audience.SendBidResult(participant, new AuctionBidResultMessage(amount, ERejectReason.None));
            Broadcast();

            // 단독 구매는 한 번 받으면 바로 끝난다
            if ((_mode == EAuctionMode.Solo) && (_step == EAuctionStep.Open))
            {
                StartClosing();
            }
        }

        private void OnClosed(bool committed, int reasonCode)
        {
            if (!committed)
            {
                // 종료 기록을 저장하지 못했다. 열린 품목은 API 경매 복구가 예약금을 풀어 취소한다
                _warn?.Invoke($"[Auction] 마감 거절, API 복구에 맡긴다, session={_sessionId}, reason={reasonCode}");
                _phase = EAuctionPhase.Cancelled;
            }
            else
            {
                _phase      = (_highBid > 0) ? EAuctionPhase.Sold : EAuctionPhase.Unsold;
                _payoutEach = (_phase == EAuctionPhase.Sold) ? ComputePayoutEach() : 0;
            }

            Broadcast();
            StartFinishing();
        }

        private void OnFinished(bool committed, int reasonCode)
        {
            // 이미 닫힌 출정(API 복구가 먼저 닫음)도 끝난 것으로 본다. 그 밖의 확정 거절은 API 복구가 출정을 닫는다
            if ((!committed) && (reasonCode != ContractCodes.RejectReason.ALREADY_SETTLED))
            {
                _warn?.Invoke($"[Auction] 출정 닫기 거절, API 복구에 맡긴다, session={_sessionId}, reason={reasonCode}");
            }

            _step = EAuctionStep.Finished;
        }

        // 분배 대상 = 품목 시작 자격 ∩ 마감 순간 접속 - 낙찰자. 표시용이며 실제 지급은 API가 저장한 접속 집합으로 정한다
        private long ComputePayoutEach()
        {
            int recipients = 0;
            for (int i = 0; i < _participantCount; ++i)
            {
                if ((_eligible[i]) && (_participantIds[i] != _highParticipant) && (IsInCloseSet(_apiIds[i])))
                {
                    ++recipients;
                }
            }

            return (recipients == 0) ? 0 : _highBid / recipients;
        }

        private bool IsInCloseSet(string apiId)
        {
            if (_closeConnected == null)
            {
                return false;
            }

            for (int i = 0; i < _closeConnected.Length; ++i)
            {
                if (string.Equals(_closeConnected[i], apiId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void StartClosing()
        {
            // 마감 본문은 여기서 한 번 정하고 재시도 내내 같은 값을 보낸다. 마감은 품목마다 한 번이라 이때만 배열을 만든다
            int connected = 0;
            for (int i = 0; i < _participantCount; ++i)
            {
                connected += _audience.IsConnected(_participantIds[i]) ? 1 : 0;
            }

            _closeConnected = new string[connected];
            int cursor = 0;
            for (int i = 0; (i < _participantCount) && (cursor < connected); ++i)
            {
                if (_audience.IsConnected(_participantIds[i]))
                {
                    _closeConnected[cursor] = _apiIds[i];
                    ++cursor;
                }
            }

            _closeEndedAtUnixMs = _unixMilliseconds();
            _closeOperationId   = Guid.NewGuid().ToString(GUID_FORMAT);
            _step               = EAuctionStep.Closing;
            _phase              = EAuctionPhase.Closing;
            Broadcast();
            SendClose();
        }

        private void StartFinishing()
        {
            _step = EAuctionStep.Finishing;
            SendFinish();
        }

        private void Resend()
        {
            if (_hasPendingBid)
            {
                SendBid();
                return;
            }

            switch (_step)
            {
                case EAuctionStep.Opening:
                    SendOpen();
                    break;
                case EAuctionStep.Closing:
                    SendClose();
                    break;
                case EAuctionStep.Finishing:
                    SendFinish();
                    break;
            }
        }

        private void SendOpen()
        {
            string[] eligible = new string[_eligibleCount];
            int cursor = 0;
            for (int i = 0; i < _participantCount; ++i)
            {
                if (_eligible[i])
                {
                    eligible[cursor] = _apiIds[i];
                    ++cursor;
                }
            }

            Send(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.AUCTIONS_FORMAT, _sessionId),
                new AuctionOpenRequest(_openOperationId, _auctionId, _itemId, eligible), _openOperationId);
        }

        private void SendBid()
        {
            int index = IndexOf(_pendingParticipant);
            Send(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.AUCTION_BIDS_FORMAT, _sessionId, _auctionId),
                new AuctionBidRequest(_pendingOperationId, _apiIds[index], _pendingAmount, _bidSeq, _pendingAcceptedAtUnixMs),
                _pendingOperationId);
        }

        private void SendClose()
        {
            Send(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.AUCTION_CLOSE_FORMAT, _sessionId, _auctionId),
                new AuctionCloseRequest(_closeOperationId, _closeConnected, _closeEndedAtUnixMs), _closeOperationId);
        }

        private void SendFinish()
        {
            Send(string.Format(CultureInfo.InvariantCulture, GameServerRoutes.AUCTION_FINISH_FORMAT, _sessionId),
                new AuctionFinishRequest(_finishOperationId), _finishOperationId);
        }

        private void Send(string path, object body, string operationId)
        {
            // 개발 출정: API가 즉시 확정한 것으로 본다
            if (_gateway == null)
            {
                Apply(true, ContractCodes.RejectReason.NONE, _hasPendingBid ? _bidSeq + 1 : 0);
                return;
            }

            BackendRequest request = BackendRequest.Post(path, body, typeof(OperationResult), operationId,
                Constants.Network.API_TIMEOUT_SECONDS);
            _activeRequestId = _gateway.Send(request, this);
        }

        // 경매 사유(자격, 마감, 최저가, 잔액)와 저장되는 거절은 확정이고, 일시 실패와 그 밖의 4xx는 같은 본문으로 다시 보낸다
        private static EAuctionReply Classify(in BackendResponse response)
        {
            switch (response.Outcome)
            {
                case EBackendOutcome.Succeeded:
                    return ((response.Payload is OperationResult result) && (result.Status == ContractCodes.OperationStatus.COMMITTED))
                        ? EAuctionReply.Committed
                        : EAuctionReply.Retry;
                case EBackendOutcome.Rejected:
                    return (IsAuctionReason(response.ReasonCode) || BackendFailureRules.IsTerminalRejection(in response))
                        ? EAuctionReply.Rejected
                        : EAuctionReply.Retry;
                default:
                    return EAuctionReply.Retry;
            }
        }

        private static bool IsAuctionReason(int reasonCode)
        {
            switch (reasonCode)
            {
                case ContractCodes.RejectReason.NOT_ELIGIBLE:
                case ContractCodes.RejectReason.CLOSED:
                case ContractCodes.RejectReason.BID_TOO_LOW:
                case ContractCodes.RejectReason.INSUFFICIENT_CURRENCY:
                case ContractCodes.RejectReason.ALREADY_SETTLED:
                    return true;
                default:
                    return false;
            }
        }

        private void ScheduleRetry(in BackendResponse response)
        {
            _retryPending      = true;
            _nextRetryTime     = _serverTime + Math.Max(_retryDelaySeconds, response.RetryAfterSeconds);
            _retryDelaySeconds = Math.Min(_retryDelaySeconds * RETRY_MULTIPLIER, Constants.Session.SETTLEMENT_RETRY_MAX_SECONDS);
            _warn?.Invoke($"[Auction] 일시 실패, 같은 operationId로 다시 보낸다, session={_sessionId}, step={_step}, " +
                $"outcome={response.Outcome}, status={response.StatusCode}, reason={response.ReasonCode}");
        }

        private void ResetRetry()
        {
            _retryPending      = false;
            _retryDelaySeconds = Constants.Session.SETTLEMENT_RETRY_INITIAL_SECONDS;
        }

        // 받는 사람마다 자격 여부가 달라 참가자별로 보낸다. 끊긴 참가자에게는 대상 구현이 보내지 않는다
        private void Broadcast()
        {
            for (int i = 0; i < _participantCount; ++i)
            {
                SendStateAt(i);
            }
        }

        // 자격은 품목 개시 때 고정한 값이다(03 9장). 개시 뒤 처음 들어온 사람은 그 품목 NotEligible로 본다
        /// <summary> 경매 전용으로 다시 들어온 참가자에게 지금 품목 상태를 한 번 보낸다, 시작 전이거나 대상이 아니면 보내지 않는다 </summary>
        public void SendSnapshot(int participantId)
        {
            int index = IndexOf(participantId);
            if ((_step == EAuctionStep.Idle) || (index < 0))
            {
                return;
            }

            SendStateAt(index);
        }

        private void SendStateAt(int index)
        {
            long minimum   = MinimumBid();
            long published = (minimum == long.MaxValue) ? 0 : minimum;
            var  message   = new AuctionStateMessage(_phase, _mode, _itemId ?? string.Empty, _highBid, _highParticipant,
                published, _endsAt, _eligible[index], _payoutEach);
            _audience.SendState(_participantIds[index], in message);
        }

        private int IndexOf(int participantId)
        {
            for (int i = 0; i < _participantCount; ++i)
            {
                if (_participantIds[i] == participantId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
