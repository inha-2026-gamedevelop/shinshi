namespace Shinshi.Combat
{
    /// <summary> 출처 정의 ID 하나가 입힌 피해량 </summary>
    public readonly struct CombatMeterSourceAmount
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _sourceId;
        private readonly float  _amount;

        /****************************************
        *              Properties
        ****************************************/

        public string SourceId => _sourceId;
        public float  Amount   => _amount;

        /****************************************
        *                Methods
        ****************************************/

        public CombatMeterSourceAmount(string sourceId, float amount)
        {
            _sourceId = sourceId;
            _amount   = amount;
        }
    }
}
