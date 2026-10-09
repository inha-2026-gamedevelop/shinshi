// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class ReviveLocationRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float NEXT_ATTACK_TIME = 1f;
        private const float DEATH_TIME       = 4f;

        private static readonly Vector3 G_DeathPosition = new Vector3(12.5f, 1.25f, -7.75f);

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Decide_NormalWithRecordedPosition_UsesDeathPosition()
        {
            Assert.AreEqual(EReviveLocationPlan.DeathPosition, ReviveLocationRules.Decide(false, true, G_DeathPosition));
        }

        [Test]
        public void Decide_BossSession_UsesSafePointEvenWithRecordedPosition()
        {
            Assert.AreEqual(EReviveLocationPlan.SafePoint, ReviveLocationRules.Decide(true, true, G_DeathPosition));
        }

        [Test]
        public void Decide_BossSessionWithoutRecordedPosition_UsesSafePoint()
        {
            Assert.AreEqual(EReviveLocationPlan.SafePoint, ReviveLocationRules.Decide(true, false, Vector3.zero));
        }

        [Test]
        public void Decide_NormalWithoutRecordedPosition_FallsBackToNearbyWalkable()
        {
            Assert.AreEqual(EReviveLocationPlan.NearbyWalkable, ReviveLocationRules.Decide(false, false, G_DeathPosition));
        }

        [Test]
        public void Decide_NormalWithNonFinitePosition_FallsBackToNearbyWalkable()
        {
            Assert.AreEqual(EReviveLocationPlan.NearbyWalkable,
                ReviveLocationRules.Decide(false, true, new Vector3(float.NaN, 0f, 0f)));
            Assert.AreEqual(EReviveLocationPlan.NearbyWalkable,
                ReviveLocationRules.Decide(false, true, new Vector3(0f, float.PositiveInfinity, 0f)));
            Assert.AreEqual(EReviveLocationPlan.NearbyWalkable,
                ReviveLocationRules.Decide(false, true, new Vector3(0f, 0f, float.NegativeInfinity)));
        }

        [Test]
        public void Decide_NormalAtOrigin_IsStillAValidDeathPosition()
        {
            Assert.AreEqual(EReviveLocationPlan.DeathPosition, ReviveLocationRules.Decide(false, true, Vector3.zero));
        }

        [Test]
        public void IsUsablePosition_FiniteValues_ReturnsTrue()
        {
            Assert.IsTrue(ReviveLocationRules.IsUsablePosition(G_DeathPosition));
        }

        [Test]
        public void IsUsablePosition_AnyAxisNonFinite_ReturnsFalse()
        {
            Assert.IsFalse(ReviveLocationRules.IsUsablePosition(new Vector3(float.NaN, 0f, 0f)));
            Assert.IsFalse(ReviveLocationRules.IsUsablePosition(new Vector3(0f, float.NaN, 0f)));
            Assert.IsFalse(ReviveLocationRules.IsUsablePosition(new Vector3(0f, 0f, float.PositiveInfinity)));
        }

        [Test]
        public void Snapshot_CarriesDeathPositionForReconnect()
        {
            var snapshot = new ParticipantCombatSnapshot(0f, EParticipantLifeState.Dead, DEATH_TIME, NEXT_ATTACK_TIME, 0u, 0, 0.0,
                0.0, G_DeathPosition, true);

            Assert.IsTrue(snapshot.HasDeathPosition);
            Assert.AreEqual(G_DeathPosition, snapshot.DeathPosition);
        }

        [Test]
        public void Snapshot_WithoutDeathPosition_ReportsNone()
        {
            var snapshot = new ParticipantCombatSnapshot(0f, EParticipantLifeState.Dead, DEATH_TIME, NEXT_ATTACK_TIME, 0u, 0, 0.0);

            Assert.IsFalse(snapshot.HasDeathPosition);
            Assert.AreEqual(EReviveLocationPlan.NearbyWalkable,
                ReviveLocationRules.Decide(false, snapshot.HasDeathPosition, snapshot.DeathPosition));
        }
    }
}
