// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class SessionEventDataTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SESSION_ID     = "6f1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string PARTICIPANT_ID = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbccdd";
        private const string DEV_KEY        = "dev-player";
        private const string SOURCE_ID      = "enemy-source";
        private const string POTION_ID      = "item_potion_small";
        private const string BINDING        = "Account";
        private const string HASH           = "settings-hash";
        private const double TIME           = 12.5;
        private const int    WAVE           = 3;
        private const long   KILL_SEQUENCE  = 7;
        private const long   AMOUNT         = 40;
        private const int    SOURCE_KIND    = 2;
        private const int    QUEUE_CAPACITY = 4;
        private const int    BATCH_CAPACITY = 2;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Factories_SameValuesAsConstructor_ProduceSameData()
        {
            var loot = new LootRollResult(5, 1, 2, "effect");
            var grants = new SettlementGrant[0];

            AssertSame(new SessionEventData(ESessionEventType.ParticipantJoined, TIME, participantId: Key(), isBossSession: true),
                SessionEventData.ParticipantJoined(TIME, Key(), true));
            AssertSame(new SessionEventData(ESessionEventType.ParticipantLeft, TIME, participantId: Key(), isBossSession: true),
                SessionEventData.ParticipantDisconnected(TIME, Key(), true));
            AssertSame(new SessionEventData(ESessionEventType.ParticipantLeft, TIME, participantId: Key(),
                reason: ESessionEndReason.Abandon, isBossSession: true),
                SessionEventData.ParticipantClosed(TIME, Key(), ESessionEndReason.Abandon, true));
            AssertSame(new SessionEventData(ESessionEventType.ParticipantSettled, TIME, participantId: Key(),
                reason: ESessionEndReason.Abandon, isBossSession: true),
                SessionEventData.ParticipantSettled(TIME, Key(), ESessionEndReason.Abandon, true));
            AssertSame(new SessionEventData(ESessionEventType.EnemyKilled, TIME, participantId: Key(), sourceId: SOURCE_ID,
                killerParticipantId: DEV_KEY, killSequence: KILL_SEQUENCE, waveIndex: WAVE, loot: loot),
                SessionEventData.EnemyKilled(TIME, Key(), SOURCE_ID, DEV_KEY, KILL_SEQUENCE, WAVE, in loot));
            AssertSame(new SessionEventData(ESessionEventType.WaveCleared, TIME, participantId: Key(), waveIndex: WAVE),
                SessionEventData.WaveCleared(TIME, Key(), WAVE));
            AssertSame(new SessionEventData(ESessionEventType.SessionEnded, TIME, reason: ESessionEndReason.Success,
                isBossSession: true, grants: grants),
                SessionEventData.SessionEnded(TIME, ESessionEndReason.Success, true, grants));
            AssertSame(new SessionEventData(ESessionEventType.EssenceAccrued, TIME, participantId: Key(),
                killSequence: KILL_SEQUENCE, waveIndex: WAVE, amount: AMOUNT),
                SessionEventData.EssenceAccrued(TIME, Key(), AMOUNT, WAVE, KILL_SEQUENCE));
            AssertSame(new SessionEventData(ESessionEventType.BaseEssenceAccrued, TIME, participantId: Key(),
                killSequence: KILL_SEQUENCE, waveIndex: WAVE, amount: AMOUNT),
                SessionEventData.BaseEssenceAccrued(TIME, Key(), AMOUNT, WAVE, KILL_SEQUENCE));
            AssertSame(new SessionEventData(ESessionEventType.ExperienceGranted, TIME, participantId: Key(), sourceId: SOURCE_ID,
                amount: AMOUNT, experienceSourceKind: SOURCE_KIND, experienceSettingsHash: HASH),
                SessionEventData.ExperienceGranted(TIME, Key(), SOURCE_ID, AMOUNT, SOURCE_KIND, HASH));
            AssertSame(new SessionEventData(ESessionEventType.PotionUsed, TIME, participantId: Key(), definitionId: POTION_ID,
                bindingKind: BINDING),
                SessionEventData.PotionUsed(TIME, Key(), POTION_ID, BINDING));
            AssertSame(new SessionEventData(ESessionEventType.PotionUseReverted, TIME, participantId: Key(), revertsSequence: 4),
                SessionEventData.PotionUseReverted(TIME, Key(), 4));
        }

        [Test]
        public void Factories_InvalidValues_DoNotThrow()
        {
            Assert.DoesNotThrow(() => SessionEventData.PotionUsed(TIME, null, null, null));
            Assert.DoesNotThrow(() => SessionEventData.PotionUseReverted(TIME, null, -1));
            Assert.DoesNotThrow(() => SessionEventData.EssenceAccrued(TIME, null, -AMOUNT, -1, -1));
            Assert.DoesNotThrow(() => SessionEventData.SessionEnded(TIME, ESessionEndReason.Success, false, null));
        }

        [Test]
        public void CreateRequest_SessionEndedFactory_SendsSessionEndedWithSettlementOperation()
        {
            SessionEventEntry entry = Single(SessionEventData.SessionEnded(TIME, ESessionEndReason.Success, true, null), 9);

            Assert.AreEqual(GameSessionCodes.EventKind.SESSION_ENDED, entry.EventKind);
            Assert.AreEqual((int)ESessionEndReason.Success, entry.EndReason);
            Assert.AreEqual(SessionContractIds.CreateSettlementOperationId(SESSION_ID), entry.SettlementOperationId);
        }

        [Test]
        public void CreateRequest_ServiceCancelledEnd_FallsBackToDiagnosticLoot()
        {
            SessionEventEntry entry = Single(SessionEventData.SessionEnded(TIME, ESessionEndReason.ServiceCancelled, false, null), 9);

            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, entry.EventKind);
            StringAssert.Contains(ESessionEventType.SessionEnded.ToString(), entry.PayloadJson);
        }

        [Test]
        public void CreateRequest_ParticipantSettledFactory_SendsDepartedOnlyForDepartReasons()
        {
            SessionEventEntry abandon = Single(SessionEventData.ParticipantSettled(TIME, Key(), ESessionEndReason.Abandon, false), 2);
            SessionEventEntry expired = Single(
                SessionEventData.ParticipantSettled(TIME, Key(), ESessionEndReason.DisconnectExpired, false), 2);
            SessionEventEntry success = Single(SessionEventData.ParticipantSettled(TIME, Key(), ESessionEndReason.Success, false), 2);
            SessionEventEntry devKey  = Single(SessionEventData.ParticipantSettled(TIME, DEV_KEY, ESessionEndReason.Abandon, false), 2);

            Assert.AreEqual(GameSessionCodes.EventKind.PARTICIPANT_DEPARTED, abandon.EventKind);
            Assert.AreEqual(PARTICIPANT_ID, abandon.ParticipantId);
            Assert.AreEqual((int)ESessionEndReason.Abandon, abandon.EndReason);
            Assert.AreEqual(GameSessionCodes.EventKind.PARTICIPANT_DEPARTED, expired.EventKind);
            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, success.EventKind);
            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, devKey.EventKind);
        }

        [Test]
        public void CreateRequest_AccrualFactories_SendAccrualKindsOnlyForPositiveAmountAndApiParticipant()
        {
            SessionEventEntry extra = Single(SessionEventData.EssenceAccrued(TIME, Key(), AMOUNT, WAVE, KILL_SEQUENCE), 3);
            SessionEventEntry baseEssence = Single(SessionEventData.BaseEssenceAccrued(TIME, Key(), AMOUNT, WAVE, 0), 3);
            SessionEventEntry zero = Single(SessionEventData.EssenceAccrued(TIME, Key(), 0, WAVE, KILL_SEQUENCE), 3);
            SessionEventEntry devKey = Single(SessionEventData.BaseEssenceAccrued(TIME, DEV_KEY, AMOUNT, WAVE, 0), 3);

            Assert.AreEqual(GameSessionCodes.EventKind.ESSENCE_ACCRUED, extra.EventKind);
            Assert.AreEqual(AMOUNT, extra.Amount);
            Assert.AreEqual(PARTICIPANT_ID, extra.ParticipantId);
            Assert.AreEqual(GameSessionCodes.EventKind.BASE_ESSENCE_ACCRUED, baseEssence.EventKind);
            Assert.AreEqual(AMOUNT, baseEssence.Amount);
            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, zero.EventKind);
            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, devKey.EventKind);
            Assert.IsNull(devKey.ParticipantId);
        }

        [Test]
        public void CreateRequest_ExperienceGrantedFactory_SendsExperienceEntry()
        {
            SessionEventEntry entry = Single(SessionEventData.ExperienceGranted(TIME, Key(), SOURCE_ID, AMOUNT, SOURCE_KIND, HASH), 5);

            Assert.AreEqual(GameSessionCodes.EventKind.EXPERIENCE_GRANTED, entry.EventKind);
            Assert.AreEqual(PARTICIPANT_ID, entry.ParticipantId);
            Assert.AreEqual(AMOUNT, entry.Amount);
            Assert.AreEqual(SOURCE_KIND, entry.ExperienceSourceKind);
            Assert.AreEqual(SOURCE_ID, entry.ExperienceSourceId);
            Assert.AreEqual(HASH, entry.ExperienceSettingsHash);
        }

        [Test]
        public void CreateRequest_PotionFactories_MatchConstructorBuiltEntries()
        {
            SessionEventEntry fromFactory = Single(SessionEventData.PotionUsed(TIME, Key(), POTION_ID, BINDING), 6);
            SessionEventEntry fromConstructor = Single(new SessionEventData(ESessionEventType.PotionUsed, TIME, Key(),
                definitionId: POTION_ID, bindingKind: BINDING), 6);

            Assert.AreEqual(fromConstructor.EventKind, fromFactory.EventKind);
            Assert.AreEqual(fromConstructor.ParticipantId, fromFactory.ParticipantId);
            Assert.AreEqual(fromConstructor.DefinitionId, fromFactory.DefinitionId);
            Assert.AreEqual(fromConstructor.BindingKind, fromFactory.BindingKind);
            Assert.AreEqual(fromConstructor.EventId, fromFactory.EventId);

            SessionEventEntry missingBinding = Single(SessionEventData.PotionUsed(TIME, Key(), POTION_ID, null), 6);
            SessionEventEntry forwardRevert = Single(SessionEventData.PotionUseReverted(TIME, Key(), 9), 6);
            SessionEventEntry validRevert = Single(SessionEventData.PotionUseReverted(TIME, Key(), 4), 6);

            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, missingBinding.EventKind);
            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, forwardRevert.EventKind);
            Assert.AreEqual(GameSessionCodes.EventKind.POTION_USE_REVERTED, validRevert.EventKind);
            Assert.AreEqual(SessionContractIds.CreateEventId(SESSION_ID, 4), validRevert.RevertsEventId);
        }

        [Test]
        public void CreateRequest_StorageOnlyFactories_SendDiagnosticLootWithStableKeys()
        {
            SessionEventEntry joined = Single(SessionEventData.ParticipantJoined(TIME, Key(), true), 1);
            SessionEventEntry killed = Single(SessionEventData.EnemyKilled(TIME, Key(), SOURCE_ID, DEV_KEY, KILL_SEQUENCE, WAVE,
                new LootRollResult(5, 1, 2, "effect")), 1);

            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, joined.EventKind);
            StringAssert.Contains("ParticipantJoined", joined.PayloadJson);
            StringAssert.Contains("isBossSession", joined.PayloadJson);
            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, killed.EventKind);
            StringAssert.Contains("EnemyKilled", killed.PayloadJson);
            StringAssert.Contains("killSequence", killed.PayloadJson);
            StringAssert.Contains("extraEssence", killed.PayloadJson);
            StringAssert.Contains(SOURCE_ID, killed.PayloadJson);
        }

        [Test]
        public void TryEnqueue_FactoryEvents_AssignSequencesFromOne()
        {
            var queue = new SessionEventQueue(SESSION_ID, new AcknowledgingSink(), QUEUE_CAPACITY, BATCH_CAPACITY);
            SessionEventData joined = SessionEventData.ParticipantJoined(TIME, Key(), false);
            SessionEventData cleared = SessionEventData.WaveCleared(TIME, Key(), WAVE);
            SessionEventData accrued = SessionEventData.EssenceAccrued(TIME, Key(), AMOUNT, WAVE, 0);

            Assert.IsTrue(queue.TryEnqueue(in joined, out long first));
            Assert.IsTrue(queue.TryEnqueue(in cleared, out long second));
            Assert.IsTrue(queue.TryEnqueue(in accrued, out long third));

            Assert.AreEqual(1, first);
            Assert.AreEqual(2, second);
            Assert.AreEqual(3, third);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static string Key()
        {
            return Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + PARTICIPANT_ID;
        }

        private static void AssertSame(SessionEventData expected, SessionEventData actual)
        {
            Assert.AreEqual(expected.Type, actual.Type);
            Assert.AreEqual(expected.ServerTime, actual.ServerTime);
            Assert.AreEqual(expected.ParticipantId, actual.ParticipantId);
            Assert.AreEqual(expected.SourceId, actual.SourceId);
            Assert.AreEqual(expected.KillerParticipantId, actual.KillerParticipantId);
            Assert.AreEqual(expected.KillSequence, actual.KillSequence);
            Assert.AreEqual(expected.WaveIndex, actual.WaveIndex);
            Assert.AreEqual(expected.Reason, actual.Reason);
            Assert.AreEqual(expected.IsBossSession, actual.IsBossSession);
            Assert.AreEqual(expected.Loot.ExtraEssence, actual.Loot.ExtraEssence);
            Assert.AreEqual(expected.Loot.TicketQuantity, actual.Loot.TicketQuantity);
            Assert.AreEqual(expected.Loot.TalismanQuantity, actual.Loot.TalismanQuantity);
            Assert.AreEqual(expected.Loot.TalismanEffectId, actual.Loot.TalismanEffectId);
            Assert.AreEqual(expected.Amount, actual.Amount);
            Assert.AreSame(expected.Grants, actual.Grants);
            Assert.AreEqual(expected.DefinitionId, actual.DefinitionId);
            Assert.AreEqual(expected.BindingKind, actual.BindingKind);
            Assert.AreEqual(expected.RevertsSequence, actual.RevertsSequence);
            Assert.AreEqual(expected.ExperienceSourceKind, actual.ExperienceSourceKind);
            Assert.AreEqual(expected.ExperienceSettingsHash, actual.ExperienceSettingsHash);
        }

        private static SessionEventEntry Single(SessionEventData data, long sequence)
        {
            var events = new[] { new SessionEvent(sequence, in data) };
            BackendRequest request = HttpBackendEventSink.CreateRequest(SESSION_ID, events, events.Length);
            SessionEventBatchRequest body = BackendJson.Deserialize<SessionEventBatchRequest>(request.BodyJson);
            Assert.AreEqual(1, body.Events.Length);
            return body.Events[0];
        }

        private sealed class AcknowledgingSink : IBackendEventSink
        {
            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count, IBackendEventSinkListener listener)
            {
                listener.OnBatchCompleted(attemptId, EEventBatchResult.Committed);
                return true;
            }
        }
    }
}
