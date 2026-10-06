// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 보스 재료로 +0 장비 하나를 만든다. 응답 itemId가 새 장비다 </summary>
    [DataContract]
    public sealed class EquipmentCraftRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]      private string _operationId;
        [DataMember(Name = "expectedRevision")] private long   _expectedRevision;
        [DataMember(Name = "definitionId")]     private string _definitionId;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId      => _operationId;
        public long   ExpectedRevision => _expectedRevision;
        public string DefinitionId     => _definitionId;

        /****************************************
        *                Methods
        ****************************************/

        public EquipmentCraftRequest(string operationId, long expectedRevision, string definitionId)
        {
            _operationId      = operationId;
            _expectedRevision = expectedRevision;
            _definitionId     = definitionId;
        }

        private EquipmentCraftRequest() { }
    }
}
