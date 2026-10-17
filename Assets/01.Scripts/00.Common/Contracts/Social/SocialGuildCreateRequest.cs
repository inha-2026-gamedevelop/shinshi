// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 길드 생성. 결과 불명이면 같은 createRequestId로 다시 보낸다 </summary>
    [DataContract]
    public sealed class SocialGuildCreateRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // GuildNameRules로 정규화한다
        [DataMember(Name = "name")]            private string _name;
        // 클라이언트가 만든 Guid "D" 형식
        [DataMember(Name = "createRequestId")] private string _createRequestId;

        /****************************************
        *              Properties
        ****************************************/

        public string Name            => _name;
        public string CreateRequestId => _createRequestId;

        /****************************************
        *                Methods
        ****************************************/

        public SocialGuildCreateRequest(string name, string createRequestId)
        {
            _name            = name;
            _createRequestId = createRequestId;
        }

        private SocialGuildCreateRequest() { }
    }
}
