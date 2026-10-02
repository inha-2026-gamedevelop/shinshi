// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 부채꼴 범위의 형상, 로컬 +Z가 공격 방향이다 </summary>
    [Serializable]
    public struct SectorShape
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float MIN_ANGLE          = 1f;
        private const float MAX_ANGLE          = 360f;
        private const float MIN_RADIUS         = 0.01f;
        private const float INNER_RADIUS_GAP   = 0.001f;
        private const int   MIN_RESOLUTION     = 4;
        private const int   MAX_RESOLUTION     = 256;
        private const float DEFAULT_ANGLE      = 120f;
        private const float DEFAULT_RADIUS     = 6f;
        private const int   DEFAULT_RESOLUTION = 64;

        [SerializeField, Range(MIN_ANGLE, MAX_ANGLE)] private float _angle;
        [SerializeField, Min(MIN_RADIUS)] private float _radius;
        [SerializeField, Min(0f)] private float _innerRadius;
        [SerializeField, Range(MIN_RESOLUTION, MAX_RESOLUTION)] private int _resolution;

        /****************************************
        *              Properties
        ****************************************/

        public static SectorShape Default
        {
            get
            {
                SectorShape shape = new SectorShape();
                shape._angle      = DEFAULT_ANGLE;
                shape._radius     = DEFAULT_RADIUS;
                shape._innerRadius = 0f;
                shape._resolution = DEFAULT_RESOLUTION;
                return shape;
            }
        }

        /// <summary> 전체 각도, 도 단위 </summary>
        public float Angle { get => _angle; set => _angle = value; }

        /// <summary> 최대 거리, m </summary>
        public float Radius { get => _radius; set => _radius = value; }

        /// <summary> 시작 거리, m </summary>
        public float InnerRadius { get => _innerRadius; set => _innerRadius = value; }

        /// <summary> 호 분할 수 </summary>
        public int Resolution { get => _resolution; set => _resolution = value; }

        /****************************************
        *                Methods
        ****************************************/

        public void Clamp()
        {
            _angle       = Mathf.Clamp(_angle, MIN_ANGLE, MAX_ANGLE);
            _radius      = Mathf.Max(MIN_RADIUS, _radius);
            _innerRadius = Mathf.Clamp(_innerRadius, 0f, _radius - INNER_RADIUS_GAP);
            _resolution  = Mathf.Clamp(_resolution, MIN_RESOLUTION, MAX_RESOLUTION);
        }

        public bool IsSameMesh(SectorShape other)
        {
            return (_angle == other._angle) && (_radius == other._radius)
                && (_innerRadius == other._innerRadius) && (_resolution == other._resolution);
        }
    }
}
