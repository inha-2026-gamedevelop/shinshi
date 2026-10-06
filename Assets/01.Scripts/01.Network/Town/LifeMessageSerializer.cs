// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 마을 생활 메시지의 Mirror 직렬화, Weaver가 이 메서드를 쓴다 </summary>
    public static class LifeMessageSerializer
    {
        // 메시지는 private readonly 필드라 Weaver 자동 직렬화가 내용을 쓰지 못한다. 여기서 순서를 고정한다.
        // 노드 상태는 접속자 전원에게 가는 유일한 생활 메시지라 정수와 1바이트 상태만 싣는다(약 17바이트)

        /****************************************
        *                Fields
        ****************************************/

        // 물약 종류 수(소형, 대형)보다 넉넉한 상한
        private const int MAX_POTION_ENTRIES = 16;

        /****************************************
        *                Methods
        ****************************************/

        public static void WriteLifeRequestMessage(this NetworkWriter writer, LifeRequestMessage value)
        {
            writer.WriteGuid(value.OperationId);
            writer.WriteByte((byte)value.Action);
            writer.WriteInt(value.Zone);
            writer.WriteInt(value.Node);
            writer.WriteString(value.PotionDefinitionId);
            writer.WriteInt(value.Count);
        }

        public static LifeRequestMessage ReadLifeRequestMessage(this NetworkReader reader)
        {
            Guid   operationId        = reader.ReadGuid();
            var    action             = (ELifeAction)reader.ReadByte();
            int    zone               = reader.ReadInt();
            int    node               = reader.ReadInt();
            string potionDefinitionId = reader.ReadString();
            int    count              = reader.ReadInt();
            return new LifeRequestMessage(operationId, action, zone, node, potionDefinitionId, count);
        }

        public static void WriteLifeResultMessage(this NetworkWriter writer, LifeResultMessage value)
        {
            writer.WriteGuid(value.OperationId);
            writer.WriteByte((byte)value.Action);
            writer.WriteVarUInt((uint)value.Reason);
            writer.WriteInt(value.Zone);
            writer.WriteInt(value.Node);
        }

        public static LifeResultMessage ReadLifeResultMessage(this NetworkReader reader)
        {
            Guid operationId = reader.ReadGuid();
            var  action      = (ELifeAction)reader.ReadByte();
            var  reason      = (ERejectReason)reader.ReadVarUInt();
            int  zone        = reader.ReadInt();
            int  node        = reader.ReadInt();
            return new LifeResultMessage(operationId, action, reason, zone, node);
        }

        public static void WriteLifeSnapshotMessage(this NetworkWriter writer, LifeSnapshotMessage value)
        {
            writer.WriteLong(value.ActionValue);
            writer.WriteLong(value.ActionMax);
            writer.WriteLong(value.NextRecoverAtUnixMs);
            writer.WriteString(value.HerbId);
            writer.WriteLong(value.HerbQuantity);
            writer.WriteLong(value.HerbCap);
            GrowthMessageSerializer.WriteItems(writer, value.Potions);
        }

        public static LifeSnapshotMessage ReadLifeSnapshotMessage(this NetworkReader reader)
        {
            long            actionValue   = reader.ReadLong();
            long            actionMax     = reader.ReadLong();
            long            nextRecoverAt = reader.ReadLong();
            string          herbId        = reader.ReadString();
            long            herbQuantity  = reader.ReadLong();
            long            herbCap       = reader.ReadLong();
            TownItemEntry[] potions       = GrowthMessageSerializer.ReadItems(reader, MAX_POTION_ENTRIES);
            return new LifeSnapshotMessage(actionValue, actionMax, nextRecoverAt, herbId, herbQuantity, herbCap, potions);
        }

        public static void WriteGatherNodeStateMessage(this NetworkWriter writer, GatherNodeStateMessage value)
        {
            writer.WriteInt(value.Zone);
            writer.WriteInt(value.Node);
            writer.WriteByte((byte)value.State);
            writer.WriteInt(value.ClaimParticipantId);
            writer.WriteFloat(value.RegrowRemaining);
        }

        public static GatherNodeStateMessage ReadGatherNodeStateMessage(this NetworkReader reader)
        {
            int   zone            = reader.ReadInt();
            int   node            = reader.ReadInt();
            var   state           = (EGatherNodeState)reader.ReadByte();
            int   participantId   = reader.ReadInt();
            float regrowRemaining = reader.ReadFloat();
            return new GatherNodeStateMessage(zone, node, state, participantId, regrowRemaining);
        }
    }
}
