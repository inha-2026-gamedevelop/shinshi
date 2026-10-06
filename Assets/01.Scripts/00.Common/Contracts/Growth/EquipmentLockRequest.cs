// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 장비 잠금을 켜거나 끈다. 잠긴 장비는 분해와 계승 원본으로 쓰지 못한다 </summary>
    [DataContract]
    public sealed class EquipmentLockRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]      private string _operationId;
        [DataMember(Name = "expectedRevision")] private long   _expectedRevision;
        [DataMember(Name = "locked")]           private bool   _locked;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId      => _operationId;
        public long   ExpectedRevision => _expectedRevision;
        public bool   Locked           => _locked;

        /****************************************
        *                Methods
        ****************************************/

        public EquipmentLockRequest(string operationId, long expectedRevision, bool locked)
        {
            _operationId      = operationId;
            _expectedRevision = expectedRevision;
            _locked           = locked;
        }

        private EquipmentLockRequest() { }
    }
}
