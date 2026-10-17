// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 예고 사건(BossTelegraphStart의 시작 서버 시각과 예고 시간)으로 판정 시각을 알고, 그 시각에 클립의 힘 순간이 오도록 모션을 시작한다.
    // 시험 씬의 PatternMotionPreview와 P01DashSequencePreview가 하던 일을 서버 사건으로 바꾼 것이다. 애니메이션 완료를 판정 근거로 쓰지 않는다
    // 서버 사건에 패턴 번호가 없는 패턴(빔, 별, 격자, 반월 연쇄, 난사)은 이 클래스가 다루지 않는다
    /// <summary> 월백 패턴 모션을 서버 판정 시각에 맞춰 컨트롤러 상태로 재생하는 표현 도우미 </summary>
    public sealed class WolbaekMotionDirector
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float ENTER_BLEND_SECONDS  = 0.05f;
        private const float FINISH_BLEND_SECONDS = 0.25f;
        private const float DEATH_BLEND_SECONDS  = 0.1f;
        private const float LATE_SKIP_SECONDS    = 0.25f;
        private const int   NO_CAST              = -1;

        // 반월(이지 2P P03)은 구역마다 따로 예고가 오지만 몸 모션은 반월 시작 사건이 정한다
        private const string SWEEP_ZONE_PATTERN = "wolbaek_ph2_p03";

        /****************************************
        *                Fields
        ****************************************/

        private readonly Animator _animator;
        private readonly int      _idleHash;
        private readonly int      _deathHash;
        private readonly int      _appearHash;
        private readonly int      _deathStateId;
        private readonly int      _deathHoldHash;
        private readonly int      _deathHoldStateId;
        private readonly int      _appearStateId;

        private int                _castId = NO_CAST;
        private WolbaekMotionEntry _entry;
        private BossTelegraphCast  _cast;
        private float              _speed = 1f;
        private double             _startAt;
        private float              _clipTime;
        private bool               _isPlanned;
        private bool               _isStarted;
        private bool               _isDash;
        private bool               _isRecovering;
        private float              _dashPlaySpeed = 1f;
        private float              _recoverElapsed;
        private bool               _isDeathPlayed;
        private int                _entryHash;
        private bool               _hasEnteredClip;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 컨트롤러에 확정한 처치 상태가 있다, 없으면 임시 도형 모션을 쓴다 </summary>
        public bool HasDeathState => _animator.HasState(0, _deathStateId);

        public bool HasAppearState => _animator.HasState(0, _appearStateId);

        /****************************************
        *                Methods
        ****************************************/

        public WolbaekMotionDirector(Animator animator)
        {
            _animator   = animator;
            _idleHash   = Animator.StringToHash(WolbaekMotionCatalog.IDLE_STATE);
            _deathHash  = Animator.StringToHash(WolbaekMotionCatalog.DEATH_STATE);
            _appearHash = Animator.StringToHash(WolbaekMotionCatalog.APPEAR_STATE);

            // HasState는 레이어 이름을 앞에 붙인 전체 경로 해시를 받는다
            string layer   = animator.GetLayerName(0);
            _deathStateId  = Animator.StringToHash(layer + "." + WolbaekMotionCatalog.DEATH_STATE);
            _deathHoldHash    = Animator.StringToHash(WolbaekMotionCatalog.DEATH_HOLD_STATE);
            _deathHoldStateId = Animator.StringToHash(layer + "." + WolbaekMotionCatalog.DEATH_HOLD_STATE);
            _appearStateId = Animator.StringToHash(layer + "." + WolbaekMotionCatalog.APPEAR_STATE);
        }

        /// <summary> 이 시전을 확정한 모션이 맡는다 </summary>
        public static bool Handles(string patternId)
        {
            return WolbaekMotionCatalog.IsDash(patternId) || WolbaekMotionCatalog.TryGet(patternId, out _);
        }

        /// <summary> 이 패턴의 몸 동작은 확정한 모션이 정한다, 임시 도형 자세를 덧붙이지 않는다. 사건 종류로 모션을 정하는 반월 구역 예고도 포함한다 </summary>
        public static bool OwnsBody(string patternId)
        {
            return (Handles(patternId)) || (patternId == SWEEP_ZONE_PATTERN);
        }

        /// <summary> 예고 시작과 발동 알림을 받는다. 예고가 시작되면 판정 시각에 맞춘 모션을 계획하고, 돌진은 발동을 받은 뒤에 시작한다 </summary>
        public void OnCastPhase(BossTelegraphCast cast, EBossTelegraphPhase phase, double serverNow)
        {
            if ((cast == null) || (_isDeathPlayed))
            {
                return;
            }

            // 재접속 복원은 발동 단계 스냅샷만 와서 예고 알림 없이 발동이 먼저 온다. 계획이 없으면 이때 세운다
            if ((phase == EBossTelegraphPhase.Telegraph) || ((phase == EBossTelegraphPhase.Execute) && (_castId != cast.CastId)))
            {
                Plan(cast, serverNow);
            }

            if ((phase == EBossTelegraphPhase.Execute) && (_castId == cast.CastId) && _isDash)
            {
                BeginDash(cast, serverNow);
            }
        }

        /// <summary> 패턴 번호 없이 사건 종류로 정하는 패턴의 모션을 계획한다. strikeSeconds는 시전 시작에서 첫 판정까지의 서버 시간이다 </summary>
        public void PlanEvent(int castId, EWolbaekEventMotion kind, double castStartServerTime, float strikeSeconds)
        {
            if (_isDeathPlayed || (_castId == castId) || (!WolbaekMotionCatalog.TryGetEvent(kind, out WolbaekMotionEntry entry)))
            {
                return;
            }

            Reset();
            _entry     = entry;
            _entryHash = Animator.StringToHash(entry.StateName);
            _castId    = castId;
            _isPlanned = true;

            WolbaekMotionPlan.ResolveStart(strikeSeconds, in _entry, out _speed, out float delay);
            _startAt = castStartServerTime + delay;
        }

        /// <summary> 시전이 정상 종료되거나 취소됐다. 맡고 있던 모션이면 Idle로 돌아간다 </summary>
        public void OnCastEnded(int castId, bool isCancelled)
        {
            // 처치 모션 중에는 끝난 시전 알림이 재생 속도나 상태를 건드리지 않는다
            if ((_isDeathPlayed) || (_castId != castId))
            {
                return;
            }

            // 정상 종료 알림은 예고 표현이 끝났다는 뜻이라 돌진 복귀 시각과 다를 수 있다. 돌진은 서버가 정한 끝 시각에 Tick이 마친다
            if (_isDash && _isStarted && (!isCancelled))
            {
                return;
            }

            Finish();
        }

        /// <summary> 매 프레임 부른다. 계획한 시각이 되면 모션을 시작하고 느리게 늘리는 구간과 돌진 복귀를 처리한다 </summary>
        public void Tick(double serverNow, float deltaTime)
        {
            if ((!_isPlanned) || (_isDeathPlayed))
            {
                return;
            }

            if (_isDash)
            {
                TickDash(serverNow, deltaTime);
                return;
            }

            // 클립이 컨트롤러 전이로 스스로 Idle에 도착했으면 서버 종료 알림이 없어도(사건형, 투사체, 장판형) 배속을 1배로 돌리고 모션을 마친다
            if ((_isStarted) && (IsSettledInIdle()))
            {
                Reset();
                return;
            }

            if ((!_isStarted) && (serverNow >= _startAt))
            {
                Start(serverNow);
            }

            if (_isStarted && _entry.HasSlowSegment)
            {
                _clipTime += deltaTime * _animator.speed;
                _animator.speed = WolbaekMotionPlan.ResolveSpeed(in _entry, _speed, _clipTime);
            }
        }

        /// <summary> 처치 클립 상태를 재생 중인가, 쓰러지는 동안이다. 끝나면 컨트롤러 전이가 정지 상태로 넘긴다 </summary>
        public bool IsDeathFalling => _isDeathPlayed && (_animator.GetCurrentAnimatorStateInfo(0).shortNameHash == _deathHash);

        /// <summary> 처치 모션을 재생한다, 쓰러진 뒤 마지막 자세에서 정지한다. 이미 재생했으면 무시한다. isLate가 참이면 재접속으로 이미 쓰러진 보스라 마지막 자세로 바로 간다 </summary>
        public void PlayDeath(bool isLate = false)
        {
            if (_isDeathPlayed || (!HasDeathState))
            {
                return;
            }

            Reset();
            _isDeathPlayed = true;
            _animator.speed = 1f;

            if (isLate && _animator.HasState(0, _deathHoldStateId))
            {
                _animator.CrossFadeInFixedTime(_deathHoldHash, 0f, 0, 0f);
                return;
            }

            // 정지 상태(속도 0)에서 정규화 길이로 섞으면 전이가 진행되지 않으므로 초 단위로 섞는다
            _animator.CrossFadeInFixedTime(_deathHash, DEATH_BLEND_SECONDS, 0, 0f);
        }

        /// <summary> 처치 상태에서 다시 일어선다, 전투를 다시 시작할 때 쓴다 </summary>
        public void ClearDeath()
        {
            if (!_isDeathPlayed)
            {
                return;
            }

            _isDeathPlayed = false;
            _animator.speed = 1f;

            // 처치 정지 상태는 속도가 0이라 정규화 길이 전이가 끝나지 않는다, 초 단위로 섞어 다음 처치가 쓰러짐부터 재생되게 한다
            _animator.CrossFadeInFixedTime(_idleHash, DEATH_BLEND_SECONDS, 0, 0f);
        }

        /// <summary> 등장 모션을 재생한다, elapsedSeconds는 서버 등장 시작부터 지난 초다. 끝은 컨트롤러 전이가 Idle로 이어 준다 </summary>
        public void PlayAppear(float elapsedSeconds = 0f)
        {
            if ((_isDeathPlayed) || (!HasAppearState))
            {
                return;
            }

            Reset();
            _animator.speed = 1f;
            _animator.CrossFadeInFixedTime(_appearHash, 0f, 0, Mathf.Max(0f, elapsedSeconds));
        }

        /// <summary> 진행 중인 계획을 버리고 재생 속도를 되돌린다 </summary>
        public void Reset()
        {
            _isPlanned     = false;
            _isStarted     = false;
            _isDash        = false;
            _isRecovering  = false;
            _castId        = NO_CAST;
            _cast          = null;
            _clipTime      = 0f;
            _hasEnteredClip = false;
            _animator.speed = 1f;
        }

        // 시작한 클립 상태에 한 번 들어간 뒤 전이 없이 Idle에 머물러 있다
        private bool IsSettledInIdle()
        {
            int current = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            if (current == _entryHash)
            {
                _hasEnteredClip = true;
                return false;
            }

            return (_hasEnteredClip) && (current == _idleHash) && (!_animator.IsInTransition(0));
        }

        // 판정 시각까지의 시간으로 시작 지연과 속도를 정한다. 시작 시각은 서버 시각이라 늦게 받아도 같은 시각에 시작한다
        private void Plan(BossTelegraphCast cast, double serverNow)
        {
            // 맡지 않는 시전(반월 구역 예고 등)이 진행 중인 사건 모션의 값을 덮어쓰지 않게 지역 변수로 먼저 찾는다
            bool isDash = WolbaekMotionCatalog.IsDash(cast.PatternId);
            WolbaekMotionEntry entry = default;
            if ((!isDash) && (!WolbaekMotionCatalog.TryGet(cast.PatternId, out entry)))
            {
                return;
            }

            Reset();
            _entry     = entry;
            _entryHash = isDash ? 0 : Animator.StringToHash(entry.StateName);
            _castId    = cast.CastId;
            _cast      = cast;
            _isPlanned = true;
            _isDash    = isDash;

            if (isDash)
            {
                // 예고 동안 몸은 Idle이고 이펙트만 쓴다, 돌진 모션은 서버가 발동을 알린 뒤에 시작한다
                return;
            }

            WolbaekMotionPlan.ResolveStart(cast.Start.TelegraphSeconds, in _entry, out _speed, out float delay);
            _startAt = cast.Start.StartServerTime + delay;
        }

        private void Start(double serverNow)
        {
            // 늦게 받았으면 계획한 시작 시각에서 흐른 시간만큼 클립을 건너뛰고 이어 간다. 느리게 늘리는 구간도 반영한다
            float clipTime = WolbaekMotionPlan.ResolveClipTimeAt(in _entry, _speed, (float)(serverNow - _startAt));

            // 힘 순간을 한참 지나 받은 모션은 이미 끝난 일회성 동작이라 다시 재생하지 않는다
            if (!WolbaekMotionPlan.IsResumable(in _entry, clipTime))
            {
                _isPlanned = false;
                return;
            }

            _isStarted      = true;
            _clipTime       = clipTime;
            _animator.speed = WolbaekMotionPlan.ResolveSpeed(in _entry, _speed, clipTime);
            _animator.CrossFadeInFixedTime(_entry.StateName, ENTER_BLEND_SECONDS, 0, clipTime);
        }

        private void BeginDash(BossTelegraphCast cast, double serverNow)
        {
            // 거리가 0이면 돌진 모션 없이 곧장 복귀한다
            if (cast.DashDistance <= WolbaekMotionPlan.DASH_MIN_DISTANCE)
            {
                Finish();
                return;
            }

            // 재접속으로 돌진 도중에 받았으면 서버 끝 시각 전일 때만 흐른 만큼 건너뛰어 이어 간다, 이미 끝났으면 재생하지 않는다
            float executeAge = Mathf.Max(0f, cast.GetExecuteAge(serverNow));
            if (!WolbaekMotionPlan.IsDashResumable(executeAge, cast.GetDashEndAge()))
            {
                Reset();
                return;
            }

            float dashSeconds = (cast.DashSpeed > 0f) ? (cast.DashDistance / cast.DashSpeed) : cast.DashSeconds;
            _dashPlaySpeed = WolbaekMotionPlan.ResolveDashSpeed(dashSeconds);
            _isStarted     = true;
            _animator.speed = _dashPlaySpeed;

            // 지연이 작은 보통 발동은 처음부터 재생한다
            if (executeAge > LATE_SKIP_SECONDS)
            {
                _animator.CrossFadeInFixedTime(WolbaekMotionCatalog.DASH_STATE, ENTER_BLEND_SECONDS, 0, executeAge * _dashPlaySpeed);
                return;
            }

            _animator.CrossFade(WolbaekMotionCatalog.DASH_STATE, ENTER_BLEND_SECONDS, 0, 0f);
        }

        // 서버가 정한 돌진 끝 시각에 재생 속도를 1배까지 서서히 낮추면서 Idle로 길게 섞는다
        private void TickDash(double serverNow, float deltaTime)
        {
            if ((!_isStarted) || (_cast == null))
            {
                return;
            }

            if (!_isRecovering)
            {
                if (_cast.GetExecuteAge(serverNow) < _cast.GetDashEndAge())
                {
                    return;
                }

                _isRecovering   = true;
                _recoverElapsed = 0f;
                _animator.CrossFadeInFixedTime(_idleHash, WolbaekMotionPlan.ResolveDashBlendSeconds(_dashPlaySpeed), 0, 0f);
            }

            _recoverElapsed += deltaTime;
            _animator.speed = WolbaekMotionPlan.ResolveDashRecoverSpeed(_dashPlaySpeed, _recoverElapsed);

            if (_recoverElapsed >= WolbaekMotionPlan.DASH_RECOVER_SECONDS)
            {
                Reset();
            }
        }

        // 회복이 끝났는데 클립이 남아 있으면(길거나 배속으로 늘어난 클립) 다음 패턴을 위해 Idle로 섞어 자른다
        private void Finish()
        {
            bool wasPlaying = _isStarted;
            Reset();

            // 클립이 스스로 Idle로 넘어가는 중이면 그대로 둔다. 시험 씬처럼 남은 클립을 초 단위로 섞어 자른다
            bool isIdle = (_animator.GetCurrentAnimatorStateInfo(0).shortNameHash == _idleHash)
                || ((_animator.IsInTransition(0)) && (_animator.GetNextAnimatorStateInfo(0).shortNameHash == _idleHash));
            if ((wasPlaying) && (!isIdle))
            {
                _animator.CrossFadeInFixedTime(_idleHash, FINISH_BLEND_SECONDS, 0, 0f);
            }
        }
    }
}
