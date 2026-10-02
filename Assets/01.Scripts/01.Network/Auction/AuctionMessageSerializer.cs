// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 파티 경매 메시지의 Mirror 직렬화, Weaver가 이 메서드를 쓴다 </summary>
    public static class AuctionMessageSerializer
    {
        // 메시지는 private readonly 필드라 Weaver 자동 직렬화가 내용을 쓰지 못한다. 여기서 순서를 고정한다.
        // 품목 상태는 단계나 최고가가 바뀔 때만 오가는 드문 메시지라 아이템 ID 문자열을 그대로 보낸다

        /****************************************
        *                Methods
        ****************************************/

        public static void WriteAuctionStateMessage(this NetworkWriter writer, AuctionStateMessage value)
        {
            writer.WriteByte((byte)value.Phase);
            writer.WriteByte((byte)value.Mode);
            writer.WriteString(value.ItemDefinitionId);
            writer.WriteLong(value.HighBid);
            writer.WriteInt(value.HighParticipantId);
            writer.WriteLong(value.MinimumBid);
            writer.WriteDouble(value.EndsAt);
            writer.WriteBool(value.IsEligible);
            writer.WriteLong(value.PayoutEach);
        }

        public static AuctionStateMessage ReadAuctionStateMessage(this NetworkReader reader)
        {
            var    phase             = (EAuctionPhase)reader.ReadByte();
            var    mode              = (EAuctionMode)reader.ReadByte();
            string itemDefinitionId  = reader.ReadString();
            long   highBid           = reader.ReadLong();
            int    highParticipantId = reader.ReadInt();
            long   minimumBid        = reader.ReadLong();
            double endsAt            = reader.ReadDouble();
            bool   isEligible        = reader.ReadBool();
            long   payoutEach        = reader.ReadLong();
            return new AuctionStateMessage(phase, mode, itemDefinitionId, highBid, highParticipantId, minimumBid, endsAt,
                isEligible, payoutEach);
        }

        public static void WriteAuctionBidMessage(this NetworkWriter writer, AuctionBidMessage value)
        {
            writer.WriteLong(value.Amount);
        }

        public static AuctionBidMessage ReadAuctionBidMessage(this NetworkReader reader)
        {
            return new AuctionBidMessage(reader.ReadLong());
        }

        public static void WriteAuctionBidResultMessage(this NetworkWriter writer, AuctionBidResultMessage value)
        {
            writer.WriteLong(value.Amount);
            writer.WriteVarUInt((uint)value.Reason);
        }

        public static AuctionBidResultMessage ReadAuctionBidResultMessage(this NetworkReader reader)
        {
            long amount = reader.ReadLong();
            var  reason = (ERejectReason)reader.ReadVarUInt();
            return new AuctionBidResultMessage(amount, reason);
        }
    }
}
