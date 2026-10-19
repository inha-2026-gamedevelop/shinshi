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
        private const int POOL_BURST    = POOL_CHARGE + 1;
        private const int POOL_COUNT    = POOL_BURST + 1;

        private const int ARROW_POOL_SIZE  = 8;
        private const int MUZZLE_POOL_SIZE = 4;
        private const int HIT_POOL_SIZE    = 4;
        private const int BURST_POOL_SIZE  = 2;

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

        private readonly ArcherVfxTracker _tracker = new ArcherVfxTracker();
        private readonly ArcherVfxPool[]  _pools   = new ArcherVfxPool[POOL_COUNT];

        private Transform _poolRoot;
        private bool      _hasConfig;
        private bool      _wasRunning;
        private float     _chargeSeconds;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnDisable()
        {
            _tracker.Reset();
            _wasRunning = false;
            StopAllEffects();
        }

        private void OnDestroy()
        {
            StopAllEffects();
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
                }

                return;
            }

            if ((!_hasConfig) && (!TryConfigure()))
            {
                return;
            }

            _wasRunning = true;
            EnsurePools();

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

            _pools[POOL_CHARGE] = new ArcherVfxPool(_chargePrefab, _poolRoot, BURST_POOL_SIZE);
            _pools[POOL_BURST]  = new ArcherVfxPool(_burstPrefab, _poolRoot, BURST_POOL_SIZE);
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
                    for (int i = 0; i < config.BasicStepWindups.Length; ++i)
                    {
                        config.BasicStepWindups[i] = basic.GetWindup(i);
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
                        Play(POOL_MUZZLE + element, origin, rotation, now, MUZZLE_MAX_SECONDS);
                        return;
                    }

                    // 서버는 발사 시각의 시전자 위치에서 쏘므로 재생 순간의 캐릭터 위치에서 출발한다
                    ArcherVfxPool arrows = _pools[POOL_ARROW + element];
                    if ((arrows != null) && (arrows.HasPrefab))
                    {
                        arrows.PlayProjectile(origin, play.Direction, play.StartAt, play.Speed, play.Range, now, play.StopsOnHit);
                    }

                    return;
                case EArcherVfxKind.Hit:
                    // 화면의 화살은 서버 적중을 모르고 사거리까지 날아가므로 적중 자리에서 멈춘다
                    ArcherVfxPool flying = _pools[POOL_ARROW + element];
                    if (flying != null)
                    {
                        flying.StopProjectileAtHit(play.Position, HIT_STOP_MAX_LATERAL, now);
                    }

                    Play(POOL_HIT + element, play.Position, rotation, now, HIT_MAX_SECONDS);
                    return;
                case EArcherVfxKind.Switch:
                    Play(POOL_MUZZLE + element, origin, rotation, now, MUZZLE_MAX_SECONDS);
                    return;
                case EArcherVfxKind.Charge:
                    Play(POOL_CHARGE, origin, rotation, now, Mathf.Max(MAX_LATE_SECONDS, _chargeSeconds));
                    return;
                case EArcherVfxKind.Burst:
                    Play(POOL_BURST, play.Position, Quaternion.identity, now, BURST_MAX_SECONDS);
                    return;
                default:
                    return;
            }
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
