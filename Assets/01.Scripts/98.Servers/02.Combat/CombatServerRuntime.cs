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

        private CombatTargetRegistry         _registry;
        private HitLedger                    _ledger;
        private DamageResolver               _resolver;
        private SkillDamageResolver          _skillResolver;
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

        // 보스전에서 열 보스 정의 ID. 보스전 출정 계약이 있으면 계약 값이고, 없으면 실행 설정(-boss-id, 기본 개발 보스)이다
        public string               BossId         => ((_host != null) && (_host.Sortie.IsBoss)) ? _host.Sortie.BossId : _bossId;

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
                playerConfig.DamageReductionCap);
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
            }
        }

        /// <summary> 서버를 연 뒤에 한 번 부른다(Listen이 처리기를 비운다). 물약 요청은 인증된 연결의 참가자 개체로만 받는다 </summary>
        public void RegisterMessageHandlers()
        {
            NetworkServer.ReplaceHandler<PotionUseRequestMessage>(OnPotionRequest);
            NetworkServer.ReplaceHandler<SortieAbandonRequestMessage>(OnAbandonRequest);
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

            if (!NetworkServer.ReplacePlayerForConnection(connection, player.InputState.gameObject, ReplacePlayerOptions.KeepActive))
            {
                return false;
            }

            player.Reattach();
            _host.OnParticipantAttached(participantId, _lastTickTime);
            SendPotionState(player, 0, ERejectReason.None);
            _host.SendStatusTo(participantId);
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

            if (!TryResolveStats(binding, playerClass, out CharacterStats stats))
            {
                input.DisconnectOwner();
                return;
            }

            // 최초 스폰 전에 DB 인증 이름/외형을 공개한다. PlayerServer 생성자가 다른 SyncVar를 먼저 설정하므로
            // 호출 순서에 기대지 않고 명시적으로 여기서 정한다
            combat.SetPublicIdentity(binding.CharacterName, binding.Appearance);
            AdoptSessionEpoch();

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
            _host?.OnParticipantAttached(participantId, _lastTickTime);
            if ((_potions != null) && (!_potions.TryRegister(participantId, binding.ParticipantKey, binding.ReservedPotions)))
            {
                Debug.LogError($"[Potion] 물약 기록 자리가 없어 이 참가자는 물약을 쓰지 못한다, participantId={participantId}");
            }

            SendPotionState(player, 0, ERejectReason.None);
            _host?.SendStatusTo(participantId);
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
            stats = CharacterStatsRules.Base(_playerConfig);
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

            string         error    = null;
            CharacterStats computed = stats;
            if (loadout.ClassId != (int)playerClass)
            {
                error = $"직업이 결합과 다르다, loadout={loadout.ClassId}";
            }
            else if (CharacterStatsRules.TryCompute(_playerConfig, _equipment, _growth, loadout, out computed, out error))
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

            // TODO(민성): 예약 때 콘텐츠 버전과 서버 콘텐츠 버전이 다를 때 거절할지 정한다. 지금은 기록만 남긴다
            if ((GameDB.IsInitialized) && (!string.Equals(loadout.ContentVersion, GameDB.ContentVersion, StringComparison.Ordinal)))
            {
                Debug.LogWarning($"[Combat] 전투 준비의 콘텐츠 버전이 서버와 다르다, participantId={participantId}, " +
                    $"loadout={loadout.ContentVersion}, server={GameDB.ContentVersion}");
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
                _registry.Unregister(player.CombatId);
                _players.RemoveAt(i);

                // 개체를 남기지 못하고 지운 경우에도 연결 축은 유예로 넘긴다
                _host?.OnParticipantDetached(player.ParticipantId, _lastTickTime);
                return;
            }
        }

        private void Step(double serverTime, float deltaTime)
        {
            bool sessionTick = (_host != null) && (_host.BeginTick(serverTime));
            ApplySessionPhase();
            TrySpawnBoss(serverTime);
            DiscardClosedParticipants();
            CloseExpiredParticipants(serverTime);
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
            if (sessionTick)
            {
                _host.TickWave();
            }

            ReportLifeStates();
            if (sessionTick)
            {
                _host.CompleteTick();
            }

            EndBossIfSessionEnded(serverTime);
            PublishBossObject();

            // 종료 판정 뒤에 확정을 본다. 이번 틱에 출정이 끝났으면 종료 이벤트 뒤로 되돌림을 넣지 않는다
            _potions?.Tick(serverTime);

            ApproveRevives(serverTime);
            SyncCompanionObjects();

            // 이동, 기술, 공격, 부활이 위치를 모두 옮긴 뒤에 보내야 예측이 틱 중간 위치를 기준으로 삼지 않는다
            for (int i = 0; i < _players.Count; ++i)
            {
                _players[i].ReplicateSkills();
                _players[i].PublishMovement(serverTime);
            }
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
        // 데이터가 비어 전투를 열 수 없으면 보스 없이 진행한다. 개발 빌드는 TryCompleteBossForDevelopment로 끝낼 수 있다
        // TODO(민성): 출시 전에 보스를 만들지 못한 보스전을 서비스 취소로 끝낼지 정한다
        // TODO(민성): 던전 씬의 보스 생성 지점이 정해지면 원점 대신 그 위치와 방향을 쓴다
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
            // TODO(민성): 난이도(쉬움, 어려움)별 보스 능력치 차이가 기획에 생기면 _host.BossDifficulty를 보스 생성에 넘긴다.
            // 지금 난이도는 보상 표(RewardCatalogSO.BossRewards)만 고른다(docs/design/growth-economy.md 보스 보상)
            string bossId = BossId;

            if (!GameDB.IsInitialized)
            {
                Debug.LogError("[Boss] GameDB 초기화 전이라 보스를 만들지 않는다");
                return;
            }

            ProgressionCatalogSO progression = GameDB.Progression;
            if (!progression.TryGetRegion(Constants.Combat.DEV_BOSS_REGION_ID, out ProgressionCatalogSO.RegionDefinition region))
            {
                Debug.LogError($"[Boss] 전장 지역이 없어 보스를 만들지 않는다, region={Constants.Combat.DEV_BOSS_REGION_ID}");
                return;
            }

            float referenceAttack = progression.GetReferenceAttack(region);
            float referenceHealth = progression.GetReferenceHealth(region);
            if ((!(referenceAttack > 0f)) || (!(referenceHealth > 0f)) || (!(BossHealthScale > 0f)) || (!(DamageScale > 0f)))
            {
                Debug.LogError($"[Boss] 기준값이나 인원 배율이 0이라 보스를 만들지 않는다, Aref={referenceAttack}, " +
                    $"Href={referenceHealth}, health={BossHealthScale}, damage={DamageScale}");
                return;
            }

            // 전장 맵이 없어 생성 지점을 원점으로 두고 전장 중심도 생성 지점으로 본다
            Vector3 spawnPosition = Vector3.zero;
            Vector3 spawnForward  = Vector3.forward;
            var boss = new BossServer(bossId, _registry.IssueCombatId(), spawnPosition, spawnForward,
                spawnPosition, referenceAttack, referenceHealth, BossHealthScale,
                new BossPatternCatalogSource(GameDB.BossPatterns), _registry, new BossDamageScaler(_skillResolver, DamageScale),
                new BossTerrainProbe(_obstacleMask), new SeededBossRandom(_sessionEpoch));

            if (!boss.Begin(serverTime))
            {
                Debug.LogError($"[Boss] 보스 전투를 열지 못해 보스 없이 진행한다, boss={bossId}");
                return;
            }

            if (!_registry.Register(boss))
            {
                Debug.LogError($"[Boss] 전투 대상 등록이 거절돼 보스 없이 진행한다, boss={bossId}");
                return;
            }

            _boss = boss;
            SpawnBossObject();
            Debug.Log($"[Boss] 보스 전투 시작, boss={bossId}, combatId={boss.CombatId}, " +
                $"maxHealth={boss.MaxHealth}");
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

            // 세션 틱이 열리지 않은 틱이면 받지 않으므로 다음 틱에 다시 알린다
            if ((sessionTick) && (_boss.HasUnreportedDefeat) && (_host.ReportBossDefeated()))
            {
                _boss.MarkDefeatReported();
                Debug.Log($"[Boss] 보스 처치를 세션에 알렸다, combatId={_boss.CombatId}");
            }
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
        private void ReportLifeStates()
        {
            if (_host == null)
            {
                return;
            }

            for (int i = 0; i < _players.Count; ++i)
            {
                PlayerServer player = _players[i];
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

                // 평가할 요청이 있는 틱에만 한 번 읽는다. 지점 Transform이 움직여도 그 틱의 위치를 쓴다
                if (!candidatesLoaded)
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

            // 안전 지점이 없으면 횟수를 쓰지 않고 다음 재평가 시각까지 요청을 남긴다
            if (!player.TryFindRevivePoint(_revivePoints, serverTime, out Vector3 point))
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

            player.Revive(point, healthRate);
            player.ReportedLifeState = player.LifeState;
        }

        // 씬에 지정 지점이 없으면 선택기를 비워 두고 PlayerServer가 사망 위치 근처 NavMesh로 대신한다(경고는 한 번만)
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
