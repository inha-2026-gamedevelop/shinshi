// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 등록한 부적 효과를 한 레벨 확정 강화한다 </summary>
    [DataContract]
    public sealed class TalismanUpgradeRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]      private string _operationId;
        [DataMember(Name = "expectedRevision")] private long   _expectedRevision;
        [DataMember(Name = "effectId")]         private string _effectId;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId      => _operationId;
        public long   ExpectedRevision => _expectedRevision;
        public string EffectId         => _effectId;

        /****************************************
        *                Methods
        ****************************************/

        public TalismanUpgradeRequest(string operationId, long expectedRevision, string effectId)
        {
            _operationId      = operationId;
            _expectedRevision = expectedRevision;
            _effectId         = effectId;
        }

        private TalismanUpgradeRequest() { }
    }
}
