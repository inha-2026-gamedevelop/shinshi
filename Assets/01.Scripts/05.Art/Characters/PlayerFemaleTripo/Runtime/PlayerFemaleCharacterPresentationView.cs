// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Combat;

namespace Shinshi.Art
{
    /// <summary> 확정 또는 예측된 루트 이동과 읽기 전용 전투 상태를 Animator 파라미터로 표현한다 </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(PlayerFemaleCharacterPresentationView.EXECUTION_ORDER)]
    public sealed class PlayerFemaleCharacterPresentationView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int EXECUTION_ORDER = 100;

        [SerializeField] private Animator          _animator;
        [SerializeField] private Transform         _movementRoot;
        [SerializeField] private PlayerCombatState _combat;
        [SerializeField] private string            _speedParameter     = "Speed";
        [SerializeField] private string            _aliveParameter     = string.Empty;
        [SerializeField] private float             _speedDampSeconds   = 0.1f;
        [SerializeField] private float             _teleportDistance   = 1.5f;
        [SerializeField] private float             _maximumVisualSpeed = 12f;

        private Vector3 _previousPosition;
        private int     _speedHash;
        private int     _aliveHash;
        private bool    _hasAliveParameter;
        private bool    _isBound;
        private string  _validationError = string.Empty;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsBound            => _isBound;
        public string ValidationError  => _validationError;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            TryBind(out _validationError);
        }

        private void OnEnable()
        {
            ResetMotionSample();
        }

        private void Update()
        {
            if ((!_isBound) || (IsServerBuild) || (_animator == null) || (_movementRoot == null))
            {
                return;
            }

            bool alive = (_combat == null) || (_combat.LifeState == EParticipantLifeState.Alive);
            Vector3 position = _movementRoot.position;
            float speed = (alive) ? GetPlanarSpeed(_previousPosition, position, Time.deltaTime,
                _teleportDistance, _maximumVisualSpeed) : 0f;
            _previousPosition = position;
            _animator.SetFloat(_speedHash, speed, _speedDampSeconds, Time.deltaTime);
            if (_hasAliveParameter)
            {
                _animator.SetBool(_aliveHash, alive);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 네트워크나 예측 컴포넌트가 옮기는 루트를 읽고 시각 Animator만 연결한다 </summary>
        public bool Configure(Animator animator, Transform movementRoot, PlayerCombatState combat = null)
        {
            _animator     = animator;
            _movementRoot = movementRoot;
            _combat       = combat;
            return TryBind(out _validationError);
        }

        /// <summary> 이동 권위를 쓰지 않도록 root motion을 금지하고 실제 컨트롤러 파라미터를 캐시한다 </summary>
        public bool TryBind(out string error)
        {
            _isBound = false;
            if ((IsServerBuild) || (_animator == null) || (_movementRoot == null)
                || (string.IsNullOrWhiteSpace(_speedParameter))
                || (_animator.runtimeAnimatorController == null) || (_animator.applyRootMotion)
                || ((_animator.transform != _movementRoot) && (!_animator.transform.IsChildOf(_movementRoot)))
                || (!SecondaryMotionMath.IsFinite(_speedDampSeconds)) || (_speedDampSeconds < 0f)
                || (!SecondaryMotionMath.IsFinite(_teleportDistance)) || (_teleportDistance <= 0f)
                || (!SecondaryMotionMath.IsFinite(_maximumVisualSpeed)) || (_maximumVisualSpeed <= 0f))
            {
                error = "root motion이 꺼진 Animator와 그 상위 이동 루트, 유효한 시각 수치가 필요하다";
                _validationError = error;
                return false;
            }

            _speedHash = Animator.StringToHash(_speedParameter);
            _aliveHash = Animator.StringToHash(_aliveParameter);
            _hasAliveParameter = !string.IsNullOrWhiteSpace(_aliveParameter);
            bool hasSpeed = false;
            bool hasAlive = !_hasAliveParameter;
            AnimatorControllerParameter[] parameters = _animator.parameters;
            for (int i = 0; i < parameters.Length; ++i)
            {
                if ((parameters[i].nameHash == _speedHash) && (parameters[i].type == AnimatorControllerParameterType.Float))
                {
                    hasSpeed = true;
                }

                if ((parameters[i].nameHash == _aliveHash) && (parameters[i].type == AnimatorControllerParameterType.Bool))
                {
                    hasAlive = true;
                }
            }

            if ((!hasSpeed) || (!hasAlive))
            {
                error = "Animator에 Speed Float와 지정한 생존 Bool 파라미터가 필요하다";
                _validationError = error;
                return false;
            }

            _isBound = true;
            ResetMotionSample();
            error = string.Empty;
            _validationError = error;
            return true;
        }

        /// <summary> 재활성이나 순간이동 뒤 첫 프레임에 거짓 이동 속도가 생기지 않게 표본을 초기화한다 </summary>
        public void ResetMotionSample()
        {
            if (_movementRoot != null)
            {
                _previousPosition = _movementRoot.position;
            }
        }

        /// <summary> 수직 이동과 순간이동을 제외하고 초당 수평 이동량을 시각 속도 한도 안에서 계산한다 </summary>
        public static float GetPlanarSpeed(Vector3 previousPosition, Vector3 position, float deltaSeconds,
            float teleportDistance, float maximumVisualSpeed)
        {
            if ((!SecondaryMotionMath.IsFinite(previousPosition)) || (!SecondaryMotionMath.IsFinite(position))
                || (!SecondaryMotionMath.IsFinite(deltaSeconds)) || (deltaSeconds <= 0f)
                || (!SecondaryMotionMath.IsFinite(teleportDistance)) || (teleportDistance <= 0f)
                || (!SecondaryMotionMath.IsFinite(maximumVisualSpeed)) || (maximumVisualSpeed <= 0f))
            {
                return 0f;
            }

            Vector3 displacement = position - previousPosition;
            if (displacement.sqrMagnitude >= teleportDistance * teleportDistance)
            {
                return 0f;
            }

            displacement.y = 0f;
            float speed = displacement.magnitude / deltaSeconds;
            return (SecondaryMotionMath.IsFinite(speed)) ? Mathf.Min(speed, maximumVisualSpeed) : 0f;
        }
    }
}
