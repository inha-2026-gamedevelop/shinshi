// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts.Admin
{
    /// <summary> 계정 지갑의 저장값과 예약량. actionValue는 회복 기준 시각의 값 </summary>
    [DataContract]
    public sealed class AdminCharacterWalletView
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "currency")]           private long _currency;
        [DataMember(Name = "currencyReserved")]   private long _currencyReserved;
        [DataMember(Name = "tickets")]            private long _tickets;
        [DataMember(Name = "ticketsReserved")]    private long _ticketsReserved;
        [DataMember(Name = "actionValue")]        private long _actionValue;
        [DataMember(Name = "actionAnchorUnixMs")] private long _actionAnchorUnixMs;
        [DataMember(Name = "revision")]           private long _revision;

        /****************************************
        *              Properties
        ****************************************/

        public long Currency           => _currency;
        public long CurrencyReserved   => _currencyReserved;
        public long Tickets            => _tickets;
        public long TicketsReserved    => _ticketsReserved;
        public long ActionValue        => _actionValue;
        public long ActionAnchorUnixMs => _actionAnchorUnixMs;
        public long Revision           => _revision;

        /****************************************
        *                Methods
        ****************************************/

        public AdminCharacterWalletView(long currency, long currencyReserved, long tickets, long ticketsReserved,
            long actionValue, long actionAnchorUnixMs, long revision)
        {
            _currency           = currency;
            _currencyReserved   = currencyReserved;
            _tickets            = tickets;
            _ticketsReserved    = ticketsReserved;
            _actionValue        = actionValue;
            _actionAnchorUnixMs = actionAnchorUnixMs;
            _revision           = revision;
        }

        private AdminCharacterWalletView() { }
    }
}
