// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Combat;

namespace Shinshi.Art
{
    /// <summary> 역할별 이동 샘플과 전투 상태를 Animator와 시각 자식 방향으로 표현한다 </summary>
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
        [SerializeField] private Transform         _visualPivot;
        [SerializeField] private PlayerCombatState _combat;
        [SerializeField] private MonoBehaviour     _motionSourceComponent;
        [SerializeField] private string            _speedParameter     = "Speed";
        [SerializeField] private string            _aliveParameter     = string.Empty;
        [SerializeField] private float             _speedDampSeconds   = 0.1f;
        [SerializeField] private float             _maximumVisualSpeed = 12f;

        private IPlayerFemaleCharacterMotionSource _motionSource;
        private MonoBehaviour                     _boundMotionSourceComponent;
        private RuntimeAnimatorController         _boundController;

        private int    _speedHash;
        private int    _aliveHash;
        private bool   _hasAliveParameter;
        private bool   _hasMotionSample;
        private bool   _isBound;
        private float  _lastVisualSpeed;
        private string _validationError = string.Empty;

        /****************************************
        *              Properties
        ****************************************/

        public bool   IsBound          => _isBound;
        public bool   HasMotionSample  => _hasMotionSample;
        public float  LastVisualSpeed  => _lastVisualSpeed;
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

        private void OnDisable()
        {
            ResetMotionSample();
        }

        private void Update()
        {
            UpdatePresentation(Time.unscaledTimeAsDouble, Time.unscaledDeltaTime);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 시각 자식과 역할별 속도 소스를 연결한다, 소스가 없으면 속도 읽기 불가 상태로 표현한다 </summary>
        public bool Configure(Animator animator, Transform movementRoot, PlayerCombatState combat = null,
            MonoBehaviour motionSource = null, Transform visualPivot = null)
        {
            ResetMotionSample();
            _animator              = animator;
            _movementRoot          = movementRoot;
            _combat                = combat;
            _motionSourceComponent = motionSource;
            _visualPivot           = (visualPivot != null) ? visualPivot : (animator != null) ? animator.transform : null;
            return TryBind(out _validationError);
        }

        /// <summary> root motion과 게임플레이 루트 회전을 금지하고 실제 컨트롤러 파라미터를 캐시한다 </summary>
        public bool TryBind(out string error)
        {
            ResetMotionSample();
            _isBound = false;
            if ((_visualPivot == null) && (_animator != null))
            {
                _visualPivot = _animator.transform;
            }

            if ((IsServerBuild) || (_animator == null) || (_movementRoot == null) || (_visualPivot == null)
                || (string.IsNullOrWhiteSpace(_speedParameter)) || (_animator.runtimeAnimatorController == null)
                || (_animator.applyRootMotion) || (_animator.transform == _movementRoot)
                || (!_animator.transform.IsChildOf(_movementRoot)) || (_visualPivot == _movementRoot)
                || (!_visualPivot.IsChildOf(_movementRoot))
                || ((_visualPivot != _animator.transform) && (!_animator.transform.IsChildOf(_visualPivot)))
                || ((_combat != null) && (_combat.transform != _movementRoot))
                || (!SecondaryMotionMath.IsFinite(_speedDampSeconds)) || (_speedDampSeconds < 0f)
                || (!SecondaryMotionMath.IsFinite(_maximumVisualSpeed)) || (_maximumVisualSpeed <= 0f))
            {
                return FailBinding("root motion이 꺼진 시각 자식 Animator와 피벗, 같은 이동 루트, 유효한 시각 수치가 필요하다", out error);
            }

            _motionSource = (_motionSourceComponent != null) ? _motionSourceComponent as IPlayerFemaleCharacterMotionSource : null;
            _boundMotionSourceComponent = _motionSourceComponent;
            if ((_motionSourceComponent != null)
                && ((_motionSource == null) || (_motionSource.MovementRoot != _movementRoot)))
            {
                return FailBinding("속도 소스는 같은 이동 루트의 IPlayerFemaleCharacterMotionSource여야 한다", out error);
            }

            _speedHash         = Animator.StringToHash(_speedParameter);
            _aliveHash         = Animator.StringToHash(_aliveParameter);
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
                return FailBinding("Animator에 Speed Float와 지정한 생존 Bool 파라미터가 필요하다", out error);
            }

            _boundController = _animator.runtimeAnimatorController;
            _isBound = true;
            ResetMotionSample();
            _animator.SetFloat(_speedHash, 0f);
            error = string.Empty;
            _validationError = error;
            return true;
        }

        /// <summary> 재활성이나 소스 교체 뒤 첫 프레임에 누적 이동을 속도로 오인하지 않게 표본을 초기화한다 </summary>
        public void ResetMotionSample()
        {
            if ((_boundMotionSourceComponent != null) && (_motionSource != null))
            {
                _motionSource.ResetMotionSample();
            }

            _hasMotionSample = false;
            _lastVisualSpeed = 0f;
            if ((_isBound) && (_animator != null) && (_animator.runtimeAnimatorController == _boundController))
            {
                _animator.SetFloat(_speedHash, 0f);
            }
        }

        private void UpdatePresentation(double unscaledTime, float unscaledDeltaSeconds)
        {
            if ((!_isBound) || (IsServerBuild) || (!isActiveAndEnabled))
            {
                ResetMotionSample();
                return;
            }

            if ((_animator == null) || (_movementRoot == null) || (_visualPivot == null)
                || (_animator.runtimeAnimatorController != _boundController) || (_animator.applyRootMotion)
                || (_animator.transform == _movementRoot) || (!_animator.transform.IsChildOf(_movementRoot))
                || (_visualPivot == _movementRoot) || (!_visualPivot.IsChildOf(_movementRoot))
                || ((_visualPivot != _animator.transform) && (!_animator.transform.IsChildOf(_visualPivot)))
                || ((_combat != null) && (_combat.transform != _movementRoot))
                || (_motionSourceComponent != _boundMotionSourceComponent))
            {
                ResetMotionSample();
                _isBound = false;
                _validationError = "표현 참조나 Animator 설정이 바뀌어 다시 연결해야 한다";
                return;
            }

            bool alive = (_combat == null) || (_combat.LifeState == EParticipantLifeState.Alive);
            bool validTime = (!double.IsNaN(unscaledTime)) && (!double.IsInfinity(unscaledTime)) && (unscaledTime >= 0.0)
                && (SecondaryMotionMath.IsFinite(unscaledDeltaSeconds)) && (unscaledDeltaSeconds > 0f);
            float speed = 0f;
            bool canRead = (alive) && (validTime) && (_motionSourceComponent != null)
                && (_motionSourceComponent.isActiveAndEnabled) && (_motionSource != null)
                && (_motionSource.MovementRoot == _movementRoot);
            bool sampleRead = (canRead) && (_motionSource.TryRead(unscaledTime, out speed));
            _hasMotionSample = (sampleRead)
                && (SecondaryMotionMath.IsFinite(speed)) && (speed >= 0f);
            if (!_hasMotionSample)
            {
                if ((!canRead) || (sampleRead))
                {
                    ResetMotionSample();
                }

                _lastVisualSpeed = 0f;
                _animator.SetFloat(_speedHash, 0f);
            }
            else
            {
                _lastVisualSpeed = Mathf.Min(speed, _maximumVisualSpeed);
                _animator.SetFloat(_speedHash, _lastVisualSpeed, _speedDampSeconds, unscaledDeltaSeconds);
            }

            if (_hasAliveParameter)
            {
                _animator.SetBool(_aliveHash, alive);
            }

            if (_combat != null)
            {
                _visualPivot.rotation = Quaternion.LookRotation(FacingRules.DecodeYaw(_combat.FacingYaw), Vector3.up);
            }
        }

        private bool FailBinding(string errorMessage, out string error)
        {
            error = errorMessage;
            _validationError = error;
            return false;
        }
    }
}
