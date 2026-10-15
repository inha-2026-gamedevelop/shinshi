// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 경로의 장비 옵션 두 줄을 새로 뽑아 바꾼다 </summary>
    [DataContract]
    public sealed class EquipmentRerollOptionsRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]      private string _operationId;
        [DataMember(Name = "expectedRevision")] private long   _expectedRevision;
        // 마을 서버가 뽑은 0에서 9999 사이 값 네 개, 순서는 EquipmentOptionRules의 ROLL_* 자리다. 요청 해시에서 빠진다
        [DataMember(Name = "rolls")]            private int[]  _rolls;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId      => _operationId;
        public long   ExpectedRevision => _expectedRevision;
        public int[]  Rolls            => _rolls;

        /****************************************
        *                Methods
        ****************************************/

        public EquipmentRerollOptionsRequest(string operationId, long expectedRevision, int[] rolls)
        {
            _operationId      = operationId;
            _expectedRevision = expectedRevision;
            _rolls            = rolls;
        }

        private EquipmentRerollOptionsRequest() { }
    }
}
