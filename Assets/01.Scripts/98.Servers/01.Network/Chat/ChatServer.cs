// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 보낸 사람 이름과 번호는 호스트가 인증된 결합에서 꺼내 넘긴다. 메시지 안에는 신원 자리가 없다.
    // 잘못된 요청도 속도 제한 창을 소비해야 변조 클라이언트가 정리 비용을 계속 쓰게 하지 못한다
    /// <summary> 채팅 요청을 검증하고 채널의 수신 대상에게 방송한다, 메인 스레드에서만 부른다 </summary>
    public sealed class ChatServer
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IChatAudience           _audience;
        private readonly IChatPartyLookup        _party;
        private readonly IChatLogSink            _log;
        private readonly Func<double>            _clock;
        private readonly Dictionary<int, Sender> _senders;
        private readonly List<int>               _partyBuffer;

        /****************************************
        *              Properties
        ****************************************/

        public int TrackedSenderCount => _senders.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> party가 null이면 프로세스 하나가 세션 하나인 던전이라 파티 채팅도 모든 참가자에게 보낸다. log가 null이면 기록하지 않는다 </summary>
        public ChatServer(IChatAudience audience, IChatPartyLookup party, Func<double> clock, IChatLogSink log = null)
        {
            if (audience == null)
            {
                throw new ArgumentNullException(nameof(audience));
            }

            _audience    = audience;
            _party       = party;
            _log         = log;
            _clock       = clock ?? BackendGateway.MonotonicSeconds;
            _senders     = new Dictionary<int, Sender>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _partyBuffer = new List<int>(Constants.Session.MAX_SESSION_PARTICIPANTS);
        }

        // 기록은 받아들여 방송한 줄만 남긴다. 거절은 속도 제한에 걸린 요청까지 변조 클라이언트가 빈도를 정하므로 남기지 않는다
        /// <summary> 요청 한 건을 처리한다. 받아들였으면 None, 거절했으면 이유(안내는 간격 제한 안에서만 보낸다) </summary>
        public EChatReject Handle(int participantId, string characterName, in ChatRequestMessage message, string characterId = null,
            string apiParticipantId = null)
        {
            double now = _clock();
            if (!_senders.TryGetValue(participantId, out Sender sender))
            {
                sender = new Sender();
                _senders.Add(participantId, sender);
            }

            if (!sender.Throttle.TryAccept(now, Constants.Chat.MESSAGES_PER_WINDOW, Constants.Chat.SEND_WINDOW_SECONDS))
            {
                return Reject(participantId, sender, now, EChatReject.RateLimited);
            }

            if (!ChatRules.IsSendable(message.Channel))
            {
                return Reject(participantId, sender, now, EChatReject.InvalidChannel);
            }

            EChatReject reason = ChatRules.TryNormalize(message.Text, out string text);
            if (reason != EChatReject.None)
            {
                return Reject(participantId, sender, now, reason);
            }

            var broadcast = new ChatBroadcastMessage(message.Channel, ChatRules.NormalizeSender(characterName, participantId), text);
            if ((message.Channel == EChatChannel.All) || (_party == null))
            {
                _audience.SendToAll(broadcast);
                _log?.Accepted(participantId, apiParticipantId, characterId, message.Channel, broadcast.Sender, text);
                return EChatReject.None;
            }

            _partyBuffer.Clear();
            if (!_party.TryGetPartyMembers(participantId, _partyBuffer))
            {
                return Reject(participantId, sender, now, EChatReject.NoParty);
            }

            for (int i = 0; i < _partyBuffer.Count; ++i)
            {
                _audience.Send(_partyBuffer[i], broadcast);
            }

            _log?.Accepted(participantId, apiParticipantId, characterId, message.Channel, broadcast.Sender, text);
            return EChatReject.None;
        }

        /// <summary> 연결이 끊긴 참가자의 제한 기록을 지운다 </summary>
        public void Forget(int participantId)
        {
            _senders.Remove(participantId);
        }

        private EChatReject Reject(int participantId, Sender sender, double now, EChatReject reason)
        {
            if ((now - sender.LastRejectNotice) >= Constants.Chat.REJECT_NOTICE_INTERVAL_SECONDS)
            {
                sender.LastRejectNotice = now;
                _audience.Send(participantId, new ChatRejectMessage(reason));
            }

            return reason;
        }

        /****************************************
        *                Types
        ****************************************/

        // 참가자마다 하나, 처음 보낼 때 만든다
        private sealed class Sender
        {
            public TownRequestThrottle Throttle;
            public double              LastRejectNotice = double.NegativeInfinity;
        }
    }
}
