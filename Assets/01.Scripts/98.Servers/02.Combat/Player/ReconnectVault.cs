// System
using System;
using System.Collections.Generic;

namespace Shinshi.Combat
{
    /// <summary> 연결이 끊긴 참가자의 전투 상태를 재접속 유예 동안 참가자 번호로 보존한다 </summary>
    public sealed class ReconnectVault
    {
        // 보존 수가 수십 이하라 배열을 선형으로 훑는다. 사전을 쓰면 만료를 훑으며 지울 때 열거자 제약이 생긴다

        /****************************************
        *                Fields
        ****************************************/

        private readonly int[]                       _participantIds;
        private readonly ParticipantCombatSnapshot[] _snapshots;
        private readonly double[]                    _expireTimes;

        private int _count = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int Count    => _count;
        public int Capacity => _participantIds.Length;

        /****************************************
        *                Methods
        ****************************************/

        public ReconnectVault(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "재접속 보존 상한은 1 이상이다");
            }

            _participantIds = new int[capacity];
            _snapshots      = new ParticipantCombatSnapshot[capacity];
            _expireTimes    = new double[capacity];
        }

        /// <summary> 상태를 보존한다, 가득 차면 가장 먼저 만료될 기록을 지우고 그 참가자 번호를 돌려준다 </summary>
        public bool Store(int participantId, in ParticipantCombatSnapshot snapshot, double expireTime, out int evictedParticipantId)
        {
            evictedParticipantId = 0;
            bool evicted = false;

            int index = IndexOf(participantId);
            if (index < 0)
            {
                if (_count == _participantIds.Length)
                {
                    int earliest = IndexOfEarliestExpiry();
                    evictedParticipantId = _participantIds[earliest];
                    evicted              = true;
                    RemoveAt(earliest);
                }

                index = _count;
                ++_count;
            }

            _participantIds[index] = participantId;
            _snapshots[index]      = snapshot;
            _expireTimes[index]    = expireTime;
            return evicted;
        }

        /// <summary> 유예 안에 돌아온 참가자의 상태를 꺼내고 지운다, 만료 시각이 지난 기록은 꺼내지 않는다 </summary>
        public bool TryTake(int participantId, double serverTime, out ParticipantCombatSnapshot snapshot)
        {
            int index = IndexOf(participantId);
            if ((index < 0) || (serverTime >= _expireTimes[index]))
            {
                snapshot = default;
                return false;
            }

            snapshot = _snapshots[index];
            RemoveAt(index);
            return true;
        }

        public bool Contains(int participantId)
        {
            return IndexOf(participantId) >= 0;
        }

        /// <summary> 만료된 기록을 지우고 그 참가자 번호를 results 뒤에 붙인다, 서버 틱에서 부른다 </summary>
        public int CollectExpired(double serverTime, List<int> results)
        {
            int removed = 0;
            for (int i = _count - 1; i >= 0; --i)
            {
                if (serverTime < _expireTimes[i])
                {
                    continue;
                }

                results.Add(_participantIds[i]);
                RemoveAt(i);
                ++removed;
            }

            return removed;
        }

        private int IndexOf(int participantId)
        {
            for (int i = 0; i < _count; ++i)
            {
                if (_participantIds[i] == participantId)
                {
                    return i;
                }
            }

            return -1;
        }

        private int IndexOfEarliestExpiry()
        {
            int earliest = 0;
            for (int i = 1; i < _count; ++i)
            {
                if (_expireTimes[i] < _expireTimes[earliest])
                {
                    earliest = i;
                }
            }

            return earliest;
        }

        // 순서는 쓰지 않으므로 마지막 기록으로 메운다
        private void RemoveAt(int index)
        {
            int last = _count - 1;
            _participantIds[index] = _participantIds[last];
            _snapshots[index]      = _snapshots[last];
            _expireTimes[index]    = _expireTimes[last];
            _snapshots[last]       = default;
            --_count;
        }
    }
}
