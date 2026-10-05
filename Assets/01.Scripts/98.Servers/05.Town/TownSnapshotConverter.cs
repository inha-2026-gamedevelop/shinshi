// System
using System;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 백엔드 성장, 생활 스냅샷 DTO를 클라이언트로 보낼 Mirror 메시지로 옮긴다 </summary>
    public static class TownSnapshotConverter
    {
        // 작업 확정 뒤나 조회 때 한 번 만드는 메시지라 배열을 새로 만든다. null 배열과 null 항목은 빈 값으로 둔다

        /****************************************
        *                Methods
        ****************************************/

        public static GrowthSnapshotMessage ToMessage(CharacterGrowthSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            EquipmentView[] sourceEquipment = snapshot.Equipment ?? Array.Empty<EquipmentView>();
            var equipment = new TownEquipmentEntry[CountNonNull(sourceEquipment)];
            int next = 0;
            for (int i = 0; i < sourceEquipment.Length; ++i)
            {
                EquipmentView view = sourceEquipment[i];
                if (view == null)
                {
                    continue;
                }

                equipment[next] = new TownEquipmentEntry(view.ItemId, view.DefinitionId, (byte)view.Slot, (byte)view.Grade,
                    view.IsTraining, view.Origin, view.Enhancement, view.Equipped, view.Locked, view.Bound);
                ++next;
            }

            EnhancementPityView[] sourcePity = snapshot.EnhancementPity ?? Array.Empty<EnhancementPityView>();
            var pity = new TownPityEntry[CountNonNull(sourcePity)];
            next = 0;
            for (int i = 0; i < sourcePity.Length; ++i)
            {
                if (sourcePity[i] == null)
                {
                    continue;
                }

                pity[next] = new TownPityEntry((byte)sourcePity[i].Slot, sourcePity[i].Stage, sourcePity[i].Failures);
                ++next;
            }

            TalismanProgressView[] sourceTalismans = snapshot.Talismans ?? Array.Empty<TalismanProgressView>();
            var talismans = new TownTalismanEntry[CountNonNull(sourceTalismans)];
            next = 0;
            for (int i = 0; i < sourceTalismans.Length; ++i)
            {
                if (sourceTalismans[i] == null)
                {
                    continue;
                }

                talismans[next] = new TownTalismanEntry(sourceTalismans[i].EffectId, sourceTalismans[i].Level,
                    sourceTalismans[i].RegisterFailures);
                ++next;
            }

            TalismanLoadoutEntry[] sourceLoadout = snapshot.Loadout ?? Array.Empty<TalismanLoadoutEntry>();
            var loadout = new TownLoadoutEntry[CountNonNull(sourceLoadout)];
            next = 0;
            for (int i = 0; i < sourceLoadout.Length; ++i)
            {
                if (sourceLoadout[i] == null)
                {
                    continue;
                }

                loadout[next] = new TownLoadoutEntry(sourceLoadout[i].SkillId, sourceLoadout[i].Tier, sourceLoadout[i].EffectId);
                ++next;
            }

            GrowthItemView[] sourceItems = snapshot.AccountItems ?? Array.Empty<GrowthItemView>();
            var items = new TownItemEntry[CountNonNull(sourceItems)];
            next = 0;
            for (int i = 0; i < sourceItems.Length; ++i)
            {
                if (sourceItems[i] == null)
                {
                    continue;
                }

                items[next] = new TownItemEntry(sourceItems[i].DefinitionId, sourceItems[i].BindingKind, sourceItems[i].Quantity,
                    sourceItems[i].Reserved);
                ++next;
            }

            int[] training = (snapshot.Training != null) ? (int[])snapshot.Training.Clone() : Array.Empty<int>();
            return new GrowthSnapshotMessage(snapshot.ClassId, snapshot.LoadoutRevision, snapshot.Essence, snapshot.Shards, training,
                equipment, pity, talismans, loadout, items, snapshot.StorageUsed, snapshot.StorageCapacity, snapshot.ContentVersion);
        }

        public static LifeSnapshotMessage ToMessage(LifeSnapshotResponse snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            LifePotionStock[] sourcePotions = snapshot.Potions ?? Array.Empty<LifePotionStock>();
            var potions = new TownItemEntry[CountNonNull(sourcePotions)];
            int next = 0;
            for (int i = 0; i < sourcePotions.Length; ++i)
            {
                if (sourcePotions[i] == null)
                {
                    continue;
                }

                potions[next] = new TownItemEntry(sourcePotions[i].DefinitionId, null, sourcePotions[i].Quantity,
                    sourcePotions[i].Reserved);
                ++next;
            }

            LifeHerbStock herb = snapshot.Herb;
            return new LifeSnapshotMessage(snapshot.ActionValue, snapshot.ActionMax, snapshot.NextRecoverAtUnixMs, herb?.DefinitionId,
                (herb != null) ? herb.Quantity : 0, (herb != null) ? herb.Cap : 0, potions);
        }

        private static int CountNonNull<T>(T[] values) where T : class
        {
            int count = 0;
            for (int i = 0; i < values.Length; ++i)
            {
                if (values[i] != null)
                {
                    ++count;
                }
            }

            return count;
        }
    }
}
