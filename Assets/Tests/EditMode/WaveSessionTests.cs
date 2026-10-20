// System
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

// Unity
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Systems;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public sealed class WaveSessionTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float REFERENCE_ATTACK = 10f;
        private const float REFERENCE_HEALTH = 100f;
        private const float STEP_SECONDS     = 0.5f;
        private const int   MAX_STEPS        = 4000;
        private const float WAVE_X_STRIDE    = 100f;
        private const float ARRIVAL_BACKOFF  = 20f; // 다음 웨이브 시작 지점은 그 웨이브 출현 지점에서 이만큼 떨어진 곳
        private const int   WAVE_COUNT       = 6;
        private const int   TOTAL_ENEMIES    = 180;
        private const int   REGISTRY_SIZE    = 256;
        private const string FOREST_SCENE_PATH = "Assets/00.Scenes/Release/DungeonForest.unity";

        private const double TELEGRAPH_STEP_SECONDS = 0.05;
        private const int    TELEGRAPH_LIMIT_STEPS  = 1200;

        /****************************************
        *                Types
        ****************************************/

        private class FakeObjectHost : IEnemyObjectHost
        {
            public readonly List<EnemyServer>     Spawned    = new List<EnemyServer>();
            public readonly List<NetworkIdentity> Identities = new List<NetworkIdentity>();
            public int DespawnCount;
            public int PublishCount;

            public virtual bool TrySpawn(EnemyServer enemy, out NetworkIdentity instance)
            {
                var root = new GameObject("FakeEnemy");
                instance = root.AddComponent<NetworkIdentity>();
                Spawned.Add(enemy);
                Identities.Add(instance);
                return true;
            }

            public void Publish(EnemyServer enemy, NetworkIdentity instance)
            {
                ++PublishCount;
            }

            public void Despawn(NetworkIdentity instance)
            {
                ++DespawnCount;
            }

            public void Cleanup()
            {
                for (int i = 0; i < Identities.Count; ++i)
                {
                    if (Identities[i] != null)
                    {
                        Object.DestroyImmediate(Identities[i].gameObject);
                    }
                }
            }
        }

        private sealed class RecordingProjectileListener : IBossProjectileListener
        {
            public readonly List<int>                LaunchedIds = new List<int>();
            public readonly List<int>                EndedIds    = new List<int>();
            public readonly List<EBossProjectileEnd> EndReasons  = new List<EBossProjectileEnd>();

            public void OnProjectileLaunched(int castInstanceId, int projectileId, Vector3 origin, Vector3 direction, float speed, float width,
                float pathLength, double launchServerTime)
            {
                LaunchedIds.Add(projectileId);
            }

            public void OnProjectileEnded(int castInstanceId, int projectileId, EBossProjectileEnd end, Vector3 position, int hitTargetId)
            {
                EndedIds.Add(projectileId);
                EndReasons.Add(end);
            }
        }

        private sealed class RecordingPresentationHost : FakeObjectHost, IEnemyPresentationHost
        {
            public readonly List<string>     Log             = new List<string>();
            public readonly List<int>        DescribedIds    = new List<int>();
            public readonly List<EEnemyRole> DescribedRoles  = new List<EEnemyRole>();
            public readonly List<int>        ReservedIds     = new List<int>();
            public readonly List<EEnemyRole> ReservedRoles   = new List<EEnemyRole>();
            public readonly List<float>      ReservedRadii   = new List<float>();
            public readonly List<double>     ReservedStarts  = new List<double>();
            public readonly List<double>     ReservedEnds    = new List<double>();
            public readonly List<int>        MaterializedIds = new List<int>();
            public readonly List<int>        CanceledIds     = new List<int>();
            public readonly List<EEnemyState> Behaviors      = new List<EEnemyState>();
            public int CancelAllCount;
            public int ListenerCount;
            public RecordingProjectileListener Projectiles;
            public int BeginCount;
            public int EndCount;
            public int DuplicateBehaviorCount;
            private int _nextPreview;

            public override bool TrySpawn(EnemyServer enemy, out NetworkIdentity instance)
            {
                Log.Add($"spawn:{enemy.CombatId}");
                return base.TrySpawn(enemy, out instance);
            }

            public void DescribeEnemy(int combatId, EEnemyRole role)
            {
                Log.Add($"describe:{combatId}");
                DescribedIds.Add(combatId);
                DescribedRoles.Add(role);
            }

            public void SetBehavior(NetworkIdentity instance, EEnemyState behavior)
            {
                if ((Behaviors.Count > 0) && (Behaviors[Behaviors.Count - 1] == behavior))
                {
                    ++DuplicateBehaviorCount;
                }

                Behaviors.Add(behavior);
            }

            public void BeginTelegraph(NetworkIdentity instance, EEnemyAttackShape shape, Vector3 origin, Vector3 direction, float range,
                float width, double startServerTime, double endServerTime)
            {
                ++BeginCount;
            }

            public void EndTelegraph(NetworkIdentity instance)
            {
                ++EndCount;
            }

            public int ReserveSpawnPreview(Vector3 position, EEnemyRole role, float radius, double startServerTime, double endServerTime)
            {
                int id = ++_nextPreview;
                ReservedIds.Add(id);
                ReservedRoles.Add(role);
                ReservedRadii.Add(radius);
                ReservedStarts.Add(startServerTime);
                ReservedEnds.Add(endServerTime);
                return id;
            }

            public void MaterializeSpawnPreview(int previewId)
            {
                MaterializedIds.Add(previewId);
            }

            public void CancelSpawnPreview(int previewId)
            {
                CanceledIds.Add(previewId);
            }

            public void CancelAllSpawnPreviews()
            {
                ++CancelAllCount;
            }

            public IBossProjectileListener CreateProjectileListener()
            {
                ++ListenerCount;
                Projectiles = new RecordingProjectileListener();
                return Projectiles;
            }
        }

        private sealed class FakeResolver : IDamageResolver
        {
            private int _next;

            public DamageResult Apply(in DamageRequest request)
            {
                return default;
            }

            public int IssueInstanceId()
            {
                return ++_next;
            }
        }

        private sealed class FakeConnections : IBossParticipantConnection
        {
            public readonly HashSet<int> Connected = new HashSet<int>();

            public bool IsConnected(int combatId)
            {
                return Connected.Contains(combatId);
            }
        }

        private sealed class RecordingListener : IWaveSessionListener
        {
            public readonly List<string> Events       = new List<string>();
            public readonly List<string> SourceIds    = new List<string>();
            public readonly List<int>    ClearedWaves = new List<int>();
            public int                   EndedCount;
            public WaveSessionOutcome    Outcome;

            public void OnEnemyDefeated(int waveIndex, string sourceId, string killerParticipantId)
            {
                Events.Add("defeated");
                SourceIds.Add(sourceId);
            }

            public void OnWaveCleared(int waveIndex)
            {
                Events.Add("cleared");
                ClearedWaves.Add(waveIndex);
            }

            public void OnWaveSessionEnded(in WaveSessionOutcome outcome)
            {
                Events.Add("ended");
                ++EndedCount;
                Outcome = outcome;
            }
        }

        private sealed class PlayerStub : IDamageable
        {
            public int                   CombatId        { get; set; }
            public ECombatFaction        Faction         => ECombatFaction.Player;
            public EParticipantLifeState LifeState       { get; set; } = EParticipantLifeState.Alive;
            public bool                  IsSummon        => false;
            public int                   OwnerCombatId   => CombatIds.NONE;
            public Vector3               Position        { get; set; }
            public Vector3               Forward         => Vector3.forward;
            public float                 BodyRadius      => 0.5f;
            public float                 Health          => 100f;
            public float                 MaxHealth       => 100f;
            public float                 Defense         => 0f;
            public bool                  IsInvulnerable  => false;
            public bool                  IsGuardingFront => false;

            public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
            {
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        private CombatTargetRegistry  _registry;
        private FakeObjectHost        _host;
        private RecordingListener     _listener;
        private WaveConfigSO          _waves;
        private EnemyCatalogSO        _enemies;
        private IWaveSpawnPointSource _spawns;
        private PlayerStub            _arrivalPlayer;
        private bool                  _arrivalRegistered;

        /****************************************
        *              Test Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _registry          = new CombatTargetRegistry(REGISTRY_SIZE);
            _host              = new FakeObjectHost();
            _listener          = new RecordingListener();
            _waves             = ScriptableObject.CreateInstance<WaveConfigSO>();
            _enemies           = ScriptableObject.CreateInstance<EnemyCatalogSO>();
            _spawns            = CreateWaveSpawns();
            _arrivalPlayer     = new PlayerStub { CombatId = _registry.IssueCombatId() };
            _arrivalRegistered = false;
        }

        [TearDown]
        public void TearDown()
        {
            _host.Cleanup();
            Object.DestroyImmediate(_waves);
            Object.DestroyImmediate(_enemies);
        }

        /****************************************
        *                 Tests
        ****************************************/

        [Test]
        public void Tick_FullPlay_ClearsSixWavesInOrderAndSucceeds()
        {
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            PlayUntilFinished(session);

            Assert.That(session.IsFinished, Is.True);
            Assert.That(_listener.EndedCount, Is.EqualTo(1));
            Assert.That(_listener.Outcome.Reason, Is.EqualTo(ESessionEndReason.Success));
            Assert.That(_listener.Outcome.ClearedWaveCount, Is.EqualTo(WAVE_COUNT));
            Assert.That(_listener.Outcome.DefeatedCount, Is.EqualTo(TOTAL_ENEMIES));
            Assert.That(_listener.ClearedWaves, Is.EqualTo(new List<int> { 0, 1, 2, 3, 4, 5 }));
            Assert.That(new HashSet<string>(_listener.SourceIds), Has.Count.EqualTo(TOTAL_ENEMIES));
            Assert.That(_listener.SourceIds, Is.EqualTo(BuildCanonicalSourceIds()));
        }

        [Test]
        public void Tick_ReleaseForestLayout_ClearsAllSixWaves()
        {
            var scene = EditorSceneManager.OpenPreviewScene(FOREST_SCENE_PATH);
            try
            {
                DungeonSpawnLayout layout = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    DungeonSpawnLayout found = root.GetComponentInChildren<DungeonSpawnLayout>(true);
                    if (found != null)
                    {
                        Assert.That(layout, Is.Null);
                        layout = found;
                    }
                }
                Assert.That(layout, Is.Not.Null);
                Assert.That(layout.TryValidate(out string reason), Is.True, reason);
                _spawns = new DungeonLayoutWaveSpawnPoints(() => layout, 0, 0, 0f);
                WaveSession session = CreateSession(1f);
                session.Begin(0.0);
                PlayUntilFinished(session);
                Assert.That(_listener.Outcome.Reason, Is.EqualTo(ESessionEndReason.Success));
                Assert.That(_listener.Outcome.ClearedWaveCount, Is.EqualTo(WAVE_COUNT));
                Assert.That(_listener.Outcome.DefeatedCount, Is.EqualTo(TOTAL_ENEMIES));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void Tick_FinalKill_NotifiesDefeatedClearedEndedInOrder()
        {
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            PlayUntilFinished(session);

            int count = _listener.Events.Count;
            Assert.That(_listener.Events[count - 3], Is.EqualTo("defeated"));
            Assert.That(_listener.Events[count - 2], Is.EqualTo("cleared"));
            Assert.That(_listener.Events[count - 1], Is.EqualTo("ended"));
        }

        [Test]
        public void Tick_BeforeTelegraph_ReservesSlotsThenSpawnsAfterPreview()
        {
            WaveSession session = CreateSession(1f);
            int expected = Math.Min(_waves.MaxActiveEnemies, _waves.Waves[0].CountEnemies());

            session.Begin(0.0);
            session.Tick(0.5);
            Assert.That(session.ReservedCount, Is.EqualTo(expected));
            Assert.That(_host.Spawned, Is.Empty);

            session.Tick(1.0);
            Assert.That(_host.Spawned, Is.Empty);

            session.Tick(0.5 + _waves.SpawnTelegraphSeconds);
            Assert.That(_host.Spawned, Has.Count.EqualTo(expected));
            Assert.That(session.ReservedCount, Is.Zero);
        }

        [Test]
        public void Tick_ActiveLimit_ReservesEmptySlotsWithoutDroppingEnemies()
        {
            SetPrivateField(_waves, "_maxActiveEnemies", 5);
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            session.Tick(1.0);
            Assert.That(session.ReservedCount, Is.EqualTo(5));
            Assert.That(_host.Spawned, Is.Empty);

            session.Tick(2.0);
            Assert.That(_host.Spawned, Has.Count.EqualTo(5));
            Assert.That(session.ActiveCount, Is.EqualTo(5));
            Assert.That(session.ReservedCount, Is.Zero);

            _host.Spawned[0].ApplyDamage(0f, true, 2.0);
            _host.Spawned[1].ApplyDamage(0f, true, 2.0);
            session.Tick(2.5);

            Assert.That(session.ActiveCount, Is.EqualTo(3));
            Assert.That(session.ReservedCount, Is.EqualTo(2));
            Assert.That(_host.Spawned, Has.Count.EqualTo(5));

            session.Tick(3.5);
            Assert.That(_host.Spawned, Has.Count.EqualTo(7));
            Assert.That(session.Progress.RemainingInWave, Is.EqualTo(_waves.Waves[0].CountEnemies() - 2));
        }

        [Test]
        public void Tick_HealthScale_AppliesToSpawnedEnemies()
        {
            WaveSession session = CreateSession(2f);

            session.Begin(0.0);
            session.Tick(0.5);
            session.Tick(0.5 + _waves.SpawnTelegraphSeconds);

            float expected = 2.4f * REFERENCE_ATTACK * 2f;
            Assert.That(_host.Spawned[0].MaxHealth, Is.EqualTo(expected).Within(0.001f));
        }

        [Test]
        public void Tick_Timeout_EndsWithTimeoutAndReleasesEnemies()
        {
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            session.Tick(0.5);
            session.Tick(0.5 + _waves.SpawnTelegraphSeconds);
            int spawned = _host.Spawned.Count;
            Assert.That(spawned, Is.GreaterThan(0));

            session.Tick(_waves.TimeLimitSeconds);

            Assert.That(_listener.EndedCount, Is.EqualTo(1));
            Assert.That(_listener.Outcome.Reason, Is.EqualTo(ESessionEndReason.Timeout));
            Assert.That(_host.DespawnCount, Is.EqualTo(spawned));
            Assert.That(_registry.TryGetDamageable(_host.Spawned[0].CombatId, out _), Is.False);

            session.Tick(_waves.TimeLimitSeconds + 1.0);
            Assert.That(_listener.EndedCount, Is.EqualTo(1));
        }

        [Test]
        public void Tick_LastKillAndTimeoutOnSameTick_Succeeds()
        {
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            double now = 0.0;
            for (int i = 0; (i < MAX_STEPS) && (_host.Spawned.Count < TOTAL_ENEMIES); ++i)
            {
                now += STEP_SECONDS;
                session.Tick(now);
                if (_host.Spawned.Count >= TOTAL_ENEMIES)
                {
                    break;
                }

                KillAllAlive(now);
                DriveArrival(session);
            }

            Assert.That(_host.Spawned, Has.Count.EqualTo(TOTAL_ENEMIES));
            Assert.That(session.Progress.WaveIndex, Is.EqualTo(WAVE_COUNT - 1));

            EnemyServer last = _host.Spawned[TOTAL_ENEMIES - 1];
            for (int i = 0; i < (TOTAL_ENEMIES - 1); ++i)
            {
                _host.Spawned[i].ApplyDamage(0f, true, now);
            }

            now += STEP_SECONDS;
            session.Tick(now);

            Assert.That(session.IsRunning, Is.True);
            Assert.That(session.Progress.RemainingInWave, Is.EqualTo(1));
            Assert.That(CountAlive(), Is.EqualTo(1));
            Assert.That(last.LifeState, Is.EqualTo(EParticipantLifeState.Alive));

            now += STEP_SECONDS;
            SetPrivateField(_waves, "_timeLimitSeconds", (float)now);
            last.ApplyDamage(0f, true, now);
            session.Tick(now);

            Assert.That(_listener.Outcome.Reason, Is.EqualTo(ESessionEndReason.Success));
            Assert.That(_listener.EndedCount, Is.EqualTo(1));
            Assert.That(session.IsAwaitingArrival, Is.False);
        }

        [Test]
        public void Abort_WhileRunning_ReleasesEnemiesWithoutNotification()
        {
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            session.Tick(0.5);
            session.Tick(0.5 + _waves.SpawnTelegraphSeconds);
            int spawned = _host.Spawned.Count;
            Assert.That(spawned, Is.GreaterThan(0));

            session.Abort();

            Assert.That(session.IsFinished, Is.True);
            Assert.That(_host.DespawnCount, Is.EqualTo(spawned));
            Assert.That(_registry.TryGetDamageable(_host.Spawned[0].CombatId, out _), Is.False);
            Assert.That(_listener.EndedCount, Is.Zero);
            Assert.That(_listener.Events, Is.Empty);
        }

        [Test]
        public void Tick_AfterWaveClear_WaitsForArrivalWithoutAnyIntermission()
        {
            WaveSession session = CreateSession(1f);
            session.Begin(0.0);
            double now = ClearUntilAwaiting(session);

            Assert.That(_listener.ClearedWaves, Is.EqualTo(new List<int> { 0 }));
            Assert.That(session.TryGetArrivalPoint(out Vector3 point), Is.True);
            Assert.That(point.x, Is.EqualTo(WAVE_X_STRIDE - ARRIVAL_BACKOFF).Within(0.001f));
            int spawnedAtClear = _host.Spawned.Count;

            session.Tick(now + 100.0);
            Assert.That(session.IsAwaitingArrival, Is.True);
            Assert.That(session.Progress.WaveIndex, Is.EqualTo(1));
            Assert.That(session.ReservedCount, Is.Zero);
            Assert.That(_host.Spawned, Has.Count.EqualTo(spawnedAtClear));

            PlaceArrivalPlayer(point);
            session.Tick(now + 100.5);
            Assert.That(session.IsAwaitingArrival, Is.False);
            Assert.That(session.ReservedCount, Is.GreaterThan(0));
        }

        [Test]
        public void Tick_ArrivalFarOrOnAnotherFloor_DoesNotStartNextWave()
        {
            WaveSession session = CreateSession(1f);
            session.Begin(0.0);
            double now = ClearUntilAwaiting(session);
            session.TryGetArrivalPoint(out Vector3 point);

            PlaceArrivalPlayer(point + new Vector3(10f, 0f, 0f));
            session.Tick(now + 1.0);
            Assert.That(session.IsAwaitingArrival, Is.True);

            PlaceArrivalPlayer(point + new Vector3(0f, 5f, 0f));
            session.Tick(now + 2.0);
            Assert.That(session.IsAwaitingArrival, Is.True);

            PlaceArrivalPlayer(point + new Vector3(3.1f, 0f, 0f));
            session.Tick(now + 3.0);
            Assert.That(session.IsAwaitingArrival, Is.True);
            Assert.That(_host.Spawned.Count, Is.EqualTo(CountSpawnedUpTo(0)));

            PlaceArrivalPlayer(point + new Vector3(2.9f, 0f, 0f));
            session.Tick(now + 4.0);
            Assert.That(session.IsAwaitingArrival, Is.False);
        }

        [Test]
        public void Tick_DeadPlayerAtArrivalPoint_DoesNotStartNextWave()
        {
            WaveSession session = CreateSession(1f);
            session.Begin(0.0);
            double now = ClearUntilAwaiting(session);
            session.TryGetArrivalPoint(out Vector3 point);

            _arrivalPlayer.LifeState = EParticipantLifeState.Dead;
            PlaceArrivalPlayer(point);
            session.Tick(now + 1.0);
            Assert.That(session.IsAwaitingArrival, Is.True);

            _arrivalPlayer.LifeState = EParticipantLifeState.Alive;
            session.Tick(now + 2.0);
            Assert.That(session.IsAwaitingArrival, Is.False);
        }

        [Test]
        public void Tick_DisconnectedPlayerAtArrivalPoint_DoesNotStartNextWave()
        {
            var connections = new FakeConnections();
            connections.Connected.Add(_arrivalPlayer.CombatId);
            WaveSession session = CreateSession(1f, connections);
            session.Begin(0.0);
            double now = ClearUntilAwaiting(session);
            session.TryGetArrivalPoint(out Vector3 point);

            connections.Connected.Clear();
            PlaceArrivalPlayer(point);
            session.Tick(now + 1.0);
            Assert.That(session.IsAwaitingArrival, Is.True);

            connections.Connected.Add(_arrivalPlayer.CombatId);
            session.Tick(now + 2.0);
            Assert.That(session.IsAwaitingArrival, Is.False);
        }

        [Test]
        public void Tick_PlayerAlreadyInsideArrivalRadius_StartsNextWaveOnClearTick()
        {
            WaveSession session = CreateSession(1f);
            _arrivalPlayer.Position = new Vector3(WAVE_X_STRIDE - ARRIVAL_BACKOFF, 0f, 0f);
            Assert.That(_registry.Register(_arrivalPlayer), Is.True);
            _arrivalRegistered = true;

            session.Begin(0.0);
            double now = 0.0;
            for (int i = 0; (i < MAX_STEPS) && (_listener.ClearedWaves.Count == 0); ++i)
            {
                now += STEP_SECONDS;
                session.Tick(now);
                KillAllAlive(now);
            }

            Assert.That(_listener.ClearedWaves, Is.EqualTo(new List<int> { 0 }));
            Assert.That(session.IsAwaitingArrival, Is.False);
            Assert.That(session.Progress.WaveIndex, Is.EqualTo(1));
        }

        [Test]
        public void Tick_TimeoutWhileAwaitingArrival_EndsWithTimeout()
        {
            WaveSession session = CreateSession(1f);
            session.Begin(0.0);
            ClearUntilAwaiting(session);

            session.Tick(_waves.TimeLimitSeconds);

            Assert.That(_listener.EndedCount, Is.EqualTo(1));
            Assert.That(_listener.Outcome.Reason, Is.EqualTo(ESessionEndReason.Timeout));
            Assert.That(_listener.Outcome.ClearedWaveCount, Is.EqualTo(1));
            Assert.That(session.IsAwaitingArrival, Is.False);
        }

        [Test]
        public void Tick_NoNextWavePoint_StallsAndLogsOnceUntilTimeout()
        {
            var byWave = new Vector3[WAVE_COUNT][];
            for (int w = 0; w < WAVE_COUNT; ++w)
            {
                byWave[w] = new[] { new Vector3(w * WAVE_X_STRIDE, 0f, 0f) };
            }

            _spawns = new ArrayWaveSpawnPoints(byWave);
            WaveSession session = CreateSession(1f);
            LogAssert.Expect(LogType.Error, new Regex("다음 웨이브 시작 지점이 없어"));
            session.Begin(0.0);
            double now = ClearUntilAwaiting(session);

            PlaceArrivalPlayer(new Vector3(WAVE_X_STRIDE, 0f, 0f));
            session.Tick(now + 1.0);
            session.Tick(now + 2.0);

            Assert.That(session.IsRunning, Is.True);
            Assert.That(session.IsAwaitingArrival, Is.True);
            Assert.That(session.TryGetArrivalPoint(out _), Is.False);

            session.Tick(_waves.TimeLimitSeconds);
            Assert.That(_listener.Outcome.Reason, Is.EqualTo(ESessionEndReason.Timeout));
        }

        [Test]
        public void Tick_SeveralDefeatsInOneTick_NotifiesInSpawnOrder()
        {
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            session.Tick(0.5);
            session.Tick(0.5 + _waves.SpawnTelegraphSeconds);

            _host.Spawned[9].ApplyDamage(0f, true, 2.0);
            _host.Spawned[2].ApplyDamage(0f, true, 2.0);
            _host.Spawned[5].ApplyDamage(0f, true, 2.0);
            session.Tick(2.0);

            Assert.That(_listener.SourceIds, Is.EqualTo(new List<string>
            {
                _waves.GetSourceId(0, 2), _waves.GetSourceId(0, 5), _waves.GetSourceId(0, 9)
            }));
        }

        // 서로 1m 이상 떨어진 점만 두고 동시 예약을 4개로 줄여, 띠 선택만 따로 본다(겹침이 끼지 않는다)
        [Test]
        public void Tick_SpawnBand_UsesRoleBandAndSkipsExclusionRadius()
        {
            var player = new PlayerStub { CombatId = _registry.IssueCombatId(), Position = Vector3.zero };
            Assert.That(_registry.Register(player), Is.True);
            SetPrivateField(_waves, "_maxActiveEnemies", 4);
            var points = new[]
            {
                new Vector3(3f, 0f, 0f), new Vector3(6f, 0f, 0f), new Vector3(7f, 0f, 0f), new Vector3(8f, 0f, 0f), new Vector3(9f, 0f, 0f),
                new Vector3(10f, 0f, 0f), new Vector3(11f, 0f, 0f), new Vector3(12f, 0f, 0f), new Vector3(13f, 0f, 0f),
                new Vector3(14f, 0f, 0f), new Vector3(20f, 0f, 0f)
            };
            var byWave = new Vector3[WAVE_COUNT][];
            for (int w = 0; w < WAVE_COUNT; ++w)
            {
                byWave[w] = points;
            }

            _spawns = new ArrayWaveSpawnPoints(byWave);
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            session.Tick(0.5);
            session.Tick(0.5 + _waves.SpawnTelegraphSeconds);

            Assert.That(_host.Spawned, Has.Count.EqualTo(4));
            for (int i = 0; i < _host.Spawned.Count; ++i)
            {
                float x = _host.Spawned[i].Position.x;
                bool  ranged = IsRangedPlanEntry(0, i);
                Assert.That(x, Is.GreaterThanOrEqualTo(ranged ? WaveSession.RANGED_BAND_MIN : WaveSession.NEAR_BAND_MIN), $"index={i}");
                Assert.That(x, Is.LessThanOrEqualTo(ranged ? WaveSession.RANGED_BAND_MAX : WaveSession.NEAR_BAND_MAX), $"index={i}");
            }
        }

        // 예약 위치와 겹치는 후보는 띠 안이어도 띠 밖의 겹치지 않는 후보보다 뒤다
        [Test]
        public void Tick_OverlapWithReservation_OutranksBandDeviation()
        {
            var player = new PlayerStub { CombatId = _registry.IssueCombatId(), Position = Vector3.zero };
            Assert.That(_registry.Register(player), Is.True);
            SetPrivateField(_waves, "_maxActiveEnemies", 2);
            var byWave = new Vector3[WAVE_COUNT][];
            for (int w = 0; w < WAVE_COUNT; ++w)
            {
                byWave[w] = new[] { new Vector3(10f, 0f, 0f), new Vector3(17f, 0f, 0f) };
            }

            _spawns = new ArrayWaveSpawnPoints(byWave);
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            session.Tick(0.5);
            session.Tick(0.5 + _waves.SpawnTelegraphSeconds);

            Assert.That(_host.Spawned, Has.Count.EqualTo(2));
            Assert.That(_host.Spawned[0].Position.x, Is.EqualTo(10f).Within(0.001f));
            Assert.That(_host.Spawned[1].Position.x, Is.EqualTo(17f).Within(0.001f));
        }

        [Test]
        public void Tick_OnlyOverlappingCandidate_StillSpawnsOnIt()
        {
            var player = new PlayerStub { CombatId = _registry.IssueCombatId(), Position = Vector3.zero };
            Assert.That(_registry.Register(player), Is.True);
            SetPrivateField(_waves, "_maxActiveEnemies", 2);
            var byWave = new Vector3[WAVE_COUNT][];
            for (int w = 0; w < WAVE_COUNT; ++w)
            {
                byWave[w] = new[] { new Vector3(10f, 0f, 0f) };
            }

            _spawns = new ArrayWaveSpawnPoints(byWave);
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            session.Tick(0.5);
            session.Tick(0.5 + _waves.SpawnTelegraphSeconds);

            Assert.That(_host.Spawned, Has.Count.EqualTo(2));
            Assert.That(_host.Spawned[0].Position.x, Is.EqualTo(10f).Within(0.001f));
            Assert.That(_host.Spawned[1].Position.x, Is.EqualTo(10f).Within(0.001f));
        }

        /****************************************
        *            Presentation Host
        ****************************************/

        [Test]
        public void Presentation_HostWithoutInterface_StillSpawnsAndClears()
        {
            Assert.That(_host is IEnemyPresentationHost, Is.False);
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            session.Tick(0.5);
            session.Tick(0.5 + _waves.SpawnTelegraphSeconds);

            Assert.That(_host.Spawned, Is.Not.Empty);
            session.Abort();
            Assert.That(_host.DespawnCount, Is.EqualTo(_host.Spawned.Count));
        }

        [Test]
        public void Presentation_CreateSession_AsksHostForProjectileListenerOnce()
        {
            RecordingPresentationHost host = UseRecordingHost();

            CreateSession(1f);

            Assert.That(host.ListenerCount, Is.EqualTo(1));
        }

        [Test]
        public void Presentation_DescribeEnemy_RunsBeforeSpawnWithRoleFromDefinition()
        {
            RecordingPresentationHost host = UseRecordingHost();
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            session.Tick(0.5);
            session.Tick(0.5 + _waves.SpawnTelegraphSeconds);

            Assert.That(host.Spawned, Is.Not.Empty);
            for (int i = 0; i < host.Spawned.Count; ++i)
            {
                int id = host.Spawned[i].CombatId;
                int describedAt = host.Log.IndexOf($"describe:{id}");
                int spawnedAt   = host.Log.IndexOf($"spawn:{id}");
                Assert.That(describedAt, Is.GreaterThanOrEqualTo(0), $"index={i}");
                Assert.That(describedAt, Is.LessThan(spawnedAt), $"index={i}");

                EEnemyRole role = host.DescribedRoles[host.DescribedIds.IndexOf(id)];
                Assert.That(role, Is.EqualTo(GetPlanDefinition(0, i).Role), $"index={i}");
            }
        }

        [Test]
        public void Presentation_SpawnPreview_ReservedOnReservationAndMaterializedOnSpawn()
        {
            RecordingPresentationHost host = UseRecordingHost();
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            session.Tick(0.5);

            Assert.That(host.ReservedIds, Has.Count.EqualTo(session.ReservedCount));
            Assert.That(host.MaterializedIds, Is.Empty);
            for (int i = 0; i < host.ReservedIds.Count; ++i)
            {
                Assert.That(host.ReservedRoles[i], Is.EqualTo(GetPlanDefinition(0, i).Role), $"index={i}");
                Assert.That(host.ReservedStarts[i], Is.EqualTo(0.5).Within(0.0001));
                Assert.That(host.ReservedEnds[i], Is.EqualTo(0.5 + _waves.SpawnTelegraphSeconds).Within(0.0001));
                Assert.That(host.ReservedRadii[i], Is.GreaterThan(0f));
            }

            session.Tick(0.5 + _waves.SpawnTelegraphSeconds);

            Assert.That(host.MaterializedIds, Is.EquivalentTo(host.ReservedIds.GetRange(0, host.Spawned.Count)));
            Assert.That(host.CanceledIds, Is.Empty);
        }

        [Test]
        public void Presentation_AbortWithOpenReservations_CancelsEveryPreview()
        {
            RecordingPresentationHost host = UseRecordingHost();
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            session.Tick(0.5);
            int reserved = session.ReservedCount;
            int cancelAllBefore = host.CancelAllCount;
            Assert.That(reserved, Is.GreaterThan(0));

            session.Abort();

            Assert.That(host.CanceledIds, Is.EquivalentTo(host.ReservedIds));
            Assert.That(host.MaterializedIds, Is.Empty);
            Assert.That(host.CancelAllCount, Is.GreaterThan(cancelAllBefore));
        }

        [Test]
        public void Presentation_Begin_ClearsLeftoverPreviews()
        {
            RecordingPresentationHost host = UseRecordingHost();
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);

            Assert.That(host.CancelAllCount, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Presentation_Telegraph_BeginsOncePerTelegraphAndEndsOnLeave()
        {
            RecordingPresentationHost host = UseRecordingHost();
            WaveSession session = StartSingleEnemyNearPlayer();
            double now = RunUntilTelegraphBegins(session, host);

            Assert.That(host.BeginCount, Is.EqualTo(1));
            Assert.That(host.EndCount, Is.Zero);

            for (int i = 0; (i < TELEGRAPH_LIMIT_STEPS) && (host.EndCount == 0); ++i)
            {
                now += TELEGRAPH_STEP_SECONDS;
                session.Tick(now);
                Assert.That(host.BeginCount - host.EndCount, Is.InRange(0, 1));
            }

            Assert.That(host.EndCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(host.BeginCount, Is.EqualTo(host.EndCount).Or.EqualTo(host.EndCount + 1));
            Assert.That(host.DuplicateBehaviorCount, Is.Zero);
        }

        [Test]
        public void Presentation_EnemyDiesDuringTelegraph_EndsTelegraphAndMarksDead()
        {
            RecordingPresentationHost host = UseRecordingHost();
            WaveSession session = StartSingleEnemyNearPlayer();
            double now = RunUntilTelegraphBegins(session, host);

            host.Spawned[0].ApplyDamage(0f, true, now);
            session.Tick(now + TELEGRAPH_STEP_SECONDS);

            Assert.That(host.EndCount, Is.EqualTo(1));
            Assert.That(host.BeginCount, Is.EqualTo(1));
            Assert.That(host.Behaviors[host.Behaviors.Count - 1], Is.EqualTo(EEnemyState.Dead));
            Assert.That(host.DespawnCount, Is.EqualTo(1));
        }

        [Test]
        public void Presentation_AbortDuringTelegraph_EndsTelegraph()
        {
            RecordingPresentationHost host = UseRecordingHost();
            WaveSession session = StartSingleEnemyNearPlayer();
            RunUntilTelegraphBegins(session, host);

            session.Abort();

            Assert.That(host.EndCount, Is.EqualTo(1));
            Assert.That(host.DespawnCount, Is.EqualTo(1));
        }

        [Test]
        public void Abort_ProjectileInFlight_EndsOnListenerAndLeavesNoneActive()
        {
            RecordingPresentationHost host = UseRecordingHost();
            WaveSession session = StartSingleRangedEnemyNearPlayer();
            RunUntilProjectileLaunched(session, host);
            RecordingProjectileListener projectiles = host.Projectiles;
            Assert.That(projectiles.EndedIds.Count, Is.LessThan(projectiles.LaunchedIds.Count), "중단 시점에 비행 중인 투사체가 있어야 한다");

            session.Abort();

            Assert.That(projectiles.EndedIds, Is.EquivalentTo(projectiles.LaunchedIds));
            Assert.That(projectiles.EndReasons, Has.All.EqualTo(EBossProjectileEnd.Cancelled));
        }

        [Test]
        public void Tick_TimeoutWithProjectileInFlight_EndsOnListenerAndLeavesNoneActive()
        {
            RecordingPresentationHost host = UseRecordingHost();
            WaveSession session = StartSingleRangedEnemyNearPlayer();
            double now = RunUntilProjectileLaunched(session, host);
            RecordingProjectileListener projectiles = host.Projectiles;
            Assert.That(projectiles.EndedIds.Count, Is.LessThan(projectiles.LaunchedIds.Count), "시간 초과 시점에 비행 중인 투사체가 있어야 한다");

            now += TELEGRAPH_STEP_SECONDS;
            SetPrivateField(_waves, "_timeLimitSeconds", (float)now);
            session.Tick(now);

            Assert.That(_listener.Outcome.Reason, Is.EqualTo(ESessionEndReason.Timeout));
            Assert.That(projectiles.EndedIds, Is.EquivalentTo(projectiles.LaunchedIds));
            Assert.That(projectiles.EndReasons, Has.All.EqualTo(EBossProjectileEnd.Cancelled));
        }

        [Test]
        public void Tick_AllSpawnPointsBlockedForThreeSeconds_RaisesNoticeAndWarning()
        {
            var player = new PlayerStub { CombatId = _registry.IssueCombatId(), Position = Vector3.zero };
            Assert.That(_registry.Register(player), Is.True);
            _spawns = new ArrayWaveSpawnPoints(new[] { Vector3.zero });
            WaveSession session = CreateSession(1f);
            LogAssert.Expect(LogType.Warning, new Regex("막혀"));

            session.Begin(0.0);
            double now = 0.0;
            while (now < 3.0)
            {
                now += STEP_SECONDS;
                session.Tick(now);
            }

            Assert.That(session.IsSpawnBlockedNotice, Is.False);
            Assert.That(_host.Spawned, Is.Empty);

            session.Tick(3.5);
            Assert.That(session.IsSpawnBlockedNotice, Is.True);
            Assert.That(_host.Spawned, Is.Empty);
        }

        [Test]
        public void ShowRemainingMarker_TurnsOnAtThreeOrFewerAliveAfterEverythingSpawned()
        {
            WaveSession session = CreateSession(1f);
            var positions = new List<Vector3>();

            session.Begin(0.0);
            session.Tick(0.5);
            session.Tick(0.5 + _waves.SpawnTelegraphSeconds);
            int total = _waves.Waves[0].CountEnemies();
            Assert.That(_host.Spawned, Has.Count.EqualTo(total));
            Assert.That(session.ShowRemainingMarker, Is.False);

            for (int i = 0; i < (total - 4); ++i)
            {
                _host.Spawned[i].ApplyDamage(0f, true, 2.0);
            }

            session.Tick(2.0);
            Assert.That(session.ActiveCount, Is.EqualTo(4));
            Assert.That(session.ShowRemainingMarker, Is.False);

            _host.Spawned[total - 4].ApplyDamage(0f, true, 2.5);
            session.Tick(2.5);
            Assert.That(session.ActiveCount, Is.EqualTo(3));
            Assert.That(session.ShowRemainingMarker, Is.True);
            Assert.That(session.CopyRemainingPositions(positions), Is.EqualTo(3));
            Assert.That(positions, Has.Count.EqualTo(3));

            KillAllAlive(3.0);
            session.Tick(3.0);
            Assert.That(session.ShowRemainingMarker, Is.False);
            Assert.That(session.CopyRemainingPositions(positions), Is.Zero);
        }

        [Test]
        public void Tick_PlayerInsideExclusionRadius_DelaysSpawn()
        {
            var player = new PlayerStub { CombatId = _registry.IssueCombatId(), Position = Vector3.zero };
            Assert.That(_registry.Register(player), Is.True);
            _spawns = new ArrayWaveSpawnPoints(new[] { Vector3.zero });
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            session.Tick(100.0);
            Assert.That(_host.Spawned, Is.Empty);
            Assert.That(session.ReservedCount, Is.Zero);

            player.Position = new Vector3(_waves.SpawnExclusionRadius + 5f, 0f, 0f);
            session.Tick(100.5);
            Assert.That(session.ReservedCount, Is.GreaterThan(0));
            Assert.That(_host.Spawned, Is.Empty);

            session.Tick(100.5 + _waves.SpawnTelegraphSeconds);
            Assert.That(_host.Spawned, Is.Not.Empty);
        }

        [Test]
        public void Tick_DeadOrReviveWaitingPoint_BlocksReservationEvenWhenAliveAreFar()
        {
            var alive = new PlayerStub { CombatId = _registry.IssueCombatId(), Position = new Vector3(50f, 0f, 0f) };
            var dead  = new PlayerStub
            {
                CombatId = _registry.IssueCombatId(), Position = new Vector3(1f, 0f, 0f), LifeState = EParticipantLifeState.Dead
            };
            Assert.That(_registry.Register(alive), Is.True);
            Assert.That(_registry.Register(dead), Is.True);
            _spawns = new ArrayWaveSpawnPoints(new[] { Vector3.zero });
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            session.Tick(100.0);
            Assert.That(session.ReservedCount, Is.Zero);

            dead.LifeState = EParticipantLifeState.ReviveWaiting;
            session.Tick(100.5);
            Assert.That(session.ReservedCount, Is.Zero);

            dead.Position = new Vector3(_waves.SpawnExclusionRadius + 5f, 0f, 0f);
            session.Tick(101.0);
            Assert.That(session.ReservedCount, Is.GreaterThan(0));
        }

        [Test]
        public void Tick_DeadPointAppearsAfterReservation_DelaysMaterializeUntilItClears()
        {
            var alive = new PlayerStub { CombatId = _registry.IssueCombatId(), Position = new Vector3(50f, 0f, 0f) };
            Assert.That(_registry.Register(alive), Is.True);
            _spawns = new ArrayWaveSpawnPoints(new[] { Vector3.zero });
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            session.Tick(0.5);
            Assert.That(session.ReservedCount, Is.GreaterThan(0));

            var dead = new PlayerStub
            {
                CombatId = _registry.IssueCombatId(), Position = new Vector3(1f, 0f, 0f), LifeState = EParticipantLifeState.Dead
            };
            Assert.That(_registry.Register(dead), Is.True);

            session.Tick(0.5 + _waves.SpawnTelegraphSeconds);
            Assert.That(_host.Spawned, Is.Empty);
            Assert.That(session.ReservedCount, Is.GreaterThan(0));

            Assert.That(_registry.Unregister(dead.CombatId), Is.True);
            session.Tick(0.5 + _waves.SpawnTelegraphSeconds + 0.1);
            Assert.That(_host.Spawned, Is.Not.Empty);
        }

        [Test]
        public void Tick_EachWave_UsesThatWavesSpawnPoints()
        {
            WaveSession session = CreateSession(1f);

            session.Begin(0.0);
            double now = 0.0;
            for (int i = 0; (i < MAX_STEPS) && (session.IsRunning); ++i)
            {
                now += STEP_SECONDS;
                session.Tick(now);

                float expectedX = session.Progress.WaveIndex * WAVE_X_STRIDE;
                for (int e = 0; e < _host.Spawned.Count; ++e)
                {
                    if (_host.Spawned[e].LifeState == EParticipantLifeState.Alive)
                    {
                        Assert.That(_host.Spawned[e].Position.x, Is.EqualTo(expectedX).Within(0.001f));
                    }
                }

                KillAllAlive(now);
                DriveArrival(session);
            }

            Assert.That(session.IsFinished, Is.True);
        }

        [Test]
        public void TryCreate_UnknownEnemyDefinition_ReturnsFalseWithReason()
        {
            SetPrivateField(_enemies, "_enemies", new EnemyCatalogSO.EnemyDefinition[0]);

            bool created = WaveSession.TryCreate(CreateContext(1f), CreateDependencies(null), out WaveSession session, out string error);

            Assert.That(created, Is.False);
            Assert.That(session, Is.Null);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void Create_MissingRuntime_ReturnsNullAndReadsProvidersLazily()
        {
            int runtimeReads = 0;
            var factory = new WaveSessionFactory(() => { ++runtimeReads; return null; }, () => null, _spawns, _enemies, null);
            Assert.That(runtimeReads, Is.Zero);
            LogAssert.Expect(LogType.Error, new Regex("웨이브 세션을 만들 수 없다"));

            IWaveSession session = factory.Create(CreateContext(1f));

            Assert.That(session, Is.Null);
            Assert.That(runtimeReads, Is.EqualTo(1));
        }

        [Test]
        public void Create_NullRuntimeProvider_ReturnsNull()
        {
            var factory = new WaveSessionFactory(null, null, _spawns, _enemies, null);
            LogAssert.Expect(LogType.Error, new Regex("웨이브 세션을 만들 수 없다"));

            Assert.That(factory.Create(CreateContext(1f)), Is.Null);
        }

        /****************************************
        *                Methods
        ****************************************/

        private static IWaveSpawnPointSource CreateWaveSpawns()
        {
            var byWave = new Vector3[WAVE_COUNT][];
            for (int w = 0; w < WAVE_COUNT; ++w)
            {
                byWave[w] = new[] { new Vector3(w * WAVE_X_STRIDE, 0f, 0f) };
            }

            var nextPoints = new Vector3[WAVE_COUNT - 1];
            for (int w = 0; w < nextPoints.Length; ++w)
            {
                nextPoints[w] = new Vector3(((w + 1) * WAVE_X_STRIDE) - ARRIVAL_BACKOFF, 0f, 0f);
            }

            return new ArrayWaveSpawnPoints(byWave, nextPoints);
        }

        private WaveSessionContext CreateContext(float healthScale)
        {
            return new WaveSessionContext("session", null, null, 1, healthScale, 1f, _waves);
        }

        private WaveSessionDependencies CreateDependencies(IBossParticipantConnection connections)
        {
            return new WaveSessionDependencies(_registry, new FakeResolver(), _host, _spawns, _enemies, new WaveEnemyMovement(null),
                null, null, REFERENCE_ATTACK, REFERENCE_HEALTH, 0.5f, 0f, null, connections);
        }

        private WaveSession CreateSession(float healthScale, IBossParticipantConnection connections = null)
        {
            bool created = WaveSession.TryCreate(CreateContext(healthScale), CreateDependencies(connections), out WaveSession session,
                out string error);
            Assert.That(created, Is.True, error);
            session.SetListener(_listener);
            return session;
        }

        private List<string> BuildCanonicalSourceIds()
        {
            var ids = new List<string>(TOTAL_ENEMIES);
            for (int w = 0; w < WAVE_COUNT; ++w)
            {
                int count = _waves.Waves[w].CountEnemies();
                for (int i = 0; i < count; ++i)
                {
                    ids.Add(_waves.GetSourceId(w, i));
                }
            }

            return ids;
        }

        private int CountSpawnedUpTo(int waveIndex)
        {
            int total = 0;
            for (int w = 0; w <= waveIndex; ++w)
            {
                total += _waves.Waves[w].CountEnemies();
            }

            return total;
        }

        private bool IsRangedPlanEntry(int waveIndex, int planIndex)
        {
            EnemyCatalogSO.EnemyDefinition definition = GetPlanDefinition(waveIndex, planIndex);
            return (definition.AttackShape != null) && (definition.AttackShape.Shape == EEnemyAttackShape.Projectile);
        }

        private EnemyCatalogSO.EnemyDefinition GetPlanDefinition(int waveIndex, int planIndex)
        {
            int index = 0;
            WaveConfigSO.WaveDefinition wave = _waves.Waves[waveIndex];
            for (int e = 0; e < wave.Enemies.Count; ++e)
            {
                WaveConfigSO.WaveEnemyEntry entry = wave.Enemies[e];
                if ((planIndex >= index) && (planIndex < (index + entry.Count)))
                {
                    Assert.That(_enemies.TryGetEnemy(entry.EnemyId, out EnemyCatalogSO.EnemyDefinition definition), Is.True);
                    return definition;
                }

                index += entry.Count;
            }

            Assert.Fail($"배치 순번이 범위를 벗어난다, wave={waveIndex}, index={planIndex}");
            return null;
        }

        private RecordingPresentationHost UseRecordingHost()
        {
            _host.Cleanup();
            var host = new RecordingPresentationHost();
            _host = host;
            return host;
        }

        // 생존자 5m 앞에 적 한 마리만 내보내, 접근한 뒤 예고에 들어가는 흐름을 본다
        private WaveSession StartSingleEnemyNearPlayer()
        {
            var player = new PlayerStub { CombatId = _registry.IssueCombatId(), Position = new Vector3(5f, 0f, 0f) };
            Assert.That(_registry.Register(player), Is.True);
            SetPrivateField(_waves, "_maxActiveEnemies", 1);
            var byWave = new Vector3[WAVE_COUNT][];
            for (int w = 0; w < WAVE_COUNT; ++w)
            {
                byWave[w] = new[] { Vector3.zero };
            }

            _spawns = new ArrayWaveSpawnPoints(byWave);
            WaveSession session = CreateSession(1f);
            session.Begin(0.0);
            return session;
        }

        // 첫 웨이브를 원거리 한 마리로 바꿔, 투사체가 날아가는 중의 정리를 본다
        private WaveSession StartSingleRangedEnemyNearPlayer()
        {
            SetPrivateField(_waves, "_waves", new[]
            {
                new WaveConfigSO.WaveDefinition("wave_test_ranged",
                    new[] { new WaveConfigSO.WaveEnemyEntry("enemy_ranged", 1) },
                    new[] { new WaveConfigSO.SpawnGroup(0f, 1) })
            });
            Assert.That(IsRangedPlanEntry(0, 0), Is.True);
            return StartSingleEnemyNearPlayer();
        }

        private static double RunUntilProjectileLaunched(WaveSession session, RecordingPresentationHost host)
        {
            double now = 0.0;
            for (int i = 0; (i < TELEGRAPH_LIMIT_STEPS) && ((host.Projectiles == null) || (host.Projectiles.LaunchedIds.Count == 0)); ++i)
            {
                now += TELEGRAPH_STEP_SECONDS;
                session.Tick(now);
            }

            Assert.That(host.Projectiles, Is.Not.Null);
            Assert.That(host.Projectiles.LaunchedIds.Count, Is.GreaterThanOrEqualTo(1), "제한 시간 안에 투사체가 발사되지 않았다");
            return now;
        }

        private static double RunUntilTelegraphBegins(WaveSession session, RecordingPresentationHost host)
        {
            double now = 0.0;
            for (int i = 0; (i < TELEGRAPH_LIMIT_STEPS) && (host.BeginCount == 0); ++i)
            {
                now += TELEGRAPH_STEP_SECONDS;
                session.Tick(now);
            }

            Assert.That(host.BeginCount, Is.GreaterThanOrEqualTo(1), "제한 시간 안에 공격 예고가 시작되지 않았다");
            return now;
        }

        // 도착 플레이어는 도착 대기 중에만 지점에 세워 두고, 웨이브 중에는 빼서 적 두뇌와 출현 계산에 끼지 않게 한다
        private void DriveArrival(WaveSession session)
        {
            if ((session.IsAwaitingArrival) && (session.TryGetArrivalPoint(out Vector3 point)))
            {
                PlaceArrivalPlayer(point);
            }
            else if (_arrivalRegistered)
            {
                _registry.Unregister(_arrivalPlayer.CombatId);
                _arrivalRegistered = false;
            }
        }

        private void PlaceArrivalPlayer(Vector3 position)
        {
            _arrivalPlayer.Position = position;
            if (!_arrivalRegistered)
            {
                Assert.That(_registry.Register(_arrivalPlayer), Is.True);
                _arrivalRegistered = true;
            }
        }

        private double ClearUntilAwaiting(WaveSession session)
        {
            double now = 0.0;
            for (int i = 0; (i < MAX_STEPS) && (!session.IsAwaitingArrival); ++i)
            {
                now += STEP_SECONDS;
                session.Tick(now);
                if (session.IsAwaitingArrival)
                {
                    break;
                }

                KillAllAlive(now);
            }

            Assert.That(session.IsAwaitingArrival, Is.True);
            return now;
        }

        private void PlayUntilFinished(WaveSession session)
        {
            double now = 0.0;
            for (int i = 0; (i < MAX_STEPS) && (session.IsRunning); ++i)
            {
                now += STEP_SECONDS;
                session.Tick(now);
                KillAllAlive(now);
                DriveArrival(session);
            }
        }

        private int CountAlive()
        {
            int alive = 0;
            for (int i = 0; i < _host.Spawned.Count; ++i)
            {
                if (_host.Spawned[i].LifeState == EParticipantLifeState.Alive)
                {
                    ++alive;
                }
            }

            return alive;
        }

        private void KillAllAlive(double serverTime)
        {
            for (int i = 0; i < _host.Spawned.Count; ++i)
            {
                _host.Spawned[i].ApplyDamage(0f, true, serverTime);
            }
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            field.SetValue(target, value);
        }
    }
}
