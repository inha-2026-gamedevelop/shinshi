// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 호스트도 서버가 돌려준 방송 한 번으로만 표시한다. 보낸 줄을 미리 로그에 넣지 않는다
    /// <summary> 클라이언트의 채팅 송신과 수신 창구, 받은 줄은 ChatLog에 넣는다 </summary>
    public static class ChatClient
    {
        /****************************************
        *                Fields
        ****************************************/

        private static double _lastSend = double.NegativeInfinity;

        /****************************************
        *                Methods
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _lastSend = double.NegativeInfinity;
        }

        /// <summary> 클라이언트가 시작할 때 한 번 부른다 </summary>
        public static void RegisterHandlers()
        {
            NetworkClient.ReplaceHandler<ChatBroadcastMessage>(OnBroadcast);
            NetworkClient.ReplaceHandler<ChatRejectMessage>(OnReject);
        }

        /// <summary> 검사를 통과하면 서버로 보내고 None. 거절하면 이유를 시스템 안내로 남긴다 </summary>
        public static EChatReject Send(EChatChannel channel, string text)
        {
            EChatReject reason = Validate(channel, text, out string normalized);
            if (reason != EChatReject.None)
            {
                ChatLog.AddSystem(ChatRules.Describe(reason));
                return reason;
            }

            _lastSend = Time.realtimeSinceStartupAsDouble;
            NetworkClient.Send(new ChatRequestMessage(channel, normalized));
            return EChatReject.None;
        }

        /// <summary> 접속을 끝내면 이전 대화와 송신 간격을 지운다 </summary>
        public static void ClearHistory()
        {
            _lastSend = double.NegativeInfinity;
            ChatLog.Clear();
        }

        private static EChatReject Validate(EChatChannel channel, string text, out string normalized)
        {
            normalized = null;
            if (!ChatRules.IsSendable(channel))
            {
                return EChatReject.InvalidChannel;
            }

            EChatReject reason = ChatRules.TryNormalize(text, out normalized);
            if (reason != EChatReject.None)
            {
                return reason;
            }

            if ((!NetworkClient.isConnected) || (NetworkClient.connection == null))
            {
                return EChatReject.NotConnected;
            }

            double elapsed = Time.realtimeSinceStartupAsDouble - _lastSend;
            return (elapsed < Constants.Chat.SEND_WINDOW_SECONDS) ? EChatReject.RateLimited : EChatReject.None;
        }

        private static void OnBroadcast(ChatBroadcastMessage message)
        {
            ChatLog.AddMessage(message.Channel, message.Sender, message.Text);
        }

        private static void OnReject(ChatRejectMessage message)
        {
            ChatLog.AddSystem(ChatRules.Describe(message.Reason));
        }
    }
}
