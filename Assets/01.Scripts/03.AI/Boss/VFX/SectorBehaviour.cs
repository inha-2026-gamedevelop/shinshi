// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 부채꼴 폭발의 시간 진행 설정 </summary>
    [Serializable]
    public struct SectorBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_EXPLOSION_DURATION = 0.01f;
        private const int   MIN_SEGMENT_COUNT      = 1;
        private const int   MAX_SEGMENT_COUNT      = 32;
        private const float DEFAULT_TELEGRAPH      = 1f;
        private const float DEFAULT_EXPLOSION      = 0.35f;
        private const float DEFAULT_DISSOLVE       = 0.25f;
        private const float DEFAULT_SWEEP          = 0.45f;
        private const int   DEFAULT_SEGMENT_COUNT  = 4;
        private const float DEFAULT_SEGMENT_DELAY  = 0.04f;

        [SerializeField] private ESectorExplosionMode _mode;
        [SerializeField] private ESectorSweepDirection _direction;
        [SerializeField, Min(0f)] private float _telegraphDuration;
        [SerializeField, Min(MIN_EXPLOSION_DURATION)] private float _explosionDuration;
        [SerializeField, Min(0f)] private float _dissolveDuration;
        [SerializeField, Min(0f)] private float _sweepDuration;
        [SerializeField, Range(MIN_SEGMENT_COUNT, MAX_SEGMENT_COUNT)] private int _segmentCount;
        [SerializeField, Min(0f)] private float _segmentDelay;

        /****************************************
        *              Properties
        ****************************************/

        public static SectorBehaviour Default
        {
            get
            {
                SectorBehaviour behaviour = new SectorBehaviour();
                behaviour._mode              = ESectorExplosionMode.AngularSweep;
                behaviour._direction         = ESectorSweepDirection.LeftToRight;
                behaviour._telegraphDuration = DEFAULT_TELEGRAPH;
                behaviour._explosionDuration = DEFAULT_EXPLOSION;
                behaviour._dissolveDuration  = DEFAULT_DISSOLVE;
                behaviour._sweepDuration     = DEFAULT_SWEEP;
                behaviour._segmentCount      = DEFAULT_SEGMENT_COUNT;
                behaviour._segmentDelay      = DEFAULT_SEGMENT_DELAY;
                return behaviour;
            }
        }

        public ESectorExplosionMode Mode { get => _mode; set => _mode = value; }

        public ESectorSweepDirection Direction { get => _direction; set => _direction = value; }

        /// <summary> 충전과 범위 표시 시간, 초 </summary>
        public float TelegraphDuration { get => _telegraphDuration; set => _telegraphDuration = value; }

        /// <summary> 각 지점의 폭발 유지 시간, 초 </summary>
        public float ExplosionDuration { get => _explosionDuration; set => _explosionDuration = value; }

        /// <summary> 각 지점의 잔류 흔적 소멸 시간, 초 </summary>
        public float DissolveDuration { get => _dissolveDuration; set => _dissolveDuration = value; }

        /// <summary> 영역을 진행하는 기본 시간, 초 </summary>
        public float SweepDuration { get => _sweepDuration; set => _sweepDuration = value; }

        /// <summary> 각도 구역 수 </summary>
        public int SegmentCount { get => _segmentCount; set => _segmentCount = value; }

        /// <summary> 다음 구역마다 추가되는 지연, 초 </summary>
        public float SegmentDelay { get => _segmentDelay; set => _segmentDelay = value; }

        /****************************************
        *                Methods
        ****************************************/

        public void Clamp()
        {
            _telegraphDuration = Mathf.Max(0f, _telegraphDuration);
            _explosionDuration = Mathf.Max(MIN_EXPLOSION_DURATION, _explosionDuration);
            _dissolveDuration  = Mathf.Max(0f, _dissolveDuration);
            _sweepDuration     = Mathf.Max(0f, _sweepDuration);
            _segmentCount      = Mathf.Clamp(_segmentCount, MIN_SEGMENT_COUNT, MAX_SEGMENT_COUNT);
            _segmentDelay      = Mathf.Max(0f, _segmentDelay);
        }
    }
}
