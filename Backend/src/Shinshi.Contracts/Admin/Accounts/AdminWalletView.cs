// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정 지갑. 사용 가능한 입장권은 tickets - ticketsReserved다 </summary>
    [DataContract]
    public sealed class AdminWalletView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "currency")]           private long _currency;
        [DataMember(Name = "tickets")]            private long _tickets;
        [DataMember(Name = "ticketsReserved")]    private long _ticketsReserved;

        // 행동력은 기준 시각의 저장 값이다. 현재 값은 게임 규칙이 기준 시각부터 회복을 더해 계산한다
        [DataMember(Name = "actionValue")]        private long _actionValue;
        [DataMember(Name = "actionAnchorUnixMs")] private long _actionAnchorUnixMs;
        [DataMember(Name = "revision")]           private long _revision;

        /****************************************
        *              Properties
        ****************************************/

        public long Currency           => _currency;
        public long Tickets            => _tickets;
        public long TicketsReserved    => _ticketsReserved;
        public long ActionValue        => _actionValue;
        public long ActionAnchorUnixMs => _actionAnchorUnixMs;
        public long Revision           => _revision;

        /****************************************
        *                Methods
        ****************************************/

        public AdminWalletView(long currency, long tickets, long ticketsReserved, long actionValue,
            long actionAnchorUnixMs, long revision)
        {
            _currency           = currency;
            _tickets            = tickets;
            _ticketsReserved    = ticketsReserved;
            _actionValue        = actionValue;
            _actionAnchorUnixMs = actionAnchorUnixMs;
            _revision           = revision;
        }

        private AdminWalletView() { }
    }
}
