// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 길드 채팅. text는 게임 서버가 정리를 끝낸 1~100자 본문이다 </summary>
    [DataContract]
    public sealed class SocialGuildChatRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "text")] private string _text;

        /****************************************
        *              Properties
        ****************************************/

        public string Text => _text;

        /****************************************
        *                Methods
        ****************************************/

        public SocialGuildChatRequest(string text)
        {
            _text = text;
        }

        private SocialGuildChatRequest() { }
    }
}
