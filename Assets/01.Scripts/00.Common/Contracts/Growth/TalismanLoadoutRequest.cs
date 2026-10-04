// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 부적 조합 전체 교체, 비운 칸은 해제된다. 최대 12줄이고 배운 효과만 넣는다 </summary>
    [DataContract]
    public sealed class TalismanLoadoutRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]      private string                 _operationId;
        [DataMember(Name = "expectedRevision")] private long                   _expectedRevision;
        [DataMember(Name = "entries")]          private TalismanLoadoutEntry[] _entries;

        /****************************************
        *              Properties
        ****************************************/

        public string                 OperationId      => _operationId;
        public long                   ExpectedRevision => _expectedRevision;
        public TalismanLoadoutEntry[] Entries          => _entries;

        /****************************************
        *                Methods
        ****************************************/

        public TalismanLoadoutRequest(string operationId, long expectedRevision, TalismanLoadoutEntry[] entries)
        {
            _operationId      = operationId;
            _expectedRevision = expectedRevision;
            _entries          = entries;
        }

        private TalismanLoadoutRequest() { }
    }
}
