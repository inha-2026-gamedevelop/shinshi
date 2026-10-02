// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 예측으로 보여 준 위치를 시간과 함께 남기는 고정 크기 기록, 서버 확정 위치와 같은 시점끼리 비교할 때 쓴다 </summary>
    public sealed class PredictionHistory
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MIN_CAPACITY = 2;

        private readonly double[]  _times;
        private readonly Vector3[] _positions;
        private readonly double    _minInterval;

        private int _start = 0;
        private int _count = 0;

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _count;

        /****************************************
        *                Methods
        ****************************************/

        public PredictionHistory(int capacity, double minInterval)
        {
            if ((capacity < MIN_CAPACITY) || (minInterval < 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "기록은 2칸 이상이고 간격은 0 이상이다");
            }

            _times       = new double[capacity];
            _positions   = new Vector3[capacity];
            _minInterval = minInterval;
        }

        public void Clear()
        {
            _start = 0;
            _count = 0;
        }

        /// <summary> 이번 프레임 위치를 남긴다, 간격보다 촘촘하면 마지막 칸을 덮어 프레임률과 무관하게 되돌아볼 시간을 지킨다 </summary>
        public void Record(double time, Vector3 position)
        {
            if ((_count > 0) && (time < _times[IndexOf(_count - 1)]))
            {
                // 시계가 뒤로 가면 이전 기록과 비교할 수 없다
                Clear();
            }

            // 마지막 칸이 아니라 그 앞 칸과 비교한다. 마지막 칸 시각은 덮을 때마다 앞으로 가므로 그것과 비교하면 영영 새 칸을 열지 못한다
            if ((_count > 1) && ((time - _times[IndexOf(_count - 2)]) < _minInterval))
            {
                int last = IndexOf(_count - 1);
                _times[last]     = time;
                _positions[last] = position;
                return;
            }

            if (_count == _times.Length)
            {
                _start = (_start + 1) % _times.Length;
                --_count;
            }

            int index = IndexOf(_count);
            _times[index]     = time;
            _positions[index] = position;
            ++_count;
        }

        /// <summary> 그 시각에 보여 준 위치, 기록보다 오래된 시각이면 거짓이고 가장 최근보다 뒤면 최근 위치다 </summary>
        public bool TrySample(double time, out Vector3 position)
        {
            position = Vector3.zero;
            if (_count == 0)
            {
                return false;
            }

            int oldest = IndexOf(0);
            if (time < _times[oldest])
            {
                return false;
            }

            int newest = IndexOf(_count - 1);
            if (time >= _times[newest])
            {
                position = _positions[newest];
                return true;
            }

            // 비교 시점은 대개 최근이라 뒤에서부터 찾는다
            for (int i = _count - 1; i > 0; --i)
            {
                int before = IndexOf(i - 1);
                if (time < _times[before])
                {
                    continue;
                }

                int    after = IndexOf(i);
                double span  = _times[after] - _times[before];
                float  t     = (span > 0.0) ? (float)((time - _times[before]) / span) : 1f;
                position = Vector3.Lerp(_positions[before], _positions[after], t);
                return true;
            }

            position = _positions[oldest];
            return true;
        }

        /// <summary> 보정으로 옮긴 만큼 기록 전체를 옮긴다, 같은 오차를 다음 비교에서 다시 세지 않게 한다 </summary>
        public void Shift(Vector3 delta)
        {
            for (int i = 0; i < _count; ++i)
            {
                _positions[IndexOf(i)] += delta;
            }
        }

        private int IndexOf(int offset)
        {
            return (_start + offset) % _times.Length;
        }
    }
}
