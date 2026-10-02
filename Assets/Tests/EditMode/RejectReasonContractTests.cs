// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Tests
{
    /// <summary> 접속 거절 코드가 Contracts 상수와 같은 값인지 확인한다 </summary>
    public sealed class RejectReasonContractTests
    {
        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void ConnectionCodes_SharedRejectReasons_MatchContractConstants()
        {
            Assert.AreEqual(ContractCodes.RejectReason.ALREADY_CONNECTED, (int)ERejectReason.AlreadyConnected);
            Assert.AreEqual(ContractCodes.RejectReason.BACKEND_UNAVAILABLE, (int)ERejectReason.BackendUnavailable);
            Assert.AreEqual(ContractCodes.RejectReason.RATE_LIMITED, (int)ERejectReason.RateLimited);
            Assert.AreEqual(ContractCodes.RejectReason.SETTLEMENT_LIMIT_EXCEEDED, (int)ERejectReason.SettlementLimitExceeded);
        }
    }
}
