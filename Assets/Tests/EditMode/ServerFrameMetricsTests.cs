// System
using System;
using System.IO;
using System.Text;
using System.Threading;

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
    public sealed class ServerFrameMetricsTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SERVICE         = "dungeon";
        private const string INSTANCE_ID     = "frame-test";
        private const string CONTENT_VERSION = "0.0.0";
        private const string PATH            = "/v1/test";
        private const float  TIMEOUT_SECONDS = 1f;
        private const double FRAME_30HZ      = 1.0 / 30.0;
        private const int    LOG_COUNT       = 200;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void RecordFrame_NormalAndSlowFrames_CountsOverrunAndMax()
        {
            var sampler = new ServerMetricsSampler(null);
            sampler.RecordFrame(10.0);
            sampler.RecordFrame(10.0 + FRAME_30HZ);
            sampler.RecordFrame(10.0 + (2.0 * FRAME_30HZ));
            sampler.RecordFrame(10.0 + (2.0 * FRAME_30HZ) + 0.2);

            Assert.That(sampler.FrameCount, Is.EqualTo(3));
            Assert.That(sampler.FrameOverrunTotal, Is.EqualTo(1));
            Assert.That(sampler.FrameMaxMs, Is.EqualTo(200.0).Within(1e-6));
            Assert.That(sampler.GetFrameCumulativeBucket(ServerMetricsSampler.FrameBoundsMs.Count), Is.EqualTo(3));
            Assert.That(sampler.FrameSumMs, Is.EqualTo(2000.0 / 30.0 + 200.0).Within(1e-6));
        }

        [Test]
        public void RecordFrame_FirstCall_OnlyStartsClock()
        {
            var sampler = new ServerMetricsSampler(null);
            sampler.RecordFrame(123.0);

            Assert.That(sampler.FrameCount, Is.EqualTo(0));
            Assert.That(sampler.StallMax1sMs, Is.LessThan(0.0));
        }

        [Test]
        public void RecordFrame_WindowClosed_ReportsLargestIntervalOfThatWindow()
        {
            var sampler = new ServerMetricsSampler(null);
            sampler.RecordFrame(0.0);
            sampler.RecordFrame(0.1);
            sampler.RecordFrame(0.2);
            sampler.RecordFrame(0.7);
            Assert.That(sampler.StallMax1sMs, Is.LessThan(0.0), "1초가 지나기 전에는 값이 없다");

            sampler.RecordFrame(1.0);
            Assert.That(sampler.StallMax1sMs, Is.EqualTo(500.0).Within(1e-6));

            sampler.RecordFrame(1.1);
            sampler.RecordFrame(2.0);
            Assert.That(sampler.StallMax1sMs, Is.EqualTo(900.0).Within(1e-6));
        }

        [Test]
        public void Render_AfterFrames_ContainsFrameSeries()
        {
            var sampler = new ServerMetricsSampler(null);
            sampler.RecordFrame(0.0);
            sampler.RecordFrame(0.5);
            sampler.RecordFrame(1.0);
            using var exporter = new ServerMetricsExporter(sampler, null, "dungeon", "b", "c", "i");

            string text = exporter.Render();

            StringAssert.Contains("server_frame_interval_ms_bucket{le=\"+Inf\"} 2", text);
            StringAssert.Contains("server_frame_interval_ms_count 2", text);
            StringAssert.Contains("server_frame_overrun_total 2", text);
            StringAssert.Contains("server_frame_interval_max_ms 500", text);
            StringAssert.Contains("server_frame_stall_max_1s_ms 500", text);
            StringAssert.DoesNotContain("kcp_send_queue_total", text, "표본이 없으면 kcp 지표를 내지 않는다");
        }

        [Test]
        public void Kcp_Observe_SumsAndMaxima()
        {
            var probe = new ServerKcpQueueProbe(100);
            probe.BeginSample();
            probe.Observe(1, 10, 5);
            probe.Observe(2, 30, 20);
            probe.EndSample();

            Assert.That(probe.IsAvailable, Is.True);
            Assert.That(probe.ConnectionCount, Is.EqualTo(2));
            Assert.That(probe.SendQueueTotal, Is.EqualTo(40));
            Assert.That(probe.SendQueueMax, Is.EqualTo(30));
            Assert.That(probe.SendBufferTotal, Is.EqualTo(25));
            Assert.That(probe.SendBufferMax, Is.EqualTo(20));
            Assert.That(probe.BacklogMax, Is.EqualTo(50));
            Assert.That(probe.WarnTotal, Is.EqualTo(0));
        }

        [Test]
        public void Kcp_OverThreshold_WarnsOncePerCrossing()
        {
            var probe = new ServerKcpQueueProbe(100);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("connection=7"));
            Sample(probe, 7, 150);
            Sample(probe, 7, 200);
            Assert.That(probe.WarnTotal, Is.EqualTo(1), "계속 넘어 있으면 다시 경고하지 않는다");

            Sample(probe, 7, 10);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("connection=7"));
            Sample(probe, 7, 150);
            Assert.That(probe.WarnTotal, Is.EqualTo(2));
        }

        [Test]
        public void Kcp_SampleWithoutKcpTransport_IsUnavailable()
        {
            var probe = new ServerKcpQueueProbe();
            probe.Sample(null);

            Assert.That(probe.IsAvailable, Is.False);
        }

        [Test]
        public void Render_WithGateway_ContainsBackendQueues()
        {
            var       transport = FakeBackendTransport.Never();
            using var gateway   = new BackendGateway(transport, null, null, null, null);
            gateway.Send(BackendRequest.Get(PATH, null, null, TIMEOUT_SECONDS), new RecordingBackendListener());
            using var exporter = new ServerMetricsExporter(null, null, "dungeon", "b", "c", "i") { Gateway = gateway };

            string text = exporter.Render();

            StringAssert.Contains("backend_gateway_in_flight 1", text);
            StringAssert.Contains("backend_gateway_completed_queue 0", text);
        }

        [Test]
        public void Handler_Background_WritesAllLinesInOrderAfterDispose()
        {
            var output = new StringWriter();
            var handler = new ServerJsonLogHandler(new ServerLogLine(SERVICE, INSTANCE_ID, CONTENT_VERSION), null, output,
                () => "s-1", true, ServerJsonLogHandler.DEFAULT_QUEUE_LIMIT);

            for (int i = 0; i < LOG_COUNT; ++i)
            {
                handler.LogFormat(LogType.Log, null, "line-{0}", i);
            }

            handler.Dispose();

            string[] lines = output.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            Assert.That(lines.Length, Is.EqualTo(LOG_COUNT));
            StringAssert.StartsWith("{\"ts\":", lines[0]);
            StringAssert.Contains("\"message\":\"line-0\"", lines[0]);
            StringAssert.Contains("\"message\":\"line-" + (LOG_COUNT - 1) + "\"", lines[LOG_COUNT - 1]);
            StringAssert.Contains("\"sessionId\":\"s-1\"", lines[0]);
            Assert.That(handler.DroppedCount, Is.EqualTo(0));
        }

        [Test]
        public void Handler_Background_AfterDispose_WritesSynchronously()
        {
            var output = new StringWriter();
            var handler = new ServerJsonLogHandler(new ServerLogLine(SERVICE, INSTANCE_ID, CONTENT_VERSION), null, output,
                null, true, ServerJsonLogHandler.DEFAULT_QUEUE_LIMIT);
            handler.Dispose();

            handler.LogFormat(LogType.Warning, null, "late");

            StringAssert.Contains("\"message\":\"late\"", output.ToString());
        }

        [Test]
        public void Handler_Background_ConcurrentDispose_LosesNoLine()
        {
            const int PRODUCERS     = 4;
            const int PER_PRODUCER  = 2000;
            var       output        = new StringWriter();
            var       handler       = new ServerJsonLogHandler(new ServerLogLine(SERVICE, INSTANCE_ID, CONTENT_VERSION), null,
                output, null, true, ServerJsonLogHandler.DEFAULT_QUEUE_LIMIT);
            var       threads       = new Thread[PRODUCERS];

            for (int t = 0; t < PRODUCERS; ++t)
            {
                threads[t] = new Thread(() =>
                {
                    for (int i = 0; i < PER_PRODUCER; ++i)
                    {
                        handler.LogFormat(LogType.Log, null, "race-{0}", i);
                    }
                });
                threads[t].Start();
            }

            handler.Dispose();
            for (int t = 0; t < PRODUCERS; ++t)
            {
                threads[t].Join();
            }

            int written = 0;
            foreach (string line in output.ToString().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Contains("\"message\":\"race-"))
                {
                    ++written;
                }
            }

            // 기록된 줄과 드롭 집계를 합치면 보낸 줄 수와 같아야 한다
            Assert.That(written + handler.DroppedCount, Is.EqualTo(PRODUCERS * PER_PRODUCER));
        }

        [Test]
        public void Handler_QueueFull_DropsAndCountsThenReportsAfterRelease()
        {
            const int LIMIT = 4;
            using var gate   = new ManualResetEventSlim(false);
            var       output = new BlockingWriter(gate);
            var       handler = new ServerJsonLogHandler(new ServerLogLine(SERVICE, INSTANCE_ID, CONTENT_VERSION), null, output,
                null, true, LIMIT);

            for (int i = 0; i < LOG_COUNT; ++i)
            {
                handler.LogFormat(LogType.Log, null, "line-{0}", i);
            }

            // 쓰기 스레드는 첫 줄에서 막혀 있으므로 큐 상한과 한 줄 밖은 모두 버려진다
            Assert.That(handler.DroppedCount, Is.GreaterThanOrEqualTo(LOG_COUNT - LIMIT - 1));
            Assert.That(handler.QueuedCount, Is.LessThanOrEqualTo(LIMIT));

            gate.Set();
            handler.Dispose();

            string text = output.ToString();
            StringAssert.Contains("로그 큐가 가득 차 줄을 버렸다", text);
            Assert.That(handler.QueuedCount, Is.EqualTo(0));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static void Sample(ServerKcpQueueProbe probe, int connectionId, int sendQueue)
        {
            probe.BeginSample();
            probe.Observe(connectionId, sendQueue, 0);
            probe.EndSample();
        }

        // 해제하기 전까지 첫 줄 쓰기에서 멈추는 출력, 쓰기 스레드가 밀리는 상황을 만든다
        private sealed class BlockingWriter : StringWriter
        {
            private readonly ManualResetEventSlim _gate;

            public BlockingWriter(ManualResetEventSlim gate)
            {
                _gate = gate;
            }

            public override Encoding Encoding => Encoding.UTF8;

            public override void WriteLine(string value)
            {
                _gate.Wait();
                base.WriteLine(value);
            }
        }
    }
}
