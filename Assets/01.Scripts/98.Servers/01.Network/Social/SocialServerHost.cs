// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 신원은 인증기가 연결에 붙인 SessionBinding에서만 꺼낸다. 결합이 없거나 끊긴 연결의 메시지는 버린다.
    // 던전도 처리기를 등록한다. 처리기가 없으면 Mirror가 모르는 메시지를 보낸 클라이언트를 끊는다
    /// <summary> 소셜 메시지를 Mirror에 등록하고 관리 중계, 채팅 중계, 전달함 꺼내기, 수신 확인을 묶는다 </summary>
    public sealed class SocialServerHost
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly SocialRelayServer    _relay;
        private readonly SocialChatRelay      _chat;
        private readonly SocialDeliveryRouter _router;
        private readonly SocialDeliveryPump   _pump;
        private readonly MirrorSocialAudience _audience;
        private readonly Func<double>         _clock;

        /****************************************
        *              Properties
        ****************************************/

        public SocialRelayServer    Relay  => _relay;
        public SocialChatRelay      Chat   => _chat;
        public SocialDeliveryRouter Router => _router;

        // 게이트웨이가 없으면 null이다
        public SocialDeliveryPump   Pump   => _pump;

        /****************************************
        *                Methods
        ****************************************/

        public SocialServerHost(SocialRelayServer relay, SocialChatRelay chat, SocialDeliveryRouter router, SocialDeliveryPump pump,
            MirrorSocialAudience audience, Func<double> clock)
        {
            _relay    = relay;
            _chat     = chat;
            _router   = router;
            _pump     = pump;
            _audience = audience;
            _clock    = clock ?? BackendGateway.MonotonicSeconds;
        }

        /// <summary> 서버를 연 뒤 부른다, 그 전에 등록하면 Listen의 초기화에 지워진다 </summary>
        public void RegisterHandlers()
        {
            NetworkServer.ReplaceHandler<SocialRequestMessage>(OnRequest);
            NetworkServer.ReplaceHandler<SocialDeliveryAckMessage>(OnAck);
        }

        /// <summary> 매니저의 연결 종료 알림에 건다 </summary>
        public void HandleServerDisconnected(NetworkConnectionToClient connection)
        {
            if (!(connection.authenticationData is SessionBinding binding))
            {
                return;
            }

            _relay.RemoveMember(binding.ParticipantId);
            _chat.Forget(binding.ParticipantId);
            _router.Ledger.Forget(binding.ParticipantId);
            _audience.Remove(connection);
        }

        /// <summary> 매 프레임 부른다 </summary>
        public void Tick()
        {
            _pump?.Tick();
        }

        private void OnRequest(NetworkConnectionToClient connection, SocialRequestMessage message)
        {
            if ((!(connection.authenticationData is SessionBinding binding)) || (!binding.IsActive))
            {
                return;
            }

            _relay.Handle(binding.ParticipantId, binding.CharacterId, binding.CharacterName, in message);
        }

        private void OnAck(NetworkConnectionToClient connection, SocialDeliveryAckMessage message)
        {
            if ((!(connection.authenticationData is SessionBinding binding)) || (!binding.IsActive))
            {
                return;
            }

            _router.Ledger.AcceptAcks(binding.ParticipantId, message.DeliveryIds, _clock());
        }
    }
}
