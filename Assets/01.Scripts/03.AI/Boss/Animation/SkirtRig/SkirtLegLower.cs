// Unity
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;

namespace Shinshi.AI
{
    // 다리를 크게 뻗는 모션에서 다리가 치마 밖으로 나와 보이면 다리를 조금 내려 치마 안에 들어오게 한다
    // 허벅지가 시작 각도를 넘어 들린 만큼의 일부를 내리고, 내리는 양에는 상한을 둔다
    // 수직 아래에서 벌어진 방향은 그대로 두고 각도만 줄이는 회전이라 다리가 옆으로 틀어지지 않는다
    // 다리 밀기와 치마 뿌리 제약이 낮아진 다리를 기준으로 계산하도록 리그에서 가장 먼저 계산되어야 한다
    /// <summary> 허벅지가 크게 들린 만큼의 일부를 내리는 Animation Rigging 제약의 계산 </summary>
    public struct SkirtLegLowerJob : IWeightedAnimationJob
    {
        private const float EPSILON = 1e-5f;

        public ReadWriteTransformHandle thigh;
        public ReadOnlyTransformHandle  calf;

        public float startAngle;
        public float ratio;
        public float maxLower;

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
                AnimationRuntimeUtils.PassThrough(stream, thigh);
                return;
            }

            Vector3 direction = (calf.GetPosition(stream) - thigh.GetPosition(stream)).normalized;
            float   angle     = Vector3.Angle(direction, Vector3.down);
            float   lower     = Mathf.Min(Mathf.Max(angle - startAngle, 0f) * ratio, maxLower) * weight;

            Vector3 axis = Vector3.Cross(Vector3.down, direction);
            if ((lower <= 0f) || (axis.sqrMagnitude < EPSILON))
            {
                return;
            }

            thigh.SetRotation(stream, Quaternion.AngleAxis(-lower, axis.normalized) * thigh.GetRotation(stream));
        }
    }

    /// <summary> 허벅지를 내리는 제약의 데이터 </summary>
    [System.Serializable]
    public struct SkirtLegLowerData : IAnimationJobData
    {
        [SerializeField] private Transform _thigh;
        [SerializeField] private Transform _calf;

        [SerializeField, Range(0f, 90f)] private float _startAngle;
        [SerializeField, Range(0f, 1f)]  private float _ratio;
        [SerializeField, Range(0f, 90f)] private float _maxLower;

        /// <summary> 내릴 허벅지 </summary>
        public Transform Thigh { get => _thigh; set => _thigh = value; }

        /// <summary> 허벅지 방향을 재는 같은 쪽 종아리 </summary>
        public Transform Calf { get => _calf; set => _calf = value; }

        /// <summary> 허벅지가 수직 아래에서 이 각도를 넘은 만큼부터 내린다 </summary>
        public float StartAngle { get => _startAngle; set => _startAngle = value; }

        /// <summary> 시작 각도를 넘은 만큼 중 내리는 비율 </summary>
        public float Ratio { get => _ratio; set => _ratio = value; }

        /// <summary> 한 번에 내리는 각도의 상한 </summary>
        public float MaxLower { get => _maxLower; set => _maxLower = value; }

        /// <inheritdoc />
        bool IAnimationJobData.IsValid()
        {
            return (_thigh != null) && (_calf != null);
        }

        /// <inheritdoc />
        void IAnimationJobData.SetDefaultValues()
        {
            _thigh      = null;
            _calf       = null;
            _startAngle = 20f;
            _ratio      = 0.5f;
            _maxLower   = 35f;
        }
    }

    /// <summary> 허벅지를 내리는 제약의 연결 </summary>
    public class SkirtLegLowerBinder : AnimationJobBinder<SkirtLegLowerJob, SkirtLegLowerData>
    {
        /// <inheritdoc />
        public override SkirtLegLowerJob Create(Animator animator, ref SkirtLegLowerData data, Component component)
        {
            return new SkirtLegLowerJob
            {
                thigh      = ReadWriteTransformHandle.Bind(animator, data.Thigh),
                calf       = ReadOnlyTransformHandle.Bind(animator, data.Calf),
                startAngle = data.StartAngle,
                ratio      = data.Ratio,
                maxLower   = data.MaxLower,
            };
        }

        /// <inheritdoc />
        public override void Destroy(SkirtLegLowerJob job) { }
    }

    /// <summary> 허벅지가 크게 들린 만큼의 일부를 내리는 Animation Rigging 제약 </summary>
    [DisallowMultipleComponent]
    public class SkirtLegLower : RigConstraint<SkirtLegLowerJob, SkirtLegLowerData, SkirtLegLowerBinder>
    {
    }
}
