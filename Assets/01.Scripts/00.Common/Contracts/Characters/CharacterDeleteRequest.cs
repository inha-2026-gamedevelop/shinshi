// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 캐릭터 소프트 삭제 요청, 대상 characterId는 경로에 넣는다. 응답은 OperationResult이고 영수증 ID는 캐릭터 ID다 </summary>
    [DataContract]
    public sealed class CharacterDeleteRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 응답을 잃으면 같은 ID로 다시 보낸다. 같은 ID에 다른 본문은 IDEMPOTENCY_CONFLICT다
        [DataMember(Name = "operationId")]      private string _operationId;

        // 목록에서 받은 캐릭터 revision. 그 사이 캐릭터가 바뀌었으면 STALE_REVISION으로 거절한다
        [DataMember(Name = "expectedRevision")] private long   _expectedRevision;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId      => _operationId;
        public long   ExpectedRevision => _expectedRevision;

        /****************************************
        *                Methods
        ****************************************/

        public CharacterDeleteRequest(string operationId, long expectedRevision)
        {
            _operationId      = operationId;
            _expectedRevision = expectedRevision;
        }

        private CharacterDeleteRequest() { }
    }
}
