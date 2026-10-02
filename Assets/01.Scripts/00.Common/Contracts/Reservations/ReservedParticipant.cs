// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 예약된 참가자의 영구 participantId </summary>
    [DataContract]
    public sealed class ReservedParticipant
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "participantId")] private string _participantId;
        [DataMember(Name = "accountId")]     private string _accountId;
        [DataMember(Name = "characterId")]   private string _characterId;

        /****************************************
        *              Properties
        ****************************************/

        public string ParticipantId => _participantId;
        public string AccountId     => _accountId;
        public string CharacterId   => _characterId;

        /****************************************
        *                Methods
        ****************************************/

        public ReservedParticipant(string participantId, string accountId, string characterId)
        {
            _participantId = participantId;
            _accountId     = accountId;
            _characterId   = characterId;
        }

        private ReservedParticipant() { }
    }
}
