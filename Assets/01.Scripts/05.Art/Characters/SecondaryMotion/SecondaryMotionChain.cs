// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 고정된 부착점의 직접 자식부터 이어지는 보조본 체인과 시각 물리 설정 </summary>
    [Serializable]
    public sealed class SecondaryMotionChain
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float DEFAULT_STIFFNESS  = 60f;
        private const float DEFAULT_DAMPING    = 10f;
        private const float DEFAULT_MAX_ANGLE  = 45f;
        private const float DEFAULT_RADIUS     = 0.02f;

        [SerializeField] private Transform   _root;
        [SerializeField] private Transform[] _bones = Array.Empty<Transform>();

        [Tooltip("마지막 본의 로컬 축과 길이. 중간 본의 끝점은 다음 본의 로컬 위치를 사용한다")]
        [SerializeField] private Vector3 _endAxis   = Vector3.up;
        [SerializeField] private float   _endLength = 0.08f;

        [SerializeField] private float _stiffness = DEFAULT_STIFFNESS;
        [SerializeField] private float _damping   = DEFAULT_DAMPING;
        [SerializeField] private float _maxAngle  = DEFAULT_MAX_ANGLE;
        [SerializeField] private float _radius    = DEFAULT_RADIUS;

        /****************************************
        *              Properties
        ****************************************/

        public Transform Root   => _root;
        public Vector3 EndAxis  => _endAxis;
        public float EndLength  => _endLength;
        public float Stiffness  => _stiffness;
        public float Damping    => _damping;
        public float MaxAngle   => _maxAngle;
        public float Radius     => _radius;
        public int BoneCount    => (_bones == null) ? 0 : _bones.Length;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> root는 움직이지 않는 부착점, bones는 root의 직접 자식부터 마지막 구동 본까지 순서대로 넣는다 </summary>
        public SecondaryMotionChain(Transform root, Transform[] bones, float endLength, Vector3 endAxis,
            float stiffness = DEFAULT_STIFFNESS, float damping = DEFAULT_DAMPING,
            float maxAngle = DEFAULT_MAX_ANGLE, float radius = DEFAULT_RADIUS)
        {
            _root      = root;
            _bones     = bones;
            _endLength = endLength;
            _endAxis   = endAxis;
            _stiffness = stiffness;
            _damping   = damping;
            _maxAngle  = maxAngle;
            _radius    = radius;
        }

        /// <summary> 초기화할 때만 참조를 읽고 런타임 캐시에 보관한다 </summary>
        public Transform GetBone(int index)
        {
            return _bones[index];
        }
    }
}
