namespace Shinshi.Common
{
    /// <summary> 경제 원장 사유, API와 공유하므로 기존 값을 바꾸거나 재배열하지 않는다 </summary>
    public enum EEconomyReason
    {
        SessionStartTicket   = 1,
        SessionPotionUse     = 2,
        SessionSettlement    = 3,
        ServiceCancelRefund  = 4,
        ServiceCancelEssence = 5,
        SessionReward        = 6,
        BossReward           = 7,
        AuctionReserve       = 8,
        AuctionRelease       = 9,
        AuctionSettle        = 10,
        AuctionPayout        = 11,
        Training             = 12,
        Craft                = 13,
        Enhance              = 14,
        Inherit              = 15,
        TalismanRegister     = 16,
        TalismanUpgrade      = 17,
        Dismantle            = 18,
        Gathering            = 19,
        PotionUse            = 20,
        PotionReturn         = 21,
        TicketConsume        = 22,
        TicketRefund         = 23,
        InboxClaim           = 24,
        InitialGrant         = 25,
        AdminGrant           = 26,
        AdminRevoke          = 27,
        EventReward          = 28,
        Coupon               = 29,
        RecoveryCompensation = 30
    }
}
