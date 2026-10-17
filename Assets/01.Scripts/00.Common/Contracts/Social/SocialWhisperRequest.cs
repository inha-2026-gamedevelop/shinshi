// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 귓속말. text는 게임 서버가 정리를 끝낸 1~100자 본문이다 </summary>
    [DataContract]
    public sealed class SocialWhisperRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "targetName")] private string _targetName;
        [DataMember(Name = "text")]       private string _text;

        /****************************************
        *              Properties
        ****************************************/

        public string TargetName => _targetName;
        public string Text       => _text;

        /****************************************
        *                Methods
        ****************************************/

        public SocialWhisperRequest(string targetName, string text)
        {
            _targetName = targetName;
            _text       = text;
        }

        private SocialWhisperRequest() { }
    }
}
