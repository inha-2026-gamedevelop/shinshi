// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 세션 이벤트 배치를 게이트웨이로 events 경로에 저장한다, 확정 응답만 성공으로 알린다 </summary>
    public sealed class HttpBackendEventSink : IBackendEventSink, IBackendResponseListener
    {
        // 저장 큐는 ACK 전까지 같은 배치(같은 순번 범위와 payload)를 다시 넘긴다. 이벤트 ID와 operationId를 순번에서 만들므로
        // 재시도 본문이 바이트 단위로 같고, 응답을 잃은 배치를 다시 보내도 API가 처음 확정한 결과를 돌려준다.
        //
        // 순번은 한 건당 하나라 서버 이벤트 하나를 여러 건으로 나눌 수 없으므로, 경제 효과가 없는 참가, 연결 이탈, 웨이브 완료는
        // 저장만 하는 LOOT 종류에 진단 payload로 싣는다. 진행 중 개인 이탈 확정(ParticipantSettled, 포기와 유예 만료)은
        // PARTICIPANT_DEPARTED로 보낸다. 추가 정기 적립은 ESSENCE_ACCRUED, 완료 웨이브 기본 정기 적립은
        // BASE_ESSENCE_ACCRUED로 보낸다. 둘 다 장애 취소의 지급 기준이다(기본 전액, 추가 50% 내림).
        // 물약 사용은 POTION_USED(참가자, 정의, 귀속), 되돌림은 POTION_USE_REVERTED(참가자, 원 이벤트 ID)로 보낸다. 형식이
        // 틀린 물약 이벤트를 그대로 보내면 API가 배치 전체를 확정 거절해 큐가 멈추므로 그때는 진단 LOOT로만 남긴다.
        // 지참 수량과 보스 사용 상한은 PotionServer가 보내기 전에 막는다. 저장 거절은 큐를 멈춰 출정을 장애 취소로 끝낸다.
        // 서비스 장애 취소는 API 복구 작업만 기록할 수 있어 SESSION_ENDED로 보내지 않는다.
        // 배치 직렬화는 1초에 한 번 이하로 일어나는 저빈도 경로라 DTO와 JSON 할당을 허용한다

        /****************************************
        *                Fields
        ****************************************/

        private const int PENDING_CAPACITY = 4;

        private readonly BackendGateway             _gateway;
        private readonly Action<string>             _warn;
        private readonly Dictionary<int, Pending>   _pending = new Dictionary<int, Pending>(PENDING_CAPACITY);

        /****************************************
        *              Properties
        ****************************************/

        public int PendingCount => _pending.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> warn은 null이면 기록하지 않는다 </summary>
        public HttpBackendEventSink(BackendGateway gateway, Action<string> warn)
        {
            if (gateway == null)
            {
                throw new ArgumentNullException(nameof(gateway));
            }

            _gateway = gateway;
            _warn    = warn;
        }

        public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count, IBackendEventSinkListener listener)
        {
            if ((listener == null) || (events == null) || (count <= 0) || (count > events.Length))
            {
                return false;
            }

            // 출정 ID가 계약 형식이 아니면 몇 번을 보내도 거절된다. 큐는 백오프로 다시 부르므로 경고만 남긴다
            if (!SessionContractIds.IsApiId(sessionId))
            {
                _warn?.Invoke("[SessionEvents] 출정 ID가 GUID가 아니라 저장하지 못한다");
                return false;
            }

            BackendRequest request = CreateRequest(sessionId, events, count);
            int requestId = _gateway.Send(request, this);
            _pending[requestId] = new Pending(attemptId, events[count - 1].Seq, listener);
            return true;
        }

        /// <summary> 배치 요청을 만든다, 같은 입력이면 operationId와 본문이 같다 </summary>
        public static BackendRequest CreateRequest(string sessionId, SessionEvent[] events, int count)
        {
            var entries = new SessionEventEntry[count];
            for (int i = 0; i < count; ++i)
            {
                entries[i] = ToEntry(sessionId, in events[i]);
            }

            string operationId = SessionContractIds.CreateEventsOperationId(sessionId, events[0].Seq, events[count - 1].Seq);
            string path        = string.Format(CultureInfo.InvariantCulture, GameServerRoutes.EVENTS_FORMAT, sessionId);
            return BackendRequest.Post(path, new SessionEventBatchRequest(operationId, entries), typeof(OperationResult),
                operationId, Constants.Network.API_TIMEOUT_SECONDS);
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            if (!_pending.TryGetValue(response.RequestId, out Pending pending))
            {
                return;
            }

            _pending.Remove(response.RequestId);
            EEventBatchResult result = Classify(in response);
            if (result == EEventBatchResult.Transient)
            {
                _warn?.Invoke($"[SessionEvents] 저장 실패, 같은 배치를 다시 보낸다, lastSeq={pending.LastSequence}, " +
                    $"outcome={response.Outcome}, status={response.StatusCode}, reason={response.ReasonCode}");
            }
            else if (result == EEventBatchResult.Terminal)
            {
                _warn?.Invoke($"[SessionEvents] 저장 확정 거절, 큐를 멈추고 출정을 장애 취소로 끝낸다, lastSeq={pending.LastSequence}, " +
                    $"status={response.StatusCode}, reason={response.ReasonCode}");
            }

            pending.Listener.OnBatchCompleted(pending.AttemptId, result, response.RetryAfterSeconds);
        }

        /// <summary> 응답을 저장 결과로 가른다, 202 PENDING은 처리 중이라 성공이 아니다 </summary>
        public static EEventBatchResult Classify(in BackendResponse response)
        {
            if (response.IsSucceeded)
            {
                return ((response.Payload is OperationResult result) && (result.Status == ContractCodes.OperationStatus.COMMITTED))
                    ? EEventBatchResult.Committed
                    : EEventBatchResult.Transient;
            }

            // API 복구 작업이 이미 정산해 닫은 출정에는 이후 이벤트가 들어갈 수 없다
            if ((response.Outcome == EBackendOutcome.Rejected)
                && (response.ReasonCode == ContractCodes.RejectReason.ALREADY_SETTLED))
            {
                return EEventBatchResult.Terminal;
            }

            return BackendFailureRules.IsTerminalRejection(in response) ? EEventBatchResult.Terminal : EEventBatchResult.Transient;
        }

        private static SessionEventEntry ToEntry(string sessionId, in SessionEvent sessionEvent)
        {
            SessionEventData data    = sessionEvent.Data;
            string           eventId = SessionContractIds.CreateEventId(sessionId, sessionEvent.Seq);
            if ((data.Type == ESessionEventType.SessionEnded) && (data.Reason != ESessionEndReason.ServiceCancelled))
            {
                return new SessionEventEntry(sessionEvent.Seq, eventId, GameSessionCodes.EventKind.SESSION_ENDED, null, null,
                    null, 0L, (int)data.Reason, SessionContractIds.CreateSettlementOperationId(sessionId),
                    data.Grants ?? Array.Empty<SettlementGrant>(), null);
            }

            // 참가자 ID가 있으면 API가 GUID로 읽는다. 개발 키처럼 형식이 다르면 배치 전체가 거절되므로 payload에만 남긴다
            string participantId = SessionContractIds.TryGetApiParticipantId(data.ParticipantId, out string apiId) ? apiId : null;

            if ((data.Type == ESessionEventType.PotionUsed) && (participantId != null)
                && (!string.IsNullOrEmpty(data.DefinitionId)) && (!string.IsNullOrEmpty(data.BindingKind)))
            {
                return new SessionEventEntry(sessionEvent.Seq, eventId, GameSessionCodes.EventKind.POTION_USED, participantId,
                    data.DefinitionId, data.BindingKind, 0L, 0, null, null, null);
            }

            // 진행 중 개인 이탈(포기, 유예 만료)은 PARTICIPANT_DEPARTED로 보낸다. API가 같은 트랜잭션에서 그 계정의 던전 접속 임대를
            // 풀어 마을 복귀를 허용한다. 경제 정산은 하지 않고 SESSION_ENDED 정산안이 이 참가자를 한 번 포함한다
            if ((data.Type == ESessionEventType.ParticipantSettled) && (participantId != null) && (IsDepartReason(data.Reason)))
            {
                return new SessionEventEntry(sessionEvent.Seq, eventId, GameSessionCodes.EventKind.PARTICIPANT_DEPARTED,
                    participantId, null, null, 0L, (int)data.Reason, null, null, null);
            }

            // 물약 정의와 귀속은 API가 원 이벤트에서 읽으므로 싣지 않는다
            if ((data.Type == ESessionEventType.PotionUseReverted) && (participantId != null) && (data.RevertsSequence > 0)
                && (data.RevertsSequence < sessionEvent.Seq))
            {
                return new SessionEventEntry(sessionEvent.Seq, eventId, GameSessionCodes.EventKind.POTION_USE_REVERTED,
                    participantId, null, null, 0L, 0, null, null, null,
                    SessionContractIds.CreateEventId(sessionId, data.RevertsSequence));
            }

            // 적립은 API 출정 원장에 쌓여 장애 취소의 지급 기준이 된다. 대상이 GUID가 아니거나 양이 0 이하면 API가
            // 배치 전체를 거절하므로 그때는 진단 LOOT로만 남긴다
            int accrualKind = GetAccrualKind(data.Type);
            if ((accrualKind != GameSessionCodes.EventKind.LOOT) && (participantId != null) && (data.Amount > 0))
            {
                return new SessionEventEntry(sessionEvent.Seq, eventId, accrualKind, participantId, null, null,
                    data.Amount, 0, null, null, null);
            }

            return new SessionEventEntry(sessionEvent.Seq, eventId, GameSessionCodes.EventKind.LOOT, participantId, null, null,
                0L, 0, null, null, BackendJson.Serialize(new DiagnosticPayload(in data)));
        }

        private static bool IsDepartReason(ESessionEndReason reason)
        {
            return (reason == ESessionEndReason.Abandon) || (reason == ESessionEndReason.DisconnectExpired);
        }

        // 적립 이벤트가 아니면 LOOT를 돌려준다
        private static int GetAccrualKind(ESessionEventType type)
        {
            switch (type)
            {
                case ESessionEventType.EssenceAccrued:
                    return GameSessionCodes.EventKind.ESSENCE_ACCRUED;
                case ESessionEventType.BaseEssenceAccrued:
                    return GameSessionCodes.EventKind.BASE_ESSENCE_ACCRUED;
                default:
                    return GameSessionCodes.EventKind.LOOT;
            }
        }

        /****************************************
        *               Records
        ****************************************/

        private readonly struct Pending
        {
            private readonly long                      _attemptId;
            private readonly long                      _lastSequence;
            private readonly IBackendEventSinkListener _listener;

            public long                      AttemptId    => _attemptId;
            public long                      LastSequence => _lastSequence;
            public IBackendEventSinkListener Listener     => _listener;

            public Pending(long attemptId, long lastSequence, IBackendEventSinkListener listener)
            {
                _attemptId    = attemptId;
                _lastSequence = lastSequence;
                _listener     = listener;
            }
        }

        // 저장만 하는 이벤트의 진단 내용. 키 이름은 저장 후 조회 도구가 읽으므로 바꾸지 않는다.
        // 필드는 JSON 직렬화기만 읽으므로 컴파일러의 미사용 경고를 끈다
#pragma warning disable CS0414
        [DataContract]
        private sealed class DiagnosticPayload
        {
            [DataMember(Name = "type")]                private string _type;
            [DataMember(Name = "serverTime")]          private double _serverTime;
            [DataMember(Name = "participantKey")]      private string _participantKey;
            [DataMember(Name = "sourceId")]            private string _sourceId;
            [DataMember(Name = "killerParticipantId")] private string _killerParticipantId;
            [DataMember(Name = "killSequence")]        private long   _killSequence;
            [DataMember(Name = "waveIndex")]           private int    _waveIndex;
            [DataMember(Name = "reason")]              private int    _reason;
            [DataMember(Name = "isBossSession")]       private bool   _isBossSession;
            [DataMember(Name = "extraEssence")]        private long   _extraEssence;
            [DataMember(Name = "ticketQuantity")]      private int    _ticketQuantity;
            [DataMember(Name = "talismanQuantity")]    private int    _talismanQuantity;
            [DataMember(Name = "talismanEffectId")]    private string _talismanEffectId;
            [DataMember(Name = "amount")]              private long   _amount;
            [DataMember(Name = "definitionId")]        private string _definitionId;
            [DataMember(Name = "revertsSequence")]     private long   _revertsSequence;

            public DiagnosticPayload(in SessionEventData data)
            {
                _type                = data.Type.ToString();
                _serverTime          = data.ServerTime;
                _participantKey      = data.ParticipantId;
                _sourceId            = data.SourceId;
                _killerParticipantId = data.KillerParticipantId;
                _killSequence        = data.KillSequence;
                _waveIndex           = data.WaveIndex;
                _reason              = (int)data.Reason;
                _isBossSession       = data.IsBossSession;
                _extraEssence        = data.Loot.ExtraEssence;
                _ticketQuantity      = data.Loot.TicketQuantity;
                _talismanQuantity    = data.Loot.TalismanQuantity;
                _talismanEffectId    = data.Loot.TalismanEffectId;
                _amount              = data.Amount;
                _definitionId        = data.DefinitionId;
                _revertsSequence     = data.RevertsSequence;
            }
        }
#pragma warning restore CS0414
    }
}
