// System
using System;

namespace Shinshi.Network
{
    /// <summary> 마을, 던전 역할 모두에서 채팅 서버를 만들고 메시지 처리기와 연결 종료 정리를 건다 </summary>
    public static class ChatServerInstaller
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버를 연 뒤 한 번 부른다. party가 null이면 파티 채팅도 프로세스의 모든 참가자에게 보낸다(던전) </summary>
        public static ChatServerHost Install(ShinshiNetworkManager manager, IChatPartyLookup party)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            var host = new ChatServerHost(new ChatServer(new MirrorChatAudience(), party, null, new ServerChatLogSink()));
            host.RegisterHandlers();

            // 서버 수명 시작에 한 번만 건다. 매니저 이벤트라 Mirror 정적 이벤트처럼 덮어써지지 않는다
            manager.ServerDisconnected += host.HandleServerDisconnected;
            return host;
        }
    }
}
