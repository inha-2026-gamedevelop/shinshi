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
        private const int BASE_LAYER      = 0;

        /// <summary> 활 든 팔이 몸 왼쪽을 향하는 궁수 사격 상태의 애니메이터 태그 </summary>
        public const string BOW_AIM_TAG = "BowAim";

        [SerializeField] private Animator          _animator;
        [SerializeField] private Transform         _movementRoot;
        [SerializeField] private Transform         _visualPivot;
        [SerializeField] private PlayerCombatState _combat;
        [SerializeField] private MonoBehaviour     _motionSourceComponent;
        [SerializeField] private string            _speedParameter     = "Speed";
        [SerializeField] private string            _aliveParameter     = string.Empty;
        [SerializeField] private float             _speedDampSeconds   = 0.1f;
        [SerializeField] private float             _maximumVisualSpeed = 12f;

        [Header("발 미끄러짐 보정")]
        [SerializeField] private string            _playbackScaleParameter   = "MoveAnimSpeed";
        [SerializeField] private float             _locomotionReferenceSpeed = 4.78f; // m/s, 달리기 클립의 원래 이동 속도
        [SerializeField] private float             _maximumPlaybackScale     = 1.6f;

        [Header("방향 전환")]
        [SerializeField] private float             _turnDegreesPerSecond     = 900f;
        [SerializeField] private float             _bowAimYawOffset          = 90f; // 도, 사격 상태에서 활 든 팔이 조준 방향을 향하게 모델을 돌리는 양

        private IPlayerFemaleCharacterMotionSource _motionSource;
        private MonoBehaviour                     _boundMotionSourceComponent;
        private RuntimeAnimatorController         _boundController;
        private PlayerPrediction                  _prediction;

        private int    _speedHash;
        private int    _aliveHash;
        private int    _playbackScaleHash;
        private bool   _hasPlaybackScale;
        private Quaternion _baseFacing = Quaternion.identity;

        private bool   _hasFacing;
        private bool   _hasActionBaseline;
        private byte   _attackBaseline;
        private byte   _dodgeBaseline;
        private int    _castBaseline;
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
            _hasFacing         = false;
            _hasActionBaseline = false;
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
            _isBound           = false;
            _hasFacing         = false;
            _hasActionBaseline = false;
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
            _playbackScaleHash = Animator.StringToHash(_playbackScaleParameter);
            _hasPlaybackScale  = false;
            _prediction        = (_combat != null) ? _combat.GetComponent<PlayerPrediction>() : null;
            bool hasSpeed = false;
            bool hasAlive = !_hasAliveParameter;
            AnimatorControllerParameter[] parameters = _animator.parameters;
            for (int i = 0; i < parameters.Length; ++i)
            {
                // 재생 배율은 선택 파라미터다. 컨트롤러에 없으면 배율 없이 기존처럼 표현한다
                if ((!string.IsNullOrWhiteSpace(_playbackScaleParameter)) && (parameters[i].nameHash == _playbackScaleHash)
                    && (parameters[i].type == AnimatorControllerParameterType.Float))
                {
                    _hasPlaybackScale = true;
                }

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
                SetPlaybackScale(0f);
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
                SetPlaybackScale(0f);
            }
            else
            {
                _lastVisualSpeed = Mathf.Min(speed, _maximumVisualSpeed);
                _animator.SetFloat(_speedHash, _lastVisualSpeed, _speedDampSeconds, unscaledDeltaSeconds);
                SetPlaybackScale(_animator.GetFloat(_speedHash));
            }

            if (_hasAliveParameter)
            {
                _animator.SetBool(_aliveHash, alive);
            }

            if (_combat != null)
            {
                UpdateFacing(alive, unscaledDeltaSeconds);
            }
        }

        // 서버 방향값은 왕복 지연 뒤에 오므로 소유자가 방향을 바꾸면 몸이 잠시 뒤로 걷는 것처럼 보인다.
        // 소유자는 예측 이동 방향을 먼저 따르고, 서 있거나 공격, 기술 중에는 서버 방향을 따른다.
        // 첫 프레임만 바로 맞추고 이후에는 초당 각도로 돌아 방향 전환이 튀지 않게 한다.
        // 공격, 회피, 기술은 서버가 같은 묶음에서 조준 방향을 정하고 모션이 바로 그 방향으로 쏘므로,
        // 돌아가는 동안 화살이 몸 옆에서 나가지 않게 새 행동이 시작되거나 기술 중이면 바로 맞춘다
        private void UpdateFacing(bool alive, float deltaSeconds)
        {
            bool snapToAction = ConsumeActionStart() || (_combat.SkillPhase != ESkillPhase.Idle);
            Vector3 facing = FacingRules.DecodeYaw(_combat.FacingYaw);
            if ((alive) && (!snapToAction) && (TryGetPredictedFacing(out Vector3 predicted)))
            {
                facing = predicted;
            }

            if (!CharacterLocomotionRules.TryGetHorizontal(facing, out Vector3 horizontal))
            {
                return;
            }

            // 회전 보간은 보정 전 방향으로 하고, 사격 자세 보정은 마지막에 얹어 보정이 회전 속도에 걸리지 않게 한다
            Quaternion aimOffset = Quaternion.AngleAxis(_bowAimYawOffset * ComputeBowAimWeight(), Vector3.up);
            if ((!_hasFacing) || (snapToAction))
            {
                _baseFacing = Quaternion.LookRotation(horizontal, Vector3.up);
                _hasFacing  = true;
            }
            else
            {
                _baseFacing = CharacterLocomotionRules.TurnTowards(_baseFacing, horizontal, _turnDegreesPerSecond, deltaSeconds);
            }

            _visualPivot.rotation = _baseFacing * aimOffset;
        }

        // 애니메이터가 사격 상태로 섞이는 비율만큼만 돌려 전환 중에 몸이 튀지 않게 한다
        private float ComputeBowAimWeight()
        {
            AnimatorStateInfo current = _animator.GetCurrentAnimatorStateInfo(BASE_LAYER);
            bool currentAims = current.IsTag(BOW_AIM_TAG);
            if (!_animator.IsInTransition(BASE_LAYER))
            {
                return currentAims ? 1f : 0f;
            }

            bool  nextAims = _animator.GetNextAnimatorStateInfo(BASE_LAYER).IsTag(BOW_AIM_TAG);
            float progress = Mathf.Clamp01(_animator.GetAnimatorTransitionInfo(BASE_LAYER).normalizedTime);
            float from     = currentAims ? 1f : 0f;
            float to       = nextAims ? 1f : 0f;
            return Mathf.Lerp(from, to, progress);
        }

        // 처음 본 값은 기준으로만 잡아 연결 직후 지난 행동으로 몸을 돌리지 않는다
        private bool ConsumeActionStart()
        {
            byte attack = _combat.AttackSequence;
            byte dodge  = _combat.DodgeSequence;
            int  cast   = _combat.CastInstanceId;
            bool isNew  = (_hasActionBaseline)
                && ((attack != _attackBaseline) || (dodge != _dodgeBaseline) || (cast != _castBaseline));

            _attackBaseline    = attack;
            _dodgeBaseline     = dodge;
            _castBaseline      = cast;
            _hasActionBaseline = true;
            return isNew;
        }

        private bool TryGetPredictedFacing(out Vector3 direction)
        {
            direction = Vector3.zero;
            if ((_prediction == null) || (!_prediction.isActiveAndEnabled) || (!_combat.isOwned) || (_combat.isServer)
                || (!_prediction.IsPredicting) || (_combat.SkillPhase != ESkillPhase.Idle))
            {
                return false;
            }

            return CharacterLocomotionRules.TryGetHorizontal(_prediction.MoveDirection, out direction);
        }

        private void SetPlaybackScale(float visualSpeed)
        {
            if (!_hasPlaybackScale)
            {
                return;
            }

            _animator.SetFloat(_playbackScaleHash,
                CharacterLocomotionRules.ComputePlaybackScale(visualSpeed, _locomotionReferenceSpeed, _maximumPlaybackScale));
        }

        private bool FailBinding(string errorMessage, out string error)
        {
            error = errorMessage;
            _validationError = error;
            return false;
        }
    }
}
