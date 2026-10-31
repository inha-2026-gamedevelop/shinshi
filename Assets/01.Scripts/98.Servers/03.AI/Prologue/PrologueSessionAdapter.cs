// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.AI
{
    // 진행기(PrologueDirector)가 말하는 두 창구(사건 원천, 지시 대상)를 실제 던전 서버 세션에 잇는다. 판단은 하지 않는다:
    // 어느 사건이 언제 오는지는 서버 훅에서 받아 그대로 전하고, 진행기의 지시는 소환기, 전투 런타임, 백엔드 보고기로 옮긴다.
    // 클라이언트가 보낸 화면 사건은 받은 즉시 처리하지 않고 큐에 모아 서버 틱 앞에서 처리한다(소환과 보고가 틱 순서 안에서만 일어나게).
    // 월백 교전의 사망은 피해 처리 도중이 아니라 틱에서 생사를 보고 알린다(교전 종료가 보스 틱 한가운데에서 일어나지 않게)
    /// <summary> 프롤로그 출정 세션 하나의 서버 어댑터, CombatServerRuntime이 출정 시작 틱에 만들고 세션 틱 안에서 Tick을 부른다 </summary>
    public sealed class PrologueSessionAdapter : IPrologueEventSource, IPrologueHost, IPlayerLethalInterceptor, IDisposable
    {
        /****************************************
        *              Constants
        ****************************************/

        // 완료 기록이 이 시간 안에 확정되지 않으면 기록 없이 세션을 닫는다. 캐릭터 단계는 마무리 구간에 남아 다음 접속에서 이어진다
        private const double COMPLETE_WAIT_SECONDS = 60.0;

        // 산귀를 만들지 못했을 때 다시 시도하는 간격(초)
        private const double SPAWN_RETRY_SECONDS = 0.5;

        // 틱 앞에서 처리하려고 모아 두는 클라이언트 화면 사건의 상한. 종류가 셋뿐이라 이보다 많이 쌓이면 중복이거나 남용이다
        private const int MAX_INBOX = 16;

        /****************************************
        *                Types
        ****************************************/

        private readonly struct PendingSpawn
        {
            public readonly EPrologueEnemyKind Kind;
            public readonly int                SpawnIndex;

            public PendingSpawn(EPrologueEnemyKind kind, int spawnIndex)
            {
                Kind       = kind;
                SpawnIndex = spawnIndex;
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        private readonly CombatServerRuntime    _runtime;
        private readonly DungeonSessionHost     _host;
        private readonly SessionBinding         _binding;
        private readonly PrologueEnemyField     _enemies;
        private readonly PrologueMarkers        _markers;
        private readonly PrologueBackendReporter _reporter;
        private readonly Queue<EPrologueClientEvent> _inbox = new Queue<EPrologueClientEvent>();
        private readonly List<PendingSpawn>     _pendingSpawns = new List<PendingSpawn>();

        private PrologueDirector _director;
        private PlayerServer     _player;
        private double           _now;
        private double           _nextSpawnRetry   = 0.0;
        private double           _protectedUntil   = 0.0;
        private double           _completeDeadline = 0.0;
        private bool             _protectForever   = false;
        private bool             _placed           = false;
        private bool             _talismanChecked  = false;
        private bool             _talismanRaised   = false;
        private bool             _controlRaised    = false;
        private bool             _reviveHandled    = false;
        private bool             _downRaised       = false;
        private bool             _wolbaekOpen      = false;
        private bool             _completing       = false;
        private bool             _sessionClosed    = false;
        private bool             _potionDirty      = false;
        private bool             _forcePublish     = true;
        private bool             _disposed         = false;
        private int              _potionStock      = 0;
        private int              _lastKnownStock   = 0;
        private int              _pendingAttacks   = 0;
        private int              _pendingSkillQ    = 0;
        private NetworkConnectionToClient _lastConnection = null;
        private int              _lastReportedPotions = 0;
        private ushort           _reviveSerial     = 0;
        private PrologueStateMessage _lastSent;

        /****************************************
        *              Properties
        ****************************************/

        public PrologueDirector Director => _director;

        // IPrologueEventSource
        public event Action      AttackUsed;
        public event Action      SkillQUsed;
        public event Action<int> EnemyKilled;
        public event Action      TelegraphStarted;
        public event Action      TelegraphEnded;
        public event Action      PlayerDown;
        public event Action      PotionUsed;
        public event Action      TalismanApproached;
        public event Action      TalismanConfirmed;
        public event Action      WolbaekIntroFinished;
        public event Action      WolbaekControlStarted;
        public event Action      IllustrationFinished;

        /****************************************
        *                Methods
        ****************************************/

        public PrologueSessionAdapter(CombatServerRuntime runtime, DungeonSessionHost host, SessionBinding binding,
            PrologueEnemyField enemies, PrologueMarkers markers, PrologueBackendReporter reporter)
        {
            _runtime  = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _host     = host ?? throw new ArgumentNullException(nameof(host));
            _binding  = binding ?? throw new ArgumentNullException(nameof(binding));
            _enemies  = enemies ?? throw new ArgumentNullException(nameof(enemies));
            _markers  = markers ?? throw new ArgumentNullException(nameof(markers));
            _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
        }

        /// <summary> 저장된 단계에서 그 구간의 처음부터 시작한다, 출정이 진행 중인 틱에 한 번 부른다 </summary>
        public void Begin(double serverTime)
        {
            _now             = serverTime;
            _talismanChecked = _binding.Prologue.TalismanChecked;

            _enemies.EnemyKilled      += OnEnemyKilled;
            _enemies.TelegraphStarted += OnTelegraphStarted;
            _enemies.TelegraphEnded   += OnTelegraphEnded;
            _director = new PrologueDirector(this, this, () => _now, _talismanChecked);
            if (!PrologueStageRules.TryFromStored(_binding.Prologue.Stage, out EPrologueStage stage))
            {
                stage = EPrologueStage.Intro;
            }

            _lastReportedPotions = _binding.Prologue.WolbaekPotions;
            _director.Start(stage, _binding.Prologue.WolbaekPotions);
            EnsurePlayer();
        }

        /// <summary> 클라이언트가 화면 사건을 보냈다, 서버 틱 앞에서 처리하도록 모아 둔다 </summary>
        public void OnClientEvent(int participantId, EPrologueClientEvent clientEvent)
        {
            if ((_disposed) || (participantId != _binding.ParticipantId))
            {
                return;
            }

            // 같은 사건이 이미 대기 중이면 한 번이면 충분하다. 상한을 넘는 요청은 버린다
            if ((_inbox.Count >= MAX_INBOX) || (_inbox.Contains(clientEvent)))
            {
                return;
            }

            _inbox.Enqueue(clientEvent);
        }

        /// <summary> 세션 틱 안에서 매번 부른다 </summary>
        public void Tick(double serverTime)
        {
            if (_disposed)
            {
                return;
            }

            _now = serverTime;
            EnsurePlayer();
            DrainInbox();
            _enemies.Tick(serverTime);
            PollPotions();
            RetrySpawns(serverTime);
            UpdateProtection(serverTime);
            CheckTalisman();
            CheckWolbaekControl();
            CheckPlayerDown();
            _director.Tick();
            PublishState();
            _reporter.Tick(serverTime);
            CheckComplete();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Unhook(_player);
            _player = null;
            _enemies.EnemyKilled      -= OnEnemyKilled;
            _enemies.TelegraphStarted -= OnTelegraphStarted;
            _enemies.TelegraphEnded   -= OnTelegraphEnded;
            _director?.Dispose();
            _enemies.Dispose();
        }

        /****************************************
        *       IPlayerLethalInterceptor
        ****************************************/

        // 숲에서는 죽지 않고 제자리에서 회복한다. 월백 교전은 죽음을 그대로 두고 틱에서 생사를 보고 알린다.
        // 그 밖의 구간에서 치명 피해가 오면(보스를 내린 뒤의 잔여 피해 등) 죽지 않게 회복만 한다
        public bool TryIntercept(PlayerServer player, double serverTime)
        {
            if (_director == null)
            {
                return false;
            }

            EPrologueStage stage = _director.Stage;
            if (stage == EPrologueStage.Wolbaek)
            {
                return false;
            }

            if (stage == EPrologueStage.Forest)
            {
                _reviveHandled = false;
                PlayerDown?.Invoke();
                bool handled = _reviveHandled;
                _reviveHandled = false;
                if (handled)
                {
                    return true;
                }
            }

            player.RestoreFullHealth();
            return true;
        }

        /****************************************
        *            IPrologueHost
        ****************************************/

        public void SpawnEnemy(EPrologueEnemyKind kind, int spawnIndex)
        {
            if (!TrySpawn(kind, spawnIndex))
            {
                _pendingSpawns.Add(new PendingSpawn(kind, spawnIndex));
                _nextSpawnRetry = _now + SPAWN_RETRY_SECONDS;
            }
        }

        public void SetPotions(int count)
        {
            _potionStock = Mathf.Max(0, count);
            _potionDirty = true;
            ApplyPotions();
            _forcePublish = true;
        }

        public void ReviveToFull(float invulnerableSeconds)
        {
            if (_player != null)
            {
                _player.RestoreFullHealth();
                _player.SetServerProtection(true);
                _protectedUntil = _now + invulnerableSeconds;
            }

            ++_reviveSerial;
            _reviveHandled = true;
            _forcePublish  = true;
        }

        // 순서: 플레이어를 전장 시작점으로 옮기고 체력을 최대로 채운 뒤 월백을 소환한다. 월백이 소환 직후 플레이어를 보게 하려는 순서다
        public void StartWolbaek()
        {
            if (_player != null)
            {
                _player.SetServerProtection(false);
                _protectedUntil = 0.0;
                _protectForever = false;
                _player.TeleportTo(_markers.WolbaekPlayerStart);
                _player.RestoreFullHealth();
            }

            Vector3 toPlayer = _markers.WolbaekPlayerStart - _markers.WolbaekCenter;
            toPlayer.y = 0f;
            Vector3 forward = (toPlayer.sqrMagnitude > 0f) ? toPlayer.normalized : Vector3.forward;

            _controlRaised = false;
            if (!_runtime.TrySpawnPrologueBoss(_markers.WolbaekCenter, forward, _now))
            {
                // 월백 없이는 교전이 성립하지 않는다. 플레이어가 갇히지 않게 출정을 서비스 취소로 닫고 저장 단계에서 다시 시작한다
                Debug.LogError("[Prologue] 월백을 소환하지 못해 출정을 서비스 취소로 닫는다");
                _host.CancelSortieService(_now);
                return;
            }

            _wolbaekOpen  = true;
            _forcePublish = true;
        }

        public void EndEngagement(EWolbaekEnd reason)
        {
            _runtime.EndPrologueBoss(_now);
            _wolbaekOpen = false;

            // 마무리 구간에서 살아 있는 채로 끝나게 한다. 살아 있을 때만 보호가 의미 있다
            if ((_player != null) && (_player.LifeState == EParticipantLifeState.Alive))
            {
                _player.SetServerProtection(true);
                _protectForever = true;
            }

            _forcePublish = true;
        }

        public void ReportStage(EPrologueStage stage, int wolbaekEntryPotions)
        {
            _lastReportedPotions = wolbaekEntryPotions;
            _forcePublish        = true;

            if (stage == EPrologueStage.Done)
            {
                _reporter.ReportComplete();
                _completing       = true;
                _completeDeadline = _now + COMPLETE_WAIT_SECONDS;
                return;
            }

            _reporter.ReportStage((int)stage, wolbaekEntryPotions, _talismanChecked);
        }

        /****************************************
        *               Private
        ****************************************/

        // 플레이어 개체가 새로 생겼거나(재접속) 처음이면 훅을 걸고 상태를 다시 보낸다
        private void EnsurePlayer()
        {
            PlayerServer found = _runtime.FindPlayer(_binding.ParticipantId);

            // 재접속은 같은 개체를 새 연결에 넘기므로 개체가 같아도 연결이 바뀌면 현재 상태를 다시 보낸다
            NetworkConnectionToClient connection = ((found != null) && (found.InputState != null)) ? found.InputState.connectionToClient : null;
            if (!ReferenceEquals(connection, _lastConnection))
            {
                _lastConnection = connection;
                _forcePublish   = true;
            }

            if (ReferenceEquals(found, _player))
            {
                return;
            }

            Unhook(_player);
            _player = found;
            if (_player == null)
            {
                return;
            }

            _player.BasicAttackStarted += OnBasicAttack;
            _player.SkillCommitted     += OnSkillCommitted;
            _player.LethalInterceptor   = this;

            // 처음 붙을 때만 숲 시작점에 세운다. 재접속은 서버에 남아 있던 위치를 그대로 쓴다
            if (!_placed)
            {
                _placed = true;
                _player.TeleportTo(_markers.ForestPlayerStart);
            }

            _potionDirty  = true;
            ApplyPotions();
            _forcePublish = true;
        }

        private void Unhook(PlayerServer player)
        {
            if (player == null)
            {
                return;
            }

            player.BasicAttackStarted -= OnBasicAttack;
            player.SkillCommitted     -= OnSkillCommitted;
            if (ReferenceEquals(player.LethalInterceptor, this))
            {
                player.LethalInterceptor = null;
            }
        }

        private void ApplyPotions()
        {
            if ((!_potionDirty) || (_player == null) || (_runtime.Potions == null))
            {
                return;
            }

            if (_runtime.Potions.SetSmallStock(_binding.ParticipantId, _binding.ParticipantKey, _potionStock))
            {
                _potionDirty    = false;
                _lastKnownStock = _potionStock;
            }
        }

        // 임시 물약 수의 원본은 PotionServer 재고다. 재고가 줄어든 만큼 진행기에 사용을 알려 수량이 어긋나지 않게 한다
        // (회복이 0이어도 물약은 소모되므로 회복 사건이 아니라 재고 변화를 본다)
        private void PollPotions()
        {
            if ((_potionDirty) || (_player == null) || (_runtime.Potions == null))
            {
                return;
            }

            int current = _runtime.Potions.GetAvailable(_binding.ParticipantId, EPotionSize.Small);
            while (_lastKnownStock > current)
            {
                --_lastKnownStock;
                PotionUsed?.Invoke();
            }
        }

        private bool TrySpawn(EPrologueEnemyKind kind, int spawnIndex)
        {
            if (!_markers.TryGetEnemyPoint(kind, spawnIndex, out Vector3 point))
            {
                Debug.LogError($"[Prologue] 산귀 소환 마커가 없다, kind={kind}, index={spawnIndex}");
                return true; // 다시 시도해도 생기지 않는다
            }

            return _enemies.Spawn(kind, spawnIndex, point);
        }

        private void RetrySpawns(double serverTime)
        {
            if ((_pendingSpawns.Count == 0) || (serverTime < _nextSpawnRetry))
            {
                return;
            }

            for (int i = _pendingSpawns.Count - 1; i >= 0; --i)
            {
                if (TrySpawn(_pendingSpawns[i].Kind, _pendingSpawns[i].SpawnIndex))
                {
                    _pendingSpawns.RemoveAt(i);
                }
            }

            _nextSpawnRetry = serverTime + SPAWN_RETRY_SECONDS;
        }

        private void DrainInbox()
        {
            // 공격과 기술 사용은 서버 입력 처리 중에 알려오므로 세어 두었다가 여기서 사건으로 낸다
            // (소환과 단계 전환이 Mirror 수신 중이 아니라 틱 안에서만 일어나게 한다)
            while (_pendingAttacks > 0)
            {
                --_pendingAttacks;
                AttackUsed?.Invoke();
            }

            while (_pendingSkillQ > 0)
            {
                --_pendingSkillQ;
                SkillQUsed?.Invoke();
            }

            while (_inbox.Count > 0)
            {
                switch (_inbox.Dequeue())
                {
                    case EPrologueClientEvent.TalismanConfirmed:
                        // 서버가 부적 접근을 확인해 조사 화면을 열게 한 숲에서만 받는다. 그 밖의 확인은 무시한다
                        if ((!_talismanRaised) || (_director.Stage != EPrologueStage.Forest))
                        {
                            break;
                        }

                        _talismanChecked = true;
                        TalismanConfirmed?.Invoke();

                        // 조사 화면을 본 사실은 다음 구간 보고를 기다리지 않고 바로 저장한다
                        _reporter.ReportStage((int)_director.Stage, _lastReportedPotions, true);
                        break;
                    case EPrologueClientEvent.IllustrationFinished:
                        IllustrationFinished?.Invoke();
                        break;
                    case EPrologueClientEvent.WolbaekIntroFinished:
                        WolbaekIntroFinished?.Invoke();
                        break;
                    default:
                        break;
                }
            }
        }

        private void UpdateProtection(double serverTime)
        {
            if ((_protectedUntil > 0.0) && (serverTime >= _protectedUntil))
            {
                _protectedUntil = 0.0;
                if ((_player != null) && (!_protectForever))
                {
                    _player.SetServerProtection(false);
                }
            }
        }

        // 부적에 닿은 순간 한 번만 알린다. 이 캐릭터가 이미 조사 화면을 본 적이 있어도 진행기가 알아서 건너뛴다
        private void CheckTalisman()
        {
            if ((_talismanRaised) || (_player == null) || (_director.Stage != EPrologueStage.Forest)
                || (_player.LifeState != EParticipantLifeState.Alive))
            {
                return;
            }

            Vector3 offset = _player.Position - _markers.Talisman;
            offset.y = 0f;
            if (offset.sqrMagnitude <= (PrologueMarkers.TALISMAN_RADIUS * PrologueMarkers.TALISMAN_RADIUS))
            {
                _talismanRaised = true;
                TalismanApproached?.Invoke();
            }
        }

        // 등장 연출이 끝나 전투 상태가 되면 조작이 시작된 것이다
        private void CheckWolbaekControl()
        {
            if ((!_wolbaekOpen) || (_controlRaised))
            {
                return;
            }

            BossServer boss = _runtime.Boss;
            if ((boss != null) && (boss.FlowState == EBossFlowState.Combat))
            {
                _controlRaised = true;
                WolbaekControlStarted?.Invoke();
            }
        }

        private void CheckPlayerDown()
        {
            if (_player == null)
            {
                return;
            }

            if (_player.LifeState == EParticipantLifeState.Alive)
            {
                _downRaised = false;
                return;
            }

            if (!_downRaised)
            {
                _downRaised = true;
                PlayerDown?.Invoke();
            }
        }

        private void PublishState()
        {
            if ((_player == null) || (_player.InputState == null))
            {
                return;
            }

            var message = new PrologueStateMessage((byte)_director.Stage, (byte)_director.CurrentHint,
                (byte)Mathf.Clamp(_director.PotionCount, 0, byte.MaxValue), _director.BlocksInput, _director.IsWolbaekOpen,
                _reviveSerial);

            if ((!_forcePublish) && (message.Equals(_lastSent)))
            {
                return;
            }

            NetworkConnectionToClient connection = _player.InputState.connectionToClient;
            if ((connection == null) || (!connection.isAuthenticated))
            {
                return;
            }

            connection.Send(message);
            _lastSent     = message;
            _forcePublish = false;
        }

        // 완료 기록이 확정(또는 확정 거절, 백엔드 없음, 대기 한도)되면 세션을 성공으로 닫는다. 받을 수 있는 틱에서만 받으므로 받을 때까지 반복한다
        private void CheckComplete()
        {
            if ((!_completing) || (_sessionClosed))
            {
                return;
            }

            bool settled = _reporter.IsCompleteSettled;
            if ((!settled) && (_now < _completeDeadline))
            {
                return;
            }

            if (!settled)
            {
                Debug.LogError("[Prologue] 완료 기록이 확정되지 않았지만 대기 한도가 지나 세션을 닫는다. 캐릭터는 마무리 구간부터 다시 시작한다");
            }

            if (_host.ReportPrologueCompleted())
            {
                _sessionClosed = true;
            }
        }

        private void OnBasicAttack()
        {
            ++_pendingAttacks;
        }

        private void OnSkillCommitted(ESkillRequestKind kind, int slot)
        {
            if ((kind == ESkillRequestKind.Regular) && (slot == Constants.Combat.SKILL_SLOT_Q))
            {
                ++_pendingSkillQ;
            }
        }

        private void OnEnemyKilled(int spawnIndex)
        {
            EnemyKilled?.Invoke(spawnIndex);
        }

        private void OnTelegraphStarted()
        {
            TelegraphStarted?.Invoke();
        }

        private void OnTelegraphEnded()
        {
            TelegraphEnded?.Invoke();
        }

    }
}
