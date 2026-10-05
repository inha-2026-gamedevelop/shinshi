// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 총 투자를 유지한 채 다섯 항목 배분을 바꾼다. 배분이 같으면 비용 없이 확정한다 </summary>
    [DataContract]
    public sealed class TrainingRespecRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]      private string _operationId;
        [DataMember(Name = "expectedRevision")] private long   _expectedRevision;
        // ETrainingStat 순서의 다섯 값
        [DataMember(Name = "allocation")]       private int[]  _allocation;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId      => _operationId;
        public long   ExpectedRevision => _expectedRevision;
        public int[]  Allocation       => _allocation;

        /****************************************
        *                Methods
        ****************************************/

        public TrainingRespecRequest(string operationId, long expectedRevision, int[] allocation)
        {
            _operationId      = operationId;
            _expectedRevision = expectedRevision;
            _allocation       = allocation;
        }

        private TrainingRespecRequest() { }
    }
}
