// System
using System;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class ServerLinksTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private GameObject _host;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            ServerLinks.Clear();
            _host = new GameObject("ServerLinksTestHost");
        }

        [TearDown]
        public void TearDown()
        {
            ServerLinks.Clear();
            Object.DestroyImmediate(_host);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Notify_RegisteredType_CallsMatchingHandler()
        {
            var state   = _host.AddComponent<BoxCollider>();
            int started = 0;
            int stopped = 0;

            ServerLinks.Register<BoxCollider>(_ => ++started, _ => ++stopped);

            ServerLinks.NotifyStarted(state);
            ServerLinks.NotifyStopped(state);

            Assert.AreEqual(1, started);
            Assert.AreEqual(1, stopped);
        }

        [Test]
        public void Notify_UnregisteredType_DoesNothing()
        {
            var state = _host.AddComponent<BoxCollider>();

            Assert.DoesNotThrow(() => ServerLinks.NotifyStarted(state));
            Assert.DoesNotThrow(() => ServerLinks.NotifyStarted(null));
        }

        [Test]
        public void Register_SameTypeTwice_Throws()
        {
            ServerLinks.Register<BoxCollider>(_ => { }, _ => { });

            Assert.Throws<InvalidOperationException>(() => ServerLinks.Register<BoxCollider>(_ => { }, _ => { }));
        }

        [Test]
        public void Notify_BaseTypeRegistered_DoesNotCallForDerivedType()
        {
            var state   = _host.AddComponent<BoxCollider>();
            int started = 0;

            ServerLinks.Register<Collider>(_ => ++started, _ => { });

            ServerLinks.NotifyStarted(state);

            Assert.AreEqual(0, started);
        }
    }
}
