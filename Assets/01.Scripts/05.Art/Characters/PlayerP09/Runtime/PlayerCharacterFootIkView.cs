// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Combat;

namespace Shinshi.Art
{
    // 게임플레이 루트는 NavMesh 높이를 따라 계단을 경사로처럼 지나간다. 표현만 실제 계단 면에 발을 붙이고,
    // 낮은 쪽 발이 닿도록 골반을 내린다. 애니메이션이 든 발 높이는 그대로 더해 걷기 동작을 지우지 않는다
    /// <summary> P09 휴머노이드 발을 실제 지면에 맞추는 표현 전용 발 IK, 게임플레이 루트와 판정은 바꾸지 않는다 </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class PlayerCharacterFootIkView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    HIT_CAPACITY   = 8;
        private const int    BASE_LAYER     = 0;
        private const float  MIN_WEIGHT     = 0.001f;
        private const string DEFAULT_LAYER  = "Default";
        private const string OBSTACLE_LAYER = "Obstacle";

        [SerializeField] private LayerMask _groundMask;
        [SerializeField] private float     _probeUp          = 0.5f;  // m, 루트 위에서 쏘기 시작하는 높이
        [SerializeField] private float     _probeDown        = 0.6f;  // m, 루트 아래로 찾는 깊이
        [SerializeField] private float     _rootAboveFloor   = 0.05f; // m, NavMesh 면이 실제 바닥보다 떠 있는 높이
        [SerializeField] private float     _maxRaise         = 0.35f; // m, 계단 한 칸보다 조금 높게
        [SerializeField] private float     _maxDrop          = 0.35f;
        [SerializeField] private float     _minimumGroundDot = 0.6f;
        [SerializeField] private float     _rotationWeight   = 0.7f;
        [SerializeField] private float     _footSharpness    = 18f;
        [SerializeField] private float     _pelvisSharpness  = 10f;
        [SerializeField] private float     _weightSharpness  = 8f;

        private readonly RaycastHit[] _hits = new RaycastHit[HIT_CAPACITY];

        private Animator          _animator;
        private Transform         _movementRoot;
        private PlayerCombatState _combat;

        private float   _weight;
        private float   _leftOffset;
        private float   _rightOffset;
        private float   _pelvisOffset;
        private Vector3 _leftNormal  = Vector3.up;
        private Vector3 _rightNormal = Vector3.up;

        /****************************************
        *              Properties
        ****************************************/

        public float PelvisOffset => _pelvisOffset;
        public float LeftOffset   => _leftOffset;
        public float RightOffset  => _rightOffset;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            TryGetComponent(out _animator);
            if (_groundMask.value == 0)
            {
                _groundMask = LayerMask.GetMask(DEFAULT_LAYER, OBSTACLE_LAYER);
            }
        }

        private void OnDisable()
        {
            ResetOffsets();
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if ((layerIndex != BASE_LAYER) || (_animator == null) || (!_animator.isHuman) || (_movementRoot == null))
            {
                return;
            }

            float deltaSeconds = Time.deltaTime;
            bool  alive        = (_combat == null) || (_combat.LifeState == EParticipantLifeState.Alive);
            _weight = CharacterLocomotionRules.Smooth(_weight, alive ? 1f : 0f, _weightSharpness, deltaSeconds);
            if (_weight < MIN_WEIGHT)
            {
                _weight = 0f;
                ResetOffsets();
                return;
            }

            // 루트는 NavMesh 높이라 평지에서도 실제 바닥보다 조금 떠 있다. 그만큼 뺀 높이를 기준으로 삼아 평지에서는 오프셋이 0이 된다
            float   rootHeight  = _movementRoot.position.y;
            float   floorHeight = rootHeight - _rootAboveFloor;
            Vector3 leftFk      = _animator.GetIKPosition(AvatarIKGoal.LeftFoot);
            Vector3 rightFk     = _animator.GetIKPosition(AvatarIKGoal.RightFoot);

            ProbeGround(leftFk, rootHeight, floorHeight, out float leftTarget, out Vector3 leftNormal);
            ProbeGround(rightFk, rootHeight, floorHeight, out float rightTarget, out Vector3 rightNormal);

            float normalBlend = 1f - Mathf.Exp(-_footSharpness * deltaSeconds);
            _leftOffset   = CharacterLocomotionRules.Smooth(_leftOffset, leftTarget, _footSharpness, deltaSeconds);
            _rightOffset  = CharacterLocomotionRules.Smooth(_rightOffset, rightTarget, _footSharpness, deltaSeconds);
            _leftNormal   = Vector3.Slerp(_leftNormal, leftNormal, normalBlend);
            _rightNormal  = Vector3.Slerp(_rightNormal, rightNormal, normalBlend);
            float pelvis  = CharacterLocomotionRules.ComputePelvisOffset(_leftOffset, _rightOffset);
            _pelvisOffset = CharacterLocomotionRules.Smooth(_pelvisOffset, pelvis, _pelvisSharpness, deltaSeconds);

            _animator.bodyPosition += Vector3.up * (_pelvisOffset * _weight);
            ApplyFoot(AvatarIKGoal.LeftFoot, leftFk, _leftOffset, _leftNormal);
            ApplyFoot(AvatarIKGoal.RightFoot, rightFk, _rightOffset, _rightNormal);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 같은 플레이어의 게임플레이 루트와 생사 상태를 연결한다, 이 루트 아래 콜라이더는 지면으로 보지 않는다 </summary>
        public void Configure(Transform movementRoot, PlayerCombatState combat)
        {
            _movementRoot = movementRoot;
            _combat       = combat;
            _weight       = 0f;
            ResetOffsets();
        }

        private void ApplyFoot(AvatarIKGoal goal, Vector3 fkPosition, float offset, Vector3 normal)
        {
            Quaternion fkRotation = _animator.GetIKRotation(goal);
            _animator.SetIKPosition(goal, fkPosition + (Vector3.up * offset));
            _animator.SetIKRotation(goal, Quaternion.FromToRotation(Vector3.up, normal) * fkRotation);
            _animator.SetIKPositionWeight(goal, _weight);
            _animator.SetIKRotationWeight(goal, _weight * _rotationWeight);
        }

        // 발 바로 위에서 아래로 쏴 가장 가까운 정적 지면을 찾는다. 자기 몸과 다른 네트워크 개체(플레이어, 적)는 밟지 않고,
        // 계단 옆 난간이나 벽 윗면처럼 한 칸보다 높은 면은 건너뛰어 그 아래 계단 면을 쓴다
        private void ProbeGround(Vector3 footPosition, float rootHeight, float floorHeight, out float offset,
            out Vector3 normal)
        {
            offset = 0f;
            normal = Vector3.up;
            Vector3 origin = new Vector3(footPosition.x, rootHeight + _probeUp, footPosition.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _hits, _probeUp + _probeDown, _groundMask,
                QueryTriggerInteraction.Ignore);

            float bestDistance = float.PositiveInfinity;
            int   bestIndex    = -1;
            for (int i = 0; i < count; ++i)
            {
                Collider hitCollider = _hits[i].collider;
                if ((hitCollider == null) || (_hits[i].distance >= bestDistance)
                    || (!CharacterLocomotionRules.IsReachableGround(_hits[i].point.y, floorHeight, _maxRaise))
                    || (hitCollider.transform.IsChildOf(_movementRoot))
                    || (hitCollider.GetComponentInParent<NetworkIdentity>() != null))
                {
                    continue;
                }

                bestDistance = _hits[i].distance;
                bestIndex    = i;
            }

            if (bestIndex < 0)
            {
                return;
            }

            offset = CharacterLocomotionRules.ComputeFootOffset(_hits[bestIndex].point.y, floorHeight, _maxRaise, _maxDrop);
            if (Vector3.Dot(_hits[bestIndex].normal, Vector3.up) >= _minimumGroundDot)
            {
                normal = _hits[bestIndex].normal;
            }
        }

        private void ResetOffsets()
        {
            _leftOffset   = 0f;
            _rightOffset  = 0f;
            _pelvisOffset = 0f;
            _leftNormal   = Vector3.up;
            _rightNormal  = Vector3.up;
        }
    }
}
