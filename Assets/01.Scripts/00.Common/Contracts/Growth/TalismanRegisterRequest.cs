// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 부적 효과 신규 등록 시도. 응답은 GrowthOperationResponse이고 outcome이 성공, 실패를 알린다 </summary>
    [DataContract]
    public sealed class TalismanRegisterRequest
    {
        /****************************************
        *                Fields
        ****************************************/

        // 응답을 잃으면 같은 ID로 다시 보낸다. 재전송은 rollBasisPoints가 달라도 처음 결과를 받는다
        [DataMember(Name = "operationId")]      private string _operationId;
        // 마을이 확인한 CharacterGrowthSnapshot.loadoutRevision, 다르면 STALE_REVISION
        [DataMember(Name = "expectedRevision")] private long   _expectedRevision;
        [DataMember(Name = "effectId")]         private string _effectId;
        // 마을 서버가 뽑은 0에서 9999 사이 값, 요청 해시에서 빠진다
        [DataMember(Name = "rollBasisPoints")]  private int    _rollBasisPoints;

        /****************************************
        *              Properties
        ****************************************/

        public string OperationId      => _operationId;
        public long   ExpectedRevision => _expectedRevision;
        public string EffectId         => _effectId;
        public int    RollBasisPoints  => _rollBasisPoints;

        /****************************************
        *                Methods
        ****************************************/

        public TalismanRegisterRequest(string operationId, long expectedRevision, string effectId, int rollBasisPoints)
        {
            _operationId      = operationId;
            _expectedRevision = expectedRevision;
            _effectId         = effectId;
            _rollBasisPoints  = rollBasisPoints;
        }

        private TalismanRegisterRequest() { }
    }
}
