// System
using System;
using System.Collections.Generic;

namespace Shinshi.Combat
{
    /// <summary> 동작 번호와 대상 번호 조합의 적중 기록, 같은 동작이 같은 대상을 두 번 맞히지 못하게 한다 </summary>
    public sealed class HitLedger
    {
        // 기록은 들어온 순서대로 원형 버퍼에 두고 오래된 것부터 지운다. 동작마다 목록을 따로 만들면
        // 동작 수만큼 할당이 생기므로, 미리 잡은 HashSet과 배열만 쓴다

        /****************************************
        *                Fields
        ****************************************/

        private const int TARGET_BITS = 32;

        private readonly HashSet<long> _keys;
        private readonly long[]        _ringKeys;
        private readonly double[]      _ringTimes;
        private readonly double        _retentionSeconds;
        private readonly double        _forceEvictAgeSeconds;

        private int  _head                = 0;
        private int  _count               = 0;
        private long _fullCount           = 0;
        private long _forcedEvictionCount = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int Count    => _count;
        public int Capacity => _ringKeys.Length;

        // 가득 차서 기록을 거절한 누적 횟수, 주기 로그가 읽는다
        public long FullCount => _fullCount;

        // 가득 차서 보관 시간 전에 회수한 누적 기록 수
        public long ForcedEvictionCount => _forcedEvictionCount;

        /****************************************
        *                Methods
        ****************************************/

        // forceEvictAgeSeconds는 가장 긴 단일 동작 시간이다. 가득 차면 이보다 오래된 기록을 보관 시간 전이라도 회수한다
        public HitLedger(int capacity, double retentionSeconds, double forceEvictAgeSeconds)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "적중 기록 상한은 1 이상이다");
            }

            if (!(retentionSeconds > 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(retentionSeconds), "보관 시간은 0보다 커야 한다");
            }

            if ((!(forceEvictAgeSeconds > 0.0)) || (forceEvictAgeSeconds > retentionSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(forceEvictAgeSeconds), "강제 회수 시간은 0보다 크고 보관 시간 이하다");
            }

            _keys             = new HashSet<long>(capacity);
            _ringKeys         = new long[capacity];
            _ringTimes        = new double[capacity];
            _retentionSeconds     = retentionSeconds;
            _forceEvictAgeSeconds = forceEvictAgeSeconds;
        }

        /// <summary> 처음 맞힌 조합이면 기록한다 </summary>
        public EHitRecord TryRecord(int actionInstanceId, int targetId, double serverTime)
        {
            long key = MakeKey(actionInstanceId, targetId);
            if (_keys.Contains(key))
            {
                return EHitRecord.Duplicate;
            }

            if (_count == _ringKeys.Length)
            {
                Prune(serverTime);
                if (_count == _ringKeys.Length)
                {
                    EvictOlderThan(serverTime, _forceEvictAgeSeconds, true);
                }

                if (_count == _ringKeys.Length)
                {
                    ++_fullCount;
                    return EHitRecord.Full;
                }
            }

            int tail = (_head + _count) % _ringKeys.Length;
            _ringKeys[tail]  = key;
            _ringTimes[tail] = serverTime;
            _keys.Add(key);
            ++_count;
            return EHitRecord.Recorded;
        }

        public bool Contains(int actionInstanceId, int targetId)
        {
            return _keys.Contains(MakeKey(actionInstanceId, targetId));
        }

        /// <summary> 보관 시간이 지난 기록을 지운다, 서버 틱 시작에 한 번 부른다 </summary>
        public void Prune(double serverTime)
        {
            EvictOlderThan(serverTime, _retentionSeconds, false);
        }

        public void Clear()
        {
            _keys.Clear();
            _head  = 0;
            _count = 0;
        }

        // 원형 버퍼는 시각 순서라 머리부터 나이가 기준 미만인 첫 기록에서 멈춘다
        private void EvictOlderThan(double serverTime, double ageSeconds, bool isForced)
        {
            while (_count > 0)
            {
                if ((serverTime - _ringTimes[_head]) < ageSeconds)
                {
                    return;
                }

                _keys.Remove(_ringKeys[_head]);
                _head = (_head + 1) % _ringKeys.Length;
                --_count;
                if (isForced)
                {
                    ++_forcedEvictionCount;
                }
            }
        }

        private static long MakeKey(int actionInstanceId, int targetId)
        {
            // 대상 번호를 uint로 넓혀 부호 비트가 동작 번호 칸을 덮지 않게 한다
            return ((long)actionInstanceId << TARGET_BITS) | (uint)targetId;
        }
    }
}
