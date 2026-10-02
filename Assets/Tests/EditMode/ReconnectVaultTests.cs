// System
using System.Collections.Generic;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class ReconnectVaultTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    CAPACITY         = 2;
        private const int    PARTICIPANT_A    = 1;
        private const int    PARTICIPANT_B    = 2;
        private const int    PARTICIPANT_C    = 3;
        private const double DISCONNECT_TIME  = 50.0;
        private const double GRACE_SECONDS    = 120.0;
        private const double INSIDE_GRACE     = DISCONNECT_TIME + GRACE_SECONDS - 1.0;
        private const double AFTER_GRACE      = DISCONNECT_TIME + GRACE_SECONDS;
        private const double DEATH_TIME       = 45.0;
        private const double NEXT_ATTACK_TIME = 52.5;
        private const float  HURT_HEALTH      = 37f;
        private const uint   LAST_SEQUENCE    = 812;
        private const int    VIOLATIONS       = 4;
        private const double VIOLATION_START  = 48.0;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryTake_InsideGrace_RestoresDeadStateAndCooldown()
        {
            var vault = new ReconnectVault(CAPACITY);
            var dead  = new ParticipantCombatSnapshot(0f, EParticipantLifeState.Dead, DEATH_TIME, NEXT_ATTACK_TIME,
                LAST_SEQUENCE, VIOLATIONS, VIOLATION_START);
            vault.Store(PARTICIPANT_A, in dead, AFTER_GRACE, out _);

            Assert.IsTrue(vault.TryTake(PARTICIPANT_A, INSIDE_GRACE, out ParticipantCombatSnapshot restored));
            Assert.AreEqual(EParticipantLifeState.Dead, restored.LifeState);
            Assert.AreEqual(0f, restored.Health);
            Assert.AreEqual(DEATH_TIME, restored.DeathTime);
            Assert.AreEqual(NEXT_ATTACK_TIME, restored.NextAttackTime);
            Assert.AreEqual(LAST_SEQUENCE, restored.LastSequence);
            Assert.AreEqual(VIOLATIONS, restored.ViolationCount);
            Assert.AreEqual(VIOLATION_START, restored.ViolationWindowStart);
            Assert.AreEqual(0, vault.Count);
        }

        [Test]
        public void TryTake_InsideGrace_KeepsReducedHealth()
        {
            var vault = new ReconnectVault(CAPACITY);
            var hurt  = new ParticipantCombatSnapshot(HURT_HEALTH, EParticipantLifeState.Alive, 0.0, NEXT_ATTACK_TIME,
                LAST_SEQUENCE, 0, 0.0);
            vault.Store(PARTICIPANT_A, in hurt, AFTER_GRACE, out _);

            vault.TryTake(PARTICIPANT_A, INSIDE_GRACE, out ParticipantCombatSnapshot restored);

            Assert.AreEqual(HURT_HEALTH, restored.Health);
            Assert.AreEqual(EParticipantLifeState.Alive, restored.LifeState);
        }

        [Test]
        public void TryTake_AfterGrace_ReturnsFalseAndCollectExpiredDiscards()
        {
            var vault   = new ReconnectVault(CAPACITY);
            var expired = new List<int>();
            vault.Store(PARTICIPANT_A, CreateSnapshot(), AFTER_GRACE, out _);

            Assert.IsFalse(vault.TryTake(PARTICIPANT_A, AFTER_GRACE, out _));
            Assert.AreEqual(1, vault.CollectExpired(AFTER_GRACE, expired));
            Assert.AreEqual(PARTICIPANT_A, expired[0]);
            Assert.AreEqual(0, vault.Count);
        }

        [Test]
        public void CollectExpired_InsideGrace_KeepsRecord()
        {
            var vault   = new ReconnectVault(CAPACITY);
            var expired = new List<int>();
            vault.Store(PARTICIPANT_A, CreateSnapshot(), AFTER_GRACE, out _);

            Assert.AreEqual(0, vault.CollectExpired(INSIDE_GRACE, expired));
            Assert.IsTrue(vault.Contains(PARTICIPANT_A));
        }

        [Test]
        public void Store_AtCapacity_EvictsEarliestExpiry()
        {
            var vault = new ReconnectVault(CAPACITY);
            vault.Store(PARTICIPANT_A, CreateSnapshot(), AFTER_GRACE, out _);
            vault.Store(PARTICIPANT_B, CreateSnapshot(), AFTER_GRACE + 1.0, out _);

            Assert.IsTrue(vault.Store(PARTICIPANT_C, CreateSnapshot(), AFTER_GRACE + 2.0, out int evicted));
            Assert.AreEqual(PARTICIPANT_A, evicted);
            Assert.IsFalse(vault.Contains(PARTICIPANT_A));
            Assert.IsTrue(vault.Contains(PARTICIPANT_C));
        }

        [Test]
        public void Store_SameParticipant_Overwrites()
        {
            var vault = new ReconnectVault(CAPACITY);
            vault.Store(PARTICIPANT_A, CreateSnapshot(), AFTER_GRACE, out _);

            Assert.IsFalse(vault.Store(PARTICIPANT_A, CreateSnapshot(), AFTER_GRACE, out _));
            Assert.AreEqual(1, vault.Count);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static ParticipantCombatSnapshot CreateSnapshot()
        {
            return new ParticipantCombatSnapshot(HURT_HEALTH, EParticipantLifeState.Alive, 0.0, NEXT_ATTACK_TIME,
                LAST_SEQUENCE, 0, 0.0);
        }
    }
}
