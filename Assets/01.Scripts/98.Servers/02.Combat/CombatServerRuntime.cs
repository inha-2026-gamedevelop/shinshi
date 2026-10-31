// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;
using Shinshi.Systems;

namespace Shinshi.Combat
{
    /// <summary> 서버 전투 틱을 고정 주기로 돌린다, CombatServerInstaller가 실행 중에 만들며 프리팹, 씬에 붙이지 않는다 </summary>
    public sealed class CombatServerRuntime : MonoBehaviour, ICombatSkillPhase, IServerPlayerRetention, IPotionHost,
        ICheckpointSource, ISessionEndingListener
    {
        // 한 틱 처리 순서: 세션 BeginTick(유예 만료를 먼저 닫는다) -> 닫힌 참가자의 남은 캐릭터 정리 -> 입력(이미 Command
        // 처리기가 검증해 보관) -> 이동 -> 기술, 기본 공격과 피해, 사망 -> 생사를 명단에 반영 -> 세션 CompleteTick(종료 판정)
        // -> 부활 승인 -> 복제. 같은 틱의 마지막 생존자 사망과 부활은 전멸이 우선해야 하므로 부활은 종료 판정 뒤에 둔다.
        // 일반전 웨이브(적 출현, 적 두뇌, 제한 시간)는 플레이어와 보스 피해 뒤, 생사 보고 앞에서 세션 틱 안에 돈다. 적이 때려 죽은
        // 플레이어가 같은 틱 생사 보고와 종료 판정에 들어간다. 포기 요청은 세션 BeginTick이 피해 처리 전에 처리한다.
        //
        // 마을(출정 없음)은 명단을 닫지 않는다. 인증기가 끊길 때 참가자 키를 지우므로 다시 들어오면 새 번호를 받고,
        // 그 번호로는 보존 기록을 찾을 수 없어 마을에서는 상태를 보존하지 않는다. 마을에는 전투 자원이 없다.
        // 던전 시작 전에도 보존 기록 만료로 명단을 닫지 않는다. 시작 전 끊김은 예약 만료까지 기다린다
        //
        // 던전 세션이 진행 중이면 연결이 끊긴 참가자의 캐릭터를 지우지 않고 소유권만 떼어 유예 동안 남긴다. 입력만 멈추고
        // 피격, 사망, 기술 타이머는 계속 처리하며, 재접속하면 같은 개체를 새 연결에 넘긴다(netId 유지). 상태를 따로 떼어 두는
        // ReconnectVault는 개체가 실제로 사라지는 경로(마을, 시작 전, 유지 실패)에만 쓴다. 두 경로가 같은 참가자를 동시에
        // 들고 있지 않도록 남긴 캐릭터는 RemovePlayer를 거치지 않고 직접 정리한다

        /****************************************
        *                Fields
        ****************************************/

        private const double TICK_INTERVAL_SECONDS = 1.0 / Constants.Network.SERVER_TICK_RATE;
        private const byte   FIRST_METER_PHASE     = 1;

        // 체력 기믹 묶음이 있는 보스 정의 ID
        private const string GIMMICK_BOSS_ID       = "boss_wolbaek";

        // 기믹 난수 시드에 섞는 값. 보스 패턴 선택과 같은 출정 시드에서 나오되 난수열은 따로 쓴다
        private const int    GIMMICK_RANDOM_SALT   = 0x5F3759DF;

        // 프롤로그 월백은 1페이즈에서 2페이즈 전환 체력(정의의 PhaseTransitionHealthRate)에 닿지 않게 그보다 이만큼 위에서 피해를 막는다
        private const float  PROLOGUE_PHASE_MARGIN = 0.01f;

        private readonly Dictionary<int, ExperienceBinding> _experience = new Dictionary<int, ExperienceBinding>();
        private SessionEventQueue _experienceQueue;
        private CharacterLevelSettings _levelSettings;
        private CombatTargetRegistry         _registry;
        private HitLedger                    _ledger;
        private DamageResolver               _resolver;
        private DamageFeedbackBudget         _damageFeedbackBudget;
        private IDamageResolver              _skillResolver;
        private CombatMeterRecorder          _combatMeter;
        private SkillCatalogSO               _skillCatalog;
        private Dictionary<int, SkillServer> _retainedSkills;
        private List<SkillServer>            _retainedSkillTicks;
        private bool                         _allowsCombat          = false;
        private PlayerConfigSO               _playerConfig;
        private List<PlayerServer>           _players;
        private ReconnectVault               _reconnectVault;
        private List<int>                    _expiredParticipants;
        private IParticipantRoster           _roster;
        private PathRequestBudget            _pathBudget;
        private CombatLineOfSight            _lineOfSight;
        private double                       _reconnectGraceSeconds = 0.0;
        private int                          _sessionEpoch          = 0;
        private int                          _pathCursor            = 0;
        private double                       _lastTickTime          = 0.0;
        private bool                         _hasTicked             = false;
        private DungeonSessionHost           _host;
        private List<int>                    _closedParticipants;
        private bool                         _scalesApplied         = false;
        private BossServer                   _boss;
        private string                       _bossId                = Constants.Combat.DEV_BOSS_ID;
        private bool                         _bossSpawnChecked      = false;
        private bool                         _finalSuccessUnreported = false;
        private bool                         _gimmickStopHandled    = false;
        private IBossObjectHost              _bossHost;
        private BossState                    _bossState;
        private int                          _obstacleMask          = 0;
        private ICompanionObjectHost         _companionHost;
        private CompanionSpawnTracker        _companionTracker;
        private ServerMetricsSampler         _metrics;
        private RevivePointSelector          _revivePoints;
        private bool                         _warnedNoRevivePoints  = false;
        private EquipmentCatalogSO           _equipment;
        private GrowthConfigSO               _growth;
        private TalismanModifierResolver     _talismans;
        private PotionServer                 _potions;
        private IEnemyObjectHost             _enemyHost;
        private bool                         _epochFromSession      = false;
        private PrologueSessionAdapter       _prologue;
        private SessionBinding               _prologueBinding;
        private bool                         _prologueChecked       = false;

        /****************************************
        *              Properties
        ****************************************/

        public CombatTargetRegistry Registry       => _registry;
        public IDamageResolver      Resolver       => _skillResolver;
        public bool                 AllowsCombat   => _allowsCombat;
        public int                  SessionEpoch   => _sessionEpoch;
        public ReconnectVault       ReconnectVault => _reconnectVault;
        public DungeonSessionHost   SessionHost    => _host;

        // 구조화 로그를 설치한 서버 빌드에서만 부트스트랩이 넣는다. 틱 수행 시간만 재고 대기 시간은 넣지 않는다
        public ServerMetricsSampler Metrics        { set => _metrics = value; }

        // 보스전 출정 시작 틱에 만든다. 일반전, 마을, 시작 전, 생성 실패면 null이다
        public BossServer           Boss           => _boss;

        // 던전 출정을 연결한 뒤에만 있다. 마을과 GameDB 없는 시험 설치는 null이다
        public PotionServer         Potions        => _potions;

        public CombatMeterRecorder  CombatMeter    => _combatMeter;

        // 보스전에서 열 보스 정의 ID. 보스전 출정 계약이 있으면 계약 값이고, 없으면 실행 설정(-boss-id, 기본 개발 보스)이다
        public string               BossId         => ((_host != null) && (_host.Sortie.IsBoss)) ? _host.Sortie.BossId : _bossId;

        // 기믹 사건의 복제와 클라이언트 표시가 연결돼 서버 부트스트랩은 기본으로 켠다. 이 속성의 초기값은 꺼짐이라
        // 부트스트랩이 값을 정하지 않은 런타임은 기믹 없이 보스를 연다.
        // 보스를 만드는 틱에 한 번 읽으므로 출정 시작 전에 정한다
        /// <summary> 보스 체력 기믹 묶음을 붙일지, 서버 부트스트랩이 실행 설정으로 켠다 </summary>
        public bool                 EnableBossGimmicks { get; set; } = false;

        // 일반 적 네트워크 개체 스포너, 프리팹이 없는 씬이면 null이다. 웨이브 구현(적 담당)이 적을 만들 때 쓴다
        public IEnemyObjectHost     EnemyObjects   => _enemyHost;

        // 출정 시작 때 고정한 시작 인원별 배율, 시작 전과 마을은 1이다. 보스는 BossHealthScale을 BossServer에, DamageScale을
        // BossDamageScaler에 넣는다. 일반 적은 웨이브 구현이 WaveSessionContext의 같은 값을 EnemyServer 생성자(healthScale)와
        // CreateScaledEnemyResolver에 넣는다
        public float EnemyHealthScale { get; private set; } = 1f;
        public float BossHealthScale  { get; private set; } = 1f;
        public float DamageScale      { get; private set; } = 1f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            double now = NetworkTime.time;
            if (!_hasTicked)
            {
                _hasTicked    = true;
                _lastTickTime = now;
                return;
            }

            int steps = 0;
            while (((now - _lastTickTime) >= TICK_INTERVAL_SECONDS) && (steps < Constants.Combat.MAX_CATCHUP_TICKS))
            {
                _lastTickTime += TICK_INTERVAL_SECONDS;
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                Step(_lastTickTime, (float)TICK_INTERVAL_SECONDS);
                _metrics?.RecordTick(System.Diagnostics.Stopwatch.GetTimestamp() - started);
                ++steps;
            }

            // 따라잡지 못한 밀림은 버린다. 다음 프레임에 몰아 돌리면 틱 시간이 더 늘어 계속 밀린다
            if ((now - _lastTickTime) >= TICK_INTERVAL_SECONDS)
            {
                _lastTickTime = now;
            }
        }

        private void OnDestroy()
        {
            if (_resolver != null)
            {
                _resolver.DamageApplied -= OnDamageApplied;
                _resolver.LifeStealApplied -= OnLifeStealApplied;
            }
            if (_experienceQueue != null)
            {
                _experienceQueue.Committed -= OnExperienceCommitted;
            }
            _experience.Clear();
            if (_potions != null)
            {
                _potions.PotionApplied -= OnPotionApplied;
            }

            if (_host != null)
            {
                _host.Roster.ParticipantClosed -= OnRosterParticipantClosed;
            }

            _companionTracker?.ReleaseAll();
            _companionHost?.DestroyPool();
            _bossHost?.Despawn(_boss, _bossState);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 만든 직후 한 번 부른다, GameDB 초기화 뒤여야 한다. 인자가 틀리면 예외를 던지고 아무것도 바꾸지 않는다 </summary>
        public void Initialize(PlayerConfigSO playerConfig, float reconnectGraceSeconds, int sessionEpoch, IParticipantRoster roster,
            SkillCatalogSO skillCatalog = null)
        {
            if (playerConfig == null)
            {
                throw new ArgumentNullException(nameof(playerConfig));
            }

            if (roster == null)
            {
                throw new ArgumentNullException(nameof(roster));
            }

            if (!(reconnectGraceSeconds > 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(reconnectGraceSeconds), "재접속 유예는 0보다 커야 한다");
            }

            _skillCatalog = (skillCatalog != null) ? skillCatalog : GameDB.Skills;

            int obstacleMask = CombatLineOfSight.ResolveLayerMask(Constants.Combat.OBSTACLE_LAYER_NAME);
            if (obstacleMask == 0)
            {
                // 레이어가 생기기 전까지는 벽 너머 판정이 열려 있다. 서버 로그에서 바로 보이게 남긴다
                Debug.LogError($"[Combat] 장애물 레이어 '{Constants.Combat.OBSTACLE_LAYER_NAME}'이 없어 공격 시야 검사를 하지 않는다");
            }

            _playerConfig          = playerConfig;
            _obstacleMask          = obstacleMask;
            _roster                = roster;
            _reconnectGraceSeconds = reconnectGraceSeconds;
            _sessionEpoch          = sessionEpoch;
            _registry              = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            _ledger                = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY, Constants.Combat.HIT_LEDGER_RETENTION_SECONDS,
                Constants.Combat.LONGEST_ACTION_SECONDS);
            _resolver              = new DamageResolver(_registry, _ledger, playerConfig.DefenseReductionConstant,
                playerConfig.DamageReductionCap, playerConfig.CritDamageMultiplier, null, FindRetainedDamageSource,
                playerConfig.PlayerDefenseReductionConstant);
            _damageFeedbackBudget  = new DamageFeedbackBudget(Constants.Combat.MAX_COMBAT_TARGETS);
            _resolver.DamageApplied += OnDamageApplied;
            _resolver.LifeStealApplied += OnLifeStealApplied;
            _skillResolver         = new SkillDamageResolver(_resolver, _registry, _ledger);
            _retainedSkills        = new Dictionary<int, SkillServer>(Constants.Session.MAX_RECONNECT_RECORDS);
            _retainedSkillTicks    = new List<SkillServer>(Constants.Session.MAX_RECONNECT_RECORDS);
            _players               = new List<PlayerServer>(Constants.Combat.MAX_COMBAT_TARGETS);
            _reconnectVault        = new ReconnectVault(Constants.Session.MAX_RECONNECT_RECORDS);
            _expiredParticipants   = new List<int>(Constants.Session.MAX_RECONNECT_RECORDS);
            _pathBudget            = new PathRequestBudget(Constants.Player.PATH_REQUESTS_PER_TICK);
            _lineOfSight           = new CombatLineOfSight(obstacleMask, Constants.Combat.LINE_OF_SIGHT_HEIGHT);
            _closedParticipants    = new List<int>(Constants.Session.MAX_RECONNECT_RECORDS);

            // 성장 데이터는 GameDB가 있을 때만 잡는다. 없는 시험 설치는 모든 참가자를 기본 능력치와 원래 기술 수치로 들인다
            if (GameDB.IsInitialized)
            {
                _equipment = GameDB.Equipment;
                _growth    = GameDB.Growth;
                _talismans = new TalismanModifierResolver(GameDB.Talismans, _skillCatalog, Constants.Session.MAX_PARTY_SIZE);
            }
        }

        // 보스전 계약이 있으면 계약 보스가 이 값보다 우선한다(BossId). 카탈로그 확인은 부트스트랩이 실행 설정을 읽을 때 한다
        /// <summary> 계약이 없는 출정(개발)에서 열 기본 보스 정의 ID를 정한다, 출정 시작 전까지만 받는다 </summary>
        public void ConfigureBoss(string bossId)
        {
            if (string.IsNullOrEmpty(bossId))
            {
                throw new ArgumentException("보스 ID가 비어 있다", nameof(bossId));
            }

            if ((_host != null) && (_host.HasStarted))
            {
                throw new InvalidOperationException("출정을 시작한 뒤에는 보스를 바꾸지 않는다");
            }

            _bossId = bossId;
        }

        // 최초 플레이어 생성 전에 공통 판정기에 연결하므로 일반전과 보스 미터 경로 모두 같은 알림을 쓴다
        private void OnDamageApplied(AppliedDamageNotice notice)
        {
            if ((notice.OwnerCombatId <= 0) || (!notice.Result.IsApplied) || (notice.Result.AppliedAmount <= 0f))
            {
                return;
            }
            for (int i = 0; i < _players.Count; ++i)
            {
                PlayerServer player = _players[i];
                if (player.CombatId != notice.OwnerCombatId)
                {
                    continue;
                }
                PlayerInputState input = player.InputState;
                NetworkConnectionToClient connection = (input != null) ? input.connectionToClient : null;
                if ((player.IsDetached) || (connection == null) || (!connection.isAuthenticated) || (!connection.isReady)
                    || (!_damageFeedbackBudget.TryTake(player.ParticipantId, notice.ServerTime, out uint sequence)))
                {
                    return;
                }
                var feedback = new DamageFeedback(_sessionEpoch, sequence, notice.TargetCombatId, notice.TargetPosition,
                    notice.Result.AppliedAmount, notice.Result.IsCritical, notice.ServerTime, notice.IsDamageOverTime);
                input.SendDamageFeedback(in feedback);
                return;
            }
        }

        // 전투 기록은 보스전에만 있다. 판정기는 전투 번호만 알아 참가자 번호로 바꿔 넘긴다
        private void OnLifeStealApplied(int ownerCombatId, float healed)
        {
            if (_combatMeter == null)
            {
                return;
            }
            for (int i = 0; i < _players.Count; ++i)
            {
                if (_players[i].CombatId == ownerCombatId)
                {
                    _combatMeter.RecordLifeSteal(_players[i].ParticipantId, healed);
                    return;
                }
            }
        }

        private IDamageable FindRetainedDamageSource(int combatId)
        {
            if (_retainedSkillTicks == null)
            {
                return null;
            }
            for (int i = 0; i < _retainedSkillTicks.Count; ++i)
            {
                IDamageable caster = _retainedSkillTicks[i]?.DamageCaster;
                if ((caster != null) && (caster.CombatId == combatId))
                {
                    return caster;
                }
            }
            return null;
        }

        /// <summary> 던전 역할에서 서버를 열기 전에 한 번 부른다, 이후 참가 자격은 출정 명단으로 판단한다 </summary>
        public void BindSession(DungeonSessionHost host)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (_host != null)
            {
                throw new InvalidOperationException("이미 출정이 연결된 전투 런타임이다");
            }

            _host   = host;
            _roster = host.Roster;
            host.Roster.ParticipantClosed += OnRosterParticipantClosed;
            host.CheckpointSource      = this;
            host.SessionEndingListener = this;

            if (GameDB.IsInitialized)
            {
                _potions = new PotionServer(_playerConfig, GameDB.Life, new DungeonPotionEventLedger(host), this,
                    Constants.Session.MAX_PARTY_SIZE);
                _potions.PotionApplied += OnPotionApplied;
            }
        }

        /// <summary> 서버를 연 뒤에 한 번 부른다(Listen이 처리기를 비운다). 물약 요청은 인증된 연결의 참가자 개체로만 받는다 </summary>
        public void RegisterMessageHandlers()
        {
            NetworkServer.ReplaceHandler<PotionUseRequestMessage>(OnPotionRequest);
            NetworkServer.ReplaceHandler<SortieAbandonRequestMessage>(OnAbandonRequest);
            NetworkServer.ReplaceHandler<PrologueEventMessage>(OnPrologueEvent);
        }

        // 메인 스레드의 Mirror 수신에서 불린다. 참가자는 연결 결합으로 정하고, 프롤로그 출정이 아니면 무시한다.
        // 어댑터가 사건을 모아 두었다가 다음 틱 앞에서 처리한다
        private void OnPrologueEvent(NetworkConnectionToClient connection, PrologueEventMessage message)
        {
            if ((_host == null) || (_prologue == null) || (connection == null)
                || (!(connection.authenticationData is SessionBinding binding)) || (!binding.IsActive)
                || (!binding.Sortie.IsPrologue))
            {
                return;
            }

            _prologue.OnClientEvent(binding.ParticipantId, message.Event);
        }

        // 메인 스레드의 Mirror 수신에서 불린다. 참가자는 연결 결합으로 정하고 메시지 값은 작업 ID만 쓴다.
        // 실제 포기는 다음 틱 세션 BeginTick 뒤에 처리한다
        private void OnAbandonRequest(NetworkConnectionToClient connection, SortieAbandonRequestMessage message)
        {
            if ((_host == null) || (connection == null) || (!(connection.authenticationData is SessionBinding binding))
                || (!binding.IsActive))
            {
                return;
            }

            _host.RequestAbandon(binding.ParticipantId, message.OperationId);
        }

        // 매니저가 플레이어 생성 요청에서 먼저 부른다. 출정 종료 뒤 경매에만 다시 들어온 연결은 캐릭터 없이 경매 상태만 받는다
        public bool TryJoinWithoutPlayer(NetworkConnectionToClient connection)
        {
            if ((_host == null) || (connection == null) || (!(connection.authenticationData is SessionBinding binding))
                || (!binding.IsActive) || (!binding.IsAuctionOnly))
            {
                return false;
            }

            _host.OnAuctionOnlyJoined(binding.ParticipantId);
            return true;
        }

        /// <summary> 서버를 열기 전에 한 번 부른다, 연결하지 않으면 적 판정은 돌지만 클라이언트에 적이 보이지 않는다 </summary>
        public void EnableEnemyObjects(IEnemyObjectHost host)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (_enemyHost != null)
            {
                return;
            }

            _enemyHost = host;
        }

        /// <summary> 일반 적 공격 피해에 시작 인원 피해 배율을 곱하는 판정기, 웨이브 구현이 출정 시작 뒤 한 번 받는다 </summary>
        public IDamageResolver CreateScaledEnemyResolver()
        {
            return new BossDamageScaler(_skillResolver, DamageScale);
        }

        public bool TryGetParticipantVitals(int participantId, out CheckpointVitals vitals)
        {
            vitals = default;
            int index = IndexOfParticipant(participantId);
            if (index < 0)
            {
                return false;
            }

            PlayerServer player = _players[index];
            SkillServer  skills = player.Skills;
            vitals = new CheckpointVitals(player.Health, player.MaxHealth, (skills != null) ? skills.Energy : 0f,
                (skills != null) ? skills.Resonance : 0f, player.DeathTime, player.IsDetached);
            return true;
        }

        // 세션이 종료 이벤트를 넣기 직전에 부른다. 미적용 물약 되돌림이 종료 이벤트보다 앞 순번이 되게 한다
        public void OnSessionEnding(double serverTime)
        {
            _potions?.RevertUnapplied(serverTime);
        }

        public bool TryGetBossVitals(out float healthRate, out int phase)
        {
            healthRate = 0f;
            phase      = 0;
            if (_boss == null)
            {
                return false;
            }

            healthRate = _boss.HealthRate;
            phase      = (int)_boss.PhaseState;
            return true;
        }

        // 매니저가 연결 종료 기본 처리 전에 부른다. 진행 중인 출정의 닫히지 않은 원래 참가자만 남긴다
        public bool TryRetainPlayer(NetworkConnectionToClient connection)
        {
            if ((_host == null) || (connection == null) || (!(connection.authenticationData is SessionBinding binding)))
            {
                return false;
            }

            int participantId = binding.ParticipantId;
            int index         = IndexOfParticipant(participantId);
            if ((index < 0) || (!_host.CanRetain(participantId)))
            {
                return false;
            }

            PlayerServer player = _players[index];
            if ((player.IsDetached) || (connection.identity != player.InputState.netIdentity))
            {
                return false;
            }

            // 소유권만 떼면 연결의 소유 목록에서 빠져 기본 정리가 개체를 지우지 않는다. 다른 관찰자에게는 계속 보인다
            NetworkServer.RemovePlayerForConnection(connection, RemovePlayerOptions.KeepActive);
            player.Detach();
            _host.OnParticipantDetached(participantId, _lastTickTime);
            return true;
        }

        // 매니저가 인증된 연결의 플레이어 생성 요청에서 부른다. 남은 캐릭터가 있으면 새 개체 대신 그것을 넘긴다
        public bool TryReattachPlayer(NetworkConnectionToClient connection)
        {
            if ((_host == null) || (connection == null) || (!(connection.authenticationData is SessionBinding binding))
                || (!binding.IsActive))
            {
                return false;
            }

            int participantId = binding.ParticipantId;
            int index         = IndexOfParticipant(participantId);
            if ((binding.IsAuctionOnly) || (index < 0) || (_roster.IsClosed(participantId)))
            {
                return false;
            }

            PlayerServer player = _players[index];
            if (!player.IsDetached)
            {
                return false;
            }

            // 남은 개체는 이전 직업으로 만들어졌다. API 직업과 다르면 넘기지 않고, 새 개체 생성이 이미 전투 중인 참가자로 거절한다
            if (!RetentionRules.MatchesBoundClass(binding.PlayerClass, player.PlayerClass))
            {
                Debug.LogError($"[Combat] 남은 캐릭터의 직업이 API 직업과 달라 새 연결에 넘기지 않는다, participantId={participantId}, " +
                    $"existing={player.PlayerClass}, bound={binding.PlayerClass}");
                return false;
            }

            // 기존 개체 재결합에도 최초 hook 순서에 기대지 않고 DB 인증 이름/외형을 다시 적용한다.
            // 값이 달라졌으면 경고만 남긴다 - 재인증된 값이 항상 이긴다
            PlayerCombatState combat = player.Combat;
            CharacterAppearance previousAppearance = combat.Appearance;
            if ((!string.Equals(combat.CharacterName, binding.CharacterName, StringComparison.Ordinal))
                || (previousAppearance == null) || (!previousAppearance.ContentEquals(binding.Appearance)))
            {
                Debug.LogWarning($"[Combat] 재결합 참가자의 DB 인증 외형이 기존 값과 달라 새 값으로 갱신한다, participantId={participantId}");
            }
            combat.SetPublicIdentity(binding.CharacterName, binding.Appearance);
            combat.SetGuildName(binding.GuildName);

            if (!NetworkServer.ReplacePlayerForConnection(connection, player.InputState.gameObject, ReplacePlayerOptions.KeepActive))
            {
                return false;
            }

            BindExperience(binding);
            player.Reattach();
            _host.OnParticipantAttached(participantId, _lastTickTime);
            SendPotionState(player, 0, ERejectReason.None);
            _host.SendStatusTo(participantId);
            _combatMeter?.Resend(participantId, _lastTickTime);
            return true;
        }

        public void AddPlayer(PlayerInputState input)
        {
            if (input == null)
            {
                return;
            }

            // 인증기가 연결에 결합한 참가자만 받는다. 매니저도 막지만 다른 생성 경로가 생겨도 여기서 한 번 더 막는다
            NetworkConnectionToClient connection = input.connectionToClient;
            SessionBinding binding = (connection != null) ? (connection.authenticationData as SessionBinding) : null;
            if ((binding == null) || (!binding.IsActive) || (binding.IsAuctionOnly))
            {
                Debug.LogError($"[Combat] 인증되지 않은 연결의 플레이어라 서버 판정을 붙이지 않고 끊는다, {input.name}");
                input.DisconnectOwner();
                return;
            }

            if (!input.TryGetComponent(out PlayerCombatState combat))
            {
                Debug.LogError($"[Combat] {input.name}에 PlayerCombatState가 없어 서버 판정을 붙이지 않는다");
                input.DisconnectOwner();
                return;
            }

            double now           = NetworkTime.time;
            int    participantId = binding.ParticipantId;

            // 이번 틱 전에 유예가 끝났으면 먼저 닫아 만료된 상태로 새로 시작하지 못하게 한다
            CloseExpiredParticipants(now);
            if (_roster.IsClosed(participantId))
            {
                Debug.LogWarning($"[Combat] 재접속 유예가 끝난 참가자라 받지 않는다, participantId={participantId}");
                input.DisconnectOwner();
                return;
            }

            if (IndexOfParticipant(participantId) >= 0)
            {
                Debug.LogError($"[Combat] 이미 전투 중인 참가자다, participantId={participantId}");
                input.DisconnectOwner();
                return;
            }

            // 직업은 결합이 API 교환에서 받은 값이 정본이다. 보존된 기술 서버가 다른 직업이면 그 기술 상태는 쓰지 않는다
            EPlayerClass playerClass = binding.PlayerClass;
            if ((_retainedSkills.TryGetValue(participantId, out SkillServer retainedClass))
                && (!RetentionRules.MatchesBoundClass(playerClass, retainedClass.PlayerClass)))
            {
                Debug.LogError($"[Combat] 보존된 기술 상태의 직업이 API 직업과 달라 버린다, participantId={participantId}, " +
                    $"retained={retainedClass.PlayerClass}, bound={playerClass}");
                ReleaseRetainedSkills(participantId);
            }

            if (binding.Sortie.IsPrologue)
            {
                _prologueBinding = binding;
            }

            BindExperience(binding);
            if (!TryResolveStats(binding, playerClass, out CharacterStats stats))
            {
                input.DisconnectOwner();
                return;
            }

            // 최초 스폰 전에 DB 인증 이름/외형을 공개한다. PlayerServer 생성자가 다른 SyncVar를 먼저 설정하므로
            // 호출 순서에 기대지 않고 명시적으로 여기서 정한다
            combat.SetPublicIdentity(binding.CharacterName, binding.Appearance);
            combat.SetGuildName(binding.GuildName);
            AdoptSessionEpoch();
            EnsureCombatMeter();

            int combatId = _registry.IssueCombatId();
            var player   = new PlayerServer(input, combat, playerClass, combatId, participantId, _sessionEpoch, _playerConfig,
                in stats, _skillResolver, _registry, _lineOfSight, now);
            // 재접속 보존 상태를 소비하기 전에 자리를 확보한다. 상한 거절로 기존 자원을 잃지 않게 한다
            if (!_registry.Register(player))
            {
                player.Shutdown();
                input.DisconnectOwner();
                return;
            }
            if (_reconnectVault.TryTake(participantId, now, out ParticipantCombatSnapshot snapshot))
            {
                player.Restore(in snapshot);
                if (_retainedSkills.TryGetValue(participantId, out SkillServer retained))
                {
                    _retainedSkills.Remove(participantId);
                    _retainedSkillTicks.Remove(retained);
                    player.ConfigureSkills(_skillCatalog, _playerConfig, _registry, this, now, retained, _talismans);
                }
            }
            if (player.Skills == null)
            {
                player.ConfigureSkills(_skillCatalog, _playerConfig, _registry, this, now, null, _talismans);
            }

            _players.Add(player);
            ApplyExperience(player);
            RegisterMeterParticipant(player, in stats);
            _host?.OnParticipantAttached(participantId, _lastTickTime);
            if ((_potions != null) && (!_potions.TryRegister(participantId, binding.ParticipantKey, binding.ReservedPotions)))
            {
                Debug.LogError($"[Potion] 물약 기록 자리가 없어 이 참가자는 물약을 쓰지 못한다, participantId={participantId}");
            }

            SendPotionState(player, 0, ERejectReason.None);
            _host?.SendStatusTo(participantId);
            _combatMeter?.Resend(participantId, _lastTickTime);
        }

        // 전투 개체가 판정기를 보관하기 전에 감싼다. 출정 시작까지 기다리면 이미 만든 기술과 소환물이 기록을 우회한다
        private void EnsureCombatMeter()
        {
            if ((_combatMeter != null) || (_host == null) || (_host.SessionId == null) || (_host.Sortie.IsNormal)
                || ((_host.Session != null) && (!_host.Session.IsBossSession)))
            {
                return;
            }

            _combatMeter   = new CombatMeterRecorder(_registry, _skillCatalog, FindMeterConnection, _sessionEpoch,
                _host.SessionId);
            _skillResolver = new CombatMeterResolver(_skillResolver, _combatMeter, _resolver);
            _host.CombatMeterSink = _combatMeter;
            if (_host.Session != null)
            {
                _host.Session.SetCombatMeterSink(_combatMeter);
                _combatMeter.Begin(_host.Session.StartTime);
            }
        }

        private void RegisterMeterParticipant(PlayerServer player, in CharacterStats stats)
        {
            if (_combatMeter == null)
            {
                return;
            }

            for (int i = 0; i < _host.Roster.Count; ++i)
            {
                ParticipantSnapshot participant = _host.Roster.GetAt(i);
                if (participant.ParticipantId != player.ParticipantId)
                {
                    continue;
                }

                _combatMeter.RegisterParticipant(player.ParticipantId, player.CombatId, player.Combat.CharacterName,
                    player.PlayerClass, in stats, i, participant.ParticipantKey);
                _combatMeter.RegisterCombatId(player.ParticipantId, player.CombatId);
                _combatMeter.ObserveLifeState(player.ParticipantId, player.LifeState, _lastTickTime);
                return;
            }
        }

        private NetworkConnectionToClient FindMeterConnection(int participantId)
        {
            // 경매 재입장은 전투 개체를 만들지 않으므로 인증 결합에서 연결을 찾는다
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if ((connection != null) && (connection.isAuthenticated)
                    && (connection.authenticationData is SessionBinding binding) && (binding.IsActive)
                    && (binding.ParticipantId == participantId))
                {
                    return connection;
                }
            }

            return null;
        }

        private void OnPotionApplied(int participantId)
        {
            _combatMeter?.RecordPotion(participantId);
        }

        // 던전은 첫 입장 토큰으로 출정이 정해진 뒤에 플레이어가 생긴다. 첫 전투 개체를 만들기 전에 한 번 출정 ID에서 번호를 만든다.
        // 이미 만든 개체나 보존 기록이 있으면 그 번호로 입력을 받고 있으므로 바꾸지 않는다
        private void AdoptSessionEpoch()
        {
            if ((_epochFromSession) || (_host == null) || (_host.SessionId == null) || (_players.Count > 0)
                || (_reconnectVault.Count > 0) || (_retainedSkills.Count > 0))
            {
                return;
            }

            _sessionEpoch     = CombatServerInstaller.CreateSessionEpoch(_host.SessionId);
            _epochFromSession = true;
        }

        // 출정 고정 전투 준비로 최종 능력치와 부적을 정한다. 입장 때 한 번만 돌아 실패 이유 문자열을 만든다.
        // 출시 빌드의 던전은 전투 준비가 없거나 콘텐츠와 맞지 않으면 기본값으로 대신하지 않고 거절한다. 장비, 수련, 부적이 조용히
        // 사라진 채 전투하면 플레이어도 운영도 알아차리기 어렵다. 개발 빌드는 개발 토큰과 0011 전 출정을 시험하도록 경고하고 들인다.
        // 마을은 전투 준비를 받지 않으므로 기본값이 정상이다
        private bool TryResolveStats(SessionBinding binding, EPlayerClass playerClass, out CharacterStats stats)
        {
            int                    participantId = binding.ParticipantId;
            CharacterCombatLoadout loadout       = binding.CombatLoadout;
            bool                   strict        = (_host != null) && (!Debug.isDebugBuild);
            stats = CharacterStatsRules.Base(_playerConfig, _levelSettings,
                _experience.TryGetValue(participantId, out ExperienceBinding initialExperience)
                    ? initialExperience.Tracker.TotalExperience : 0);
            _talismans?.Unregister(participantId);
            if (loadout == null)
            {
                if (strict)
                {
                    Debug.LogError($"[Combat] 던전 입장에 전투 준비가 없어 받지 않는다, participantId={participantId}");
                    return false;
                }

                if (_host != null)
                {
                    Debug.LogWarning($"[Combat] 전투 준비가 없어 기본 능력치로 들인다(개발 빌드), participantId={participantId}");
                }

                return true;
            }

            // 고정 장비와 경험치 곡선이 같은 콘텐츠 버전이어야 저장과 전투 계산이 일치한다.
            // 부적을 등록하기 전에 거절해 실패한 참가자가 합성 칸에 남지 않게 한다
            if ((GameDB.IsInitialized) && (!string.Equals(loadout.ContentVersion, GameDB.ContentVersion, StringComparison.Ordinal)))
            {
                Debug.LogError($"[Combat] 전투 준비의 콘텐츠 버전이 서버와 다르다, participantId={participantId}, " +
                    $"loadout={loadout.ContentVersion}, server={GameDB.ContentVersion}");
                return false;
            }

            string         error    = null;
            CharacterStats computed = stats;
            if (loadout.ClassId != (int)playerClass)
            {
                error = $"직업이 결합과 다르다, loadout={loadout.ClassId}";
            }
            else if (CharacterStatsRules.TryCompute(_playerConfig, _equipment, _growth, loadout, _levelSettings,
                _experience.TryGetValue(participantId, out ExperienceBinding xp) ? xp.Tracker.TotalExperience : 0, out computed, out error))
            {
                if ((_talismans != null) && (!_talismans.TryRegister(participantId, playerClass, loadout.Talismans, out error)))
                {
                    computed = stats;
                }
            }

            if (error != null)
            {
                _talismans?.Unregister(participantId);
                if (strict)
                {
                    Debug.LogError($"[Combat] 전투 준비가 콘텐츠와 맞지 않아 받지 않는다, participantId={participantId}, {error}");
                    return false;
                }

                Debug.LogWarning($"[Combat] 전투 준비가 콘텐츠와 맞지 않아 기본 능력치로 들인다(개발 빌드), participantId={participantId}, " +
                    error);
                return true;
            }

            stats = computed;
            return true;
        }

        // 메인 스레드의 Mirror 수신에서 불린다. 참가자는 연결 결합으로 정하고 메시지 값은 물약 종류만 쓴다
        private void OnPotionRequest(NetworkConnectionToClient connection, PotionUseRequestMessage message)
        {
            if ((connection == null) || (!(connection.authenticationData is SessionBinding binding)) || (!binding.IsActive))
            {
                return;
            }

            int index = IndexOfParticipant(binding.ParticipantId);
            if (index < 0)
            {
                return;
            }

            PlayerServer player = _players[index];
            if ((player.IsDetached) || (connection.identity != player.InputState.netIdentity)
                || (!player.TryAcceptPotionInput(message.Header)))
            {
                return;
            }

            ERejectReason reason = (_potions != null)
                ? _potions.TryUse(player, message.Size, message.Header.Sequence, NetworkTime.time)
                : ERejectReason.WrongPhase;
            SendPotionState(player, message.Header.Sequence, reason);
        }

        public bool TryGetPotionUser(int participantId, out IPotionUser user)
        {
            int index = IndexOfParticipant(participantId);
            user = (index >= 0) ? _players[index] : null;
            return index >= 0;
        }

        public void OnPotionState(int participantId, uint sequence, ERejectReason reason)
        {
            int index = IndexOfParticipant(participantId);
            if (index >= 0)
            {
                SendPotionState(_players[index], sequence, reason);
            }
        }

        // 소유 연결에만 보낸다. 연결이 끊겨 남은 개체는 재접속 때 다시 보낸다
        private void SendPotionState(PlayerServer player, uint sequence, ERejectReason reason)
        {
            if ((_potions == null) || (player.IsDetached))
            {
                return;
            }

            NetworkConnectionToClient connection = player.InputState.connectionToClient;
            if (connection == null)
            {
                return;
            }

            connection.Send(_potions.CreateState(player.ParticipantId, sequence, reason));
        }

        public void RemovePlayer(PlayerInputState input)
        {
            for (int i = 0; i < _players.Count; ++i)
            {
                PlayerServer player = _players[i];
                if (player.InputState != input)
                {
                    continue;
                }

                if (_host == null)
                {
                    _experience.Remove(player.ParticipantId);
                    player.Shutdown();
                    _registry.Unregister(player.CombatId);
                    _players.RemoveAt(i);
                    return;
                }

                // 어떤 이유로 개체가 사라지든 상태를 보존한다. 끊고 다시 붙는 것으로 체력, 생사, 쿨다운을 되돌리지 못하게 한다
                ParticipantCombatSnapshot snapshot = player.CaptureSnapshot();
                double expireTime = NetworkTime.time + _reconnectGraceSeconds;
                if (_reconnectVault.Store(player.ParticipantId, in snapshot, expireTime, out int evictedParticipantId))
                {
                    Debug.LogWarning($"[Combat] 재접속 보존 상한을 넘어 가장 먼저 만료될 참가자 상태를 버린다, participantId={evictedParticipantId}");
                    ExpireRetention(evictedParticipantId);
                }
                _retainedSkills[player.ParticipantId] = player.Skills;
                _retainedSkillTicks.Add(player.Skills);
                player.Shutdown(true);
                _combatMeter?.SuspendActive(player.ParticipantId, _lastTickTime);
                _registry.Unregister(player.CombatId);
                _players.RemoveAt(i);

                // 개체를 남기지 못하고 지운 경우에도 연결 축은 유예로 넘긴다
                _host?.OnParticipantDetached(player.ParticipantId, _lastTickTime);
                return;
            }
        }

        private void BindExperience(SessionBinding binding)
        {
            if ((binding.Experience == null) || !GameDB.IsInitialized)
            {
                return;
            }
            if (_levelSettings == null)
            {
                _levelSettings = GameDB.CharacterLevel.ToSettings();
            }
            if (_experience.TryGetValue(binding.ParticipantId, out ExperienceBinding existing))
            {
                existing.Tracker.Merge(binding.Experience);
                existing.Applied = -1;
                existing.FailedTotal = -1;
                return;
            }
            _experience.Add(binding.ParticipantId, new ExperienceBinding
            {
                Key = binding.ParticipantKey, Loadout = binding.CombatLoadout,
                Tracker = new CharacterExperienceTracker(_levelSettings, binding.Experience)
            });
        }

        private void EnsureExperienceObserver()
        {
            SessionEventQueue queue = _host?.Session?.Events;
            if (queue == _experienceQueue)
            {
                return;
            }
            if (_experienceQueue != null)
            {
                _experienceQueue.Committed -= OnExperienceCommitted;
            }
            _experienceQueue = queue;
            if (queue != null)
            {
                queue.Committed += OnExperienceCommitted;
            }
        }

        private void OnExperienceCommitted(SessionEvent entry)
        {
            if (entry.Data.Type != ESessionEventType.ExperienceGranted)
            {
                return;
            }
            foreach (KeyValuePair<int, ExperienceBinding> pair in _experience)
            {
                if (pair.Value.Key == entry.Data.ParticipantId)
                {
                    pair.Value.Tracker.Commit(_experienceQueue.SessionId, entry.Seq, entry.Data.Amount);
                    return;
                }
            }
        }

        private void ApplyExperience(PlayerServer player)
        {
            if (_roster.IsClosed(player.ParticipantId)
                || !_experience.TryGetValue(player.ParticipantId, out ExperienceBinding experience))
            {
                return;
            }
            long total = experience.Tracker.TotalExperience;
            if ((experience.Applied == total) || (experience.FailedTotal == total))
            {
                return;
            }
            bool levelChanged = (experience.Applied < 0)
                || (CharacterLevelRules.Level(_levelSettings, experience.Applied) != CharacterLevelRules.Level(_levelSettings, total));
            if ((experience.Loadout != null) && levelChanged)
            {
                if (!CharacterStatsRules.TryCompute(_playerConfig, _equipment, _growth, experience.Loadout,
                    _levelSettings, total, out CharacterStats stats, out string error))
                {
                    experience.FailedTotal = total;
                    Debug.LogError("[CharacterExperience] 확정 성장 능력치 적용 실패: " + error);
                    return;
                }
                player.ApplyGrowth(in stats);
            }
            player.Combat.SetTotalExperience(total);
            experience.Applied = total;
        }

        private sealed class ExperienceBinding
        {
            public string Key;
            public CharacterCombatLoadout Loadout;
            public CharacterExperienceTracker Tracker;
            public long Applied = -1;
            public long FailedTotal = -1;
        }

        private void Step(double serverTime, float deltaTime)
        {
            AdoptSessionEpoch();
            EnsureCombatMeter();
            EnsureExperienceObserver();
            bool sessionTick = (_host != null) && (_host.BeginTick(serverTime));
            EnsureExperienceObserver();
            ApplySessionPhase();
            TrySpawnBoss(serverTime);
            TryBeginPrologue(serverTime);
            DiscardClosedParticipants();
            CloseExpiredParticipants(serverTime);
            for (int i = 0; i < _players.Count; ++i)
            {
                ApplyExperience(_players[i]);
            }
            _resolver.BeginTick(serverTime);
            _pathBudget.BeginTick();

            // 틱 예산이 모자랄 때 같은 참가자만 밀리지 않도록 시작 위치를 틱마다 돌린다
            int count = _players.Count;
            for (int i = 0; i < count; ++i)
            {
                _players[(_pathCursor + i) % count].TickMovement(deltaTime, serverTime, _pathBudget);
            }

            if (count > 0)
            {
                _pathCursor = (_pathCursor + 1) % count;
            }

            for (int i = 0; i < _players.Count; ++i)
            {
                _players[i].TickSkills(deltaTime, serverTime, _pathBudget);
                _players[i].TickAttack(serverTime);
            }
            for (int i = 0; i < _retainedSkillTicks.Count; ++i)
            {
                _retainedSkillTicks[i].Tick(serverTime, deltaTime, _pathBudget);
            }

            TickBoss(serverTime, sessionTick);
            ObserveBossMeterPhase(serverTime);
            if (sessionTick)
            {
                _host.TickWave();
                _prologue?.Tick(serverTime);
            }

            ReportLifeStates(serverTime);
            if (sessionTick)
            {
                _host.CompleteTick();
            }

            EndBossIfSessionEnded(serverTime);
            EndPrologueIfSessionEnded();
            PublishBossObject();

            // 종료 판정 뒤에 확정을 본다. 이번 틱에 출정이 끝났으면 종료 이벤트 뒤로 되돌림을 넣지 않는다
            _potions?.Tick(serverTime);

            ApproveRevives(serverTime);
            SyncCompanionObjects();
            _combatMeter?.Tick(serverTime);

            // 이동, 기술, 공격, 부활이 위치를 모두 옮긴 뒤에 보내야 예측이 틱 중간 위치를 기준으로 삼지 않는다
            for (int i = 0; i < _players.Count; ++i)
            {
                _players[i].ReplicateSkills();
                _players[i].PublishMovement(serverTime);
            }
        }

        /// <summary> 참가자 번호의 서버 플레이어 개체, 없으면 null이다. 프롤로그 어댑터가 재접속으로 바뀐 개체를 따라가는 데 쓴다 </summary>
        public PlayerServer FindPlayer(int participantId)
        {
            int index = IndexOfParticipant(participantId);
            return (index >= 0) ? _players[index] : null;
        }

        // 프롤로그 출정이 진행되는 첫 틱에 한 번 시도한다. 전장 마커나 산귀 소환기를 만들 수 없으면 플레이어가 갇히지 않도록
        // 출정을 서비스 취소로 닫는다(캐릭터 저장 단계는 그대로라 다음 입장에서 이어진다). 플레이어 개체가 생길 때까지는 기다린다
        private void TryBeginPrologue(double serverTime)
        {
            if ((_prologueChecked) || (!_scalesApplied) || (_host == null) || (!_host.IsSessionRunning) || (!_host.Sortie.IsPrologue))
            {
                return;
            }

            if ((_prologueBinding == null) || (FindPlayer(_prologueBinding.ParticipantId) == null))
            {
                return;
            }

            _prologueChecked = true;
            if (!PrologueMarkers.TryLoad(out PrologueMarkers markers, out string error))
            {
                Debug.LogError($"[Prologue] {error}");
                _host.CancelSortieService(serverTime);
                return;
            }

            PrologueEnemyField enemies = (_host.WaveFactory as WaveSessionFactory)?.CreatePrologueEnemyField(DamageScale, EnemyHealthScale);
            if (enemies == null)
            {
                Debug.LogError("[Prologue] 산귀 소환기를 만들지 못해 출정을 서비스 취소로 닫는다");
                _host.CancelSortieService(serverTime);
                return;
            }

            var reporter = new PrologueBackendReporter(_host.Gateway, _host.SessionId, _prologueBinding.CharacterId);
            _prologue = new PrologueSessionAdapter(this, _host, _prologueBinding, enemies, markers, reporter);
            _prologue.Begin(serverTime);
            Debug.Log($"[Prologue] 프롤로그 세션 시작, stage={_prologueBinding.Prologue.Stage}, " +
                $"potions={_prologueBinding.Prologue.WolbaekPotions}, talisman={_prologueBinding.Prologue.TalismanChecked}");
        }

        // 세션이 끝나면 어댑터와 월백을 정리한다
        private void EndPrologueIfSessionEnded()
        {
            if ((_prologue == null) || (_host == null) || (_host.Session == null) || (!_host.Session.IsEnded))
            {
                return;
            }

            EndPrologueBoss(_lastTickTime);
            _prologue.Dispose();
            _prologue = null;
        }

        /// <summary> 프롤로그 월백을 지정한 중심에 이지 난이도로 소환한다. 체력 하한을 두고 기믹은 붙이지 않는다, 소환하지 못하면 거짓이다 </summary>
        public bool TrySpawnPrologueBoss(Vector3 center, Vector3 forward, double serverTime)
        {
            if ((_boss != null) || (_host == null) || (!_host.Sortie.IsPrologue) || (!_host.IsSessionRunning) || (!GameDB.IsInitialized))
            {
                return false;
            }

            string bossId = _host.Sortie.BossId;
            if ((!GameDB.Progression.TryGetRegion(Constants.Combat.DEV_BOSS_REGION_ID, out ProgressionCatalogSO.RegionDefinition region))
                || (!GameDB.BossPatterns.TryGetBoss(bossId, out BossPatternCatalogSO.BossDefinition definition)))
            {
                Debug.LogError($"[Prologue] 월백 정의나 전장 지역이 없다, boss={bossId}, region={Constants.Combat.DEV_BOSS_REGION_ID}");
                return false;
            }

            float referenceAttack = GameDB.Progression.GetReferenceAttack(region);
            float referenceHealth = GameDB.Progression.GetReferenceHealth(region);
            if ((!(referenceAttack > 0f)) || (!(referenceHealth > 0f)) || (!(BossHealthScale > 0f)) || (!(DamageScale > 0f)))
            {
                Debug.LogError($"[Prologue] 기준값이나 인원 배율이 0이라 월백을 만들지 않는다, Aref={referenceAttack}, " +
                    $"Href={referenceHealth}, health={BossHealthScale}, damage={DamageScale}");
                return false;
            }

            var scaledResolver = new BossDamageScaler(_skillResolver, DamageScale);
            var boss = new BossServer(bossId, _registry.IssueCombatId(), center, forward, center, referenceAttack, referenceHealth,
                BossHealthScale, new BossPatternCatalogSource(GameDB.BossPatterns, EBossDifficulty.Easy), _registry,
                scaledResolver, new BossTerrainProbe(_obstacleMask), new SeededBossRandom(_sessionEpoch),
                connections: new BossParticipantConnections(_players, _host.Roster),
                displaceTargets: new BossPlayerDisplaceLookup(_players), chargeStructures: null,
                sealStartingCount: _host.Session.StartingCount);

            // 30초 안에 쓰러지지 않고 2페이즈 전환 체력에도 닿지 않게 한다. 기믹은 붙이지 않아 75%, 50%, 25% 체력 기믹이 없다
            boss.MinHealthRate = Mathf.Min(1f, definition.PhaseTransitionHealthRate + PROLOGUE_PHASE_MARGIN);

            if (!boss.Begin(serverTime))
            {
                Debug.LogError($"[Prologue] 월백 전투를 열지 못했다, boss={bossId}");
                return false;
            }

            if (!_registry.Register(boss))
            {
                Debug.LogError($"[Prologue] 월백을 전투 대상에 등록하지 못했다, boss={bossId}");
                return false;
            }

            _boss = boss;
            SpawnBossObject();
            Debug.Log($"[Prologue] 월백 소환, combatId={boss.CombatId}, maxHealth={boss.MaxHealth}, minRate={boss.MinHealthRate}");
            return true;
        }

        /// <summary> 프롤로그 월백 전투를 끝내고 개체를 내린다, 없으면 아무것도 하지 않는다 </summary>
        public void EndPrologueBoss(double serverTime)
        {
            if (_boss == null)
            {
                return;
            }

            if (_boss.IsActive)
            {
                _boss.EndCombat(serverTime);
            }

            if ((_bossHost != null) && (_bossState != null))
            {
                _bossHost.Despawn(_boss, _bossState);
                _bossState = null;
            }

            _registry.Unregister(_boss.CombatId);
            _boss = null;
        }

        // 출정 시작 틱에 배율을 한 번 고정하고, 진행 중일 때만 기술 전투를 연다
        private void ApplySessionPhase()
        {
            if (_host == null)
            {
                return;
            }

            bool running = _host.IsSessionRunning;
            if (running != _allowsCombat)
            {
                SetSkillCombatPhase(running);
            }

            if ((_scalesApplied) || (!_host.HasStarted))
            {
                return;
            }

            DungeonSession session = _host.Session;
            EnemyHealthScale = session.EnemyHealthScale;
            BossHealthScale  = session.BossHealthScale;
            DamageScale      = session.DamageScale;
            _scalesApplied   = true;
        }

        // 보스전 출정이 진행되는 첫 틱에 한 번만 시도한다. 보스 체력과 피해 배율은 출정 시작 때 고정되므로 그 전에는 만들 수 없다.
        // 이지는 데이터가 비어 전투를 열 수 없으면 보스 없이 진행한다. 개발 빌드는 TryCompleteBossForDevelopment로 끝낼 수 있다
        // 하드는 보스 없이 진행하지 않는다: 출정 준비 검사(BossServer.IsSortieReady)가 거짓이거나 보스를 만들지, 열지, 등록하지 못하면
        // 서비스 취소(ServiceCancelled)로 끝낸다. 데이터가 비었거나 만들 수 없는 패턴을 둔 채 열면 그 패턴만 조용히 빠진 하드가 되기 때문이다
        // 취소는 이 틱 안에서 부른다: 세션 취소는 진행 중이고 시각이 BeginTick에 준 틱 시각과 같을 때만 받으므로 Step이 넘긴 serverTime을 그대로 쓴다.
        // 취소로 명단이 닫혀도 DiscardClosedParticipants가 이 호출 바로 뒤에서 정리한다
        // TODO(기웅): 이지에서 보스를 만들지 못한 보스전도 출시 전에 서비스 취소로 끝낼지 정한다
        // TODO(기웅, 진욱): 던전 씬의 보스 생성 지점이 정해지면 원점 대신 그 위치와 방향을 쓴다
        private void TrySpawnBoss(double serverTime)
        {
            if ((_bossSpawnChecked) || (!_scalesApplied) || (!_host.IsSessionRunning))
            {
                return;
            }

            _bossSpawnChecked = true;
            if (!_host.Session.IsBossSession)
            {
                return;
            }

            // 보스전 계약이면 계약 보스, 아니면 실행 설정 기본 보스다. 계약 보스는 호스트가 입장 때 카탈로그에 있는지 확인했다
            // 난이도는 패턴 카탈로그의 정의 집합(이지, 하드 패턴)과 하드 차단을 고른다. 보스 능력치는 난이도로 바꾸지 않고(기획 미정),
            // 보상 표(RewardCatalogSO.BossRewards)는 호스트가 따로 난이도로 고른다
            string          bossId     = BossId;
            EBossDifficulty difficulty = _host.BossDifficulty;

            if (!GameDB.IsInitialized)
            {
                Debug.LogError("[Boss] GameDB 초기화 전이라 보스를 만들지 않는다");
                CancelHardSortie(difficulty, serverTime, "GameDB 초기화 전");
                return;
            }

            ProgressionCatalogSO progression = GameDB.Progression;
            if (!progression.TryGetRegion(Constants.Combat.DEV_BOSS_REGION_ID, out ProgressionCatalogSO.RegionDefinition region))
            {
                Debug.LogError($"[Boss] 전장 지역이 없어 보스를 만들지 않는다, region={Constants.Combat.DEV_BOSS_REGION_ID}");
                CancelHardSortie(difficulty, serverTime, "전장 지역 없음");
                return;
            }

            float referenceAttack = progression.GetReferenceAttack(region);
            float referenceHealth = progression.GetReferenceHealth(region);
            if ((!(referenceAttack > 0f)) || (!(referenceHealth > 0f)) || (!(BossHealthScale > 0f)) || (!(DamageScale > 0f)))
            {
                Debug.LogError($"[Boss] 기준값이나 인원 배율이 0이라 보스를 만들지 않는다, Aref={referenceAttack}, " +
                    $"Href={referenceHealth}, health={BossHealthScale}, damage={DamageScale}");
                CancelHardSortie(difficulty, serverTime, "기준값이나 인원 배율이 0");
                return;
            }

            // 전장 맵이 없어 생성 지점을 원점으로 두고 전장 중심도 생성 지점으로 본다
            Vector3 spawnPosition = Vector3.zero;
            Vector3 spawnForward  = Vector3.forward;

            // 보스 패턴과 기믹이 같은 배율 적용 판정기를 쓴다
            var scaledResolver = new BossDamageScaler(_skillResolver, DamageScale);
            var boss = new BossServer(bossId, _registry.IssueCombatId(), spawnPosition, spawnForward,
                spawnPosition, referenceAttack, referenceHealth, BossHealthScale,
                new BossPatternCatalogSource(GameDB.BossPatterns, difficulty), _registry,
                scaledResolver, new BossTerrainProbe(_obstacleMask),
                new SeededBossRandom(_sessionEpoch), connections: new BossParticipantConnections(_players, _host.Roster),
                displaceTargets: new BossPlayerDisplaceLookup(_players),
                chargeStructures: CreateChargeStructures(bossId, spawnPosition),
                sealStartingCount: _host.Session.StartingCount);

            // 이지는 준비되지 않은 정의가 있어도 기존처럼 연다(팩토리가 만들지 못한 패턴은 두뇌가 건너뛴다). 하드는 모든 정의가 준비돼야 연다
            if ((difficulty == EBossDifficulty.Hard) && (!boss.IsSortieReady(out string notReadyReason)))
            {
                Debug.LogError($"[Boss] 하드 출정 준비가 안 돼 보스를 만들지 않는다, boss={bossId}, {notReadyReason}");
                CancelHardSortie(difficulty, serverTime, "하드 출정 준비 안 됨");
                return;
            }

            // 기믹은 전투를 열기 전에만 붙는다. 일부만 붙은 보스는 인계가 끊겨 교착되므로 버린다
            if (!TryAttachBossGimmicks(boss, bossId, difficulty, referenceHealth, scaledResolver))
            {
                Debug.LogError($"[Boss] 기믹 묶음을 붙이지 못해 보스 없이 진행한다, boss={bossId}");
                CancelHardSortie(difficulty, serverTime, "기믹 묶음을 붙이지 못함");
                return;
            }

            if (!boss.Begin(serverTime))
            {
                Debug.LogError($"[Boss] 보스 전투를 열지 못해 보스 없이 진행한다, boss={bossId}");
                CancelHardSortie(difficulty, serverTime, "보스 전투를 열지 못함");
                return;
            }

            if (!_registry.Register(boss))
            {
                Debug.LogError($"[Boss] 전투 대상 등록이 거절돼 보스 없이 진행한다, boss={bossId}");
                CancelHardSortie(difficulty, serverTime, "전투 대상 등록 거절");
                return;
            }

            _boss = boss;
            SpawnBossObject();
            Debug.Log($"[Boss] 보스 전투 시작, boss={bossId}, difficulty={difficulty}, combatId={boss.CombatId}, " +
                $"maxHealth={boss.MaxHealth}, gimmicks={boss.HasGimmicks}");
        }

        // 무철만 돌진 구조물을 쓴다. 씬에 맵 담당의 표식 목록이 있으면 그것을, 없으면 기믹이 켜졌을 때만 전장 중심 기준 임시 개발 지점을 쓴다
        // 기믹이 꺼진 운영 기본값에서 임시 지점을 두면 보이지 않는 맵에 돌진만 막히므로 구조물 없이 연다
        // 표식 목록이 어긋나면 임시 지점으로 바꾸지 않고 구조물 없이 연다. 맵이 의도한 배치를 임시값으로 덮으면 잘못을 가린다
        // TODO(진욱): 무철 전장 맵에 BossChargeStructureSet과 표식을 두면 임시 개발 지점은 쓰이지 않는다
        private BossChargeStructureField CreateChargeStructures(string bossId, Vector3 arenaCenter)
        {
            if (!string.Equals(bossId, MucheolPatternFactory.BOSS_ID, StringComparison.Ordinal))
            {
                return null;
            }

            BossChargeStructureSet scene = BossChargeStructureSet.Current;
            if (scene == null)
            {
                if (!EnableBossGimmicks)
                {
                    return null;
                }

                Debug.LogWarning("[Boss] 씬에 돌진 구조물 표식 목록이 없어 임시 개발 지점을 쓴다");
                return BossChargeStructureField.CreateDevSites(arenaCenter);
            }

            if (!BossChargeStructureField.TryCreateFromScene(scene, out BossChargeStructureField field, out string reason))
            {
                Debug.LogError($"[Boss] 씬 돌진 구조물 표식이 어긋나 구조물 없이 연다, {reason}");
                return null;
            }

            return field;
        }

        // 꺼져 있거나 기믹이 없는 보스면 아무것도 하지 않고 참이다. 묶음을 만들지 못하면 기믹 없이 열도록 참이고,
        // 만든 묶음을 보스가 받지 않았을 때만 거짓이다. 보스를 만드는 틱에 한 번 돌아 입력 묶음과 조회 대리자를 새로 만든다
        private bool TryAttachBossGimmicks(BossServer boss, string bossId, EBossDifficulty difficulty, float referenceHealth,
            IDamageResolver scaledResolver)
        {
            if ((!EnableBossGimmicks) || (!string.Equals(bossId, GIMMICK_BOSS_ID, StringComparison.Ordinal)))
            {
                return true;
            }

            // 부적진의 다음 경계는 보스 정의의 2페이즈 전환 비율이다. 정의가 없으면 0을 넘겨 팩토리가 사유와 함께 거절하게 한다
            float phaseTransitionHealthRate = GameDB.BossPatterns.TryGetBoss(bossId, out BossPatternCatalogSO.BossDefinition definition)
                ? definition.PhaseTransitionHealthRate
                : 0f;

            // 기믹이 보스 패턴 선택의 난수열을 소비해 바꾸지 않도록 난수기를 따로 둔다
            var random = new SeededBossRandom(_sessionEpoch ^ GIMMICK_RANDOM_SALT);
            var inputs = new WolbaekGimmickInputs(boss, difficulty, referenceHealth, scaledResolver, _skillResolver, _registry,
                random, _players, _host.Roster, ReadRemainingRevives, phaseTransitionHealthRate);
            if (!WolbaekGimmickFactory.TryCreate(inputs, out BossGimmickSet set, out string reason))
            {
                Debug.LogError($"[Boss] 기믹 묶음을 만들지 못해 기믹 없이 연다, boss={bossId}, difficulty={difficulty}, {reason}");
                return true;
            }

            return boss.AttachGimmicks(set);
        }

        private int ReadRemainingRevives()
        {
            return _host.Session.RemainingRevives;
        }

        // 하드에서만 부른 쪽의 실패를 출정 취소로 바꾼다. 이지는 아무것도 하지 않아 보스 없이 진행하는 기존 동작을 지킨다
        private void CancelHardSortie(EBossDifficulty difficulty, double serverTime, string reason)
        {
            if (difficulty != EBossDifficulty.Hard)
            {
                return;
            }

            if (_host.CancelSortieService(serverTime))
            {
                Debug.LogError($"[Boss] 하드 출정을 서비스 취소로 끝낸다, reason={reason}");
                return;
            }

            Debug.LogError($"[Boss] 하드 출정 서비스 취소가 거절됐다, reason={reason}, sessionRunning={_host.IsSessionRunning}");
        }

        /// <summary> 서버를 열기 전에 한 번 부른다, 연결하지 않으면 보스 판정은 돌지만 클라이언트에 보스와 공격이 보이지 않는다 </summary>
        public void EnableBossObjects(IBossObjectHost host)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            // 처음 연결만 쓴다. 이미 스폰한 보스의 짝을 다른 스포너로 바꾸지 않는다
            if (_bossHost != null)
            {
                return;
            }

            _bossHost = host;
        }

        // 보스 전투가 열린 같은 틱, 첫 보스 Tick보다 앞에서 스폰한다. 스폰 메시지가 같은 신뢰 채널의 첫 예고 Rpc보다 먼저 가야
        // 클라이언트가 사건을 놓치지 않으므로 이 호출을 TickBoss 뒤로 옮기지 않는다
        private void SpawnBossObject()
        {
            if (_bossHost == null)
            {
                Debug.LogWarning("[Boss] 보스 개체 스포너가 없어 클라이언트에 보스를 보내지 않는다");
                return;
            }

            if (!_bossHost.TrySpawn(_boss, out _bossState))
            {
                _bossState = null;
                Debug.LogError($"[Boss] 보스 개체를 스폰하지 못해 클라이언트에 보스를 보내지 않는다, combatId={_boss.CombatId}");
            }
        }

        // 보스 틱, 종료 처리가 위치와 생사와 흐름을 모두 바꾼 뒤에 기록한다
        private void PublishBossObject()
        {
            if ((_bossHost == null) || (_bossState == null))
            {
                return;
            }

            _bossHost.Publish(_boss, _bossState);
        }

        // 서버 종료 등으로 개체가 스포너를 거치지 않고 파괴됐으면 보스 틱 전에 사건 연결을 끊어 파괴된 개체로 Rpc를 보내지 않는다
        private void DetachDestroyedBossObject()
        {
            if ((ReferenceEquals(_bossState, null)) || (_bossState != null))
            {
                return;
            }

            _boss.SetEventListener(null);
            _bossState = null;
            Debug.LogWarning($"[Boss] 보스 개체가 파괴돼 사건 전달을 멈춘다, combatId={_boss.CombatId}");
        }

        // 플레이어 기술과 공격 뒤, 생사 보고 앞에 둔다. 같은 틱에 플레이어가 보스를 처치하면 두뇌가 사망을 먼저 보고
        // 공격하지 않고, 보스 공격으로 마지막 생존자가 죽으면 같은 틱의 종료 판정이 전멸을 본다
        private void TickBoss(double serverTime, bool sessionTick)
        {
            if (_boss == null)
            {
                return;
            }

            DetachDestroyedBossObject();
            if ((_allowsCombat) && (_boss.IsActive))
            {
                _boss.Tick(serverTime);
            }

            if (sessionTick)
            {
                ReportBossGimmicks(serverTime);
            }

            // 세션 틱이 열리지 않은 틱이면 받지 않으므로 다음 틱에 다시 알린다
            if ((sessionTick) && (_boss.HasUnreportedDefeat) && (_host.ReportBossDefeated()))
            {
                _boss.MarkDefeatReported();
                Debug.Log($"[Boss] 보스 처치를 세션에 알렸다, combatId={_boss.CombatId}");
            }
        }

        // 세션 틱 안에서만 부른다. 최종 성공은 기믹이 한 번만 내주므로 세션이 받을 때까지 여기서 들고 다음 세션 틱에 다시 알린다.
        // 정지와 실패는 종료 판정보다 앞에서 알려야 같은 틱의 종료 순서(최종 성공, 기믹 실패, 전멸)가 세션에서 정해진다.
        // 로그 문자열은 성공, 정지, 실패가 처리되는 틱에만 만든다
        private void ReportBossGimmicks(double serverTime)
        {
            if (!_boss.HasGimmicks)
            {
                return;
            }

            if (_boss.TryConsumeFinalSuccess(out _))
            {
                _finalSuccessUnreported = true;
            }

            if (_finalSuccessUnreported)
            {
                if (_host.ReportBossFinalSuccess())
                {
                    _finalSuccessUnreported = false;
                    Debug.Log($"[Boss] 기믹 최종 성공을 세션에 알렸다, combatId={_boss.CombatId}");

                    // 성공이 이 틱의 종료 사유다. 같은 틱의 정지를 서비스 취소로 바꾸지 않는다
                    return;
                }

                // 끝난 출정은 다시 받지 않는다
                if (!_host.IsSessionRunning)
                {
                    _finalSuccessUnreported = false;
                }
            }

            if ((_gimmickStopHandled)
                || (!_boss.TryGetGimmickStop(out EBossGimmickStop kind, out ESessionEndReason failureReason, out string detail))
                || (kind == EBossGimmickStop.None))
            {
                return;
            }

            // 같은 정지를 틱마다 다시 알리지 않는다. 세션이 거절하는 경우는 이미 끝났거나 저장 장애로 이 틱에 끝나는 출정이다
            _gimmickStopHandled = true;
            bool accepted = (kind == EBossGimmickStop.Halted)
                ? _host.CancelSortieService(serverTime)
                : _host.ReportBossFightFailed(failureReason);
            if (accepted)
            {
                Debug.LogError($"[Boss] 기믹이 멈춰 출정을 끝낸다, combatId={_boss.CombatId}, kind={kind}, " +
                    $"reason={((kind == EBossGimmickStop.Halted) ? ESessionEndReason.ServiceCancelled : failureReason)}, {detail}");
                return;
            }

            Debug.LogError($"[Boss] 기믹 정지를 세션이 받지 않았다, combatId={_boss.CombatId}, kind={kind}, " +
                $"reason={failureReason}, sessionRunning={_host.IsSessionRunning}, {detail}");
        }

        // 피해 처리 뒤 정본 단계와 대기를 함께 반영한다. 전환 틱 피해는 이전 단계에 남고 인계 대기는 새 단계에 쌓인다
        private void ObserveBossMeterPhase(double serverTime)
        {
            if ((_combatMeter == null) || (_boss == null))
            {
                return;
            }

            int phase = _boss.Phase;
            if ((phase < FIRST_METER_PHASE) || (phase > Constants.CombatMeter.MAX_PHASES))
            {
                return;
            }

            EBossPhaseState state = _boss.PhaseState;
            bool waiting = (state == EBossPhaseState.AwaitingHandoff) || (state == EBossPhaseState.AwaitingReadiness);
            _combatMeter.ObservePhase((byte)phase, waiting, serverTime);
        }

        // 승패는 세션 종료 사유가 정본이다. 같은 틱 보스 사망과 전멸이면 보스는 이미 사망 상태라 종료 요청을 받지 않는다
        private void EndBossIfSessionEnded(double serverTime)
        {
            if ((_boss == null) || (!_boss.IsActive) || (!_host.Session.IsEnded))
            {
                return;
            }

            _boss.EndCombat(serverTime);
            Debug.Log($"[Boss] 출정 종료로 보스 전투를 멈춘다, reason={_host.Session.EndReason}");
        }

        // 생사가 바뀐 참가자만 명단에 알린다. 연결이 끊겨 남은 캐릭터의 사망도 여기서 기록된다
        private void ReportLifeStates(double serverTime)
        {
            if (_host == null)
            {
                return;
            }

            for (int i = 0; i < _players.Count; ++i)
            {
                PlayerServer player = _players[i];
                _combatMeter?.ObserveLifeState(player.ParticipantId, player.LifeState, serverTime);
                if (player.LifeState == player.ReportedLifeState)
                {
                    continue;
                }

                _host.ReportLifeState(player.ParticipantId, player.LifeState);
                player.ReportedLifeState = player.LifeState;
            }
        }

        // 명단 닫힘은 세션 처리 중에 통지되므로 번호만 모아 두고 틱 앞에서 처리한다. 통지 안에서 개체를 지우면
        // OnStopServer가 같은 호출 스택에서 선수 목록을 바꾼다
        private void OnRosterParticipantClosed(int participantId, ESessionEndReason reason, double serverTime)
        {
            _combatMeter?.CloseParticipant(participantId, serverTime);
            if (_closedParticipants.Count < _closedParticipants.Capacity)
            {
                _closedParticipants.Add(participantId);
            }
        }

        // 유예 만료, 포기, 세션 종료로 닫힌 참가자 중 연결 없이 남은 캐릭터를 지운다. 접속 중인 캐릭터는 귀환할 때까지 둔다
        private void DiscardClosedParticipants()
        {
            if (_closedParticipants.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _closedParticipants.Count; ++i)
            {
                int index = IndexOfParticipant(_closedParticipants[i]);
                if ((index < 0) || (!_players[index].IsDetached))
                {
                    continue;
                }

                DiscardDetachedPlayer(index);
            }

            _closedParticipants.Clear();
        }

        // 선수 목록에서 먼저 빼고 지운다. 그래야 OnStopServer에서 오는 RemovePlayer가 상태를 보존소에 다시 넣지 않는다.
        // 만료는 드물어서 이때만 로그 문자열을 만든다
        private void DiscardDetachedPlayer(int index)
        {
            PlayerServer player = _players[index];
            GameObject   owner  = player.InputState.gameObject;
            player.Shutdown();
            _registry.Unregister(player.CombatId);
            _players.RemoveAt(index);
            Debug.Log($"[Combat] 명단에서 닫힌 참가자의 남은 캐릭터를 지운다, participantId={player.ParticipantId}");
            if ((NetworkServer.active) && (owner != null))
            {
                NetworkServer.Destroy(owner);
            }
        }

        /// <summary> 세션이 전투 가능한 단계일 때만 연다, 시작 기본값은 닫힘이다 </summary>
        public void SetSkillCombatPhase(bool allowsCombat)
        {
            _allowsCombat = allowsCombat;
            if (allowsCombat)
            {
                return;
            }
            for (int i = 0; i < _players.Count; ++i)
            {
                _players[i].StopSkillCombat();
            }
            for (int i = 0; i < _retainedSkillTicks.Count; ++i)
            {
                _retainedSkillTicks[i].Shutdown();
            }
        }

        /// <summary> 서버를 열기 전에 한 번 부른다, 연결하지 않으면 동료 판정은 돌지만 클라이언트에 개체가 보이지 않는다 </summary>
        public void EnableCompanionObjects(ICompanionObjectHost host)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            // 설치처럼 여러 번 불려도 처음 연결만 쓴다. 이미 스폰한 개체의 짝을 다른 스포너로 바꾸지 않는다
            if (_companionHost != null)
            {
                return;
            }

            _companionHost    = host;
            _companionTracker = new CompanionSpawnTracker(host, Constants.Combat.MAX_COMPANION_OBJECTS);
        }

        // 기술, 보스 피해, 종료 판정, 부활이 동료를 모두 바꾼 뒤에 짝을 맞춘다. 같은 틱에 복귀한 동료를 먼저 내려야
        // 재호출이 상한 안에서 새 개체를 받는다. 보존 기술 서버의 동료도 남은 수명 동안 화면에 남긴다
        private void SyncCompanionObjects()
        {
            if (_companionTracker == null)
            {
                return;
            }

            _companionTracker.ReleaseStale();
            for (int i = 0; i < _players.Count; ++i)
            {
                _companionTracker.Track(_players[i].Skills);
            }
            for (int i = 0; i < _retainedSkillTicks.Count; ++i)
            {
                _companionTracker.Track(_retainedSkillTicks[i]);
            }
        }

        // 개체가 지워진 경로의 보존 기록만 만료시킨다. 던전에서는 명단을 닫으면 세션이 ParticipantSettled를 저장한다.
        // 만료는 드물어서 이때만 로그 문자열을 만든다
        private void CloseExpiredParticipants(double serverTime)
        {
            if (_reconnectVault.Count == 0)
            {
                return;
            }

            _expiredParticipants.Clear();
            if (_reconnectVault.CollectExpired(serverTime, _expiredParticipants) == 0)
            {
                return;
            }

            for (int i = 0; i < _expiredParticipants.Count; ++i)
            {
                int participantId = _expiredParticipants[i];
                ExpireRetention(participantId);
                Debug.Log($"[Combat] 재접속 유예 만료로 참가자 상태를 버린다, participantId={participantId}");
            }

            _expiredParticipants.Clear();
        }

        // 보존 상태는 항상 버린다. 명단을 닫는 것(개인 실패 정산, 재합류 금지)은 진행 중인 던전 출정에서만 한다
        private void ExpireRetention(int participantId)
        {
            if (RetentionRules.ClosesRosterOnExpiry(_host != null, (_host != null) && (_host.ClosesParticipantOnRetentionExpiry)))
            {
                _roster.Close(participantId);
            }

            ReleaseRetainedSkills(participantId);
        }

        // 종료 판정 뒤에 돈다. 접속 중인 캐릭터의 부활 요청만 본다. 유예 중인 캐릭터가 공용 횟수를 쓰지 않게 한다
        private void ApproveRevives(double serverTime)
        {
            if ((_host == null) || (!_host.IsSessionRunning))
            {
                return;
            }

            EParticipantLifeState deathState = _host.LifeStateOnDeath();
            bool candidatesLoaded = false;
            for (int i = 0; i < _players.Count; ++i)
            {
                PlayerServer player = _players[i];
                if ((deathState == EParticipantLifeState.Spectating) && (player.LifeState == EParticipantLifeState.Dead))
                {
                    player.EnterSpectating();
                    continue;
                }

                if ((!player.HasReviveRequest) || (player.IsDetached) || (serverTime < player.NextReviveCheckTime))
                {
                    continue;
                }

                // 평가할 요청이 있는 틱에만 한 번 읽는다. 지점 Transform이 움직여도 그 틱의 위치를 쓴다.
                // 안전 지점은 협동 보스에만 쓰므로 일반전은 읽지 않는다
                if ((!candidatesLoaded) && (_host.Session.IsBossSession))
                {
                    LoadRevivePoints();
                    candidatesLoaded = true;
                }

                ApproveRevive(player, serverTime);
            }
        }

        private void ApproveRevive(PlayerServer player, double serverTime)
        {
            int           participantId = player.ParticipantId;
            ERejectReason reason        = _host.CheckRevive(participantId, player.DeathTime, serverTime);
            if (reason != ERejectReason.None)
            {
                player.RejectRevive(reason);
                return;
            }

            // 일반전은 안전 지점이나 위험 종료를 기다리지 않고 사망 위치에서 되살아난다. 협동 보스만 안전 지점을 고른다.
            // 지점이 없으면 횟수를 쓰지 않고 다음 재평가 시각까지 요청을 남긴다
            Vector3 point;
            bool    found = (_host.Session.IsBossSession)
                ? TryFindBossRevivePoint(player, serverTime, out point)
                : player.TryFindNormalRevivePoint(out point);
            if (!found)
            {
                player.DeferRevive(serverTime + _host.SafePointRetryInterval);
                return;
            }

            reason = _host.CommitRevive(participantId, player.DeathTime, serverTime, out float healthRate);
            if (reason != ERejectReason.None)
            {
                player.RejectRevive(reason);
                return;
            }

            player.Revive(point, healthRate, serverTime);
            _combatMeter?.RecordRevive(participantId, serverTime);
            player.ReportedLifeState = player.LifeState;
        }

        // 진행 중인 보스 기믹이 부활을 막거나 지점을 정하면 그것을 따른다. 거짓이면 부른 쪽이 횟수를 쓰지 않고 요청을 미룬다.
        // 기믹이 지점을 맡았는데 줄 곳이 없으면 기존 지점 선택으로 되돌아가지 않는다. 사라진 바닥 위로 되살리지 않기 위해서다
        private bool TryFindBossRevivePoint(PlayerServer player, double serverTime, out Vector3 point)
        {
            point = default;
            if ((_boss != null) && (_boss.IsActive))
            {
                if (!_boss.IsReviveWindowOpen)
                {
                    return false;
                }

                EBossRevivePoint managed = _boss.TryGetGimmickRevivePoint(player.CombatId, out point);
                if (managed != EBossRevivePoint.NotManaged)
                {
                    return managed == EBossRevivePoint.Available;
                }
            }

            return player.TryFindRevivePoint(_revivePoints, serverTime, out point);
        }

        // 협동 보스 전용이다. 씬에 지정 지점이 없으면 선택기를 비워 두고 PlayerServer가 사망 위치 근처 NavMesh로 대신한다(경고는 한 번만)
        private void LoadRevivePoints()
        {
            if (_revivePoints == null)
            {
                _revivePoints = new RevivePointSelector(_registry, new CombatReviveHazardQuery(), _host.SafePointEnemyClearance);
            }

            _revivePoints.ClearCandidates();
            SafeRevivePointSet set = SafeRevivePointSet.Current;
            if (set != null)
            {
                for (int i = 0; i < set.Count; ++i)
                {
                    if (set.TryGetPosition(i, out Vector3 position))
                    {
                        _revivePoints.AddCandidate(position);
                    }
                }
            }

            if ((_revivePoints.CandidateCount == 0) && (!_warnedNoRevivePoints))
            {
                _warnedNoRevivePoints = true;
                Debug.LogWarning("[Combat] 씬에 안전 복귀 지점이 없어 사망 위치 근처 NavMesh 지점으로 부활시킨다");
            }
        }

        private int IndexOfParticipant(int participantId)
        {
            for (int i = 0; i < _players.Count; ++i)
            {
                if (_players[i].ParticipantId == participantId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void ReleaseRetainedSkills(int participantId)
        {
            if (!_retainedSkills.TryGetValue(participantId, out SkillServer skills))
            {
                return;
            }

            skills.Shutdown();
            _retainedSkills.Remove(participantId);
            _retainedSkillTicks.Remove(skills);
        }
    }
}
