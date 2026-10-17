// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 보낸 사람 이름과 번호는 호스트가 인증된 결합에서 꺼내 넘긴다. 메시지 안에는 신원 자리가 없다.
    // 잘못된 요청도 속도 제한 창을 소비해야 변조 클라이언트가 정리 비용을 계속 쓰게 하지 못한다.
    // 귓속말, 길드 채팅은 같은 속도 제한 창을 쓰고 검사를 마친 뒤 소셜 채팅 중계로 넘긴다. 결과는 API 응답 뒤에 중계가 알려 주며
    // 거절 안내는 여기의 간격 제한을 그대로 거친다
    /// <summary> 채팅 요청을 검증하고 채널의 수신 대상에게 방송한다, 메인 스레드에서만 부른다 </summary>
    public sealed class ChatServer : ISocialChatListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IChatAudience           _audience;
        private readonly IChatPartyLookup        _party;
        private readonly IChatLogSink            _log;
        private readonly SocialChatRelay         _social;
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

        /// <summary> party가 null이면 프로세스 하나가 세션 하나인 던전이라 파티 채팅도 모든 참가자에게 보낸다. log가 null이면 기록하지 않는다.
        /// social이 null이면 길드 채팅과 귓속말을 FeatureDisabled로 거절한다 </summary>
        public ChatServer(IChatAudience audience, IChatPartyLookup party, Func<double> clock, IChatLogSink log = null,
            SocialChatRelay social = null)
        {
            if (audience == null)
            {
                throw new ArgumentNullException(nameof(audience));
            }

            _audience    = audience;
            _party       = party;
            _log         = log;
            _social      = social;
            _clock       = clock ?? BackendGateway.MonotonicSeconds;
            _senders     = new Dictionary<int, Sender>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            _partyBuffer = new List<int>(Constants.Session.MAX_SESSION_PARTICIPANTS);
            if (_social != null)
            {
                _social.Listener = this;
            }
        }

        // 기록은 받아들여 방송한 줄만 남긴다. 거절은 속도 제한에 걸린 요청까지 변조 클라이언트가 빈도를 정하므로 남기지 않는다
        /// <summary> 요청 한 건을 처리한다. 받아들였으면 None, 거절했으면 이유(안내는 간격 제한 안에서만 보낸다) </summary>
        public EChatReject Handle(int participantId, string characterName, in ChatRequestMessage message, string characterId = null,
            string apiParticipantId = null)
        {
            double now    = _clock();
            Sender sender = GetOrCreate(participantId);

            if (!sender.Throttle.TryAccept(now, Constants.Chat.MESSAGES_PER_WINDOW, Constants.Chat.SEND_WINDOW_SECONDS))
            {
                return Reject(participantId, sender, now, EChatReject.RateLimited);
            }

            if (!ChatRules.IsSendable(message.Channel))
            {
                return Reject(participantId, sender, now, EChatReject.InvalidChannel);
            }

            if ((message.Channel == EChatChannel.Guild) && (_social == null))
            {
                return Reject(participantId, sender, now, EChatReject.FeatureDisabled);
            }

            EChatReject reason = ChatRules.TryNormalize(message.Text, out string text);
            if (reason != EChatReject.None)
            {
                return Reject(participantId, sender, now, reason);
            }

            // 길드 채팅은 전체, 파티 방송으로 새지 않고 백엔드 전달함을 거친다
            if (message.Channel == EChatChannel.Guild)
            {
                var line = new SocialChatLine(EChatChannel.Guild, characterId, ChatRules.NormalizeSender(characterName, participantId),
                    null, text, apiParticipantId);
                return Submit(participantId, sender, now, in line);
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

        /// <summary> 귓속말 요청 한 건. 받아들였으면 None(전달 결과는 API 응답 뒤에 온다), 거절했으면 이유 </summary>
        public EChatReject HandleWhisper(int participantId, string characterName, in ChatWhisperRequestMessage message,
            string characterId = null, string apiParticipantId = null)
        {
            double now    = _clock();
            Sender sender = GetOrCreate(participantId);
            if (!sender.Throttle.TryAccept(now, Constants.Chat.MESSAGES_PER_WINDOW, Constants.Chat.SEND_WINDOW_SECONDS))
            {
                return Reject(participantId, sender, now, EChatReject.RateLimited);
            }

            if (_social == null)
            {
                return Reject(participantId, sender, now, EChatReject.FeatureDisabled);
            }

            EChatReject reason = ChatRules.TryNormalize(message.Text, out string text);
            if (reason != EChatReject.None)
            {
                return Reject(participantId, sender, now, reason);
            }

            if (!CharacterNameRules.TryNormalize(message.TargetName, out string targetName, out string targetKey))
            {
                return Reject(participantId, sender, now, EChatReject.WhisperTargetNotFound);
            }

            if (SocialRequestRules.IsSelfName(targetKey, characterName))
            {
                return Reject(participantId, sender, now, EChatReject.WhisperSelf);
            }

            var line = new SocialChatLine(EChatChannel.Whisper, characterId, ChatRules.NormalizeSender(characterName, participantId),
                targetName, text, apiParticipantId);
            return Submit(participantId, sender, now, in line);
        }

        // 중계가 API 응답으로 거절을 알렸다. 안내는 다른 거절과 같은 간격 제한을 거친다
        public void OnSocialChatRejected(int participantId, EChatReject reason)
        {
            if (reason == EChatReject.None)
            {
                return;
            }

            Reject(participantId, GetOrCreate(participantId), _clock(), reason);
        }

        public void OnSocialChatCommitted(int participantId, string characterId, string apiParticipantId, EChatChannel channel,
            string sender, string text)
        {
            _log?.Accepted(participantId, apiParticipantId, characterId, channel, sender, text);
        }

        /// <summary> 연결이 끊긴 참가자의 제한 기록을 지운다 </summary>
        public void Forget(int participantId)
        {
            _senders.Remove(participantId);
            _social?.Forget(participantId);
        }

        private EChatReject Submit(int participantId, Sender sender, double now, in SocialChatLine line)
        {
            EChatReject reason = _social.Submit(participantId, in line);
            return (reason == EChatReject.None) ? EChatReject.None : Reject(participantId, sender, now, reason);
        }

        private Sender GetOrCreate(int participantId)
        {
            if (!_senders.TryGetValue(participantId, out Sender sender))
            {
                sender = new Sender();
                _senders.Add(participantId, sender);
            }

            return sender;
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
