// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class ServerHardeningRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int STATUS_BAD_REQUEST  = 400;
        private const int STATUS_UNAUTHORIZED = 401;
        private const int STATUS_CONFLICT     = 409;

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(STATUS_BAD_REQUEST, 0, true)]
        [TestCase(STATUS_CONFLICT, ContractCodes.RejectReason.POTION_LIMIT, true)]
        [TestCase(STATUS_CONFLICT, ContractCodes.RejectReason.SESSION_MISMATCH, true)]
        [TestCase(STATUS_CONFLICT, ContractCodes.RejectReason.STALE_REVISION, true)]
        [TestCase(STATUS_CONFLICT, ContractCodes.RejectReason.WRONG_PHASE, false)]
        [TestCase(STATUS_UNAUTHORIZED, 0, false)]
        public void IsTerminalRejection_MixedRejections_OnlyBadRequestAndStoredReasons(int status, int reasonCode, bool expected)
        {
            var response = new BackendResponse(1, EBackendOutcome.Rejected, status, reasonCode, null, 1);
            Assert.AreEqual(expected, BackendFailureRules.IsTerminalRejection(in response));
        }

        [TestCase(EBackendOutcome.ServerError)]
        [TestCase(EBackendOutcome.Timeout)]
        [TestCase(EBackendOutcome.LeaseUnavailable)]
        public void IsTerminalRejection_TransientOutcomes_AreNeverTerminal(EBackendOutcome outcome)
        {
            var response = new BackendResponse(1, outcome, STATUS_BAD_REQUEST, ContractCodes.RejectReason.POTION_LIMIT, null, 1);
            Assert.IsFalse(BackendFailureRules.IsTerminalRejection(in response));
        }

        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        [TestCase(false, true, false)]
        [TestCase(false, false, false)]
        public void ClosesRosterOnExpiry_RoleAndStartCombinations_OnlyStartedDungeon(bool isDungeon, bool started, bool expected)
        {
            Assert.AreEqual(expected, RetentionRules.ClosesRosterOnExpiry(isDungeon, started),
                "마을과 시작 전 던전은 보존 만료로 명단을 닫지 않는다");
        }

        [TestCase("dungeon-01.example.com", true)]
        [TestCase("203.0.113.5", true)]
        [TestCase("bad host", false)]
        [TestCase("http://x", false)]
        public void TryResolve_PublicAddress_AcceptsHostOrIp(string raw, bool expected)
        {
            Assert.AreEqual(expected, PublicAddressRules.TryResolve(raw, false, out string address, out string error));
            Assert.AreEqual(expected, error == null);
            if (expected)
            {
                Assert.AreEqual(raw, address);
            }
        }

        [Test]
        public void TryResolve_MissingAddress_DefaultsOnlyInDevelopment()
        {
            Assert.IsTrue(PublicAddressRules.TryResolve(null, true, out string address, out _));
            Assert.AreEqual(Constants.Network.DEV_PUBLIC_ADDRESS, address);
            Assert.IsFalse(PublicAddressRules.TryResolve(null, false, out _, out string error), "출시 서버는 주소가 필요하다");
            Assert.IsNotNull(error);
        }
    }
}
