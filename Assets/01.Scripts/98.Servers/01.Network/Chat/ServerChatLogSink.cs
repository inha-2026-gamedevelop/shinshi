namespace Shinshi.Network
{
    /// <summary> 수락한 채팅을 서버 JSON 로그 chat.message 한 줄로 쓴다, 로그 처리기가 없는 에디터에서는 아무것도 만들지 않는다 </summary>
    public sealed class ServerChatLogSink : IChatLogSink
    {
        public void Accepted(int connParticipantId, string apiParticipantId, string characterId, EChatChannel channel, string sender, string text)
        {
            // 처리기가 없으면 문자열을 만들지 않는다
            if (!ServerJsonLogHandler.IsInstalled)
            {
                return;
            }

            ServerJsonLogHandler.LogParticipantEvent(ServerLogLine.LEVEL_INFO, ServerLogLine.EVENT_CHAT_MESSAGE, apiParticipantId,
                ChatLogRules.BuildMessageData(channel, connParticipantId, sender, characterId, text));
        }
    }
}
