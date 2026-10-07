// System
using System.Collections.Generic;
using System.IO;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class CombatMeterTests
    {
        /****************************************
        *                Tests (직렬화 왕복)
        ****************************************/

        [Test]
        public void SelfMessage_RoundTrip_KeepsAllFields()
        {
            var slots = new[]
            {
                new CombatMeterSlotStat(10f, 3, 7f),
                new CombatMeterSlotStat(0f, 0, 0f)
            };
            var value = new CombatMeterSelfMessage(1, 12.5f, 30f, 25f, 100f, 40f, 15f, 2, slots);

            var writer = new NetworkWriter();
            writer.WriteCombatMeterSelfMessage(value);
            var                    reader = new NetworkReader(writer.ToArraySegment());
            CombatMeterSelfMessage read   = reader.ReadCombatMeterSelfMessage();

            Assert.AreEqual(1, read.Phase);
            Assert.AreEqual(12.5f, read.PhaseElapsedSeconds);
            Assert.AreEqual(30f, read.EncounterElapsedSeconds);
            Assert.AreEqual(25f, read.ActiveSeconds);
            Assert.AreEqual(100f, read.EncounterDamage);
            Assert.AreEqual(40f, read.PhaseDamage);
            Assert.AreEqual(15f, read.DamageTaken);
            Assert.AreEqual(2, read.GuardedHits);
            Assert.AreEqual(2, read.Slots.Length);
            Assert.AreEqual(10f, read.Slots[0].Damage);
            Assert.AreEqual((ushort)3, read.Slots[0].Hits);
            Assert.AreEqual(7f, read.Slots[0].MaxHit);
        }

        [Test]
        public void PartyMessage_RoundTrip_KeepsEntries()
        {
            var entries = new[]
            {
                new CombatMeterPartyEntry(11, 50f, 20f),
                new CombatMeterPartyEntry(12, 30f, 10f)
            };
            var value = new CombatMeterPartyMessage(2, 5f, 40f, entries);

            var writer = new NetworkWriter();
            writer.WriteCombatMeterPartyMessage(value);
            var                     reader = new NetworkReader(writer.ToArraySegment());
            CombatMeterPartyMessage read   = reader.ReadCombatMeterPartyMessage();

            Assert.AreEqual(2, read.Phase);
            Assert.AreEqual(5f, read.PhaseElapsedSeconds);
            Assert.AreEqual(40f, read.EncounterElapsedSeconds);
            Assert.AreEqual(2, read.Entries.Length);
            Assert.AreEqual(11, read.Entries[0].CombatId);
            Assert.AreEqual(50f, read.Entries[0].EncounterDamage);
            Assert.AreEqual(20f, read.Entries[0].PhaseDamage);
            Assert.AreEqual(12, read.Entries[1].CombatId);
        }

        [Test]
        public void FinalSelfMessage_RoundTrip_KeepsAllFields()
        {
            var slots  = new[] { new CombatMeterSlotStat(5f, 1, 5f) };
            var phases = new[] { new CombatMeterPhaseStat(1, 30f, 100f), new CombatMeterPhaseStat(2, 20f, 80f) };
            var top    = new[] { new CombatMeterSourceAmount("boss_wolbaek_p1", 40f) };
            var deaths = new[] { new CombatMeterDeathEntry("boss_wolbaek_p1", 20f, 1.5f) };
            var value  = new CombatMeterFinalSelfMessage(7, 50f, 45f, 180f, 60f, 3, 1, 2, 4, slots, phases, top, deaths);

            var writer = new NetworkWriter();
            writer.WriteCombatMeterFinalSelfMessage(value);
            var                         reader = new NetworkReader(writer.ToArraySegment());
            CombatMeterFinalSelfMessage read   = reader.ReadCombatMeterFinalSelfMessage();

            Assert.AreEqual(7, read.SessionEpoch);
            Assert.AreEqual(50f, read.EncounterSeconds);
            Assert.AreEqual(45f, read.ActiveSeconds);
            Assert.AreEqual(180f, read.EncounterDamage);
            Assert.AreEqual(60f, read.DamageTaken);
            Assert.AreEqual(3, read.GuardedHits);
            Assert.AreEqual(1, read.Deaths);
            Assert.AreEqual(2, read.Revives);
            Assert.AreEqual(4, read.PotionsUsed);
            Assert.AreEqual(1, read.Slots.Length);
            Assert.AreEqual(2, read.Phases.Length);
            Assert.AreEqual(1, read.TopDamageTaken.Length);
            Assert.AreEqual("boss_wolbaek_p1", read.TopDamageTaken[0].SourceId);
            Assert.AreEqual(1, read.LastDeathRecap.Length);
            Assert.AreEqual(1.5f, read.LastDeathRecap[0].SecondsBeforeDeath);
        }

        [Test]
        public void ResultMessage_RoundTrip_KeepsAllFields()
        {
            var entries = new[]
            {
                new CombatMeterResultEntry(1, "min", 200f, 30f, 0, 1),
                new CombatMeterResultEntry(2, "gi", 150f, 50f, 1, 0)
            };
            var totals = new[] { new CombatMeterPhaseStat(1, 40f, 300f) };
            var value  = new CombatMeterResultMessage(9, ESessionEndReason.Success, 40f, entries, totals);

            var writer = new NetworkWriter();
            writer.WriteCombatMeterResultMessage(value);
            var                      reader = new NetworkReader(writer.ToArraySegment());
            CombatMeterResultMessage read   = reader.ReadCombatMeterResultMessage();

            Assert.AreEqual(9, read.SessionEpoch);
            Assert.AreEqual(ESessionEndReason.Success, read.Reason);
            Assert.AreEqual(40f, read.EncounterSeconds);
            Assert.AreEqual(2, read.Entries.Length);
            Assert.AreEqual("min", read.Entries[0].DisplayName);
            Assert.AreEqual(1, read.PhaseTotals.Length);
        }

        /****************************************
        *                Tests (배열 상한, 음수 길이 방어)
        ****************************************/

        [Test]
        public void PartyMessage_EntryCountOverCap_Throws()
        {
            var writer = new NetworkWriter();
            writer.WriteByte(1);
            writer.WriteFloat(5f);
            writer.WriteFloat(40f);
            writer.WriteInt(Constants.CombatMeter.MAX_PARTICIPANTS + 1);

            var reader = new NetworkReader(writer.ToArraySegment());

            Assert.Throws<InvalidDataException>(() => reader.ReadCombatMeterPartyMessage());
        }

        [Test]
        public void PartyMessage_NegativeEntryCount_Throws()
        {
            var writer = new NetworkWriter();
            writer.WriteByte(1);
            writer.WriteFloat(5f);
            writer.WriteFloat(40f);
            writer.WriteInt(-1);

            var reader = new NetworkReader(writer.ToArraySegment());

            Assert.Throws<InvalidDataException>(() => reader.ReadCombatMeterPartyMessage());
        }

        [Test]
        public void SelfMessage_SlotCountOverCap_Throws()
        {
            var writer = new NetworkWriter();
            writer.WriteByte(1);
            writer.WriteFloat(1f);
            writer.WriteFloat(1f);
            writer.WriteFloat(1f);
            writer.WriteFloat(1f);
            writer.WriteFloat(1f);
            writer.WriteFloat(1f);
            writer.WriteUShort(0);
            writer.WriteInt(Constants.CombatMeter.SLOT_COUNT + 1);

            var reader = new NetworkReader(writer.ToArraySegment());

            Assert.Throws<InvalidDataException>(() => reader.ReadCombatMeterSelfMessage());
        }

        /****************************************
        *                Tests (CombatMeterViewRules 정렬)
        ****************************************/

        [Test]
        public void BuildPartyRowOrder_PutsLocalFirst_ThenReceivedOrder()
        {
            var entries = new[]
            {
                new CombatMeterPartyEntry(10, 0f, 0f),
                new CombatMeterPartyEntry(20, 0f, 0f),
                new CombatMeterPartyEntry(30, 0f, 0f)
            };
            var order = new List<int>();

            CombatMeterViewRules.BuildPartyRowOrder(20, entries, order);

            Assert.AreEqual(new[] { 1, 0, 2 }, order.ToArray());
        }

        [Test]
        public void BuildPartyRowOrder_LocalNotFound_KeepsReceivedOrder()
        {
            var entries = new[]
            {
                new CombatMeterPartyEntry(10, 0f, 0f),
                new CombatMeterPartyEntry(20, 0f, 0f)
            };
            var order = new List<int>();

            CombatMeterViewRules.BuildPartyRowOrder(99, entries, order);

            Assert.AreEqual(new[] { 0, 1 }, order.ToArray());
        }

        [Test]
        public void BuildResultRowOrder_PutsLocalFirst()
        {
            var entries = new[]
            {
                new CombatMeterResultEntry(10, "a", 0f, 0f, 0, 0),
                new CombatMeterResultEntry(20, "b", 0f, 0f, 0, 0)
            };
            var order = new List<int>();

            CombatMeterViewRules.BuildResultRowOrder(20, entries, order);

            Assert.AreEqual(new[] { 1, 0 }, order.ToArray());
        }

        /****************************************
        *                Tests (DPS, 점유율, "-")
        ****************************************/

        [Test]
        public void TryComputeDps_SecondsAtOrAboveMin_ReturnsDamageOverSeconds()
        {
            Assert.IsTrue(CombatMeterViewRules.TryComputeDps(100f, 2f, out float dps));
            Assert.AreEqual(50f, dps);
        }

        [Test]
        public void TryComputeDps_SecondsBelowMin_ReturnsFalse()
        {
            Assert.IsFalse(CombatMeterViewRules.TryComputeDps(100f, 0.5f, out float dps));
            Assert.AreEqual(0f, dps);
        }

        [Test]
        public void FormatDps_SecondsBelowMin_ReturnsDash()
        {
            Assert.AreEqual("-", CombatMeterViewRules.FormatDps(100f, 0f));
        }

        [Test]
        public void FormatDps_SecondsAtMin_ReturnsFormattedNumber()
        {
            Assert.AreEqual(CombatMeterViewRules.FormatDamage(100f), CombatMeterViewRules.FormatDps(100f, 1f));
        }

        [Test]
        public void ComputeShare_PartyTotalZero_ReturnsZero()
        {
            Assert.AreEqual(0f, CombatMeterViewRules.ComputeShare(10f, 0f));
        }

        [Test]
        public void ComputeShare_HalfOfTotal_ReturnsFifty()
        {
            Assert.AreEqual(50f, CombatMeterViewRules.ComputeShare(50f, 100f), 0.0001f);
        }
    }
}
