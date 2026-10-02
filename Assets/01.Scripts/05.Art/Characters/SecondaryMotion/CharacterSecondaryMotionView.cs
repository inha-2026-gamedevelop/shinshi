// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> Animator 뒤에 머리와 옷 보조본의 회전만 갱신하는 클라이언트 시각 물리 </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(CharacterSecondaryMotionView.EXECUTION_ORDER)]
    public sealed class CharacterSecondaryMotionView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int EXECUTION_ORDER         = 10000;
        private const int CONSTRAINT_ITERATIONS   = 8;
        private const int MAX_ALLOWED_SUBSTEPS    = 32;
        private const float MAX_ALLOWED_STIFFNESS = 10000f;
        private const float MAX_ALLOWED_DAMPING   = 100f;
        private const float MAX_STIFFNESS_STEP    = 1f;
        private const float POSE_TOLERANCE        = 0.00001f;
        private const float COLLISION_TOLERANCE   = 0.0001f;
        private const float MIN_STEP_SECONDS      = 0.001f;
        private const float MAX_STEP_SECONDS      = 0.033333334f;

        [Header("보조본과 몸체")]
        [SerializeField] private Transform                  _motionRoot;
        [SerializeField] private SecondaryMotionChain[]     _chains   = Array.Empty<SecondaryMotionChain>();
        [SerializeField] private SecondaryMotionCapsule[]   _capsules = Array.Empty<SecondaryMotionCapsule>();

        [Header("시각 물리")]
        [SerializeField] private Vector3 _gravity            = new Vector3(0f, -9.81f, 0f);
        [SerializeField] private float   _stepSeconds        = 0.008333334f;
        [SerializeField] private int     _maxSubsteps        = 12;
        [SerializeField] private float   _maxFrameSeconds    = 0.1f;
        [SerializeField] private float   _teleportDistance   = 1.5f;
        [SerializeField] private float   _teleportAngle      = 100f;

        private BoneState[]    _boneStates    = Array.Empty<BoneState>();
        private CapsuleState[] _capsuleStates = Array.Empty<CapsuleState>();
        private HierarchyState[] _hierarchyStates = Array.Empty<HierarchyState>();
        private Vector3        _previousRootPosition;
        private Quaternion     _previousRootRotation;
        private Vector3        _previousRootScale;
        private float          _accumulator;
        private bool           _initialized;
        private bool           _needsReset = true;
        private string         _validationError = string.Empty;
        private int            _unresolvedCollisionCount;

        private struct BoneState
        {
            public Transform Bone;
            public Transform Parent;
            public Vector3 BindLocalPosition;
            public Vector3 BindLocalScale;
            public Vector3 EndLocalOffset;
            public Quaternion BindLocalRotation;
            public Quaternion AnimatedLocalRotation;
            public Vector3 Tip;
            public Vector3 PreviousTip;
            public float Stiffness;
            public float Damping;
            public float MaxAngle;
            public float Radius;
        }

        private struct CapsuleState
        {
            public Transform Anchor;
            public Vector3 LocalStart;
            public Vector3 LocalEnd;
            public float LocalRadius;
            public Vector3 WorldStart;
            public Vector3 WorldEnd;
            public float WorldRadius;
        }

        private struct HierarchyState
        {
            public Transform Transform;
            public Transform Parent;
        }

        /****************************************
        *              Properties
        ****************************************/

        public bool IsInitialized     => _initialized;
        public int SimulatedBoneCount => _boneStates.Length;
        public string ValidationError => _validationError;
        /// <summary> 마지막 시각 물리 적용 뒤에 몸체 캡슐 안에 남은 본 끝점 수, 배치와 회전 한계를 조정할 때 쓴다 </summary>
        public int UnresolvedCollisionCount => _unresolvedCollisionCount;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            // 보조본의 authored pose는 Animator가 처음 평가하기 전에 한 번만 잡는다
            TryInitialize(out _validationError);
        }

        private void OnEnable()
        {
            if (IsServerBuild)
            {
                enabled = false;
                return;
            }

            ResetSimulation();
        }

        private void OnDisable()
        {
            RestoreAnimatedPose();
            _accumulator = 0f;
            _needsReset  = true;
        }

        private void Update()
        {
            if ((!_initialized) || (IsServerBuild))
            {
                return;
            }

            // 애니메이션에 키가 없는 추가본도 이전 시각 회전을 다시 기준 포즈로 읽지 않아야 한다.
            // Update에서 이전 기준 회전을 돌려놓고 Animator 평가 뒤에 새 기준 포즈를 읽는다.
            RestoreAnimatedPose();
        }

        private void LateUpdate()
        {
            UpdateVisual(Time.deltaTime);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 최초 Animator 평가 전의 authored pose에서 설정한다, 재설정은 기존 시각 회전을 먼저 복원한다 </summary>
        public bool Configure(Transform motionRoot, SecondaryMotionChain[] chains,
            SecondaryMotionCapsule[] capsules)
        {
            RestoreAnimatedPose();
            _motionRoot = motionRoot;
            _chains     = chains;
            _capsules   = capsules;
            return TryInitialize(out _validationError);
        }

        /// <summary> 계층과 수치를 검증하고 캐시를 만든다, 매 프레임 호출하지 않는다 </summary>
        public bool TryInitialize(out string error)
        {
            RestoreAnimatedPose();
            _initialized = false;
            _boneStates = Array.Empty<BoneState>();
            _capsuleStates = Array.Empty<CapsuleState>();
            _hierarchyStates = Array.Empty<HierarchyState>();
            _needsReset = true;
            _accumulator = 0f;

            if (IsServerBuild)
            {
                error = "전용 서버에서는 보조본 시각 물리를 초기화하지 않는다";
                _validationError = error;
                return false;
            }

            if (!ValidateConfiguration(out int boneCount, out error))
            {
                _validationError = error;
                return false;
            }

            _boneStates = new BoneState[boneCount];
            _capsuleStates = new CapsuleState[(_capsules == null) ? 0 : _capsules.Length];
            int stateIndex = 0;
            for (int chainIndex = 0; chainIndex < _chains.Length; ++chainIndex)
            {
                SecondaryMotionChain chain = _chains[chainIndex];
                for (int boneIndex = 0; boneIndex < chain.BoneCount; ++boneIndex)
                {
                    Transform bone = chain.GetBone(boneIndex);
                    Vector3 endOffset = (boneIndex < (chain.BoneCount - 1))
                        ? chain.GetBone(boneIndex + 1).localPosition : chain.EndAxis.normalized * chain.EndLength;
                    _boneStates[stateIndex] = new BoneState
                    {
                        Bone = bone,
                        Parent = bone.parent,
                        BindLocalPosition = bone.localPosition,
                        BindLocalScale = bone.localScale,
                        EndLocalOffset = endOffset,
                        BindLocalRotation = bone.localRotation,
                        AnimatedLocalRotation = bone.localRotation,
                        Stiffness = chain.Stiffness,
                        Damping = chain.Damping,
                        MaxAngle = chain.MaxAngle,
                        Radius = chain.Radius
                    };
                    ++stateIndex;
                }
            }

            for (int i = 0; i < _capsuleStates.Length; ++i)
            {
                SecondaryMotionCapsule capsule = _capsules[i];
                _capsuleStates[i] = new CapsuleState
                {
                    Anchor = capsule.Anchor,
                    LocalStart = capsule.Start,
                    LocalEnd = capsule.End,
                    LocalRadius = capsule.Radius
                };
            }

            CacheHierarchy();

            _initialized = true;
            _validationError = string.Empty;
            error = string.Empty;
            ResetSimulation();
            return true;
        }

        /// <summary> 순간이동이나 재활성 시 호출하면 현재 기준 포즈에서 관성을 버리고 다시 시작한다 </summary>
        public void ResetSimulation()
        {
            RestoreAnimatedPose();
            _accumulator = 0f;
            _needsReset = true;
            _unresolvedCollisionCount = 0;

            if ((!_initialized) || (IsServerBuild) || (!ValidateRuntimePose()))
            {
                return;
            }

            ResetTips();
        }

        private void UpdateVisual(float deltaTime)
        {
            if ((!_initialized) || (IsServerBuild))
            {
                return;
            }

            if ((!SecondaryMotionMath.IsFinite(deltaTime)) || (deltaTime < 0f) || (!HasValidIntegrationSettings())
                || (!ValidateRuntimePose()))
            {
                ResetSimulation();
                return;
            }

            CaptureAnimatedPose();
            bool scaleChanged = (_motionRoot.lossyScale - _previousRootScale).sqrMagnitude
                > (POSE_TOLERANCE * POSE_TOLERANCE);
            if ((_needsReset) || (deltaTime > _maxFrameSeconds) || (scaleChanged)
                || (SecondaryMotionMath.HasTeleported(_previousRootPosition, _previousRootRotation,
                    _motionRoot.position, _motionRoot.rotation, _teleportDistance, _teleportAngle)))
            {
                ResetTips();
                return;
            }

            if (deltaTime == 0f)
            {
                _needsReset = true;
                return;
            }

            if (!CacheWorldCapsules())
            {
                ResetSimulation();
                return;
            }
            _accumulator += deltaTime;
            int steps = 0;
            while ((_accumulator >= _stepSeconds) && (steps < _maxSubsteps))
            {
                SimulateStep(_stepSeconds);
                _accumulator -= _stepSeconds;
                ++steps;
            }

            // 오래 멈춘 프레임의 누적 시간을 다음 프레임으로 넘기면 짧은 본의 스프링이 폭주한다
            if (_accumulator >= _stepSeconds)
            {
                ResetTips();
                return;
            }

            ApplySimulatedPose();
            CacheRootPose();
        }

        private void SimulateStep(float step)
        {
            RestoreAnimatedPose();
            for (int i = 0; i < _boneStates.Length; ++i)
            {
                ref BoneState state = ref _boneStates[i];
                Vector3 origin = state.Bone.position;
                Vector3 restTip = state.Bone.TransformPoint(state.EndLocalOffset);
                Vector3 velocity = (state.Tip - state.PreviousTip) * Mathf.Exp(-state.Damping * step);
                Vector3 acceleration = ((restTip - state.Tip) * state.Stiffness) + _gravity;
                Vector3 candidate = state.Tip + velocity + (acceleration * (step * step));
                if (!SecondaryMotionMath.IsFinite(candidate))
                {
                    candidate = restTip;
                    state.Tip = restTip;
                }

                state.PreviousTip = state.Tip;
                state.Tip = ConstrainTip(ref state, origin, candidate);
                ApplyBoneRotation(ref state, origin);
                state.Tip = state.Bone.TransformPoint(state.EndLocalOffset);
            }
        }

        private void ApplySimulatedPose()
        {
            RestoreAnimatedPose();
            _unresolvedCollisionCount = 0;
            for (int i = 0; i < _boneStates.Length; ++i)
            {
                ref BoneState state = ref _boneStates[i];
                Vector3 origin = state.Bone.position;
                state.Tip = ConstrainTip(ref state, origin, state.Tip);
                ApplyBoneRotation(ref state, origin);
                state.Tip = state.Bone.TransformPoint(state.EndLocalOffset);
                if (HasUnresolvedCollision(ref state))
                {
                    ++_unresolvedCollisionCount;
                }
            }
        }

        private Vector3 ConstrainTip(ref BoneState state, Vector3 origin, Vector3 candidate)
        {
            Vector3 restLocal = state.AnimatedLocalRotation * Vector3.Scale(state.EndLocalOffset, state.BindLocalScale);
            float radius = state.Radius * GetMaximumScale(state.Bone.lossyScale);
            Vector3 fallback = state.Parent.TransformVector(restLocal);
            for (int iteration = 0; iteration < CONSTRAINT_ITERATIONS; ++iteration)
            {
                Vector3 previousCandidate = candidate;
                for (int capsuleIndex = 0; capsuleIndex < _capsuleStates.Length; ++capsuleIndex)
                {
                    ref CapsuleState capsule = ref _capsuleStates[capsuleIndex];
                    Vector3 collisionFallback = GetCollisionFallback(fallback, capsule.WorldEnd - capsule.WorldStart);
                    candidate = SecondaryMotionMath.ResolveCapsule(candidate, capsule.WorldStart,
                        capsule.WorldEnd, capsule.WorldRadius + radius, collisionFallback);
                }

                // 부모의 비균일 스케일에서도 로컬 본 길이를 유지해야 하므로 부모 공간에서 제한한다
                Vector3 localDirection = state.Parent.InverseTransformVector(candidate - origin);
                Vector3 constrained = SecondaryMotionMath.ConstrainDirection(localDirection, restLocal, state.MaxAngle);
                candidate = origin + state.Parent.TransformVector(constrained);

                if (((candidate - previousCandidate).sqrMagnitude <= (POSE_TOLERANCE * POSE_TOLERANCE))
                    && (iteration < (CONSTRAINT_ITERATIONS - 1)))
                {
                    // 축 위에서 반지름 방향으로 밀면 길이 제한이 같은 관통점으로 돌려놓는다.
                    // 움직임이 멈춘 관통점은 본과 캡슐 축 모두에 수직인 접선 후보로 다시 푼다.
                    for (int capsuleIndex = 0; capsuleIndex < _capsuleStates.Length; ++capsuleIndex)
                    {
                        ref CapsuleState capsule = ref _capsuleStates[capsuleIndex];
                        float combinedRadius = capsule.WorldRadius + radius;
                        if (!SecondaryMotionMath.IsInsideCapsule(candidate, capsule.WorldStart, capsule.WorldEnd,
                            combinedRadius, COLLISION_TOLERANCE))
                        {
                            continue;
                        }

                        Vector3 tangent = GetCollisionFallback(candidate - origin, capsule.WorldEnd - capsule.WorldStart);
                        Vector3 escapeLocal = state.Parent.InverseTransformVector((candidate - origin) + (tangent * combinedRadius));
                        Vector3 escape = SecondaryMotionMath.ConstrainDirection(escapeLocal, restLocal, state.MaxAngle);
                        candidate = origin + state.Parent.TransformVector(escape);
                        break;
                    }
                }
            }

            return SecondaryMotionMath.IsFinite(candidate) ? candidate : origin + fallback;
        }

        private static Vector3 GetCollisionFallback(Vector3 boneDirection, Vector3 capsuleAxis)
        {
            Vector3 tangent = Vector3.Cross(boneDirection, capsuleAxis);
            if ((!SecondaryMotionMath.IsFinite(tangent)) || (tangent.sqrMagnitude <= (POSE_TOLERANCE * POSE_TOLERANCE)))
            {
                tangent = Vector3.Cross(boneDirection, Vector3.up);
            }

            if ((!SecondaryMotionMath.IsFinite(tangent)) || (tangent.sqrMagnitude <= (POSE_TOLERANCE * POSE_TOLERANCE)))
            {
                tangent = Vector3.Cross(boneDirection, Vector3.right);
            }

            return tangent.normalized;
        }

        private bool HasUnresolvedCollision(ref BoneState state)
        {
            float radius = state.Radius * GetMaximumScale(state.Bone.lossyScale);
            for (int i = 0; i < _capsuleStates.Length; ++i)
            {
                ref CapsuleState capsule = ref _capsuleStates[i];
                if (SecondaryMotionMath.IsInsideCapsule(state.Tip, capsule.WorldStart, capsule.WorldEnd,
                    capsule.WorldRadius + radius, COLLISION_TOLERANCE))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyBoneRotation(ref BoneState state, Vector3 origin)
        {
            Vector3 restLocal = state.AnimatedLocalRotation * Vector3.Scale(state.EndLocalOffset, state.BindLocalScale);
            Vector3 desiredLocal = state.Parent.InverseTransformVector(state.Tip - origin);
            Quaternion rotation = SecondaryMotionMath.GetSwingRotation(restLocal, desiredLocal) * state.AnimatedLocalRotation;
            state.Bone.localRotation = SecondaryMotionMath.IsFinite(rotation) ? rotation : state.BindLocalRotation;
        }

        private void RestoreAnimatedPose()
        {
            for (int i = 0; i < _boneStates.Length; ++i)
            {
                ref BoneState state = ref _boneStates[i];
                if (state.Bone != null)
                {
                    state.Bone.localRotation = state.AnimatedLocalRotation;
                }
            }
        }

        private void CaptureAnimatedPose()
        {
            for (int i = 0; i < _boneStates.Length; ++i)
            {
                _boneStates[i].AnimatedLocalRotation = _boneStates[i].Bone.localRotation;
            }
        }

        private void ResetTips()
        {
            for (int i = 0; i < _boneStates.Length; ++i)
            {
                ref BoneState state = ref _boneStates[i];
                Vector3 tip = state.Bone.TransformPoint(state.EndLocalOffset);
                state.Tip = tip;
                state.PreviousTip = tip;
            }

            _accumulator = 0f;
            _needsReset = false;
            _unresolvedCollisionCount = 0;
            CacheRootPose();
        }

        private void CacheRootPose()
        {
            _previousRootPosition = _motionRoot.position;
            _previousRootRotation = _motionRoot.rotation;
            _previousRootScale = _motionRoot.lossyScale;
        }

        private bool CacheWorldCapsules()
        {
            for (int i = 0; i < _capsuleStates.Length; ++i)
            {
                ref CapsuleState state = ref _capsuleStates[i];
                state.WorldStart = state.Anchor.TransformPoint(state.LocalStart);
                state.WorldEnd = state.Anchor.TransformPoint(state.LocalEnd);
                // 비균일 스케일은 가장 큰 축의 반지름으로 보수적으로 감싼다
                state.WorldRadius = state.LocalRadius * GetMaximumScale(state.Anchor.lossyScale);
                if ((!SecondaryMotionMath.IsFinite(state.WorldStart)) || (!SecondaryMotionMath.IsFinite(state.WorldEnd))
                    || (!SecondaryMotionMath.IsFinite(state.WorldRadius)))
                {
                    return false;
                }
            }

            return true;
        }

        private bool ValidateRuntimePose()
        {
            if ((_motionRoot == null) || (!HasFinitePose(_motionRoot)))
            {
                return false;
            }

            for (int i = 0; i < _hierarchyStates.Length; ++i)
            {
                ref HierarchyState state = ref _hierarchyStates[i];
                if ((state.Transform == null) || (state.Transform.parent != state.Parent))
                {
                    return false;
                }
            }

            for (int i = 0; i < _boneStates.Length; ++i)
            {
                ref BoneState state = ref _boneStates[i];
                if ((state.Bone == null) || (state.Parent == null) || (state.Bone.parent != state.Parent) || (!HasFinitePose(state.Bone))
                    || (!HasFinitePose(state.Parent)) || (!SecondaryMotionMath.IsFinite(state.Bone.TransformPoint(state.EndLocalOffset))))
                {
                    return false;
                }

                // 보조본의 위치와 스케일 키는 지원하지 않는다. 외부 변형을 강제로 덮어쓰지도 않는다.
                if (((state.Bone.localPosition - state.BindLocalPosition).sqrMagnitude > (POSE_TOLERANCE * POSE_TOLERANCE))
                    || ((state.Bone.localScale - state.BindLocalScale).sqrMagnitude > (POSE_TOLERANCE * POSE_TOLERANCE)))
                {
                    return false;
                }
            }

            for (int i = 0; i < _capsuleStates.Length; ++i)
            {
                Transform anchor = _capsuleStates[i].Anchor;
                if ((anchor == null) || (!HasFinitePose(anchor)))
                {
                    return false;
                }
            }

            return true;
        }

        private void CacheHierarchy()
        {
            var visited = new HashSet<Transform>();
            var hierarchy = new List<HierarchyState>();
            for (int i = 0; i < _chains.Length; ++i)
            {
                CacheAncestors(_chains[i].Root, visited, hierarchy);
            }

            for (int i = 0; i < _capsuleStates.Length; ++i)
            {
                CacheAncestors(_capsuleStates[i].Anchor, visited, hierarchy);
            }

            _hierarchyStates = hierarchy.ToArray();
        }

        private void CacheAncestors(Transform value, HashSet<Transform> visited, List<HierarchyState> hierarchy)
        {
            while ((value != null) && (value != _motionRoot))
            {
                if (!visited.Add(value))
                {
                    return;
                }

                hierarchy.Add(new HierarchyState { Transform = value, Parent = value.parent });
                value = value.parent;
            }
        }

        private bool ValidateConfiguration(out int boneCount, out string error)
        {
            boneCount = 0;
            error = "보조본 설정이 유효하지 않다";
            if ((_motionRoot == null) || (!HasFinitePose(_motionRoot)) || (_chains == null) || (_chains.Length == 0))
            {
                error = "유한한 motionRoot와 한 개 이상의 보조본 체인이 필요하다";
                return false;
            }

            if (!HasValidIntegrationSettings())
            {
                error = "중력, 시간 간격, 최대 substep 또는 순간이동 한계가 유효하지 않다";
                return false;
            }

            var driven = new HashSet<Transform>();
            for (int chainIndex = 0; chainIndex < _chains.Length; ++chainIndex)
            {
                SecondaryMotionChain chain = _chains[chainIndex];
                if ((chain == null) || (chain.Root == null) || (!IsInMotionHierarchy(chain.Root))
                    || (!HasFinitePose(chain.Root)) || (chain.BoneCount == 0)
                    || (!SecondaryMotionMath.IsFinite(chain.EndAxis))
                    || (chain.EndAxis.sqrMagnitude <= (SecondaryMotionMath.LENGTH_EPSILON * SecondaryMotionMath.LENGTH_EPSILON))
                    || (!SecondaryMotionMath.IsFinite(chain.EndLength)) || (chain.EndLength <= SecondaryMotionMath.LENGTH_EPSILON)
                    || (!SecondaryMotionMath.IsFinite(chain.Stiffness)) || (chain.Stiffness < 0f)
                    || (chain.Stiffness > MAX_ALLOWED_STIFFNESS)
                    || ((chain.Stiffness * _stepSeconds * _stepSeconds) > MAX_STIFFNESS_STEP)
                    || (!SecondaryMotionMath.IsFinite(chain.Damping)) || (chain.Damping < 0f)
                    || (chain.Damping > MAX_ALLOWED_DAMPING)
                    || (!SecondaryMotionMath.IsFinite(chain.MaxAngle)) || (chain.MaxAngle < 0f)
                    || (chain.MaxAngle > SecondaryMotionMath.MAX_ANGLE)
                    || (!SecondaryMotionMath.IsFinite(chain.Radius)) || (chain.Radius < 0f))
                {
                    error = $"체인 {chainIndex}의 부착점, 길이 또는 시각 물리 수치가 유효하지 않다";
                    return false;
                }

                Transform parent = chain.Root;
                for (int boneIndex = 0; boneIndex < chain.BoneCount; ++boneIndex)
                {
                    Transform bone = chain.GetBone(boneIndex);
                    if ((bone == null) || (bone.parent != parent) || (!HasFinitePose(bone)) || (!driven.Add(bone)))
                    {
                        error = $"체인 {chainIndex}의 본 {boneIndex}가 직접 자식이 아니거나 중복되었다";
                        return false;
                    }

                    Transform next = (boneIndex < (chain.BoneCount - 1)) ? chain.GetBone(boneIndex + 1) : null;
                    if ((boneIndex < (chain.BoneCount - 1)) && (next == null))
                    {
                        error = $"체인 {chainIndex}의 다음 본이 비어 있다";
                        return false;
                    }

                    Vector3 offset = (next != null) ? next.localPosition : chain.EndAxis.normalized * chain.EndLength;
                    Vector3 worldOffset = bone.TransformVector(offset);
                    if ((!SecondaryMotionMath.IsFinite(offset)) || (!SecondaryMotionMath.IsFinite(worldOffset))
                        || (!SecondaryMotionMath.IsFinite(bone.TransformPoint(offset)))
                        || (worldOffset.sqrMagnitude <= (SecondaryMotionMath.LENGTH_EPSILON * SecondaryMotionMath.LENGTH_EPSILON)))
                    {
                        error = $"체인 {chainIndex}의 본 {boneIndex} 끝점 길이가 0이거나 유한하지 않다";
                        return false;
                    }

                    parent = bone;
                    ++boneCount;
                }
            }

            foreach (Transform bone in driven)
            {
                for (int chainIndex = 0; chainIndex < _chains.Length; ++chainIndex)
                {
                    Transform root = _chains[chainIndex].Root;
                    if ((root == bone) || (root.IsChildOf(bone)))
                    {
                        error = "체인의 부착점이 다른 체인의 구동 본 아래에 있어 조상 체인이 겹친다";
                        return false;
                    }
                }
            }

            if (_capsules != null)
            {
                for (int i = 0; i < _capsules.Length; ++i)
                {
                    SecondaryMotionCapsule capsule = _capsules[i];
                    if ((capsule == null) || (capsule.Anchor == null) || (!IsInMotionHierarchy(capsule.Anchor))
                        || (!HasFinitePose(capsule.Anchor)) || (!SecondaryMotionMath.IsFinite(capsule.Start))
                        || (!SecondaryMotionMath.IsFinite(capsule.End)) || (!SecondaryMotionMath.IsFinite(capsule.Radius))
                        || (capsule.Radius <= 0f)
                        || (!SecondaryMotionMath.IsFinite(capsule.Anchor.TransformPoint(capsule.Start)))
                        || (!SecondaryMotionMath.IsFinite(capsule.Anchor.TransformPoint(capsule.End)))
                        || (!SecondaryMotionMath.IsFinite(capsule.Radius * GetMaximumScale(capsule.Anchor.lossyScale))))
                    {
                        error = $"몸체 캡슐 {i}의 부착점이나 크기가 유효하지 않다";
                        return false;
                    }

                    foreach (Transform bone in driven)
                    {
                        if ((capsule.Anchor == bone) || (capsule.Anchor.IsChildOf(bone)))
                        {
                            error = "몸체 캡슐은 시뮬레이션하는 보조본 아래에 붙일 수 없다";
                            return false;
                        }
                    }
                }
            }

            error = string.Empty;
            return true;
        }

        private bool HasValidIntegrationSettings()
        {
            return (SecondaryMotionMath.IsFinite(_gravity)) && (SecondaryMotionMath.IsFinite(_stepSeconds))
                && (_stepSeconds >= MIN_STEP_SECONDS) && (_stepSeconds <= MAX_STEP_SECONDS)
                && (_maxSubsteps > 0) && (_maxSubsteps <= MAX_ALLOWED_SUBSTEPS)
                && (SecondaryMotionMath.IsFinite(_maxFrameSeconds)) && (_maxFrameSeconds >= _stepSeconds)
                && (SecondaryMotionMath.IsFinite(_teleportDistance)) && (_teleportDistance > 0f)
                && (SecondaryMotionMath.IsFinite(_teleportAngle)) && (_teleportAngle > 0f)
                && (_teleportAngle <= SecondaryMotionMath.MAX_ANGLE);
        }

        private bool IsInMotionHierarchy(Transform value)
        {
            return (value == _motionRoot) || (value.IsChildOf(_motionRoot));
        }

        private static bool HasFinitePose(Transform value)
        {
            Vector3 scale = value.lossyScale;
            return (SecondaryMotionMath.IsFinite(value.position)) && (SecondaryMotionMath.IsFinite(value.rotation))
                && (SecondaryMotionMath.IsFinite(value.localPosition)) && (SecondaryMotionMath.IsFinite(value.localRotation))
                && (SecondaryMotionMath.IsFinite(value.localScale)) && (SecondaryMotionMath.IsFinite(scale))
                && (Mathf.Abs(scale.x) > SecondaryMotionMath.LENGTH_EPSILON)
                && (Mathf.Abs(scale.y) > SecondaryMotionMath.LENGTH_EPSILON)
                && (Mathf.Abs(scale.z) > SecondaryMotionMath.LENGTH_EPSILON);
        }

        private static float GetMaximumScale(Vector3 scale)
        {
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }
    }
}
