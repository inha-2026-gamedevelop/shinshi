// System
using System.Collections.Generic;

namespace Shinshi.AI
{
    // 취소 사건에 시전 번호가 없던 때의 대체 경로이자, 한 시전의 남은 장판(빔, 별) 수를 아는 데 쓴다. 시작 사건에는 둘 다 있으므로 그때 짝을 기억해 둔다
    // 한 시전이 장판을 여럿 예고하고 취소는 장판마다 오므로, 그 시전의 마지막 장판이 취소될 때만 시전이 끝난 것으로 본다
    /// <summary> 추적 장판 번호와 시전 번호의 짝을 기억해 장판 취소를 시전 종료로 바꾼다 </summary>
    public sealed class BossTrackingCastMap
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Dictionary<int, int> _castByArea = new Dictionary<int, int>();

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _castByArea.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 장판 예고가 시작됐다, 같은 장판 번호가 다시 오면 짝을 덮어쓴다 </summary>
        public void Add(int areaId, int castId)
        {
            _castByArea[areaId] = castId;
        }

        /// <summary> 장판이 활성됐거나 끝나 더는 취소 사건을 받지 않는다 </summary>
        public void Remove(int areaId)
        {
            _castByArea.Remove(areaId);
        }

        /// <summary> 장판이 취소됐다. 짝을 모르는 장판이면 거짓이고, 그 시전의 남은 장판이 없을 때만 isLastOfCast가 참이다 </summary>
        public bool TryCancel(int areaId, out int castId, out bool isLastOfCast)
        {
            isLastOfCast = false;

            if (!_castByArea.TryGetValue(areaId, out castId))
            {
                return false;
            }

            _castByArea.Remove(areaId);

            isLastOfCast = true;
            foreach (KeyValuePair<int, int> pair in _castByArea)
            {
                if (pair.Value == castId)
                {
                    isLastOfCast = false;
                    break;
                }
            }

            return true;
        }

        /// <summary> 이 시전의 짝이 하나라도 남아 있다 </summary>
        public bool HasCast(int castId)
        {
            foreach (KeyValuePair<int, int> pair in _castByArea)
            {
                if (pair.Value == castId)
                {
                    return true;
                }
            }

            return false;
        }

        public void Clear()
        {
            _castByArea.Clear();
        }
    }
}
