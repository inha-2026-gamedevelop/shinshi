// System
using System;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 마을 성장 메시지의 Mirror 직렬화, Weaver가 이 메서드를 쓴다 </summary>
    public static class GrowthMessageSerializer
    {
        // 메시지는 private readonly 필드라 Weaver 자동 직렬화가 내용을 쓰지 못한다. 여기서 순서를 고정한다.
        // 클라이언트가 보내는 배열(재분배, 조합)은 계약 상한으로, 서버가 보내는 성장 상태 배열은 넉넉한 상한으로 막는다

        /****************************************
        *                Fields
        ****************************************/

        // 성장 상태 한 배열의 상한. 보관함 용량과 효과 수보다 크게 잡아 정상 상태가 잘리지 않게 한다
        private const int MAX_SNAPSHOT_ENTRIES = 1024;

        /****************************************
        *                Methods
        ****************************************/

        public static void WriteGrowthRequestMessage(this NetworkWriter writer, GrowthRequestMessage value)
        {
            writer.WriteGuid(value.OperationId);
            writer.WriteByte((byte)value.Action);
            writer.WriteString(value.TargetId);
            writer.WriteString(value.SecondaryId);
            writer.WriteInt(value.IntArg);
            writer.WriteInt(value.IntArg2);
            writer.WriteBool(value.Flag);
            WriteInts(writer, value.Allocation);
            WriteLoadout(writer, value.Loadout);
        }

        public static GrowthRequestMessage ReadGrowthRequestMessage(this NetworkReader reader)
        {
            Guid               operationId = reader.ReadGuid();
            var                action      = (EGrowthAction)reader.ReadByte();
            string             targetId    = reader.ReadString();
            string             secondaryId = reader.ReadString();
            int                intArg      = reader.ReadInt();
            int                intArg2     = reader.ReadInt();
            bool               flag        = reader.ReadBool();
            int[]              allocation  = ReadInts(reader, Constants.Town.TRAINING_STAT_COUNT);
            TownLoadoutEntry[] loadout     = ReadLoadout(reader, Constants.Town.MAX_GROWTH_LOADOUT_SLOTS);
            return new GrowthRequestMessage(operationId, action, targetId, secondaryId, intArg, intArg2, flag, allocation, loadout);
        }

        public static void WriteGrowthResultMessage(this NetworkWriter writer, GrowthResultMessage value)
        {
            writer.WriteGuid(value.OperationId);
            writer.WriteByte((byte)value.Action);
            writer.WriteVarUInt((uint)value.Reason);
            writer.WriteInt(value.Outcome);
            writer.WriteString(value.ItemId);
        }

        public static GrowthResultMessage ReadGrowthResultMessage(this NetworkReader reader)
        {
            Guid   operationId = reader.ReadGuid();
            var    action      = (EGrowthAction)reader.ReadByte();
            var    reason      = (ERejectReason)reader.ReadVarUInt();
            int    outcome     = reader.ReadInt();
            string itemId      = reader.ReadString();
            return new GrowthResultMessage(operationId, action, reason, outcome, itemId);
        }

        public static void WriteGrowthSnapshotMessage(this NetworkWriter writer, GrowthSnapshotMessage value)
        {
            writer.WriteInt(value.ClassId);
            writer.WriteLong(value.LoadoutRevision);
            writer.WriteLong(value.Essence);
            writer.WriteLong(value.Shards);
            WriteInts(writer, value.Training);

            TownEquipmentEntry[] equipment = value.Equipment;
            writer.WriteInt(equipment.Length);
            for (int i = 0; i < equipment.Length; ++i)
            {
                ref readonly TownEquipmentEntry entry = ref equipment[i];
                writer.WriteString(entry.ItemId);
                writer.WriteString(entry.DefinitionId);
                writer.WriteByte(entry.Slot);
                writer.WriteByte(entry.Grade);
                writer.WriteBool(entry.IsTraining);
                writer.WriteString(entry.Origin);
                writer.WriteInt(entry.Enhancement);
                writer.WriteBool(entry.Equipped);
                writer.WriteBool(entry.Locked);
                writer.WriteBool(entry.Bound);
                writer.WriteBool(entry.InMarket);
                writer.WriteByte(entry.Option1Kind);
                writer.WriteInt(entry.Option1Value);
                writer.WriteByte(entry.Option2Kind);
                writer.WriteInt(entry.Option2Value);
            }

            TownPityEntry[] pity = value.Pity;
            writer.WriteInt(pity.Length);
            for (int i = 0; i < pity.Length; ++i)
            {
                writer.WriteByte(pity[i].Slot);
                writer.WriteInt(pity[i].Stage);
                writer.WriteInt(pity[i].Failures);
            }

            TownTalismanEntry[] talismans = value.Talismans;
            writer.WriteInt(talismans.Length);
            for (int i = 0; i < talismans.Length; ++i)
            {
                writer.WriteString(talismans[i].EffectId);
                writer.WriteInt(talismans[i].Level);
                writer.WriteInt(talismans[i].RegisterFailures);
            }

            WriteLoadout(writer, value.Loadout);
            WriteItems(writer, value.AccountItems);
            writer.WriteInt(value.StorageUsed);
            writer.WriteInt(value.StorageCapacity);
            writer.WriteString(value.ContentVersion);
            writer.WriteLong(value.TotalExperience);
            WriteInts(writer, value.OptionPity);
        }

        public static GrowthSnapshotMessage ReadGrowthSnapshotMessage(this NetworkReader reader)
        {
            int   classId         = reader.ReadInt();
            long  loadoutRevision = reader.ReadLong();
            long  essence         = reader.ReadLong();
            long  shards          = reader.ReadLong();
            int[] training        = ReadInts(reader, MAX_SNAPSHOT_ENTRIES);

            int equipmentCount = PartyMessageSerializer.ReadCount(reader, MAX_SNAPSHOT_ENTRIES);
            var equipment      = new TownEquipmentEntry[equipmentCount];
            for (int i = 0; i < equipmentCount; ++i)
            {
                string itemId       = reader.ReadString();
                string definitionId = reader.ReadString();
                byte   slot         = reader.ReadByte();
                byte   grade        = reader.ReadByte();
                bool   isTraining   = reader.ReadBool();
                string origin       = reader.ReadString();
                int    enhancement  = reader.ReadInt();
                bool   equipped     = reader.ReadBool();
                bool   locked       = reader.ReadBool();
                bool   bound        = reader.ReadBool();
                bool   inMarket     = reader.ReadBool();
                byte   option1Kind  = reader.ReadByte();
                int    option1Value = reader.ReadInt();
                byte   option2Kind  = reader.ReadByte();
                int    option2Value = reader.ReadInt();
                equipment[i] = new TownEquipmentEntry(itemId, definitionId, slot, grade, isTraining, origin, enhancement,
                    equipped, locked, bound, inMarket, option1Kind, option1Value, option2Kind, option2Value);
            }

            int pityCount = PartyMessageSerializer.ReadCount(reader, MAX_SNAPSHOT_ENTRIES);
            var pity      = new TownPityEntry[pityCount];
            for (int i = 0; i < pityCount; ++i)
            {
                byte slot     = reader.ReadByte();
                int  stage    = reader.ReadInt();
                int  failures = reader.ReadInt();
                pity[i] = new TownPityEntry(slot, stage, failures);
            }

            int talismanCount = PartyMessageSerializer.ReadCount(reader, MAX_SNAPSHOT_ENTRIES);
            var talismans     = new TownTalismanEntry[talismanCount];
            for (int i = 0; i < talismanCount; ++i)
            {
                string effectId = reader.ReadString();
                int    level    = reader.ReadInt();
                int    failures = reader.ReadInt();
                talismans[i] = new TownTalismanEntry(effectId, level, failures);
            }

            TownLoadoutEntry[] loadout         = ReadLoadout(reader, MAX_SNAPSHOT_ENTRIES);
            TownItemEntry[]    accountItems    = ReadItems(reader, MAX_SNAPSHOT_ENTRIES);
            int                storageUsed     = reader.ReadInt();
            int                storageCapacity = reader.ReadInt();
            string             contentVersion  = reader.ReadString();
            long totalExperience = reader.ReadLong();
            int[] optionPity     = ReadInts(reader, MAX_SNAPSHOT_ENTRIES);
            return new GrowthSnapshotMessage(classId, loadoutRevision, essence, shards, training, equipment, pity, talismans,
                loadout, accountItems, storageUsed, storageCapacity, contentVersion, totalExperience, optionPity);
        }

        /// <summary> 수량형 아이템 배열, 생활 재고 메시지도 같은 형식을 쓴다 </summary>
        public static void WriteItems(NetworkWriter writer, TownItemEntry[] items)
        {
            int count = (items != null) ? items.Length : 0;
            writer.WriteInt(count);
            for (int i = 0; i < count; ++i)
            {
                writer.WriteString(items[i].DefinitionId);
                writer.WriteString(items[i].BindingKind);
                writer.WriteLong(items[i].Quantity);
                writer.WriteLong(items[i].Reserved);
            }
        }

        public static TownItemEntry[] ReadItems(NetworkReader reader, int max)
        {
            int count = PartyMessageSerializer.ReadCount(reader, max);
            var items = new TownItemEntry[count];
            for (int i = 0; i < count; ++i)
            {
                string definitionId = reader.ReadString();
                string bindingKind  = reader.ReadString();
                long   quantity     = reader.ReadLong();
                long   reserved     = reader.ReadLong();
                items[i] = new TownItemEntry(definitionId, bindingKind, quantity, reserved);
            }

            return items;
        }

        private static void WriteInts(NetworkWriter writer, int[] values)
        {
            int count = (values != null) ? values.Length : 0;
            writer.WriteInt(count);
            for (int i = 0; i < count; ++i)
            {
                writer.WriteInt(values[i]);
            }
        }

        private static int[] ReadInts(NetworkReader reader, int max)
        {
            int count  = PartyMessageSerializer.ReadCount(reader, max);
            var values = new int[count];
            for (int i = 0; i < count; ++i)
            {
                values[i] = reader.ReadInt();
            }

            return values;
        }

        private static void WriteLoadout(NetworkWriter writer, TownLoadoutEntry[] entries)
        {
            int count = (entries != null) ? entries.Length : 0;
            writer.WriteInt(count);
            for (int i = 0; i < count; ++i)
            {
                writer.WriteString(entries[i].SkillId);
                writer.WriteInt(entries[i].Tier);
                writer.WriteString(entries[i].EffectId);
            }
        }

        private static TownLoadoutEntry[] ReadLoadout(NetworkReader reader, int max)
        {
            int count   = PartyMessageSerializer.ReadCount(reader, max);
            var entries = new TownLoadoutEntry[count];
            for (int i = 0; i < count; ++i)
            {
                string skillId  = reader.ReadString();
                int    tier     = reader.ReadInt();
                string effectId = reader.ReadString();
                entries[i] = new TownLoadoutEntry(skillId, tier, effectId);
            }

            return entries;
        }
    }
}
