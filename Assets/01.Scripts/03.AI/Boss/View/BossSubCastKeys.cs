// System
using System.Collections.Generic;

namespace Shinshi.AI
{
    // 한 시전이 예고를 여러 개 동시에 또는 차례로 그릴 때(콤보의 타, 순차 타격의 타, 연속 돌진의 돌진) 예고 표현은 예고마다 따로 번호가 있어야 한다.
    // 서버 시전 번호에 오프셋을 더하면 번호가 커질 때 다른 시전과 겹칠 수 있어, 클라이언트 안에서만 쓰는 음수 번호를 발급한다
    // 서버가 발급하는 번호는 항상 양수라 이 번호와 겹치지 않는다. 같은 묶음 번호(GroupId)에 시전 번호를 달아 서로 지우지 않게 하는 것은 부르는 쪽이 한다
    // 여러 뷰가 같은 예고 표현 진입점을 함께 쓰므로 카운터는 인스턴스가 아니라 프로세스가 공유한다. 뷰마다 -1부터 세면 서로 다른 뷰의 예고 번호가 겹쳐 뒤의 예고가 거절된다
    /// <summary> (시전 번호, 순번)마다 예고 표현용 고유 번호를 발급하고 시전 단위로 잊는다 </summary>
    public sealed class BossSubCastKeys
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int FIRST_KEY = -1;

        private readonly Dictionary<long, int> _keys = new Dictionary<long, int>();
        private readonly List<long>            _removals = new List<long>();

        private static int _nextKey = FIRST_KEY;

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _keys.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 번호를 돌려준다, 처음 부르면 새로 발급하고 같은 (시전, 순번)은 같은 번호다 </summary>
        public int Get(int castId, int index)
        {
            long id = Compose(castId, index);
            if (!_keys.TryGetValue(id, out int key))
            {
                key = _nextKey;
                --_nextKey;
                _keys.Add(id, key);
            }

            return key;
        }

        /// <summary> 이미 발급한 번호만 찾는다 </summary>
        public bool TryGet(int castId, int index, out int key)
        {
            return _keys.TryGetValue(Compose(castId, index), out key);
        }

        /// <summary> 한 시전의 번호를 모두 잊는다 </summary>
        public void Forget(int castId)
        {
            _removals.Clear();
            foreach (KeyValuePair<long, int> pair in _keys)
            {
                if ((int)(pair.Key >> 32) == castId)
                {
                    _removals.Add(pair.Key);
                }
            }

            for (int i = 0; i < _removals.Count; ++i)
            {
                _keys.Remove(_removals[i]);
            }
        }

        public void Clear()
        {
            _keys.Clear();
        }

        private static long Compose(int castId, int index)
        {
            return ((long)castId << 32) | (uint)index;
        }
    }
}
