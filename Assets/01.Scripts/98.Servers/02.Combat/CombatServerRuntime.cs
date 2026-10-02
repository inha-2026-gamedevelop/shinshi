// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.Combat
{
    /// <summary> 서버 전투 틱을 고정 주기로 돌린다, CombatServerInstaller가 실행 중에 만들며 프리팹, 씬에 붙이지 않는다 </summary>
    public sealed class CombatServerRuntime : MonoBehaviour, ICombatSkillPhase, IServerPlayerRetention
    {
        // 한 틱 처리 순서: 세션 BeginTick(유예 만료를 먼저 닫는다) -> 닫힌 참가자의 남은 캐릭터 정리 -> 입력(이미 Command
        // 처리기가 검증해 보관) -> 이동 -> 기술, 기본 공격과 피해, 사망 -> 생사를 명단에 반영 -> 세션 CompleteTick(종료 판정)
        // -> 부활 승인 -> 복제. 같은 틱의 마지막 생존자 사망과 부활은 전멸이 우선해야 하므로 부활은 종료 판정 뒤에 둔다.
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

        /****************************************
        *              Properties
        ****************************************/

        public CombatTargetRegistry Registry       => _registry;
        public IDamageResolver      Resolver       => _skillResolver;
        public bool                 AllowsCombat   => _allowsCombat;
        public int                  SessionEpoch   => _sessionEpoch;
        public ReconnectVault       ReconnectVault => _reconnectVault;
        public DungeonSessionHost   SessionHost    => _host;

        // 출정 시작 때 고정한 시작 인원별 배율, 스포너가 적과 보스를 만들 때 읽는다. 시작 전과 마을은 1이다
        // TODO(민성): 적 스폰과 피해 계산이 붙으면 이 값을 적용 위치에서 곱한다
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
                Step(_lastTickTime, (float)TICK_INTERVAL_SECONDS);
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
            if ((index < 0) || (_roster.IsClosed(participantId)))
            {
                return false;
            }

            PlayerServer player = _players[index];
            if (!player.IsDetached)
            {
                return false;
            }

            if (!NetworkServer.ReplacePlayerForConnection(connection, player.InputState.gameObject, ReplacePlayerOptions.KeepActive))
            {
                return false;
            }

            player.Reattach();
            _host.OnParticipantAttached(participantId, _lastTickTime);
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
            if ((binding == null) || (!binding.IsActive))
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

            int combatId = _registry.IssueCombatId();
            if (_retainedSkills.TryGetValue(participantId, out SkillServer retainedClass))
            {
                combat.SetPlayerClass(retainedClass.PlayerClass);
            }
            var player   = new PlayerServer(input, combat, combatId, participantId, _sessionEpoch, _playerConfig, _skillResolver,
                _registry, _lineOfSight, now);
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
                    player.ConfigureSkills(_skillCatalog, _playerConfig, _registry, this, now, retained);
                }
            }
            if (player.Skills == null)
            {
                player.ConfigureSkills(_skillCatalog, _playerConfig, _registry, this, now);
            }

            _players.Add(player);
            _host?.OnParticipantAttached(participantId, _lastTickTime);
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

            ReportLifeStates();
            if (sessionTick)
            {
                _host.CompleteTick();
            }

            ApproveRevives(serverTime);

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

        /// <summary> 서버 스포너가 미리 생성한 동료 네트워크 상태를 참가자의 실행 객체에 결합한다 </summary>
        public bool BindCompanionState(int participantId, int slot, CompanionCombatState state)
        {
            int index = IndexOfParticipant(participantId);
            return (index >= 0) && (_players[index].Skills.BindCompanionState(slot, state));
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
            if (!player.TryFindRevivePoint(out Vector3 point))
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
