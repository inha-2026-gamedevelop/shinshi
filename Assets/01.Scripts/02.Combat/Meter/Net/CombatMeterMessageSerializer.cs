// System
using System.IO;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 딜미터 메시지의 Mirror 직렬화, Weaver가 NetworkMessage에 이 메서드를 쓴다 </summary>
    public static class CombatMeterMessageSerializer
    {
        // 메시지는 private readonly 필드라 Weaver 자동 직렬화가 내용을 쓰지 못한다. 여기서 순서를 고정한다.
        // 배열은 길이를 먼저 쓰고 원소를 쓴다. 읽을 때 길이가 계약 상한을 넘거나 음수면 PartyMessageSerializer.ReadCount와
        // 같은 관례로 예외를 던져 메시지를 통째로 버린다(Mirror가 연결을 끊는다). 길이 뒤의 필드가 밀려 읽히는 것을 막는다

        /****************************************
        *                Methods
        ****************************************/

        public static void WriteCombatMeterSelfMessage(this NetworkWriter writer, CombatMeterSelfMessage value)
        {
            writer.WriteByte(value.Phase);
            writer.WriteFloat(value.PhaseElapsedSeconds);
            writer.WriteFloat(value.EncounterElapsedSeconds);
            writer.WriteFloat(value.ActiveSeconds);
            writer.WriteFloat(value.EncounterDamage);
            writer.WriteFloat(value.PhaseDamage);
            writer.WriteFloat(value.DamageTaken);
            writer.WriteUShort(value.GuardedHits);
            WriteSlots(writer, value.Slots);
        }

        public static CombatMeterSelfMessage ReadCombatMeterSelfMessage(this NetworkReader reader)
        {
            byte   phase                   = reader.ReadByte();
            float  phaseElapsedSeconds     = reader.ReadFloat();
            float  encounterElapsedSeconds = reader.ReadFloat();
            float  activeSeconds           = reader.ReadFloat();
            float  encounterDamage         = reader.ReadFloat();
            float  phaseDamage             = reader.ReadFloat();
            float  damageTaken             = reader.ReadFloat();
            ushort guardedHits             = reader.ReadUShort();
            CombatMeterSlotStat[] slots    = ReadSlots(reader);
            return new CombatMeterSelfMessage(phase, phaseElapsedSeconds, encounterElapsedSeconds, activeSeconds,
                encounterDamage, phaseDamage, damageTaken, guardedHits, slots);
        }

        public static void WriteCombatMeterPartyMessage(this NetworkWriter writer, CombatMeterPartyMessage value)
        {
            writer.WriteByte(value.Phase);
            writer.WriteFloat(value.PhaseElapsedSeconds);
            writer.WriteFloat(value.EncounterElapsedSeconds);

            CombatMeterPartyEntry[] entries = value.Entries;
            writer.WriteInt(entries.Length);
            for (int i = 0; i < entries.Length; ++i)
            {
                WritePartyEntry(writer, in entries[i]);
            }
        }

        public static CombatMeterPartyMessage ReadCombatMeterPartyMessage(this NetworkReader reader)
        {
            byte  phase                   = reader.ReadByte();
            float phaseElapsedSeconds     = reader.ReadFloat();
            float encounterElapsedSeconds = reader.ReadFloat();

            int count   = ReadSafeCount(reader, Constants.CombatMeter.MAX_PARTICIPANTS);
            var entries = new CombatMeterPartyEntry[count];
            for (int i = 0; i < count; ++i)
            {
                entries[i] = ReadPartyEntry(reader);
            }

            return new CombatMeterPartyMessage(phase, phaseElapsedSeconds, encounterElapsedSeconds, entries);
        }

        public static void WriteCombatMeterFinalSelfMessage(this NetworkWriter writer, CombatMeterFinalSelfMessage value)
        {
            writer.WriteInt(value.SessionEpoch);
            writer.WriteFloat(value.EncounterSeconds);
            writer.WriteFloat(value.ActiveSeconds);
            writer.WriteFloat(value.EncounterDamage);
            writer.WriteFloat(value.DamageTaken);
            writer.WriteUShort(value.GuardedHits);
            writer.WriteByte(value.Deaths);
            writer.WriteByte(value.Revives);
            writer.WriteByte(value.PotionsUsed);
            WriteSlots(writer, value.Slots);

            CombatMeterPhaseStat[] phases = value.Phases;
            writer.WriteInt(phases.Length);
            for (int i = 0; i < phases.Length; ++i)
            {
                WritePhaseStat(writer, in phases[i]);
            }

            CombatMeterSourceAmount[] topDamageTaken = value.TopDamageTaken;
            writer.WriteInt(topDamageTaken.Length);
            for (int i = 0; i < topDamageTaken.Length; ++i)
            {
                WriteSourceAmount(writer, in topDamageTaken[i]);
            }

            CombatMeterDeathEntry[] lastDeathRecap = value.LastDeathRecap;
            writer.WriteInt(lastDeathRecap.Length);
            for (int i = 0; i < lastDeathRecap.Length; ++i)
            {
                WriteDeathEntry(writer, in lastDeathRecap[i]);
            }
        }

        public static CombatMeterFinalSelfMessage ReadCombatMeterFinalSelfMessage(this NetworkReader reader)
        {
            int    sessionEpoch    = reader.ReadInt();
            float  encounterSeconds = reader.ReadFloat();
            float  activeSeconds    = reader.ReadFloat();
            float  encounterDamage  = reader.ReadFloat();
            float  damageTaken      = reader.ReadFloat();
            ushort guardedHits      = reader.ReadUShort();
            byte   deaths           = reader.ReadByte();
            byte   revives          = reader.ReadByte();
            byte   potionsUsed      = reader.ReadByte();

            CombatMeterSlotStat[] slots = ReadSlots(reader);

            int phaseCount = ReadSafeCount(reader, Constants.CombatMeter.MAX_PHASES);
            var phases     = new CombatMeterPhaseStat[phaseCount];
            for (int i = 0; i < phaseCount; ++i)
            {
                phases[i] = ReadPhaseStat(reader);
            }

            int topCount = ReadSafeCount(reader, Constants.CombatMeter.TOP_DAMAGE_TAKEN_SOURCES);
            var topDamageTaken = new CombatMeterSourceAmount[topCount];
            for (int i = 0; i < topCount; ++i)
            {
                topDamageTaken[i] = ReadSourceAmount(reader);
            }

            int deathCount = ReadSafeCount(reader, Constants.CombatMeter.DEATH_RECAP_MAX_ENTRIES);
            var lastDeathRecap = new CombatMeterDeathEntry[deathCount];
            for (int i = 0; i < deathCount; ++i)
            {
                lastDeathRecap[i] = ReadDeathEntry(reader);
            }

            return new CombatMeterFinalSelfMessage(sessionEpoch, encounterSeconds, activeSeconds, encounterDamage,
                damageTaken, guardedHits, deaths, revives, potionsUsed, slots, phases, topDamageTaken, lastDeathRecap);
        }

        public static void WriteCombatMeterResultMessage(this NetworkWriter writer, CombatMeterResultMessage value)
        {
            writer.WriteInt(value.SessionEpoch);
            writer.WriteVarUInt((uint)value.Reason);
            writer.WriteFloat(value.EncounterSeconds);

            CombatMeterResultEntry[] entries = value.Entries;
            writer.WriteInt(entries.Length);
            for (int i = 0; i < entries.Length; ++i)
            {
                WriteResultEntry(writer, in entries[i]);
            }

            CombatMeterPhaseStat[] phaseTotals = value.PhaseTotals;
            writer.WriteInt(phaseTotals.Length);
            for (int i = 0; i < phaseTotals.Length; ++i)
            {
                WritePhaseStat(writer, in phaseTotals[i]);
            }
        }

        public static CombatMeterResultMessage ReadCombatMeterResultMessage(this NetworkReader reader)
        {
            int   sessionEpoch     = reader.ReadInt();
            var   reason           = (ESessionEndReason)reader.ReadVarUInt();
            float encounterSeconds = reader.ReadFloat();

            int entryCount = ReadSafeCount(reader, Constants.CombatMeter.MAX_PARTICIPANTS);
            var entries    = new CombatMeterResultEntry[entryCount];
            for (int i = 0; i < entryCount; ++i)
            {
                entries[i] = ReadResultEntry(reader);
            }

            int phaseCount   = ReadSafeCount(reader, Constants.CombatMeter.MAX_PHASES);
            var phaseTotals  = new CombatMeterPhaseStat[phaseCount];
            for (int i = 0; i < phaseCount; ++i)
            {
                phaseTotals[i] = ReadPhaseStat(reader);
            }

            return new CombatMeterResultMessage(sessionEpoch, reason, encounterSeconds, entries, phaseTotals);
        }

        // Slots는 항상 SLOT_COUNT 길이지만 다른 배열과 같은 길이 접두, 상한 방어 방식으로 통일한다
        private static void WriteSlots(NetworkWriter writer, CombatMeterSlotStat[] slots)
        {
            writer.WriteInt(slots.Length);
            for (int i = 0; i < slots.Length; ++i)
            {
                WriteSlotStat(writer, in slots[i]);
            }
        }

        private static CombatMeterSlotStat[] ReadSlots(NetworkReader reader)
        {
            int count = ReadSafeCount(reader, Constants.CombatMeter.SLOT_COUNT);
            var slots = new CombatMeterSlotStat[count];
            for (int i = 0; i < count; ++i)
            {
                slots[i] = ReadSlotStat(reader);
            }

            return slots;
        }

        private static void WriteSlotStat(NetworkWriter writer, in CombatMeterSlotStat value)
        {
            writer.WriteFloat(value.Damage);
            writer.WriteUShort(value.Hits);
            writer.WriteFloat(value.MaxHit);
        }

        private static CombatMeterSlotStat ReadSlotStat(NetworkReader reader)
        {
            float  damage = reader.ReadFloat();
            ushort hits   = reader.ReadUShort();
            float  maxHit = reader.ReadFloat();
            return new CombatMeterSlotStat(damage, hits, maxHit);
        }

        private static void WritePartyEntry(NetworkWriter writer, in CombatMeterPartyEntry value)
        {
            writer.WriteInt(value.CombatId);
            writer.WriteFloat(value.EncounterDamage);
            writer.WriteFloat(value.PhaseDamage);
        }

        private static CombatMeterPartyEntry ReadPartyEntry(NetworkReader reader)
        {
            int   combatId        = reader.ReadInt();
            float encounterDamage = reader.ReadFloat();
            float phaseDamage     = reader.ReadFloat();
            return new CombatMeterPartyEntry(combatId, encounterDamage, phaseDamage);
        }

        private static void WritePhaseStat(NetworkWriter writer, in CombatMeterPhaseStat value)
        {
            writer.WriteByte(value.Phase);
            writer.WriteFloat(value.Seconds);
            writer.WriteFloat(value.Damage);
        }

        private static CombatMeterPhaseStat ReadPhaseStat(NetworkReader reader)
        {
            byte  phase   = reader.ReadByte();
            float seconds = reader.ReadFloat();
            float damage  = reader.ReadFloat();
            return new CombatMeterPhaseStat(phase, seconds, damage);
        }

        private static void WriteSourceAmount(NetworkWriter writer, in CombatMeterSourceAmount value)
        {
            writer.WriteString(value.SourceId);
            writer.WriteFloat(value.Amount);
        }

        private static CombatMeterSourceAmount ReadSourceAmount(NetworkReader reader)
        {
            string sourceId = reader.ReadString();
            float  amount   = reader.ReadFloat();
            return new CombatMeterSourceAmount(sourceId, amount);
        }

        private static void WriteDeathEntry(NetworkWriter writer, in CombatMeterDeathEntry value)
        {
            writer.WriteString(value.SourceId);
            writer.WriteFloat(value.Amount);
            writer.WriteFloat(value.SecondsBeforeDeath);
        }

        private static CombatMeterDeathEntry ReadDeathEntry(NetworkReader reader)
        {
            string sourceId           = reader.ReadString();
            float  amount             = reader.ReadFloat();
            float  secondsBeforeDeath = reader.ReadFloat();
            return new CombatMeterDeathEntry(sourceId, amount, secondsBeforeDeath);
        }

        private static void WriteResultEntry(NetworkWriter writer, in CombatMeterResultEntry value)
        {
            writer.WriteInt(value.CombatId);
            writer.WriteString(value.DisplayName);
            writer.WriteFloat(value.Damage);
            writer.WriteFloat(value.DamageTaken);
            writer.WriteByte(value.Deaths);
            writer.WriteByte(value.Revives);
        }

        private static CombatMeterResultEntry ReadResultEntry(NetworkReader reader)
        {
            int    combatId    = reader.ReadInt();
            string displayName = reader.ReadString();
            float  damage      = reader.ReadFloat();
            float  damageTaken = reader.ReadFloat();
            byte   deaths      = reader.ReadByte();
            byte   revives     = reader.ReadByte();
            return new CombatMeterResultEntry(combatId, displayName, damage, damageTaken, deaths, revives);
        }

        // 길이가 음수거나 상한을 넘으면 예외를 던져 메시지를 통째로 버린다(변조 방어, PartyMessageSerializer.ReadCount와 같은 관례)
        private static int ReadSafeCount(NetworkReader reader, int max)
        {
            int count = reader.ReadInt();
            if ((count < 0) || (count > max))
            {
                throw new InvalidDataException("배열 길이가 계약 상한을 넘는다");
            }

            return count;
        }
    }
}
