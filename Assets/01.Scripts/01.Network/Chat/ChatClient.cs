// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 호스트도 서버가 돌려준 방송 한 번으로만 표시한다. 보낸 줄을 미리 로그에 넣지 않는다
    /// <summary> 클라이언트의 채팅 송신과 수신 창구, 받은 줄은 ChatLog에 넣는다 </summary>
    public static class ChatClient
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string NO_REPLY_TARGET_TEXT = "답장할 귓속말 상대가 없습니다.";

        private static double _lastSend        = double.NegativeInfinity;
        private static string _lastWhisperFrom = string.Empty;

        /****************************************
        *              Properties
        ****************************************/

        // 마지막으로 귓속말을 보내 온 상대, /r 답장 대상이다. 연결이 끝나면 비운다
        public static string LastWhisperFrom => _lastWhisperFrom;

        /****************************************
        *                Methods
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _lastSend        = double.NegativeInfinity;
            _lastWhisperFrom = string.Empty;
        }

        /// <summary> 클라이언트가 시작할 때 한 번 부른다 </summary>
        public static void RegisterHandlers()
        {
            NetworkClient.ReplaceHandler<ChatBroadcastMessage>(OnBroadcast);
            NetworkClient.ReplaceHandler<ChatRejectMessage>(OnReject);
        }

        // 송신 간격은 일반 채팅과 함께 센다. 서버도 귓속말, 길드, 전체, 파티를 한 창으로 센다
        /// <summary> 캐릭터 이름으로 귓속말을 보낸다. 거절하면 이유를 시스템 안내로 남긴다 </summary>
        public static EChatReject SendWhisper(string targetName, string text)
        {
            EChatReject reason = ValidateWhisper(targetName, text, out string name, out string normalized);
            if (reason != EChatReject.None)
            {
                ChatLog.AddSystem(ChatRules.Describe(reason));
                return reason;
            }

            _lastSend = Time.realtimeSinceStartupAsDouble;
            NetworkClient.Send(new ChatWhisperRequestMessage(name, normalized));
            return EChatReject.None;
        }

        /// <summary> 마지막으로 귓속말을 보내 온 상대에게 답장한다, 상대가 없으면 안내만 남긴다 </summary>
        public static EChatReject SendReply(string text)
        {
            if (string.IsNullOrEmpty(_lastWhisperFrom))
            {
                ChatLog.AddSystem(NO_REPLY_TARGET_TEXT);
                return EChatReject.WhisperTargetNotFound;
            }

            return SendWhisper(_lastWhisperFrom, text);
        }

        // 서버를 옮기면 이전 연결의 답장 상대가 남지 않게 한다. 대화 기록은 ClearHistory가 따로 정한다
        /// <summary> 연결이 끝날 때 부른다, 답장 상대를 지운다 </summary>
        public static void ResetConnection()
        {
            _lastWhisperFrom = string.Empty;
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
            _lastSend        = double.NegativeInfinity;
            _lastWhisperFrom = string.Empty;
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

            if (!ShinshiClientAuthenticator.IsAuthenticatedConnection())
            {
                return EChatReject.NotConnected;
            }

            return CheckInterval();
        }

        private static EChatReject ValidateWhisper(string targetName, string text, out string name, out string normalized)
        {
            normalized = null;
            if (!CharacterNameRules.TryNormalize(targetName, out name, out _))
            {
                return EChatReject.WhisperTargetNotFound;
            }

            EChatReject reason = ChatRules.TryNormalize(text, out normalized);
            if (reason != EChatReject.None)
            {
                return reason;
            }

            if (!ShinshiClientAuthenticator.IsAuthenticatedConnection())
            {
                return EChatReject.NotConnected;
            }

            return CheckInterval();
        }

        private static EChatReject CheckInterval()
        {
            double elapsed = Time.realtimeSinceStartupAsDouble - _lastSend;
            return (elapsed < Constants.Chat.SEND_WINDOW_SECONDS) ? EChatReject.RateLimited : EChatReject.None;
        }

        private static void OnBroadcast(ChatBroadcastMessage message)
        {
            if ((message.Channel == EChatChannel.Whisper) && (!string.IsNullOrEmpty(message.Sender)))
            {
                _lastWhisperFrom = message.Sender;
            }

            ChatLog.AddMessage(message.Channel, message.Sender, message.Text, message.DeliveryId);
        }

        private static void OnReject(ChatRejectMessage message)
        {
            ChatLog.AddSystem(ChatRules.Describe(message.Reason));
        }
    }
}
