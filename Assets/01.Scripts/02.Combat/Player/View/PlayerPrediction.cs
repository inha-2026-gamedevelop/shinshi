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

            if ((!_isFollowing) && (!isDodging))
            {
                TryBeginDeferredMove(now);
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
            _predictor.Reset(Time.unscaledTimeAsDouble);

            if (_networkTransform != null)
            {
                _networkTransform.enabled = false;
            }

            _input.MoveSent      += OnMoveSent;
            _input.DodgeSent     += OnDodgeSent;
            _input.ActionSent    += OnActionSent;
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
            _input.InputRejected -= OnInputRejected;
            _input.SkillResult   -= OnSkillResult;
        }

        private void OnMoveSent(uint sequence, Vector3 destination)
        {
            double now = Time.unscaledTimeAsDouble;

            // 서버는 기술이나 회피가 끝난 뒤 이 이동을 시작하므로 그때 시작한다
            if ((_isFollowing) || (now < _dodgeEndTime))
            {
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

        private void OnInputRejected(uint sequence, ERejectReason reason)
        {
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
