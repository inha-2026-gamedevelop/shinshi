// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 출정 포기, 상태 메시지의 Mirror 직렬화, Weaver가 이 메서드를 쓴다 </summary>
    public static class SortieMessageSerializer
    {
        // 메시지는 private readonly 필드라 Weaver 자동 직렬화가 내용을 쓰지 못한다. 여기서 순서를 고정한다.
        // 작업 ID는 GUID 16바이트로 보낸다(문자열보다 짧고 형식 검사가 필요 없다)

        /****************************************
        *                Methods
        ****************************************/

        public static void WriteSortieAbandonRequestMessage(this NetworkWriter writer, SortieAbandonRequestMessage value)
        {
            writer.WriteGuid(value.OperationId);
        }

        public static SortieAbandonRequestMessage ReadSortieAbandonRequestMessage(this NetworkReader reader)
        {
            return new SortieAbandonRequestMessage(reader.ReadGuid());
        }

        public static void WriteSortieAbandonResultMessage(this NetworkWriter writer, SortieAbandonResultMessage value)
        {
            writer.WriteGuid(value.OperationId);
            writer.WriteVarUInt((uint)value.Reason);
        }

        public static SortieAbandonResultMessage ReadSortieAbandonResultMessage(this NetworkReader reader)
        {
            Guid operationId = reader.ReadGuid();
            var  reason      = (ERejectReason)reader.ReadVarUInt();
            return new SortieAbandonResultMessage(operationId, reason);
        }

        public static void WriteSortieStatusMessage(this NetworkWriter writer, SortieStatusMessage value)
        {
            writer.WriteByte((byte)value.Phase);
            writer.WriteByte((byte)value.EndReason);
            writer.WriteBool(value.HasPersonalClose);
            writer.WriteByte((byte)value.PersonalCloseReason);
            writer.WriteBool(value.CanReturn);
            writer.WriteByte((byte)value.SettlementState);
        }

        public static SortieStatusMessage ReadSortieStatusMessage(this NetworkReader reader)
        {
            var  phase               = (ESortiePhase)reader.ReadByte();
            var  endReason           = (ESessionEndReason)reader.ReadByte();
            bool hasPersonalClose    = reader.ReadBool();
            var  personalCloseReason = (ESessionEndReason)reader.ReadByte();
            bool canReturn           = reader.ReadBool();
            var  settlementState     = (ESortieSettlementState)reader.ReadByte();
            return new SortieStatusMessage(phase, endReason, hasPersonalClose, personalCloseReason, canReturn, settlementState);
        }

        // 웨이브 수는 한 바이트라 받는 쪽 배열도 255칸을 넘지 않는다
        public static void WriteWaveProgressMessage(this NetworkWriter writer, WaveProgressMessage value)
        {
            int count = Math.Min(value.WaveCount, byte.MaxValue);
            writer.WriteByte((byte)count);
            for (int i = 0; i < count; ++i)
            {
                writer.WriteUShort((ushort)value.GetPlanned(i));
            }

            writer.WriteByte(value.WaveIndex);
            writer.WriteUShort(value.RemainingInWave);
            writer.WriteBool(value.IsRunning);
            writer.WriteFloat(value.ElapsedSeconds);
            writer.WriteFloat(value.TimeLimitSeconds);
            writer.WriteDouble(value.ServerTime);
        }

        public static WaveProgressMessage ReadWaveProgressMessage(this NetworkReader reader)
        {
            int      count   = reader.ReadByte();
            ushort[] planned = new ushort[count];
            for (int i = 0; i < count; ++i)
            {
                planned[i] = reader.ReadUShort();
            }

            byte   waveIndex        = reader.ReadByte();
            ushort remainingInWave  = reader.ReadUShort();
            bool   isRunning        = reader.ReadBool();
            float  elapsedSeconds   = reader.ReadFloat();
            float  timeLimitSeconds = reader.ReadFloat();
            double serverTime       = reader.ReadDouble();
            return new WaveProgressMessage(waveIndex, remainingInWave, isRunning, elapsedSeconds, timeLimitSeconds, serverTime,
                planned);
        }
    }
}
