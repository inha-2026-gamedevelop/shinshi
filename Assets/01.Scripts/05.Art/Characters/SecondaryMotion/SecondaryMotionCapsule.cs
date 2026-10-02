// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> anchor의 로컬 공간에 정의하는 시각 전용 몸체 캡슐, 게임플레이 Collider를 만들지 않는다 </summary>
    [Serializable]
    public sealed class SecondaryMotionCapsule
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Transform _anchor;
        [SerializeField] private Vector3   _start  = Vector3.zero;
        [SerializeField] private Vector3   _end    = Vector3.up;
        [SerializeField] private float     _radius = 0.1f;

        /****************************************
        *              Properties
        ****************************************/

        public Transform Anchor => _anchor;
        public Vector3 Start    => _start;
        public Vector3 End      => _end;
        public float Radius     => _radius;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> start와 end는 anchor 로컬 좌표이며 radius는 같은 공간의 반지름이다 </summary>
        public SecondaryMotionCapsule(Transform anchor, Vector3 start, Vector3 end, float radius)
        {
            _anchor = anchor;
            _start  = start;
            _end    = end;
            _radius = radius;
        }
    }
}
