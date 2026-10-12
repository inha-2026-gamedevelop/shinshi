// System
using System;
using System.Collections.Generic;

namespace Shinshi.Combat
{
    /// <summary> 서버 전체와 수신자별 숫자 전송에 고정 상한을 적용한다. 초과해도 피해 판정은 유지한다 </summary>
    public sealed class DamageFeedbackBudget
    {
        /****************************************
        *                Fields
        ****************************************/

        // 피해 숫자의 표시 계약값이며 공격 속도나 피해량을 제한하지 않는다
        public const int SERVER_PER_SECOND = 256;
        public const int OWNER_PER_SECOND = 64;
        private const double WINDOW_SECONDS = 1.0;

        private readonly Dictionary<int, int> _owners;
        private readonly int _ownerCapacity;
        private double _windowEnd = double.NegativeInfinity;
        private int _count;
        private uint _sequence;

        /****************************************
        *                Methods
        ****************************************/

        public DamageFeedbackBudget(int ownerCapacity)
        {
            if (ownerCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ownerCapacity));
            }
            _ownerCapacity = ownerCapacity;
            _owners = new Dictionary<int, int>(ownerCapacity);
        }

        // 재접속으로 전투 번호가 바뀌어도 같은 참가자의 이번 표시 예산은 이어진다
        public bool TryTake(int participantId, double serverTime, out uint sequence)
        {
            sequence = 0;
            if ((participantId <= 0) || (!DamageFeedbackRules.IsFinite(serverTime)) || (_sequence == uint.MaxValue))
            {
                return false;
            }
            if (serverTime >= _windowEnd)
            {
                _windowEnd = serverTime + WINDOW_SECONDS;
                _count = 0;
                _owners.Clear();
            }
            bool known = _owners.TryGetValue(participantId, out int ownerCount);
            if ((_count >= SERVER_PER_SECOND) || (ownerCount >= OWNER_PER_SECOND)
                || ((!known) && (_owners.Count >= _ownerCapacity)))
            {
                return false;
            }
            ++_count;
            _owners[participantId] = ownerCount + 1;
            sequence = ++_sequence;
            return true;
        }
    }
}
