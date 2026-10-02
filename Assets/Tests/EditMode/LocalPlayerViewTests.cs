// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class LocalPlayerViewTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float TOLERANCE = 0.0001f;

        private readonly List<GameObject> _objects = new List<GameObject>();

        private LocalPlayerView     _view;
        private int                 _callCount;
        private LocalPlayerSnapshot _last;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _view      = new LocalPlayerView();
            _callCount = 0;
            _last      = LocalPlayerSnapshot.Unbound;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _objects.Count; ++i)
            {
                Object.DestroyImmediate(_objects[i]);
            }

            _objects.Clear();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Snapshot_BeforeAttach_IsUnbound()
        {
            Assert.IsFalse(_view.Snapshot.IsBound);
            Assert.AreEqual(LocalPlayerSnapshot.UNBOUND_VERSION, _view.Snapshot.BindingVersion);
            Assert.AreEqual(0f, _view.ReadGauges().MaxHealth);
        }

        [Test]
        public void Subscribe_CallsHandlerOnceImmediatelyWithCurrentSnapshot()
        {
            PlayerCombatState combat = CreateCombat(EPlayerClass.Sorcerer);
            _view.Attach(combat);

            _view.SubscribeSnapshot(OnSnapshot);

            Assert.AreEqual(1, _callCount);
            Assert.IsTrue(_last.IsBound);
            Assert.AreEqual(EPlayerClass.Sorcerer, _last.PlayerClass);
        }

        [Test]
        public void Subscribe_SameHandlerTwice_IsCalledOncePerChange()
        {
            PlayerCombatState combat = CreateCombat(EPlayerClass.Warrior);
            _view.SubscribeSnapshot(OnSnapshot);
            _view.SubscribeSnapshot(OnSnapshot);
            Assert.AreEqual(1, _callCount);

            _view.Attach(combat);
            Assert.AreEqual(2, _callCount);
        }

        [Test]
        public void Attach_RaisesBindingVersionAndDetachUnbinds()
        {
            PlayerCombatState first  = CreateCombat(EPlayerClass.Warrior);
            PlayerCombatState second = CreateCombat(EPlayerClass.Summoner);

            _view.Attach(first);
            int firstVersion = _view.Snapshot.BindingVersion;
            Assert.AreEqual(1, firstVersion);

            _view.Attach(second);
            Assert.AreEqual(firstVersion + 1, _view.Snapshot.BindingVersion);
            Assert.AreEqual(EPlayerClass.Summoner, _view.Snapshot.PlayerClass);

            // 이미 바뀐 이전 캐릭터의 해제는 새 캐릭터를 풀지 않는다
            _view.Detach(first);
            Assert.IsTrue(_view.Snapshot.IsBound);

            _view.Detach(second);
            Assert.IsFalse(_view.Snapshot.IsBound);
        }

        [Test]
        public void Attach_SameCombatTwice_DoesNotRaiseVersion()
        {
            PlayerCombatState combat = CreateCombat(EPlayerClass.Warrior);
            _view.Attach(combat);
            _view.Attach(combat);
            Assert.AreEqual(1, _view.Snapshot.BindingVersion);
        }

        [Test]
        public void ClassChange_NotifiesSubscriberWithNewClass()
        {
            PlayerCombatState combat = CreateCombat(EPlayerClass.Warrior);
            _view.Attach(combat);
            _view.SubscribeSnapshot(OnSnapshot);
            int before = _callCount;

            SetField(combat, "_playerClass", EPlayerClass.Summoner);
            typeof(PlayerCombatState).GetMethod("OnPlayerClassChanged", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(combat, new object[] { EPlayerClass.Warrior, EPlayerClass.Summoner });

            Assert.AreEqual(before + 1, _callCount);
            Assert.AreEqual(EPlayerClass.Summoner, _last.PlayerClass);
        }

        [Test]
        public void LifeStateChange_NotifiesSubscriber()
        {
            PlayerCombatState combat = CreateCombat(EPlayerClass.Warrior);
            _view.Attach(combat);
            _view.SubscribeSnapshot(OnSnapshot);
            int before = _callCount;

            SetField(combat, "_lifeState", EParticipantLifeState.Dead);
            typeof(PlayerCombatState).GetMethod("OnLifeStateChanged", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(combat, new object[] { EParticipantLifeState.Alive, EParticipantLifeState.Dead });

            Assert.AreEqual(before + 1, _callCount);
            Assert.AreEqual(EParticipantLifeState.Dead, _last.LifeState);
        }

        [Test]
        public void Unsubscribe_StopsNotifications_AndUnknownHandlerIsHarmless()
        {
            PlayerCombatState combat = CreateCombat(EPlayerClass.Warrior);
            _view.SubscribeSnapshot(OnSnapshot);
            _view.UnsubscribeSnapshot(OnSnapshot);
            _view.UnsubscribeSnapshot(OnSnapshot);
            _view.UnsubscribeSnapshot(null);
            int before = _callCount;

            _view.Attach(combat);
            Assert.AreEqual(before, _callCount);
        }

        [Test]
        public void Handler_UnsubscribingItselfDuringNotify_DoesNotThrow()
        {
            PlayerCombatState combat = CreateCombat(EPlayerClass.Warrior);
            _view.SubscribeSnapshot(OnSnapshotThenUnsubscribe);
            int before = _callCount;

            Assert.DoesNotThrow(() => _view.Attach(combat));
            Assert.AreEqual(before + 1, _callCount);

            _view.Detach(combat);
            Assert.AreEqual(before + 1, _callCount);
        }

        [Test]
        public void ReadGauges_ReflectsReplicatedValues_AndHidesResonanceForNonSummoner()
        {
            PlayerCombatState combat = CreateCombat(EPlayerClass.Warrior);
            SetField(combat, "_health", 40f);
            SetField(combat, "_maxHealth", 80f);
            SetField(combat, "_energy", 10f);
            SetField(combat, "_maxEnergy", 50f);
            SetField(combat, "_resonance", 30f);
            _view.Attach(combat);

            LocalPlayerGauges gauges = _view.ReadGauges();
            Assert.AreEqual(0.5f, gauges.HealthRatio, TOLERANCE);
            Assert.AreEqual(0.2f, gauges.EnergyRatio, TOLERANCE);
            Assert.IsFalse(gauges.HasResonance);
        }

        [Test]
        public void ReadGauges_Summoner_ShowsResonance()
        {
            PlayerCombatState combat = CreateCombat(EPlayerClass.Summoner);
            SetField(combat, "_resonance", 30f);
            _view.Attach(combat);

            LocalPlayerGauges gauges = _view.ReadGauges();
            Assert.IsTrue(gauges.HasResonance);
            Assert.AreEqual(30f, gauges.Resonance, TOLERANCE);
        }

        [Test]
        public void Snapshot_AfterCombatDestroyed_IsUnbound()
        {
            PlayerCombatState combat = CreateCombat(EPlayerClass.Warrior);
            _view.Attach(combat);

            Object.DestroyImmediate(combat.gameObject);

            Assert.IsFalse(_view.Snapshot.IsBound);
            Assert.AreEqual(0f, _view.ReadGauges().MaxHealth);
        }

        [Test]
        public void SkillRejected_IsForwardedToSubscribers()
        {
            ESkillRequestKind seenKind   = ESkillRequestKind.Regular;
            byte              seenSlot   = 0;
            ERejectReason     seenReason = ERejectReason.None;
            _view.SkillRejected += (kind, slot, reason) =>
            {
                seenKind   = kind;
                seenSlot   = slot;
                seenReason = reason;
            };

            _view.RaiseSkillRejected(ESkillRequestKind.Ascension, 2, ERejectReason.Cooldown);

            Assert.AreEqual(ESkillRequestKind.Ascension, seenKind);
            Assert.AreEqual(2, seenSlot);
            Assert.AreEqual(ERejectReason.Cooldown, seenReason);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void OnSnapshot(LocalPlayerSnapshot snapshot)
        {
            ++_callCount;
            _last = snapshot;
        }

        private void OnSnapshotThenUnsubscribe(LocalPlayerSnapshot snapshot)
        {
            ++_callCount;
            if (snapshot.IsBound)
            {
                _view.UnsubscribeSnapshot(OnSnapshotThenUnsubscribe);
            }
        }

        private PlayerCombatState CreateCombat(EPlayerClass playerClass)
        {
            var owner = new GameObject("TestLocalPlayer", typeof(NetworkIdentity));
            _objects.Add(owner);
            PlayerCombatState combat = owner.AddComponent<PlayerCombatState>();
            typeof(NetworkBehaviour).GetProperty("netIdentity", BindingFlags.Instance | BindingFlags.Public)
                .SetValue(combat, owner.GetComponent<NetworkIdentity>());
            SetField(combat, "_playerClass", playerClass);
            return combat;
        }

        private static void SetField(PlayerCombatState combat, string name, object value)
        {
            FieldInfo field = typeof(PlayerCombatState).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(combat, value);
        }
    }
}
