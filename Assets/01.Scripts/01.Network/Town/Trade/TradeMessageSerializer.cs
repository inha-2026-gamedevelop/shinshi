// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 1:1 거래 메시지의 Mirror 직렬화, Weaver가 이 메서드를 쓴다 </summary>
    public static class TradeMessageSerializer
    {
        // 품목 칸은 계약 상한(TradeCodes.TRADE_MAX_ITEMS)으로 막는다. 이름 길이는 Mirror 문자열 상한을 따른다

        /****************************************
        *                Methods
        ****************************************/

        public static void WriteTradeRequestMessage(this NetworkWriter writer, TradeRequestMessage value)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteGuid(value.TradeId);
            writer.WriteByte((byte)value.Action);
            writer.WriteString(value.TargetName);
            writer.WriteBool(value.Accept);
            WriteLines(writer, value.Lines);
            writer.WriteLong(value.Currency);
            writer.WriteInt(value.OfferRevision);
            writer.WriteInt(value.ConfirmGeneration);
        }

        public static TradeRequestMessage ReadTradeRequestMessage(this NetworkReader reader)
        {
            Guid             requestId  = reader.ReadGuid();
            Guid             tradeId    = reader.ReadGuid();
            var              action     = (ETradeAction)reader.ReadByte();
            string           targetName = reader.ReadString();
            bool             accept     = reader.ReadBool();
            TradeOfferLine[] lines      = ReadLines(reader);
            long             currency   = reader.ReadLong();
            int              revision   = reader.ReadInt();
            int              generation = reader.ReadInt();
            return new TradeRequestMessage(requestId, tradeId, action, targetName, accept, lines, currency, revision, generation);
        }

        public static void WriteTradeStateMessage(this NetworkWriter writer, TradeStateMessage value)
        {
            writer.WriteGuid(value.TradeId);
            writer.WriteByte((byte)value.State);
            writer.WriteVarUInt((uint)value.Reason);
            writer.WriteString(value.PartnerName);
            WriteLines(writer, value.MyLines);
            WriteLines(writer, value.PartnerLines);
            writer.WriteLong(value.MyCurrency);
            writer.WriteLong(value.PartnerCurrency);
            writer.WriteBool(value.MyLocked);
            writer.WriteBool(value.PartnerLocked);
            writer.WriteBool(value.MyAccepted);
            writer.WriteBool(value.PartnerAccepted);
            writer.WriteInt(value.OfferRevision);
            writer.WriteInt(value.ConfirmGeneration);
            writer.WriteFloat(value.CountdownRemaining);
        }

        public static TradeStateMessage ReadTradeStateMessage(this NetworkReader reader)
        {
            Guid             tradeId          = reader.ReadGuid();
            var              state            = (ETradeState)reader.ReadByte();
            var              reason           = (ERejectReason)reader.ReadVarUInt();
            string           partnerName      = reader.ReadString();
            TradeOfferLine[] myLines          = ReadLines(reader);
            TradeOfferLine[] partnerLines     = ReadLines(reader);
            long             myCurrency       = reader.ReadLong();
            long             partnerCurrency  = reader.ReadLong();
            bool             myLocked         = reader.ReadBool();
            bool             partnerLocked    = reader.ReadBool();
            bool             myAccepted       = reader.ReadBool();
            bool             partnerAccepted  = reader.ReadBool();
            int              offerRevision    = reader.ReadInt();
            int              generation       = reader.ReadInt();
            float            countdown        = reader.ReadFloat();
            return new TradeStateMessage(tradeId, state, reason, partnerName, myLines, partnerLines, myCurrency, partnerCurrency,
                myLocked, partnerLocked, myAccepted, partnerAccepted, offerRevision, generation, countdown);
        }

        public static void WriteTradeInviteMessage(this NetworkWriter writer, TradeInviteMessage value)
        {
            writer.WriteGuid(value.TradeId);
            writer.WriteString(value.FromName);
            writer.WriteFloat(value.ExpiresIn);
        }

        public static TradeInviteMessage ReadTradeInviteMessage(this NetworkReader reader)
        {
            Guid   tradeId   = reader.ReadGuid();
            string fromName  = reader.ReadString();
            float  expiresIn = reader.ReadFloat();
            return new TradeInviteMessage(tradeId, fromName, expiresIn);
        }

        public static void WriteTradeResultMessage(this NetworkWriter writer, TradeResultMessage value)
        {
            writer.WriteGuid(value.RequestId);
            writer.WriteGuid(value.TradeId);
            writer.WriteByte((byte)value.Action);
            writer.WriteVarUInt((uint)value.Reason);
        }

        public static TradeResultMessage ReadTradeResultMessage(this NetworkReader reader)
        {
            Guid requestId = reader.ReadGuid();
            Guid tradeId   = reader.ReadGuid();
            var  action    = (ETradeAction)reader.ReadByte();
            var  reason    = (ERejectReason)reader.ReadVarUInt();
            return new TradeResultMessage(requestId, tradeId, action, reason);
        }

        private static void WriteLines(NetworkWriter writer, TradeOfferLine[] lines)
        {
            int count = (lines != null) ? lines.Length : 0;
            writer.WriteInt(count);
            for (int i = 0; i < count; ++i)
            {
                writer.WriteString(lines[i].DefinitionId);
                writer.WriteString(lines[i].ItemId);
                writer.WriteInt(lines[i].Quantity);
            }
        }

        private static TradeOfferLine[] ReadLines(NetworkReader reader)
        {
            int count = PartyMessageSerializer.ReadCount(reader, TradeCodes.TRADE_MAX_ITEMS);
            var lines = new TradeOfferLine[count];
            for (int i = 0; i < count; ++i)
            {
                string definitionId = reader.ReadString();
                string itemId       = reader.ReadString();
                int    quantity     = reader.ReadInt();
                lines[i] = new TradeOfferLine(definitionId, itemId, quantity);
            }

            return lines;
        }
    }
}
