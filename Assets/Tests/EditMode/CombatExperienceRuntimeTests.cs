// System
using System;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// Third Party
using Mirror;
using NUnit.Framework;

// Project
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public sealed class CombatExperienceRuntimeTests
    {
        private const BindingFlags PRIVATE = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<Object> _created = new List<Object>();
        private CombatServerRuntime _runtime;
        private PlayerServer _player;
        private PlayerCombatState _combat;
        private SessionEventQueue _queue;
        private RecordingSink _sink;
        private bool _serverActive;

        [SetUp]
        public void SetUp()
        {
            _serverActive = NetworkServer.active;
            typeof(NetworkServer).GetProperty("active").SetValue(null, true);
            GameDB.ResetForTests();
            GameDB.Initialize(GameDBTestFixtures.CreateValidRoot(_created));
            var runtimeObject = new GameObject("CombatExperienceRuntimeTest");
            _created.Add(runtimeObject);
            _runtime = runtimeObject.AddComponent<CombatServerRuntime>();
            _runtime.Initialize(GameDB.Player, 30, 1, new OpenRoster());

            var playerObject = new GameObject("ExperiencePlayer", typeof(NetworkIdentity));
            _created.Add(playerObject);
            PlayerInputState input = playerObject.AddComponent<PlayerInputState>();
            _combat = playerObject.GetComponent<PlayerCombatState>();
            NetworkIdentity identity = playerObject.GetComponent<NetworkIdentity>();
            PropertyInfo netIdentity = typeof(NetworkBehaviour).GetProperty("netIdentity");
            netIdentity.SetValue(input, identity);
            netIdentity.SetValue(_combat, identity);
            var loadout = new CharacterCombatLoadout((int)EPlayerClass.Warrior,
                new CombatEquipmentEntry[Constants.Combat.EQUIPMENT_SLOT_COUNT],
                new int[Constants.Combat.TRAINING_STAT_COUNT], new CombatTalismanEntry[0], GameDB.ContentVersion);
            CharacterStats stats = CharacterStatsRules.Base(GameDB.Player, GameDB.CharacterLevel.ToSettings(), 2850);
            var targets = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var hits = new HitLedger(Constants.Combat.HIT_LEDGER_CAPACITY,
                Constants.Combat.HIT_LEDGER_RETENTION_SECONDS, Constants.Combat.LONGEST_ACTION_SECONDS);
            var resolver = new DamageResolver(targets, hits, GameDB.Player.DefenseReductionConstant, GameDB.Player.DamageReductionCap);
            _player = new PlayerServer(input, _combat, EPlayerClass.Warrior, 1, 1, 1, GameDB.Player,
                in stats, resolver, targets, new CombatLineOfSight(0, 1), 0);
            ((List<PlayerServer>)Field("_players").GetValue(_runtime)).Add(_player);
            var binding = new SessionBinding("participant", 1, null, EPlayerClass.Warrior);
            typeof(SessionBinding).GetProperty("CombatLoadout").SetValue(binding, loadout);
            typeof(SessionBinding).GetProperty("Experience").SetValue(binding,
                new CharacterExperienceSnapshot(2850, "session", 0));
            Method("BindExperience").Invoke(_runtime, new object[] { binding });
            Method("ApplyExperience").Invoke(_runtime, new object[] { _player });
            _sink = new RecordingSink();
            _queue = new SessionEventQueue("session", _sink, 8, 8);
            Field("_experienceQueue").SetValue(_runtime, _queue);
            _queue.Committed += (Action<SessionEvent>)Delegate.CreateDelegate(typeof(Action<SessionEvent>),
                _runtime, Method("OnExperienceCommitted"));
        }

        [TearDown]
        public void TearDown()
        {
            _player?.Shutdown();
            GameDB.ResetForTests();
            for (int i = _created.Count - 1; i >= 0; --i)
            {
                Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
            typeof(NetworkServer).GetProperty("active").SetValue(null, _serverActive);
        }

        [Test]
        public void CommittedExperience_ChangesStatsOnlyOnNextRuntimeTick_WithoutHealing()
        {
            _player.ApplyDamage(37, false, 0);
            float health = _player.Health;
            float maximum = _player.MaxHealth;
            float attack = _player.AttackPower;
            Commit(1400);
            Assert.AreEqual(health, _player.Health);
            Assert.AreEqual(maximum, _player.MaxHealth);
            Assert.AreEqual(attack, _player.AttackPower);
            Assert.AreEqual(2850, _combat.TotalExperience);

            Method("Step").Invoke(_runtime, new object[] { 1.0, 0.05f });

            Assert.AreEqual(maximum + 15, _player.MaxHealth);
            Assert.AreEqual(attack + 1, _player.AttackPower);
            Assert.AreEqual(health, _player.Health);
            Assert.AreEqual(4250, _combat.TotalExperience);
        }

        [Test]
        public void CommittedExperience_WithinSameLevel_DoesNotRecomputeStats()
        {
            float attack = _player.AttackPower;
            float maximum = _player.MaxHealth;
            // 재계산하면 실패할 의존성을 제거해 같은 레벨에서는 계산을 건너뛰는지 검증한다.
            Field("_equipment").SetValue(_runtime, null);
            Commit(5);
            Method("Step").Invoke(_runtime, new object[] { 1.0, 0.05f });
            Assert.AreEqual(2855, _combat.TotalExperience);
            Assert.AreEqual(attack, _player.AttackPower);
            Assert.AreEqual(maximum, _player.MaxHealth);
        }

        private void Commit(long amount)
        {
            Assert.IsTrue(_queue.TryEnqueue(new SessionEventData(ESessionEventType.ExperienceGranted, 0,
                participantId: "participant", amount: amount), out _));
            _queue.RequestImmediateSend();
            _queue.Tick(0);
            _sink.Commit();
        }

        private static FieldInfo Field(string name) => typeof(CombatServerRuntime).GetField(name, PRIVATE);
        private static MethodInfo Method(string name) => typeof(CombatServerRuntime).GetMethod(name, PRIVATE);

        private sealed class OpenRoster : IParticipantRoster
        {
            public bool IsClosed(int participantId) => false;
            public void Close(int participantId) { }
        }

        private sealed class RecordingSink : IBackendEventSink
        {
            private long _attempt;
            private IBackendEventSinkListener _listener;
            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
                IBackendEventSinkListener listener)
            {
                _attempt = attemptId;
                _listener = listener;
                return true;
            }
            public void Commit() => _listener.OnBatchCompleted(_attempt, EEventBatchResult.Committed);
        }
    }
}
