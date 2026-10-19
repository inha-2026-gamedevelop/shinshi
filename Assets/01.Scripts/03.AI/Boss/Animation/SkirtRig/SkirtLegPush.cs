// Unity
using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;

namespace Shinshi.AI
{
    // 다리를 크게 앞으로 뻗으면 치마 뿌리가 허벅지를 충분히 따라가지 못해 다리가 치마를 뚫고 나온다
    // 허벅지가 시작 각도를 넘어 들린 만큼만 치마 줄이 허벅지 위로 밀려 오르게 하고, 걷기처럼 작은 각도에서는 아무것도 바꾸지 않는다
    // 다리가 앞으로 뻗으면서 몸 중앙 쪽으로 향하면 반대편 앞줄을 지나가므로, 줄은 양쪽 다리를 모두 보고 줄이 다리 쪽을 향한 정도로 밀림을 정한다
    // 줄의 방향은 기본 자세에서 치마 끝이 골반 둘레 어디에 있는지로 고정한다, 몸통이 기울어도 흔들리지 않게 하려는 것이다
    // 치마 뿌리 회전 제약 뒤, Damped Transform 앞에서 계산되어야 한다
    /// <summary> 허벅지가 들린 각도에 따라 치마 줄을 허벅지 위로 밀어 올리는 Animation Rigging 제약의 계산 </summary>
    public struct SkirtLegPushJob : IWeightedAnimationJob
    {
        private const float EPSILON = 1e-5f;

        public ReadOnlyTransformHandle pelvis;
        public ReadOnlyTransformHandle upright;
        public ReadOnlyTransformHandle leftThigh;
        public ReadOnlyTransformHandle leftCalf;
        public ReadOnlyTransformHandle rightThigh;
        public ReadOnlyTransformHandle rightCalf;

        public NativeArray<ReadWriteTransformHandle> roots;
        public NativeArray<ReadOnlyTransformHandle>  tips;

        /// <summary> 줄마다 기본 자세에서 골반 둘레의 방향, 세움 기준의 로컬 좌표다 </summary>
        public NativeArray<Vector3> outwards;

        public float startAngle;
        public float fullAngle;
        public float marginAngle;
        public float maxFollow;
        public float facingOffset;

        /// <inheritdoc />
        public FloatProperty jobWeight { get; set; }

        /// <inheritdoc />
        public void ProcessRootMotion(AnimationStream stream) { }

        /// <inheritdoc />
        public void ProcessAnimation(AnimationStream stream)
        {
            float weight = jobWeight.Get(stream);

            if (weight <= 0f)
            {
                for (int i = 0; i < roots.Length; i++)
                {
                    AnimationRuntimeUtils.PassThrough(stream, roots[i]);
                }

                return;
            }

            Quaternion uprightRotation = upright.GetRotation(stream);

            Vector3 leftDirection  = (leftCalf.GetPosition(stream) - leftThigh.GetPosition(stream)).normalized;
            Vector3 rightDirection = (rightCalf.GetPosition(stream) - rightThigh.GetPosition(stream)).normalized;

            for (int i = 0; i < roots.Length; i++)
            {
                ReadWriteTransformHandle root = roots[i];
                ReadOnlyTransformHandle  tip  = tips[i];
                Vector3 outward = uprightRotation * outwards[i];

                Push(stream, ref root, ref tip, outward, leftDirection, weight);
                Push(stream, ref root, ref tip, outward, rightDirection, weight);

                roots[i] = root;
                tips[i]  = tip;
            }
        }

        private void Push(AnimationStream stream, ref ReadWriteTransformHandle root, ref ReadOnlyTransformHandle tip,
            Vector3 outward, Vector3 legDirection, float weight)
        {
            float legAngle = Vector3.Angle(legDirection, Vector3.down);
            float lift = Mathf.InverseLerp(startAngle, fullAngle, legAngle);
            if (lift <= 0f)
            {
                return;
            }

            Vector3 legSide = new Vector3(legDirection.x, 0f, legDirection.z);
            if ((legSide.sqrMagnitude < EPSILON) || (outward.sqrMagnitude < EPSILON))
            {
                return;
            }

            float facing = Mathf.Clamp01(Vector3.Dot(outward.normalized, legSide.normalized) + facingOffset);
            float follow = Mathf.SmoothStep(0f, 1f, lift) * facing * maxFollow * weight;
            if (follow <= 0f)
            {
                return;
            }

            Vector3 chain = tip.GetPosition(stream) - root.GetPosition(stream);
            if (chain.sqrMagnitude < EPSILON)
            {
                return;
            }

            // 허벅지가 들린 방향에서 아래에서 더 멀어지는 쪽으로 여유각만큼 더 든 방향이 치마 줄이 있어야 할 곳이다
            Vector3 axis = Vector3.Cross(Vector3.down, legDirection);
            if (axis.sqrMagnitude < EPSILON)
            {
                return;
            }

            Vector3 target = Quaternion.AngleAxis(marginAngle, axis.normalized) * legDirection;

            // 이미 허벅지 위에 있으면 아래로 끌어내리지 않는다
            if (Vector3.Angle(chain, Vector3.down) >= Vector3.Angle(target, Vector3.down))
            {
                return;
            }

            Quaternion push = Quaternion.FromToRotation(chain, target);
            root.SetRotation(stream, Quaternion.Slerp(Quaternion.identity, push, follow) * root.GetRotation(stream));
        }
    }

    /// <summary> 허벅지가 들린 각도로 치마 줄을 밀어 올리는 제약의 데이터 </summary>
    [System.Serializable]
    public struct SkirtLegPushData : IAnimationJobData
    {
        [SerializeField] private Transform _pelvis;
        [SerializeField] private Transform _upright;
        [SerializeField] private Transform _leftThigh;
        [SerializeField] private Transform _leftCalf;
        [SerializeField] private Transform _rightThigh;
        [SerializeField] private Transform _rightCalf;

        [SerializeField] private Transform[] _roots;
        [SerializeField] private Transform[] _tips;

        [SerializeField, Range(0f, 90f)]  private float _startAngle;
        [SerializeField, Range(0f, 120f)] private float _fullAngle;
        [SerializeField, Range(0f, 45f)]  private float _marginAngle;
        [SerializeField, Range(0f, 1f)]   private float _maxFollow;
        [SerializeField, Range(0f, 1f)]   private float _facingOffset;

        /// <summary> 치마 줄이 퍼지는 중심 </summary>
        public Transform Pelvis { get => _pelvis; set => _pelvis = value; }

        /// <summary> 숙이지 않고 바라보는 방향으로만 도는 기준, 줄 방향을 몸통 기울기와 상관없이 구하는 데 쓴다 </summary>
        public Transform Upright { get => _upright; set => _upright = value; }

        public Transform LeftThigh { get => _leftThigh; set => _leftThigh = value; }

        public Transform LeftCalf { get => _leftCalf; set => _leftCalf = value; }

        public Transform RightThigh { get => _rightThigh; set => _rightThigh = value; }

        public Transform RightCalf { get => _rightCalf; set => _rightCalf = value; }

        /// <summary> 밀어 올릴 치마 줄의 뿌리 본 </summary>
        public Transform[] Roots { get => _roots; set => _roots = value; }

        /// <summary> 같은 줄에서 뿌리로부터 줄 방향을 재고 방위를 정하는 끝쪽 본 </summary>
        public Transform[] Tips { get => _tips; set => _tips = value; }

        /// <summary> 허벅지가 수직 아래에서 이 각도를 넘어야 밀기 시작한다 </summary>
        public float StartAngle { get => _startAngle; set => _startAngle = value; }

        /// <summary> 이 각도에서 밀림이 최대가 된다 </summary>
        public float FullAngle { get => _fullAngle; set => _fullAngle = value; }

        /// <summary> 허벅지 방향보다 이만큼 더 들린 곳에 치마 줄이 놓이게 한다 </summary>
        public float MarginAngle { get => _marginAngle; set => _marginAngle = value; }

        /// <summary> 밀림의 최대 비율, 1이면 치마 줄을 목표 방향까지 보낸다 </summary>
        public float MaxFollow { get => _maxFollow; set => _maxFollow = value; }

        /// <summary> 다리와 직각이거나 조금 반대쪽인 줄도 밀리게 하는 보정, 클수록 먼 줄까지 밀린다 </summary>
        public float FacingOffset { get => _facingOffset; set => _facingOffset = value; }

        /// <inheritdoc />
        bool IAnimationJobData.IsValid()
        {
            if ((_pelvis == null) || (_upright == null) ||
                (_leftThigh == null) || (_leftCalf == null) || (_rightThigh == null) || (_rightCalf == null) ||
                (_roots == null) || (_roots.Length == 0) || (_tips == null) || (_tips.Length != _roots.Length))
            {
                return false;
            }

            for (int i = 0; i < _roots.Length; i++)
            {
                if ((_roots[i] == null) || (_tips[i] == null))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        void IAnimationJobData.SetDefaultValues()
        {
            _pelvis       = null;
            _upright      = null;
            _leftThigh    = null;
            _leftCalf     = null;
            _rightThigh   = null;
            _rightCalf    = null;
            _roots        = new Transform[0];
            _tips         = new Transform[0];
            _startAngle   = 40f;
            _fullAngle    = 75f;
            _marginAngle  = 10f;
            _maxFollow    = 0.9f;
            _facingOffset = 0.6f;
        }
    }

    /// <summary> 허벅지가 들린 각도로 치마 줄을 밀어 올리는 제약의 연결 </summary>
    public class SkirtLegPushBinder : AnimationJobBinder<SkirtLegPushJob, SkirtLegPushData>
    {
        /// <inheritdoc />
        public override SkirtLegPushJob Create(Animator animator, ref SkirtLegPushData data, Component component)
        {
            int count = data.Roots.Length;

            SkirtLegPushJob job = new SkirtLegPushJob
            {
                pelvis       = ReadOnlyTransformHandle.Bind(animator, data.Pelvis),
                upright      = ReadOnlyTransformHandle.Bind(animator, data.Upright),
                leftThigh    = ReadOnlyTransformHandle.Bind(animator, data.LeftThigh),
                leftCalf     = ReadOnlyTransformHandle.Bind(animator, data.LeftCalf),
                rightThigh   = ReadOnlyTransformHandle.Bind(animator, data.RightThigh),
                rightCalf    = ReadOnlyTransformHandle.Bind(animator, data.RightCalf),
                roots        = new NativeArray<ReadWriteTransformHandle>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                tips         = new NativeArray<ReadOnlyTransformHandle>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                outwards     = new NativeArray<Vector3>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                startAngle   = data.StartAngle,
                fullAngle    = data.FullAngle,
                marginAngle  = data.MarginAngle,
                maxFollow    = data.MaxFollow,
                facingOffset = data.FacingOffset,
            };

            // 그래프를 만드는 시점의 자세가 기본 자세이므로 이때 줄마다 치마 끝이 골반 둘레 어느 방향인지를 기록한다
            Quaternion toUprightLocal = Quaternion.Inverse(data.Upright.rotation);

            for (int i = 0; i < count; i++)
            {
                job.roots[i] = ReadWriteTransformHandle.Bind(animator, data.Roots[i]);
                job.tips[i]  = ReadOnlyTransformHandle.Bind(animator, data.Tips[i]);

                Vector3 around = data.Tips[i].position - data.Pelvis.position;
                around.y = 0f;
                job.outwards[i] = (toUprightLocal * around).normalized;
            }

            return job;
        }

        /// <inheritdoc />
        public override void Destroy(SkirtLegPushJob job)
        {
            job.roots.Dispose();
            job.tips.Dispose();
            job.outwards.Dispose();
        }
    }

    /// <summary> 허벅지가 크게 들릴 때 치마 줄이 허벅지 위로 밀려 오르게 하는 Animation Rigging 제약 </summary>
    [DisallowMultipleComponent]
    public class SkirtLegPush : RigConstraint<SkirtLegPushJob, SkirtLegPushData, SkirtLegPushBinder>
    {
    }
}
