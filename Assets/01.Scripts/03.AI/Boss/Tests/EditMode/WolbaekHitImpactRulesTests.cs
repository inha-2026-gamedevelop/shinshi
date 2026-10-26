// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public sealed class WolbaekHitImpactRulesTests
    {
        [Test]
        public void ResolveRotation_ZeroDirection_ReturnsIdentity()
        {
            Assert.AreEqual(Quaternion.identity, WolbaekHitImpactRules.ResolveRotation(Vector3.zero));
        }

        [Test]
        public void ResolveRotation_OnlyVertical_ReturnsIdentity()
        {
            Assert.AreEqual(Quaternion.identity, WolbaekHitImpactRules.ResolveRotation(Vector3.up));
        }

        [Test]
        public void ResolveRotation_AttackAlongX_FacesBackToAttacker()
        {
            Vector3 forward = WolbaekHitImpactRules.ResolveRotation(Vector3.right) * Vector3.forward;

            Assert.AreEqual(-1f, forward.x, 0.001f);
            Assert.AreEqual(0f, forward.z, 0.001f);
        }

        [Test]
        public void IsFresh_WithinMaxAge_ReturnsTrue()
        {
            Assert.IsTrue(WolbaekHitImpactRules.IsFresh(10.0, 10.3, 0.5f));
        }

        [Test]
        public void IsFresh_PastMaxAge_ReturnsFalse()
        {
            Assert.IsFalse(WolbaekHitImpactRules.IsFresh(10.0, 11.0, 0.5f));
        }
    }
}
