// System
using System.Collections.Generic;

// Unity
using UnityEngine;

using UnityEngine.Rendering.Universal;

// 서드파티
using Mirror;
using Unity.Cinemachine;

// 프로젝트 내부
using Shinshi.AI;
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

        private const string BLOOD_RESOURCE        = "DevHitBlood/PF_DevHitBlood";
        private const float KILL_BLOOD_HEIGHT      = 1f; // m, 개체 발밑 기준 몸통 높이
        private const float DEFAULT_SHAKE_SECONDS  = 0.2f;
        private const float DIRECTION_EPSILON_SQR  = 0.0001f;
        // 치명타 줌 펀치는 짧게 당겼다가 바로 풀므로 유지 시간과 당기는 감쇠 시간이 매우 짧다
        private const float CRIT_ZOOM_HOLD_SECONDS = 0.04f;
        private const float CRIT_ZOOM_IN_SMOOTH    = 0.04f;
        private const float MULTI_HIT_SHAKE_BONUS  = 0.25f; // 한 마리 더 맞힐 때마다 흔들림에 더하는 비율
        private const float MULTI_HIT_SHAKE_MAX    = 2f;    // 흔들림 배율 상한
        private const float HALF                   = 0.5f;

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
        // 한 프레임에 여럿을 맞히면 맞힌 수만큼 흔들림을 키운다. 0이면 합산하지 않는다
        [SerializeField, Min(0f)] private float _multiHitShakeBonus = MULTI_HIT_SHAKE_BONUS;
        [SerializeField, Min(1f)] private float _multiHitShakeMax   = MULTI_HIT_SHAKE_MAX;

        [Header("맞은 적 반응")]
        // 켜면 내 적중에 맞은 적이 잠시 굳어 떨고(렉프레임), 찌그러지고, 치명타면 금색으로 번쩍인다. 표현 전용이라 서버 위치와 판정은 그대로다
        [SerializeField] private bool _reactEnemies = true;
        [SerializeField, Min(0)] private int _lightLagFrames = 3;
        // 마검사 검파와 섬광 일섬 적중은 맞은 적이 더 오래 굳는다
        [SerializeField, Min(0)] private int _heavyLagFrames = 15;
        [SerializeField, Min(0)] private int _critExtraLagFrames = 5;
        [SerializeField, Min(0f)] private float _vibrateMeters = 0.05f;
        // 쿼터뷰는 카메라가 멀어 고정 미터 떨림이 묻히므로 맞은 자리에서 보이는 화면 높이의 이 비율로 키운다
        [SerializeField, Range(0f, 0.1f)] private float _vibrateScreenRatio = 0.008f;
        [SerializeField, Range(0f, 0.6f)] private float _punchAmount = 0.25f;
        [SerializeField, Min(1f)] private float _critPunchMultiplier = 1.6f;

        [Header("적중 불똥")]
        // 백열 섬광과 잔불로 나눠 튀는 불똥. 비어 있으면 아래 옛 불똥 재질로 대신한다
        [SerializeField] private HitSparkEmitter _sparkEmitterPrefab;
        // 불똥 프리팹이 없을 때만 쓰는 옛 방식의 재질. 둘 다 비어 있으면 불똥을 내지 않는다
        [SerializeField] private Material _sparkMaterial;
        [SerializeField, Min(0)] private int _lightSparkCount = 10;
        [SerializeField, Min(0)] private int _heavySparkCount = 28;
        [SerializeField, Min(1f)] private float _critSparkMultiplier = 2.5f;

        [Header("적중 혈흔")]
        // 켜면 Resources의 혈흔 프리팹을 세워 내 적중마다 분사와 바닥 혈흔을 낸다. 지속 피해는 제외한다
        [SerializeField] private bool _useBlood = true;

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
        private readonly HashSet<int>             _bossCombatIds = new HashSet<int>();
        private int _bossSessionEpoch = -1;
        private readonly HitFeedbackCooldown      _impactCooldown = new HitFeedbackCooldown();
        private HitSparkEmitter                   _emitter    = null;
        private HitSparkPool                      _sparks     = null;
        private Camera                            _mainCamera = null;
        private Vector3                           _shakeDirectionSum = Vector3.zero;
        private float                             _shakeBase  = 0f;
        private int                               _shakeFrame = -1;
        private int                               _shakeHits  = 0;
        private HitFeedbackMotionBlur             _motionBlur = null;
        private HitFeedbackScreenFx               _screenFx   = null;
        private HitFeedbackBlood                  _blood      = null;

        private NetworkIdentity        _boundPlayer = null;
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
            if (_sparkEmitterPrefab != null)
            {
                _emitter = Instantiate(_sparkEmitterPrefab, transform, false);
                _emitter.name = "HitSparkEmitter";
            }
            else if (_sparkMaterial != null)
            {
                _sparks = new GameObject("HitSparks").AddComponent<HitSparkPool>();
                _sparks.transform.SetParent(transform, false);
                _sparks.Initialize(_sparkMaterial);
            }

            if (_useBlood)
            {
                CreateBlood();
            }

            if ((_critBlurFeature != null) && (_critBlurMaterial != null))
            {
                _motionBlur = new GameObject("CritMotionBlur").AddComponent<HitFeedbackMotionBlur>();
                _motionBlur.transform.SetParent(transform, false);
                _motionBlur.Init(_critBlurFeature, _critBlurMaterial);
            }

            if ((_critFlashAlpha > 0f) || (_useBlood))
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

        private void OnEnable()
        {
            EnemyNetworkView.Killed += OnEnemyKilled;
            if (_blood != null)
            {
                _blood.gameObject.SetActive(true);
            }
        }

        private void OnDisable()
        {
            EnemyNetworkView.Killed -= OnEnemyKilled;
            Unbind();
            if (_blood != null)
            {
                _blood.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_blood != null)
            {
                Destroy(_blood.gameObject);
            }
        }

        // Cinemachine이 LateUpdate에서 카메라 상태를 만들기 전에 값을 넣어야 해서 Update에서 계산한다
        private void Update()
        {
            if ((_rig == null) || (_camera == null))
            {
                return;
            }

            EnsureBound();
            RememberBosses();
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
            _bossCombatIds.Clear();
            _bossSessionEpoch = -1;
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

            if (_bossSessionEpoch != feedback.SessionEpoch)
            {
                _bossCombatIds.Clear();
                _bossSessionEpoch = feedback.SessionEpoch;
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
            bool isHeavy = kind != MageknightHitClassifier.EKind.Other;
            if (!feedback.IsDamageOverTime)
            {
                KickAccumulated(cameraDirection, strength);
            }
            BurstSparks(feedback, isHeavy);
            if (!feedback.IsDamageOverTime)
            {
                if (!IsBossBloodTarget(feedback.TargetCombatId))
                {
                    BurstBlood(feedback);
                    if ((_useBlood) && (feedback.IsCritical))
                    {
                        _screenFx?.SplashBlood();
                    }
                }
                ReactEnemy(feedback, isHeavy);
            }

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

        // 같은 프레임에 맞힌 수만큼 흔들림을 키운다. 방향은 맞힌 방향들의 평균이고, 서로 상쇄되면 이번 방향을 쓴다.
        // Kick은 진행 중인 흔들림보다 약하면 무시하므로, 커진 값으로 다시 부르면 이번 프레임의 흔들림이 갱신된다
        private void KickAccumulated(Vector3 cameraDirection, float strength)
        {
            if (Time.frameCount != _shakeFrame)
            {
                _shakeFrame        = Time.frameCount;
                _shakeHits         = 0;
                _shakeBase         = 0f;
                _shakeDirectionSum = Vector3.zero;
            }

            ++_shakeHits;
            _shakeBase         = Mathf.Max(_shakeBase, strength);
            _shakeDirectionSum += cameraDirection;

            float   scale = Mathf.Min(_multiHitShakeMax, 1f + (_multiHitShakeBonus * (_shakeHits - 1)));
            Vector3 aim   = (_shakeDirectionSum.sqrMagnitude > DIRECTION_EPSILON_SQR) ? _shakeDirectionSum.normalized : cameraDirection;
            _effects.Kick(aim, _shakeBase * scale, _shakeSeconds, Time.unscaledTimeAsDouble);
        }

        // 내 적중에 맞은 적을 서버가 알려 준 전투 개체 번호로 찾아 굳고, 떨고, 찌그러지게 한다
        private void ReactEnemy(DamageFeedback feedback, bool isHeavy)
        {
            if (!_reactEnemies)
            {
                return;
            }

            Vector3 away = feedback.WorldPosition - _boundPlayer.transform.position;
            away.y = 0f;
            int   frames  = (isHeavy ? _heavyLagFrames : _lightLagFrames) + (feedback.IsCritical ? _critExtraLagFrames : 0);
            float vibrate = Mathf.Max(_vibrateMeters, VisibleHeightAt(feedback.WorldPosition) * _vibrateScreenRatio);
            float punch   = _punchAmount * (feedback.IsCritical ? _critPunchMultiplier : 1f);
            EnemyNetworkView.TryReact(feedback.TargetCombatId, new EnemyHitCue(away, frames, vibrate, punch, feedback.IsCritical));
        }

        // 화면에 보이는 세로 길이(m), 카메라를 못 찾으면 0이라 최소 떨림만 쓴다
        private float VisibleHeightAt(Vector3 point)
        {
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null)
                {
                    return 0f;
                }
            }

            if (_mainCamera.orthographic)
            {
                return _mainCamera.orthographicSize / HALF;
            }

            float distance = Mathf.Max(0f, Vector3.Dot(point - _mainCamera.transform.position, _mainCamera.transform.forward));
            return distance * Mathf.Tan(_mainCamera.fieldOfView * HALF * Mathf.Deg2Rad) / HALF;
        }

        // 맞은 자리에서 플레이어가 공격한 쪽으로 불똥을 튀긴다
        private void BurstSparks(DamageFeedback feedback, bool isHeavy)
        {
            if ((_emitter == null) && (_sparks == null))
            {
                return;
            }

            Vector3 direction = feedback.WorldPosition - _boundPlayer.transform.position;
            direction.y = 0f;
            int count = HitSparkPool.ResolveCount(isHeavy ? _heavySparkCount : _lightSparkCount, feedback.IsCritical,
                _critSparkMultiplier);
            if (_emitter != null)
            {
                _emitter.Burst(feedback.WorldPosition, direction.normalized, count, feedback.IsCritical);
                return;
            }

            _sparks.Burst(feedback.WorldPosition, direction.normalized, count, feedback.IsCritical);
        }

        // 혈흔 풀은 프리팹 하나로 Resources에서 읽는다. 없으면 혈흔만 건너뛴다
        private void CreateBlood()
        {
            var prefab = Resources.Load<GameObject>(BLOOD_RESOURCE);
            if ((prefab == null) || (!prefab.TryGetComponent(out HitFeedbackBlood bloodPrefab)))
            {
                Debug.LogWarning("[CombatCameraFeedback] 혈흔 프리팹이 없어 혈흔을 건너뛴다", this);
                return;
            }

            _blood = Instantiate(bloodPrefab);
            _blood.name = "HitBlood";
        }

        private bool IsBossBloodTarget(int combatId)
        {
            RememberBosses();
            return _bossCombatIds.Contains(combatId);
        }

        private void RememberBosses()
        {
            if ((_input != null) && (_bossSessionEpoch != _input.SessionEpoch))
            {
                _bossCombatIds.Clear();
                _bossSessionEpoch = _input.SessionEpoch;
            }
            for (int i = 0; i < BossBodyBlockerRegistry.Count; ++i)
            {
                if ((BossBodyBlockerRegistry.Get(i) is BossState boss) && (boss.CombatId > 0))
                {
                    _bossCombatIds.Add(boss.CombatId);
                }
            }
        }

        // 처치는 피해 알림에 없어 적 표현이 시체를 날리는 순간을 쓴다. 내 처치와 파티원의 처치를 모두 보여 준다.
        // 몸통 높이에서 플레이어 쪽으로 분사해 시체가 날아가는 반대편에 피가 튄다
        private void OnEnemyKilled(Vector3 position, Vector3 awayFromPlayer)
        {
            if (_blood == null)
            {
                return;
            }

            awayFromPlayer.y = 0f;
            _blood.Burst(position + (Vector3.up * KILL_BLOOD_HEIGHT), awayFromPlayer.normalized, true, false);
        }

        // 맞은 자리에서 플레이어가 공격한 쪽으로 피를 튀긴다. 처치 분량은 OnEnemyKilled가 따로 낸다
        private void BurstBlood(DamageFeedback feedback)
        {
            if (_blood == null)
            {
                return;
            }

            Vector3 direction = feedback.WorldPosition - _boundPlayer.transform.position;
            direction.y = 0f;
            _blood.Burst(feedback.WorldPosition, direction.normalized, false, feedback.IsCritical);
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
