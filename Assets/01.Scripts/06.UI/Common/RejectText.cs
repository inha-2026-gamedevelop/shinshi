// 프로젝트
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>서버가 돌려준 거절 사유를 화면에 보일 한 줄 안내로 바꾼다. 거절 사유가 아닌 값은 빈 글자다</summary>
    public static class RejectText
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string RESONANCE_SHORT_TEXT = "공명이 부족합니다";

        /****************************************
        *                Methods
        ****************************************/

        // 서버는 기력 부족과 강림의 공명 부족을 모두 InsufficientEnergy로 돌려준다.
        // 사유만으로는 둘을 가를 수 없어 공명을 쓰는 요청인지를 함께 받는다
        public static string Describe(ERejectReason reason, bool usesResonance)
        {
            if (usesResonance && (reason == ERejectReason.InsufficientEnergy))
            {
                return RESONANCE_SHORT_TEXT;
            }

            return Describe(reason);
        }

        // 같은 사유를 여러 기능이 함께 쓴다(TooEarly는 부활과 기술, PendingCommit은 경매, NoCapacity는 서버 정원과
        // 기술 효과 수). 그래서 사유 이름만 보고 특정 기능을 가리키는 문구를 쓰지 않는다
        public static string Describe(ERejectReason reason)
        {
            switch (reason)
            {
                case ERejectReason.Dead:
                    return "쓰러진 상태에서는 할 수 없습니다";
                case ERejectReason.WrongPhase:
                    return "지금은 할 수 없습니다";
                case ERejectReason.Cooldown:
                    return "아직 사용할 수 없습니다";
                case ERejectReason.InsufficientEnergy:
                    return "기력이 부족합니다";
                case ERejectReason.InvalidTarget:
                    return "대상을 지정할 수 없습니다";
                case ERejectReason.FullHealth:
                    return "체력이 가득 찼습니다";
                case ERejectReason.Disabled:
                    return "지금은 사용할 수 없습니다";
                case ERejectReason.PotionLimit:
                    return "물약 사용 횟수를 모두 썼습니다";
                case ERejectReason.PendingCommit:
                    return "처리 중입니다. 잠시 기다려 주세요";
                case ERejectReason.TooEarly:
                    return "아직 할 수 없습니다";
                case ERejectReason.NoSafePoint:
                    return "안전한 부활 지점을 찾는 중입니다";
                case ERejectReason.NoRevives:
                    return "남은 부활 횟수가 없습니다";
                case ERejectReason.SessionTerminal:
                    return "전투가 이미 끝났습니다";
                case ERejectReason.Locked:
                    return "잠겨 있습니다";
                case ERejectReason.MissingUnlock:
                    return "아직 해금하지 않았습니다";
                case ERejectReason.InsufficientTicket:
                    return "보스 입장권이 부족합니다";
                case ERejectReason.StaleRevision:
                    return "정보가 바뀌었습니다. 다시 시도해 주세요";
                case ERejectReason.NotLeader:
                    return "방장만 할 수 있습니다";
                case ERejectReason.NotAllReady:
                    return "모든 참가자가 준비해야 합니다";
                case ERejectReason.NoCapacity:
                    return "수용 한도를 넘었습니다";
                case ERejectReason.ReservationConflict:
                    return "예약이 겹쳤습니다. 잠시 뒤 다시 시도해 주세요";
                case ERejectReason.NotEligible:
                    return "참여할 수 없습니다";
                case ERejectReason.Closed:
                    return "이미 마감되었습니다";
                case ERejectReason.BidTooLow:
                    return "입찰 금액이 낮습니다";
                case ERejectReason.InsufficientCurrency:
                    return "엽전이 부족합니다";
                case ERejectReason.BackendUnavailable:
                    return "서버와 연결이 불안정합니다. 잠시 뒤 다시 시도해 주세요";
                case ERejectReason.RateLimited:
                    return "요청이 너무 빠릅니다. 잠시 기다려 주세요";
                case ERejectReason.FeatureDisabled:
                    return "지금은 이용할 수 없는 기능입니다";
                case ERejectReason.InventoryFull:
                    return "보관함이 가득 찼습니다. 공간을 비운 뒤 다시 받아 주세요";
                case ERejectReason.InboxExpired:
                    return "보관 기간이 지난 우편입니다";
                case ERejectReason.InboxAlreadyClaimed:
                    return "이미 받은 우편입니다";
                case ERejectReason.InboxCancelled:
                    return "취소된 우편입니다";
                case ERejectReason.InboxCharacterRequired:
                    return "받을 캐릭터를 골라야 합니다";
                case ERejectReason.InboxNotFound:
                    return "우편을 찾을 수 없습니다";
                case ERejectReason.RoomNotFound:
                    return "파티 방을 찾을 수 없습니다";
                case ERejectReason.RoomPasswordMismatch:
                    return "비밀번호가 맞지 않습니다";
                case ERejectReason.InviteExpired:
                    return "초대 시간이 지났습니다";
                case ERejectReason.MarketListingLimit:
                    return "동시에 등록할 수 있는 물품은 10건입니다";
                case ERejectReason.MarketNotTradable:
                    return "거래할 수 없는 품목입니다";
                case ERejectReason.MarketPriceOutOfRange:
                    return "가격이나 수량이 허용 범위를 벗어났습니다";
                case ERejectReason.MarketSoldOut:
                    return "이미 다 팔렸거나 취소된 물품입니다";
                case ERejectReason.MarketQuantityChanged:
                    return "남은 수량이 줄었습니다. 다시 확인해 주세요";
                case ERejectReason.MarketPriceChanged:
                    return "가격이 바뀌었습니다. 다시 확인해 주세요";
                case ERejectReason.MarketExpired:
                    return "등록 기간이 끝난 물품입니다";
                case ERejectReason.MarketSelfPurchase:
                    return "내 물품은 살 수 없습니다";
                case ERejectReason.MarketEscrowed:
                    return "경매장에 등록된 장비입니다";
                case ERejectReason.MarketNotOwner:
                    return "내 소유나 내 등록이 아닙니다";
                case ERejectReason.CharacterHasMarketListing:
                    return "경매장에 등록한 장비가 있어 캐릭터를 삭제할 수 없습니다";
                case ERejectReason.CharacterDeletePending:
                    return "삭제 대기 시간이 아직 끝나지 않았습니다";
                case ERejectReason.CharacterDeleteNotRequested:
                    return "삭제 예약을 먼저 해야 합니다";
                case ERejectReason.TradeItemChanged:
                    return "거래 품목이나 수량이 맞지 않습니다";
                case ERejectReason.TradeNotTradable:
                    return "거래할 수 없는 품목이 있습니다";
                case ERejectReason.TradeInventoryFull:
                    return "받는 쪽 보관함이 가득 찼습니다";
                case ERejectReason.TradeInsufficientCurrency:
                    return "엽전이 부족합니다";
                case ERejectReason.TradeSameAccount:
                    return "같은 계정과는 거래할 수 없습니다";
                case ERejectReason.TradeNotInTown:
                    return "상대가 마을을 떠났거나 거래를 할 수 없는 상태입니다";
                case ERejectReason.TradeBusy:
                    return "이미 거래 중입니다";
                case ERejectReason.TradeTooFar:
                    return "상대와 너무 멉니다";
                case ERejectReason.TradeRequestExpired:
                    return "거래 요청 시간이 지났습니다";
                case ERejectReason.TradeExpired:
                    return "거래 시간이 초과되었습니다";
                default:
                    return string.Empty;
            }
        }
    }
}
