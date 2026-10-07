// System
using System;
using System.Collections.Generic;

namespace Shinshi.Network
{
    // 요청은 수가 적고 수명이 짧아 고정 배열을 훑는다. 같은 사람에게 다시 요청하면 만료만 늘린다(거래 ID는 그대로).
    // 지운 칸은 마지막 칸으로 메운다. 메인 스레드에서만 부른다
    /// <summary> 마을 1:1 거래 요청 표, 받는 사람과 거래 ID로 찾고 만료가 지나면 받지 않는다 </summary>
    public sealed class TradeRequestTable
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly TradeRequestEntry[] _entries;
        private readonly double              _lifetimeSeconds;
        private readonly int                 _maxPerRequester;

        private int _count = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _count;

        /****************************************
        *                Methods
        ****************************************/

        public TradeRequestTable(int capacity, double lifetimeSeconds, int maxPerRequester)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (maxPerRequester <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPerRequester));
            }

            if (lifetimeSeconds <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(lifetimeSeconds));
            }

            _entries         = new TradeRequestEntry[capacity];
            _lifetimeSeconds = lifetimeSeconds;
            _maxPerRequester = maxPerRequester;
        }

        /// <summary> 요청을 넣는다. 같은 쌍이 있으면 만료만 늘리고 그 거래 ID를 돌려준다. 표가 가득 찼거나 요청자의 요청이 상한이면 거짓 </summary>
        public bool Add(int requesterId, int targetId, double now, out Guid tradeId)
        {
            int index = IndexOfPair(requesterId, targetId);
            if (index >= 0)
            {
                tradeId = _entries[index].TradeId;
                _entries[index] = new TradeRequestEntry(tradeId, requesterId, targetId, now + _lifetimeSeconds);
                return true;
            }

            tradeId = Guid.Empty;
            if ((_count == _entries.Length) || (CountByRequester(requesterId) >= _maxPerRequester))
            {
                return false;
            }

            tradeId = Guid.NewGuid();
            _entries[_count] = new TradeRequestEntry(tradeId, requesterId, targetId, now + _lifetimeSeconds);
            ++_count;
            return true;
        }

        /// <summary> 같은 요청자와 받는 사람의 요청이 있으면 찾는다, 만료와 상관없다 </summary>
        public bool TryFindPair(int requesterId, int targetId, out TradeRequestEntry entry)
        {
            int index = IndexOfPair(requesterId, targetId);
            if (index >= 0)
            {
                entry = _entries[index];
                return true;
            }

            entry = default;
            return false;
        }

        /// <summary> 받는 사람과 거래 ID로 찾는다, 만료와 상관없이 찾고 만료 여부는 호출자가 본다 </summary>
        public bool TryGet(Guid tradeId, int targetId, out TradeRequestEntry entry)
        {
            for (int i = 0; i < _count; ++i)
            {
                if ((_entries[i].TradeId == tradeId) && (_entries[i].TargetId == targetId))
                {
                    entry = _entries[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        public bool Remove(Guid tradeId)
        {
            for (int i = 0; i < _count; ++i)
            {
                if (_entries[i].TradeId == tradeId)
                {
                    RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary> 참가자가 요청자이거나 받는 사람인 요청을 모두 지우고 지운 요청을 removed에 담는다 </summary>
        public void RemoveAll(int participantId, List<TradeRequestEntry> removed)
        {
            for (int i = _count - 1; i >= 0; --i)
            {
                if ((_entries[i].RequesterId == participantId) || (_entries[i].TargetId == participantId))
                {
                    removed?.Add(_entries[i]);
                    RemoveAt(i);
                }
            }
        }

        /// <summary> 만료된 요청을 지우고 expired에 담는다, 매 틱 부른다 </summary>
        public void Prune(double now, List<TradeRequestEntry> expired)
        {
            for (int i = _count - 1; i >= 0; --i)
            {
                if (now >= _entries[i].ExpiresAt)
                {
                    expired?.Add(_entries[i]);
                    RemoveAt(i);
                }
            }
        }

        private int CountByRequester(int requesterId)
        {
            int count = 0;
            for (int i = 0; i < _count; ++i)
            {
                if (_entries[i].RequesterId == requesterId)
                {
                    ++count;
                }
            }

            return count;
        }

        private int IndexOfPair(int requesterId, int targetId)
        {
            for (int i = 0; i < _count; ++i)
            {
                if ((_entries[i].RequesterId == requesterId) && (_entries[i].TargetId == targetId))
                {
                    return i;
                }
            }

            return -1;
        }

        private void RemoveAt(int index)
        {
            int last = _count - 1;
            _entries[index] = _entries[last];
            _entries[last]  = default;
            --_count;
        }
    }
}
