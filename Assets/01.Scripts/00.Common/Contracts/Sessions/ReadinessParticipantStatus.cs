// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 참가자 한 명의 준비 상태. reasonCode가 NONE일 때만 나머지 값을 읽는다 </summary>
    [DataContract]
    public sealed class ReadinessParticipantStatus
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "accountId")]         private string              _accountId;
        [DataMember(Name = "characterId")]       private string              _characterId;

        // NONE, SESSION_MISMATCH(이 마을에 접속하지 않았거나 남의, 삭제한 캐릭터), RESERVATION_CONFLICT(열린 출정 있음)
        [DataMember(Name = "reasonCode")]        private int                 _reasonCode;

        // 예약 요청의 characterRevision에 그대로 넣는다. 다르면 예약이 STALE_REVISION으로 거절된다
        [DataMember(Name = "characterRevision")] private long                _characterRevision;

        // 예약분을 뺀 가용 보스 티켓
        [DataMember(Name = "availableTickets")]  private long                _availableTickets;

        // 예약분을 뺀 가용 물약. quantity가 가용 수량이다
        [DataMember(Name = "potions")]           private ReservationPotion[] _potions;

        // 계정 진행 기록. reasonCode가 NONE이 아니면 null이다(접속하지 않은 계정의 진행을 알려 주지 않는다)
        [DataMember(Name = "progress")]          private ReadinessProgress   _progress;

        /****************************************
        *              Properties
        ****************************************/

        public string              AccountId         => _accountId;
        public string              CharacterId       => _characterId;
        public int                 ReasonCode        => _reasonCode;
        public long                CharacterRevision => _characterRevision;
        public long                AvailableTickets  => _availableTickets;
        public ReservationPotion[] Potions           => _potions;
        public ReadinessProgress   Progress          => _progress;

        /****************************************
        *                Methods
        ****************************************/

        public ReadinessParticipantStatus(string accountId, string characterId, int reasonCode, long characterRevision,
            long availableTickets, ReservationPotion[] potions)
            : this(accountId, characterId, reasonCode, characterRevision, availableTickets, potions, null)
        {
        }

        public ReadinessParticipantStatus(string accountId, string characterId, int reasonCode, long characterRevision,
            long availableTickets, ReservationPotion[] potions, ReadinessProgress progress)
        {
            _accountId         = accountId;
            _characterId       = characterId;
            _reasonCode        = reasonCode;
            _characterRevision = characterRevision;
            _availableTickets  = availableTickets;
            _potions           = potions;
            _progress          = progress;
        }

        private ReadinessParticipantStatus() { }
    }
}
