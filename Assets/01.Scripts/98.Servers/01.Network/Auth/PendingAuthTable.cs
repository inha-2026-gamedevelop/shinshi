// System
using System;
using System.Collections.Generic;

namespace Shinshi.Network
{
    /// <summary> 인증을 마치지 않은 연결의 마감 시각 표, 시간 초과와 거절 뒤 끊기를 정한다 </summary>
    public sealed class PendingAuthTable
    {
        // 인증기 Update가 매 프레임 훑으므로 사전 대신 배열과 제자리 수정(ref)만 쓴다. 상한이 작아 선형 탐색이 싸다.
        // 주소별 상한은 한 곳에서 연결만 열어 대기 자리를 모두 차지하지 못하게 한다. 주소 비교는 대기 연결 수만큼만 돈다.
        // 주소는 AuthAddressKey로 묶는다(IPv4 그대로, IPv4-mapped IPv6는 IPv4로, IPv6는 /64 접두사로)

        /****************************************
        *                Fields
        ****************************************/

        private readonly PendingAuthEntry[] _entries;
        private readonly int                _maxPerAddress;

        private int _count = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int Count         => _count;
        public int Capacity      => _entries.Length;
        public int MaxPerAddress => _maxPerAddress;

        /****************************************
        *                Methods
        ****************************************/

        public PendingAuthTable(int capacity, int maxPerAddress)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "인증 대기 상한은 1 이상이다");
            }

            if (maxPerAddress <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPerAddress), "주소별 인증 대기 상한은 1 이상이다");
            }

            _entries       = new PendingAuthEntry[capacity];
            _maxPerAddress = maxPerAddress;
        }

        /// <summary> 새 연결을 대기에 넣는다, 가득 찼거나 같은 주소의 대기가 상한이거나 이미 있으면 거짓 </summary>
        public bool TryAdd(int connectionId, string address, double deadline)
        {
            if ((_count == _entries.Length) || (IndexOf(connectionId) >= 0))
            {
                return false;
            }

            AuthAddressKey key = AuthAddressKey.FromAddress(address);
            if (CountByAddress(key) >= _maxPerAddress)
            {
                return false;
            }

            _entries[_count] = new PendingAuthEntry(connectionId, key, deadline);
            ++_count;
            return true;
        }

        /// <summary> 같은 주소 묶음에서 아직 인증을 마치지 않은 연결 수, 읽지 못하는 주소는 모두 한 묶음으로 센다 </summary>
        public int CountByAddress(string address)
        {
            return CountByAddress(AuthAddressKey.FromAddress(address));
        }

        public int CountByAddress(AuthAddressKey key)
        {
            if (key.IsNone)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < _count; ++i)
            {
                if (_entries[i].Address.Equals(key))
                {
                    ++count;
                }
            }

            return count;
        }

        public bool IsWaiting(int connectionId)
        {
            int index = IndexOf(connectionId);
            return (index >= 0) && (_entries[index].Stage == EPendingAuthStage.Waiting);
        }

        /// <summary> 요청을 받은 연결을 검증 대기로 바꾸고 검증 마감을 새로 준다, 요청을 기다리던 연결이 아니면 거짓 </summary>
        public bool TryBeginVerify(int connectionId, int requestId, double deadline)
        {
            int index = IndexOf(connectionId);
            if ((index < 0) || (_entries[index].Stage != EPendingAuthStage.Waiting))
            {
                return false;
            }

            _entries[index].BeginVerify(requestId, deadline);
            return true;
        }

        /// <summary> 검증 결과의 요청 번호로 연결을 찾는다, 이미 시간 초과로 닫혔으면 거짓 </summary>
        public bool TryFindVerifying(int requestId, out int connectionId)
        {
            for (int i = 0; i < _count; ++i)
            {
                ref PendingAuthEntry entry = ref _entries[i];
                if ((entry.Stage == EPendingAuthStage.Verifying) && (entry.RequestId == requestId))
                {
                    connectionId = entry.ConnectionId;
                    return true;
                }
            }

            connectionId = 0;
            return false;
        }

        /// <summary> 거절 응답을 보낸 연결을 끊기 대기로 바꾼다, 표에 없으면 넣고 자리가 없으면 거짓 </summary>
        public bool MarkClosing(int connectionId, double deadline)
        {
            int index = IndexOf(connectionId);
            if (index < 0)
            {
                if (_count == _entries.Length)
                {
                    return false;
                }

                index = _count;
                _entries[index] = new PendingAuthEntry(connectionId, AuthAddressKey.None, deadline);
                ++_count;
            }

            _entries[index].BeginClosing(deadline);
            return true;
        }

        public bool Remove(int connectionId)
        {
            int index = IndexOf(connectionId);
            if (index < 0)
            {
                return false;
            }

            RemoveAt(index);
            return true;
        }

        /// <summary> 마감이 지난 항목을 표에서 빼고 results 뒤에 붙인다, 단계로 시간 초과와 끊기 대기를 구분한다 </summary>
        public int CollectDue(double now, List<PendingAuthEntry> results)
        {
            int collected = 0;
            for (int i = _count - 1; i >= 0; --i)
            {
                if (now < _entries[i].Deadline)
                {
                    continue;
                }

                results.Add(_entries[i]);
                RemoveAt(i);
                ++collected;
            }

            return collected;
        }

        public void Clear()
        {
            _count = 0;
        }

        private int IndexOf(int connectionId)
        {
            for (int i = 0; i < _count; ++i)
            {
                if (_entries[i].ConnectionId == connectionId)
                {
                    return i;
                }
            }

            return -1;
        }

        // 순서는 쓰지 않으므로 마지막 항목으로 메운다
        private void RemoveAt(int index)
        {
            int last = _count - 1;
            _entries[index] = _entries[last];
            --_count;
        }
    }
}
