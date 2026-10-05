// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 경로의 장비를 한 단계 강화 시도한다 </summary>
    [DataContract]
    public sealed class EquipmentEnhanceRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]      private string _operationId;
        [DataMember(Name = "expectedRevision")] private long   _expectedRevision;
        // 마을 서버가 뽑은 0에서 9999 사이 값, 요청 해시에서 빠진다
        [DataMember(Name = "rollBasisPoints")]  private int    _rollBasisPoints;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId      => _operationId;
        public long   ExpectedRevision => _expectedRevision;
        public int    RollBasisPoints  => _rollBasisPoints;

        /****************************************
        *                Methods
        ****************************************/

        public EquipmentEnhanceRequest(string operationId, long expectedRevision, int rollBasisPoints)
        {
            _operationId      = operationId;
            _expectedRevision = expectedRevision;
            _rollBasisPoints  = rollBasisPoints;
        }

        private EquipmentEnhanceRequest() { }
    }
}
