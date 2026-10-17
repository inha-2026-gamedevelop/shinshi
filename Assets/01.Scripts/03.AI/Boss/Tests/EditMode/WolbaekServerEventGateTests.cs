// System
using System;
using System.Collections.Generic;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 기믹 사건이 시계 오차로 서버 시각보다 조금 일찍 도착했을 때 버리지 않고 그 시각까지 미루는지, 순서와 정리를 지키는지 본다
    public class WolbaekServerEventGateTests
    {
        [Test]
        public void PastOrPresentEvent_RunsImmediately()
        {
            var gate = new WolbaekServerEventGate();

            Assert.That(Submit(gate, 99.9d, 100d, () => { }), Is.False);
            Assert.That(Submit(gate, 100d, 100d, () => { }), Is.False);
            Assert.That(gate.Count, Is.Zero);
        }

        [Test]
        public void SlightlyFutureEvent_RunsAtItsServerTimeOnce()
        {
            var gate = new WolbaekServerEventGate();
            int runs = 0;

            Assert.That(Submit(gate, 100.1d, 100d, () => ++runs), Is.True);
            gate.Flush(100.05d);
            Assert.That(runs, Is.Zero);

            gate.Flush(100.1d);
            gate.Flush(100.2d);
            Assert.That(runs, Is.EqualTo(1));
            Assert.That(gate.Count, Is.Zero);
        }

        [Test]
        public void FarFutureEvent_IsDroppedNotPulledToNow()
        {
            var gate = new WolbaekServerEventGate();
            int runs = 0;

            Assert.That(Submit(gate, 100.7d, 100d, () => ++runs), Is.True);
            gate.Flush(101d);
            Assert.That(runs, Is.Zero);
            Assert.That(gate.Count, Is.Zero);
        }

        [Test]
        public void EventArrivingBehindAPendingOne_KeepsArrivalOrder()
        {
            var gate = new WolbaekServerEventGate();
            var order = new List<string>();

            Assert.That(Submit(gate, 100.2d, 100d, () => order.Add("start")), Is.True);
            // 뒤에 온 사건은 서버 시각이 이미 지났어도 앞 사건보다 먼저 처리하지 않는다
            Assert.That(Submit(gate, 100d, 100d, () => order.Add("resolve")), Is.True);

            gate.Flush(100.1d);
            Assert.That(order, Is.Empty);

            gate.Flush(100.2d);
            Assert.That(order, Is.EqualTo(new[] { "start", "resolve" }));
        }

        [Test]
        public void Clear_DropsPendingEventsFromTheOldBattle()
        {
            var gate = new WolbaekServerEventGate();
            int runs = 0;

            Submit(gate, 100.2d, 100d, () => ++runs);
            gate.Clear();
            gate.Flush(101d);

            Assert.That(runs, Is.Zero);
            Assert.That(Submit(gate, 101d, 101d, () => ++runs), Is.False);
        }

        [Test]
        public void ReplayDuringFlush_RunsInsteadOfQueueingAgain()
        {
            var gate = new WolbaekServerEventGate();
            int runs = 0;
            WolbaekServerEventGate captured = gate;

            Submit(gate, 100.1d, 100d, () =>
            {
                // 보류했던 사건을 다시 부르면 같은 검사를 거치지만 이번에는 처리해야 한다
                if (!Submit(captured, 100.1d, 100.1d, () => { }))
                {
                    ++runs;
                }
            });
            gate.Flush(100.1d);

            Assert.That(runs, Is.EqualTo(1));
        }

        // 뷰가 쓰는 방식 그대로 사건을 넘긴다. 보류했거나 버렸으면 참, 지금 처리해야 하면 거짓이다
        private static bool Submit(WolbaekServerEventGate gate, double serverTime, double now, Action replay)
        {
            if (!gate.MustDefer(serverTime, now))
            {
                return false;
            }

            gate.Defer(serverTime, now, replay);
            return true;
        }
    }
}
