// Unity
using UnityEngine;

using UnityEngine.Rendering.Universal;

// 서드파티
using Mirror;
using Unity.Cinemachine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Art
{
    /// <summary> 내 캐릭터의 적중 흔들림과 불똥, 마검사 섬광 일섬 시전 줌을 입힌다. 서버가 이미 보내는 피해 알림만 쓴다 </summary>
    public sealed class CombatCameraFeedback : ClientView
    {
        // 새 네트워크 멤버 없이 소유 플레이어가 받는 피해 알림과 공개 전투 상태(시전 번호, 슬롯, 확정 시각)로 판단한다.
        // 흔들림은 Cinemachine 카메라 오프셋 확장으로, 줌은 쿼터뷰 리그의 시야각 배율로 겹쳐 쓰므로 줌 단계 전환과 부딪치지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const float DEFAULT_SHAKE_SECONDS  = 0.2f;
        private const float DIRECTION_EPSILON_SQR  = 0.0001f;
        // 치명타 줌 펀치는 짧게 당겼다가 바로 풀므로 유지 시간과 당기는 감쇠 시간이 매우 짧다
        private const float CRIT_ZOOM_HOLD_SECONDS = 0.04f;
        private const float CRIT_ZOOM_IN_SMOOTH    = 0.04f;

        [Header("참조")]
        [SerializeField] private QuarterViewCameraRig _rig;
        [SerializeField] private CinemachineCamera    _camera;

        [Header("섬광 일섬 줌")]
        [SerializeField, Range(0f, 0.6f)] private float _blinkZoomAmount   = 0.2f;
        // 당기고 풀 때의 감쇠 시간, 클수록 부드럽고 느리게 따라간다
        [SerializeField, Min(0.01f)]      private float _zoomInSmooth      = 0.12f;
        [SerializeField, Min(0.01f)]      private float _zoomOutSmooth     = 0.2f;

        [Header("적중 흔들림")]
        // 모든 직업의 일반 적중
        [SerializeField, Min(0f)] private float _lightShake       = 0.06f;
        // 마검사 검파와 섬광 일섬 적중
        [SerializeField, Min(0f)] private float _waveShake        = 0.2f;
        [SerializeField, Min(0f)] private float _blinkShakeScale  = 1.2f;
        [SerializeField, Min(0.01f)] private float _shakeSeconds  = DEFAULT_SHAKE_SECONDS;

        [Header("적중 불똥")]
        // 비어 있으면 불똥을 내지 않는다
        [SerializeField] private Material _sparkMaterial;
        [SerializeField, Min(0)] private int _lightSparkCount = 10;
        [SerializeField, Min(0)] private int _heavySparkCount = 28;
        [SerializeField, Min(1f)] private float _critSparkMultiplier = 2.5f;

        [Header("치명타 화면 연출")]
        // 렌더러의 풀스크린 패스와 그 재질. 비어 있으면 블러와 임팩트 프레임은 건너뛴다
        [SerializeField] private FullScreenPassRendererFeature _critBlurFeature;
        [SerializeField] private Material _critBlurMaterial;
        [SerializeField, Range(0f, 1f)]   private float _critBlurIntensity = 0.8f;
        [SerializeField, Min(0.01f)]      private float _critBlurSeconds   = 0.45f;
        // 0이면 줌 펀치를 쓰지 않는다
        [SerializeField, Range(0f, 0.5f)] private float _critZoomPunch     = 0.08f;
        [SerializeField, Min(0.01f)]      private float _critZoomSeconds   = 0.3f;
        // 0이면 번쩍임을 쓰지 않는다
        [SerializeField, Range(0f, 1f)]   private float _critFlashAlpha    = 0f;
        [SerializeField, Min(0.01f)]      private float _critFlashSeconds  = 0.08f;

        [Header("치명타 임팩트 프레임")]
        [SerializeField] private bool _useImpactFrame = true;
        [SerializeField, Range(1, 20)] private int _impactFrames = 6;
        // 부드러운 방식은 배경을 회색으로 어둡게 하고 서서히 풀린다. 끄면 흑백과 강조색의 강한 방식이다
        [SerializeField] private bool _impactSoft = true;
        [SerializeField, Range(0f, 1f)] private float _impactDim = 0.6f;
        [SerializeField, Range(0f, 1f)] private float _impactThreshold = 0.12f;
        // 이보다 밝은 곳(번쩍인 몸, 불똥)은 색을 남기거나 강조색으로 칠한다
        [SerializeField, Range(0f, 2f)] private float _impactHotThreshold = 0.5f;
        [SerializeField] private bool _impactInvert = true;
        [SerializeField, Min(0f)] private float _impactCooldownSeconds = 0.4f;

        [Header("분류")]
        // 피해 알림 시각과 시전 시각 비교에 두는 여유
        [SerializeField, Min(0f)] private float _classifyMargin = 0.15f;

        private readonly CombatCameraEffects      _effects    = new CombatCameraEffects();
        private readonly MageknightHitClassifier  _classifier = new MageknightHitClassifier();
        private readonly DamageFeedbackReceiver   _receiver   = new DamageFeedbackReceiver();
        private readonly HitFeedbackCooldown      _impactCooldown = new HitFeedbackCooldown();
        private HitSparkPool                      _sparks     = null;
        private HitFeedbackMotionBlur             _motionBlur = null;
        private HitFeedbackScreenFx               _screenFx   = null;

        private NetworkIdentity         _boundPlayer = null;
        private PlayerInputState        _input       = null;
        private PlayerCombatState       _combat      = null;
        private CinemachineCameraOffset _offset      = null;
        private int                     _lastCastId  = 0;
        private bool                    _isConfigured = false;
        private float                   _blinkHoldSeconds = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if (_sparkMaterial != null)
            {
                _sparks = new GameObject("HitSparks").AddComponent<HitSparkPool>();
                _sparks.transform.SetParent(transform, false);
                _sparks.Initialize(_sparkMaterial);
            }

            if ((_critBlurFeature != null) && (_critBlurMaterial != null))
            {
                _motionBlur = new GameObject("CritMotionBlur").AddComponent<HitFeedbackMotionBlur>();
                _motionBlur.transform.SetParent(transform, false);
                _motionBlur.Init(_critBlurFeature, _critBlurMaterial);
            }

            if (_critFlashAlpha > 0f)
            {
                _screenFx = new GameObject("CritScreenFx").AddComponent<HitFeedbackScreenFx>();
                _screenFx.transform.SetParent(transform, false);
            }

            if (_camera != null)
            {
                _offset = _camera.GetComponent<CinemachineCameraOffset>();
                if (_offset == null)
                {
                    _offset = _camera.gameObject.AddComponent<CinemachineCameraOffset>();
                }
            }
        }

        private void OnDisable()
        {
            Unbind();
        }

        // Cinemachine이 LateUpdate에서 카메라 상태를 만들기 전에 값을 넣어야 해서 Update에서 계산한다
        private void Update()
        {
            if ((_rig == null) || (_camera == null))
            {
                return;
            }

            EnsureBound();
            if (_combat == null)
            {
                ClearEffects();
                return;
            }

            if ((!_isConfigured) && (!TryConfigure()))
            {
                return;
            }

            if (_combat.PlayerClass == EPlayerClass.Mageknight)
            {
                ObserveCast();
            }

            _effects.Tick(Time.unscaledTimeAsDouble, Time.unscaledDeltaTime);
            _rig.SetCombatZoom(_effects.ZoomScale);
            if (_offset != null)
            {
                _offset.Offset = _effects.ShakeOffset;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        // 로컬 플레이어는 스폰 뒤에 정해지고 재접속하면 새 개체로 바뀌므로 바뀐 순간에만 다시 묶는다
        private void EnsureBound()
        {
            NetworkIdentity localPlayer = NetworkClient.localPlayer;
            if (localPlayer == _boundPlayer)
            {
                return;
            }

            Unbind();
            if (localPlayer == null)
            {
                return;
            }

            _boundPlayer = localPlayer;
            _input       = localPlayer.GetComponent<PlayerInputState>();
            _combat      = localPlayer.GetComponent<PlayerCombatState>();
            if (_input != null)
            {
                _input.DamageFeedbackReceived += OnDamageFeedback;
            }

            _classifier.Reset();
            _effects.Reset();
            _impactCooldown.Reset();
            _lastCastId = 0;
        }

        private void Unbind()
        {
            if (_input != null)
            {
                _input.DamageFeedbackReceived -= OnDamageFeedback;
            }

            _boundPlayer = null;
            _input       = null;
            _combat      = null;
            ClearEffects();
        }

        private void ClearEffects()
        {
            if (!_effects.IsActive)
            {
                return;
            }

            _effects.Reset();
            _rig.SetCombatZoom(1f);
            if (_offset != null)
            {
                _offset.Offset = Vector3.zero;
            }
        }

        // GameDB는 Awake 시점에 준비되지 않을 수 있어 초기화될 때까지 다음 프레임에 다시 시도한다
        private bool TryConfigure()
        {
            if (!GameDB.IsInitialized)
            {
                return false;
            }

            float waveSeconds  = 0f;
            float blinkSeconds = 0f;
            if (GameDB.Skills.TryGetSkillBySlot(EPlayerClass.Mageknight, Constants.Combat.SKILL_SLOT_W, out SkillCatalogSO.SkillDefinition wave))
            {
                waveSeconds = (float)MageknightVfxTracker.WaveTravelSeconds(wave.Area.ProjectileSpeed, wave.Area.CastRange);
            }

            if (GameDB.Skills.TryGetSkillBySlot(EPlayerClass.Mageknight, Constants.Combat.SKILL_SLOT_R, out SkillCatalogSO.SkillDefinition blink))
            {
                blinkSeconds      = blink.Timeline.Active;
                _blinkHoldSeconds = blink.Timeline.Windup + blink.Timeline.Active;
            }

            _classifier.Configure(waveSeconds, blinkSeconds, _classifyMargin);
            _isConfigured = true;
            return true;
        }

        // 새 시전이 공개되면 어느 기술인지 기록하고, 섬광 일섬이면 준비와 발동 동안 화면을 당긴다
        private void ObserveCast()
        {
            int castId = _combat.CastInstanceId;
            if ((castId == _lastCastId) || (_combat.SkillPhase == ESkillPhase.Idle) || (_combat.SkillKind != ESkillRequestKind.Regular))
            {
                return;
            }

            _lastCastId = castId;
            bool isWave  = _combat.SkillSlot == Constants.Combat.SKILL_SLOT_W;
            bool isBlink = _combat.SkillSlot == Constants.Combat.SKILL_SLOT_R;
            _classifier.ObserveCast(isWave, isBlink, castId, _combat.SkillResolveAt);

            if (isBlink)
            {
                _effects.ZoomHold(_blinkZoomAmount, _blinkHoldSeconds, _zoomInSmooth, _zoomOutSmooth, Time.unscaledTimeAsDouble);
            }
        }

        private void OnDamageFeedback(DamageFeedback feedback)
        {
            if ((_input == null) || (!_input.isOwned) || (_combat == null))
            {
                return;
            }

            double now = DamageFeedbackRules.ReceptionTime(NetworkTime.time, NetworkClient.bufferTime);
            if (!_receiver.TryAccept(in feedback, _input.SessionEpoch, now))
            {
                return;
            }

            // 마검사의 검파와 섬광 일섬은 강한 연출, 나머지 적중은 모든 직업이 약한 연출이다
            MageknightHitClassifier.EKind kind = (_combat.PlayerClass == EPlayerClass.Mageknight)
                ? _classifier.Classify(feedback.ServerTime)
                : MageknightHitClassifier.EKind.Other;

            float strength = _lightShake;
            if (kind == MageknightHitClassifier.EKind.Wave)
            {
                strength = _waveShake;
            }
            else if (kind == MageknightHitClassifier.EKind.Blink)
            {
                strength = _waveShake * _blinkShakeScale;
            }

            Vector3 cameraDirection = ToCameraSpace(feedback.WorldPosition);
            if (!feedback.IsDamageOverTime)
            {
                _effects.Kick(cameraDirection, strength, _shakeSeconds, Time.unscaledTimeAsDouble);
            }
            BurstSparks(feedback, kind != MageknightHitClassifier.EKind.Other);

            // 지속 피해는 틱마다 들어오므로 치명타로 보여 주지 않는다
            if ((feedback.IsCritical) && (!feedback.IsDamageOverTime))
            {
                PlayCriticalScreen();
            }
        }

        // 내 치명타에만 거는 화면 연출. 이 클라이언트 화면에만 보이는 표현이라 서버 판정과 이동에는 관여하지 않는다
        private void PlayCriticalScreen()
        {
            _motionBlur?.Pulse(_critBlurIntensity, _critBlurSeconds);

            // 이미 당겨진 줌(섬광 일섬)이 있으면 그 줌을 끊지 않는다
            if (!_effects.IsZooming)
            {
                _effects.ZoomHold(_critZoomPunch, CRIT_ZOOM_HOLD_SECONDS, CRIT_ZOOM_IN_SMOOTH, _critZoomSeconds, Time.unscaledTimeAsDouble);
            }

            _screenFx?.Flash(_critFlashAlpha, _critFlashSeconds);

            if ((_useImpactFrame) && (_motionBlur != null) && (_impactCooldown.TryUse(Time.unscaledTimeAsDouble, _impactCooldownSeconds)))
            {
                _motionBlur.ImpactFrame(_impactFrames, _impactSoft, _impactDim, _impactThreshold, _impactHotThreshold, _impactInvert);
            }
        }

        // 맞은 자리에서 플레이어가 공격한 쪽으로 불똥을 튀긴다
        private void BurstSparks(DamageFeedback feedback, bool isHeavy)
        {
            if (_sparks == null)
            {
                return;
            }

            Vector3 direction = feedback.WorldPosition - _boundPlayer.transform.position;
            direction.y = 0f;
            int count = HitSparkPool.ResolveCount(isHeavy ? _heavySparkCount : _lightSparkCount, feedback.IsCritical,
                _critSparkMultiplier);
            _sparks.Burst(feedback.WorldPosition, direction.normalized, count, feedback.IsCritical);
        }

        // 맞은 쪽으로 밀리도록 플레이어에서 적중 지점으로 향하는 방향을 카메라 좌표로 바꾼다
        private Vector3 ToCameraSpace(Vector3 worldPosition)
        {
            Vector3 direction = worldPosition - _boundPlayer.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < DIRECTION_EPSILON_SQR)
            {
                direction = _camera.transform.forward;
            }

            return _camera.transform.InverseTransformDirection(direction.normalized);
        }
    }
}
