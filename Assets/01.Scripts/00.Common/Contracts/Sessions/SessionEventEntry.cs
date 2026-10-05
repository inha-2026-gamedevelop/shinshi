// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 순번 있는 세션 이벤트 한 건, 종류마다 쓰는 필드만 채운다 </summary>
    [DataContract]
    public sealed class SessionEventEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        // 던전당 1부터 빈틈없이 증가한다. 중복, 역행, 건너뜀은 배치 전체를 거절한다
        [DataMember(Name = "eventSeq")]              private long              _eventSeq;
        [DataMember(Name = "eventId")]               private string            _eventId;

        // GameSessionCodes.EventKind
        [DataMember(Name = "eventKind")]             private int               _eventKind;

        // POTION_USED, ESSENCE_ACCRUED, BASE_ESSENCE_ACCRUED, PARTICIPANT_DEPARTED의 대상
        [DataMember(Name = "participantId")]         private string            _participantId;

        // POTION_USED의 물약 정의와 귀속
        [DataMember(Name = "definitionId")]          private string            _definitionId;
        [DataMember(Name = "bindingKind")]           private string            _bindingKind;

        // ESSENCE_ACCRUED의 추가 정기, 정산 전 출정 원장에만 쌓인다.
        // BASE_ESSENCE_ACCRUED에서는 완료 웨이브의 기본 정기이며 역시 출정 원장에만 쌓인다
        [DataMember(Name = "amount")]                private long              _amount;

        // SESSION_ENDED의 GameSessionCodes.EndReason. PARTICIPANT_DEPARTED에서는 떠난 이유(ABANDON, DISCONNECT_EXPIRED)다
        [DataMember(Name = "endReason")]             private int               _endReason;

        // SESSION_ENDED 뒤 /settlement에 쓸 ID. 프로세스가 사라지면 API 복구 작업도 같은 ID로 정산한다
        [DataMember(Name = "settlementOperationId")] private string            _settlementOperationId;

        // SESSION_ENDED의 참가자별 정산안, 던전 서버가 계산하고 API는 다시 계산하지 않는다
        [DataMember(Name = "grants")]                private SettlementGrant[] _grants;

        // LOOT 등 저장만 하는 진단 내용
        [DataMember(Name = "payloadJson")]           private string            _payloadJson;

        // POTION_USE_REVERTED가 되돌리는 원 POTION_USED의 eventId
        [DataMember(Name = "revertsEventId")]        private string            _revertsEventId;

        /****************************************
        *              Properties
        ****************************************/

        public long              EventSeq              => _eventSeq;
        public string            EventId               => _eventId;
        public int               EventKind             => _eventKind;
        public string            ParticipantId         => _participantId;
        public string            DefinitionId          => _definitionId;
        public string            BindingKind           => _bindingKind;
        public long              Amount                => _amount;
        public int               EndReason             => _endReason;
        public string            SettlementOperationId => _settlementOperationId;
        public SettlementGrant[] Grants                => _grants;
        public string            PayloadJson           => _payloadJson;
        public string            RevertsEventId        => _revertsEventId;

        /****************************************
        *                Methods
        ****************************************/

        public SessionEventEntry(long eventSeq, string eventId, int eventKind, string participantId,
            string definitionId, string bindingKind, long amount, int endReason, string settlementOperationId,
            SettlementGrant[] grants, string payloadJson)
        {
            _eventSeq              = eventSeq;
            _eventId               = eventId;
            _eventKind             = eventKind;
            _participantId         = participantId;
            _definitionId          = definitionId;
            _bindingKind           = bindingKind;
            _amount                = amount;
            _endReason             = endReason;
            _settlementOperationId = settlementOperationId;
            _grants                = grants;
            _payloadJson           = payloadJson;
        }

        /// <summary> POTION_USE_REVERTED처럼 원 이벤트를 가리키는 항목 </summary>
        public SessionEventEntry(long eventSeq, string eventId, int eventKind, string participantId,
            string definitionId, string bindingKind, long amount, int endReason, string settlementOperationId,
            SettlementGrant[] grants, string payloadJson, string revertsEventId)
            : this(eventSeq, eventId, eventKind, participantId, definitionId, bindingKind, amount, endReason,
                settlementOperationId, grants, payloadJson)
        {
            _revertsEventId = revertsEventId;
        }

        private SessionEventEntry() { }
    }
}
