// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 캐릭터 이름으로 대상을 고르는 요청(친구 요청, 길드 초대) </summary>
    [DataContract]
    public sealed class SocialTargetNameRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 정규화 전 이름도 받는다. API가 CharacterNameRules로 다시 정규화한다
        [DataMember(Name = "targetName")] private string _targetName;

        /****************************************
        *              Properties
        ****************************************/

        public string TargetName => _targetName;

        /****************************************
        *                Methods
        ****************************************/

        public SocialTargetNameRequest(string targetName)
        {
            _targetName = targetName;
        }

        private SocialTargetNameRequest() { }
    }
}
