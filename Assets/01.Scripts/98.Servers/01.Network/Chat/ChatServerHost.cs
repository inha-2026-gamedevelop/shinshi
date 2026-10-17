// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 신원은 인증기가 연결에 붙인 SessionBinding에서만 꺼낸다. 결합이 없거나 끊긴 연결의 메시지는 버린다
    /// <summary> 채팅 요청을 Mirror에 등록하고 인증된 연결을 ChatServer에 넘긴다 </summary>
    public sealed class ChatServerHost
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ChatServer _server;

        /****************************************
        *              Properties
        ****************************************/

        public ChatServer Server => _server;

        /****************************************
        *                Methods
        ****************************************/

        public ChatServerHost(ChatServer server)
        {
            _server = server;
        }

        /// <summary> 서버를 연 뒤 부른다, 그 전에 등록하면 Listen의 초기화에 지워진다 </summary>
        public void RegisterHandlers()
        {
            NetworkServer.ReplaceHandler<ChatRequestMessage>(OnChat);
            NetworkServer.ReplaceHandler<ChatWhisperRequestMessage>(OnWhisper);
        }

        /// <summary> 매니저의 연결 종료 알림에 건다 </summary>
        public void HandleServerDisconnected(NetworkConnectionToClient connection)
        {
            if (connection.authenticationData is SessionBinding binding)
            {
                _server.Forget(binding.ParticipantId);
            }
        }

        private void OnChat(NetworkConnectionToClient connection, ChatRequestMessage message)
        {
            if ((!(connection.authenticationData is SessionBinding binding)) || (!binding.IsActive))
            {
                return;
            }

            // API 참가자 ID는 던전 키에서만 나온다. 마을 키와 개발 토큰은 null이다
            SessionContractIds.TryGetApiParticipantId(binding.ParticipantKey, out string apiParticipantId);
            _server.Handle(binding.ParticipantId, binding.CharacterName, in message, binding.CharacterId, apiParticipantId);
        }

        private void OnWhisper(NetworkConnectionToClient connection, ChatWhisperRequestMessage message)
        {
            if ((!(connection.authenticationData is SessionBinding binding)) || (!binding.IsActive))
            {
                return;
            }

            SessionContractIds.TryGetApiParticipantId(binding.ParticipantKey, out string apiParticipantId);
            _server.HandleWhisper(binding.ParticipantId, binding.CharacterName, in message, binding.CharacterId, apiParticipantId);
        }
    }
}
