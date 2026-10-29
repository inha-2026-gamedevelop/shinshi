// System
using System;

// Unity
using UnityEngine;
using UnityEngine.Serialization;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.UI;

namespace Shinshi.Art
{
    /// <summary> 마검사 공개 전투 상태를 관찰해 Hovl 효과를 재생하는 표현 전용 뷰, 서버 판정과 이동에 관여하지 않는다 </summary>
    [DisallowMultipleComponent]
    public sealed class MageknightVfxView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    KIND_COUNT       = 5;
        private const int    MIN_POOL_SIZE    = 1;
        private const float  MIN_PLAY_SECONDS = 0.05f;
        private const float  FREEZE_FRAMES_PER_SECOND = 60f;
        private const float  MIN_JUMP_DISTANCE = 0.1f; // m, 이보다 덜 움직였으면 길이를 맞추지 않는다
        private const string POOL_ROOT_NAME   = "MageknightVfxPool";

        [Header("관찰 대상")]
        [SerializeField] private PlayerCombatState _combat;

        // 스택형 개편 전 Q, R 칸의 프리팹 참조를 그대로 이어받아 플레이어 프리팹을 다시 저장하지 않아도 된다
        [Header("효과 프리팹")]
        [SerializeField] private GameObject _basicPrefab;
        [FormerlySerializedAs("_arcanePrefab")]
        [SerializeField] private GameObject _shieldPrefab;
        [SerializeField] private GameObject _wavePrefab;
        [SerializeField] private GameObject _fieldPrefab;
        [FormerlySerializedAs("_sealPrefab")]
        [SerializeField] private GameObject _jumpPrefab;
        [SerializeField] private GameObject _fullStackAuraPrefab;

        [Header("풀과 재생")]
        [SerializeField] private int   _poolSizePerPrefab = 2;
        [SerializeField] private float _maxPlaySeconds    = 3f;
        // 재생 시각을 이만큼 넘겨 관찰한 짧은 효과는 이미 지난 일이라 재생하지 않는다
        [SerializeField] private float _maxLateSeconds    = 0.25f;
        [SerializeField] private float _heightOffset      = 0f;

        [Header("검파 적중 정지")]
        // 내 검파가 적을 맞힐 때마다 날아가는 본체를 이 프레임 수만큼 세운다. 맞는 연출만 바꾸고 서버 판정과는 무관하다
        [SerializeField, Min(0)] private int _waveFreezeFrames       = 4;
        // 한 번 쏜 검파가 서는 시간의 합 상한. 길어지면 본체가 실제 판정보다 한참 뒤처진다
        [SerializeField, Min(0)] private int _waveFreezeBudgetFrames = 12;
        [SerializeField, Min(0f)] private float _classifyMargin      = 0.15f;

        [Header("순간 이동 베기")]
        // 캐릭터가 도착한 뒤 효과를 이만큼 늦게 시작한다. 후딜 안이어야 한다
        [SerializeField, Min(0f)] private float _jumpBeatSeconds = 0.1f;
        // 효과 프리팹이 앞쪽으로 닿는 길이(m). 실제로 이동한 거리에 맞춰 이 길이를 늘이거나 줄인다
        [SerializeField, Min(0.1f)] private float _jumpReach     = 6.3f;

        private readonly MageknightVfxTracker _tracker = new MageknightVfxTracker();
        private readonly MageknightVfxPool[]  _pools   = new MageknightVfxPool[KIND_COUNT];

        private Transform _poolRoot;
        private GameObject _fullStackAura;
        private bool      _hasConfig;
        private bool      _wasRunning;
        private float     _waveSpeed;
        private float     _waveRange;
        private int       _basicStepCount;
        private float     _jumpDelay;
        private PlayerInputState _input;
        private bool      _isFeedbackBound;
        private readonly MageknightHitClassifier _hitClassifier = new MageknightHitClassifier();
        private readonly DamageFeedbackReceiver  _hitReceiver   = new DamageFeedbackReceiver();
        private int       _hitCastId;
        private int       _freezeCastId;
        private int       _freezeSpentFrames;
        private AudioSource _sfxAudio;
        private AudioClip[] _basicClips;
        private AudioClip _waveClip;
        private readonly System.Random _basicSoundRandom = new System.Random();

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            SoundVolume.PreviewChanged += OnSfxVolumeChanged;
            OnSfxVolumeChanged(SoundVolume.Load());
        }

        private void OnDisable()
        {
            SoundVolume.PreviewChanged -= OnSfxVolumeChanged;
            if (_sfxAudio != null)
            {
                _sfxAudio.Stop();
            }

            StopFullStackAura();
            UnbindFeedback();
            _tracker.Reset();
            _wasRunning = false;
            StopAllEffects();
        }

        private void OnDestroy()
        {
            StopFullStackAura();
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

            if (_combat.PlayerClass != EPlayerClass.Mageknight)
            {
                StopFullStackAura();
                // 직업이 바뀌면 남은 효과를 멈추고, 마검사로 돌아오면 새 기준값부터 본다
                if (_wasRunning)
                {
                    _tracker.Reset();
                    _wasRunning = false;
                    StopAllEffects();
                }

                return;
            }

            UpdateFullStackAura();
            if ((!_hasConfig) && (!TryConfigure()))
            {
                return;
            }

            _wasRunning = true;
            EnsurePools();
            UpdateWaveFreeze();

            // SyncVar hook의 중간값이 아니라 이번 프레임에 모인 현재값으로만 판단한다
            double now = NetworkTime.time;
            MageknightVfxTracker.Observation observation = default;
            observation.PlayerClass    = _combat.PlayerClass;
            observation.LifeState      = _combat.LifeState;
            observation.AttackSequence = _combat.AttackSequence;
            observation.AttackComboStep = _combat.AttackComboStep;
            observation.DodgeSequence  = _combat.DodgeSequence;
            observation.ActionCancelSequence = _combat.ActionCancelSequence;
            observation.CastInstanceId = _combat.CastInstanceId;
            observation.SkillPhase     = _combat.SkillPhase;
            observation.SkillKind      = _combat.SkillKind;
            observation.SkillSlot      = _combat.SkillSlot;
            observation.SkillResolveAt = _combat.SkillResolveAt;
            observation.SkillOrigin    = _combat.SkillOrigin;
            observation.SkillAim       = _combat.SkillAim;
            observation.SkillPoint     = _combat.SkillPoint;
            observation.Facing         = FacingRules.DecodeYaw(_combat.FacingYaw);
            observation.Now            = now;

            // 사망, 회피, 다음 시전은 준비 중인 효과만 버린다. 이미 나간 장판과 검기는 풀이 자기 수명까지 유지한다
            int count = _tracker.Update(in observation);
            for (int i = 0; i < count; ++i)
            {
                PlayEffect(_tracker.GetPlay(i), now);
            }

            TickPools(now);
            FollowActor();
        }

        /****************************************
        *                Methods
        ****************************************/

        protected override void OnViewAwake()
        {
            TryGetComponent(out _input);
            // 프리팹은 부모 빌더가 주입하며, 전투 상태가 빠졌을 때만 부모에서 한 번 찾는다
            if (_combat == null)
            {
                _combat = GetComponentInParent<PlayerCombatState>();
            }
        }

        private void UpdateFullStackAura()
        {
            bool full = GameDB.IsInitialized && (GameDB.Skills.ClassStackMax > 0)
                && (_combat.ClassStacks >= GameDB.Skills.ClassStackMax)
                && (_combat.LifeState == EParticipantLifeState.Alive);
            if ((!full) || (_fullStackAuraPrefab == null))
            {
                StopFullStackAura();
                return;
            }

            if (_fullStackAura == null)
            {
                _fullStackAura = Instantiate(_fullStackAuraPrefab, _combat.transform, false);
            }
        }

        private void StopFullStackAura()
        {
            if (_fullStackAura != null)
            {
                Destroy(_fullStackAura);
                _fullStackAura = null;
            }
        }

        private void EnsurePools()
        {
            if (_poolRoot != null)
            {
                return;
            }

            // 직업 공개값을 받은 마검사만 풀을 만든다. 다른 직업의 플레이어에는 파티클 복제 비용이 없다.
            _poolRoot = new GameObject(POOL_ROOT_NAME).transform;
            int size = Mathf.Max(MIN_POOL_SIZE, _poolSizePerPrefab);
            _pools[(int)EMageknightVfxKind.Basic]  = new MageknightVfxPool(_basicPrefab, _poolRoot, size);
            _pools[(int)EMageknightVfxKind.Shield] = new MageknightVfxPool(_shieldPrefab, _poolRoot, size);
            _pools[(int)EMageknightVfxKind.Wave]   = new MageknightVfxPool(_wavePrefab, _poolRoot, size);
            _pools[(int)EMageknightVfxKind.Field]  = new MageknightVfxPool(_fieldPrefab, _poolRoot, size);
            _pools[(int)EMageknightVfxKind.Jump]   = new MageknightVfxPool(_jumpPrefab, _poolRoot, size);
        }

        // GameDB는 Awake 시점에 준비되지 않을 수 있어 초기화될 때까지 다음 프레임에 다시 시도한다
        private bool TryConfigure()
        {
            if (!GameDB.IsInitialized)
            {
                return false;
            }

            // 명세를 읽지 못한 값은 0으로 두어 해당 효과만 재생하지 않거나 즉시 재생한다
            float basicDelay = 0f;
            _basicStepCount  = 0;
            try
            {
                PlayerConfigSO.BasicAttackSpec basic = GameDB.Player.GetBasicAttack(EPlayerClass.Mageknight);
                basicDelay      = basic.Windup;
                _basicStepCount = basic.ComboStepCount;
            }
            catch (ArgumentOutOfRangeException)
            {
                basicDelay = 0f;
            }

            float shieldDuration = 0f;
            if (GameDB.Skills.TryGetSkillBySlot(EPlayerClass.Mageknight, Constants.Combat.SKILL_SLOT_Q, out SkillCatalogSO.SkillDefinition shield))
            {
                shieldDuration = shield.Effect.HitImmunityDuration;
            }

            float fieldDuration = 0f;
            if (GameDB.Skills.TryGetSkillBySlot(EPlayerClass.Mageknight, Constants.Combat.SKILL_SLOT_E, out SkillCatalogSO.SkillDefinition field))
            {
                fieldDuration = field.Effect.Duration;
            }

            _waveSpeed = 0f;
            _waveRange = 0f;
            if (GameDB.Skills.TryGetSkillBySlot(EPlayerClass.Mageknight, Constants.Combat.SKILL_SLOT_W, out SkillCatalogSO.SkillDefinition wave))
            {
                _waveSpeed = wave.Area.ProjectileSpeed;
                _waveRange = wave.Area.CastRange;
            }

            // 발동 구간이 끝나는 순간 서버가 캐릭터를 옮기므로 그 뒤 한 박자 늦게 시작한다
            _jumpDelay = 0f;
            if (GameDB.Skills.TryGetSkillBySlot(EPlayerClass.Mageknight, Constants.Combat.SKILL_SLOT_R, out SkillCatalogSO.SkillDefinition jump))
            {
                _jumpDelay = jump.Timeline.Active + Mathf.Min(_jumpBeatSeconds, jump.Timeline.Recovery);
            }

            _tracker.Configure(basicDelay, shieldDuration, fieldDuration, _waveSpeed, _waveRange, _maxLateSeconds, _jumpDelay);
            _hitClassifier.Configure((float)MageknightVfxTracker.WaveTravelSeconds(_waveSpeed, _waveRange), 0f, _classifyMargin);
            _hasConfig = true;
            return true;
        }

        private void PlayEffect(MageknightVfxTracker.Play play, double now)
        {
            if (play.Kind == EMageknightVfxKind.Basic)
            {
                PlayBasicSound();
            }
            else if (play.Kind == EMageknightVfxKind.Wave)
            {
                PlayWaveSound();
            }
            else if ((play.Kind == EMageknightVfxKind.Shield) && ((now - play.StartAt) <= _maxLateSeconds))
            {
                PlayBuffSound();
            }

            MageknightVfxPool pool = _pools[(int)play.Kind];
            if ((pool == null) || (!pool.HasPrefab))
            {
                return;
            }

            // 기본 공격은 재생 순간의 캐릭터 위치, 나머지는 서버가 공개한 시전 위치에서 터진다
            Vector3 position = MageknightVfxTracker.UsesActorPosition(play.Kind) ? _combat.transform.position : play.Origin;
            position.y += _heightOffset;

            Vector3 direction = play.Direction;
            direction.y = 0f;

            switch (play.Kind)
            {
                case EMageknightVfxKind.Basic:
                    pool.PlayBrief(position, ToRotation(direction), now, Mathf.Max(MIN_PLAY_SECONDS, _maxPlaySeconds),
                        GetBasicGroup(play.Variant, pool.GroupCount));
                    break;
                case EMageknightVfxKind.Field:
                case EMageknightVfxKind.Shield:
                    pool.PlayTimed(position, ToRotation(direction), play.EndAt);
                    break;
                case EMageknightVfxKind.Wave:
                    pool.PlayWave(position, direction, play.StartAt, _waveSpeed, _waveRange, now);
                    break;
                case EMageknightVfxKind.Jump:
                    PlayJump(pool, position, direction, now);
                    break;
                default:
                    pool.PlayBrief(position, ToRotation(direction), now, Mathf.Max(MIN_PLAY_SECONDS, _maxPlaySeconds));
                    break;
            }
        }

        private void PlayBasicSound()
        {
            if (_basicClips == null)
            {
                _basicClips = new[]
                {
                    Resources.Load<AudioClip>("SFX-Light-Slash-01_wav"),
                    Resources.Load<AudioClip>("SFX-Light-Slash-02_wav"),
                    Resources.Load<AudioClip>("SFX-Light-Slash-03_wav")
                };
            }

            AudioClip clip = _basicClips[_basicSoundRandom.Next(_basicClips.Length)];
            if (clip != null)
            {
                EnsureSfxAudio();
                _sfxAudio.PlayOneShot(clip);
            }
        }

        private void PlayWaveSound()
        {
            if (_waveClip == null)
            {
                _waveClip = Resources.Load<AudioClip>("WEAPON_Dagger");
            }

            if (_waveClip != null)
            {
                EnsureSfxAudio();
                _sfxAudio.PlayOneShot(_waveClip);
            }
        }

        private void PlayBuffSound()
        {
            AudioClip clip = BuffSoundClips.Pick(_basicSoundRandom);
            if (clip != null)
            {
                EnsureSfxAudio();
                _sfxAudio.PlayOneShot(clip);
            }
        }

        private void EnsureSfxAudio()
        {
            if (_sfxAudio != null)
            {
                return;
            }

            _sfxAudio = gameObject.AddComponent<AudioSource>();
            _sfxAudio.playOnAwake = false;
            _sfxAudio.spatialBlend = 1f;
            _sfxAudio.rolloffMode = AudioRolloffMode.Linear;
            _sfxAudio.maxDistance = 60f;
            OnSfxVolumeChanged(SoundVolume.Load());
        }

        private void OnSfxVolumeChanged(SoundVolume volume)
        {
            if (_sfxAudio != null)
            {
                _sfxAudio.volume = volume.Sfx;
            }
        }

        // 소유 플레이어만 피해 알림을 받는다. 소유권은 스폰 뒤에 정해지므로 정해진 뒤에 한 번 묶는다
        private void UpdateWaveFreeze()
        {
            if ((_input != null) && (!_isFeedbackBound) && (_input.isOwned))
            {
                _input.DamageFeedbackReceived += OnDamageFeedback;
                _isFeedbackBound = true;
            }

            if ((_combat.CastInstanceId != _hitCastId) && (_combat.SkillPhase != ESkillPhase.Idle)
                && (_combat.SkillKind == ESkillRequestKind.Regular))
            {
                _hitCastId = _combat.CastInstanceId;
                _hitClassifier.ObserveCast(_combat.SkillSlot == Constants.Combat.SKILL_SLOT_W,
                    _combat.SkillSlot == Constants.Combat.SKILL_SLOT_R, _hitCastId, _combat.SkillResolveAt);
            }
        }

        private void UnbindFeedback()
        {
            if ((_input != null) && (_isFeedbackBound))
            {
                _input.DamageFeedbackReceived -= OnDamageFeedback;
            }

            _isFeedbackBound = false;
            _hitClassifier.Reset();
            _hitCastId = 0;
        }

        private void OnDamageFeedback(DamageFeedback feedback)
        {
            if ((_waveFreezeFrames <= 0) || (_input == null) || (!_input.isOwned))
            {
                return;
            }

            double now = DamageFeedbackRules.ReceptionTime(NetworkTime.time, NetworkClient.bufferTime);
            if ((!_hitReceiver.TryAccept(in feedback, _input.SessionEpoch, now))
                || (_hitClassifier.Classify(feedback.ServerTime) != MageknightHitClassifier.EKind.Wave))
            {
                return;
            }

            if (_hitCastId != _freezeCastId)
            {
                _freezeCastId      = _hitCastId;
                _freezeSpentFrames = 0;
            }

            if ((_freezeSpentFrames + _waveFreezeFrames) > _waveFreezeBudgetFrames)
            {
                return;
            }

            _freezeSpentFrames += _waveFreezeFrames;
            FreezeWave(_waveFreezeFrames / FREEZE_FRAMES_PER_SECOND);
        }

        /// <summary> 날아가는 검파 본체를 seconds 동안 세운다. 적중 연출용이라 서버 판정과는 무관하다 </summary>
        public void FreezeWave(float seconds)
        {
            MageknightVfxPool pool = _pools[(int)EMageknightVfxKind.Wave];
            if (pool != null)
            {
                pool.FreezeActiveWaves(NetworkTime.time, seconds);
            }
        }

        // 이 시점에는 캐릭터가 이미 도착해 있다. 출발점에서 실제 도착 지점까지 이어지게 방향과 길이를 맞춘다
        private void PlayJump(MageknightVfxPool pool, Vector3 origin, Vector3 aim, double now)
        {
            Vector3 delta = _combat.transform.position - origin;
            delta.y = 0f;
            float distance = delta.magnitude;

            Vector3 direction = aim;
            float   scale     = 1f;
            if (distance > MIN_JUMP_DISTANCE)
            {
                direction = delta / distance;
                scale     = distance / _jumpReach;
            }

            pool.PlayBrief(origin, ToRotation(direction), now, Mathf.Max(MIN_PLAY_SECONDS, _maxPlaySeconds), -1, scale);
        }

        // 콤보 효과 묶음 수와 콤보 단계 수가 달라도 마지막 단계는 마지막 묶음(마무리 베기)을 쓴다
        private int GetBasicGroup(int step, int groupCount)
        {
            if (groupCount <= 0)
            {
                return -1;
            }

            if ((_basicStepCount > 1) && (step >= _basicStepCount - 1))
            {
                return groupCount - 1;
            }

            return Mathf.Clamp(step, 0, groupCount - 1);
        }

        private void FollowActor()
        {
            MageknightVfxPool shield = _pools[(int)EMageknightVfxKind.Shield];
            if (shield == null)
            {
                return;
            }

            Vector3 position = _combat.transform.position;
            position.y += _heightOffset;
            shield.MoveActive(position);
        }

        private static Quaternion ToRotation(Vector3 direction)
        {
            return (direction.sqrMagnitude > Mathf.Epsilon) ? Quaternion.LookRotation(direction) : Quaternion.identity;
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
