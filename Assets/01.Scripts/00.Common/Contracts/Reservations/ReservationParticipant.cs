// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 출정 참가자 한 명의 예약 요청 </summary>
    [DataContract]
    public sealed class ReservationParticipant
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "accountId")]         private string              _accountId;
        [DataMember(Name = "characterId")]       private string              _characterId;

        // 마을이 준비 확인에 쓴 캐릭터 revision, 다르면 전체를 거절한다
        [DataMember(Name = "characterRevision")] private long                _characterRevision;
        [DataMember(Name = "potions")]           private ReservationPotion[] _potions;

        /****************************************
        *              Properties
        ****************************************/

        public string              AccountId         => _accountId;
        public string              CharacterId       => _characterId;
        public long                CharacterRevision => _characterRevision;
        public ReservationPotion[] Potions           => _potions;

        /****************************************
        *                Methods
        ****************************************/

        public ReservationParticipant(string accountId, string characterId, long characterRevision,
            ReservationPotion[] potions)
        {
            _accountId         = accountId;
            _characterId       = characterId;
            _characterRevision = characterRevision;
            _potions           = potions;
        }

        private ReservationParticipant() { }
    }
}
