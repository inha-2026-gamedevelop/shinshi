// System
using System;

namespace Shinshi.Combat
{
    /// <summary> 서버 틱 하나에서 계산할 수 있는 경로 수, 넘는 요청은 다음 틱으로 미룬다 </summary>
    public sealed class PathRequestBudget
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int _perTick;

        private int _remaining = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int PerTick   => _perTick;
        public int Remaining => _remaining;

        /****************************************
        *                Methods
        ****************************************/

        public PathRequestBudget(int perTick)
        {
            if (perTick <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(perTick), "틱당 경로 수는 1 이상이다");
            }

            _perTick = perTick;
        }

        /// <summary> 서버 틱 시작에 한 번 부른다 </summary>
        public void BeginTick()
        {
            _remaining = _perTick;
        }

        public bool TryConsume()
        {
            if (_remaining <= 0)
            {
                return false;
            }

            --_remaining;
            return true;
        }
    }
}
