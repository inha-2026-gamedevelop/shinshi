// System
using System.Globalization;
using System.Text;

namespace Shinshi.Network
{
    // 계정 ID, 접속 주소, 토큰은 넣지 않는다. 던전은 인증 결합에 계정이 없고, 마을 결합에는 있으나 신고 조사는
    // sessionId, API 참가자 ID, characterId로 API에서 역추적한다. 본문은 방송한 정리본이라 화면에 보인 그대로다
    /// <summary> 수락한 채팅 한 줄을 로그 data JSON 객체로 바꾸는 순수 규칙 </summary>
    public static class ChatLogRules
    {
        /****************************************
        *                Fields
        ****************************************/

        // 이벤트 data 의 키와 채널 이름. 수집기 쿼리가 쓰므로 바꾸지 않는다
        private const string CHANNEL_ALL      = "All";
        private const string CHANNEL_PARTY    = "Party";
        private const string CHANNEL_SYSTEM   = "System";
        private const string CHANNEL_GUILD    = "Guild";
        private const string CHANNEL_WHISPER  = "Whisper";
        private const int    INITIAL_CAPACITY = 256;

        /****************************************
        *                Methods
        ****************************************/

        // 본문은 사용자 입력이라 토큰을 붙여 넣을 수 있다. 다른 로그 줄과 같은 Bearer 마스킹을 거친다
        /// <summary> data 객체 한 개, 문자열 이스케이프는 서버 로그 줄과 같은 규칙이다. 값이 없으면 null 리터럴이다 </summary>
        public static string BuildMessageData(EChatChannel channel, int connParticipantId, string sender, string characterId, string text)
        {
            var b = new StringBuilder(INITIAL_CAPACITY);
            b.Append("{\"channel\":");
            ServerLogLine.AppendJsonString(b, ChannelName(channel));
            b.Append(",\"connParticipant\":");
            b.Append(connParticipantId.ToString(CultureInfo.InvariantCulture));
            b.Append(",\"characterId\":");
            ServerLogLine.AppendJsonString(b, string.IsNullOrEmpty(characterId) ? null : characterId);
            b.Append(",\"sender\":");
            ServerLogLine.AppendJsonString(b, sender);
            b.Append(",\"text\":");
            ServerLogLine.AppendJsonString(b, ServerLogLine.RedactBearer(text));
            b.Append('}');
            return b.ToString();
        }

        /// <summary> 열거형 ToString 의 할당과 이름 변경 영향을 피해 고정 문자열을 쓴다 </summary>
        public static string ChannelName(EChatChannel channel)
        {
            switch (channel)
            {
                case EChatChannel.All:
                    return CHANNEL_ALL;
                case EChatChannel.Party:
                    return CHANNEL_PARTY;
                case EChatChannel.Guild:
                    return CHANNEL_GUILD;
                // 보낸 귓속말은 보낸 사람 쪽 기록 한 줄만 남긴다
                case EChatChannel.Whisper:
                case EChatChannel.WhisperSent:
                    return CHANNEL_WHISPER;
                default:
                    return CHANNEL_SYSTEM;
            }
        }
    }
}
