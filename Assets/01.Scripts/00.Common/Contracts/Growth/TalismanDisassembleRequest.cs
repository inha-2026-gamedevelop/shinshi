// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 같은 직업의 미등록 부적 카드를 분해해 캐릭터 정기로 바꾼다 </summary>
    [DataContract]
    public sealed class TalismanDisassembleRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "operationId")]      private string _operationId;
        [DataMember(Name = "expectedRevision")] private long   _expectedRevision;
        [DataMember(Name = "effectId")]         private string _effectId;
        [DataMember(Name = "quantity")]         private int    _quantity;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId      => _operationId;
        public long   ExpectedRevision => _expectedRevision;
        public string EffectId         => _effectId;
        public int    Quantity         => _quantity;

        /****************************************
        *                Methods
        ****************************************/

        public TalismanDisassembleRequest(string operationId, long expectedRevision, string effectId, int quantity)
        {
            _operationId      = operationId;
            _expectedRevision = expectedRevision;
            _effectId         = effectId;
            _quantity         = quantity;
        }

        private TalismanDisassembleRequest() { }
    }
}
