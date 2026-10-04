// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 성장 쓰기 결과와 현재 성장 상태. 재전송도 처음 outcome, itemId를 받는다 </summary>
    [DataContract]
    public sealed class GrowthOperationResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "result")]  private OperationResult         _result;
        // GrowthCodes.Outcome, 확률 판정이 없는 작업은 COMMITTED여도 NONE이다
        [DataMember(Name = "outcome")] private int                     _outcome;
        // 제작, 계승 대상, 강화, 착용 등 영향을 받은 장비, 없으면 null
        [DataMember(Name = "itemId")]  private string                  _itemId;
        // 캐릭터가 없으면 null
        [DataMember(Name = "growth")]  private CharacterGrowthSnapshot _growth;

        /****************************************
        *              Properties
        ****************************************/

        public OperationResult         Result  => _result;
        public int                     Outcome => _outcome;
        public string                  ItemId  => _itemId;
        public CharacterGrowthSnapshot Growth  => _growth;

        /****************************************
        *                Methods
        ****************************************/

        public GrowthOperationResponse(OperationResult result, int outcome, string itemId,
            CharacterGrowthSnapshot growth)
        {
            _result  = result;
            _outcome = outcome;
            _itemId  = itemId;
            _growth  = growth;
        }

        private GrowthOperationResponse() { }
    }
}
