// System
using System;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    /// <summary> 내 계정의 행동력, 약초, 물약 재고, 조회나 채집, 제작 확정 뒤 소유자에게만 온다 </summary>
    public readonly struct LifeSnapshotMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly long            _actionValue;
        private readonly long            _actionMax;
        private readonly long            _nextRecoverAtUnixMs;
        private readonly string          _herbId;
        private readonly long            _herbQuantity;
        private readonly long            _herbCap;
        private readonly TownItemEntry[] _potions;

        /****************************************
        *              Properties
        ****************************************/

        public long            ActionValue         => _actionValue;
        public long            ActionMax           => _actionMax;

        // 다음 회복 시각(유닉스 ms, API 시각), 상한이면 0
        public long            NextRecoverAtUnixMs => _nextRecoverAtUnixMs;
        public string          HerbId              => _herbId;
        public long            HerbQuantity        => _herbQuantity;
        public long            HerbCap             => _herbCap;
        public TownItemEntry[] Potions             => _potions ?? Array.Empty<TownItemEntry>();

        /****************************************
        *                Methods
        ****************************************/

        public LifeSnapshotMessage(long actionValue, long actionMax, long nextRecoverAtUnixMs, string herbId, long herbQuantity,
            long herbCap, TownItemEntry[] potions)
        {
            _actionValue         = actionValue;
            _actionMax           = actionMax;
            _nextRecoverAtUnixMs = nextRecoverAtUnixMs;
            _herbId              = herbId;
            _herbQuantity        = herbQuantity;
            _herbCap             = herbCap;
            _potions             = potions;
        }
    }
}
