// Unity
using UnityEngine;
using UnityEngine.AI;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> 소유 클라이언트에서 클릭 이동과 회피를 바로 보여 주고 서버 확정 위치로 조금씩 맞춘다 </summary>
    [RequireComponent(typeof(PlayerInputState))]
    [RequireComponent(typeof(PlayerCombatState))]
    public sealed class PlayerPrediction : ClientView
    {
        // 소유 클라이언트의 위치는 이 컴포넌트만 옮긴다. NetworkTransform도 서버 위치를 보간해 같은 Transform을 밀면
        // 두 계층이 한 프레임에 번갈아 써서 떨린다. 그래서 소유권을 얻으면 NetworkTransform을 끄고, 원격 관찰자에게는
        // 그대로 둔다. 호스트(서버이자 클라이언트)는 서버가 직접 옮기므로 예측하지 않는다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PlayerInputState     _input;
        [SerializeField] private PlayerCombatState    _combat;
        [SerializeField] private NetworkTransformBase _networkTransform;

        private PlayerMovePredictor _predictor;
        private NavMeshPath         _navPath;

        private bool    _isActive             = false;
        private bool    _isFollowing          = false;
        private bool    _hasInterruptSequence = false;
        private uint    _interruptSequence    = 0;
        private bool    _hasDeferredMove      = false;
        private uint    _deferredSequence     = 0;
        private Vector3 _deferredDestination  = Vector3.zero;
        private double  _dodgeEndTime         = 0.0;
        private float   _dodgeSpeed           = 0f;

        // 서버는 이동으로 끊기지 않는 기본 공격의 준비부터 마지막 발사까지 이동을 보관했다가 다음 틱에 시작한다.
        // 예측도 그 구간 동안 서 있어야 서버와 같은 자리에서 같은 순번으로 출발한다. 서버 확정 전에는 왕복 시간을 더한
        // 넉넉한 구간으로 미리 막고, 공격 순번이 오면 서버 시작 시각으로 다시 잡는다
        private const double ATTACK_HOLD_MARGIN_SECONDS = 1.0 / Constants.Network.SERVER_TICK_RATE;

        // 왕복 지연 중 서버에서 클라이언트로 오는 쪽의 비율
        private const double ONE_WAY_LATENCY_RATIO      = 0.5;

        private byte    _attackBaseline         = 0;
        private byte    _cancelBaseline         = 0;
        private bool    _hasAttackInFlight      = false;
        private uint    _attackInFlightSequence = 0;
        private double  _provisionalHoldEndTime = 0.0;
        private double  _confirmedHoldEndTime   = 0.0;

        /****************************************
        *              Properties
        ****************************************/

        // 기술, 사망 구간에는 서버 위치를 따라가므로 예측 중이 아니다
        public bool    IsPredicting  => (_isActive) && (!_isFollowing);
        public Vector3 MoveDirection => (_predictor != null) ? _predictor.MoveDirection : Vector3.zero;

        // 개발 측정용. 예측으로 움직인 거리, 서버에 맞추려고 옮긴 거리, 스냅 횟수
        public float TravelledDistance => (_predictor != null) ? _predictor.TravelledDistance : 0f;
        public float CorrectedDistance => (_predictor != null) ? _predictor.CorrectedDistance : 0f;
        public int   SnapCount         => (_predictor != null) ? _predictor.SnapCount : 0;

        // 확정 공격과 아직 확정되지 않은 공격 요청 중 더 늦게 끝나는 쪽까지 이동을 미룬다
        private double AttackHoldEndTime => System.Math.Max(_provisionalHoldEndTime, _confirmedHoldEndTime);

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _predictor = new PlayerMovePredictor(Constants.Player.MAX_PATH_CORNERS, Constants.Player.PREDICTION_HISTORY_CAPACITY,
                Constants.Player.PREDICTION_HISTORY_INTERVAL_SECONDS);
            _navPath = new NavMeshPath();
        }

        private void OnDisable()
        {
            Deactivate();
        }

        private void Update()
        {
            if ((_input == null) || (_combat == null))
            {
                return;
            }

            // 소유권은 스폰 뒤에 붙고 재접속 때 바뀌므로 콜백 순서에 기대지 않고 매 프레임 본다
            bool shouldPredict = (_input.isOwned) && (!_input.isServer);
            if (shouldPredict != _isActive)
            {
                if (shouldPredict)
                {
                    Activate();
                }
                else
                {
                    Deactivate();
                }
            }

            if (!_isActive)
            {
                return;
            }

            double now       = Time.unscaledTimeAsDouble;
            float  deltaTime = Time.unscaledDeltaTime;
            bool   isDodging = now < _dodgeEndTime;

            // 회피는 기술 준비와 후딜을 끊는다. 서버가 기술을 끊었다는 상태가 오기 전에도 회피는 예측한다
            bool shouldFollow = (_combat.LifeState != EParticipantLifeState.Alive)
                || ((_combat.SkillPhase != ESkillPhase.Idle) && (!isDodging));
            if (shouldFollow != _isFollowing)
            {
                if (shouldFollow)
                {
                    EnterFollow();
                }
                else
                {
                    _isFollowing = false;
                }
            }

            TrackAttackHold(now);
            if ((!_isFollowing) && (!isDodging))
            {
                // 서버가 보관 없이 이미 움직였으면 이 공격 보관은 끝난 것이다
                if (HasServerStartedDeferredMove())
                {
                    ClearAttackHold();
                }

                if (now >= AttackHoldEndTime)
                {
                    TryBeginDeferredMove(now);
                }
            }

            PlayerMoveAuthority authority = _input.MoveAuthority;
            Vector3             position  = transform.position;

            if (_isFollowing)
            {
                position = _predictor.Follow(in authority, position, deltaTime);
                position = _predictor.Advance(position, 0f, now);
            }
            else
            {
                _predictor.SetRoundTripTime(NetworkTime.rtt);
                float speed = isDodging ? _dodgeSpeed : _input.MoveSpeed;
                position = _predictor.Advance(position, speed, now);
                position = _predictor.Reconcile(in authority, position, now, deltaTime);
            }

            transform.position = position;
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Activate()
        {
            _isActive             = true;
            _isFollowing          = false;
            _hasInterruptSequence = false;
            _hasDeferredMove      = false;
            _dodgeEndTime         = 0.0;
            _attackBaseline       = _combat.AttackSequence;
            _cancelBaseline       = _combat.ActionCancelSequence;
            ClearAttackHold();
            _predictor.Reset(Time.unscaledTimeAsDouble);

            if (_networkTransform != null)
            {
                _networkTransform.enabled = false;
            }

            _input.MoveSent      += OnMoveSent;
            _input.DodgeSent     += OnDodgeSent;
            _input.ActionSent    += OnActionSent;
            _input.AttackSent    += OnAttackSent;
            _input.InputRejected += OnInputRejected;
            _input.SkillResult   += OnSkillResult;
        }

        private void Deactivate()
        {
            if (!_isActive)
            {
                return;
            }

            _isActive = false;
            _predictor.Reset(Time.unscaledTimeAsDouble);

            // 다시 켜면 NetworkTransform이 버퍼를 비우고 서버 위치부터 보간한다
            if (_networkTransform != null)
            {
                _networkTransform.enabled = true;
            }

            if (_input == null)
            {
                return;
            }

            _input.MoveSent      -= OnMoveSent;
            _input.DodgeSent     -= OnDodgeSent;
            _input.ActionSent    -= OnActionSent;
            _input.AttackSent    -= OnAttackSent;
            _input.InputRejected -= OnInputRejected;
            _input.SkillResult   -= OnSkillResult;
        }

        private void OnMoveSent(uint sequence, Vector3 destination)
        {
            double now = Time.unscaledTimeAsDouble;

            // 서버는 기술, 회피, 기본 공격 준비가 끝난 뒤 이 이동을 시작하므로 그때 시작한다
            if ((_isFollowing) || (now < _dodgeEndTime) || (now < AttackHoldEndTime))
            {
                // 서버도 보관한 목적지와 같은 곳이면 앞 순번을 그대로 두므로 여기서도 바꾸지 않는다
                if ((_hasDeferredMove)
                    && ((destination - _deferredDestination).sqrMagnitude <= Constants.Player.MOVE_SAME_DESTINATION_EPSILON_SQR))
                {
                    return;
                }

                _hasDeferredMove     = true;
                _deferredSequence    = sequence;
                _deferredDestination = destination;
                return;
            }

            // 서버도 같은 목적지는 경로를 다시 만들지 않아 이동 순번이 바뀌지 않는다. 여기서도 기록을 바꾸지 않아야 짝이 맞는다
            if (_predictor.IsSameActiveDestination(destination))
            {
                return;
            }

            // 경로를 못 만들면 서버도 대개 거절하고 진행 중인 경로를 이어 간다. 기록을 바꾸지 않아야 그 경로와 계속 짝이 맞는다
            if (!MovePathRules.TryBeginPath(transform.position, destination, _navPath, _predictor.Path))
            {
                return;
            }

            _predictor.BeginMove(sequence, destination, now);
            _hasInterruptSequence = false;
        }

        // 적 몸체는 서버만 정확히 안다. 클라이언트는 벽까지만 보고 가고, 몸체에 막혀 모자란 거리는 보정이 당긴다
        private void OnDodgeSent(uint sequence, Vector3 direction)
        {
            if (_combat.LifeState != EParticipantLifeState.Alive)
            {
                return;
            }

            PlayerConfigSO config = GameDB.Player;
            if (!DodgeRules.TryGetWallClampedEnd(transform.position, direction, config.DodgeDistance, out Vector3 start,
                out Vector3 end))
            {
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            _predictor.Path.CornerBuffer[0] = start;
            _predictor.Path.CornerBuffer[1] = end;
            _predictor.Path.Begin(2);
            _predictor.BeginMove(sequence, end, now);

            _dodgeSpeed           = (config.DodgeDuration > 0f) ? (Vector3.Distance(start, end) / config.DodgeDuration) : 0f;
            _dodgeEndTime         = now + config.DodgeDuration;
            ClearAttackHold();
            _isFollowing          = false;
            _hasInterruptSequence = false;
            _hasDeferredMove      = false;
        }

        // 회피 중에 보낸 공격은 서버가 회피 뒤로 미루므로 회피 경로를 끊지 않는다
        private void OnActionSent(uint sequence)
        {
            if ((_isFollowing) || (!_predictor.IsMoving) || (Time.unscaledTimeAsDouble < _dodgeEndTime))
            {
                return;
            }

            _predictor.InterruptMove();
            _interruptSequence    = sequence;
            _hasInterruptSequence = true;
        }

        // 서버가 준비 중 이동을 보관하는 공격이면 확정이 올 때까지 왕복 시간을 더한 가장 긴 구간만큼 서 있는다
        private void OnAttackSent(uint sequence)
        {
            PlayerConfigSO.BasicAttackSpec spec = GameDB.Player.GetBasicAttack(_combat.PlayerClass);
            float hold = (spec != null) ? spec.GetMaxMoveHoldSeconds() : 0f;
            if (hold <= 0f)
            {
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            _hasAttackInFlight      = true;
            _attackInFlightSequence = sequence;
            _provisionalHoldEndTime = now + NetworkTime.rtt + hold + ATTACK_HOLD_MARGIN_SECONDS;
        }

        private void OnInputRejected(uint sequence, ERejectReason reason)
        {
            // 거절된 공격은 서버가 시작하지 않았다. 확정 공격의 보관은 그대로 두고 이 요청분만 푼다
            if ((_hasAttackInFlight) && (sequence == _attackInFlightSequence))
            {
                _hasAttackInFlight      = false;
                _provisionalHoldEndTime = 0.0;
            }

            // 미뤄 둔 이동이 거절되면 서버에 짝이 없으므로 나중에 시작하지 않는다
            if ((_hasDeferredMove) && (sequence == _deferredSequence))
            {
                _hasDeferredMove = false;
                return;
            }

            if ((_hasInterruptSequence) && (sequence == _interruptSequence))
            {
                ResumeInterruptedMove();
                return;
            }

            // 서버가 경로나 회피를 거절했다. 서버는 이전 경로를 이어 가거나 서 있으므로 멈춘 뒤에 위치를 맞춘다
            if (_predictor.IsRecord(sequence))
            {
                _predictor.ResolveRecord();
                _dodgeEndTime = 0.0;
            }
        }

        private void OnSkillResult(uint sequence, int castInstanceId, ERejectReason reason)
        {
            if ((reason == ERejectReason.None) || (!_hasInterruptSequence) || (sequence != _interruptSequence))
            {
                return;
            }

            ResumeInterruptedMove();
        }

        // 공격, 기술이 거절되면 서버는 멈추지 않고 원래 경로를 계속 간다
        private void ResumeInterruptedMove()
        {
            _hasInterruptSequence = false;
            if ((_isFollowing) || (!_predictor.WasInterrupted))
            {
                return;
            }

            _predictor.ClearInterrupt();
            MovePathRules.TryBeginPath(transform.position, _predictor.Destination, _navPath, _predictor.Path);
        }

        // 서버가 공격을 시작하면 공격 순번이 바뀐다. 서버 시작 시각과 그 단계의 보관 구간으로 막는 끝을 다시 잡는다.
        // 서버가 기절 등으로 준비를 끊으면 취소 순번이 바뀌므로 바로 풀어 준다
        private void TrackAttackHold(double now)
        {
            byte cancel = _combat.ActionCancelSequence;
            if (cancel != _cancelBaseline)
            {
                _cancelBaseline = cancel;
                ClearAttackHold();
            }

            byte sequence = _combat.AttackSequence;
            if (sequence == _attackBaseline)
            {
                return;
            }

            _attackBaseline = sequence;
            ClearAttackHold();

            PlayerConfigSO.BasicAttackSpec spec = GameDB.Player.GetBasicAttack(_combat.PlayerClass);
            float hold = (spec != null) ? spec.GetMoveHoldSeconds(_combat.AttackComboStep) : 0f;
            if (hold <= 0f)
            {
                return;
            }

            // NetworkTime.time은 보간 버퍼만큼 늦은 타임라인이다. 입력이 서버에 닿는 예측 시각에서 편도 지연을 빼 지금 서버 시각을 잡는다
            double serverNow = NetworkTime.predictedTime - (NetworkTime.rtt * ONE_WAY_LATENCY_RATIO);
            double serverEnd = _combat.AttackStartedAt + hold + ATTACK_HOLD_MARGIN_SECONDS;
            _confirmedHoldEndTime = now + (serverEnd - serverNow);
        }

        private void ClearAttackHold()
        {
            _hasAttackInFlight      = false;
            _provisionalHoldEndTime = 0.0;
            _confirmedHoldEndTime   = 0.0;
        }

        // 서버가 대기 공격을 이동으로 지우면 공격 순번도 거절도 오지 않는다. 미뤄 둔 이동을 서버가 이미 시작했다고
        // 알려 오면 기다리지 않고 바로 출발한다. 순번은 입력마다 늘어나므로 같거나 큰 순번이면 이미 지나간 것이다
        private bool HasServerStartedDeferredMove()
        {
            if (!_hasDeferredMove)
            {
                return false;
            }

            PlayerMoveAuthority authority = _input.MoveAuthority;
            return (authority.IsValid) && (authority.IsMoving) && (authority.MoveSequence >= _deferredSequence);
        }

        private void EnterFollow()
        {
            _isFollowing          = true;
            _hasInterruptSequence = false;
            _hasDeferredMove      = false;
            _dodgeEndTime         = 0.0;
            _predictor.ResolveRecord();
        }

        private void TryBeginDeferredMove(double now)
        {
            if (!_hasDeferredMove)
            {
                return;
            }

            _hasDeferredMove = false;
            if (MovePathRules.TryBeginPath(transform.position, _deferredDestination, _navPath, _predictor.Path))
            {
                _predictor.BeginMove(_deferredSequence, _deferredDestination, now);
            }
        }
    }
}
