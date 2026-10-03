// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 같은 개체의 Humanoid Animator IK 단계에서 발만 접지하며 이동 루트와 Collider를 쓰지 않는다 </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class PlayerFemaleCharacterFootIkView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   HIT_CAPACITY              = 16;
        private const float DEFAULT_EXTRA_SOLE_HEIGHT = 0.002f;

        [SerializeField] private Animator  _animator;
        [SerializeField] private Transform _characterRoot;
        [SerializeField] private LayerMask _groundMask          = Physics.DefaultRaycastLayers;
        [SerializeField] private int       _ikLayerIndex        = 0;
        [SerializeField] private float     _probeHeight         = 0.3f;
        [SerializeField] private float     _probeDepth          = 0.4f;
        [SerializeField] private float     _contactRange        = 0.08f;
        [SerializeField] private float     _maxRaise            = 0.15f;
        [SerializeField] private float     _maxDrop             = 0.08f;
        [SerializeField] private float     _minimumGroundDot    = 0.6f;
        [SerializeField] private float     _leftExtraSoleHeight  = DEFAULT_EXTRA_SOLE_HEIGHT;
        [SerializeField] private float     _rightExtraSoleHeight = DEFAULT_EXTRA_SOLE_HEIGHT;
        [SerializeField] private float     _positionWeight      = 1f;
        [SerializeField] private float     _rotationWeight      = 0f;

        private readonly RaycastHit[] _hits = new RaycastHit[HIT_CAPACITY];
        private bool   _groundingAllowed = true;
        private bool   _isBound;
        private string _validationError = string.Empty;
        private int    _ikEvaluationCount;
        private float  _lastLeftWeight;
        private float  _lastRightWeight;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsBound            => _isBound;
        public string ValidationError  => _validationError;
        public int IkEvaluationCount   => _ikEvaluationCount;
        public float LastLeftWeight    => _lastLeftWeight;
        public float LastRightWeight   => _lastRightWeight;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            // Animator 메시지를 받는 개체와 정확히 같아야 하므로 초기화 때 한 번만 로컬 참조를 보완한다
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

        private void OnAnimatorIK(int layerIndex)
        {
            if ((!_isBound) || (IsServerBuild) || (_animator == null) || (!_animator.isHuman)
                || (layerIndex != _ikLayerIndex))
            {
                return;
            }

            ++_ikEvaluationCount;
            _lastLeftWeight  = ApplyFoot(AvatarIKGoal.LeftFoot, _animator.leftFeetBottomHeight + _leftExtraSoleHeight);
            _lastRightWeight = ApplyFoot(AvatarIKGoal.RightFoot, _animator.rightFeetBottomHeight + _rightExtraSoleHeight);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> Animator와 게임플레이 캐릭터 루트를 연결해 자기 Collider를 접지 쿼리에서 제외한다 </summary>
        public bool Configure(Animator animator, Transform characterRoot, LayerMask groundMask,
            float leftExtraSoleHeight = DEFAULT_EXTRA_SOLE_HEIGHT, float rightExtraSoleHeight = DEFAULT_EXTRA_SOLE_HEIGHT)
        {
            _animator             = animator;
            _characterRoot        = characterRoot;
            _groundMask           = groundMask;
            _leftExtraSoleHeight   = leftExtraSoleHeight;
            _rightExtraSoleHeight  = rightExtraSoleHeight;
            return TryBind(out _validationError);
        }

        /// <summary> Avatar와 수치를 검사한다, IK Pass 활성 여부는 실제 평가 횟수로 별도 확인한다 </summary>
        public bool TryBind(out string error)
        {
            _isBound = false;
            if ((IsServerBuild) || (_animator == null) || (_animator.gameObject != gameObject)
                || (_animator.avatar == null) || (!_animator.avatar.isValid) || (!_animator.avatar.isHuman)
                || (_characterRoot == null) || ((_animator.transform != _characterRoot)
                    && (!_animator.transform.IsChildOf(_characterRoot))) || (!HasValidSettings()))
            {
                error = "같은 개체의 Humanoid Animator와 캐릭터 루트, 유효한 접지 수치가 필요하다";
                _validationError = error;
                return false;
            }

            _isBound = true;
            _ikEvaluationCount = 0;
            _lastLeftWeight  = 0f;
            _lastRightWeight = 0f;
            error = string.Empty;
            _validationError = error;
            return true;
        }

        /// <summary> 점프나 비접지 동작을 보여 줄 때 시각 접지를 끄고 다시 켠다 </summary>
        public void SetGroundingAllowed(bool allowed)
        {
            _groundingAllowed = allowed;
        }

        private float ApplyFoot(AvatarIKGoal goal, float soleHeight)
        {
            _animator.SetIKPositionWeight(goal, 0f);
            _animator.SetIKRotationWeight(goal, 0f);
            if (!_groundingAllowed)
            {
                return 0f;
            }

            Vector3 position = _animator.GetIKPosition(goal);
            Quaternion rotation = _animator.GetIKRotation(goal);
            Vector3 up = Vector3.up;
            Vector3 origin = position - up * soleHeight + up * _probeHeight;
            if (!TryFindGround(origin, up, out RaycastHit hit))
            {
                return 0f;
            }

            if (!PlayerFemaleCharacterFootIkMath.TryGetTarget(position, rotation, hit.point, hit.normal, up,
                soleHeight, _contactRange, _maxRaise, _maxDrop, _minimumGroundDot, out Vector3 targetPosition,
                out Quaternion targetRotation, out float contactWeight))
            {
                return 0f;
            }

            // 접지 목표는 Animator 평가마다 달라지므로 Tween 대신 IK 가중치로 접촉만 조정한다
            float weight = contactWeight * _positionWeight;
            _animator.SetIKPositionWeight(goal, weight);
            _animator.SetIKPosition(goal, targetPosition);
            _animator.SetIKRotationWeight(goal, contactWeight * _rotationWeight);
            _animator.SetIKRotation(goal, targetRotation);
            return weight;
        }

        private bool TryFindGround(Vector3 origin, Vector3 up, out RaycastHit hit)
        {
            hit = default;
            if (!SecondaryMotionMath.IsFinite(origin))
            {
                return false;
            }

            int count = Physics.RaycastNonAlloc(origin, -up, _hits, _probeHeight + _probeDepth,
                _groundMask, QueryTriggerInteraction.Ignore);
            // 버퍼가 포화되면 가장 가까운 지면을 확정할 수 없으므로 해당 평가에서는 접지를 생략한다
            if (count == _hits.Length)
            {
                return false;
            }

            float nearestDistance = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; ++i)
            {
                Collider collider = _hits[i].collider;
                if ((collider == null) || (collider.transform == _characterRoot)
                    || (collider.transform.IsChildOf(_characterRoot)) || (_hits[i].distance >= nearestDistance)
                    || (Vector3.Dot(_hits[i].normal, up) < _minimumGroundDot))
                {
                    continue;
                }

                hit = _hits[i];
                nearestDistance = hit.distance;
                found = true;
            }

            return found;
        }

        private bool HasValidSettings()
        {
            return (_ikLayerIndex >= 0) && (SecondaryMotionMath.IsFinite(_probeHeight)) && (_probeHeight > 0f)
                && (SecondaryMotionMath.IsFinite(_probeDepth)) && (_probeDepth > 0f)
                && (SecondaryMotionMath.IsFinite(_contactRange)) && (_contactRange > 0f)
                && (SecondaryMotionMath.IsFinite(_maxRaise)) && (_maxRaise >= 0f) && (_maxRaise <= _probeHeight)
                && (SecondaryMotionMath.IsFinite(_maxDrop)) && (_maxDrop >= 0f) && (_maxDrop <= _probeDepth)
                && (SecondaryMotionMath.IsFinite(_minimumGroundDot)) && (_minimumGroundDot >= 0f) && (_minimumGroundDot <= 1f)
                && (SecondaryMotionMath.IsFinite(_leftExtraSoleHeight)) && (_leftExtraSoleHeight >= 0f)
                && (SecondaryMotionMath.IsFinite(_rightExtraSoleHeight)) && (_rightExtraSoleHeight >= 0f)
                && (SecondaryMotionMath.IsFinite(_positionWeight)) && (_positionWeight >= 0f) && (_positionWeight <= 1f)
                && (SecondaryMotionMath.IsFinite(_rotationWeight)) && (_rotationWeight >= 0f) && (_rotationWeight <= 1f);
        }
    }
}
