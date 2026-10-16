// System
using System;
using System.Collections.Generic;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 같은 서버의 대상에게도 모든 줄을 API 전달함에 먼저 기록한다. API 응답의 localDeliveries에는 이 서버에 임대가 있는 수신자 앞
    // 남은 행(이 줄과 그 수신자 앞 앞선 행)이 수신자별 오름차순으로 오며, 행에 저장된 내용 그대로 바로 보낸다. 행은 클라이언트가 수신을
    // 확인한 뒤 다음 꺼내기에서 지워진다. 길드원 판정은 API 트랜잭션이 하므로 방금 추방된 캐릭터에게 새 줄이 가지 않는다
    /// <summary> 채팅 서버가 넘긴 귓속말, 길드 채팅을 발신자별로 직렬화해 소셜 API로 보내고 로컬 전달을 마친다, 메인 스레드에서만 부른다 </summary>
    public sealed class SocialChatRelay : IBackendResponseListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BackendGateway                _gateway;
        private readonly ISocialAudience               _audience;
        private readonly SocialDeliveryRouter          _router;
        private readonly SocialChatQueue               _queue;
        private readonly Dictionary<int, InFlightLine> _owners;

        /****************************************
        *              Properties
        ****************************************/

        // 채팅 서버가 설치 때 자신을 넣는다
        public ISocialChatListener Listener { get; set; }

        public SocialChatQueue Queue => _queue;

        // 응답을 기다리는 API 호출 수
        public int PendingCount => _owners.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> gateway가 null이면 모든 줄을 BackendUnavailable로 거절한다 </summary>
        public SocialChatRelay(BackendGateway gateway, ISocialAudience audience, SocialDeliveryRouter router)
        {
            if ((audience == null) || (router == null))
            {
                throw new ArgumentNullException(nameof(audience), "채팅 중계에 필요한 참조가 없다");
            }

            _gateway  = gateway;
            _audience = audience;
            _router   = router;
            _queue    = new SocialChatQueue();
            _owners   = new Dictionary<int, InFlightLine>(Constants.Session.MAX_SESSION_PARTICIPANTS);
        }

        /// <summary> 정리를 끝낸 한 줄을 받는다. 곧바로 거절할 사유가 있으면 그 사유, 받아들였으면 None(결과는 Listener로 간다) </summary>
        public EChatReject Submit(int participantId, in SocialChatLine line)
        {
            if (_gateway == null)
            {
                return EChatReject.BackendUnavailable;
            }

            if (string.IsNullOrEmpty(line.CharacterId))
            {
                return EChatReject.NotConnected;
            }

            EChatReject reason = _queue.TryEnqueue(participantId, in line, out bool sendNow);
            if ((reason == EChatReject.None) && (sendNow))
            {
                Send(participantId, in line);
            }

            return reason;
        }

        /// <summary> 연결이 끊긴 발신자의 대기 줄을 버린다 </summary>
        public void Forget(int participantId)
        {
            _queue.Forget(participantId);
        }

        // 게이트웨이가 메인 스레드의 Pump에서 부른다
        public void OnBackendResponse(in BackendResponse response)
        {
            if (!_owners.TryGetValue(response.RequestId, out InFlightLine inFlight))
            {
                return;
            }

            _owners.Remove(response.RequestId);
            SocialChatLine line = inFlight.Line;
            SocialActionResponse nested;
            SocialDeliveryRow[]  rows;
            string               targetName = line.TargetName;
            if (response.Payload is SocialWhisperResponse whisper)
            {
                nested = whisper.Result;
                rows   = whisper.LocalDeliveries;
                if (!string.IsNullOrEmpty(whisper.TargetName))
                {
                    targetName = whisper.TargetName;
                }
            }
            else if (response.Payload is SocialGuildChatResponse guild)
            {
                nested = guild.Result;
                rows   = guild.LocalDeliveries;
            }
            else
            {
                nested = null;
                rows   = null;
            }

            ESocialApiVerdict verdict = SocialApiRules.Classify(in response, nested, out ERejectReason reason);
            if ((verdict == ESocialApiVerdict.Committed) && (response.Payload != null))
            {
                OnCommitted(inFlight.ParticipantId, in line, targetName, rows);
            }
            else
            {
                EChatReject chatReason = (verdict == ESocialApiVerdict.Rejected)
                    ? SocialApiRules.ToChatReject(reason)
                    : EChatReject.BackendUnavailable;
                Listener?.OnSocialChatRejected(inFlight.ParticipantId, chatReason);
            }

            if (_queue.TryNext(inFlight.ParticipantId, out SocialChatLine next))
            {
                Send(inFlight.ParticipantId, in next);
            }
        }

        private void OnCommitted(int participantId, in SocialChatLine line, string targetName, SocialDeliveryRow[] rows)
        {
            if (line.Channel == EChatChannel.Whisper)
            {
                _audience.Send(participantId, new ChatBroadcastMessage(EChatChannel.WhisperSent, ClipName(targetName), line.Text, 0));
            }

            Listener?.OnSocialChatCommitted(participantId, line.CharacterId, line.ApiParticipantId, line.Channel, line.SenderName,
                line.Text);
            _router.RouteAll(rows);
        }

        private void Send(int participantId, in SocialChatLine line)
        {
            BackendRequest request;
            if (line.Channel == EChatChannel.Whisper)
            {
                request = BackendRequest.Post(Format(SocialRoutes.WHISPER_FORMAT, line.CharacterId),
                    new SocialWhisperRequest(line.TargetName, line.Text), typeof(SocialWhisperResponse), null,
                    Constants.Network.API_TIMEOUT_SECONDS);
            }
            else
            {
                request = BackendRequest.Post(Format(SocialRoutes.GUILD_CHAT_FORMAT, line.CharacterId),
                    new SocialGuildChatRequest(line.Text), typeof(SocialGuildChatResponse), null, Constants.Network.API_TIMEOUT_SECONDS);
            }

            int requestId = _gateway.Send(request, this);
            _owners[requestId] = new InFlightLine(participantId, line);
        }

        private static string ClipName(string name)
        {
            if (name == null)
            {
                return string.Empty;
            }

            return (name.Length > Constants.Chat.MAX_SENDER_LENGTH) ? name.Substring(0, Constants.Chat.MAX_SENDER_LENGTH) : name;
        }

        private static string Format(string format, string characterId)
        {
            return string.Format(CultureInfo.InvariantCulture, format, characterId);
        }

        /****************************************
        *               Records
        ****************************************/

        private readonly struct InFlightLine
        {
            public readonly int            ParticipantId;
            public readonly SocialChatLine Line;

            public InFlightLine(int participantId, in SocialChatLine line)
            {
                ParticipantId = participantId;
                Line          = line;
            }
        }
    }
}
