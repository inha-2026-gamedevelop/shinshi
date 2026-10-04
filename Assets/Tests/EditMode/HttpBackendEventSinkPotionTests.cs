// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class HttpBackendEventSinkPotionTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SESSION_ID     = "6f1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string PARTICIPANT_ID = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbccdd";
        private const string POTION_ID      = "item_potion_small";
        private const string BINDING        = "Account";
        private const string DEV_KEY        = "dev-player";

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void CreateRequest_PotionUsed_SendsPotionUsedWithDefinitionAndBinding()
        {
            SessionEventEntry entry = Single(new SessionEventData(ESessionEventType.PotionUsed, 1.0, Key(),
                definitionId: POTION_ID, bindingKind: BINDING), 1);

            Assert.AreEqual(GameSessionCodes.EventKind.POTION_USED, entry.EventKind);
            Assert.AreEqual(PARTICIPANT_ID, entry.ParticipantId);
            Assert.AreEqual(POTION_ID, entry.DefinitionId);
            Assert.AreEqual(BINDING, entry.BindingKind);
            Assert.IsTrue(string.IsNullOrEmpty(entry.PayloadJson));
        }

        [Test]
        public void CreateRequest_PotionReverted_SendsRevertWithOriginalEventId()
        {
            var events = new[]
            {
                new SessionEvent(4, new SessionEventData(ESessionEventType.PotionUsed, 1.0, Key(), definitionId: POTION_ID,
                    bindingKind: BINDING)),
                new SessionEvent(5, new SessionEventData(ESessionEventType.PotionUseReverted, 2.0, Key(), revertsSequence: 4))
            };

            SessionEventBatchRequest body = Body(events);

            Assert.AreEqual(GameSessionCodes.EventKind.POTION_USE_REVERTED, body.Events[1].EventKind);
            Assert.AreEqual(PARTICIPANT_ID, body.Events[1].ParticipantId);
            Assert.AreEqual(SessionContractIds.CreateEventId(SESSION_ID, 4), body.Events[1].RevertsEventId);
            Assert.AreEqual(body.Events[0].EventId, body.Events[1].RevertsEventId);
            Assert.IsTrue(string.IsNullOrEmpty(body.Events[1].DefinitionId));
        }

        [Test]
        public void CreateRequest_PotionUsedWithoutBinding_FallsBackToDiagnosticLoot()
        {
            SessionEventEntry entry = Single(new SessionEventData(ESessionEventType.PotionUsed, 1.0, Key(), definitionId: POTION_ID),
                1);

            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, entry.EventKind);
            StringAssert.Contains(POTION_ID, entry.PayloadJson);
        }

        [Test]
        public void CreateRequest_PotionUsedWithDevKey_FallsBackToDiagnosticLoot()
        {
            SessionEventEntry entry = Single(new SessionEventData(ESessionEventType.PotionUsed, 1.0, DEV_KEY,
                definitionId: POTION_ID, bindingKind: BINDING), 1);

            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, entry.EventKind);
            Assert.IsNull(entry.ParticipantId);
        }

        [Test]
        public void CreateRequest_RevertPointingForward_FallsBackToDiagnosticLoot()
        {
            SessionEventEntry entry = Single(new SessionEventData(ESessionEventType.PotionUseReverted, 1.0, Key(),
                revertsSequence: 9), 3);

            Assert.AreEqual(GameSessionCodes.EventKind.LOOT, entry.EventKind);
            Assert.IsTrue(string.IsNullOrEmpty(entry.RevertsEventId));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static string Key()
        {
            return Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + PARTICIPANT_ID;
        }

        private static SessionEventEntry Single(SessionEventData data, long sequence)
        {
            SessionEventBatchRequest body = Body(new[] { new SessionEvent(sequence, in data) });
            Assert.AreEqual(1, body.Events.Length);
            return body.Events[0];
        }

        private static SessionEventBatchRequest Body(SessionEvent[] events)
        {
            BackendRequest request = HttpBackendEventSink.CreateRequest(SESSION_ID, events, events.Length);
            return BackendJson.Deserialize<SessionEventBatchRequest>(request.BodyJson);
        }
    }
}
