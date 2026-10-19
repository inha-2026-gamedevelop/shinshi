// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Systems
{
    // 배열의 점은 호출자가 이미 지면에서 확인한 값이다. 높이 보정은 하지 않고 받은 위치를 그대로 쓴다
    /// <summary> 웨이브별 고정 배열로 만든 지점 목록, 테스트와 좌표를 직접 모아 넘기는 개발 구성이 쓴다 </summary>
    public sealed class ArrayWaveSpawnPoints : IWaveSpawnPointSource
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Vector3[][] _pointsByWave;
        private readonly Vector3[]   _nextWavePoints;

        /****************************************
        *                Methods
        ****************************************/

        // 모든 웨이브가 같은 지점을 쓴다
        public ArrayWaveSpawnPoints(Vector3[] points, Vector3[] nextWavePoints = null) : this(new[] { points }, nextWavePoints)
        {
        }

        // pointsByWave[waveIndex]가 그 웨이브의 지점이다. 웨이브 수보다 짧으면 마지막 배열을 이어 쓴다.
        // nextWavePoints[waveIndex]는 그 웨이브를 마친 뒤 도착해야 할 지점이고 null이면 도착 지점이 없다
        public ArrayWaveSpawnPoints(Vector3[][] pointsByWave, Vector3[] nextWavePoints = null)
        {
            _nextWavePoints = (nextWavePoints == null) ? null : (Vector3[])nextWavePoints.Clone();

            if ((pointsByWave == null) || (pointsByWave.Length == 0))
            {
                throw new ArgumentException("웨이브 지점 배열이 비어 있다", nameof(pointsByWave));
            }

            _pointsByWave = new Vector3[pointsByWave.Length][];
            for (int i = 0; i < pointsByWave.Length; ++i)
            {
                if (pointsByWave[i] == null)
                {
                    throw new ArgumentException("지점 배열에 null이 있다", nameof(pointsByWave));
                }

                _pointsByWave[i] = (Vector3[])pointsByWave[i].Clone();
            }
        }

        public int GetPointCount(int waveIndex)
        {
            return Select(waveIndex).Length;
        }

        public bool TryGetPoint(int waveIndex, int index, out Vector3 position)
        {
            Vector3[] points = Select(waveIndex);
            if ((index < 0) || (index >= points.Length))
            {
                position = default;
                return false;
            }

            position = points[index];
            return true;
        }

        public bool TrySnapToGround(int waveIndex, Vector3 position, out Vector3 grounded)
        {
            grounded = position;
            return false;
        }

        public bool TryGetNextWavePoint(int waveIndex, out Vector3 position)
        {
            if ((_nextWavePoints == null) || (waveIndex < 0) || (waveIndex >= _nextWavePoints.Length))
            {
                position = default;
                return false;
            }

            position = _nextWavePoints[waveIndex];
            return true;
        }

        private Vector3[] Select(int waveIndex)
        {
            int index = Mathf.Clamp(waveIndex, 0, _pointsByWave.Length - 1);
            return _pointsByWave[index];
        }
    }
}
