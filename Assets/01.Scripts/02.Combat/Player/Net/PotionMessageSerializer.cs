// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 물약 메시지의 Mirror 직렬화, Weaver가 NetworkMessage에 이 메서드를 쓴다 </summary>
    public static class PotionMessageSerializer
    {
        // 메시지는 바꿀 수 없게 private readonly 필드로 두었다. Weaver는 public 필드만 자동 직렬화하므로 여기서 순서를 고정한다.
        // 순서를 바꾸면 서버와 클라이언트가 다른 값을 읽으므로 둘을 함께 배포한다(PROTOCOL_VERSION이 같아야 접속된다)

        /****************************************
        *                Methods
        ****************************************/

        public static void WritePotionUseRequestMessage(this NetworkWriter writer, PotionUseRequestMessage value)
        {
            writer.WritePlayerInputHeader(value.Header);
            writer.WriteByte((byte)value.Size);
        }

        public static PotionUseRequestMessage ReadPotionUseRequestMessage(this NetworkReader reader)
        {
            PlayerInputHeader header = reader.ReadPlayerInputHeader();
            var               size   = (EPotionSize)reader.ReadByte();
            return new PotionUseRequestMessage(header, size);
        }

        public static void WritePotionStateMessage(this NetworkWriter writer, PotionStateMessage value)
        {
            writer.WriteVarUInt(value.Sequence);
            writer.WriteVarUInt((uint)value.Reason);
            writer.WriteUShort(value.SmallRemaining);
            writer.WriteUShort(value.LargeRemaining);
            writer.WriteByte(value.BossUsed);
            writer.WriteByte(value.BossLimit);
            writer.WriteBool(value.IsPending);
            writer.WriteDouble(value.CooldownEndsAt);
        }

        public static PotionStateMessage ReadPotionStateMessage(this NetworkReader reader)
        {
            uint   sequence       = reader.ReadVarUInt();
            var    reason         = (ERejectReason)reader.ReadVarUInt();
            ushort smallRemaining = reader.ReadUShort();
            ushort largeRemaining = reader.ReadUShort();
            byte   bossUsed       = reader.ReadByte();
            byte   bossLimit      = reader.ReadByte();
            bool   isPending      = reader.ReadBool();
            double cooldownEndsAt = reader.ReadDouble();
            return new PotionStateMessage(sequence, reason, smallRemaining, largeRemaining, bossUsed, bossLimit, isPending,
                cooldownEndsAt);
        }
    }
}
