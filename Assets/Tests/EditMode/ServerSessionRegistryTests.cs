// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class ServerSessionRegistryTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    CAPACITY = 2;
        private const string KEY_A    = "player-a";
        private const string KEY_B    = "player-b";
        private const string KEY_C    = "player-c";

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryAdmit_SameKeyWhileConnected_ReturnsAlreadyConnected()
        {
            var registry = new ServerSessionRegistry(CAPACITY);
            registry.TryAdmit(new JoinTokenClaims(KEY_A), out _);

            Assert.AreEqual(ERejectReason.AlreadyConnected, registry.TryAdmit(new JoinTokenClaims(KEY_A), out SessionBinding second));
            Assert.IsNull(second);
        }

        [Test]
        public void TryAdmit_AfterRelease_KeepsSameParticipantId()
        {
            var registry = new ServerSessionRegistry(CAPACITY);
            registry.TryAdmit(new JoinTokenClaims(KEY_A), out SessionBinding first);
            registry.Release(first);

            Assert.AreEqual(ERejectReason.None, registry.TryAdmit(new JoinTokenClaims(KEY_A), out SessionBinding second));
            Assert.AreEqual(first.ParticipantId, second.ParticipantId);
            Assert.IsFalse(first.IsActive);
            Assert.IsTrue(second.IsActive);
        }

        [Test]
        public void TryAdmit_ClosedParticipant_ReturnsAlreadySettled()
        {
            var registry = new ServerSessionRegistry(CAPACITY);
            registry.TryAdmit(new JoinTokenClaims(KEY_A), out SessionBinding first);
            registry.Release(first);
            registry.Close(first.ParticipantId);

            Assert.AreEqual(ERejectReason.AlreadySettled, registry.TryAdmit(new JoinTokenClaims(KEY_A), out _));
        }

        [Test]
        public void Forget_TownDisconnects_FreesCapacityForNewKeys()
        {
            var registry = new ServerSessionRegistry(CAPACITY);
            registry.TryAdmit(new JoinTokenClaims(KEY_A), out SessionBinding first);
            registry.TryAdmit(new JoinTokenClaims(KEY_B), out SessionBinding second);
            registry.Forget(first);
            registry.Forget(second);

            Assert.AreEqual(0, registry.ParticipantCount, "마을은 끊긴 키를 쌓지 않는다");
            Assert.AreEqual(ERejectReason.None, registry.TryAdmit(new JoinTokenClaims(KEY_C), out _));
            Assert.AreEqual(ERejectReason.None, registry.TryAdmit(new JoinTokenClaims(KEY_A), out SessionBinding again));
            Assert.AreNotEqual(first.ParticipantId, again.ParticipantId, "다시 들어오면 새 번호를 받는다");
            Assert.IsFalse(registry.IsClosed(again.ParticipantId));
        }

        [Test]
        public void Forget_StaleBindingAfterNewAdmission_KeepsNewMapping()
        {
            var registry = new ServerSessionRegistry(CAPACITY);
            registry.TryAdmit(new JoinTokenClaims(KEY_A), out SessionBinding first);
            registry.Forget(first);
            registry.TryAdmit(new JoinTokenClaims(KEY_A), out SessionBinding second);

            registry.Forget(first);

            Assert.IsTrue(registry.IsConnected(second.ParticipantId), "늦게 온 이전 연결의 정리가 새 연결을 지우지 않는다");
            Assert.AreEqual(ERejectReason.AlreadyConnected, registry.TryAdmit(new JoinTokenClaims(KEY_A), out _));
        }

        [Test]
        public void TryAdmit_NewKeyOverCapacity_ReturnsNoCapacity()
        {
            var registry = new ServerSessionRegistry(CAPACITY);
            registry.TryAdmit(new JoinTokenClaims(KEY_A), out _);
            registry.TryAdmit(new JoinTokenClaims(KEY_B), out _);

            Assert.AreEqual(ERejectReason.NoCapacity, registry.TryAdmit(new JoinTokenClaims(KEY_C), out _));
        }

        [Test]
        public void TryAdmit_DistinctKeys_GetDistinctNonZeroIds()
        {
            var registry = new ServerSessionRegistry(CAPACITY);
            registry.TryAdmit(new JoinTokenClaims(KEY_A), out SessionBinding a);
            registry.TryAdmit(new JoinTokenClaims(KEY_B), out SessionBinding b);

            Assert.AreNotEqual(0, a.ParticipantId);
            Assert.AreNotEqual(a.ParticipantId, b.ParticipantId);
        }

        [Test]
        public void TryAdmit_EmptyKey_ReturnsTokenRevoked()
        {
            var registry = new ServerSessionRegistry(CAPACITY);

            Assert.AreEqual(ERejectReason.TokenRevoked, registry.TryAdmit(new JoinTokenClaims(string.Empty), out _));
        }
    }
}
