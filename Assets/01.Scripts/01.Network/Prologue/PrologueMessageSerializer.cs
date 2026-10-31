// 서드파티
using Mirror;

namespace Shinshi.Network
{
    /// <summary> 프롤로그 메시지의 Mirror 직렬화, Weaver가 이 메서드를 쓴다 </summary>
    public static class PrologueMessageSerializer
    {
        // 메시지는 private readonly 필드라 Weaver 자동 직렬화가 내용을 쓰지 못한다. 여기서 순서를 고정한다

        /****************************************
        *                Methods
        ****************************************/

        public static void WritePrologueStateMessage(this NetworkWriter writer, PrologueStateMessage value)
        {
            writer.WriteByte(value.Stage);
            writer.WriteByte(value.Hint);
            writer.WriteByte(value.PotionCount);
            writer.WriteBool(value.IsTalismanOpen);
            writer.WriteBool(value.IsWolbaekOpen);
            writer.WriteUShort(value.ReviveSerial);
        }

        public static PrologueStateMessage ReadPrologueStateMessage(this NetworkReader reader)
        {
            byte   stage          = reader.ReadByte();
            byte   hint           = reader.ReadByte();
            byte   potionCount    = reader.ReadByte();
            bool   isTalismanOpen = reader.ReadBool();
            bool   isWolbaekOpen  = reader.ReadBool();
            ushort reviveSerial   = reader.ReadUShort();
            return new PrologueStateMessage(stage, hint, potionCount, isTalismanOpen, isWolbaekOpen, reviveSerial);
        }

        public static void WritePrologueEventMessage(this NetworkWriter writer, PrologueEventMessage value)
        {
            writer.WriteByte((byte)value.Event);
        }

        public static PrologueEventMessage ReadPrologueEventMessage(this NetworkReader reader)
        {
            return new PrologueEventMessage((EPrologueClientEvent)reader.ReadByte());
        }
    }
}
