// System
using System;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class SessionEventQueueTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SESSION_ID           = "session-event-queue-test";
        private const string PARTICIPANT_ID       = "participant-durable-key";
        private const string SOURCE_ID            = "enemy-source";
        private const string EFFECT_ID            = "talisman-effect";
        private const string QUEUE_WARNING        = "[SessionEventQueue] 이벤트 저장 큐 한계에 도달했다. 저장 대기 이벤트는 보존한다";
        private const string SEND_EXCEPTION_ERROR = "[SessionEventQueue] 백엔드 이벤트 전송 시작 중 예외가 발생했다. 동일 배치를 재시도한다";
        private const string TERMINAL_ERROR       = "[SessionEventQueue] 저장이 확정 거절돼 큐를 멈춘다. 출정은 서비스 장애 취소로 끝난다";
        private const int    CAPACITY             = 4;
        private const int    BATCH_CAPACITY       = 2;
        private const int    WARMUP_ITERATIONS    = 128;
        private const int    MEASURED_ITERATIONS  = 512;
        private const double START_TIME           = 100.0;
        private const double JUST_BEFORE          = 0.01;
        private const double REENTRY_TIME         = 1000.0;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Tick_BatchIntervalAndCapacity_PreservesOrderUntilAck()
        {
            var sink    = new RecordingSink(CAPACITY);
            var queue   = new SessionEventQueue(SESSION_ID, sink, CAPACITY, BATCH_CAPACITY);
            var joined  = new SessionEventData(ESessionEventType.ParticipantJoined, START_TIME,
                participantId: PARTICIPANT_ID);
            var left    = new SessionEventData(ESessionEventType.ParticipantLeft, START_TIME,
                participantId: PARTICIPANT_ID);
            var cleared = new SessionEventData(ESessionEventType.WaveCleared, START_TIME);
            queue.Tick(START_TIME);

            Assert.IsTrue(queue.TryEnqueue(in joined, out long joinedSequence));
            Assert.IsTrue(queue.TryEnqueue(in left, out long leftSequence));
            Assert.IsTrue(queue.TryEnqueue(in cleared, out long clearedSequence));
            Assert.AreEqual(1, joinedSequence);
            Assert.AreEqual(2, leftSequence);
            Assert.AreEqual(3, clearedSequence);
            queue.Tick(START_TIME + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS - JUST_BEFORE);
            Assert.AreEqual(0, sink.AttemptCount);

            double sendTime = START_TIME + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS;
            queue.Tick(sendTime);
            Assert.AreEqual(1, sink.AttemptCount);
            Assert.AreEqual(SESSION_ID, sink.SessionId);
            Assert.AreEqual(SESSION_ID, queue.SessionId);
            Assert.AreEqual(BATCH_CAPACITY, sink.Count);
            Assert.AreEqual(joinedSequence, sink.Snapshot[0].Seq);
            Assert.AreEqual(leftSequence, sink.Snapshot[1].Seq);
            Assert.AreEqual(ESessionEventType.ParticipantJoined, sink.Snapshot[0].Data.Type);
            Assert.AreEqual(PARTICIPANT_ID, sink.Snapshot[0].Data.ParticipantId);
            Assert.AreEqual(3, queue.QueuedCount);

            queue.Tick(sendTime + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS);
            Assert.AreEqual(1, sink.AttemptCount);
            sink.Complete(true);
            Assert.AreEqual(1, queue.QueuedCount);
            Assert.AreEqual(leftSequence, queue.LastAcknowledgedSequence);
            Assert.IsNull(sink.BorrowedBuffer[0].Data.ParticipantId);
            queue.Tick(sendTime + (Constants.Session.EVENT_BATCH_INTERVAL_SECONDS * 2));
            Assert.AreEqual(2, sink.AttemptCount);
            Assert.AreEqual(1, sink.Count);
            Assert.AreEqual(clearedSequence, sink.Snapshot[0].Seq);
        }

        [Test]
        public void OnBatchCompleted_Terminal_StopsSendingAndRejectsNewEvents()
        {
            var sink  = new RecordingSink(CAPACITY);
            var queue = new SessionEventQueue(SESSION_ID, sink, CAPACITY, BATCH_CAPACITY);
            var data  = new SessionEventData(ESessionEventType.EnemyKilled, START_TIME);
            queue.Tick(START_TIME);
            queue.TryEnqueue(in data, out _);

            double sendTime = START_TIME + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS;
            queue.Tick(sendTime);
            LogAssert.Expect(LogType.Error, TERMINAL_ERROR);
            sink.Complete(EEventBatchResult.Terminal);

            Assert.IsTrue(queue.IsTerminal);
            Assert.IsFalse(queue.TryEnqueue(in data, out long sequence), "멈춘 큐는 새 이벤트를 받지 않는다");
            Assert.AreEqual(0, sequence);
            queue.Tick(sendTime + Constants.Session.EVENT_RETRY_MAX_SECONDS);
            Assert.AreEqual(1, sink.AttemptCount, "확정 거절된 배치를 다시 보내지 않는다");
            Assert.AreEqual(0, queue.LastAcknowledgedSequence);
        }

        [Test]
        public void OnBatchCompleted_Transient_RetriesSameBatch()
        {
            var sink  = new RecordingSink(CAPACITY);
            var queue = new SessionEventQueue(SESSION_ID, sink, CAPACITY, BATCH_CAPACITY);
            var data  = new SessionEventData(ESessionEventType.EnemyKilled, START_TIME);
            queue.Tick(START_TIME);
            queue.TryEnqueue(in data, out _);

            double sendTime = START_TIME + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS;
            queue.Tick(sendTime);
            sink.Complete(EEventBatchResult.Transient);
            queue.Tick(sendTime + Constants.Session.EVENT_RETRY_INITIAL_SECONDS);

            Assert.IsFalse(queue.IsTerminal);
            Assert.AreEqual(2, sink.AttemptCount);
            Assert.AreEqual(1, sink.Snapshot[0].Seq);
        }

        [Test]
        public void OnBatchCompleted_StaleAndDuplicateTokens_PreservesCurrentBatch()
        {
            var sink  = new RecordingSink(CAPACITY);
            var queue = new SessionEventQueue(SESSION_ID, sink, CAPACITY, 1);
            var data  = new SessionEventData(ESessionEventType.EnemyKilled, START_TIME);
            queue.Tick(START_TIME);
            queue.TryEnqueue(in data, out _);
            queue.TryEnqueue(in data, out _);
            queue.TryEnqueue(in data, out _);

            double sendTime = START_TIME + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS;
            queue.Tick(sendTime);
            long firstAttempt = sink.AttemptId;
            sink.Complete(false);
            queue.OnBatchCompleted(firstAttempt, EEventBatchResult.Committed);
            Assert.AreEqual(3, queue.QueuedCount);
            Assert.AreEqual(0, queue.LastAcknowledgedSequence);

            queue.Tick(sendTime + Constants.Session.EVENT_RETRY_INITIAL_SECONDS);
            long retryAttempt = sink.AttemptId;
            Assert.Greater(retryAttempt, firstAttempt);
            Assert.AreEqual(1, sink.Snapshot[0].Seq);
            queue.OnBatchCompleted(firstAttempt, EEventBatchResult.Committed);
            Assert.AreEqual(3, queue.QueuedCount);
            sink.Complete(true);
            queue.OnBatchCompleted(retryAttempt, EEventBatchResult.Committed);
            Assert.AreEqual(2, queue.QueuedCount);
            Assert.AreEqual(1, queue.LastAcknowledgedSequence);

            queue.Tick(sendTime + Constants.Session.EVENT_RETRY_INITIAL_SECONDS +
                Constants.Session.EVENT_BATCH_INTERVAL_SECONDS);
            queue.OnBatchCompleted(retryAttempt, EEventBatchResult.Committed);
            Assert.AreEqual(2, queue.QueuedCount);
            Assert.AreEqual(2, sink.Snapshot[0].Seq);
            sink.Complete(true);
            Assert.AreEqual(1, queue.QueuedCount);
            Assert.AreEqual(2, queue.LastAcknowledgedSequence);
        }

        [Test]
        public void Tick_SendStartFailures_RetriesSamePayloadWithCappedBackoff()
        {
            var sink  = new RecordingSink(CAPACITY) { AcceptSend = false };
            var queue = new SessionEventQueue(SESSION_ID, sink, CAPACITY, BATCH_CAPACITY);
            var loot  = new LootRollResult(17, 1, 1, EFFECT_ID);
            var data  = new SessionEventData(ESessionEventType.EnemyKilled, START_TIME,
                participantId: PARTICIPANT_ID, sourceId: SOURCE_ID, killerParticipantId: PARTICIPANT_ID,
                killSequence: 7, waveIndex: 3, reason: ESessionEndReason.Success,
                isBossSession: true, loot: in loot);
            queue.Tick(START_TIME);
            queue.TryEnqueue(in data, out _);
            queue.TryEnqueue(in data, out _);
            queue.TryEnqueue(in data, out _);
            double sendTime = START_TIME + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS;
            queue.Tick(sendTime);
            SessionEvent[] originalBuffer  = sink.BorrowedBuffer;
            long           previousAttempt = sink.AttemptId;
            double[]       delays          = { 1.0, 2.0, 4.0, 8.0, 16.0, 30.0, 30.0 };

            for (int i = 0; i < delays.Length; ++i)
            {
                queue.Tick(sendTime + delays[i] - JUST_BEFORE);
                Assert.AreEqual(i + 1, sink.AttemptCount);
                sendTime += delays[i];
                queue.Tick(sendTime);
                Assert.AreEqual(i + 2, sink.AttemptCount);
                Assert.Greater(sink.AttemptId, previousAttempt);
                Assert.AreSame(originalBuffer, sink.BorrowedBuffer);
                Assert.AreEqual(BATCH_CAPACITY, sink.Count);
                Assert.AreEqual(1, sink.Snapshot[0].Seq);
                Assert.AreEqual(2, sink.Snapshot[1].Seq);
                Assert.AreEqual(START_TIME, sink.Snapshot[0].Data.ServerTime);
                Assert.AreEqual(SOURCE_ID, sink.Snapshot[0].Data.SourceId);
                Assert.AreEqual(PARTICIPANT_ID, sink.Snapshot[0].Data.KillerParticipantId);
                Assert.AreEqual(7, sink.Snapshot[0].Data.KillSequence);
                Assert.AreEqual(3, sink.Snapshot[0].Data.WaveIndex);
                Assert.IsTrue(sink.Snapshot[0].Data.IsBossSession);
                Assert.AreEqual(17, sink.Snapshot[0].Data.Loot.ExtraEssence);
                Assert.AreEqual(EFFECT_ID, sink.Snapshot[0].Data.Loot.TalismanEffectId);
                Assert.AreEqual(3, queue.QueuedCount);
                previousAttempt = sink.AttemptId;
            }

            Assert.AreEqual(delays.Length, queue.RetryCount);
            sink.AcceptSend = true;
            queue.Tick(sendTime + Constants.Session.EVENT_RETRY_MAX_SECONDS);
            sink.Complete(true);
            Assert.AreEqual(1, queue.QueuedCount);
            Assert.AreEqual(2, queue.LastAcknowledgedSequence);
        }

        [Test]
        public void Tick_SendException_PreservesBatchAndRetriesWithoutPropagating()
        {
            var sink  = new RecordingSink(CAPACITY)
            {
                SendException = new InvalidOperationException("backend send failed")
            };
            var queue = new SessionEventQueue(SESSION_ID, sink, CAPACITY, BATCH_CAPACITY);
            var data  = new SessionEventData(ESessionEventType.EnemyKilled, START_TIME,
                sourceId: SOURCE_ID);
            queue.Tick(START_TIME);
            queue.TryEnqueue(in data, out _);
            double sendTime = START_TIME + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS;
            LogAssert.Expect(LogType.Error, SEND_EXCEPTION_ERROR);
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: backend send failed"));
            Assert.DoesNotThrow(() => queue.Tick(sendTime));
            Assert.AreEqual(1, queue.QueuedCount);
            Assert.AreEqual(0, queue.LastAcknowledgedSequence);

            sink.SendException = null;
            queue.Tick(sendTime + Constants.Session.EVENT_RETRY_INITIAL_SECONDS - JUST_BEFORE);
            Assert.AreEqual(1, sink.AttemptCount);
            queue.Tick(sendTime + Constants.Session.EVENT_RETRY_INITIAL_SECONDS);
            Assert.AreEqual(2, sink.AttemptCount);
            Assert.AreEqual(1, queue.RetryCount);
            Assert.AreEqual(1, sink.Snapshot[0].Seq);
            Assert.AreEqual(SOURCE_ID, sink.Snapshot[0].Data.SourceId);
            sink.Complete(true);
            Assert.AreEqual(0, queue.QueuedCount);
        }

        [Test]
        public void TryEnqueueBatch_InsufficientCapacity_RejectsAllWithoutSequenceGap()
        {
            var                sink  = new RecordingSink(CAPACITY);
            var                queue = new SessionEventQueue(SESSION_ID, sink, 3, 3);
            var                data  = new SessionEventData(ESessionEventType.EnemyKilled, START_TIME);
            SessionEventData[] batch = { data, data, data };
            Assert.IsTrue(queue.TryEnqueue(in data, out long firstSequence));
            Assert.IsFalse(queue.TryEnqueueBatch(batch, batch.Length));
            Assert.AreEqual(1, queue.QueuedCount);
            Assert.AreEqual(1, queue.HighWatermark);
            Assert.AreEqual(batch.Length, queue.RejectedCount);
            Assert.IsTrue(queue.TryEnqueueBatch(batch, BATCH_CAPACITY));
            Assert.AreEqual(3, queue.QueuedCount);
            Assert.AreEqual(3, queue.HighWatermark);
            Assert.IsFalse(queue.TryEnqueue(in data, out long rejectedSequence));
            Assert.AreEqual(0, rejectedSequence);
            Assert.AreEqual(4, queue.RejectedCount);

            LogAssert.Expect(LogType.Warning, QUEUE_WARNING);
            queue.Tick(START_TIME);
            queue.Tick(START_TIME + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS);
            Assert.AreEqual(firstSequence, sink.Snapshot[0].Seq);
            Assert.AreEqual(firstSequence + 1, sink.Snapshot[1].Seq);
            Assert.AreEqual(firstSequence + 2, sink.Snapshot[2].Seq);
        }

        [Test]
        public void TryEnqueue_FullWhileInFlight_PreservesOriginalEventsAndWrapOrder()
        {
            var sink  = new RecordingSink(CAPACITY);
            var queue = new SessionEventQueue(SESSION_ID, sink, BATCH_CAPACITY, 1);
            var data  = new SessionEventData(ESessionEventType.EnemyKilled, START_TIME);
            queue.Tick(START_TIME);
            queue.TryEnqueue(in data, out _);
            queue.TryEnqueue(in data, out _);
            double sendTime = START_TIME + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS;
            queue.Tick(sendTime);
            Assert.IsFalse(queue.TryEnqueue(in data, out _));
            Assert.AreEqual(BATCH_CAPACITY, queue.QueuedCount);
            Assert.AreEqual(1, sink.Snapshot[0].Seq);

            LogAssert.Expect(LogType.Warning, QUEUE_WARNING);
            queue.Tick(sendTime);
            sink.Complete(true);
            Assert.IsTrue(queue.TryEnqueue(in data, out long nextSequence));
            Assert.AreEqual(3, nextSequence);
            queue.Tick(sendTime + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS);
            Assert.AreEqual(2, sink.Snapshot[0].Seq);
            sink.Complete(true);
            queue.Tick(sendTime + (Constants.Session.EVENT_BATCH_INTERVAL_SECONDS * 2));
            Assert.AreEqual(nextSequence, sink.Snapshot[0].Seq);
            sink.Complete(true);
            Assert.AreEqual(0, queue.QueuedCount);
        }

        [Test]
        public void Tick_InvalidOrRegressingTime_DoesNotMoveBatchOrRetryClock()
        {
            var sink  = new RecordingSink(CAPACITY) { AcceptSend = false };
            var queue = new SessionEventQueue(SESSION_ID, sink, CAPACITY, BATCH_CAPACITY);
            var data  = new SessionEventData(ESessionEventType.EnemyKilled, START_TIME);
            queue.TryEnqueue(in data, out _);
            queue.Tick(double.NaN);
            queue.Tick(double.PositiveInfinity);
            queue.Tick(double.NegativeInfinity);
            queue.Tick(START_TIME);
            queue.Tick(START_TIME - Constants.Session.EVENT_BATCH_INTERVAL_SECONDS);
            double sendTime = START_TIME + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS;
            queue.Tick(sendTime - JUST_BEFORE);
            Assert.AreEqual(0, sink.AttemptCount);
            queue.Tick(sendTime);
            Assert.AreEqual(1, sink.AttemptCount);

            queue.Tick(START_TIME);
            queue.Tick(double.NaN);
            queue.Tick(double.PositiveInfinity);
            queue.Tick(sendTime + Constants.Session.EVENT_RETRY_INITIAL_SECONDS - JUST_BEFORE);
            Assert.AreEqual(1, sink.AttemptCount);
            queue.Tick(sendTime + Constants.Session.EVENT_RETRY_INITIAL_SECONDS);
            Assert.AreEqual(2, sink.AttemptCount);
            Assert.AreEqual(1, queue.QueuedCount);
        }

        [Test]
        public void Tick_SynchronousCallbackAndReentry_AcknowledgesWithoutOverwritingNextBatch()
        {
            var sink  = new RecordingSink(CAPACITY)
            {
                CompleteSynchronously = true,
                ReenterTick           = true
            };
            var queue = new SessionEventQueue(SESSION_ID, sink, CAPACITY, 1);
            var data  = new SessionEventData(ESessionEventType.EnemyKilled, START_TIME);
            queue.Tick(START_TIME);
            queue.TryEnqueue(in data, out _);
            queue.TryEnqueue(in data, out _);
            double sendTime = START_TIME + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS;
            queue.Tick(sendTime);
            Assert.AreEqual(1, sink.AttemptCount);
            Assert.AreEqual(1, queue.QueuedCount);
            Assert.AreEqual(1, queue.LastAcknowledgedSequence);
            queue.Tick(sendTime);
            Assert.AreEqual(1, sink.AttemptCount);

            queue.Tick(sendTime + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS);
            Assert.AreEqual(2, sink.AttemptCount);
            Assert.AreEqual(2, sink.Snapshot[0].Seq);
            Assert.AreEqual(0, queue.QueuedCount);
            Assert.AreEqual(2, queue.LastAcknowledgedSequence);
        }

        [Test]
        public void Tick_CapacityWarning_RateLimitsFixedMessage()
        {
            var sink  = new RecordingSink(CAPACITY) { AcceptSend = false };
            var queue = new SessionEventQueue(SESSION_ID, sink, 1, 1);
            var data  = new SessionEventData(ESessionEventType.EnemyKilled, START_TIME);
            queue.TryEnqueue(in data, out _);
            queue.TryEnqueue(in data, out _);
            LogAssert.Expect(LogType.Warning, QUEUE_WARNING);
            queue.Tick(START_TIME);
            queue.TryEnqueue(in data, out _);
            queue.Tick(START_TIME + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS);
            queue.Tick(START_TIME + Constants.Session.EVENT_QUEUE_WARNING_INTERVAL_SECONDS - JUST_BEFORE);
            LogAssert.NoUnexpectedReceived();

            LogAssert.Expect(LogType.Warning, QUEUE_WARNING);
            queue.Tick(START_TIME + Constants.Session.EVENT_QUEUE_WARNING_INTERVAL_SECONDS);
            Assert.AreEqual(2, queue.RejectedCount);
            Assert.AreEqual(1, queue.HighWatermark);
            Assert.AreEqual(1, queue.QueuedCount);
        }

        [Test]
        public void Tick_NormalPathAfterWarmup_AllocatesZeroManagedBytes()
        {
            var                sink       = new RecordingSink(CAPACITY) { CompleteSynchronously = true };
            var                queue      = new SessionEventQueue(SESSION_ID, sink, CAPACITY, BATCH_CAPACITY);
            var                data       = new SessionEventData(ESessionEventType.EnemyKilled, START_TIME,
                participantId: PARTICIPANT_ID, sourceId: SOURCE_ID);
            SessionEventData[] batch      = { data };
            double             serverTime = START_TIME;
            queue.Tick(serverTime);
            RunNormalPath(queue, in data, batch, ref serverTime, WARMUP_ITERATIONS);
            GC.GetAllocatedBytesForCurrentThread();

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            RunNormalPath(queue, in data, batch, ref serverTime, MEASURED_ITERATIONS);
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.AreEqual(0, allocatedBytes);
            Assert.AreEqual(0, queue.QueuedCount);
            Assert.AreEqual(WARMUP_ITERATIONS + MEASURED_ITERATIONS, queue.LastAcknowledgedSequence);
        }

        /****************************************
        *                Methods
        ****************************************/

        private static void RunNormalPath(SessionEventQueue queue, in SessionEventData data,
            SessionEventData[] batch, ref double serverTime, int iterations)
        {
            for (int i = 0; i < iterations; ++i)
            {
                if ((i & 1) == 0)
                {
                    queue.TryEnqueue(in data, out _);
                }
                else
                {
                    queue.TryEnqueueBatch(batch, batch.Length);
                }

                serverTime += Constants.Session.EVENT_BATCH_INTERVAL_SECONDS;
                queue.Tick(serverTime);
            }
        }

        /****************************************
        *              Test Doubles
        ****************************************/

        private sealed class RecordingSink : IBackendEventSink
        {
            /****************************************
            *                Fields
            ****************************************/

            private IBackendEventSinkListener _listener;

            /****************************************
            *              Properties
            ****************************************/

            public bool           AcceptSend            { get; set; }         = true;
            public bool           CompleteSynchronously { get; set; }         = false;
            public bool           ReenterTick           { get; set; }         = false;
            public Exception      SendException         { get; set; }
            public int            AttemptCount          { get; private set; } = 0;
            public int            Count                 { get; private set; } = 0;
            public long           AttemptId             { get; private set; } = 0;
            public string         SessionId             { get; private set; }
            public SessionEvent[] BorrowedBuffer        { get; private set; }
            public SessionEvent[] Snapshot              { get; }

            /****************************************
            *                Methods
            ****************************************/

            public RecordingSink(int capacity)
            {
                Snapshot = new SessionEvent[capacity];
            }

            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
                IBackendEventSinkListener listener)
            {
                ++AttemptCount;
                SessionId      = sessionId;
                AttemptId      = attemptId;
                BorrowedBuffer = events;
                Count          = count;
                _listener      = listener;
                for (int i = 0; i < count; ++i)
                {
                    Snapshot[i] = events[i];
                }

                if (SendException != null)
                {
                    throw SendException;
                }

                if (!AcceptSend)
                {
                    return false;
                }

                if (CompleteSynchronously)
                {
                    listener.OnBatchCompleted(attemptId, EEventBatchResult.Committed);
                    if ((ReenterTick) && (listener is SessionEventQueue queue))
                    {
                        queue.Tick(REENTRY_TIME);
                    }
                }

                return true;
            }

            public void Complete(bool success)
            {
                Complete(success ? EEventBatchResult.Committed : EEventBatchResult.Transient);
            }

            public void Complete(EEventBatchResult result)
            {
                _listener.OnBatchCompleted(AttemptId, result);
            }
        }
    }
}
