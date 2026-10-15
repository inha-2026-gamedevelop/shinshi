// System
using System.Collections.Generic;

namespace Shinshi.AI
{
    /// <summary> 끝난 번호를 최근 순서로 일정 개수만 기억한다, 늦게 온 시작 알림이 끝난 표현을 되살리지 못하게 한다 </summary>
    public sealed class BossEndedIdHistory
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly HashSet<int> _ids   = new HashSet<int>();
        private readonly Queue<int>   _order = new Queue<int>();
        private readonly int          _capacity;

        /****************************************
        *                Methods
        ****************************************/

        public BossEndedIdHistory(int capacity)
        {
            _capacity = capacity;
        }

        public bool Contains(int id) { return _ids.Contains(id); }

        public void Remember(int id)
        {
            if (!_ids.Add(id))
            {
                return;
            }

            _order.Enqueue(id);
            while (_order.Count > _capacity)
            {
                _ids.Remove(_order.Dequeue());
            }
        }

        public void Clear()
        {
            _ids.Clear();
            _order.Clear();
        }
    }
}
