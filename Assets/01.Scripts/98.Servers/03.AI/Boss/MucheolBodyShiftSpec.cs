// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 경로는 보스 로컬 (오른쪽 x, 앞 z) 미터를 일정 간격으로 이어 적은 표이고, 키는 패턴 시각을 클립 시각에 묶는다.
    // 경로 값 사이와 키 사이는 모두 선형으로 잇는다. 표와 키는 만든 뒤 바꾸지 않는다
    /// <summary> 무철 패턴 하나의 몸 이동 경로와 클립 시간 키 </summary>
    public sealed class MucheolBodyShiftSpec
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int PAIR_SIZE = 2;

        /****************************************
        *                Fields
        ****************************************/

        private readonly float[]               _path;
        private readonly float                 _sampleStep;
        private readonly MucheolBodyShiftKey[] _keys;
        private readonly int                   _maxStrikeIndex;

        /****************************************
        *              Properties
        ****************************************/

        public int KeyCount => _keys.Length;

        /// <summary> 키가 기준으로 삼는 가장 큰 타 번호, 시전 시작만 쓰면 CAST_START다 </summary>
        public int MaxStrikeIndex => _maxStrikeIndex;

        /****************************************
        *                Methods
        ****************************************/

        // path는 (x, z) 쌍을 sampleStep 초마다 이어 적은 배열이다. keys는 패턴 시각 순서대로 적고 같은 시각의 두 키는 클립이 건너뛰는 자리다
        public MucheolBodyShiftSpec(float[] path, float sampleStep, params MucheolBodyShiftKey[] keys)
        {
            if ((path == null) || (path.Length < PAIR_SIZE) || ((path.Length % PAIR_SIZE) != 0))
            {
                throw new ArgumentException("몸 이동 경로는 (x, z) 쌍이 하나 이상이어야 한다", nameof(path));
            }

            if (!(sampleStep > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(sampleStep));
            }

            if ((keys == null) || (keys.Length == 0))
            {
                throw new ArgumentException("몸 이동 키가 하나 이상이어야 한다", nameof(keys));
            }

            _path           = path;
            _sampleStep     = sampleStep;
            _keys           = keys;
            _maxStrikeIndex = MucheolBodyShiftKey.CAST_START;

            for (int i = 0; i < keys.Length; ++i)
            {
                _maxStrikeIndex = Mathf.Max(_maxStrikeIndex, keys[i].StrikeIndex);
            }
        }

        public MucheolBodyShiftKey GetKey(int index)
        {
            return _keys[index];
        }

        /// <summary> 클립 시각의 이동량(오른쪽 x, 앞 z), 표 끝을 지나면 마지막 값을 유지한다 </summary>
        public Vector2 Sample(float clipSeconds)
        {
            int   count = _path.Length / PAIR_SIZE;
            float pos   = Mathf.Max(0f, clipSeconds) / _sampleStep;
            int   index = Mathf.FloorToInt(pos);
            if (index >= (count - 1))
            {
                int last = (count - 1) * PAIR_SIZE;
                return new Vector2(_path[last], _path[last + 1]);
            }

            float t    = pos - index;
            int   from = index * PAIR_SIZE;
            int   to   = from + PAIR_SIZE;
            return new Vector2(Mathf.Lerp(_path[from], _path[to], t), Mathf.Lerp(_path[from + 1], _path[to + 1], t));
        }
    }
}
