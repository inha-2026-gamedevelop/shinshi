// System
using System;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.UI;

namespace Shinshi.Art
{
    /// <summary> 궁수 공개 전투 상태를 관찰해 속성 화살, 적중, 속성 부여, 속성 폭발 효과를 재생하는 표현 전용 뷰, 서버 판정과 이동에 관여하지 않는다 </summary>
    [DisallowMultipleComponent]
    public sealed class ArcherVfxView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        // 속성 순서(EArcherElement 값)로 화살, 총구, 적중 풀을 둔다
        private const int ELEMENT_COUNT = 4;
        private const int POOL_ARROW    = 0;
        private const int POOL_MUZZLE   = POOL_ARROW + ELEMENT_COUNT;
        private const int POOL_HIT      = POOL_MUZZLE + ELEMENT_COUNT;
        private const int POOL_CHARGE   = POOL_HIT + ELEMENT_COUNT;
        private const int POOL_BURST    = POOL_CHARGE + ELEMENT_COUNT;
        private const int POOL_HEAVY_ARROW  = POOL_BURST + ELEMENT_COUNT;
        private const int POOL_HEAVY_MUZZLE = POOL_HEAVY_ARROW + ELEMENT_COUNT;
        private const int POOL_HEAVY_HIT    = POOL_HEAVY_MUZZLE + ELEMENT_COUNT;
        private const int POOL_COUNT        = POOL_HEAVY_HIT + ELEMENT_COUNT;

        private const int ARROW_POOL_SIZE  = 8;
        private const int MUZZLE_POOL_SIZE = 4;
        private const int HIT_POOL_SIZE    = 4;
        private const int BURST_POOL_SIZE  = 2;
        private const int HEAVY_POOL_SIZE  = 3;

        // 발사 시각을 이만큼 넘겨 관찰한 총구 효과는 이미 지난 일이라 재생하지 않는다
        private const float  MAX_LATE_SECONDS       = 0.25f;
        private const float  MAX_BURST_LATE_SECONDS = 1f;
        private const float  MUZZLE_MAX_SECONDS     = 1.5f;
        private const float  HIT_MAX_SECONDS        = 1.5f;
        private const float  BURST_MAX_SECONDS      = 3f;
        // 적중 신호를 받으면 진행선이 적중 위치에서 옆으로 이 거리 안인 같은 속성 화살을 적중 거리에서 멈춘다.
        // 서버는 몸체 반경까지 맞히고 적중 위치는 대상 중심이므로 가장 큰 몸체 반경에 화면과 서버 출발 위치 차이를 더한다
        private const float  HIT_STOP_LATERAL_MARGIN = 0.5f;
        private const float  HIT_STOP_MAX_LATERAL    = Constants.Combat.MAX_BODY_RADIUS + HIT_STOP_LATERAL_MARGIN;
        private const string POOL_ROOT_NAME         = "ArcherVfxPool";

        [Header("관찰 대상")]
        [SerializeField] private PlayerCombatState _combat;

        [Header("속성 없음")]
        [SerializeField] private GameObject _arrowPrefab;
        [SerializeField] private GameObject _muzzlePrefab;
        [SerializeField] private GameObject _impactPrefab;

        [Header("얼음")]
        [SerializeField] private GameObject _iceArrowPrefab;
        [SerializeField] private GameObject _iceMuzzlePrefab;
        [SerializeField] private GameObject _iceHitPrefab;

        [Header("바람")]
        [SerializeField] private GameObject _windPrefab;
        [SerializeField] private GameObject _windMuzzlePrefab;
        [SerializeField] private GameObject _windHitPrefab;

        [Header("불")]
        [SerializeField] private GameObject _fireArrowPrefab;
        [SerializeField] private GameObject _fireMuzzlePrefab;
        [SerializeField] private GameObject _fireHitPrefab;

        [Header("속성 폭발")]
        [SerializeField] private GameObject _chargePrefab;
        [SerializeField] private GameObject _burstPrefab;
        // 차지와 폭발은 프리팹 하나를 속성 색으로 착색해 쓴다. 속성이 없으면 프리팹 원래 색이다
        [SerializeField] private Color _burstIceColor  = new Color(0.45f, 0.8f, 1f);
        [SerializeField] private Color _burstWindColor = new Color(0.5f, 1f, 0.55f);
        [SerializeField] private Color _burstFireColor = new Color(1f, 0.45f, 0.12f);

        [Header("속성 부여 오라")]
        [SerializeField] private GameObject _fullStackAuraPrefab;
        [SerializeField] private Color _auraIceColor = new Color(0.45f, 0.8f, 1f, 0.8f);
        [SerializeField] private Color _auraWindColor = new Color(0.5f, 1f, 0.55f, 0.8f);
        [SerializeField] private Color _auraFireColor = new Color(1f, 0.45f, 0.12f, 0.8f);
        [SerializeField, Min(1f)] private float _fullStackAuraSpeedMultiplier = 4f;
        [SerializeField, Min(1f)] private float _fullStackParticleSizeMultiplier = 6f;
        [SerializeField, Min(0f)] private float _fullStackParticleRadialSpeed = 1.2f;

        [Header("강공격 (HeavyBasicStep 단계의 기본 공격)")]
        [SerializeField] private GameObject _heavyArrowPrefab;
        [SerializeField] private GameObject _heavyMuzzlePrefab;
        [SerializeField] private GameObject _heavyHitPrefab;

        [Header("강공격 속성색 (기존 속성 화살의 잔광색)")]
        [SerializeField] private Color _heavyIceColor  = new Color(0.55f, 0.85f, 1f);
        [SerializeField] private Color _heavyWindColor = new Color(1f, 0.85f, 0.5f);
        [SerializeField] private Color _heavyFireColor = new Color(1f, 0.45f, 0.12f);

        private readonly ArcherVfxTracker _tracker = new ArcherVfxTracker();

        /// <summary> 이 콤보 단계의 기본 공격은 강공격 효과로 그린다. -1이면 모두 보통 효과다 </summary>
        public int HeavyBasicStep
        {
            get => _tracker.HeavyBasicStep;
            set => _tracker.HeavyBasicStep = value;
        }
        private readonly ArcherVfxPool[]  _pools   = new ArcherVfxPool[POOL_COUNT];

        private Transform _poolRoot;
        private bool      _hasConfig;
        private bool      _wasRunning;
        private float     _chargeSeconds;
        private GameObject _auraInstance;
        private ParticleSystem[] _auraParticles;
        private EArcherElement _auraElement;
        private Vector3[] _auraOrbitSpeeds;
        private ParticleSystem.MinMaxCurve[] _auraParticleSizes;
        private ParticleSystem.MinMaxCurve[] _auraRadialSpeeds;
        private bool _auraFast;
        private AudioSource _bowAudio;
        private AudioClip[] _bowClips;
        private readonly System.Random _bowRandom = new System.Random();

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            SoundVolume.PreviewChanged += OnBowVolumeChanged;
            OnBowVolumeChanged(SoundVolume.Load());
        }

        private void OnDisable()
        {
            SoundVolume.PreviewChanged -= OnBowVolumeChanged;
            if (_bowAudio != null)
            {
                _bowAudio.Stop();
            }
            _tracker.Reset();
            _wasRunning = false;
            StopAllEffects();
            StopAura();
        }

        private void OnDestroy()
        {
            StopAllEffects();
            if (_auraInstance != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_auraInstance);
                }
                else
                {
                    DestroyImmediate(_auraInstance);
                }
            }
            if (_poolRoot != null)
            {
                // 플레이 중에는 프레임 끝에 파괴하고, EditMode 직접 검증에서는 즉시 파괴한다
                if (Application.isPlaying)
                {
                    Destroy(_poolRoot.gameObject);
                }
                else
                {
                    DestroyImmediate(_poolRoot.gameObject);
                }

                _poolRoot = null;
            }
        }

        private void Update()
        {
            if (_combat == null)
            {
                return;
            }

            if (_combat.PlayerClass != EPlayerClass.Archer)
            {
                // 직업이 바뀌면 남은 효과를 멈추고, 궁수로 돌아오면 새 기준값부터 본다
                if (_wasRunning)
                {
                    _tracker.Reset();
                    _wasRunning = false;
                    StopAllEffects();
                    StopAura();
                }

                return;
            }

            if ((!_hasConfig) && (!TryConfigure()))
            {
                return;
            }

            _wasRunning = true;
            EnsurePools();
            UpdateAura();

            // SyncVar hook의 중간값이 아니라 이번 프레임에 모인 현재값으로만 판단한다
            double now = NetworkTime.time;
            ArcherVfxTracker.Observation observation;
            observation.PlayerClass          = _combat.PlayerClass;
            observation.LifeState            = _combat.LifeState;
            observation.AttackSequence       = _combat.AttackSequence;
            observation.AttackComboStep      = _combat.AttackComboStep;
            observation.AttackStartedAt      = _combat.AttackStartedAt;
            observation.LocomotionStartedAt  = _combat.LocomotionStartedAt;
            observation.DodgeSequence        = _combat.DodgeSequence;
            observation.ActionCancelSequence = _combat.ActionCancelSequence;
            observation.CastInstanceId       = _combat.CastInstanceId;
            observation.SkillPhase           = _combat.SkillPhase;
            observation.SkillKind            = _combat.SkillKind;
            observation.SkillSlot            = _combat.SkillSlot;
            observation.SkillResolveAt       = _combat.SkillResolveAt;
            observation.SkillAim             = _combat.SkillAim;
            observation.SkillPoint           = _combat.SkillPoint;
            observation.Facing               = FacingRules.DecodeYaw(_combat.FacingYaw);
            observation.LocomotionSpeed      = _combat.LocomotionSpeed;
            observation.ArcherElement        = _combat.ArcherElement;
            observation.ArrowHitSequence     = _combat.ArrowHitSequence;
            observation.ArrowHitPosition     = _combat.ArrowHitPosition;
            observation.ArrowHitElement      = _combat.ArrowHitElement;
            observation.ArrowHitIsHeavy      = _combat.ArrowHitIsHeavy;
            observation.Now                  = now;

            // 사망, 회피, 다음 시전은 아직 나가지 않은 발사만 버린다. 이미 나간 투사체는 풀이 자기 수명까지 유지한다
            int count = _tracker.Update(in observation);
            if (count > 0)
            {
                Vector3 origin = _combat.transform.position;
                for (int i = 0; i < count; ++i)
                {
                    PlayEffect(_tracker.GetPlay(i), origin, now);
                }
            }

            TickPools(now);
        }

        /****************************************
        *                Methods
        ****************************************/

        protected override void OnViewAwake()
        {
            // 프리팹은 부모 빌더가 주입하며, 전투 상태가 빠졌을 때만 부모에서 한 번 찾는다
            if (_combat == null)
            {
                _combat = GetComponentInParent<PlayerCombatState>();
            }
        }

        private void UpdateAura()
        {
            EArcherElement element = _combat.ArcherElement;
            if ((_fullStackAuraPrefab == null) || (_combat.LifeState != EParticipantLifeState.Alive)
                || (element == EArcherElement.None))
            {
                StopAura();
                return;
            }

            if (_auraInstance == null)
            {
                _auraInstance = Instantiate(_fullStackAuraPrefab, _combat.transform, false);
                _auraParticles = _auraInstance.GetComponentsInChildren<ParticleSystem>(true);
                _auraOrbitSpeeds = new Vector3[_auraParticles.Length];
                _auraParticleSizes = new ParticleSystem.MinMaxCurve[_auraParticles.Length];
                _auraRadialSpeeds = new ParticleSystem.MinMaxCurve[_auraParticles.Length];
                for (int i = 0; i < _auraParticles.Length; ++i)
                {
                    var velocity = _auraParticles[i].velocityOverLifetime;
                    _auraOrbitSpeeds[i] = new Vector3(velocity.orbitalXMultiplier, velocity.orbitalYMultiplier,
                        velocity.orbitalZMultiplier);
                    _auraParticleSizes[i] = _auraParticles[i].main.startSize;
                    _auraRadialSpeeds[i] = velocity.radial;
                }
            }

            int stackMax = GameDB.Skills.ClassStackMax;
            bool fast = (stackMax > 0) && (_combat.ClassStacks >= stackMax);
            if (fast != _auraFast)
            {
                float multiplier = fast ? Mathf.Max(1f, _fullStackAuraSpeedMultiplier) : 1f;
                for (int i = 0; i < _auraParticles.Length; ++i)
                {
                    var velocity = _auraParticles[i].velocityOverLifetime;
                    velocity.orbitalXMultiplier = _auraOrbitSpeeds[i].x * multiplier;
                    velocity.orbitalYMultiplier = _auraOrbitSpeeds[i].y * multiplier;
                    velocity.orbitalZMultiplier = _auraOrbitSpeeds[i].z * multiplier;
                    if (_auraParticles[i].gameObject != _auraInstance)
                    {
                        velocity.radial = fast ? new ParticleSystem.MinMaxCurve(Mathf.Max(0f, _fullStackParticleRadialSpeed))
                            : _auraRadialSpeeds[i];
                        var size = _auraParticleSizes[i];
                        float sizeMultiplier = fast ? Mathf.Max(1f, _fullStackParticleSizeMultiplier) : 1f;
                        if (size.mode == ParticleSystemCurveMode.TwoConstants)
                        {
                            size.constantMin *= sizeMultiplier;
                            size.constantMax *= sizeMultiplier;
                        }
                        else if (size.mode == ParticleSystemCurveMode.Constant)
                        {
                            size.constant *= sizeMultiplier;
                        }
                        else
                        {
                            size.curveMultiplier *= sizeMultiplier;
                        }

                        var main = _auraParticles[i].main;
                        main.startSize = size;
                    }
                }

                _auraFast = fast;
            }

            if (element == _auraElement)
            {
                return;
            }

            Color color = element == EArcherElement.Ice ? _auraIceColor
                : element == EArcherElement.Wind ? _auraWindColor : _auraFireColor;
            foreach (ParticleSystem particle in _auraParticles)
            {
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particle.main;
                main.startColor = particle.gameObject == _auraInstance ? Color.white : color;
                if (particle.gameObject == _auraInstance)
                {
                    var gradient = new Gradient();
                    gradient.SetKeys(
                        new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 0.65f),
                            new GradientColorKey(Color.white, 1f) },
                        new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.3f),
                            new GradientAlphaKey(1f, 1f) });
                    var trails = particle.trails;
                    trails.colorOverTrail = gradient;
                }
                particle.Play(true);
            }

            _auraElement = element;
        }

        private void StopAura()
        {
            if ((_auraParticles == null) || (_auraElement == EArcherElement.None))
            {
                return;
            }

            foreach (ParticleSystem particle in _auraParticles)
            {
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            _auraElement = EArcherElement.None;
        }

        private void EnsurePools()
        {
            if (_poolRoot != null)
            {
                return;
            }

            // 직업 공개값을 받은 궁수만 풀을 만든다. 다른 직업의 플레이어에는 파티클 복제 비용이 없다.
            _poolRoot = new GameObject(POOL_ROOT_NAME).transform;
            GameObject[] arrows  = { _arrowPrefab, _iceArrowPrefab, _windPrefab, _fireArrowPrefab };
            GameObject[] muzzles = { _muzzlePrefab, _iceMuzzlePrefab, _windMuzzlePrefab, _fireMuzzlePrefab };
            GameObject[] hits    = { _impactPrefab, _iceHitPrefab, _windHitPrefab, _fireHitPrefab };
            for (int i = 0; i < ELEMENT_COUNT; ++i)
            {
                _pools[POOL_ARROW + i]  = new ArcherVfxPool(arrows[i], _poolRoot, ARROW_POOL_SIZE);
                _pools[POOL_MUZZLE + i] = new ArcherVfxPool(muzzles[i], _poolRoot, MUZZLE_POOL_SIZE);
                _pools[POOL_HIT + i]    = new ArcherVfxPool(hits[i], _poolRoot, HIT_POOL_SIZE);
            }

            Color?[] burstColors = { null, _burstIceColor, _burstWindColor, _burstFireColor };
            for (int i = 0; i < ELEMENT_COUNT; ++i)
            {
                _pools[POOL_CHARGE + i] = new ArcherVfxPool(_chargePrefab, _poolRoot, BURST_POOL_SIZE, burstColors[i]);
                _pools[POOL_BURST + i]  = new ArcherVfxPool(_burstPrefab, _poolRoot, BURST_POOL_SIZE, burstColors[i]);
            }

            Color?[] heavyColors = { null, _heavyIceColor, _heavyWindColor, _heavyFireColor };
            for (int i = 0; i < ELEMENT_COUNT; ++i)
            {
                _pools[POOL_HEAVY_ARROW + i]  = new ArcherVfxPool(_heavyArrowPrefab, _poolRoot, HEAVY_POOL_SIZE, heavyColors[i]);
                _pools[POOL_HEAVY_MUZZLE + i] = new ArcherVfxPool(_heavyMuzzlePrefab, _poolRoot, HEAVY_POOL_SIZE, heavyColors[i]);
                _pools[POOL_HEAVY_HIT + i]    = new ArcherVfxPool(_heavyHitPrefab, _poolRoot, HEAVY_POOL_SIZE, heavyColors[i]);
            }
        }

        // GameDB는 Awake 시점에 준비되지 않을 수 있어 초기화될 때까지 다음 프레임에 다시 시도한다
        private bool TryConfigure()
        {
            if (!GameDB.IsInitialized)
            {
                return false;
            }

            // 명세를 읽지 못한 값은 0으로 두어 해당 발사를 재생하지 않는다
            var config = new ArcherVfxTracker.Config();
            config.MaxLateSeconds      = MAX_LATE_SECONDS;
            config.MaxBurstLateSeconds = MAX_BURST_LATE_SECONDS;
            config.ServerTickSeconds   = 1f / Constants.Network.SERVER_TICK_RATE;
            config.WindSpeedScale      = ArcherElementRules.GetProjectileSpeedScale(GameDB.Skills, EArcherElement.Wind);
            try
            {
                var basic = GameDB.Player.GetBasicAttack(EPlayerClass.Archer);
                config.BasicWindup       = basic.Windup;
                if (basic.HasCombo)
                {
                    config.BasicStepWindups = new float[basic.ComboStepCount];
                    config.BasicStepShotCounts = new int[basic.ComboStepCount];
                    config.BasicStepShotIntervals = new float[basic.ComboStepCount];
                    for (int i = 0; i < config.BasicStepWindups.Length; ++i)
                    {
                        config.BasicStepWindups[i] = basic.GetWindup(i);
                        config.BasicStepShotCounts[i] = basic.GetShotCount(i);
                        config.BasicStepShotIntervals[i] = basic.GetShotInterval(i);
                        if (basic.IsHeavy(i))
                        {
                            _tracker.HeavyBasicStep = i;
                        }
                    }
                }
                config.BasicCancelOnMove = basic.CancelOnMove;
                config.Basic.Speed       = basic.ProjectileSpeed;
                config.Basic.Range       = basic.Range;
            }
            catch (ArgumentOutOfRangeException)
            {
                config.Basic = default;
            }

            for (int slot = 0; slot < ArcherVfxTracker.SLOT_COUNT; ++slot)
            {
                var spec = new ArcherVfxTracker.ShotSpec();
                if (GameDB.Skills.TryGetSkillBySlot(EPlayerClass.Archer, slot, out SkillCatalogSO.SkillDefinition skill))
                {
                    if (skill.Area.Shape == ESkillShape.Projectile)
                    {
                        spec.Speed           = skill.Area.ProjectileSpeed;
                        spec.Range           = skill.Area.CastRange;
                        spec.ProjectileCount = skill.Effect.ProjectileCount;
                        spec.SpreadAngle     = skill.Effect.SpreadAngle;
                        spec.RepeatCount     = skill.Effect.RepeatCount;
                        spec.RepeatInterval  = skill.Effect.RepeatInterval;
                    }

                    _tracker.SetSkillElement(slot, skill.Effect.ArcherElement);
                    if (skill.Effect.RequiredStacks > 0)
                    {
                        config.HasBurstSlot = true;
                        config.BurstSlot    = slot;
                        _chargeSeconds      = skill.Timeline.Windup;
                    }
                }

                _tracker.SetSkill(slot, in spec);
            }

            _tracker.Configure(in config);
            _hasConfig = true;
            return true;
        }

        private void PlayEffect(ArcherVfxTracker.Play play, Vector3 origin, double now)
        {
            int element = Mathf.Clamp((int)play.Element, 0, ELEMENT_COUNT - 1);
            Vector3 direction = play.Direction;
            direction.y = 0f;
            Quaternion rotation = (direction.sqrMagnitude > Mathf.Epsilon) ? Quaternion.LookRotation(direction) : Quaternion.identity;
            switch (play.Kind)
            {
                case EArcherVfxKind.Arrow:
                    if (play.IsMuzzle)
                    {
                        if (play.StopsOnHit)
                        {
                            PlayBowSound();
                        }
                        Play(IsHeavyReady(play, POOL_HEAVY_MUZZLE + element) ? (POOL_HEAVY_MUZZLE + element) : (POOL_MUZZLE + element), origin, rotation, now,
                            MUZZLE_MAX_SECONDS);
                        return;
                    }

                    // 서버는 발사 시각의 시전자 위치에서 쏘므로 재생 순간의 캐릭터 위치에서 출발한다
                    ArcherVfxPool arrows = _pools[IsHeavyReady(play, POOL_HEAVY_ARROW + element) ? (POOL_HEAVY_ARROW + element) : (POOL_ARROW + element)];
                    if ((arrows != null) && (arrows.HasPrefab))
                    {
                        arrows.PlayProjectile(origin, play.Direction, play.StartAt, play.Speed, play.Range, now, play.StopsOnHit);
                    }

                    return;
                case EArcherVfxKind.Hit:
                    // 화면의 화살은 서버 적중을 모르고 사거리까지 날아가므로 적중 자리에서 멈춘다
                    ArcherVfxPool flying = _pools[IsHeavyReady(play, POOL_HEAVY_ARROW + element) ? (POOL_HEAVY_ARROW + element) : (POOL_ARROW + element)];
                    if (flying != null)
                    {
                        flying.StopProjectileAtHit(play.Position, HIT_STOP_MAX_LATERAL, now);
                    }

                    Play(IsHeavyReady(play, POOL_HEAVY_HIT + element) ? (POOL_HEAVY_HIT + element) : (POOL_HIT + element), play.Position, rotation, now,
                        HIT_MAX_SECONDS);
                    return;
                case EArcherVfxKind.Switch:
                    Play(POOL_MUZZLE + element, origin, rotation, now, MUZZLE_MAX_SECONDS);
                    return;
                case EArcherVfxKind.Charge:
                    Play(POOL_CHARGE + element, origin, rotation, now, Mathf.Max(MAX_LATE_SECONDS, _chargeSeconds));
                    return;
                case EArcherVfxKind.Burst:
                    Play(POOL_BURST + element, play.Position, Quaternion.identity, now, BURST_MAX_SECONDS);
                    return;
                default:
                    return;
            }
        }

        private void PlayBowSound()
        {
            if (_bowClips == null)
            {
                _bowClips = new[]
                {
                    Resources.Load<AudioClip>("Bow1"),
                    Resources.Load<AudioClip>("Bow10"),
                    Resources.Load<AudioClip>("Bow11"),
                    Resources.Load<AudioClip>("Bow12")
                };
            }

            if (_bowAudio == null)
            {
                _bowAudio = gameObject.AddComponent<AudioSource>();
                _bowAudio.playOnAwake = false;
                _bowAudio.spatialBlend = 1f;
                _bowAudio.rolloffMode = AudioRolloffMode.Linear;
                _bowAudio.maxDistance = 60f;
                OnBowVolumeChanged(SoundVolume.Load());
            }

            AudioClip clip = _bowClips[_bowRandom.Next(_bowClips.Length)];
            if (clip != null)
            {
                _bowAudio.PlayOneShot(clip);
            }
        }

        private void OnBowVolumeChanged(SoundVolume volume)
        {
            if (_bowAudio != null)
            {
                _bowAudio.volume = volume.Sfx;
            }
        }

        // 강공격이고 그 풀에 프리팹이 있을 때만 강공격 효과를 쓴다. 없으면 속성별 보통 효과로 대신한다
        private bool IsHeavyReady(ArcherVfxTracker.Play play, int heavyPool)
        {
            return (play.IsHeavy) && (_pools[heavyPool] != null) && (_pools[heavyPool].HasPrefab);
        }

        private void Play(int index, Vector3 position, Quaternion rotation, double now, float maxSeconds)
        {
            ArcherVfxPool pool = _pools[index];
            if ((pool == null) || (!pool.HasPrefab))
            {
                return;
            }

            pool.PlayBrief(position, rotation, now, maxSeconds);
        }

        private void TickPools(double now)
        {
            for (int i = 0; i < _pools.Length; ++i)
            {
                if (_pools[i] != null)
                {
                    _pools[i].Tick(now);
                }
            }
        }

        private void StopAllEffects()
        {
            for (int i = 0; i < _pools.Length; ++i)
            {
                if (_pools[i] != null)
                {
                    _pools[i].StopAll();
                }
            }
        }
    }
}
