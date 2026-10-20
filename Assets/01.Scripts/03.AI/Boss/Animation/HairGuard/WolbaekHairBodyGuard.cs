// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 머리카락 본은 모션과 상관없이 머리에 붙어 있어서, 몸을 숙이거나 팔이 움직이면 몸통을 뚫는다
    // 그래서 애니메이션이 끝난 뒤 가슴 본 기준 앞뒤 좌표로 각 마디를 검사해, 앞머리는 몸통 앞면 앞에서, 뒷머리는 몸통 뒷면 뒤에서 나오지 못하게 부모 마디를 돌려 밀어낸다
    // 밀려난 양은 마디마다 기억해서, 밀릴 때는 바로 따라가고 돌아올 때는 일정한 속도로 천천히 줄인다. 필요한 양 아래로는 내려가지 않으므로 돌아오는 도중에 다시 뚫지 않는다
    // 애니메이터가 머리카락 본을 매 프레임 쓰지 않으면 우리가 돌린 값이 그대로 남으므로, 직전에 쓴 값과 같으면 원래 자세로 되돌린 뒤 다시 계산한다
    /// <summary> 머리카락이 몸통을 뚫지 않게 마디를 밀어내는 표현 전용 컴포넌트 </summary>
    public sealed class WolbaekHairBodyGuard : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TORSO_BONE      = "spine_03";
        private const string NECK_BONE       = "neck_01";
        private const string PELVIS_BONE     = "pelvis";
        private const string LEFT_SHOULDER   = "clavicle_l";
        private const string RIGHT_SHOULDER  = "clavicle_r";
        private const string HAIR_PREFIX     = "hair_";
        private const string FRONT_MARK      = "front";
        private const float  SAME_ANGLE      = 0.001f;
        private const float  MIN_LIFT        = 0.0001f;

        private sealed class Chain
        {
            public Transform[]  Bones;
            public Quaternion[] BaseLocal;
            public Quaternion[] LastWritten;
            public float[]      Lift;
            public bool         IsFront;
            public bool         HasWritten;
        }

        // 가슴 본 기준 높이(모델 단위)에 따른 경계의 앞뒤 위치다, 몸통 표면을 재서 정한 출발값이고 화면을 보며 맞춘다
        [SerializeField] private AnimationCurve _frontBoundary = new AnimationCurve(
            new Keyframe(0.35f, -1f, 0f, 0f),
            new Keyframe(0.20f, 0.15f, 0f, 0f),
            new Keyframe(0.05f, 0.14f, 0f, 0f),
            new Keyframe(-0.15f, 0.16f, 0f, 0f),
            new Keyframe(-0.35f, 0.13f, 0f, 0f),
            new Keyframe(-0.75f, 0.07f, 0f, 0f),
            new Keyframe(-1.15f, 0.03f, 0f, 0f));

        [SerializeField] private AnimationCurve _backBoundary = new AnimationCurve(
            new Keyframe(0.35f, 1f, 0f, 0f),
            new Keyframe(0.20f, -0.04f, 0f, 0f),
            new Keyframe(0.05f, -0.09f, 0f, 0f),
            new Keyframe(-0.35f, -0.12f, 0f, 0f),
            new Keyframe(-0.95f, -0.115f, 0f, 0f),
            new Keyframe(-1.15f, -0.09f, 0f, 0f));

        [SerializeField] private float _releaseSpeed      = 0.6f;   // 초당 줄어드는 밀림 양(모델 단위)

        // 앞머리는 숙일 때 몸통 안쪽으로 깊이 들어가 과하게 뻗기 쉬워서 상한을 작게 두고, 뒷머리는 등에 묻히지 않게 더 크게 허용한다
        [SerializeField] private float _frontMaxLiftDistance = 0.1f;    // 한 마디가 밀려나는 최대 거리(모델 단위)
        [SerializeField] private float _frontMaxLiftDegrees  = 25f;
        [SerializeField] private float _backMaxLiftDistance  = 0.3f;
        [SerializeField] private float _backMaxLiftDegrees   = 60f;

        private readonly List<Chain> _chains = new List<Chain>();

        private Transform _torso;
        private Transform _neck;
        private Transform _pelvis;
        private Transform _leftShoulder;
        private Transform _rightShoulder;
        private Transform _root;
        private float     _forwardSign = 1f;
        private float     _scale       = 1f;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _root = transform;
            _scale = _root.lossyScale.x;

            Transform[] all = GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < all.Length; ++i)
            {
                switch (all[i].name)
                {
                    case TORSO_BONE:
                        _torso = all[i];
                        break;
                    case NECK_BONE:
                        _neck = all[i];
                        break;
                    case PELVIS_BONE:
                        _pelvis = all[i];
                        break;
                    case LEFT_SHOULDER:
                        _leftShoulder = all[i];
                        break;
                    case RIGHT_SHOULDER:
                        _rightShoulder = all[i];
                        break;
                }
            }

            if ((_torso == null) || (_neck == null) || (_pelvis == null) || (_leftShoulder == null) || (_rightShoulder == null))
            {
                Debug.LogWarning("월백 머리카락 보호: 몸통 기준 본을 찾지 못해 꺼집니다", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < all.Length; ++i)
            {
                if (IsChainRoot(all[i]))
                {
                    _chains.Add(BuildChain(all[i]));
                }
            }

            // 모델이 바라보는 쪽이 몸통 앞이라고 보고 한 번만 부호를 정한다
            ComputeFrame(out Vector3 right, out Vector3 up, out Vector3 forward);
            _forwardSign = (Vector3.Dot(forward, _root.forward) >= 0f) ? 1f : -1f;
        }

        private void LateUpdate()
        {
            ComputeFrame(out Vector3 right, out Vector3 up, out Vector3 forward);
            forward *= _forwardSign;

            Vector3 origin = _torso.position;

            for (int i = 0; i < _chains.Count; ++i)
            {
                Apply(_chains[i], origin, up, forward);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 보스가 순간 이동한 프레임에 불러서 기억한 밀림 양을 비운다 </summary>
        public void OnTeleported()
        {
            for (int i = 0; i < _chains.Count; ++i)
            {
                System.Array.Clear(_chains[i].Lift, 0, _chains[i].Lift.Length);
                _chains[i].HasWritten = false;
            }
        }

        private static bool IsChainRoot(Transform bone)
        {
            return (bone.name.StartsWith(HAIR_PREFIX)) && ((bone.parent == null) || (!bone.parent.name.StartsWith(HAIR_PREFIX)));
        }

        private static Chain BuildChain(Transform root)
        {
            var bones = new List<Transform>();

            for (Transform current = root; current != null; current = (current.childCount > 0) ? current.GetChild(0) : null)
            {
                if (!current.name.StartsWith(HAIR_PREFIX))
                {
                    break;
                }

                bones.Add(current);
            }

            var chain = new Chain
            {
                Bones       = bones.ToArray(),
                BaseLocal   = new Quaternion[bones.Count],
                LastWritten = new Quaternion[bones.Count],
                Lift        = new float[bones.Count],
                IsFront     = root.name.Contains(FRONT_MARK),
            };

            return chain;
        }

        // 가슴 위쪽 방향과 어깨 방향으로 몸통 좌표계를 만든다, 숙이면 같이 기운다
        private void ComputeFrame(out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            up = (_neck.position - _pelvis.position).normalized;

            right = _rightShoulder.position - _leftShoulder.position;

            if (Vector3.Dot(right, _root.right) < 0f)
            {
                right = -right;
            }

            right = (right - (up * Vector3.Dot(right, up))).normalized;
            forward = Vector3.Cross(right, up);
        }

        private void Apply(Chain chain, Vector3 origin, Vector3 up, Vector3 forward)
        {
            Transform[] bones = chain.Bones;

            // 부모부터 위치가 정해지므로 먼저 모든 마디를 원래 자세로 돌려 놓는다
            for (int i = 0; i < bones.Length; ++i)
            {
                if ((chain.HasWritten) && (Quaternion.Angle(bones[i].localRotation, chain.LastWritten[i]) < SAME_ANGLE))
                {
                    bones[i].localRotation = chain.BaseLocal[i];
                }
                else
                {
                    chain.BaseLocal[i] = bones[i].localRotation;
                }
            }

            AnimationCurve boundary = chain.IsFront ? _frontBoundary : _backBoundary;
            float releaseStep = _releaseSpeed * _scale * Time.deltaTime;
            float maxDistance = (chain.IsFront ? _frontMaxLiftDistance : _backMaxLiftDistance) * _scale;
            float maxDegrees = chain.IsFront ? _frontMaxLiftDegrees : _backMaxLiftDegrees;

            // 마디 i+1의 위치는 마디 i의 회전이 정하므로, 부모에서 자식 순서로 하나씩 고친다
            for (int i = 0; i < (bones.Length - 1); ++i)
            {
                Transform bone = bones[i];
                Vector3 joint = bones[i + 1].position;
                Vector3 offset = joint - origin;

                float height = Vector3.Dot(offset, up) / _scale;
                float depth = Vector3.Dot(offset, forward);
                float limit = boundary.Evaluate(height) * _scale;

                float needed = chain.IsFront ? (limit - depth) : (depth - limit);
                float lift = Mathf.Min(Mathf.Max(needed, chain.Lift[i + 1] - releaseStep), maxDistance);

                if (lift < MIN_LIFT)
                {
                    chain.Lift[i + 1] = 0f;
                    continue;
                }

                chain.Lift[i + 1] = lift;

                Vector3 push = chain.IsFront ? forward : -forward;
                Vector3 target = joint + (push * lift);
                Quaternion swing = Quaternion.FromToRotation(joint - bone.position, target - bone.position);
                swing = Quaternion.RotateTowards(Quaternion.identity, swing, maxDegrees);

                bone.rotation = swing * bone.rotation;
            }

            for (int i = 0; i < bones.Length; ++i)
            {
                chain.LastWritten[i] = bones[i].localRotation;
            }

            chain.HasWritten = true;
        }
    }
}
