// System
using System;
using System.IO;
using System.Text;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 진단용 체크포인트의 바이너리 형식(schemaVersion 1), BinaryWriter 리틀 엔디언 순서를 고정한다 </summary>
    public static class SessionCheckpointCodec
    {
        // 형식을 바꾸면 SCHEMA_VERSION을 올리고 이전 판 읽기를 남긴다. 조회 도구가 이 순서로 읽는다.
        // 1: int 판, double 서버 시각, double 경과, int 시작 인원, long 처치 순번, int 남은 공용 부활,
        //    bool 웨이브 + int 웨이브 순서 + int 웨이브 수 + int 웨이브 남은 적 + double 웨이브 경과,
        //    bool 보스 + float 보스 체력 비율 + int 보스 페이즈,
        //    int 참가자 수 + 참가자마다 string 키, byte 연결, byte 생사, byte 닫힘 원인, double 유예 만료,
        //    bool 전투 값 + float 체력 + float 최대 체력 + float 기력 + float 공명 + double 사망 시각 + bool 소유권 분리

        /****************************************
        *                Fields
        ****************************************/

        public const int SCHEMA_VERSION = 1;

        // 백엔드 CheckpointInput.MAX_STATE_BYTES(session_checkpoints 256KB 상한)와 같다
        public const int MAX_STATE_BYTES = 262144;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 상태를 쓴다, 스트림 위치부터 이어 쓴다 </summary>
        public static void Write(BinaryWriter writer, SessionCheckpointState state)
        {
            if ((writer == null) || (state == null))
            {
                throw new ArgumentNullException((writer == null) ? nameof(writer) : nameof(state));
            }

            writer.Write(SCHEMA_VERSION);
            writer.Write(state.ServerTime);
            writer.Write(state.ElapsedSeconds);
            writer.Write(state.StartingCount);
            writer.Write(state.KillSequence);
            writer.Write(state.RemainingRevives);

            writer.Write(state.HasWave);
            writer.Write(state.WaveIndex);
            writer.Write(state.WaveCount);
            writer.Write(state.RemainingInWave);
            writer.Write(state.WaveElapsed);

            writer.Write(state.HasBoss);
            writer.Write(state.BossHealthRate);
            writer.Write(state.BossPhase);

            writer.Write(state.ParticipantCount);
            for (int i = 0; i < state.ParticipantCount; ++i)
            {
                ref readonly CheckpointParticipant participant = ref state.GetAt(i);
                writer.Write(participant.ParticipantKey ?? string.Empty);
                writer.Write((byte)participant.ConnectionState);
                writer.Write((byte)participant.LifeState);
                writer.Write((byte)participant.CloseReason);
                writer.Write(participant.GraceUntil);
                writer.Write(participant.HasVitals);
                writer.Write(participant.Vitals.Health);
                writer.Write(participant.Vitals.MaxHealth);
                writer.Write(participant.Vitals.Energy);
                writer.Write(participant.Vitals.Resonance);
                writer.Write(participant.Vitals.DeathTime);
                writer.Write(participant.Vitals.IsDetached);
            }
        }

        /// <summary> Write가 쓴 바이트를 읽는다, 판이 다르거나 길이가 맞지 않으면 거짓. 조회 도구와 시험용이다 </summary>
        public static bool TryRead(byte[] bytes, SessionCheckpointState state)
        {
            if ((bytes == null) || (state == null))
            {
                return false;
            }

            try
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (reader.ReadInt32() != SCHEMA_VERSION)
                    {
                        return false;
                    }

                    state.ServerTime       = reader.ReadDouble();
                    state.ElapsedSeconds   = reader.ReadDouble();
                    state.StartingCount    = reader.ReadInt32();
                    state.KillSequence     = reader.ReadInt64();
                    state.RemainingRevives = reader.ReadInt32();

                    state.HasWave         = reader.ReadBoolean();
                    state.WaveIndex       = reader.ReadInt32();
                    state.WaveCount       = reader.ReadInt32();
                    state.RemainingInWave = reader.ReadInt32();
                    state.WaveElapsed     = reader.ReadDouble();

                    state.HasBoss        = reader.ReadBoolean();
                    state.BossHealthRate = reader.ReadSingle();
                    state.BossPhase      = reader.ReadInt32();

                    int count = reader.ReadInt32();
                    if ((count < 0) || (count > state.Capacity))
                    {
                        return false;
                    }

                    state.ClearParticipants();
                    for (int i = 0; i < count; ++i)
                    {
                        string key         = reader.ReadString();
                        var    connection  = (EParticipantConnectionState)reader.ReadByte();
                        var    life        = (EParticipantLifeState)reader.ReadByte();
                        var    closeReason = (ESessionEndReason)reader.ReadByte();
                        double graceUntil  = reader.ReadDouble();
                        bool   hasVitals   = reader.ReadBoolean();
                        float  health      = reader.ReadSingle();
                        float  maxHealth   = reader.ReadSingle();
                        float  energy      = reader.ReadSingle();
                        float  resonance   = reader.ReadSingle();
                        double deathTime   = reader.ReadDouble();
                        bool   detached    = reader.ReadBoolean();
                        var    vitals      = new CheckpointVitals(health, maxHealth, energy, resonance, deathTime, detached);
                        state.TryAdd(new CheckpointParticipant(key, connection, life, closeReason, graceUntil, hasVitals,
                            in vitals));
                    }

                    return stream.Position == stream.Length;
                }
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }
    }
}
