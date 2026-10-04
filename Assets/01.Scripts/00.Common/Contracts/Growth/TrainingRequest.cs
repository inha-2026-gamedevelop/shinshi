// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 수련 한 항목(ContractCodes와 같은 ETrainingStat 값)에 steps단계를 투자한다 </summary>
    [DataContract]
    public sealed class TrainingRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]      private string _operationId;
        [DataMember(Name = "expectedRevision")] private long   _expectedRevision;
        [DataMember(Name = "stat")]             private int    _stat;
        [DataMember(Name = "steps")]            private int    _steps;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId      => _operationId;
        public long   ExpectedRevision => _expectedRevision;
        public int    Stat             => _stat;
        public int    Steps            => _steps;

        /****************************************
        *                Methods
        ****************************************/

        public TrainingRequest(string operationId, long expectedRevision, int stat, int steps)
        {
            _operationId      = operationId;
            _expectedRevision = expectedRevision;
            _stat             = stat;
            _steps            = steps;
        }

        private TrainingRequest() { }
    }
}
