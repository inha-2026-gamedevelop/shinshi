// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 경로의 장비 하나에 대한 착용, 해제, 분해 </summary>
    [DataContract]
    public sealed class EquipmentActionRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]      private string _operationId;
        [DataMember(Name = "expectedRevision")] private long   _expectedRevision;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId      => _operationId;
        public long   ExpectedRevision => _expectedRevision;

        /****************************************
        *                Methods
        ****************************************/

        public EquipmentActionRequest(string operationId, long expectedRevision)
        {
            _operationId      = operationId;
            _expectedRevision = expectedRevision;
        }

        private EquipmentActionRequest() { }
    }
}
