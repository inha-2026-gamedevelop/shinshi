// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class SortieDepartFlowTests
    {
        // 마을 -> 던전 이동 흐름의 순수 규칙: 입장권 형식 확인, 흐름 시작 조건

        /****************************************
        *                Fields
        ****************************************/

        private const string SESSION_ID  = "4a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string INSTANCE_ID = "5a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string ADDRESS     = "127.0.0.1";
        private const string HOST_NAME   = "dungeon-1.shinshi.local";
        private const string JOIN_TOKEN  = "token-a";
        private const int    PORT        = 7780;
        private const int    PORT_LIMIT  = 65536;
        private const long   EXPIRES_AT  = 1000;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void IsValidDungeonTicket_AddressPortAndToken_ReturnsTrue()
        {
            Assert.IsTrue(SortieDepartFlow.IsValidDungeonTicket(Ticket(SESSION_ID, ADDRESS, PORT, JOIN_TOKEN)));
            Assert.IsTrue(SortieDepartFlow.IsValidDungeonTicket(Ticket(SESSION_ID, HOST_NAME, PORT, JOIN_TOKEN)));
        }

        [Test]
        public void IsValidDungeonTicket_MissingTokenOrSession_ReturnsFalse()
        {
            Assert.IsFalse(SortieDepartFlow.IsValidDungeonTicket(Ticket(SESSION_ID, ADDRESS, PORT, null)));
            Assert.IsFalse(SortieDepartFlow.IsValidDungeonTicket(Ticket(SESSION_ID, ADDRESS, PORT, string.Empty)));
            Assert.IsFalse(SortieDepartFlow.IsValidDungeonTicket(Ticket(null, ADDRESS, PORT, JOIN_TOKEN)));
        }

        [Test]
        public void IsValidDungeonTicket_BadAddressOrPort_ReturnsFalse()
        {
            Assert.IsFalse(SortieDepartFlow.IsValidDungeonTicket(Ticket(SESSION_ID, null, PORT, JOIN_TOKEN)));
            Assert.IsFalse(SortieDepartFlow.IsValidDungeonTicket(Ticket(SESSION_ID, "not a host", PORT, JOIN_TOKEN)));
            Assert.IsFalse(SortieDepartFlow.IsValidDungeonTicket(Ticket(SESSION_ID, ADDRESS, 0, JOIN_TOKEN)));
            Assert.IsFalse(SortieDepartFlow.IsValidDungeonTicket(Ticket(SESSION_ID, ADDRESS, PORT_LIMIT, JOIN_TOKEN)));
        }

        [Test]
        public void IsValidDungeonTicket_DefaultMessage_ReturnsFalse()
        {
            Assert.IsFalse(SortieDepartFlow.IsValidDungeonTicket(default(SortieTicketMessage)));
        }

        [Test]
        public void CanBegin_ConnectedAndIdle_ReturnsTrue()
        {
            Assert.IsTrue(SortieDepartFlow.CanBegin(false, false, true));
        }

        [Test]
        public void CanBegin_RunningTransferringOrDisconnected_ReturnsFalse()
        {
            Assert.IsFalse(SortieDepartFlow.CanBegin(true, false, true));
            Assert.IsFalse(SortieDepartFlow.CanBegin(false, true, true));
            Assert.IsFalse(SortieDepartFlow.CanBegin(false, false, false));
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static SortieTicketMessage Ticket(string sessionId, string address, int port, string joinToken)
        {
            return new SortieTicketMessage(sessionId, INSTANCE_ID, address, port, joinToken, EXPIRES_AT);
        }
    }
}
