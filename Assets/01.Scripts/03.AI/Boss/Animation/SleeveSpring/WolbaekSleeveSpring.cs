// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 소매 본(sleeve_01 -> 02 -> 03) 끝점을 월드 공간의 입자로 두고, 중력과 관성, 원래 자세로 돌아가려는 힘으로 움직인다
    // 입자는 본 길이만큼만 떨어지고 원래 방향에서 최대 각도 안에서만 벗어나므로 팔이나 몸통을 크게 뚫지 못한다
    // 애니메이터가 소매 본을 매 프레임 쓰지 않으면 우리가 돌린 값이 그대로 남으므로, 직전에 쓴 값과 같으면 원래 자세로 되돌린 뒤 다시 계산한다
    // 가중치가 있는 본은 01과 02뿐이고 03은 끝점 역할만 한다
    /// <summary> 소매가 중력을 받아 늘어지고 출렁이게 하는 표현 전용 컴포넌트 </summary>
    public sealed class WolbaekSleeveSpring : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SLEEVE_PREFIX = "sleeve_";
        private const float  SAME_ANGLE    = 0.001f;
        private const float  MAX_STEP      = 1f / 30f;
        private const float  MIN_LENGTH    = 0.0001f;

        private sealed class Joint
        {
            public Transform  Bone;
            public Transform  Tip;
            public Quaternion BaseLocal;
            public Quaternion LastWritten;
            public Vector3    Current;
            public Vector3    Previous;
        }

        private sealed class Chain
        {
            public Joint[] Joints;
            public bool    HasWritten;
        }

        [SerializeField] private float _stiffness    = 40f;    // 원래 자세로 돌아가려는 힘, 클수록 덜 처진다
        [SerializeField] private float _damping      = 5f;     // 속도가 줄어드는 정도, 작을수록 오래 출렁인다
        [SerializeField] private float _gravityScale = 1f;     // 1이면 지구 중력
        [SerializeField] private float _maxDegrees   = 45f;    // 원래 자세에서 벗어날 수 있는 최대 각도

        private readonly List<Chain> _chains = new List<Chain>();

        private bool _needsReset = true;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < all.Length; ++i)
            {
                if (IsChainRoot(all[i]))
                {
                    Chain chain = BuildChain(all[i]);
                    if (chain.Joints.Length > 0)
                    {
                        _chains.Add(chain);
                    }
                }
            }

            if (_chains.Count == 0)
            {
                Debug.LogWarning("월백 소매 스프링: 소매 본을 찾지 못해 꺼집니다", this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            float step = Mathf.Min(Time.deltaTime, MAX_STEP);
            if (step <= 0f)
            {
                return;
            }

            for (int i = 0; i < _chains.Count; ++i)
            {
                Simulate(_chains[i], step);
            }

            _needsReset = false;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 보스가 순간 이동한 프레임에 불러서 기억한 속도와 위치를 새 자세로 다시 잡게 한다 </summary>
        public void OnTeleported()
        {
            _needsReset = true;

            for (int i = 0; i < _chains.Count; ++i)
            {
                _chains[i].HasWritten = false;
            }
        }

        private static bool IsChainRoot(Transform bone)
        {
            return bone.name.StartsWith(SLEEVE_PREFIX) && ((bone.parent == null) || (!bone.parent.name.StartsWith(SLEEVE_PREFIX)));
        }

        // 자식이 있는 마디만 관절로 삼는다, 끝 본은 앞 관절의 끝점으로만 쓰인다
        private static Chain BuildChain(Transform root)
        {
            var joints = new List<Joint>();

            for (Transform current = root; (current != null) && (current.childCount > 0) && current.name.StartsWith(SLEEVE_PREFIX); current = current.GetChild(0))
            {
                joints.Add(new Joint { Bone = current, Tip = current.GetChild(0) });
            }

            return new Chain { Joints = joints.ToArray() };
        }

        private void Simulate(Chain chain, float step)
        {
            Joint[] joints = chain.Joints;

            // 부모부터 위치가 정해지므로 먼저 모든 관절을 원래 자세로 돌려 놓는다
            for (int i = 0; i < joints.Length; ++i)
            {
                Joint joint = joints[i];

                if (chain.HasWritten && (Quaternion.Angle(joint.Bone.localRotation, joint.LastWritten) < SAME_ANGLE))
                {
                    joint.Bone.localRotation = joint.BaseLocal;
                }
                else
                {
                    joint.BaseLocal = joint.Bone.localRotation;
                }
            }

            Vector3 gravity = Physics.gravity * _gravityScale;
            float keep = Mathf.Exp(-_damping * step);
            float maxRadians = _maxDegrees * Mathf.Deg2Rad;

            // 앞 관절이 돌아가면 뒤 관절의 위치도 따라 움직이므로, 부모에서 자식 순서로 하나씩 계산한다
            for (int i = 0; i < joints.Length; ++i)
            {
                Joint joint = joints[i];
                Vector3 origin = joint.Bone.position;
                Vector3 animated = joint.Tip.position - origin;
                float length = animated.magnitude;

                if (length < MIN_LENGTH)
                {
                    continue;
                }

                Vector3 animatedDir = animated / length;
                Vector3 rest = origin + animated;

                if (_needsReset || !chain.HasWritten)
                {
                    joint.Current = rest;
                    joint.Previous = rest;
                }

                Vector3 velocity = (joint.Current - joint.Previous) * keep;
                Vector3 next = joint.Current + velocity + (((rest - joint.Current) * _stiffness) + gravity) * (step * step);

                Vector3 dir = next - origin;
                dir = (dir.sqrMagnitude < (MIN_LENGTH * MIN_LENGTH)) ? animatedDir : dir.normalized;
                dir = Vector3.RotateTowards(animatedDir, dir, maxRadians, 0f);

                next = origin + (dir * length);
                joint.Previous = joint.Current;
                joint.Current = next;

                joint.Bone.rotation = Quaternion.FromToRotation(animatedDir, dir) * joint.Bone.rotation;
                joint.LastWritten = joint.Bone.localRotation;
            }

            chain.HasWritten = true;
        }
    }
}
