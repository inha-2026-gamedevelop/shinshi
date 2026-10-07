// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 내가 거래 창에 올리는 제안의 편집본. 서버 상태가 오면 그 값으로 맞추고, 편집은 서버로 보낼 배열을 만든다 </summary>
    public sealed class TradeOfferDraft
    {
        // 칸 수와 수량 상한은 표시용 사전 검사다. 서버가 같은 규칙으로 다시 본다

        /****************************************
        *                Fields
        ****************************************/

        private readonly List<TradeOfferLine> _lines = new List<TradeOfferLine>(TradeCodes.TRADE_MAX_ITEMS);

        /****************************************
        *              Properties
        ****************************************/

        public int  Count    => _lines.Count;
        public long Currency { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        public TradeOfferLine LineAt(int index)
        {
            return _lines[index];
        }

        /// <summary> 서버가 알려 준 제안으로 맞춘다 </summary>
        public void SetFrom(TradeOfferLine[] lines, long currency)
        {
            _lines.Clear();
            if (lines != null)
            {
                for (int i = 0; (i < lines.Length) && (i < TradeCodes.TRADE_MAX_ITEMS); ++i)
                {
                    _lines.Add(lines[i]);
                }
            }

            Currency = currency;
        }

        public void Clear()
        {
            _lines.Clear();
            Currency = 0;
        }

        /// <summary> 이 품목을 아직 더 올릴 수 있는 수량. 장비는 이미 올렸으면 0이고 아니면 1 </summary>
        public int RemainingFor(in SellableItem item)
        {
            int used = 0;
            for (int i = 0; i < _lines.Count; ++i)
            {
                if (IsSame(_lines[i], in item))
                {
                    used += _lines[i].Quantity;
                }
            }

            return Math.Max(0, item.MaxQuantity - used);
        }

        /// <summary> 품목을 올린다. 같은 수량형 품목은 수량만 늘린다. 더 올릴 수 없거나 칸이 가득 차면 거짓 </summary>
        public bool TryAdd(in SellableItem item, int quantity)
        {
            if ((quantity < 1) || (quantity > RemainingFor(in item)))
            {
                return false;
            }

            for (int i = 0; i < _lines.Count; ++i)
            {
                if ((!item.IsEquipment) && (IsSame(_lines[i], in item)))
                {
                    _lines[i] = new TradeOfferLine(item.DefinitionId, null, _lines[i].Quantity + quantity);
                    return true;
                }
            }

            if (_lines.Count >= TradeCodes.TRADE_MAX_ITEMS)
            {
                return false;
            }

            _lines.Add(new TradeOfferLine(item.DefinitionId, item.ItemId, quantity));
            return true;
        }

        public bool RemoveAt(int index)
        {
            if ((index < 0) || (index >= _lines.Count))
            {
                return false;
            }

            _lines.RemoveAt(index);
            return true;
        }

        public void SetCurrency(long currency)
        {
            Currency = Math.Max(0L, currency);
        }

        public TradeOfferLine[] ToArray()
        {
            return _lines.ToArray();
        }

        private static bool IsSame(TradeOfferLine line, in SellableItem item)
        {
            return item.IsEquipment
                ? (line.IsEquipment) && (string.Equals(line.ItemId, item.ItemId, StringComparison.OrdinalIgnoreCase))
                : (!line.IsEquipment) && (string.Equals(line.DefinitionId, item.DefinitionId, StringComparison.Ordinal));
        }
    }
}
