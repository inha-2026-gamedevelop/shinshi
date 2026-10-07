// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public sealed class ServerMetricsExporterTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int READ_TIMEOUT_MS = 3000;

        private ServerMetricsExporter _exporter = null;

        /****************************************
        *                Methods
        ****************************************/

        [TearDown]
        public void TearDown()
        {
            _exporter?.Dispose();
            _exporter = null;
        }

        [Test]
        public void WriteBucket_InfinityBound_WritesPlusInf()
        {
            var writer = new PrometheusTextWriter(64);
            writer.WriteBucket("x", double.PositiveInfinity, 3);

            Assert.That(writer.ToString(), Is.EqualTo("x_bucket{le=\"+Inf\"} 3\n"));
        }

        [Test]
        public void EscapeLabelValue_QuoteBackslashNewline_Escaped()
        {
            Assert.That(PrometheusTextWriter.EscapeLabelValue("a\"b\\c\nd"), Is.EqualTo("a\\\"b\\\\c\\nd"));
        }

        [Test]
        public void TryParse_NoPort_DisabledWithoutError()
        {
            bool ok = ServerMetricsSettings.TryParse(Array.Empty<string>(), Env(null), out ServerMetricsSettings settings,
                out string error);

            Assert.That(ok, Is.True, error);
            Assert.That(settings.Enabled, Is.False);
        }

        [Test]
        public void TryParse_PortOnly_LoopbackDefault()
        {
            bool ok = ServerMetricsSettings.TryParse(new[] { Constants.Network.ARG_METRICS_PORT, "9310" }, Env(null),
                out ServerMetricsSettings settings, out string error);

            Assert.That(ok, Is.True, error);
            Assert.That(settings.Port, Is.EqualTo(9310));
            Assert.That(settings.BindAddress, Is.EqualTo(IPAddress.Loopback));
            Assert.That(settings.MessageDetail, Is.False);
        }

        [Test]
        public void TryParse_HostName_Rejected()
        {
            var env = new Dictionary<string, string>
            {
                { Constants.Network.ENV_METRICS_PORT, "9310" },
                { Constants.Network.ENV_METRICS_BIND_ADDRESS, "metrics.example.com" }
            };

            bool ok = ServerMetricsSettings.TryParse(Array.Empty<string>(), Env(env), out _, out string error);

            Assert.That(ok, Is.False);
            Assert.That(error, Does.Contain(Constants.Network.ENV_METRICS_BIND_ADDRESS));
        }

        [TestCase("70000")]
        [TestCase("-1")]
        [TestCase("abc")]
        public void TryParse_BadPort_Rejected(string port)
        {
            var env = new Dictionary<string, string> { { Constants.Network.ENV_METRICS_PORT, port } };

            Assert.That(ServerMetricsSettings.TryParse(Array.Empty<string>(), Env(env), out _, out _), Is.False);
        }

        [Test]
        public void TryParse_DetailSwitch_Enabled()
        {
            var env = new Dictionary<string, string>
            {
                { Constants.Network.ENV_METRICS_PORT, "9310" },
                { Constants.Network.ENV_METRICS_MESSAGE_DETAIL, "1" }
            };

            bool ok = ServerMetricsSettings.TryParse(Array.Empty<string>(), Env(env), out ServerMetricsSettings settings,
                out string error);

            Assert.That(ok, Is.True, error);
            Assert.That(settings.MessageDetail, Is.True);
        }

        [Test]
        public void GetCumulativeBucket_RecordedTicks_CumulativeByBound()
        {
            var sampler = new ServerMetricsSampler(null);
            sampler.RecordTickMs(0.5);
            sampler.RecordTickMs(4.0);
            sampler.RecordTickMs(40.0);
            sampler.RecordTickMs(1000.0);

            int last = ServerMetricsSampler.HistogramBoundsMs.Count;
            Assert.That(sampler.GetCumulativeBucket(0), Is.EqualTo(1));
            Assert.That(sampler.GetCumulativeBucket(2), Is.EqualTo(2));
            Assert.That(sampler.GetCumulativeBucket(last - 1), Is.EqualTo(3));
            Assert.That(sampler.GetCumulativeBucket(last), Is.EqualTo(4));
            Assert.That(sampler.TotalTickCount, Is.EqualTo(4));
            Assert.That(sampler.TotalTickSumMs, Is.EqualTo(1044.5).Within(1e-9));
        }

        [Test]
        public void GetCumulativeBucket_AfterWindowReset_KeepsTotals()
        {
            var sampler = new ServerMetricsSampler(null);
            sampler.RecordTickMs(3.0);
            sampler.ResetWindow();

            Assert.That(sampler.SampleCount, Is.EqualTo(0));
            Assert.That(sampler.GetCumulativeBucket(ServerMetricsSampler.HistogramBoundsMs.Count), Is.EqualTo(1));
        }

        [Test]
        public void Render_WithTraffic_ContainsCoreSeries()
        {
            var sampler = new ServerMetricsSampler(null);
            sampler.RecordTickMs(10.0);
            var traffic = new ServerTrafficCounter();
            traffic.RecordMessageSent(typeof(ServerMetricsExporterTests), 10, 3);

            _exporter = new ServerMetricsExporter(sampler, traffic, "town", "build-1", "0.1.0", "inst-1");
            string text = _exporter.Render();

            Assert.That(text, Does.Contain("server_info{role=\"town\",build_id=\"build-1\",content_version=\"0.1.0\",instance_id=\"inst-1\"} 1"));
            Assert.That(text, Does.Contain("server_tick_duration_ms_count 1"));
            Assert.That(text, Does.Contain("server_tick_duration_ms_bucket{le=\"+Inf\"} 1"));
            Assert.That(text, Does.Contain("network_send_bytes_total 0"));
            // 종류별 집계를 켜지 않았으면 메시지 시계열을 쓰지 않는다
            Assert.That(text, Does.Not.Contain("network_message_send_bytes_total{"));
        }

        [Test]
        public void RecordMessageSent_MultipleRecipients_MultipliesBytes()
        {
            var traffic = new ServerTrafficCounter();
            traffic.RecordMessageSent(typeof(ServerMetricsExporterTests), 10, 3);
            traffic.RecordMessageReceived(typeof(ServerMetricsExporterTests), 7);

            Assert.That(traffic.Messages.Count, Is.EqualTo(1));
            Assert.That(traffic.Messages[0].SentBytes, Is.EqualTo(30));
            Assert.That(traffic.Messages[0].SentCount, Is.EqualTo(3));
            Assert.That(traffic.Messages[0].ReceivedBytes, Is.EqualTo(7));
        }

        [Test]
        public void Start_GetMetrics_Returns200WithBody()
        {
            _exporter = new ServerMetricsExporter(new ServerMetricsSampler(null), null, "dungeon", "b", "c", "i");
            Assert.That(_exporter.Start(IPAddress.Loopback, 0, out string error), Is.True, error);

            string response = Request(_exporter.BoundPort, "GET /metrics HTTP/1.1\r\nHost: localhost\r\n\r\n");

            Assert.That(response, Does.StartWith("HTTP/1.1 200 OK"));
            Assert.That(response, Does.Contain(PrometheusTextWriter.CONTENT_TYPE));
            Assert.That(response, Does.Contain("server_info{role=\"dungeon\""));
        }

        [Test]
        public void Start_UnknownPath_Returns404()
        {
            _exporter = new ServerMetricsExporter(null, null, "town", "b", "c", "i");
            Assert.That(_exporter.Start(IPAddress.Loopback, 0, out string error), Is.True, error);

            Assert.That(Request(_exporter.BoundPort, "GET / HTTP/1.1\r\n\r\n"), Does.StartWith("HTTP/1.1 404"));
        }

        [Test]
        public void Start_PostMethod_Returns405()
        {
            _exporter = new ServerMetricsExporter(null, null, "town", "b", "c", "i");
            Assert.That(_exporter.Start(IPAddress.Loopback, 0, out string error), Is.True, error);

            Assert.That(Request(_exporter.BoundPort, "POST /metrics HTTP/1.1\r\nContent-Length: 0\r\n\r\n"),
                Does.StartWith("HTTP/1.1 405"));
        }

        [Test]
        public void Dispose_AfterStart_StopsListening()
        {
            _exporter = new ServerMetricsExporter(null, null, "town", "b", "c", "i");
            Assert.That(_exporter.Start(IPAddress.Loopback, 0, out string error), Is.True, error);
            int port = _exporter.BoundPort;

            _exporter.Dispose();

            Assert.That(_exporter.IsRunning, Is.False);
            Assert.Throws<SocketException>(() =>
            {
                using (var client = new TcpClient())
                {
                    client.Connect(IPAddress.Loopback, port);
                }
            });
        }

        private static Func<string, string> Env(Dictionary<string, string> values)
        {
            return name => ((values != null) && (values.TryGetValue(name, out string value))) ? value : null;
        }

        private static string Request(int port, string request)
        {
            using (var client = new TcpClient())
            {
                client.ReceiveTimeout = READ_TIMEOUT_MS;
                client.SendTimeout    = READ_TIMEOUT_MS;
                client.Connect(IPAddress.Loopback, port);
                NetworkStream stream = client.GetStream();
                byte[] bytes = Encoding.ASCII.GetBytes(request);
                stream.Write(bytes, 0, bytes.Length);

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
