// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 생활 재고 조회 결과. 행동력은 DB 시각으로 회복을 반영한 값이며 조회만으로 저장 값은 바뀌지 않는다 </summary>
    [DataContract]
    public sealed class LifeSnapshotResponse
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "actionValue")]         private long              _actionValue;
        [DataMember(Name = "actionMax")]           private long              _actionMax;

        // 다음 회복 시각(유닉스 ms, DB 시각). 상한이면 0이다
        [DataMember(Name = "nextRecoverAtUnixMs")] private long              _nextRecoverAtUnixMs;
        [DataMember(Name = "herb")]                private LifeHerbStock     _herb;

        // 물약 정의 순서(소형, 대형). 재고 행이 없으면 0으로 채운다
        [DataMember(Name = "potions")]             private LifePotionStock[] _potions;
        [DataMember(Name = "serverTimeUnixMs")]    private long              _serverTimeUnixMs;

        /****************************************
        *              Properties
        ****************************************/

        public long              ActionValue         => _actionValue;
        public long              ActionMax           => _actionMax;
        public long              NextRecoverAtUnixMs => _nextRecoverAtUnixMs;
        public LifeHerbStock     Herb                => _herb;
        public LifePotionStock[] Potions             => _potions;
        public long              ServerTimeUnixMs    => _serverTimeUnixMs;

        /****************************************
        *                Methods
        ****************************************/

        public LifeSnapshotResponse(long actionValue, long actionMax, long nextRecoverAtUnixMs, LifeHerbStock herb,
            LifePotionStock[] potions, long serverTimeUnixMs)
        {
            _actionValue         = actionValue;
            _actionMax           = actionMax;
            _nextRecoverAtUnixMs = nextRecoverAtUnixMs;
            _herb                = herb;
            _potions             = potions;
            _serverTimeUnixMs    = serverTimeUnixMs;
        }

        private LifeSnapshotResponse() { }
    }
}
