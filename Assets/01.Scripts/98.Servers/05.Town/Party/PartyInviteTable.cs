// System
using System;

namespace Shinshi.Network
{
    // 초대는 수가 적고 수명이 짧아 고정 배열을 훑는다. 같은 사람을 같은 방에 다시 초대하면 만료만 늘린다.
    // 지운 칸은 마지막 칸으로 메운다. 메인 스레드에서만 부른다
    /// <summary> 마을 직접 초대 목록, 초대받은 참가자와 방으로 찾고 만료가 지나면 받지 않는다 </summary>
    public sealed class PartyInviteTable
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Entry[] _entries;
        private readonly double  _lifetimeSeconds;
        private readonly int     _maxPerInviter;

        private int _count = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _count;

        /****************************************
        *                Methods
        ****************************************/

        public PartyInviteTable(int capacity, double lifetimeSeconds, int maxPerInviter)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (maxPerInviter <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPerInviter));
            }

            if (lifetimeSeconds <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(lifetimeSeconds));
            }

            _entries         = new Entry[capacity];
            _lifetimeSeconds = lifetimeSeconds;
            _maxPerInviter   = maxPerInviter;
        }

        /// <summary> 초대를 넣는다, 이미 있으면 만료를 다시 잡는다. 표가 가득 찼거나 이 초대자의 초대가 상한이면 거짓 </summary>
        public bool Add(int inviteeId, int roomId, int inviterId, double now)
        {
            int index = IndexOf(inviteeId, roomId);
            if (index >= 0)
            {
                _entries[index] = new Entry(inviteeId, roomId, inviterId, now + _lifetimeSeconds);
                return true;
            }

            if ((_count == _entries.Length) || (CountByInviter(inviterId) >= _maxPerInviter))
            {
                return false;
            }

            _entries[_count] = new Entry(inviteeId, roomId, inviterId, now + _lifetimeSeconds);
            ++_count;
            return true;
        }

        /// <summary> 만료 전 초대를 쓰고 지운다, 없거나 만료면 거짓(만료된 초대도 지운다) </summary>
        public bool TryConsume(int inviteeId, int roomId, double now)
        {
            int index = IndexOf(inviteeId, roomId);
            if (index < 0)
            {
                return false;
            }

            bool valid = now < _entries[index].ExpiresAt;
            RemoveAt(index);
            return valid;
        }

        /// <summary> 남은 초, 초대가 없으면 0 </summary>
        public double RemainingSeconds(int inviteeId, int roomId, double now)
        {
            int index = IndexOf(inviteeId, roomId);
            return (index < 0) ? 0.0 : Math.Max(0.0, _entries[index].ExpiresAt - now);
        }

        public bool Remove(int inviteeId, int roomId)
        {
            int index = IndexOf(inviteeId, roomId);
            if (index < 0)
            {
                return false;
            }

            RemoveAt(index);
            return true;
        }

        /// <summary> 연결이 끊긴 참가자나 닫힌 방의 초대를 지운다. participantId나 roomId가 0이면 그 조건은 보지 않는다 </summary>
        public void RemoveAll(int participantId, int roomId)
        {
            for (int i = _count - 1; i >= 0; --i)
            {
                ref Entry entry = ref _entries[i];
                bool byParticipant = (participantId != 0)
                    && ((entry.InviteeId == participantId) || (entry.InviterId == participantId));
                bool byRoom = (roomId != 0) && (entry.RoomId == roomId);
                if ((byParticipant) || (byRoom))
                {
                    RemoveAt(i);
                }
            }
        }

        /// <summary> 만료된 초대를 지운다, 매 틱 부른다 </summary>
        public void Prune(double now)
        {
            for (int i = _count - 1; i >= 0; --i)
            {
                if (now >= _entries[i].ExpiresAt)
                {
                    RemoveAt(i);
                }
            }
        }

        // 만료가 지났어도 Prune 전이면 센다. 틱마다 지우므로 오래 남지 않는다
        private int CountByInviter(int inviterId)
        {
            int count = 0;
            for (int i = 0; i < _count; ++i)
            {
                if (_entries[i].InviterId == inviterId)
                {
                    ++count;
                }
            }

            return count;
        }

        private int IndexOf(int inviteeId, int roomId)
        {
            for (int i = 0; i < _count; ++i)
            {
                if ((_entries[i].InviteeId == inviteeId) && (_entries[i].RoomId == roomId))
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

        /****************************************
        *               Records
        ****************************************/

        // 배열 안에서 ref로 읽는 값 묶음이라 클래스 밖으로 내보내지 않는다
        private struct Entry
        {
            public int    InviteeId;
            public int    RoomId;
            public int    InviterId;
            public double ExpiresAt;

            public Entry(int inviteeId, int roomId, int inviterId, double expiresAt)
            {
                InviteeId = inviteeId;
                RoomId    = roomId;
                InviterId = inviterId;
                ExpiresAt = expiresAt;
            }
        }
    }
}
