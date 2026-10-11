// System
using System;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.Tests
{
    public sealed class SessionEventQueueExperienceTests
    {
        [Test]
        public void RequestImmediateSend_InFlight_SendsNextBatchOnNextTick()
        {
            var sink = new RecordingSink();
            var queue = new SessionEventQueue("session", sink, 4, 1);
            EnqueueAndSend(queue, 0);
            queue.TryEnqueue(new SessionEventData(ESessionEventType.WaveCleared, 0), out _);
            queue.RequestImmediateSend();
            queue.Tick(0.01);
            Assert.That(sink.Attempts, Is.EqualTo(1));
            sink.Complete(EEventBatchResult.Committed);
            queue.Tick(0.02);
            Assert.That(sink.Attempts, Is.EqualTo(2));
        }

        [Test]
        public void RequestImmediateSend_MultipleBatches_DrainsThroughRequestedSequence()
        {
            var sink = new RecordingSink();
            var queue = new SessionEventQueue("session", sink, 4, 1);
            for (int i = 0; i < 3; ++i)
            {
                queue.TryEnqueue(new SessionEventData(ESessionEventType.WaveCleared, 0), out _);
            }
            queue.RequestImmediateSend();
            queue.Tick(0);
            sink.Complete(EEventBatchResult.Committed);
            queue.Tick(0.01);
            Assert.That(sink.Attempts, Is.EqualTo(2));
            sink.Complete(EEventBatchResult.Committed);
            queue.Tick(0.02);
            Assert.That(sink.Attempts, Is.EqualTo(3));
            sink.Complete(EEventBatchResult.Committed);
            Assert.That(queue.QueuedCount, Is.Zero);
        }

        [Test]
        public void RequestImmediateSend_RetryAfter_DoesNotBypassBackoff()
        {
            var sink = new RecordingSink();
            var queue = new SessionEventQueue("session", sink, 4, 1);
            EnqueueAndSend(queue, 0);
            sink.Complete(EEventBatchResult.Transient, 10);
            queue.RequestImmediateSend();
            queue.Tick(9);
            Assert.That(sink.Attempts, Is.EqualTo(1));
            queue.Tick(10);
            Assert.That(sink.Attempts, Is.EqualTo(2));
            Assert.That(queue.LastAcknowledgedSequence, Is.Zero);
        }

        [Test]
        public void Committed_ThrowingObserverAndReentry_StillAcknowledgesExactlyOnce()
        {
            var sink = new RecordingSink();
            var queue = new SessionEventQueue("session", sink, 4, 1);
            int observed = 0;
            queue.Committed += _ => throw new InvalidOperationException("observer-failure");
            queue.Committed += current =>
            {
                Assert.That(current.Seq, Is.EqualTo(1));
                ++observed;
                queue.Tick(100);
            };
            EnqueueAndSend(queue, 0);
            LogAssert.Expect(LogType.Exception, new Regex("observer-failure"));
            sink.Complete(EEventBatchResult.Committed);
            sink.Complete(EEventBatchResult.Committed);
            Assert.That(observed, Is.EqualTo(1));
            Assert.That(queue.QueuedCount, Is.Zero);
            Assert.That(queue.LastAcknowledgedSequence, Is.EqualTo(1));
            Assert.That(sink.Attempts, Is.EqualTo(1));
        }

        [Test]
        public void Committed_StaleAttempt_DoesNotNotifyObservers()
        {
            var sink = new RecordingSink();
            var queue = new SessionEventQueue("session", sink);
            int observed = 0;
            queue.Committed += _ => ++observed;
            EnqueueAndSend(queue, 0);
            long first = sink.AttemptId;
            sink.Complete(EEventBatchResult.Transient);
            queue.Tick(1);
            queue.OnBatchCompleted(first, EEventBatchResult.Committed);
            Assert.That(observed, Is.Zero);
            sink.Complete(EEventBatchResult.Committed);
            Assert.That(observed, Is.EqualTo(1));
        }

        [Test]
        public void Committed_SubscribedAfterWarmup_AllocatesZeroManagedBytes()
        {
            const int WARMUP_COUNT = 128;
            const int SAMPLE_COUNT = 512;
            var sink = new RecordingSink();
            var queue = new SessionEventQueue("session", sink);
            queue.Committed += Observe;
            for (int i = 0; i < WARMUP_COUNT; ++i)
            {
                EnqueueAndSend(queue, i);
                sink.Complete(EEventBatchResult.Committed);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < SAMPLE_COUNT; ++i)
            {
                EnqueueAndSend(queue, WARMUP_COUNT + i);
                sink.Complete(EEventBatchResult.Committed);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        private static void Observe(SessionEvent current) { }

        private static void EnqueueAndSend(SessionEventQueue queue, double time)
        {
            queue.TryEnqueue(new SessionEventData(ESessionEventType.WaveCleared, time), out _);
            queue.RequestImmediateSend();
            queue.Tick(time);
        }

        private sealed class RecordingSink : IBackendEventSink
        {
            private IBackendEventSinkListener _listener;
            public int Attempts { get; private set; }
            public long AttemptId { get; private set; }

            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
                IBackendEventSinkListener listener)
            {
                ++Attempts;
                AttemptId = attemptId;
                _listener = listener;
                return true;
            }

            public void Complete(EEventBatchResult result, double retryAfter = 0)
            {
                _listener.OnBatchCompleted(AttemptId, result, retryAfter);
            }
        }
    }
}
