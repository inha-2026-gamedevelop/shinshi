// System
using System.Collections.Generic;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class PendingAuthTableTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    CAPACITY             = 2;
        private const int    CONNECTION_A         = 11;
        private const int    CONNECTION_B         = 12;
        private const int    CONNECTION_C         = 13;
        private const int    REQUEST_ID           = 5;
        private const double START_TIME           = 100.0;
        private const double JUST_BEFORE          = 0.01;
        private const int    PER_ADDRESS          = 2;
        private const int    WIDE_CAPACITY        = 8;
        private const string ADDRESS_A            = "203.0.113.10";
        private const string ADDRESS_B            = "203.0.113.11";
        private const string MAPPED_ADDRESS_A     = "::ffff:203.0.113.10";
        private const string IPV6_PREFIX_A_HOST_1 = "2001:db8:1:2::1";
        private const string IPV6_PREFIX_A_HOST_2 = "2001:db8:1:2:aaaa:bbbb:cccc:dddd";
        private const string IPV6_PREFIX_A_HOST_3 = "2001:db8:1:2::ffff";
        private const string IPV6_PREFIX_B_HOST_1 = "2001:db8:1:3::1";
        private const string ZERO_PREFIX_IPV6     = "::cb00:710a";
        private const int    DUNGEON_PER_ADDRESS  = 2;

        private static readonly double _deadline = START_TIME + Constants.Network.AUTH_TIMEOUT_SECONDS;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void CollectDue_WaitingPastTimeout_CollectsAsTimeout()
        {
            var table   = new PendingAuthTable(CAPACITY, PER_ADDRESS);
            var results = new List<PendingAuthEntry>();
            table.TryAdd(CONNECTION_A, ADDRESS_A, _deadline);

            Assert.AreEqual(0, table.CollectDue(_deadline - JUST_BEFORE, results));
            Assert.AreEqual(1, table.CollectDue(_deadline, results));
            Assert.AreEqual(CONNECTION_A, results[0].ConnectionId);
            Assert.AreEqual(EPendingAuthStage.Waiting, results[0].Stage);
            Assert.AreEqual(0, table.Count);
        }

        [Test]
        public void CollectDue_VerifyingPastTimeout_CollectsAndDropsLateResult()
        {
            var table   = new PendingAuthTable(CAPACITY, PER_ADDRESS);
            var results = new List<PendingAuthEntry>();
            table.TryAdd(CONNECTION_A, ADDRESS_A, _deadline);
            Assert.IsTrue(table.TryBeginVerify(CONNECTION_A, REQUEST_ID, _deadline));

            table.CollectDue(_deadline, results);

            Assert.AreEqual(EPendingAuthStage.Verifying, results[0].Stage);
            Assert.IsFalse(table.TryFindVerifying(REQUEST_ID, out _));
        }

        [Test]
        public void TryBeginVerify_SecondRequest_IsRejected()
        {
            var table = new PendingAuthTable(CAPACITY, PER_ADDRESS);
            table.TryAdd(CONNECTION_A, ADDRESS_A, _deadline);

            Assert.IsTrue(table.TryBeginVerify(CONNECTION_A, REQUEST_ID, _deadline));
            Assert.IsFalse(table.IsWaiting(CONNECTION_A));
            Assert.IsFalse(table.TryBeginVerify(CONNECTION_A, REQUEST_ID + 1, _deadline));
        }

        [Test]
        public void TryAdd_AtCapacity_ReturnsFalse()
        {
            var table = new PendingAuthTable(CAPACITY, PER_ADDRESS);

            Assert.IsTrue(table.TryAdd(CONNECTION_A, ADDRESS_A, _deadline));
            Assert.IsTrue(table.TryAdd(CONNECTION_B, ADDRESS_B, _deadline));
            Assert.IsFalse(table.TryAdd(CONNECTION_C, ADDRESS_B, _deadline));
            Assert.IsFalse(table.TryAdd(CONNECTION_A, ADDRESS_A, _deadline));
        }

        [Test]
        public void TryAdd_SameAddressAtLimit_RejectsOnlyThatAddress()
        {
            var table = new PendingAuthTable(WIDE_CAPACITY, PER_ADDRESS);

            Assert.IsTrue(table.TryAdd(1, ADDRESS_A, _deadline));
            Assert.IsTrue(table.TryAdd(2, ADDRESS_A, _deadline));
            Assert.IsFalse(table.TryAdd(3, ADDRESS_A, _deadline), "한 주소가 대기 자리를 모두 차지하지 못한다");
            Assert.IsTrue(table.TryAdd(4, ADDRESS_B, _deadline));
            Assert.AreEqual(PER_ADDRESS, table.CountByAddress(ADDRESS_A));

            table.Remove(1);
            Assert.IsTrue(table.TryAdd(5, ADDRESS_A, _deadline), "빠진 자리만큼 다시 받는다");
        }

        [Test]
        public void TryBeginVerify_ShortRequestDeadline_ExtendsToVerifyDeadline()
        {
            var    table           = new PendingAuthTable(CAPACITY, PER_ADDRESS);
            var    results         = new List<PendingAuthEntry>();
            double requestDeadline = START_TIME + Constants.Network.AUTH_REQUEST_TIMEOUT_SECONDS;
            double verifyDeadline  = START_TIME + Constants.Network.AUTH_TIMEOUT_SECONDS;
            table.TryAdd(CONNECTION_A, ADDRESS_A, requestDeadline);

            Assert.IsTrue(table.TryBeginVerify(CONNECTION_A, REQUEST_ID, verifyDeadline));
            Assert.AreEqual(0, table.CollectDue(requestDeadline, results), "요청을 보낸 연결은 요청 대기 마감에 끊지 않는다");
            Assert.AreEqual(1, table.CollectDue(verifyDeadline, results));
            Assert.AreEqual(EPendingAuthStage.Verifying, results[0].Stage);
        }

        [Test]
        public void CollectDue_SilentConnectionAfterRequestTimeout_CollectsAsWaiting()
        {
            var    table           = new PendingAuthTable(CAPACITY, PER_ADDRESS);
            var    results         = new List<PendingAuthEntry>();
            double requestDeadline = START_TIME + Constants.Network.AUTH_REQUEST_TIMEOUT_SECONDS;
            table.TryAdd(CONNECTION_A, ADDRESS_A, requestDeadline);

            Assert.AreEqual(1, table.CollectDue(requestDeadline, results));
            Assert.AreEqual(EPendingAuthStage.Waiting, results[0].Stage, "요청 없이 1초가 지나면 응답 없이 끊는 대상이다");
        }

        [Test]
        public void MaxPendingFor_Dungeon_IsPartySizeTimesMultiplier()
        {
            Assert.AreEqual(Constants.Session.MAX_PARTY_SIZE * Constants.Network.DUNGEON_PENDING_AUTH_PER_PARTICIPANT,
                ShinshiServerAuthenticator.MaxPendingFor(EServerRole.Dungeon));
            Assert.AreEqual(Constants.Network.MAX_PENDING_AUTH_CONNECTIONS, ShinshiServerAuthenticator.MaxPendingFor(EServerRole.Town));
        }

        // N9 회귀: IPv4-mapped IPv6는 같은 IPv4로 센다
        [Test]
        public void TryAdd_IPv4MappedIPv6_CountsAsSameIPv4()
        {
            var table = new PendingAuthTable(WIDE_CAPACITY, PER_ADDRESS);

            Assert.IsTrue(table.TryAdd(1, ADDRESS_A, _deadline));
            Assert.IsTrue(table.TryAdd(2, MAPPED_ADDRESS_A, _deadline));
            Assert.IsFalse(table.TryAdd(3, MAPPED_ADDRESS_A, _deadline), "표기만 바꿔 주소별 상한을 넘지 못한다");
            Assert.AreEqual(PER_ADDRESS, table.CountByAddress(ADDRESS_A));
        }

        // N9 회귀: 같은 /64 안에서 주소만 바꾼 IPv6는 한 묶음이고, 다른 /64는 따로 센다
        [Test]
        public void TryAdd_IPv6SamePrefix64_CountsTogether()
        {
            var table = new PendingAuthTable(WIDE_CAPACITY, PER_ADDRESS);

            Assert.IsTrue(table.TryAdd(1, IPV6_PREFIX_A_HOST_1, _deadline));
            Assert.IsTrue(table.TryAdd(2, IPV6_PREFIX_A_HOST_2, _deadline));
            Assert.IsFalse(table.TryAdd(3, IPV6_PREFIX_A_HOST_3, _deadline), "같은 /64는 한 주소로 센다");
            Assert.IsTrue(table.TryAdd(4, IPV6_PREFIX_B_HOST_1, _deadline), "다른 /64는 따로 센다");
        }

        [Test]
        public void FromAddress_MixedAddressFamilies_NormalizesKeys()
        {
            Assert.AreEqual(AuthAddressKey.FromAddress(ADDRESS_A), AuthAddressKey.FromAddress(MAPPED_ADDRESS_A));
            Assert.AreEqual(AuthAddressKey.FromAddress(IPV6_PREFIX_A_HOST_1), AuthAddressKey.FromAddress(IPV6_PREFIX_A_HOST_2));
            Assert.AreNotEqual(AuthAddressKey.FromAddress(IPV6_PREFIX_A_HOST_1), AuthAddressKey.FromAddress(IPV6_PREFIX_B_HOST_1));
            Assert.AreNotEqual(AuthAddressKey.FromAddress(ADDRESS_A), AuthAddressKey.FromAddress(ADDRESS_B));
            Assert.AreEqual(AuthAddressKey.FromAddress(null), AuthAddressKey.FromAddress("not-an-address"),
                "읽지 못한 주소는 한 묶음이다");
            Assert.AreNotEqual(AuthAddressKey.FromAddress(ZERO_PREFIX_IPV6), AuthAddressKey.FromAddress(ADDRESS_A),
                "IPv4와 IPv6 접두사 키가 겹치지 않는다");
        }

        [Test]
        public void MarkClosing_InsertedEntry_DoesNotCountTowardAnyAddress()
        {
            var table = new PendingAuthTable(WIDE_CAPACITY, PER_ADDRESS);

            Assert.IsTrue(table.MarkClosing(1, _deadline));
            Assert.IsTrue(table.MarkClosing(2, _deadline));
            Assert.IsTrue(table.MarkClosing(3, _deadline));

            Assert.AreEqual(0, table.CountByAddress((string)null));
            Assert.IsTrue(table.TryAdd(4, null, _deadline));
        }

        // N9 회귀: 던전은 주소별 대기 상한이 2다
        [Test]
        public void MaxPendingPerAddressFor_Dungeon_ReturnsTwo()
        {
            Assert.AreEqual(Constants.Network.DUNGEON_MAX_PENDING_AUTH_PER_ADDRESS,
                ShinshiServerAuthenticator.MaxPendingPerAddressFor(EServerRole.Dungeon));
            Assert.AreEqual(DUNGEON_PER_ADDRESS, Constants.Network.DUNGEON_MAX_PENDING_AUTH_PER_ADDRESS);
            Assert.AreEqual(Constants.Network.MAX_PENDING_AUTH_PER_ADDRESS,
                ShinshiServerAuthenticator.MaxPendingPerAddressFor(EServerRole.Town));
        }

        [Test]
        public void MarkClosing_WaitingEntry_MovesDeadlineAndStage()
        {
            var    table   = new PendingAuthTable(CAPACITY, PER_ADDRESS);
            var    results = new List<PendingAuthEntry>();
            double closeAt = START_TIME + Constants.Network.AUTH_REJECT_DISCONNECT_DELAY_SECONDS;
            table.TryAdd(CONNECTION_A, ADDRESS_A, _deadline);

            Assert.IsTrue(table.MarkClosing(CONNECTION_A, closeAt));
            table.CollectDue(closeAt, results);

            Assert.AreEqual(EPendingAuthStage.Closing, results[0].Stage);
            Assert.IsFalse(table.IsWaiting(CONNECTION_A));
        }
    }
}
