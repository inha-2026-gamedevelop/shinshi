// System
using System;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class LootRollerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    REPEAT_COUNT   = 1000;
        private const string SESSION_ID     = "session_forest_01";
        private const string PARTICIPANT_ID = "participant_01";
        private static readonly string[] G_Effects = { "T01-1A", "T01-1B", "T01-2A" };
        private static readonly byte[]   G_Salt    = CreateSalt(1);

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Roll_SameTupleAndRules_RepeatsExactResult()
        {
            var            rule     = new EnemyLootRule(new RewardCatalogSO.EnemyDropSpec("enemy_melee", 5, 0.4f, 1, 0.6f, 1));
            LootRollResult expected = LootRoller.Roll(G_Salt, SESSION_ID, PARTICIPANT_ID, 7, in rule, G_Effects);
            for (int i = 0; i < REPEAT_COUNT; ++i)
            {
                LootRollResult actual = LootRoller.Roll(G_Salt, SESSION_ID, PARTICIPANT_ID, 7, in rule, G_Effects);
                Assert.AreEqual(expected.ExtraEssence, actual.ExtraEssence);
                Assert.AreEqual(expected.TicketQuantity, actual.TicketQuantity);
                Assert.AreEqual(expected.TalismanQuantity, actual.TalismanQuantity);
                Assert.AreEqual(expected.TalismanEffectId, actual.TalismanEffectId);
            }
        }

        [Test]
        public void Roll_ZeroAndOneChance_RespectBoundaries()
        {
            var zero = new EnemyLootRule(new RewardCatalogSO.EnemyDropSpec("enemy_melee", 5, 0f, 2, 0f, 3));
            var one  = new EnemyLootRule(new RewardCatalogSO.EnemyDropSpec("enemy_melee", 5, 1f, 2, 1f, 3));
            for (int i = 1; i <= REPEAT_COUNT; ++i)
            {
                LootRollResult noDrop = LootRoller.Roll(G_Salt, SESSION_ID, PARTICIPANT_ID, i, in zero, null);
                LootRollResult drop   = LootRoller.Roll(G_Salt, SESSION_ID, PARTICIPANT_ID, i, in one, G_Effects);
                Assert.AreEqual(5, noDrop.ExtraEssence);
                Assert.AreEqual(0, noDrop.TicketQuantity);
                Assert.AreEqual(0, noDrop.TalismanQuantity);
                Assert.IsNull(noDrop.TalismanEffectId);
                Assert.AreEqual(2, drop.TicketQuantity);
                Assert.AreEqual(3, drop.TalismanQuantity);
                CollectionAssert.Contains(G_Effects, drop.TalismanEffectId);
            }
        }

        [Test]
        public void Roll_DifferentKillSequences_ProduceIndependentOutcomes()
        {
            var    rule         = new EnemyLootRule(new RewardCatalogSO.EnemyDropSpec("enemy_melee", 5, 0.5f, 1, 1f, 1));
            bool   ticketSeen   = false;
            bool   noTicketSeen = false;
            bool[] effectsSeen  = new bool[G_Effects.Length];
            for (int i = 1; i <= REPEAT_COUNT; ++i)
            {
                LootRollResult roll = LootRoller.Roll(G_Salt, SESSION_ID, PARTICIPANT_ID, i, in rule, G_Effects);
                ticketSeen |= roll.TicketQuantity == 1;
                noTicketSeen |= roll.TicketQuantity == 0;
                effectsSeen[Array.IndexOf(G_Effects, roll.TalismanEffectId)] = true;
            }

            Assert.IsTrue(ticketSeen);
            Assert.IsTrue(noTicketSeen);
            CollectionAssert.DoesNotContain(effectsSeen, false);
        }

        [Test]
        public void Roll_InvalidInput_RejectsBeforeDrawing()
        {
            var rule = new EnemyLootRule(new RewardCatalogSO.EnemyDropSpec("enemy_melee", 5, 1f, 1, 1f, 1));
            Assert.Throws<ArgumentException>(() => LootRoller.Roll(G_Salt, "", PARTICIPANT_ID, 1, in rule, G_Effects));
            Assert.Throws<ArgumentException>(() => LootRoller.Roll(G_Salt, SESSION_ID, "", 1, in rule, G_Effects));
            Assert.Throws<ArgumentOutOfRangeException>(() => LootRoller.Roll(G_Salt, SESSION_ID, PARTICIPANT_ID, 0, in rule, G_Effects));
            Assert.Throws<ArgumentException>(() => LootRoller.Roll(G_Salt, SESSION_ID, PARTICIPANT_ID, 1, in rule, null));
            Assert.Throws<ArgumentException>(() => new EnemyLootRule(
                new RewardCatalogSO.EnemyDropSpec("enemy_melee", 5, float.NaN, 1, 1f, 1)));
        }

        [Test]
        public void Roll_DifferentSalt_ChangesOutcomesForSameTuple()
        {
            var    rule        = new EnemyLootRule(new RewardCatalogSO.EnemyDropSpec("enemy_melee", 5, 0.5f, 1, 1f, 1));
            byte[] otherSalt   = CreateSalt(2);
            int    differences = 0;
            for (int i = 1; i <= REPEAT_COUNT; ++i)
            {
                LootRollResult first  = LootRoller.Roll(G_Salt, SESSION_ID, PARTICIPANT_ID, i, in rule, G_Effects);
                LootRollResult second = LootRoller.Roll(otherSalt, SESSION_ID, PARTICIPANT_ID, i, in rule, G_Effects);
                if ((first.TicketQuantity != second.TicketQuantity) || (first.TalismanEffectId != second.TalismanEffectId))
                {
                    ++differences;
                }
            }

            // 출정 ID, 참가자, 처치 순번만 알아서는 결과를 미리 계산할 수 없어야 한다
            Assert.Greater(differences, REPEAT_COUNT / 4);
        }

        [Test]
        public void Roll_MissingOrShortSalt_Rejects()
        {
            var rule = new EnemyLootRule(new RewardCatalogSO.EnemyDropSpec("enemy_melee", 5, 1f, 1, 1f, 1));
            Assert.Throws<ArgumentException>(() => LootRoller.Roll(null, SESSION_ID, PARTICIPANT_ID, 1, in rule, G_Effects));
            Assert.Throws<ArgumentException>(() => LootRoller.Roll(new byte[Constants.Session.LOOT_SALT_BYTES - 1], SESSION_ID,
                PARTICIPANT_ID, 1, in rule, G_Effects));
        }

        [Test]
        public void CreateSalt_TwoCalls_ReturnDifferentFullLengthSecrets()
        {
            byte[] first  = LootRoller.CreateSalt();
            byte[] second = LootRoller.CreateSalt();

            Assert.AreEqual(Constants.Session.LOOT_SALT_BYTES, first.Length);
            CollectionAssert.AreNotEqual(first, second);
        }

        [Test]
        public void Roll_WarmedRepeatedCalls_AllocatesZeroBytes()
        {
            var rule = new EnemyLootRule(new RewardCatalogSO.EnemyDropSpec("enemy_melee", 5, 1f, 1, 1f, 1));
            LootRoller.Roll(G_Salt, SESSION_ID, PARTICIPANT_ID, 1, in rule, G_Effects);
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 1; i <= REPEAT_COUNT; ++i)
            {
                LootRoller.Roll(G_Salt, SESSION_ID, PARTICIPANT_ID, i, in rule, G_Effects);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
        }

        private static byte[] CreateSalt(byte seed)
        {
            var salt = new byte[Constants.Session.LOOT_SALT_BYTES];
            for (int i = 0; i < salt.Length; ++i)
            {
                salt[i] = (byte)(seed + i);
            }

            return salt;
        }
    }
}
