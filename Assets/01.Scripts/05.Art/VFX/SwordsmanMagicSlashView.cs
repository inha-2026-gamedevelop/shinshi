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
    /// <summary> 검사 Magic Slashes 효과 종류, 기본 공격과 기술 Q, W, E, R(두 타격)에 하나씩 대응한다 </summary>
    public enum EMagicSlashKind
    {
        Basic,
        Wide,
        Dash,
        Focus,
        Chain,
        ChainSecond
    }

    /// <summary> 검사 공개 전투 상태를 관찰해 Magic Slashes 효과를 재생하는 표현 전용 뷰, 서버 판정과 이동에 관여하지 않는다 </summary>
    [DisallowMultipleComponent]
    public sealed class SwordsmanMagicSlashView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    KIND_COUNT       = 6;
        private const int    MIN_POOL_SIZE    = 1;
        private const float  MIN_PLAY_SECONDS = 0.05f;
        private const string POOL_ROOT_NAME   = "SwordsmanMagicSlashPool";

        [Header("관찰 대상")]
        [SerializeField] private PlayerCombatState _combat;

        [Header("효과 프리팹")]
        [SerializeField] private GameObject _basicPrefab;
        [SerializeField] private GameObject _widePrefab;
        [SerializeField] private GameObject _dashPrefab;
        [SerializeField] private GameObject _focusPrefab;
        [SerializeField] private GameObject _chainPrefab;
        [SerializeField] private GameObject _chainSecondPrefab;

        [Header("풀과 재생")]
        [SerializeField] private int   _poolSizePerPrefab = 2;
        [SerializeField] private float _maxPlaySeconds    = 3f;
        // 재생 시각을 이만큼 넘겨 관찰한 효과는 이미 지난 일이라 재생하지 않는다
        [SerializeField] private float _maxLateSeconds    = 0.25f;
        [SerializeField] private float _heightOffset      = 0f;

        private readonly SwordsmanMagicSlashTracker _tracker = new SwordsmanMagicSlashTracker();
        private readonly MagicSlashPool[]           _pools   = new MagicSlashPool[KIND_COUNT];

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
                Destroy(_poolRoot.gameObject);
                _poolRoot = null;
            }
        }

        private void Update()
        {
            if (_combat == null)
            {
                return;
            }

            if (_combat.PlayerClass != EPlayerClass.Swordsman)
            {
                // 직업이 바뀌면 남은 효과를 멈추고, 검사로 돌아오면 새 기준값부터 본다
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
            SwordsmanMagicSlashTracker.Observation observation;
            observation.PlayerClass    = _combat.PlayerClass;
            observation.LifeState      = _combat.LifeState;
            observation.AttackSequence = _combat.AttackSequence;
            observation.DodgeSequence  = _combat.DodgeSequence;
            observation.ActionCancelSequence = _combat.ActionCancelSequence;
            observation.CastInstanceId = _combat.CastInstanceId;
            observation.SkillPhase     = _combat.SkillPhase;
            observation.SkillKind      = _combat.SkillKind;
            observation.SkillSlot      = _combat.SkillSlot;
            observation.SkillResolveAt = _combat.SkillResolveAt;
            observation.SkillOrigin    = _combat.SkillOrigin;
            observation.SkillAim       = _combat.SkillAim;
            observation.Facing         = FacingRules.DecodeYaw(_combat.FacingYaw);
            observation.Now            = NetworkTime.time;

            int count = _tracker.Update(in observation);
            if (_tracker.StopRequested)
            {
                StopAllEffects();
            }

            for (int i = 0; i < count; ++i)
            {
                PlayEffect(_tracker.GetPlay(i));
            }

            TickPools();
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

            // 직업 공개값을 받은 검사만 풀을 만든다. 다른 직업의 플레이어에는 파티클 복제 비용이 없다.
            _poolRoot = new GameObject(POOL_ROOT_NAME).transform;
            int size = Mathf.Max(MIN_POOL_SIZE, _poolSizePerPrefab);
            _pools[(int)EMagicSlashKind.Basic]       = new MagicSlashPool(_basicPrefab, _poolRoot, size);
            _pools[(int)EMagicSlashKind.Wide]        = new MagicSlashPool(_widePrefab, _poolRoot, size);
            _pools[(int)EMagicSlashKind.Dash]        = new MagicSlashPool(_dashPrefab, _poolRoot, size);
            _pools[(int)EMagicSlashKind.Focus]       = new MagicSlashPool(_focusPrefab, _poolRoot, size);
            _pools[(int)EMagicSlashKind.Chain]       = new MagicSlashPool(_chainPrefab, _poolRoot, size);
            _pools[(int)EMagicSlashKind.ChainSecond] = new MagicSlashPool(_chainSecondPrefab, _poolRoot, size);
        }

        // GameDB는 Awake 시점에 준비되지 않을 수 있어 초기화될 때까지 다음 프레임에 다시 시도한다
        private bool TryConfigure()
        {
            if (!GameDB.IsInitialized)
            {
                return false;
            }

            float basicDelay = GameDB.Player.GetBasicAttack(EPlayerClass.Swordsman).Windup;

            // 두 번째 타격은 기술 정의에 값이 있을 때만 재생한다
            float secondDelay = 0f;
            if (GameDB.Skills.TryGetSkillBySlot(EPlayerClass.Swordsman, Constants.Combat.SKILL_SLOT_R, out SkillCatalogSO.SkillDefinition chain)
                && (chain.Effect.SecondHitCoefficient > 0f))
            {
                secondDelay = chain.Effect.SecondHitDelay;
            }

            _tracker.Configure(basicDelay, secondDelay, _maxLateSeconds);
            _hasConfig = true;
            return true;
        }

        private void PlayEffect(SwordsmanMagicSlashTracker.Play play)
        {
            MagicSlashPool pool = _pools[(int)play.Kind];
            if ((pool == null) || (!pool.HasPrefab) || (!VfxVisibility.Allows(_combat.isOwned)))
            {
                return;
            }

            // 기본 공격과 돌진은 재생 순간의 캐릭터 위치, 나머지는 서버가 공개한 시전 위치에서 터진다
            Vector3 position = SwordsmanMagicSlashTracker.UsesActorPosition(play.Kind) ? _combat.transform.position : play.Origin;
            position.y += _heightOffset;

            Vector3 direction = play.Direction;
            direction.y = 0f;
            Quaternion rotation = (direction.sqrMagnitude > Mathf.Epsilon) ? Quaternion.LookRotation(direction) : Quaternion.identity;

            pool.SetOpacity(VfxVisibility.Opacity(_combat.isOwned));
            pool.Play(position, rotation, Time.time + Mathf.Max(MIN_PLAY_SECONDS, _maxPlaySeconds));
        }

        private void TickPools()
        {
            float now = Time.time;
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

        /****************************************
        *                Pool
        ****************************************/

        // 프리팹마다 고정 개수를 미리 만들어 두고 돌려 쓴다. 재생 중에는 Instantiate, Destroy, GetComponent를 부르지 않는다
        private sealed class MagicSlashPool
        {
            private readonly GameObject[]       _objects;
            private readonly Transform[]        _transforms;
            private readonly ParticleSystem[][] _systems;
            private readonly float[]            _expireAt;
            private readonly bool[]             _playing;
            private float                       _opacity = 1f;
            private int                         _next;

            public bool HasPrefab { get; }

            public MagicSlashPool(GameObject prefab, Transform root, int size)
            {
                HasPrefab = prefab != null;
                if (!HasPrefab)
                {
                    return;
                }

                _objects    = new GameObject[size];
                _transforms = new Transform[size];
                _systems    = new ParticleSystem[size][];
                _expireAt   = new float[size];
                _playing    = new bool[size];
                for (int i = 0; i < size; ++i)
                {
                    GameObject instance = UnityEngine.Object.Instantiate(prefab, root);
                    instance.SetActive(false);
                    _objects[i]    = instance;
                    _transforms[i] = instance.transform;
                    _systems[i]    = instance.GetComponentsInChildren<ParticleSystem>(true);
                }
            }

            // 이후 재생하는 효과의 투명도, 값이 바뀔 때만 모든 칸의 입자 색을 다시 계산한다
            public void SetOpacity(float alpha)
            {
                if ((!HasPrefab) || (Mathf.Approximately(_opacity, alpha)))
                {
                    return;
                }

                _opacity = alpha;
                for (int i = 0; i < _systems.Length; ++i)
                {
                    VfxOpacity.Apply(_systems[i], alpha);
                }
            }

            // 비어 있는 칸을 먼저 쓰고, 모두 재생 중이면 돌아가며 가장 먼저 쓴 칸을 다시 시작한다
            public void Play(Vector3 position, Quaternion rotation, float expireAt)
            {
                int index = -1;
                for (int i = 0; i < _objects.Length; ++i)
                {
                    if (!_playing[i])
                    {
                        index = i;
                        break;
                    }
                }

                if (index < 0)
                {
                    index = _next;
                }

                _next = (index + 1) % _objects.Length;

                StopSlot(index);
                _transforms[index].SetPositionAndRotation(position, rotation);
                _objects[index].SetActive(true);
                ParticleSystem[] systems = _systems[index];
                for (int i = 0; i < systems.Length; ++i)
                {
                    systems[i].Clear(false);
                    systems[i].Play(false);
                }

                _playing[index]  = true;
                _expireAt[index] = expireAt;
            }

            // 모든 파티클이 끝났거나 상한 시간이 지난 칸을 비활성화한다
            public void Tick(float now)
            {
                if (!HasPrefab)
                {
                    return;
                }

                for (int i = 0; i < _objects.Length; ++i)
                {
                    if ((!_playing[i]) || (_objects[i] == null))
                    {
                        continue;
                    }

                    if ((now >= _expireAt[i]) || (!IsAlive(i)))
                    {
                        StopSlot(i);
                    }
                }
            }

            public void StopAll()
            {
                if (!HasPrefab)
                {
                    return;
                }

                for (int i = 0; i < _objects.Length; ++i)
                {
                    StopSlot(i);
                }
            }

            private bool IsAlive(int index)
            {
                ParticleSystem[] systems = _systems[index];
                for (int i = 0; i < systems.Length; ++i)
                {
                    if (systems[i].IsAlive(false))
                    {
                        return true;
                    }
                }

                return false;
            }

            private void StopSlot(int index)
            {
                _playing[index] = false;
                if (_objects[index] == null)
                {
                    return;
                }

                ParticleSystem[] systems = _systems[index];
                for (int i = 0; i < systems.Length; ++i)
                {
                    systems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }

                _objects[index].SetActive(false);
            }
        }
    }

    /// <summary> 공개 전투 상태의 현재값만으로 검사 효과의 재생 시점과 취소를 정하는 순수 추적기, 엔진 오브젝트에 의존하지 않아 EditMode에서 시험한다 </summary>
    public sealed class SwordsmanMagicSlashTracker
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_PENDING = 4;

        /// <summary> 한 프레임에 모은 공개 상태 현재값 </summary>
        public struct Observation
        {
            public EPlayerClass          PlayerClass;
            public EParticipantLifeState LifeState;
            public byte                  AttackSequence;
            public byte                  DodgeSequence;
            public byte                  ActionCancelSequence;
            public int                   CastInstanceId;
            public ESkillPhase           SkillPhase;
            public ESkillRequestKind     SkillKind;
            public byte                  SkillSlot;
            public double                SkillResolveAt;
            public Vector3               SkillOrigin;
            public Vector3               SkillAim;
            public Vector3               Facing;
            public double                Now;
        }

        /// <summary> 지금 재생할 효과 하나 </summary>
        public struct Play
        {
            public EMagicSlashKind Kind;
            public Vector3         Origin;
            public Vector3         Direction;
        }

        private struct Pending
        {
            public bool            Active;
            public bool            IsBasic;
            public EMagicSlashKind Kind;
            public int             CastId;
            // 기술은 resolveAt에 더하는 지연, 기본 공격은 관찰 시각에서 정한 절대 재생 시각
            public double          Time;
            public Vector3         Origin;
            public Vector3         Direction;
        }

        private readonly Pending[] _pending = new Pending[MAX_PENDING];
        private readonly Play[]    _plays   = new Play[MAX_PENDING];

        private float _basicDelay;
        private float _secondDelay;
        private float _maxLate;

        private bool _hasBaseline;
        private bool _wasAlive;
        private byte _attackBaseline;
        private byte _dodgeBaseline;
        private byte _cancelBaseline;
        private int  _castBaseline;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 직전 Update에서 사망, 회피, 직업 변경 등으로 재생 중인 효과도 멈춰야 하면 참 </summary>
        public bool StopRequested { get; private set; }

        public bool HasPending
        {
            get
            {
                for (int i = 0; i < _pending.Length; ++i)
                {
                    if (_pending[i].Active)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 기본 공격 준비 시간, 연쇄 베기 두 번째 타격 지연(0 이하면 재생하지 않음), 재생 시각을 넘겨 관찰해도 허용하는 시간 </summary>
        public void Configure(float basicDelay, float chainSecondDelay, float maxLateSeconds)
        {
            _basicDelay  = Mathf.Max(0f, basicDelay);
            _secondDelay = chainSecondDelay;
            _maxLate     = Mathf.Max(0f, maxLateSeconds);
        }

        /// <summary> 대기 중인 효과를 모두 버리고 다음 관찰을 새 기준값으로 삼는다 </summary>
        public void Reset()
        {
            ClearPending();
            _hasBaseline  = false;
            StopRequested = false;
        }

        public Play GetPlay(int index)
        {
            return _plays[index];
        }

        public static bool UsesActorPosition(EMagicSlashKind kind)
        {
            return (kind == EMagicSlashKind.Basic) || (kind == EMagicSlashKind.Dash);
        }

        /// <summary> 이번 프레임의 현재값을 반영하고 지금 재생할 효과의 개수를 돌려준다, 효과는 GetPlay로 읽는다 </summary>
        public int Update(in Observation obs)
        {
            StopRequested = false;

            if (obs.PlayerClass != EPlayerClass.Swordsman)
            {
                if (_hasBaseline)
                {
                    Reset();
                    StopRequested = true;
                }

                return 0;
            }

            bool isAlive = obs.LifeState == EParticipantLifeState.Alive;
            if (!_hasBaseline)
            {
                _hasBaseline = true;
                _wasAlive    = isAlive;
                ConsumeBaselines(in obs);
                return 0;
            }

            if ((isAlive != _wasAlive) || (!isAlive))
            {
                // 쓰러져 있는 동안 생긴 행동은 재생하지 않고 기준값만 따라간다
                bool wasAlive = _wasAlive;
                _wasAlive = isAlive;
                if (wasAlive || HasPending)
                {
                    StopRequested = true;
                }

                ClearPending();
                ConsumeBaselines(in obs);
                return 0;
            }

            if (obs.DodgeSequence != _dodgeBaseline)
            {
                // 회피는 준비 중인 기술과 기본 공격을 끊는다
                StopRequested = true;
                ClearPending();
                ConsumeBaselines(in obs);
                return 0;
            }

            if (obs.ActionCancelSequence != _cancelBaseline)
            {
                // 서버가 기절로 준비 중인 기술과 기본 공격을 끊었다
                StopRequested = true;
                ClearPending();
                ConsumeBaselines(in obs);
                return 0;
            }

            bool isCastNew = (obs.CastInstanceId != 0) && (obs.CastInstanceId != _castBaseline);
            if (isCastNew)
            {
                // 다른 시전이 시작되면 이전 시전과 기본 공격 준비는 서버에서도 끊긴다
                ClearPending();
                _castBaseline = obs.CastInstanceId;
                RegisterCast(in obs);
            }

            bool isAttackNew = obs.AttackSequence != _attackBaseline;
            _attackBaseline = obs.AttackSequence;
            if (isAttackNew && (!isCastNew) && (obs.SkillPhase == ESkillPhase.Idle))
            {
                RegisterBasic(in obs);
            }

            return Resolve(in obs);
        }

        private void ConsumeBaselines(in Observation obs)
        {
            _attackBaseline = obs.AttackSequence;
            _dodgeBaseline  = obs.DodgeSequence;
            _cancelBaseline = obs.ActionCancelSequence;
            _castBaseline   = obs.CastInstanceId;
        }

        private void RegisterBasic(in Observation obs)
        {
            // 기본 공격은 한 번에 하나만 준비되므로 이전 준비는 새 값으로 바꾼다
            for (int i = 0; i < _pending.Length; ++i)
            {
                if (_pending[i].Active && _pending[i].IsBasic)
                {
                    _pending[i].Active = false;
                }
            }

            int slot = FreeSlot();
            if (slot < 0)
            {
                return;
            }

            _pending[slot].Active    = true;
            _pending[slot].IsBasic   = true;
            _pending[slot].Kind      = EMagicSlashKind.Basic;
            _pending[slot].CastId    = 0;
            _pending[slot].Time      = obs.Now + _basicDelay;
            _pending[slot].Origin    = Vector3.zero;
            _pending[slot].Direction = obs.Facing;
        }

        private void RegisterCast(in Observation obs)
        {
            if (obs.SkillKind != ESkillRequestKind.Regular)
            {
                return;
            }

            // 이미 끝났거나 취소된 시전, 시작 전 단계는 재생하지 않는다
            if ((obs.SkillPhase != ESkillPhase.Windup) && (obs.SkillPhase != ESkillPhase.Active) && (obs.SkillPhase != ESkillPhase.Recovery))
            {
                return;
            }

            if (!TryGetKind(obs.SkillSlot, out EMagicSlashKind kind))
            {
                return;
            }

            AddSkillPending(kind, obs.CastInstanceId, 0.0, in obs);
            if ((kind == EMagicSlashKind.Chain) && (_secondDelay > 0f))
            {
                AddSkillPending(EMagicSlashKind.ChainSecond, obs.CastInstanceId, _secondDelay, in obs);
            }
        }

        private void AddSkillPending(EMagicSlashKind kind, int castId, double delay, in Observation obs)
        {
            int slot = FreeSlot();
            if (slot < 0)
            {
                return;
            }

            _pending[slot].Active    = true;
            _pending[slot].IsBasic   = false;
            _pending[slot].Kind      = kind;
            _pending[slot].CastId    = castId;
            _pending[slot].Time      = delay;
            _pending[slot].Origin    = obs.SkillOrigin;
            _pending[slot].Direction = obs.SkillAim;
        }

        private int Resolve(in Observation obs)
        {
            int count = 0;
            for (int i = 0; i < _pending.Length; ++i)
            {
                if (!_pending[i].Active)
                {
                    continue;
                }

                if (_pending[i].IsBasic)
                {
                    count = ResolveBasic(i, count, in obs);
                }
                else
                {
                    count = ResolveSkill(i, count, in obs);
                }
            }

            return count;
        }

        private int ResolveBasic(int index, int count, in Observation obs)
        {
            // 준비 중에 기술이 시작되면 서버가 기본 공격을 버린다
            if (obs.SkillPhase != ESkillPhase.Idle)
            {
                _pending[index].Active = false;
                return count;
            }

            if (obs.Now < _pending[index].Time)
            {
                return count;
            }

            _pending[index].Active = false;
            if ((obs.Now - _pending[index].Time) > _maxLate)
            {
                return count;
            }

            return Emit(in _pending[index], count);
        }

        private int ResolveSkill(int index, int count, in Observation obs)
        {
            // 다른 시전으로 바뀌었거나 Idle이면 취소 또는 이미 끝난 시전이다
            if ((obs.CastInstanceId != _pending[index].CastId) || (obs.SkillPhase == ESkillPhase.Idle))
            {
                _pending[index].Active = false;
                return count;
            }

            double playAt  = obs.SkillResolveAt + _pending[index].Time;
            bool   isFirst = _pending[index].Kind != EMagicSlashKind.ChainSecond;

            // 첫 타격은 서버가 Windup을 지났다는 신호(Active, Recovery)만으로도 도달한 것이다. Active 0 기술도 Recovery에서 잡는다
            bool isDue = (obs.Now >= playAt) || (isFirst && (obs.SkillPhase != ESkillPhase.Windup));
            if (!isDue)
            {
                return count;
            }

            _pending[index].Active = false;
            if ((obs.Now - playAt) > _maxLate)
            {
                return count;
            }

            return Emit(in _pending[index], count);
        }

        private int Emit(in Pending pending, int count)
        {
            _plays[count].Kind      = pending.Kind;
            _plays[count].Origin    = pending.Origin;
            _plays[count].Direction = pending.Direction;
            return count + 1;
        }

        private int FreeSlot()
        {
            for (int i = 0; i < _pending.Length; ++i)
            {
                if (!_pending[i].Active)
                {
                    return i;
                }
            }

            return -1;
        }

        private void ClearPending()
        {
            for (int i = 0; i < _pending.Length; ++i)
            {
                _pending[i].Active = false;
            }
        }

        private static bool TryGetKind(byte slot, out EMagicSlashKind kind)
        {
            switch (slot)
            {
                case Constants.Combat.SKILL_SLOT_Q:
                    kind = EMagicSlashKind.Wide;
                    return true;
                case Constants.Combat.SKILL_SLOT_W:
                    kind = EMagicSlashKind.Dash;
                    return true;
                case Constants.Combat.SKILL_SLOT_E:
                    kind = EMagicSlashKind.Focus;
                    return true;
                case Constants.Combat.SKILL_SLOT_R:
                    kind = EMagicSlashKind.Chain;
                    return true;
                default:
                    kind = EMagicSlashKind.Basic;
                    return false;
            }
        }
    }
}
