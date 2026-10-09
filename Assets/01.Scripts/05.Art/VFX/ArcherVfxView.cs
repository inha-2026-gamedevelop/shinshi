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
    /// <summary> 궁수 공개 전투 상태를 관찰해 화살과 바람 효과를 재생하는 표현 전용 뷰, 서버 판정과 이동에 관여하지 않으며 적중 효과는 만들지 않는다 </summary>
    [DisallowMultipleComponent]
    public sealed class ArcherVfxView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int POOL_ARROW       = 0;
        private const int POOL_WIND        = 1;
        private const int POOL_MUZZLE      = 2;
        private const int POOL_WIND_MUZZLE = 3;
        private const int POOL_COUNT       = 4;

        private const int ARROW_POOL_SIZE       = 16;
        private const int WIND_POOL_SIZE        = 4;
        private const int MUZZLE_POOL_SIZE      = 8;
        private const int WIND_MUZZLE_POOL_SIZE = 4;

        // 발사 시각을 이만큼 넘겨 관찰한 총구 효과는 이미 지난 일이라 재생하지 않는다
        private const float  MAX_LATE_SECONDS   = 0.25f;
        private const float  MUZZLE_MAX_SECONDS = 1.5f;
        private const string POOL_ROOT_NAME     = "ArcherVfxPool";

        [Header("관찰 대상")]
        [SerializeField] private PlayerCombatState _combat;

        [Header("효과 프리팹")]
        [SerializeField] private GameObject _arrowPrefab;
        [SerializeField] private GameObject _muzzlePrefab;
        [SerializeField] private GameObject _windPrefab;
        [SerializeField] private GameObject _windMuzzlePrefab;

        private readonly ArcherVfxTracker _tracker = new ArcherVfxTracker();
        private readonly ArcherVfxPool[]  _pools   = new ArcherVfxPool[POOL_COUNT];

        private Transform _poolRoot;
        private bool      _hasConfig;
        private bool      _wasRunning;

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
            observation.PlayerClass     = _combat.PlayerClass;
            observation.LifeState       = _combat.LifeState;
            observation.AttackSequence  = _combat.AttackSequence;
            observation.DodgeSequence   = _combat.DodgeSequence;
            observation.CastInstanceId  = _combat.CastInstanceId;
            observation.SkillPhase      = _combat.SkillPhase;
            observation.SkillKind       = _combat.SkillKind;
            observation.SkillSlot       = _combat.SkillSlot;
            observation.SkillResolveAt  = _combat.SkillResolveAt;
            observation.SkillAim        = _combat.SkillAim;
            observation.Facing          = FacingRules.DecodeYaw(_combat.FacingYaw);
            observation.LocomotionSpeed = _combat.LocomotionSpeed;
            observation.Now             = now;

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
            _pools[POOL_ARROW]       = new ArcherVfxPool(_arrowPrefab, _poolRoot, ARROW_POOL_SIZE);
            _pools[POOL_WIND]        = new ArcherVfxPool(_windPrefab, _poolRoot, WIND_POOL_SIZE);
            _pools[POOL_MUZZLE]      = new ArcherVfxPool(_muzzlePrefab, _poolRoot, MUZZLE_POOL_SIZE);
            _pools[POOL_WIND_MUZZLE] = new ArcherVfxPool(_windMuzzlePrefab, _poolRoot, WIND_MUZZLE_POOL_SIZE);
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
            config.MaxLateSeconds = MAX_LATE_SECONDS;
            try
            {
                var basic = GameDB.Player.GetBasicAttack(EPlayerClass.Archer);
                config.BasicWindup       = basic.Windup;
                config.BasicCancelOnMove = basic.CancelOnMove;
                config.Basic.Speed       = basic.ProjectileSpeed;
                config.Basic.Range       = basic.Range;
            }
            catch (ArgumentOutOfRangeException)
            {
                config.Basic = default;
            }

            _tracker.Configure(in config);

            for (int slot = 0; slot < ArcherVfxTracker.SLOT_COUNT; ++slot)
            {
                var spec = new ArcherVfxTracker.ShotSpec();
                if (GameDB.Skills.TryGetSkillBySlot(EPlayerClass.Archer, slot, out SkillCatalogSO.SkillDefinition skill))
                {
                    spec.Speed           = skill.Area.ProjectileSpeed;
                    spec.Range           = skill.Area.CastRange;
                    spec.ProjectileCount = skill.Effect.ProjectileCount;
                    spec.SpreadAngle     = skill.Effect.SpreadAngle;
                    spec.RepeatCount     = skill.Effect.RepeatCount;
                    spec.RepeatInterval  = skill.Effect.RepeatInterval;
                }

                _tracker.SetSkill(slot, in spec);
            }

            _hasConfig = true;
            return true;
        }

        private void PlayEffect(ArcherVfxTracker.Play play, Vector3 origin, double now)
        {
            bool isWind = play.Kind == EArcherVfxKind.Wind;
            int  index  = play.IsMuzzle ? (isWind ? POOL_WIND_MUZZLE : POOL_MUZZLE) : (isWind ? POOL_WIND : POOL_ARROW);
            ArcherVfxPool pool = _pools[index];
            if ((pool == null) || (!pool.HasPrefab))
            {
                return;
            }

            // 서버는 발사 시각의 시전자 위치에서 쏘므로 재생 순간의 캐릭터 위치에서 출발한다
            if (play.IsMuzzle)
            {
                Vector3 direction = play.Direction;
                direction.y = 0f;
                Quaternion rotation = (direction.sqrMagnitude > Mathf.Epsilon) ? Quaternion.LookRotation(direction) : Quaternion.identity;
                pool.PlayBrief(origin, rotation, now, MUZZLE_MAX_SECONDS);
                return;
            }

            pool.PlayProjectile(origin, play.Direction, play.StartAt, play.Speed, play.Range, now);
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
