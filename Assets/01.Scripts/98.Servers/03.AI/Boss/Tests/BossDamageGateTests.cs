// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class BossDamageGateTests
    {
        /****************************************
        *                Helpers
        ****************************************/

        private static DamageRequest MakeRequest()
        {
            return new DamageRequest(1, 10, 1, 1, 1f, EDamageFlags.None, Vector3.zero, "p1");
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryApply_BeforeOpen_DoesNotReachResolver()
        {
            FakeDamageResolver resolver = new FakeDamageResolver();
            BossDamageGate gate = new BossDamageGate(resolver);
            DamageRequest request = MakeRequest();

            Assert.IsFalse(gate.TryApply(in request, out DamageResult _));
            Assert.AreEqual(0, resolver.Requests.Count);
        }

        [Test]
        public void TryApply_WhenOpen_PassesRequestAndResultThrough()
        {
            FakeDamageResolver resolver = new FakeDamageResolver();
            BossDamageGate gate = new BossDamageGate(resolver);
            gate.Open();
            DamageRequest request = MakeRequest();

            Assert.IsTrue(gate.TryApply(in request, out DamageResult result));
            Assert.AreEqual(1, resolver.Requests.Count);
            Assert.IsTrue(result.IsApplied);
        }

        [Test]
        public void Open_AfterCloseForGood_StaysClosed()
        {
            FakeDamageResolver resolver = new FakeDamageResolver();
            BossDamageGate gate = new BossDamageGate(resolver);
            gate.Open();
            gate.CloseForGood();

            gate.Open();
            DamageRequest request = MakeRequest();

            Assert.IsFalse(gate.IsOpen);
            Assert.IsFalse(gate.TryApply(in request, out DamageResult _));
            Assert.AreEqual(0, resolver.Requests.Count);
        }

        [Test]
        public void IssueInstanceId_WhenClosed_StillIssues()
        {
            FakeDamageResolver resolver = new FakeDamageResolver();
            BossDamageGate gate = new BossDamageGate(resolver);

            Assert.AreNotEqual(CombatIds.NONE, gate.IssueInstanceId());
        }
    }
}
