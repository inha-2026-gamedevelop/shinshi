// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 원본 강화 단계를 같은 부위의 +0 미착용 대상에 옮기고 원본을 소모한다 </summary>
    [DataContract]
    public sealed class EquipmentInheritRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]      private string _operationId;
        [DataMember(Name = "expectedRevision")] private long   _expectedRevision;
        [DataMember(Name = "sourceItemId")]     private string _sourceItemId;
        [DataMember(Name = "targetItemId")]     private string _targetItemId;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId      => _operationId;
        public long   ExpectedRevision => _expectedRevision;
        public string SourceItemId     => _sourceItemId;
        public string TargetItemId     => _targetItemId;

        /****************************************
        *                Methods
        ****************************************/

        public EquipmentInheritRequest(string operationId, long expectedRevision, string sourceItemId,
            string targetItemId)
        {
            _operationId      = operationId;
            _expectedRevision = expectedRevision;
            _sourceItemId     = sourceItemId;
            _targetItemId     = targetItemId;
        }

        private EquipmentInheritRequest() { }
    }
}
