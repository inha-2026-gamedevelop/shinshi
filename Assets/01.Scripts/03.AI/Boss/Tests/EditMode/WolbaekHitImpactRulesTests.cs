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

        // 복제는 yaw 하나로 줄이므로 되돌린 방향이 원래 수평 방향과 거의 같아야 한다
        [Test]
        public void EncodeDecode_HorizontalDirection_RoundTripsClosely()
        {
            Vector3 direction = new Vector3(-0.28f, 0f, 0.96f);

            Assert.IsTrue(WolbaekHitImpactRules.TryEncodeDirection(direction, out ushort yaw));
            Vector3 decoded = WolbaekHitImpactRules.DecodeDirection(true, yaw);

            Assert.That(Vector3.Angle(direction, decoded), Is.LessThan(0.1f));
            Assert.AreEqual(0f, decoded.y, 0.0001f);
        }

        [Test]
        public void EncodeDecode_ZeroOrVerticalDirection_StaysWithoutDirection()
        {
            Assert.IsFalse(WolbaekHitImpactRules.TryEncodeDirection(Vector3.zero, out ushort zeroYaw));
            Assert.IsFalse(WolbaekHitImpactRules.TryEncodeDirection(Vector3.up, out _));
            Assert.AreEqual(Vector3.zero, WolbaekHitImpactRules.DecodeDirection(false, zeroYaw));
        }
    }
}
