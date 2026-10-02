// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> session_participants 한 행. 두 정기 적립은 정산 전 출정 원장이며 장애 취소 지급의 근거다 </summary>
    [DataContract]
    public sealed class AdminSessionParticipant
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "participantId")]      private string _participantId;
        [DataMember(Name = "accountId")]          private string _accountId;
        [DataMember(Name = "characterId")]        private string _characterId;
        [DataMember(Name = "state")]              private string _state;
        [DataMember(Name = "essenceAccrued")]     private long   _essenceAccrued;
        [DataMember(Name = "baseEssenceAccrued")] private long   _baseEssenceAccrued;
        [DataMember(Name = "settledAtUnixMs")]    private long?  _settledAtUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public string ParticipantId      => _participantId;
        public string AccountId          => _accountId;
        public string CharacterId        => _characterId;
        public string State              => _state;
        public long   EssenceAccrued     => _essenceAccrued;
        public long   BaseEssenceAccrued => _baseEssenceAccrued;
        public long?  SettledAtUnixMs    => _settledAtUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public AdminSessionParticipant(string participantId, string accountId, string characterId, string state,
            long essenceAccrued, long baseEssenceAccrued, long? settledAtUnixMs)
        {
            _participantId      = participantId;
            _accountId          = accountId;
            _characterId        = characterId;
            _state              = state;
            _essenceAccrued     = essenceAccrued;
            _baseEssenceAccrued = baseEssenceAccrued;
            _settledAtUnixMs    = settledAtUnixMs;
        }

        private AdminSessionParticipant() { }
    }
}
