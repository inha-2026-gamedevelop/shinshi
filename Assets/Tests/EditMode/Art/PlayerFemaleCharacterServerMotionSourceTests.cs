// System
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class PlayerFemaleCharacterServerMotionSourceTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float SAMPLE_SPEED = 3.25f;

        private readonly List<GameObject> _objects = new List<GameObject>();

        /****************************************
        *              Unity Event
        ****************************************/

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
        public void Configure_NullCombat_FailsBinding()
        {
            PlayerFemaleCharacterServerMotionSource source = CreateSource();

            Assert.IsFalse(source.Configure(null));
            Assert.IsFalse(source.IsBound);
            Assert.IsNotEmpty(source.ValidationError);
        }

        [Test]
        public void Configure_ValidCombat_BindsAndExposesMovementRoot()
        {
            PlayerCombatState combat = CreateCombat(isClient: true, isOwned: false, isServer: false, EParticipantLifeState.Alive);
            PlayerFemaleCharacterServerMotionSource source = CreateSource();

            Assert.IsTrue(source.Configure(combat), source.ValidationError);
            Assert.IsTrue(source.IsBound);
            Assert.AreSame(combat.transform, source.MovementRoot);
        }

        [Test]
        public void TryRead_RemoteAliveClient_ReturnsTrueWithPublishedSpeed()
        {
            PlayerCombatState combat = CreateCombat(isClient: true, isOwned: false, isServer: false, EParticipantLifeState.Alive);
            SetField(combat, "_locomotionSpeed", SAMPLE_SPEED);
            PlayerFemaleCharacterServerMotionSource source = CreateSource();
            Assert.IsTrue(source.Configure(combat));

            bool available = source.TryRead(0.0, out float speed);

            Assert.IsTrue(available);
            Assert.AreEqual(SAMPLE_SPEED, speed);
        }

        [Test]
        public void TryRead_Owner_ReturnsFalseAndZero()
        {
            PlayerCombatState combat = CreateCombat(isClient: true, isOwned: true, isServer: false, EParticipantLifeState.Alive);
            SetField(combat, "_locomotionSpeed", SAMPLE_SPEED);
            PlayerFemaleCharacterServerMotionSource source = CreateSource();
            Assert.IsTrue(source.Configure(combat));

            bool available = source.TryRead(0.0, out float speed);

            Assert.IsFalse(available);
            Assert.AreEqual(0f, speed);
        }

        [Test]
        public void TryRead_Server_ReturnsFalseAndZero()
        {
            PlayerCombatState combat = CreateCombat(isClient: true, isOwned: false, isServer: true, EParticipantLifeState.Alive);
            SetField(combat, "_locomotionSpeed", SAMPLE_SPEED);
            PlayerFemaleCharacterServerMotionSource source = CreateSource();
            Assert.IsTrue(source.Configure(combat));

            bool available = source.TryRead(0.0, out float speed);

            Assert.IsFalse(available);
            Assert.AreEqual(0f, speed);
        }

        [Test]
        public void TryRead_DeadRemote_ReturnsFalseAndZero()
        {
            PlayerCombatState combat = CreateCombat(isClient: true, isOwned: false, isServer: false, EParticipantLifeState.Dead);
            SetField(combat, "_locomotionSpeed", SAMPLE_SPEED);
            PlayerFemaleCharacterServerMotionSource source = CreateSource();
            Assert.IsTrue(source.Configure(combat));

            bool available = source.TryRead(0.0, out float speed);

            Assert.IsFalse(available);
            Assert.AreEqual(0f, speed);
        }

        [Test]
        public void TryRead_NotYetBound_ReturnsFalseAndZero()
        {
            PlayerFemaleCharacterServerMotionSource source = CreateSource();

            bool available = source.TryRead(0.0, out float speed);

            Assert.IsFalse(available);
            Assert.AreEqual(0f, speed);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private PlayerFemaleCharacterServerMotionSource CreateSource()
        {
            var owner = new GameObject("TestServerMotionSource");
            _objects.Add(owner);
            return owner.AddComponent<PlayerFemaleCharacterServerMotionSource>();
        }

        private PlayerCombatState CreateCombat(bool isClient, bool isOwned, bool isServer, EParticipantLifeState lifeState)
        {
            var owner = new GameObject("TestCombat", typeof(NetworkIdentity));
            _objects.Add(owner);
            NetworkIdentity identity = owner.GetComponent<NetworkIdentity>();
            PlayerCombatState combat = owner.AddComponent<PlayerCombatState>();
            typeof(NetworkBehaviour).GetProperty("netIdentity", BindingFlags.Instance | BindingFlags.Public)
                .SetValue(combat, identity);

            SetIdentityFlag(identity, "isClient", isClient);
            SetIdentityFlag(identity, "isOwned", isOwned);
            SetIdentityFlag(identity, "isServer", isServer);
            SetField(combat, "_lifeState", lifeState);
            return combat;
        }

        private static void SetIdentityFlag(NetworkIdentity identity, string propertyName, bool value)
        {
            PropertyInfo property = typeof(NetworkIdentity).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(property, propertyName);
            property.SetValue(identity, value);
        }

        private static void SetField(PlayerCombatState combat, string name, object value)
        {
            FieldInfo field = typeof(PlayerCombatState).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(combat, value);
        }
    }
}
