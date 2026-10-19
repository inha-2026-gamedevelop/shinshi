// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.Events;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 보스 외형을 그리는 표현이다. 서버가 정한 위치와 방향을 받아 따라가고, 예고 표현 진입점(BossTelegraphPresenter)이
    // 알리는 시전 단계(예고, 발동, 끝)와 돌진 값으로 임시 모션을 만든다. 서버 값만 읽고 판정에는 관여하지 않는다.
    // 모션은 애니메이션 클립이 없는 동안 쓰는 휴머노이드 본 직접 조작이다. 컨트롤러가 붙으면 그 위에 덧붙여 계속 쓸 수 있고
    // 진짜 모션이 들어오면 이 클래스의 모션 부분만 걷어내면 된다.
    // 본을 세계 좌표 축으로 돌려서 본마다 다른 로컬 축 방향을 신경 쓰지 않는다
    /// <summary> 월백 외형 표현, 위치와 방향을 따라가고 시전 단계에 맞춰 임시 모션을 보여 준다 </summary>
    public sealed class BossView : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        // 표현 상수다, 밸런스 값이 아니다
        private const float  POSITION_SMOOTH      = 20f;  // 1/초, 서버 갱신 사이를 부드럽게 이을 때만 쓴다
        private const float  TURN_SMOOTH          = 12f;  // 1/초
        private const float  GATHER_SMOOTH        = 6f;   // 1/초, 예고 동안 천천히 모으는 자세로
        private const float  RELEASE_SMOOTH       = 22f;  // 1/초, 발동은 순간적으로 튕긴다
        private const float  IDLE_SMOOTH          = 7f;   // 1/초
        private const float  RELEASE_SECONDS      = 0.35f; // 초, 발동 자세를 유지하는 시간
        private const float  BREATH_RATE          = 1.6f; // rad/초
        private const float  BREATH_PITCH         = 1.5f; // 도
        private const float  BREATH_LIFT          = 0.02f; // m
        private const float  DOWN_PITCH           = -80f; // 도, 쓰러질 때 뒤로 눕는 각도
        private const float  DOWN_LIFT            = 0.2f; // m
        private const float  DASH_END_MARGIN      = 0.15f; // 초, 돌진 끝 뒤에 돌진 자세를 풀기까지
        private const float  DOWN_ARM_SIDE        = 30f;  // 도
        private const float  DASH_PIVOT_PITCH     = 28f;  // 도, 돌진은 몸 전체를 앞으로 눕힌다
        private const float  DASH_SPINE_PITCH     = 14f;  // 도
        private const float  DASH_HEAD_PITCH      = -10f; // 도
        private const float  DASH_ARM_SIDE        = 20f;  // 도
        private const float  DASH_ARM_FORWARD     = -40f; // 도, 팔을 뒤로 젖힌다
        private const float  DASH_WINDUP_PITCH    = -12f; // 도, 돌진 예고 동안 몸을 뒤로 모은다
        private const float  RELEASE_ARM_SCALE    = 1.5f; // 발동 때 팔을 모으던 각도보다 더 내미는 배율
        private const float  HALF                 = 0.5f;
        private const float  APPEAR_EFFECT_HEIGHT = 0.02f; // m, 등장 마법진을 바닥에서 띄우는 높이
        private const float  APPEAR_EFFECT_LIFE   = 6f;    // 초, 생성을 멈춘 뒤 남은 파티클이 사라지기를 기다리는 시간
        private const float  STABLE_RADIUS        = 0.5f;  // m, 표시 위치가 서버 위치에 이만큼 가까우면 자리를 잡은 것으로 본다
        private const float  DIRECTION_EPSILON    = 0.0001f;
        private const string IDLE_STATE           = "Idle";
        private const float  CONTROL_RETURN_SPEED = 1f;    // 외부 제어를 돌려받으면 정상 재생 속도로 돌아간다
        private const float  CONTROL_RETURN_BLEND = 0.05f; // 외부 제어(컷신)를 돌려받을 때 Idle로 섞는 정규화 시간, 색 결계 모션 복귀와 같은 값
        // 클라이언트 어셈블리는 서버 어셈블리의 패턴 팩토리 상수를 볼 수 없어 장판 패턴의 ID를 따로 둔다. 모양은 원형이어도 자세가 달라서다
        private const string AREA_PATTERN_ID     = "wolbaek_p05";
        private const string AREA_PHASE_TWO_ID   = Shinshi.Data.BossPatternCatalogSO.WOLBAEK_P2_P04_ID; // 2페이즈 지정 장판도 같은 던지는 자세다
        private const string URP_LIT_SHADER       = "Universal Render Pipeline/Lit";
        private const string PROP_SRC_TEXTURE     = "_MainTex";
        private const string PROP_SRC_COLOR       = "_Color";
        private const string PROP_SRC_SMOOTH      = "_Glossiness";
        private const string PROP_BASE_MAP        = "_BaseMap";
        private const string PROP_BASE_COLOR      = "_BaseColor";
        private const string PROP_BUMP_MAP        = "_BumpMap";
        private const string PROP_SMOOTHNESS      = "_Smoothness";
        private const string PROP_CULL            = "_Cull";
        private const string PROP_ALPHA_CLIP      = "_AlphaClip";
        private const string PROP_CUTOFF          = "_Cutoff";
        private const string KEYWORD_NORMAL       = "_NORMALMAP";
        private const string KEYWORD_ALPHATEST    = "_ALPHATEST_ON";
        private const float  ALPHA_CUTOFF_DEFAULT = 0.5f;
        private const float  FLAG_ON              = 1f;

        /****************************************
        *                Types
        ****************************************/

        // 한 순간의 자세, 각도는 모두 도이고 본 하나가 아니라 몸 전체의 뜻으로 쓴다
        private struct Pose
        {
            public float PivotPitch;   // 몸 전체가 앞으로 숙이는 각도, 음수는 뒤로
            public float SpinePitch;   // 상체가 앞으로 숙이는 각도
            public float HeadPitch;    // 고개가 숙이는 각도, 음수는 든다
            public float ArmSide;      // 양팔이 옆으로 벌어지는 각도
            public float ArmForward;   // 양팔이 앞으로 드는 각도, 음수는 뒤로
            public float Lift;         // 몸이 위로 뜨는 높이

            public static Pose Lerp(in Pose from, in Pose to, float t)
            {
                return new Pose
                {
                    PivotPitch = Mathf.Lerp(from.PivotPitch, to.PivotPitch, t),
                    SpinePitch = Mathf.Lerp(from.SpinePitch, to.SpinePitch, t),
                    HeadPitch  = Mathf.Lerp(from.HeadPitch, to.HeadPitch, t),
                    ArmSide    = Mathf.Lerp(from.ArmSide, to.ArmSide, t),
                    ArmForward = Mathf.Lerp(from.ArmForward, to.ArmForward, t),
                    Lift       = Mathf.Lerp(from.Lift, to.Lift, t)
                };
            }
        }

        // 휴머노이드 본 하나와 기본 자세의 로컬 회전
        private struct BoneRef
        {
            public Transform  Bone;
            public Quaternion Bind;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("연결")]
        [SerializeField] private Transform _pivot;    // 몸 전체를 숙이고 띄우는 자식, 모델이 이 아래에 있다
        [SerializeField] private Animator  _animator; // 휴머노이드 아바타를 가진 모델의 애니메이터
        [SerializeField] private WolbaekWarpView _warp; // 2페이즈 P07 워프 표현, 비어 있으면 생략한다
        [SerializeField] private WolbaekPatternVfxView _patternVfx; // 패턴 VFX 표현, 비어 있으면 생략한다
        [SerializeField] private WolbaekDissolveView _dissolve; // 처치 뒤 정지와 디졸브 표현, 비어 있으면 쓰러진 자세로 남는다
        [SerializeField] private UnityEvent _onTeleported; // 모델 위치가 한 번에 바뀐 같은 프레임에 불린다, 치마 같은 위치 기억 표현을 초기화하는 용도다
        [SerializeField] private GameObject _appearEffectPrefab; // 등장 마법진, 서버 등장 상태 동안 보이고 비어 있으면 생략한다

        [Header("자세")]
        [SerializeField] private Vector3 _gatherSideArms = new Vector3(-6f, 60f, 25f);   // 부채꼴, 직선: 몸을 뒤로 젖히고 팔을 벌린다. x 상체, y 팔 옆, z 팔 앞
        [SerializeField] private Vector3 _gatherCircle   = new Vector3(8f, 25f, -10f);   // 원형: 웅크려 모은다
        [SerializeField] private Vector3 _gatherThrow    = new Vector3(-10f, 20f, 55f);  // 투사체, 장판: 팔을 앞으로 든다

        private readonly List<BoneRef>  _bones            = new List<BoneRef>();
        private readonly List<Material> _runtimeMaterials = new List<Material>();

        private BossTelegraphPresenter _presenter;
        private Func<double>           _serverClock;
        private BoneRef                _spine;
        private BoneRef                _chest;
        private BoneRef                _head;
        private BoneRef                _leftArm;
        private BoneRef                _rightArm;
        private bool                   _hasBones;
        private bool                   _resetsBind;
        private bool                   _hasClip;

        private Vector3    _targetPosition;
        private Vector3    _targetForward = Vector3.forward;
        private bool       _hasPose;

        private WolbaekMotionDirector _motion;
        private WolbaekTalismanView _talismanView;
        private WolbaekColorWardView _colorWardView;
        private WolbaekFoxfireSealView _foxfireSealView;
        private WolbaekFinalAreaView _finalAreaView;
        private Renderer[]            _modelRenderers;
        private Vector3               _stablePosition;
        private bool                  _hasStablePosition;
        private Vector3               _holdPosition;
        private bool                  _isHeld;
        private GameObject            _appearEffect;

        private int                _castId;
        private bool               _hasCast;
        private bool               _isDefeated;
        private Pose               _current;

        private IBossViewControlOwner _controlOwner; // 컷신처럼 몸 표현을 잠시 넘겨받은 쪽, 없으면 이 클래스가 직접 그린다

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_pivot == null) || (_animator == null))
            {
                Debug.LogError("[BossView] 몸 피벗 또는 애니메이터 참조가 비어 있다", this);
                enabled = false;
                return;
            }

            _modelRenderers = _pivot.GetComponentsInChildren<Renderer>();
            ConvertToUrpLit(_modelRenderers, _runtimeMaterials);
            CacheBones();
            _motion = new WolbaekMotionDirector(_animator);
            _talismanView = GetComponent<WolbaekTalismanView>();
            _colorWardView = GetComponent<WolbaekColorWardView>();
            _foxfireSealView = GetComponent<WolbaekFoxfireSealView>();
            _finalAreaView = GetComponent<WolbaekFinalAreaView>();
        }

        private void OnDestroy()
        {
            RevokeExternalControl(EBossViewControlRevoke.Destroyed, false);
            Unbind();
            DestroyRuntimeMaterials();

            if (_appearEffect != null)
            {
                Destroy(_appearEffect);
            }
        }

        // 외부 제어 중에는 위치 따라가기와 모션 진행을 멈춘다. 컷신이 보스를 전환 시작 자리에 두고 Animator를 직접 쓰기 때문이다.
        // 워프와 패턴 VFX는 서버 사건 표현이라 계속 진행한다
        private void Update()
        {
            double now = ReadClock();
            if (_controlOwner == null)
            {
                FollowPose(Time.deltaTime);
                _motion.Tick(now, Time.deltaTime);
            }

            if (_warp != null)
            {
                _warp.Tick(now);
            }

            if (_patternVfx != null)
            {
                _patternVfx.Tick(now);
            }

            if (_isDefeated && (_dissolve != null))
            {
                _dissolve.Tick(Time.deltaTime, _motion.IsDeathFalling);
            }
        }

        // 본은 애니메이터가 쓴 뒤에 얹어야 하므로 LateUpdate다
        private void LateUpdate()
        {
            if ((!_hasBones) || (_controlOwner != null))
            {
                return;
            }

            UpdateMotion(Time.deltaTime);
            ApplyPose(in _current);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 예고 표현 진입점에 연결한다, 시전 단계 알림으로 모션을 정하고 시각은 serverClock으로 읽는다. 비우면 로컬 시각이다 </summary>
        public void Bind(BossTelegraphPresenter presenter, Func<double> serverClock)
        {
            Unbind();

            _presenter   = presenter;
            _serverClock = serverClock;

            if (_presenter != null)
            {
                _presenter.PhaseChanged += OnPhaseChanged;

                // 워프 충격파 예고는 워프 표현이 직접 그리므로 기본 지면 예고는 겹치지 않게 생략한다
                if (_warp != null)
                {
                    for (int i = 0; i < WolbaekMotionCatalog.WarpPatternIds.Length; ++i)
                    {
                        _presenter.RegisterExternalVisual(WolbaekMotionCatalog.WarpPatternIds[i]);
                    }
                }

                if (_patternVfx != null)
                {
                    _patternVfx.Bind(_presenter, _serverClock);
                }
            }
        }

        /// <summary> 서버가 정한 위치와 방향, 처음 받으면 그 자리로 바로 옮기고 이후는 부드럽게 따라간다 </summary>
        public void SetPose(Vector3 position, Vector3 forward)
        {
            _targetPosition = position;

            forward.y = 0f;
            if (forward.sqrMagnitude > DIRECTION_EPSILON)
            {
                _targetForward = forward.normalized;
            }

            if (!_hasPose)
            {
                _hasPose           = true;
                transform.position = _targetPosition;
                transform.rotation = Quaternion.LookRotation(_targetForward, Vector3.up);
            }
        }

        // lateElapsedSeconds는 서버 처치 시각부터 지난 시간이고 모르면 음수다. 재접속이면 정지와 디졸브를 이 시간만큼 지난 지점부터 잇는다
        /// <summary> 쓰러진 모습을 보인다, 보스 사망이다. 되돌리면 다시 일어선다. isLate가 참이면 재접속으로 이미 쓰러진 상태를 받은 것이다 </summary>
        public void SetDefeated(bool isDefeated, bool isLate = false, float lateElapsedSeconds = WolbaekDeathTimeline.UNKNOWN_ELAPSED)
        {
            if (_isDefeated == isDefeated)
            {
                return;
            }

            _isDefeated = isDefeated;

            // 확정한 처치 모션이 컨트롤러에 있으면 그것을 쓰고 없으면 임시 도형 자세로 쓰러진다
            if (isDefeated)
            {
                // 컷신 장비와 컷신이 바꾼 모델 자세를 먼저 걷어 내야 처치 모션이 원래 자리에서 시작한다
                RevokeExternalControl(EBossViewControlRevoke.Defeated, false);

                if (_talismanView != null)
                {
                    _talismanView.Clear();
                }
                if (_colorWardView != null)
                {
                    _colorWardView.Clear();
                }
                if (_foxfireSealView != null)
                {
                    _foxfireSealView.Clear();
                }
                if (_finalAreaView != null)
                {
                    _finalAreaView.Clear();
                }

                // 서버 취소 사건을 다 받지 못해도 공격 효과가 처치 연출 위에 남지 않게 끊는다. 처치 모션과 디졸브는 아래에서 그대로 이어 간다
                if (_patternVfx != null)
                {
                    _patternVfx.StopAttackEffects();
                }
                _motion.PlayDeath(isLate);

                // 재접속이면 쓰러짐을 건너뛴다. 서버 처치 시각을 알면 그만큼 지난 정지와 디졸브 지점부터 잇는다
                if (_dissolve != null)
                {
                    _dissolve.Begin(isLate, lateElapsedSeconds);
                }
            }
            else
            {
                _motion.ClearDeath();

                if (_dissolve != null)
                {
                    _dissolve.ResetDeath();
                }
            }
        }

        // appearElapsedSeconds는 서버가 등장을 시작한 시각부터 지난 시간이다. 늦게 받은 등장 모션을 처음부터 다시 틀지 않고 그 지점부터 잇는다
        /// <summary> 서버 흐름 상태가 바뀌었다. 등장 상태가 시작되면 등장 모션을 재생한다 </summary>
        public void SetFlowState(EBossFlowState flowState, float appearElapsedSeconds = 0f)
        {
            if ((flowState == EBossFlowState.Defeated) || (flowState == EBossFlowState.Ended))
            {
                RevokeExternalControl(EBossViewControlRevoke.CombatEnded, !_isDefeated);
            }

            if (flowState == EBossFlowState.Appearing)
            {
                _motion.PlayAppear(appearElapsedSeconds);
                ShowAppearEffect();
                return;
            }

            // 등장 상태가 끝나면 마법진은 새 파티클만 멈추고 남은 파티클은 자연 소멸한다
            HideAppearEffect();
        }

        private void ShowAppearEffect()
        {
            if ((_appearEffectPrefab == null) || (_appearEffect != null))
            {
                return;
            }

            _appearEffect = Instantiate(_appearEffectPrefab, transform.position + (Vector3.up * APPEAR_EFFECT_HEIGHT), Quaternion.identity);
        }

        private void HideAppearEffect()
        {
            if (_appearEffect == null)
            {
                return;
            }

            foreach (ParticleSystem system in _appearEffect.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }

            Destroy(_appearEffect, APPEAR_EFFECT_LIFE);
            _appearEffect = null;
        }

        /// <summary> 패턴 VFX 표현, 서버 사건을 받는 연결부가 효과를 부를 때 쓴다 </summary>
        public WolbaekPatternVfxView PatternVfx => _patternVfx;

        /// <summary> 서버 기믹 사건으로 정해진 월백 모션을 재생하고 패턴 모션 계획을 비운다 </summary>
        public void PlayGimmickMotion(string state, float speed, float blend)
        {
            if ((_isDefeated) || (_controlOwner != null) || (_animator == null) || (_motion == null) || (string.IsNullOrEmpty(state)))
            {
                return;
            }

            int hash = Animator.StringToHash(_animator.GetLayerName(0) + "." + state);
            if (!_animator.HasState(0, hash))
            {
                return;
            }

            _motion.Reset();
            _animator.speed = Mathf.Max(0.01f, speed);
            _animator.CrossFade(hash, blend, 0, 0f);
        }

        /// <summary> 서버 사건 종류로 정하는 패턴의 모션을 첫 판정 시각에 맞춰 시작하도록 계획한다 </summary>
        public void BeginEventMotion(int castId, EWolbaekEventMotion kind, double castStartServerTime, float strikeSeconds)
        {
            _motion.PlanEvent(castId, kind, castStartServerTime, strikeSeconds);
        }

        /// <summary> 서버가 시전의 회복까지 끝났다고 알렸다. 클립이 아직 남아 있으면 Idle로 섞어 자르고, 이미 Idle로 가는 중이면 그대로 둔다 </summary>
        public void EndServerCast(int castId)
        {
            if (_motion != null)
            {
                _motion.OnCastEnded(castId, false);
            }
        }

        /// <summary> 사건으로 정한 패턴이 끝났거나 취소됐다 </summary>
        public void EndEventMotion(int castId, bool isCancelled)
        {
            _motion.OnCastEnded(castId, isCancelled);
        }

        /// <summary> 휴머노이드 모델의 애니메이터, 외부 제어를 받은 쪽이 Timeline에 연결할 때 쓴다 </summary>
        public Animator ModelAnimator => _animator;

        /// <summary> 컷신처럼 몸 표현을 넘겨받은 쪽이 있다 </summary>
        public bool IsExternallyControlled => _controlOwner != null;

        // 넘겨주는 것은 모션 진행, 본 덧입힘, 서버 위치 따라가기다. 처치된 뒤에는 넘기지 않는다.
        // 다른 소유자가 이미 있으면 그쪽을 먼저 정리시키고 넘긴다
        /// <summary> 몸 표현 제어권을 넘겨받는다. 처치됐거나 모델이 없으면 거짓이다 </summary>
        public bool AcquireExternalControl(IBossViewControlOwner owner)
        {
            if ((owner == null) || (_isDefeated) || (_animator == null))
            {
                return false;
            }

            if (_controlOwner == owner)
            {
                return true;
            }

            RevokeExternalControl(EBossViewControlRevoke.Replaced, false);
            _controlOwner = owner;
            return true;
        }

        /// <summary> 넘겨받은 제어권을 돌려준다. returnToIdle이 참이면 모션 계획을 비우고 정상 속도의 Idle로 돌아간다 </summary>
        public void ReleaseExternalControl(IBossViewControlOwner owner, bool returnToIdle)
        {
            if ((owner == null) || (_controlOwner != owner))
            {
                return;
            }

            _controlOwner = null;
            if (returnToIdle)
            {
                PlayGimmickMotion(IDLE_STATE, CONTROL_RETURN_SPEED, CONTROL_RETURN_BLEND);
            }
        }

        // 소유자에게 알린 뒤 거둔다. 소유자는 자기 장비와 모델 자세만 되돌리고 이 클래스의 모션은 건드리지 않는다
        private void RevokeExternalControl(EBossViewControlRevoke reason, bool returnToIdle)
        {
            IBossViewControlOwner owner = _controlOwner;
            if (owner == null)
            {
                return;
            }

            _controlOwner = null;
            owner.OnViewControlRevoked(reason);
            if (returnToIdle)
            {
                PlayGimmickMotion(IDLE_STATE, CONTROL_RETURN_SPEED, CONTROL_RETURN_BLEND);
            }
        }

        /// <summary> 서버가 알린 P07 워프 직전 출발 위치를 워프 표현에 넘긴다 </summary>
        public void SetWarpDeparture(int castId, Vector3 departure)
        {
            if (_warp != null)
            {
                _warp.SetDeparture(castId, departure, ReadClock());
            }
        }

        /// <summary> 모델을 이루는 렌더러, 워프 표현이 숨기고 보일 때 쓴다 </summary>
        public Renderer[] ModelRenderers => _modelRenderers;

        /// <summary> 마지막으로 서버 위치에 자리를 잡았던 표시 위치, 서버가 위치를 갑자기 옮기기 직전의 출발 위치를 알 때 쓴다 </summary>
        public bool TryGetStablePosition(out Vector3 position)
        {
            position = _stablePosition;
            return _hasStablePosition;
        }

        /// <summary> 서버 위치가 바뀌어도 모델을 이 위치에 붙잡아 둔다, 서버 판정 위치와 모델 표시 위치를 분리할 때 쓴다 </summary>
        public void BeginPositionHold(Vector3 position)
        {
            _isHeld       = true;
            _holdPosition = position;
            transform.position = position;
            _onTeleported.Invoke();
        }

        /// <summary> 모델을 서버 위치로 바로 옮긴다, 늦게 받은 사건처럼 부드럽게 따라갈 때가 아닐 때 쓴다 </summary>
        public void SnapToServerPosition()
        {
            if (_hasPose)
            {
                transform.position = _targetPosition;
                _onTeleported.Invoke();
            }
        }

        /// <summary> 붙잡기를 푼다. snapToServer가 참이면 서버 위치로 바로 옮기고 거짓이면 부드럽게 따라간다 </summary>
        public void EndPositionHold(bool snapToServer)
        {
            if (!_isHeld)
            {
                return;
            }

            _isHeld = false;

            if (snapToServer && _hasPose)
            {
                transform.position = _targetPosition;
                _onTeleported.Invoke();
            }
        }

        /// <summary> 모션과 위치 기억을 처음으로 되돌린다, 전투를 다시 시작할 때 쓴다 </summary>
        public void ResetView()
        {
            RevokeExternalControl(EBossViewControlRevoke.Reset, false);

            bool wasDefeated = _isDefeated;
            _hasPose    = false;
            _hasCast    = false;
            _isDefeated = false;
            _isHeld     = false;
            _hasStablePosition = false;
            _current    = default;

            if (_motion != null)
            {
                if (wasDefeated)
                {
                    _motion.ClearDeath();
                }
                _motion.Reset();
            }
            if (_talismanView != null)
            {
                _talismanView.ResetNetworkSession();
            }
            if (_colorWardView != null)
            {
                _colorWardView.ResetNetworkSession();
            }
            if (_foxfireSealView != null)
            {
                _foxfireSealView.ResetNetworkSession();
            }
            if (_finalAreaView != null)
            {
                _finalAreaView.ResetNetworkSession();
            }

            if (_dissolve != null)
            {
                _dissolve.ResetDeath();
            }

            if (_warp != null)
            {
                _warp.Reset();
            }
        }

        private void Unbind()
        {
            if (_presenter != null)
            {
                _presenter.PhaseChanged -= OnPhaseChanged;

                if (_patternVfx != null)
                {
                    _patternVfx.Unbind();
                }
            }

            _presenter = null;
        }

        private double ReadClock()
        {
            return (_serverClock != null) ? _serverClock() : Time.timeAsDouble;
        }

        // 새 시전이 시작되면 그것을 따라가고, 따라가던 시전이 끝나면 놓는다
        private void OnPhaseChanged(int castId, EBossTelegraphPhase phase)
        {
            ForwardToMotion(castId, phase);

            if (phase == EBossTelegraphPhase.Telegraph)
            {
                _castId  = castId;
                _hasCast = true;
                return;
            }

            if ((_hasCast) && (castId == _castId) &&
                ((phase == EBossTelegraphPhase.Finished) || (phase == EBossTelegraphPhase.Cancelled)))
            {
                _hasCast = false;
            }
        }

        // 확정한 모션과 워프 표현에 시전 단계를 전한다. 끝난 시전은 번호만으로도 정리한다
        private void ForwardToMotion(int castId, EBossTelegraphPhase phase)
        {
            bool isEnded = (phase == EBossTelegraphPhase.Finished) || (phase == EBossTelegraphPhase.Cancelled);

            if (isEnded)
            {
                // 예고 표현의 정상 종료는 지면 연출이 끝났다는 뜻이라 공격 모션의 끝이 아니다. 모션은 서버 시전 종료(EndServerCast)나 클립 끝에서 끝난다
                if (phase == EBossTelegraphPhase.Cancelled)
                {
                    _motion.OnCastEnded(castId, true);
                }

                if (_warp != null)
                {
                    _warp.OnCastEnded(castId, phase == EBossTelegraphPhase.Cancelled);
                }

                if (_patternVfx != null)
                {
                    _patternVfx.OnCastEnded(castId, phase == EBossTelegraphPhase.Cancelled);
                }

                return;
            }

            if ((_presenter == null) || (!_presenter.TryGetVisuals(castId, out BossTelegraphCast cast, out _, out _)))
            {
                return;
            }

            double now = ReadClock();
            _motion.OnCastPhase(cast, phase, now);

            if (_warp != null)
            {
                _warp.OnCastPhase(cast, phase, now);
            }

            if (_patternVfx != null)
            {
                _patternVfx.OnCastPhase(cast, phase, now);
            }
        }

        // 위치는 서버 값을 부드럽게 따라가되 돌진 중에는 서버가 보낸 시작 위치와 방향과 속도로 직접 계산한다.
        // 복제 위치는 돌진처럼 짧고 빠른 이동을 성기게 보내기 때문이다
        private void FollowPose(float deltaTime)
        {
            if (!_hasPose)
            {
                return;
            }

            Vector3 target = _isHeld ? _holdPosition : _targetPosition;
            if ((!_isHeld) && TryGetDashPosition(out Vector3 dashPosition))
            {
                target = dashPosition;
            }

            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-POSITION_SMOOTH * deltaTime));

            // 서버 위치가 갑자기 멀리 옮겨 가기 직전의 표시 위치를 출발 위치로 쓰려고 자리를 잡은 마지막 위치를 기억한다
            if ((!_isHeld) && ((transform.position - _targetPosition).sqrMagnitude <= (STABLE_RADIUS * STABLE_RADIUS)))
            {
                _stablePosition    = transform.position;
                _hasStablePosition = true;
            }

            Quaternion look = Quaternion.LookRotation(_targetForward, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-TURN_SMOOTH * deltaTime));
        }

        private bool TryGetDashPosition(out Vector3 position)
        {
            position = default;

            if ((!_hasCast) || (_presenter == null) || (!_presenter.TryGetVisuals(_castId, out BossTelegraphCast cast, out _, out _)))
            {
                return false;
            }

            if ((cast.Shape != EBossTelegraphShape.Dash) || (!cast.HasExecuted))
            {
                return false;
            }

            position = cast.Origin + (cast.Direction * cast.GetDashTraveled(ReadClock()));
            return true;
        }

        // 지금 시전 단계에 맞는 목표 자세로 현재 자세를 부드럽게 옮긴다
        private void UpdateMotion(float deltaTime)
        {
            Pose  target = IdlePose();
            float smooth = IDLE_SMOOTH;

            if (_isDefeated)
            {
                target = _motion.HasDeathState ? default : DownPose();
            }
            else if ((_hasCast) && (_presenter != null) &&
                (_presenter.TryGetVisuals(_castId, out BossTelegraphCast cast, out _, out _))
                && (!WolbaekMotionDirector.OwnsBody(cast.PatternId)) && (!MucheolMotionCatalog.Handles(cast.PatternId)))
            {
                // 확정한 모션이 있는 패턴(월백, 무철)은 클립이 몸을 쓰므로 임시 도형 자세를 덧붙이지 않는다
                target = CastPose(cast, ref smooth);
            }

            _current = Pose.Lerp(in _current, in target, 1f - Mathf.Exp(-smooth * deltaTime));
        }

        private Pose CastPose(BossTelegraphCast cast, ref float smooth)
        {
            Pose   idle = IdlePose();
            double now = ReadClock();

            if (!cast.HasExecuted)
            {
                smooth = GATHER_SMOOTH;
                Pose gather = GatherPose(cast.Shape, cast.PatternId);
                return Pose.Lerp(in idle, in gather, Mathf.SmoothStep(0f, 1f, cast.GetTelegraphProgress(now)));
            }

            smooth = RELEASE_SMOOTH;

            if (cast.Shape == EBossTelegraphShape.Dash)
            {
                bool isDashing = cast.GetExecuteAge(now) <= (cast.GetDashEndAge() + DASH_END_MARGIN);
                return isDashing ? DashPose() : idle;
            }

            // 발동 직후에만 자세를 내밀고 곧 풀어서 다음 예고의 모으기와 이어지게 한다
            if (cast.GetExecuteAge(now) > RELEASE_SECONDS)
            {
                smooth = IDLE_SMOOTH;
                return idle;
            }

            return ReleasePose(cast.Shape, cast.PatternId);
        }

        // 대기 클립이 호흡을 하므로 클립이 붙어 있으면 임시 호흡은 더하지 않는다
        private Pose IdlePose()
        {
            if (_hasClip)
            {
                return default;
            }

            float breath = Mathf.Sin(Time.time * BREATH_RATE);
            return new Pose
            {
                SpinePitch = breath * BREATH_PITCH,
                Lift       = (breath + 1f) * HALF * BREATH_LIFT
            };
        }

        // 예고가 끝나는 순간의 자세, 모양마다 다르게 모아서 어떤 공격인지 몸으로도 읽히게 한다
        private Pose GatherPose(EBossTelegraphShape shape, string patternId)
        {
            Vector3 values = _gatherSideArms;

            if (shape == EBossTelegraphShape.Circle)
            {
                // 장판(P05)도 원형 예고를 쓰지만 팔을 앞으로 뻗어 지정 위치를 가리킨다
                values = IsAreaPattern(patternId) ? _gatherThrow : _gatherCircle;
            }
            else if (shape == EBossTelegraphShape.Projectile)
            {
                values = _gatherThrow;
            }

            return new Pose
            {
                PivotPitch = (shape == EBossTelegraphShape.Dash) ? DASH_WINDUP_PITCH : 0f,
                SpinePitch = values.x,
                HeadPitch  = values.x * -HALF,
                ArmSide    = values.y,
                ArmForward = values.z,
                Lift       = BREATH_LIFT
            };
        }

        // 발동 순간의 자세, 모으던 반대 방향으로 튕겨 타격이 나가는 순간을 강조한다
        private Pose ReleasePose(EBossTelegraphShape shape, string patternId)
        {
            Vector3 values = _gatherSideArms;

            if (shape == EBossTelegraphShape.Circle)
            {
                values = IsAreaPattern(patternId) ? _gatherThrow : _gatherCircle;
            }
            else if (shape == EBossTelegraphShape.Projectile)
            {
                values = _gatherThrow;
            }

            return new Pose
            {
                SpinePitch = -values.x + (values.x * -HALF),
                HeadPitch  = values.x * HALF,
                ArmSide    = values.y * HALF,
                ArmForward = values.z * RELEASE_ARM_SCALE,
                Lift       = -BREATH_LIFT
            };
        }

        private static bool IsAreaPattern(string patternId)
        {
            return (patternId == AREA_PATTERN_ID) || (patternId == AREA_PHASE_TWO_ID);
        }

        private Pose DashPose()
        {
            return new Pose
            {
                PivotPitch = DASH_PIVOT_PITCH,
                SpinePitch = DASH_SPINE_PITCH,
                HeadPitch  = DASH_HEAD_PITCH,
                ArmSide    = DASH_ARM_SIDE,
                ArmForward = DASH_ARM_FORWARD,
                Lift       = 0f
            };
        }

        private Pose DownPose()
        {
            return new Pose
            {
                PivotPitch = DOWN_PITCH,
                ArmSide    = DOWN_ARM_SIDE,
                Lift       = DOWN_LIFT
            };
        }

        /****************************************
        *               Bones
        ****************************************/

        private void CacheBones()
        {
            _bones.Clear();

            _spine    = Cache(HumanBodyBones.Spine);
            _chest    = Cache(HumanBodyBones.Chest);
            _head     = Cache(HumanBodyBones.Head);
            _leftArm  = Cache(HumanBodyBones.LeftUpperArm);
            _rightArm = Cache(HumanBodyBones.RightUpperArm);

            _hasBones = (_spine.Bone != null) && (_leftArm.Bone != null) && (_rightArm.Bone != null);

            // 컨트롤러가 없으면 아무도 본을 되돌리지 않으므로 매 프레임 기본 자세에서 시작한다.
            // 컨트롤러가 붙으면 그 포즈 위에 덧붙인다
            _resetsBind = _animator.runtimeAnimatorController == null;
            _hasClip    = !_resetsBind;
        }

        private BoneRef Cache(HumanBodyBones id)
        {
            Transform bone = _animator.GetBoneTransform(id);
            var       item = new BoneRef { Bone = bone, Bind = (bone != null) ? bone.localRotation : Quaternion.identity };

            if (bone != null)
            {
                _bones.Add(item);
            }

            return item;
        }

        private void ApplyPose(in Pose pose)
        {
            _pivot.localRotation = Quaternion.Euler(pose.PivotPitch, 0f, 0f);
            _pivot.localPosition = new Vector3(0f, pose.Lift, 0f);

            if (_resetsBind)
            {
                for (int i = 0; i < _bones.Count; ++i)
                {
                    _bones[i].Bone.localRotation = _bones[i].Bind;
                }
            }

            Vector3 right   = transform.right;
            Vector3 forward = transform.forward;

            // 위에서 아래로 부모 먼저 돌려야 자식의 세계 회전에 부모 회전이 이미 반영돼 있다
            if (_chest.Bone != null)
            {
                Rotate(_spine.Bone, right, pose.SpinePitch * HALF);
                Rotate(_chest.Bone, right, pose.SpinePitch * HALF);
            }
            else
            {
                Rotate(_spine.Bone, right, pose.SpinePitch);
            }

            Rotate(_head.Bone, right, pose.HeadPitch);

            // 휴머노이드가 앞을 보고 서 있을 때 오른팔은 몸의 오른쪽에 있어 앞 축 둘레 양의 회전이 팔을 옆으로 올린다
            Rotate(_rightArm.Bone, forward, pose.ArmSide);
            Rotate(_leftArm.Bone, forward, -pose.ArmSide);
            Rotate(_rightArm.Bone, right, -pose.ArmForward);
            Rotate(_leftArm.Bone, right, -pose.ArmForward);
        }

        // 본을 세계 축 둘레로 돌린다, 본이 없거나 각도가 0이면 아무것도 하지 않는다
        private static void Rotate(Transform bone, Vector3 axis, float degrees)
        {
            if ((bone == null) || (Mathf.Approximately(degrees, 0f)))
            {
                return;
            }

            bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        /****************************************
        *             Materials
        ****************************************/

        // 외부 모델의 재질은 내장 파이프라인용 Standard 셰이더라 URP에서 분홍색으로 깨진다. 원본 에셋은 고치지 않고
        // 이 인스턴스에만 같은 텍스처를 쓰는 URP Lit 재질을 입힌다. 정식 재질 가공은 아트 제작 절차에서 따로 한다
        // 만든 재질은 created에 담는다, 부른 쪽이 파괴할 책임을 진다. 프리워밍 어댑터가 같은 변환을 쓰려고 공개해 둔다
        /// <summary> 렌더러의 비 URP Lit 재질을 URP Lit 사본으로 바꾼다 </summary>
        public static void ConvertToUrpLit(Renderer[] renderers, List<Material> created)
        {
            Shader lit = Shader.Find(URP_LIT_SHADER);
            if (lit == null)
            {
                return;
            }

            var converted = new Dictionary<Material, Material>();
            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; ++i)
                {
                    Material source = materials[i];
                    if ((source == null) || (source.shader == lit))
                    {
                        continue;
                    }

                    if (!converted.TryGetValue(source, out Material target))
                    {
                        target = CreateUrpMaterial(source, lit);
                        converted.Add(source, target);
                        created.Add(target);
                    }

                    materials[i] = target;
                }

                renderer.sharedMaterials = materials;
            }
        }

        private void DestroyRuntimeMaterials()
        {
            for (int i = 0; i < _runtimeMaterials.Count; ++i)
            {
                if (_runtimeMaterials[i] != null)
                {
                    Destroy(_runtimeMaterials[i]);
                }
            }

            _runtimeMaterials.Clear();
        }

        private static Material CreateUrpMaterial(Material source, Shader lit)
        {
            var target = new Material(lit) { name = source.name + "_URP" };

            if (source.HasProperty(PROP_SRC_TEXTURE))
            {
                target.SetTexture(PROP_BASE_MAP, source.GetTexture(PROP_SRC_TEXTURE));
            }

            if (source.HasProperty(PROP_SRC_COLOR))
            {
                target.SetColor(PROP_BASE_COLOR, source.GetColor(PROP_SRC_COLOR));
            }

            if ((source.HasProperty(PROP_BUMP_MAP)) && (source.GetTexture(PROP_BUMP_MAP) != null))
            {
                target.SetTexture(PROP_BUMP_MAP, source.GetTexture(PROP_BUMP_MAP));
                target.EnableKeyword(KEYWORD_NORMAL);
            }

            if (source.HasProperty(PROP_SRC_SMOOTH))
            {
                target.SetFloat(PROP_SMOOTHNESS, source.GetFloat(PROP_SRC_SMOOTH));
            }

            target.SetFloat(PROP_CULL, (float)UnityEngine.Rendering.CullMode.Off);

            // 머리카락과 속눈썹은 알파 테스트 재질이라 같은 방식으로 자른다
            if (source.IsKeywordEnabled(KEYWORD_ALPHATEST))
            {
                target.SetFloat(PROP_ALPHA_CLIP, FLAG_ON);
                target.SetFloat(PROP_CUTOFF, source.HasProperty(PROP_CUTOFF) ? source.GetFloat(PROP_CUTOFF) : ALPHA_CUTOFF_DEFAULT);
                target.EnableKeyword(KEYWORD_ALPHATEST);
            }

            return target;
        }
    }
}
