// Unity
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;

namespace Shinshi.AI
{
    // 다리를 크게 뻗는 모션에서는 허벅지가 나오는 옆면이 두 치마 줄 사이의 넓은 틈에 놓여 속옷이 보인다
    // 그 틈 양옆의 두 줄을 수직 축으로 허벅지 관절이 있는 방위 쪽으로 돌려서 틈을 메운다
    // 줄마다 돌리는 비율을 따로 주고, 줄이 거의 수직으로 늘어져 방위를 알 수 없을 때는 돌리는 양을 줄인다
    // 치마 뿌리 회전과 다리 밀기 뒤, Damped Transform 앞에서 계산되어야 한다
    /// <summary> 두 치마 줄을 허벅지 관절의 방위 쪽으로 돌려 그 사이 틈을 메우는 Animation Rigging 제약의 계산 </summary>
    public struct SkirtHipCoverJob : IWeightedAnimationJob
    {
        private const float EPSILON    = 1e-5f;
        private const float REACH_FROM = 0.1f;   // 줄이 이만큼 이상 옆으로 퍼져야 돌리기 시작한다
        private const float REACH_FULL = 0.35f;  // 이만큼 퍼지면 정해진 비율을 전부 쓴다

        public ReadOnlyTransformHandle pelvis;
        public ReadOnlyTransformHandle hip;

        public ReadWriteTransformHandle rootA;
        public ReadOnlyTransformHandle  tipA;
        public ReadWriteTransformHandle rootB;
        public ReadOnlyTransformHandle  tipB;

        public FloatProperty factorA;
        public FloatProperty factorB;

        public float maxYaw;

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
                AnimationRuntimeUtils.PassThrough(stream, rootA);
                AnimationRuntimeUtils.PassThrough(stream, rootB);
                return;
            }

            Vector3 toHip = hip.GetPosition(stream) - pelvis.GetPosition(stream);
            toHip.y = 0f;
            if (toHip.sqrMagnitude < EPSILON)
            {
                return;
            }

            Turn(stream, ref rootA, ref tipA, toHip, factorA.Get(stream) * weight);
            Turn(stream, ref rootB, ref tipB, toHip, factorB.Get(stream) * weight);
        }

        private void Turn(AnimationStream stream, ref ReadWriteTransformHandle root, ref ReadOnlyTransformHandle tip, Vector3 toHip, float factor)
        {
            if (factor <= 0f)
            {
                return;
            }

            Vector3 chain = tip.GetPosition(stream) - root.GetPosition(stream);
            Vector3 flat  = new Vector3(chain.x, 0f, chain.z);
            if ((chain.sqrMagnitude < EPSILON) || (flat.sqrMagnitude < EPSILON))
            {
                return;
            }

            float reach = Mathf.InverseLerp(REACH_FROM, REACH_FULL, flat.magnitude / chain.magnitude);
            float delta = Mathf.Clamp(Vector3.SignedAngle(flat, toHip, Vector3.up), -maxYaw, maxYaw);
            float turn  = delta * factor * Mathf.SmoothStep(0f, 1f, reach);

            root.SetRotation(stream, Quaternion.AngleAxis(turn, Vector3.up) * root.GetRotation(stream));
        }
    }

    /// <summary> 두 치마 줄을 허벅지 관절의 방위 쪽으로 돌리는 제약의 데이터 </summary>
    [System.Serializable]
    public struct SkirtHipCoverData : IAnimationJobData
    {
        [SerializeField] private Transform _pelvis;
        [SerializeField] private Transform _hip;
        [SerializeField] private Transform _rootA;
        [SerializeField] private Transform _tipA;
        [SerializeField] private Transform _rootB;
        [SerializeField] private Transform _tipB;

        // 비율은 실행 중에도 바꿔서 확인할 수 있게 장면의 값을 매 프레임 스트림으로 보낸다
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] private float _factorA;
        [SyncSceneToStream, SerializeField, Range(0f, 1f)] private float _factorB;

        [SerializeField, Range(0f, 180f)] private float _maxYaw;

        /// <summary> 치마 줄이 퍼지는 중심 </summary>
        public Transform Pelvis { get => _pelvis; set => _pelvis = value; }

        /// <summary> 방위를 잴 허벅지 관절 </summary>
        public Transform Hip { get => _hip; set => _hip = value; }

        public Transform RootA { get => _rootA; set => _rootA = value; }

        public Transform TipA { get => _tipA; set => _tipA = value; }

        public Transform RootB { get => _rootB; set => _rootB = value; }

        public Transform TipB { get => _tipB; set => _tipB = value; }

        /// <summary> 첫째 줄을 허벅지 방위 쪽으로 돌리는 비율, 1이면 방위까지 돌린다 </summary>
        public float FactorA { get => _factorA; set => _factorA = value; }

        /// <summary> 둘째 줄을 허벅지 방위 쪽으로 돌리는 비율 </summary>
        public float FactorB { get => _factorB; set => _factorB = value; }

        /// <summary> 한 번에 돌릴 수 있는 각도의 상한 </summary>
        public float MaxYaw { get => _maxYaw; set => _maxYaw = value; }

        /// <summary> 첫째 줄 비율의 스트림 속성 이름 </summary>
        public string FactorAProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(_factorA));

        /// <summary> 둘째 줄 비율의 스트림 속성 이름 </summary>
        public string FactorBProperty => ConstraintsUtils.ConstructConstraintDataPropertyName(nameof(_factorB));

        /// <inheritdoc />
        bool IAnimationJobData.IsValid()
        {
            return (_pelvis != null) && (_hip != null) && (_rootA != null) && (_tipA != null) && (_rootB != null) && (_tipB != null);
        }

        /// <inheritdoc />
        void IAnimationJobData.SetDefaultValues()
        {
            _pelvis  = null;
            _hip     = null;
            _rootA   = null;
            _tipA    = null;
            _rootB   = null;
            _tipB    = null;
            _factorA = 0.8f;
            _factorB = 0.5f;
            _maxYaw  = 80f;
        }
    }

    /// <summary> 두 치마 줄을 허벅지 관절의 방위 쪽으로 돌리는 제약의 연결 </summary>
    public class SkirtHipCoverBinder : AnimationJobBinder<SkirtHipCoverJob, SkirtHipCoverData>
    {
        /// <inheritdoc />
        public override SkirtHipCoverJob Create(Animator animator, ref SkirtHipCoverData data, Component component)
        {
            return new SkirtHipCoverJob
            {
                pelvis  = ReadOnlyTransformHandle.Bind(animator, data.Pelvis),
                hip     = ReadOnlyTransformHandle.Bind(animator, data.Hip),
                rootA   = ReadWriteTransformHandle.Bind(animator, data.RootA),
                tipA    = ReadOnlyTransformHandle.Bind(animator, data.TipA),
                rootB   = ReadWriteTransformHandle.Bind(animator, data.RootB),
                tipB    = ReadOnlyTransformHandle.Bind(animator, data.TipB),
                factorA = FloatProperty.Bind(animator, component, data.FactorAProperty),
                factorB = FloatProperty.Bind(animator, component, data.FactorBProperty),
                maxYaw  = data.MaxYaw,
            };
        }

        /// <inheritdoc />
        public override void Destroy(SkirtHipCoverJob job) { }
    }

    /// <summary> 허벅지가 나오는 쪽의 두 치마 줄을 서로 가깝게 돌려 틈을 메우는 Animation Rigging 제약 </summary>
    [DisallowMultipleComponent]
    public class SkirtHipCover : RigConstraint<SkirtHipCoverJob, SkirtHipCoverData, SkirtHipCoverBinder>
    {
    }
}
