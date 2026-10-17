// System
using System;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;

// 서드파티
using Unity.Cinemachine;

namespace Shinshi.AI
{
    // 서버가 확정한 2페이즈 전환 구간(WolbaekPhase2TransitionCue)을 받아 확정 컷신을 한 번 재생한다.
    // 시작, 위치, 종료는 모두 서버 시각으로 정한다: 매 프레임 (서버 지금 - 서버 시작)으로 Timeline을 평가하고 서버 종료 시각에 끝낸다.
    // 그래서 늦게 받으면 중간부터 재생하고, 이미 끝난 구간은 재생하지 않는다. 클라이언트 재생 종료는 서버 전투 재개를 정하지 않는다.
    //
    // 재생 중에는 BossView에서 몸 표현 제어권(모션, 본 덧입힘, 위치 따라가기)을 넘겨받고, 끝나면 돌려준다.
    // 처치, Reset, 전투 종료처럼 BossView가 먼저 표현을 이어 가야 하면 BossView가 제어권을 거두고 이 재생기는 자기 장비만 즉시 지운다.
    // 처치 모션과 처치 VFX는 BossView 쪽이라 이 정리에서 지우지 않는다.
    //
    // 전투 카메라는 바꾸지 않는다. 컷 카메라는 우선순위가 낮은 장비 안 카메라이고 Timeline 카메라 트랙의 덮어쓰기로만 화면을 잡는다.
    // 덮어쓰기가 풀리면 브레인이 원래 전투 카메라로 바로 돌아간다. 덮어쓰기는 브레인의 일반 카메라 선택 위에 얹힌 층이라
    // 풀릴 때 기본 섞기가 생기지 않는다(플레이 모드 실측). 부드럽게 돌아가려면 확정 Timeline 마지막 컷에 빠지는 섞기를 넣어야 해서 사용자 결정으로 남겼다.
    //
    // Timeline은 Update와 LateUpdate에서 같은 시각으로 두 번 평가한다. 보스 Animator에는 컨트롤러가 붙어 있어,
    // 직접 평가하는 그래프의 자세는 같은 프레임의 애니메이션 갱신에서 컨트롤러 자세로 덮인다(플레이 모드 실측: 떠오르지 않고 Idle만 보였다).
    // 그래서 애니메이션 갱신 뒤인 LateUpdate에서 한 번 더 평가해 컷신 자세를 마지막에 쓴다. 같은 시각이라 파티클과 투명도는 다시 진행되지 않는다.
    // 브레인(LateUpdate)보다 먼저 돌도록 실행 순서를 앞당겨 카메라가 이번 프레임의 컷신 자세를 본다
    /// <summary> 월백 2페이즈 전환 컷신 재생기, 서버 구간 기준으로 재생하고 끝나면 몸 표현과 카메라를 원래대로 돌려준다 </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(EXECUTION_ORDER)]
    public sealed class WolbaekPhase2TransitionPlayer : MonoBehaviour, IBossViewControlOwner
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double LENGTH_WARN_SECONDS = 0.05d; // 서버 구간과 Timeline 길이가 이만큼 넘게 다르면 한 번 알린다
        private const string MAIN_CAMERA_TAG     = "MainCamera";
        private const int    EXECUTION_ORDER     = -100; // 기본 순서(0)인 Cinemachine 브레인보다 먼저 LateUpdate를 돈다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private WolbaekPhase2TransitionRig _rigPrefab;
        [SerializeField] private BossView                   _view; // 비어 있으면 같은 오브젝트에서 한 번 찾는다
        [SerializeField] private WolbaekColorWardView       _ward; // 50% 보호막을 넘겨받을 곳, 비어 있으면 같은 오브젝트에서 한 번 찾는다

        private readonly WolbaekPhase2TransitionGate _gate = new WolbaekPhase2TransitionGate();
        private readonly WolbaekHandedShield         _handedShield = new WolbaekHandedShield();
        private float                                _handedShieldBase; // 넘겨받은 시각의 컷신 보호막 투명도, 이 값으로 나눠 인계 순간 밝기를 그대로 잇는다

        private Func<double>               _clock;
        private WolbaekPhase2TransitionCue _cue;
        private WolbaekPhase2TransitionRig _rig;
        private Transform                  _model;
        private Vector3                    _modelPosition;
        private Quaternion                 _modelRotation;
        private bool                       _modelApplyRootMotion;
        private double                     _playbackTime;
        private bool                       _isPending;
        private bool                       _isPlaying;
        private EWolbaekPhase2TransitionStop _lastStop = EWolbaekPhase2TransitionStop.None;
        private EWolbaekShieldHandover     _lastShieldHandover = EWolbaekShieldHandover.None;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsPending => _isPending;
        public bool IsPlaying => _isPlaying;

        /// <summary> 지금 재생 중이거나 마지막으로 재생한 전환 번호 </summary>
        public int TransitionId => _cue.TransitionId;

        /// <summary> 마지막으로 평가한 Timeline 시각(초) </summary>
        public double PlaybackTime => _playbackTime;

        public EWolbaekPhase2TransitionStop LastStop => _lastStop;

        /// <summary> 마지막 재생 시작 때 50% 정식 보호막을 넘긴 결과 </summary>
        public EWolbaekShieldHandover LastShieldHandover => _lastShieldHandover;

        /// <summary> 넘겨받아 소멸시키는 중인 정식 보호막, 없으면 null이다 </summary>
        public GameObject HandedShield => _handedShield.Shield;

        /// <summary> 재생 중인 장비, 없으면 null이다 </summary>
        public WolbaekPhase2TransitionRig ActiveRig => _rig;

        public WolbaekPhase2TransitionGate Gate => _gate;

        // HUD, 입력처럼 다른 파트가 컷신 표현 시작과 끝에 맞춰 자기 표현을 바꿀 때 구독한다. 서버 전투 제한의 근거로 쓰지 않는다
        /// <summary> 컷신 재생을 실제로 시작했다. 전환당 한 번만 온다 </summary>
        public event Action<WolbaekPhase2TransitionCue> Started;

        /// <summary> 시작한 컷신이 끝났다. 정상 종료와 모든 중단에서 한 번만 온다 </summary>
        public event Action<EWolbaekPhase2TransitionStop> Stopped;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_view == null)
            {
                TryGetComponent(out _view);
            }

            if (_ward == null)
            {
                TryGetComponent(out _ward);
            }
        }

        private void Update()
        {
            Tick(ReadClock());
        }

        private void LateUpdate()
        {
            ReapplyPose();
        }

        private void OnDisable()
        {
            Stop(EWolbaekPhase2TransitionStop.Disabled);
        }

        private void OnDestroy()
        {
            Stop(EWolbaekPhase2TransitionStop.Disabled);
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetServerClock(Func<double> clock)
        {
            _clock = clock;
        }

        /// <summary> 새 연결이나 새 전투를 시작한다. 재생 중이면 끝내고 이전 세션의 번호 기록을 버린다 </summary>
        public void ResetSession(int combatId)
        {
            Stop(EWolbaekPhase2TransitionStop.SessionReset);
            _gate.ResetSession(combatId);
        }

        /// <summary> 연결이 끊겼다. 재생 중이면 끝내고 다음 ResetSession 전까지 사건을 받지 않는다 </summary>
        public void EndSession()
        {
            Stop(EWolbaekPhase2TransitionStop.Unbound);
            _gate.EndSession();
        }

        /// <summary> 서버 전환 사건이나 접속 스냅샷을 받는다. 같은 구간이 두 경로로 와도 한 번만 재생한다 </summary>
        public EWolbaekPhase2TransitionDecision Receive(in WolbaekPhase2TransitionCue cue, double serverNow)
        {
            EWolbaekPhase2TransitionDecision decision = _gate.Evaluate(in cue, serverNow);
            if ((decision != EWolbaekPhase2TransitionDecision.Play) && (decision != EWolbaekPhase2TransitionDecision.Scheduled))
            {
                return decision;
            }

            Stop(EWolbaekPhase2TransitionStop.Superseded);
            _lastShieldHandover = EWolbaekShieldHandover.None;
            _cue       = cue;
            _isPending = true;
            Tick(serverNow);
            return decision;
        }

        /// <summary> 서버 지금 시각으로 진행한다. Update가 부르며 검증 도구는 직접 부를 수 있다 </summary>
        public void Tick(double serverNow)
        {
            if (_isPending)
            {
                if (serverNow < _cue.StartServerTime)
                {
                    return;
                }

                _isPending = false;

                // 기다리는 동안 프레임이 멈춰 서버 구간이 이미 지나갔으면 한 프레임 보이고 끝나는 재생을 하지 않는다
                if (serverNow >= _cue.EndServerTime)
                {
                    _lastStop = EWolbaekPhase2TransitionStop.Completed;
                    return;
                }

                if (!TryBegin(serverNow))
                {
                    _lastStop = EWolbaekPhase2TransitionStop.Unavailable;
                    return;
                }
            }

            if (!_isPlaying)
            {
                return;
            }

            if (serverNow >= _cue.EndServerTime)
            {
                Finish();
                return;
            }

            // Timeline보다 서버 구간이 길면 마지막 장면(착지 뒤 대기)을 서버 종료까지 유지한다
            _playbackTime = Math.Min(serverNow - _cue.StartServerTime, _rig.Duration);
            _rig.Evaluate(_playbackTime);
            UpdateHandedShield();
        }

        /// <summary> 정상 종료가 아닌 중단, 장비를 바로 지우고 몸 표현을 돌려준다 </summary>
        public void Stop(EWolbaekPhase2TransitionStop reason)
        {
            if (_isPending)
            {
                _isPending = false;
                _lastStop  = reason;
            }

            if (!_isPlaying)
            {
                return;
            }

            _isPlaying = false;
            TeardownRig();
            RestoreModel();
            if (_view != null)
            {
                _view.ReleaseExternalControl(this, true);
            }

            _lastStop = reason;
            Stopped?.Invoke(reason);
        }

        /// <summary> 애니메이션 갱신 뒤 같은 시각으로 다시 평가해 컨트롤러 자세를 덮는다. LateUpdate가 부른다 </summary>
        public void ReapplyPose()
        {
            if ((!_isPlaying) || (_rig == null))
            {
                return;
            }

            _rig.Evaluate(_playbackTime);
            UpdateHandedShield();
        }

        void IBossViewControlOwner.OnViewControlRevoked(EBossViewControlRevoke reason)
        {
            _isPending = false;
            if (!_isPlaying)
            {
                return;
            }

            _isPlaying = false;
            TeardownRig();
            RestoreModel();
            _lastStop = EWolbaekPhase2TransitionStop.Revoked;
            Stopped?.Invoke(EWolbaekPhase2TransitionStop.Revoked);
        }

        private bool TryBegin(double serverNow)
        {
            if ((_rigPrefab == null) || (_view == null))
            {
                return false;
            }

            Animator animator = _view.ModelAnimator;
            if ((animator == null) || (!_view.AcquireExternalControl(this)))
            {
                return false;
            }

            _model                = animator.transform;
            _modelPosition        = _model.localPosition;
            _modelRotation        = _model.localRotation;
            _modelApplyRootMotion = animator.applyRootMotion;

            // 시안의 보스 Animator는 루트 이동을 끈 상태로 Timeline이 몸을 띄웠다. 같은 조건으로 재생하고 끝나면 되돌린다
            animator.applyRootMotion = false;

            Transform boss = _view.transform;
            Vector3 forward = boss.forward;
            forward.y = 0f;
            Quaternion facing = (forward.sqrMagnitude > 0f) ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity;

            _rig = Instantiate(_rigPrefab, boss.position, facing);
            if (_rig.gameObject.scene != gameObject.scene)
            {
                SceneManager.MoveGameObjectToScene(_rig.gameObject, gameObject.scene);
            }

            // 정식 보호막을 이어받을 수 있으면(시작 보호막이 아직 보이는 시각) 컷신 자체 보호막은 그리지 않는다. 둘이 겹치지 않고
            // 정식 인스턴스가 위치, 크기, 밝기를 그대로 이어 간다. 넘겨받을 것이 없으면(늦은 접속) 확정 v03의 자체 보호막을 그린다
            double elapsed = Math.Max(0d, serverNow - _cue.StartServerTime);
            float baseVisibility = _rig.ShieldVisibility(elapsed);
            bool takesOfficialShield = (_ward != null) && _ward.HasShieldForCutscene && (baseVisibility > 0f);
            if (!_rig.Bind(animator, ResolveBrain(), !takesOfficialShield))
            {
                TeardownRig();
                RestoreModel();
                _view.ReleaseExternalControl(this, true);
                return false;
            }

            double window = _cue.EndServerTime - _cue.StartServerTime;
            if (Math.Abs(window - _rig.Duration) > LENGTH_WARN_SECONDS)
            {
                Debug.LogWarning($"[Phase2Transition] 서버 구간 {window:F2}초와 컷신 {_rig.Duration:F2}초가 다르다. 서버 종료 시각을 따른다, transitionId={_cue.TransitionId}", this);
            }

            // 장비와 바인딩이 모두 준비된 뒤에만 정식 보호막을 넘긴다. 위에서 실패하면 정식 보호막의 2.5초 소멸이 그대로 이어진다.
            // 0.75초 전이면 이 프레임의 첫 평가에서 컷신 보호막이 보이므로 두 보호막이 같이 그려지거나 비는 프레임이 없다.
            // 0.75초를 지나 받았으면 컷신 표현에 보호막이 없으므로 정식 보호막도 처음부터 다시 보이지 않고 사라진다(카메라 컷과 같은 프레임)
            GameObject official = null;
            if (_ward != null)
            {
                official = _ward.TakeShieldForCutscene(out _lastShieldHandover);
            }

            if ((official != null) && takesOfficialShield)
            {
                _handedShield.Capture(official);
                _handedShieldBase = baseVisibility;
            }
            else if (official != null)
            {
                // 시작 보호막이 이미 끝난 시각에 받았다. 컷신 표현에 보호막이 없으므로 처음부터 다시 보이지 않고 사라진다
                _handedShield.Capture(official);
                _handedShield.Release();
            }

            _isPlaying = true;
            Started?.Invoke(_cue);
            return true;
        }

        private void Finish()
        {
            _playbackTime = Math.Min(_cue.EndServerTime - _cue.StartServerTime, _rig.Duration);
            _rig.Evaluate(_playbackTime);

            _isPlaying = false;
            TeardownRig();
            RestoreModel();
            if (_view != null)
            {
                _view.ReleaseExternalControl(this, true);
            }

            _lastStop = EWolbaekPhase2TransitionStop.Completed;
            Stopped?.Invoke(EWolbaekPhase2TransitionStop.Completed);
        }

        // 그래프를 먼저 없애 카메라 덮어쓰기와 Animator 출력을 풀고 VFX 인스턴스를 지운 뒤 장비를 지운다
        private void TeardownRig()
        {
            if (_rig == null)
            {
                return;
            }

            _handedShield.Release();
            _rig.StopGraph();
            DestroyRig(_rig);
            _rig = null;
        }

        // 컷신 보호막 곡선을 넘겨받은 순간 값으로 나눠 인계 순간은 그대로, 0.7초에 완전히 투명하게 만든다. 시작 보호막 끝(0.75초)에 지운다
        private void UpdateHandedShield()
        {
            if (!_handedShield.IsHeld)
            {
                return;
            }

            if (_playbackTime >= _rig.ShieldEnd)
            {
                _handedShield.Release();
                return;
            }

            _handedShield.Apply(_rig.ShieldVisibility(_playbackTime) / _handedShieldBase);
        }

        private void RestoreModel()
        {
            if (_model == null)
            {
                return;
            }

            _model.localPosition = _modelPosition;
            _model.localRotation = _modelRotation;
            if (_model.TryGetComponent(out Animator animator))
            {
                animator.applyRootMotion = _modelApplyRootMotion;
            }

            _model = null;
        }

        private static void DestroyRig(WolbaekPhase2TransitionRig rig)
        {
            if (Application.isPlaying)
            {
                Destroy(rig.gameObject);
            }
            else
            {
                DestroyImmediate(rig.gameObject);
            }
        }

        // 활성 브레인 목록에서 고른다. 씬 전체 탐색을 하지 않고, 전투 카메라 설정은 읽기만 한다
        private static CinemachineBrain ResolveBrain()
        {
            CinemachineBrain first = null;
            for (int i = 0; i < CinemachineBrain.ActiveBrainCount; ++i)
            {
                CinemachineBrain brain = CinemachineBrain.GetActiveBrain(i);
                if (brain == null)
                {
                    continue;
                }

                if (brain.CompareTag(MAIN_CAMERA_TAG))
                {
                    return brain;
                }

                if (first == null)
                {
                    first = brain;
                }
            }

            return first;
        }

        private double ReadClock()
        {
            return (_clock != null) ? _clock() : Time.timeAsDouble;
        }
    }
}
