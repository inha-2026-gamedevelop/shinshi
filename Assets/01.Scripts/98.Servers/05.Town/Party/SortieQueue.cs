// System
using System;

namespace Shinshi.Network
{
    // 던전 풀이 빈(NO_CAPACITY) 방을 들어온 순서대로 들고 맨 앞 방만 다시 예약하게 한다. 마을 메모리에만 있고
    // 마을 프로세스가 내려가면 사라진다. 방에 바뀐 것이 있으면(revision 변경) 준비 상태가 달라졌으므로 줄에서 뺀다.
    // 방 수 상한만큼 고정 배열을 쓰고 앞에서 빼면 뒤를 당긴다. 대기 방은 많아야 수십이라 당기는 비용이 작다
    /// <summary> 마을 출정 대기열, 방 번호와 줄을 설 때의 방 revision을 순서대로 든다 </summary>
    public sealed class SortieQueue
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int[]  _roomIds;
        private readonly long[] _revisions;

        private int _count = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _count;

        /****************************************
        *                Methods
        ****************************************/

        public SortieQueue(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _roomIds   = new int[capacity];
            _revisions = new long[capacity];
        }

        /// <summary> 줄 끝에 넣는다, 이미 있으면 자리는 두고 revision만 바꾼다. 가득 차면 거짓 </summary>
        public bool Enqueue(int roomId, long revision)
        {
            int index = IndexOf(roomId);
            if (index >= 0)
            {
                _revisions[index] = revision;
                return true;
            }

            if (_count == _roomIds.Length)
            {
                return false;
            }

            _roomIds[_count]   = roomId;
            _revisions[_count] = revision;
            ++_count;
            return true;
        }

        public bool TryPeek(out int roomId, out long revision)
        {
            if (_count == 0)
            {
                roomId   = 0;
                revision = 0;
                return false;
            }

            roomId   = _roomIds[0];
            revision = _revisions[0];
            return true;
        }

        public bool Contains(int roomId)
        {
            return IndexOf(roomId) >= 0;
        }

        /// <summary> 순서 안의 위치(0이 맨 앞), 없으면 -1 </summary>
        public int PositionOf(int roomId)
        {
            return IndexOf(roomId);
        }

        public bool Remove(int roomId)
        {
            int index = IndexOf(roomId);
            if (index < 0)
            {
                return false;
            }

            for (int i = index; i < (_count - 1); ++i)
            {
                _roomIds[i]   = _roomIds[i + 1];
                _revisions[i] = _revisions[i + 1];
            }

            --_count;
            _roomIds[_count]   = 0;
            _revisions[_count] = 0;
            return true;
        }

        /// <summary> 방이 사라졌거나 줄을 선 뒤 바뀐 방을 뺀다, 뺀 수를 돌려준다 </summary>
        public int PruneStale(ISortieRoomLookup rooms)
        {
            int removed = 0;
            for (int i = _count - 1; i >= 0; --i)
            {
                if (!rooms.IsQueuedAt(_roomIds[i], _revisions[i]))
                {
                    Remove(_roomIds[i]);
                    ++removed;
                }
            }

            return removed;
        }

        private int IndexOf(int roomId)
        {
            for (int i = 0; i < _count; ++i)
            {
                if (_roomIds[i] == roomId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
