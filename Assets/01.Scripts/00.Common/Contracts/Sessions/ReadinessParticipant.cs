// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 준비 확인을 받을 참가자 한 명 </summary>
    [DataContract]
    public sealed class ReadinessParticipant
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "accountId")]   private string _accountId;
        [DataMember(Name = "characterId")] private string _characterId;

        /****************************************
        *              Properties
        ****************************************/

        public string AccountId   => _accountId;
        public string CharacterId => _characterId;

        /****************************************
        *                Methods
        ****************************************/

        public ReadinessParticipant(string accountId, string characterId)
        {
            _accountId   = accountId;
            _characterId = characterId;
        }

        private ReadinessParticipant() { }
    }
}
