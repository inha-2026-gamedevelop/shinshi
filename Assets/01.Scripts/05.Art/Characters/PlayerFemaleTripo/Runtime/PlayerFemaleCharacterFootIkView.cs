// System
using System.Collections.Generic;
using System.Text;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> authoring된 실제 발바닥을 평가하여 침투한 발만 접지하며 게임플레이 루트를 쓰지 않는다 </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class PlayerFemaleCharacterFootIkView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int HIT_CAPACITY                   = 16;
        private const float DEFAULT_EXTRA_SOLE_HEIGHT    = 0.002f;
        private const float WEIGHT_SUM_TOLERANCE          = 0.001f;
        private const float UNIT_SCALE_TOLERANCE          = 0.0001f;
        private const float MINIMUM_SKIN_CLASS_WEIGHT     = 0.5f;

        [SerializeField] private Animator _animator;
        [SerializeField] private Transform _characterRoot;
        [SerializeField] private SkinnedMeshRenderer _supportRenderer;
        [SerializeField] private PlayerFemaleCharacterFootSupportProfile _supportProfile;
        [SerializeField] private LayerMask _groundMask          = Physics.DefaultRaycastLayers;
        [SerializeField] private int _ikLayerIndex              = 0;
        [SerializeField] private float _probeHeight             = 0.3f;
        [SerializeField] private float _probeDepth              = 0.4f;
        [SerializeField] private float _maxRaise                = 0.15f;
        [SerializeField] private float _minimumGroundDot        = 0.6f;
        [SerializeField] private float _leftExtraSoleHeight     = DEFAULT_EXTRA_SOLE_HEIGHT;
        [SerializeField] private float _rightExtraSoleHeight    = DEFAULT_EXTRA_SOLE_HEIGHT;

        private readonly RaycastHit[] _hits = new RaycastHit[HIT_CAPACITY];
        private Transform[] _bones;
        private Matrix4x4[] _boneWorldMatrices;
        private Transform _leftFoot;
        private Transform _rightFoot;
        private RuntimeAnimatorController _boundController;
        private Avatar _boundAvatar;
        private PlayerFemaleCharacterFootSupportProfile.SupportPoint[] _leftPoints;
        private PlayerFemaleCharacterFootSupportProfile.SupportPoint[] _rightPoints;
        private PlayerFemaleCharacterFootSupportProfile.GoalMapping _leftMapping;
        private PlayerFemaleCharacterFootSupportProfile.GoalMapping _rightMapping;
        private bool _groundingAllowed = true;
        private bool _isBound;
        private string _validationError = string.Empty;
        private int _ikEvaluationCount;
        private float _lastLeftWeight;
        private float _lastRightWeight;
        private FootEvaluation _lastLeftEvaluation;
        private FootEvaluation _lastRightEvaluation;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsBound                    => _isBound;
        public string ValidationError          => _validationError;
        public int IkEvaluationCount           => _ikEvaluationCount;
        public float LastLeftWeight            => _lastLeftWeight;
        public float LastRightWeight           => _lastRightWeight;
        public FootEvaluation LastLeftEvaluation  => _lastLeftEvaluation;
        public FootEvaluation LastRightEvaluation => _lastRightEvaluation;

        /****************************************
        *                Types
        ****************************************/

        /// <summary> 평가 직전 FK와 발바닥, 실제 요청된 상승량을 검증 도구에 제공한다 </summary>
        public readonly struct FootEvaluation
        {
            public readonly Vector3 FkPosition;
            public readonly Quaternion FkRotation;
            public readonly Vector3 SupportPosition;
            public readonly int SupportVertex;
            public readonly float Raise;
            public readonly bool GroundFound;

            public FootEvaluation(Vector3 fkPosition, Quaternion fkRotation, Vector3 supportPosition,
                int supportVertex, float raise, bool groundFound)
            {
                FkPosition = fkPosition;
                FkRotation = fkRotation;
                SupportPosition = supportPosition;
                SupportVertex = supportVertex;
                Raise = raise;
                GroundFound = groundFound;
            }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            // Animator 메시지를 받는 같은 개체의 초기화 참조만 보완한다
            if (_animator == null)
            {
                TryGetComponent(out _animator);
            }

            if (_characterRoot == null)
            {
                _characterRoot = transform;
            }

            TryBind(out _validationError);
        }

        private void OnDisable()
        {
            _lastLeftWeight = 0f;
            _lastRightWeight = 0f;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if ((IsServerBuild) || (!isActiveAndEnabled) || (_animator == null) || (layerIndex != _ikLayerIndex))
            {
                return;
            }

            _animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
            _animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
            _animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
            _lastLeftWeight = 0f;
            _lastRightWeight = 0f;
            _lastLeftEvaluation = default;
            _lastRightEvaluation = default;
            if ((!_isBound) || (!_animator.isHuman) || (_animator.avatar != _boundAvatar)
                || (_animator.runtimeAnimatorController != _boundController) || (_animator.applyRootMotion)
                || (_supportRenderer == null) || (_supportProfile == null)
                || (_supportRenderer.sharedMesh != _supportProfile.SourceMesh)
                || (!IsUnitScale(_animator.transform.lossyScale)))
            {
                _isBound = false;
                return;
            }

            ++_ikEvaluationCount;
            if (!_groundingAllowed)
            {
                return;
            }

            for (int i = 0; i < _bones.Length; ++i)
            {
                if (_bones[i] == null)
                {
                    _isBound = false;
                    return;
                }

                _boneWorldMatrices[i] = _bones[i].localToWorldMatrix;
            }

            _lastLeftWeight = ApplyFoot(AvatarIKGoal.LeftFoot, _leftFoot, _leftPoints,
                in _leftMapping, _leftExtraSoleHeight, out _lastLeftEvaluation);
            _lastRightWeight = ApplyFoot(AvatarIKGoal.RightFoot, _rightFoot, _rightPoints,
                in _rightMapping, _rightExtraSoleHeight, out _lastRightEvaluation);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 기존 호출은 유지하며 authoring된 profile 참조가 없으면 실행을 거절한다 </summary>
        public bool Configure(Animator animator, Transform characterRoot, LayerMask groundMask,
            float leftExtraSoleHeight = DEFAULT_EXTRA_SOLE_HEIGHT, float rightExtraSoleHeight = DEFAULT_EXTRA_SOLE_HEIGHT)
        {
            _animator = animator;
            _characterRoot = characterRoot;
            _groundMask = groundMask;
            _leftExtraSoleHeight = leftExtraSoleHeight;
            _rightExtraSoleHeight = rightExtraSoleHeight;
            return TryBind(out _validationError);
        }

        /// <summary> 같은 Avatar와 원본 High renderer의 발바닥 profile을 명시적으로 연결한다 </summary>
        public bool Configure(Animator animator, Transform characterRoot, LayerMask groundMask,
            SkinnedMeshRenderer supportRenderer, PlayerFemaleCharacterFootSupportProfile supportProfile,
            float leftExtraSoleHeight = DEFAULT_EXTRA_SOLE_HEIGHT, float rightExtraSoleHeight = DEFAULT_EXTRA_SOLE_HEIGHT)
        {
            _supportRenderer = supportRenderer;
            _supportProfile = supportProfile;
            return Configure(animator, characterRoot, groundMask, leftExtraSoleHeight, rightExtraSoleHeight);
        }

        /// <summary> 모델과 Avatar, bone 순서와 모든 양수 skin 영향도를 한 번 검사하고 캐시한다 </summary>
        public bool TryBind(out string error)
        {
            _isBound = false;
            _lastLeftWeight = 0f;
            _lastRightWeight = 0f;
            if (IsServerBuild || (_animator == null) || (_animator.gameObject != gameObject)
                || (_animator.avatar == null) || (!_animator.avatar.isValid) || (!_animator.avatar.isHuman)
                || (_animator.applyRootMotion) || (_animator.runtimeAnimatorController == null)
                || (_characterRoot == null) || ((_animator.transform != _characterRoot)
                    && (!_animator.transform.IsChildOf(_characterRoot)))
                || (_supportRenderer == null) || (_supportProfile == null)
                || (!_supportRenderer.transform.IsChildOf(_animator.transform))
                || (_supportRenderer.sharedMesh == null) || (_supportRenderer.sharedMesh != _supportProfile.SourceMesh)
                || (_supportProfile.Avatar != _animator.avatar)
                || (_supportRenderer.sharedMesh.vertexCount != _supportProfile.SourceVertexCount)
                || (string.IsNullOrEmpty(_supportProfile.SourceAssetHash))
                || (string.IsNullOrEmpty(_supportProfile.SourceMetadataHash))
                || (!IsUnitScale(_animator.transform.lossyScale)) || (!HasValidSettings()))
            {
                return RejectBinding("같은 모델과 Avatar의 발바닥 profile, 유효한 Animator와 접지 수치가 필요하다", out error);
            }

            Transform[] bones = _supportRenderer.bones;
            if ((bones.Length == 0) || (bones.Length > PlayerFemaleCharacterFootSupportProfile.MAX_BONES)
                || (bones.Length != _supportProfile.BoneCount))
            {
                return RejectBinding("발바닥 profile의 bone 순서와 개수가 다르다", out error);
            }

            for (int i = 0; i < bones.Length; ++i)
            {
                if ((bones[i] == null) || (!bones[i].IsChildOf(_animator.transform))
                    || (!IsUnitScale(bones[i].localScale))
                    || (BuildRelativePath(bones[i], _animator.transform) != _supportProfile.GetBonePath(i)))
                {
                    return RejectBinding("발바닥 profile의 bone 경로나 크기가 다르다", out error);
                }
            }

            Transform leftFoot = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform leftToe = _animator.GetBoneTransform(HumanBodyBones.LeftToes);
            Transform rightToe = _animator.GetBoneTransform(HumanBodyBones.RightToes);
            if ((leftFoot == null) || (rightFoot == null) || (leftToe == null) || (rightToe == null)
                || (!ValidatePoints(_supportProfile.LeftPoints, bones, leftFoot, leftToe))
                || (!ValidatePoints(_supportProfile.RightPoints, bones, rightFoot, rightToe))
                || (!PlayerFemaleCharacterFootSupportMath.TryMapGoal(leftFoot.position, leftFoot.rotation,
                    _supportProfile.LeftMapping, out _, out _))
                || (!PlayerFemaleCharacterFootSupportMath.TryMapGoal(rightFoot.position, rightFoot.rotation,
                    _supportProfile.RightMapping, out _, out _)))
            {
                return RejectBinding("발바닥 profile의 양수 skin 데이터나 리그 목표 좌표가 유효하지 않다", out error);
            }

            _bones = bones;
            _boneWorldMatrices = new Matrix4x4[bones.Length];
            _leftFoot = leftFoot;
            _rightFoot = rightFoot;
            _leftPoints = _supportProfile.LeftPoints;
            _rightPoints = _supportProfile.RightPoints;
            _leftMapping = _supportProfile.LeftMapping;
            _rightMapping = _supportProfile.RightMapping;
            _boundAvatar = _animator.avatar;
            _boundController = _animator.runtimeAnimatorController;
            _ikEvaluationCount = 0;
            _isBound = true;
            error = string.Empty;
            _validationError = error;
            return true;
        }

        /// <summary> 점프와 비접지 동작에서는 시각 보정을 끈다 </summary>
        public void SetGroundingAllowed(bool allowed)
        {
            _groundingAllowed = allowed;
        }

        private float ApplyFoot(AvatarIKGoal goal, Transform foot,
            PlayerFemaleCharacterFootSupportProfile.SupportPoint[] points,
            in PlayerFemaleCharacterFootSupportProfile.GoalMapping mapping, float extraHeight,
            out FootEvaluation evaluation)
        {
            evaluation = default;
            if ((foot == null) || (!TryGetLowestSupport(points, out Vector3 support, out int sourceVertex)))
            {
                return 0f;
            }

            Vector3 fkPosition = foot.position;
            Quaternion fkRotation = foot.rotation;
            evaluation = new FootEvaluation(fkPosition, fkRotation, support, sourceVertex, 0f, false);
            if (!_groundingAllowed)
            {
                return 0f;
            }

            if ((!TryFindGround(support + Vector3.up * _probeHeight, out RaycastHit hit))
                || (!PlayerFemaleCharacterFootSupportMath.TryGetRaise(support, hit.point, hit.normal,
                    extraHeight, _maxRaise, _minimumGroundDot, out float raise))
                || (!PlayerFemaleCharacterFootSupportMath.TryMapGoal(fkPosition, fkRotation,
                    in mapping, out Vector3 goalPosition, out Quaternion goalRotation)))
            {
                return 0f;
            }

            evaluation = new FootEvaluation(fkPosition, fkRotation, support, sourceVertex, raise, true);
            // 위치만 보정하면 Humanoid solve가 발 회전을 바꾸므로 FK 방향도 같은 목표 좌표로 보존한다
            _animator.SetIKPositionWeight(goal, 1f);
            _animator.SetIKRotationWeight(goal, 1f);
            _animator.SetIKPosition(goal, goalPosition + Vector3.up * raise);
            _animator.SetIKRotation(goal, goalRotation);
            return 1f;
        }

        private bool TryGetLowestSupport(PlayerFemaleCharacterFootSupportProfile.SupportPoint[] points,
            out Vector3 support, out int sourceVertex)
        {
            support = default;
            sourceVertex = -1;
            float lowest = float.PositiveInfinity;
            for (int i = 0; i < points.Length; ++i)
            {
                ref PlayerFemaleCharacterFootSupportProfile.SupportPoint point = ref points[i];
                Vector3 world = Vector3.zero;
                for (int j = 0; j < point.InfluenceCount; ++j)
                {
                    PlayerFemaleCharacterFootSupportProfile.Influence influence = point.GetInfluence(j);
                    world += _boneWorldMatrices[influence.BoneIndex].MultiplyPoint3x4(influence.BoneLocalPoint) * influence.Weight;
                }

                if (!SecondaryMotionMath.IsFinite(world))
                {
                    return false;
                }

                if (world.y < lowest)
                {
                    lowest = world.y;
                    support = world;
                    sourceVertex = point.SourceVertex;
                }
            }

            return sourceVertex >= 0;
        }

        private bool TryFindGround(Vector3 origin, out RaycastHit hit)
        {
            hit = default;
            if (!SecondaryMotionMath.IsFinite(origin))
            {
                return false;
            }

            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, _probeHeight + _probeDepth,
                _groundMask, QueryTriggerInteraction.Ignore);
            // 포화된 버퍼에서는 가장 가까운 유효 지면을 확정할 수 없다
            if (count == _hits.Length)
            {
                return false;
            }

            float nearest = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; ++i)
            {
                Collider collider = _hits[i].collider;
                if ((collider == null) || (collider.transform == _characterRoot)
                    || (collider.transform.IsChildOf(_characterRoot)) || (!SecondaryMotionMath.IsFinite(_hits[i].point))
                    || (!SecondaryMotionMath.IsFinite(_hits[i].normal)) || (!SecondaryMotionMath.IsFinite(_hits[i].distance))
                    || (_hits[i].distance >= nearest) || (Vector3.Dot(_hits[i].normal, Vector3.up) < _minimumGroundDot))
                {
                    continue;
                }

                hit = _hits[i];
                nearest = hit.distance;
                found = true;
            }

            return found;
        }

        private bool ValidatePoints(PlayerFemaleCharacterFootSupportProfile.SupportPoint[] points,
            Transform[] bones, Transform foot, Transform toe)
        {
            if ((points == null) || (points.Length == 0)
                || (points.Length > PlayerFemaleCharacterFootSupportProfile.MAX_SUPPORTS))
            {
                return false;
            }

            var vertices = new HashSet<int>(points.Length);
            for (int i = 0; i < points.Length; ++i)
            {
                ref PlayerFemaleCharacterFootSupportProfile.SupportPoint point = ref points[i];
                if ((point.SourceVertex < 0) || (point.SourceVertex >= _supportProfile.SourceVertexCount)
                    || (!vertices.Add(point.SourceVertex)) || (point.InfluenceCount <= 0)
                    || (point.InfluenceCount > PlayerFemaleCharacterFootSupportProfile.MAX_INFLUENCES))
                {
                    return false;
                }

                float total = 0f;
                float classWeight = 0f;
                for (int j = 0; j < point.InfluenceCount; ++j)
                {
                    PlayerFemaleCharacterFootSupportProfile.Influence influence = point.GetInfluence(j);
                    if ((influence.BoneIndex < 0) || (influence.BoneIndex >= bones.Length)
                        || (!SecondaryMotionMath.IsFinite(influence.Weight)) || (influence.Weight <= 0f)
                        || (!SecondaryMotionMath.IsFinite(influence.BoneLocalPoint)))
                    {
                        return false;
                    }

                    for (int k = 0; k < j; ++k)
                    {
                        if (point.GetInfluence(k).BoneIndex == influence.BoneIndex)
                        {
                            return false;
                        }
                    }

                    total += influence.Weight;
                    if ((bones[influence.BoneIndex] == foot) || (bones[influence.BoneIndex] == toe))
                    {
                        classWeight += influence.Weight;
                    }
                }

                if ((Mathf.Abs(total - 1f) > WEIGHT_SUM_TOLERANCE) || (classWeight < MINIMUM_SKIN_CLASS_WEIGHT))
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasValidSettings()
        {
            return (_ikLayerIndex >= 0) && (_ikLayerIndex < _animator.layerCount)
                && (_groundMask.value != 0) && SecondaryMotionMath.IsFinite(_probeHeight) && (_probeHeight > 0f)
                && SecondaryMotionMath.IsFinite(_probeDepth) && (_probeDepth > 0f)
                && SecondaryMotionMath.IsFinite(_maxRaise) && (_maxRaise > 0f) && (_maxRaise <= _probeHeight)
                && SecondaryMotionMath.IsFinite(_minimumGroundDot) && (_minimumGroundDot >= 0f) && (_minimumGroundDot <= 1f)
                && SecondaryMotionMath.IsFinite(_leftExtraSoleHeight) && (_leftExtraSoleHeight >= 0f)
                && SecondaryMotionMath.IsFinite(_rightExtraSoleHeight) && (_rightExtraSoleHeight >= 0f);
        }

        private bool RejectBinding(string message, out string error)
        {
            error = message;
            _validationError = error;
            return false;
        }

        private static bool IsUnitScale(Vector3 scale)
        {
            return SecondaryMotionMath.IsFinite(scale) && (Mathf.Abs(scale.x - 1f) <= UNIT_SCALE_TOLERANCE)
                && (Mathf.Abs(scale.y - 1f) <= UNIT_SCALE_TOLERANCE) && (Mathf.Abs(scale.z - 1f) <= UNIT_SCALE_TOLERANCE);
        }

        private static string BuildRelativePath(Transform bone, Transform root)
        {
            var builder = new StringBuilder(bone.name);
            Transform parent = bone.parent;
            while ((parent != null) && (parent != root))
            {
                builder.Insert(0, parent.name + "/");
                parent = parent.parent;
            }

            return builder.ToString();
        }
    }
}
