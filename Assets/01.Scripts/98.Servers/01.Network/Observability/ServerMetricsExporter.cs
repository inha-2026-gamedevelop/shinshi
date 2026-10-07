// System
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

// 서드파티
using Mirror;

namespace Shinshi.Network
{
    /// <summary> 서버 지표를 Prometheus 텍스트로 GET /metrics에 내보낸다. 수집기가 주기적으로 긁어 간다 </summary>
    // 지표는 메인 스레드가 1초마다 본문을 새로 만들어 두고, 수신 스레드는 마지막 본문만 돌려준다.
    // 그래서 게임 상태를 다른 스레드에서 읽지 않는다. 본문 갱신은 1초에 한 번 몇 KB를 할당한다.
    // HttpListener는 Windows에서 루프백이 아닌 주소에 URL 예약(관리자 권한)이 필요해 TcpListener로 받는다.
    // 요청은 한 번에 하나씩 처리한다. 수집기는 몇 초에 한 번 오므로 동시 처리가 필요 없다
    public sealed class ServerMetricsExporter : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const double REFRESH_INTERVAL_SECONDS = 1.0;

        private const int    BUILDER_CAPACITY  = 8192;
        private const int    MAX_REQUEST_BYTES = 8192;
        private const int    IO_TIMEOUT_MS     = 2000;
        private const int    STOP_JOIN_MS      = 1000;
        private const double MS_PER_SECOND     = 1000.0;
        private const string METRICS_PATH      = "/metrics";
        private const string METHOD_GET        = "GET";
        private const string LINE_END          = "\r\n";
        private const string HEADER_END        = "\r\n\r\n";
        private const string LABEL_MESSAGE     = "message";
        private const string THREAD_NAME       = "ShinshiMetricsExporter";

        private static readonly string[] G_InfoLabels = { "role", "build_id", "content_version", "instance_id" };

        private readonly ServerMetricsSampler _sampler;
        private readonly ServerTrafficCounter _traffic;
        private readonly string[]             _infoValues;
        private readonly double               _startTimeUnixSeconds;
        private readonly PrometheusTextWriter _writer = new PrometheusTextWriter(BUILDER_CAPACITY);
        private readonly Process              _process;

        private TcpListener _listener      = null;
        private Thread      _thread        = null;
        private double      _nextRefreshAt = 0.0;
        private bool        _rssAvailable  = true;

        private volatile byte[] _payload = Array.Empty<byte>();
        private volatile bool   _running = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 실제로 연 포트, 0을 주면 운영체제가 고른 포트다 </summary>
        public int  BoundPort { get; private set; }
        public bool IsRunning => _running;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> sampler와 traffic은 없을 수 있다. 없으면 그 지표를 쓰지 않는다 </summary>
        public ServerMetricsExporter(ServerMetricsSampler sampler, ServerTrafficCounter traffic, string role, string buildId,
            string contentVersion, string instanceId)
        {
            _sampler              = sampler;
            _traffic              = traffic;
            _infoValues           = new[] { role ?? string.Empty, buildId ?? string.Empty, contentVersion ?? string.Empty,
                instanceId ?? string.Empty };
            _startTimeUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / MS_PER_SECOND;

            try
            {
                _process = Process.GetCurrentProcess();
            }
            catch (Exception)
            {
                _process      = null;
                _rssAvailable = false;
            }
        }

        /// <summary> 수신을 연다, 실패하면 거짓과 이유 </summary>
        public bool Start(IPAddress address, int port, out string error)
        {
            error = null;
            if (_running)
            {
                return true;
            }

            Refresh();
            try
            {
                _listener = new TcpListener(address ?? IPAddress.Loopback, port);
                _listener.Start();
                BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
            }
            catch (SocketException exception)
            {
                _listener = null;
                error     = $"지표 포트를 열지 못했다 ({address}:{port}, {exception.SocketErrorCode})";
                return false;
            }

            _running = true;
            _thread  = new Thread(ServeLoop) { IsBackground = true, Name = THREAD_NAME };
            _thread.Start();
            return true;
        }

        /// <summary> 매 프레임 부른다, 1초마다 본문을 새로 만든다. 단조 시계 초를 받는다 </summary>
        public void Tick(double nowSeconds)
        {
            if (nowSeconds < _nextRefreshAt)
            {
                return;
            }

            _nextRefreshAt = nowSeconds + REFRESH_INTERVAL_SECONDS;
            Refresh();
        }

        /// <summary> 지금 값으로 본문을 만든다. 메인 스레드에서만 부른다 </summary>
        public void Refresh()
        {
            _payload = Encoding.UTF8.GetBytes(Render());
        }

        /// <summary> 지금 값의 노출 본문, 시험과 진단에서도 쓴다 </summary>
        public string Render()
        {
            PrometheusTextWriter w = _writer;
            w.Clear();

            w.WriteGaugeHeader("server_info", "Server role and build identity. Value is always 1");
            w.WriteSample("server_info", G_InfoLabels, _infoValues, 1);

            w.WriteGaugeHeader("process_start_time_seconds", "Process start time in unix seconds");
            w.WriteSample("process_start_time_seconds", _startTimeUnixSeconds);

            w.WriteGaugeHeader("process_managed_heap_bytes", "Managed GC heap bytes");
            w.WriteSample("process_managed_heap_bytes", GC.GetTotalMemory(false));

            long rss = ReadResidentBytes();
            if (rss >= 0)
            {
                w.WriteGaugeHeader("process_resident_memory_bytes", "Process resident memory bytes");
                w.WriteSample("process_resident_memory_bytes", rss);
            }

            WriteNetworkState(w);
            WriteTick(w);
            WriteStorage(w);
            WriteTraffic(w);
            return w.ToString();
        }

        public void Dispose()
        {
            _running = false;
            try
            {
                _listener?.Stop();
            }
            catch (SocketException)
            {
                // 이미 닫힌 소켓이다
            }

            if ((_thread != null) && (_thread.IsAlive) && (_thread != Thread.CurrentThread))
            {
                _thread.Join(STOP_JOIN_MS);
            }

            _listener = null;
            _thread   = null;
            _process?.Dispose();
        }

        private static void WriteNetworkState(PrometheusTextWriter w)
        {
            w.WriteGaugeHeader("server_active_connections", "Authenticated client connections");
            w.WriteSample("server_active_connections", (long)ServerMetricsSampler.CountAuthenticatedConnections());

            w.WriteGaugeHeader("server_spawned_objects", "Network objects spawned on the server");
            w.WriteSample("server_spawned_objects", NetworkServer.active ? (long)NetworkServer.spawned.Count : 0L);
        }

        private void WriteTick(PrometheusTextWriter w)
        {
            w.WriteGaugeHeader("server_tick_budget_ms", "Tick budget derived from the server tick rate");
            w.WriteSample("server_tick_budget_ms", ServerMetricsSampler.TickBudgetMs);

            if (_sampler == null)
            {
                return;
            }

            const string TICK = "server_tick_duration_ms";
            w.WriteHistogramHeader(TICK, "Server combat tick execution time in milliseconds");
            IReadOnlyList<double> bounds = ServerMetricsSampler.HistogramBoundsMs;
            for (int i = 0; i < bounds.Count; ++i)
            {
                w.WriteBucket(TICK, bounds[i], _sampler.GetCumulativeBucket(i));
            }

            w.WriteBucket(TICK, double.PositiveInfinity, _sampler.GetCumulativeBucket(bounds.Count));
            w.WriteHistogramTotals(TICK, _sampler.TotalTickSumMs, _sampler.TotalTickCount);

            w.WriteCounterHeader("server_tick_overrun_total", "Ticks that exceeded the tick budget");
            w.WriteSample("server_tick_overrun_total", _sampler.OverrunTotal);
        }

        // 저장 큐는 던전 역할에만 있다. 마을은 시계열을 만들지 않는다
        private void WriteStorage(PrometheusTextWriter w)
        {
            SessionEventQueue events = _sampler?.Host?.Session?.Events;
            if (events == null)
            {
                return;
            }

            w.WriteGaugeHeader("storage_queue_depth", "Session events not yet confirmed by the API");
            w.WriteSample("storage_queue_depth", (long)events.QueuedCount);
            w.WriteGaugeHeader("storage_queue_oldest_age_seconds", "Age of the oldest unconfirmed session event");
            w.WriteSample("storage_queue_oldest_age_seconds", events.OldestAgeSeconds);
            w.WriteCounterHeader("storage_retry_total", "Session event retries with the same operation id");
            w.WriteSample("storage_retry_total", (long)events.RetryCount);
        }

        private void WriteTraffic(PrometheusTextWriter w)
        {
            if (_traffic == null)
            {
                return;
            }

            w.WriteCounterHeader("network_send_bytes_total", "Payload bytes handed to the transport, without KCP and UDP headers");
            w.WriteSample("network_send_bytes_total", _traffic.SentBytes);
            w.WriteCounterHeader("network_receive_bytes_total", "Payload bytes received from the transport, without KCP and UDP headers");
            w.WriteSample("network_receive_bytes_total", _traffic.ReceivedBytes);
            w.WriteCounterHeader("network_send_packets_total", "Transport send calls");
            w.WriteSample("network_send_packets_total", _traffic.SentPackets);
            w.WriteCounterHeader("network_receive_packets_total", "Transport receive callbacks");
            w.WriteSample("network_receive_packets_total", _traffic.ReceivedPackets);

            if (!_traffic.IsDetailed)
            {
                return;
            }

            IReadOnlyList<ServerTrafficCounter.MessageStat> messages = _traffic.Messages;
            w.WriteCounterHeader("network_message_send_bytes_total", "Mirror message bytes sent, multiplied by recipients");
            for (int i = 0; i < messages.Count; ++i)
            {
                w.WriteSample("network_message_send_bytes_total", LABEL_MESSAGE, messages[i].Name, messages[i].SentBytes);
            }

            w.WriteCounterHeader("network_message_send_count_total", "Mirror messages sent, counted per recipient");
            for (int i = 0; i < messages.Count; ++i)
            {
                w.WriteSample("network_message_send_count_total", LABEL_MESSAGE, messages[i].Name, messages[i].SentCount);
            }

            w.WriteCounterHeader("network_message_receive_bytes_total", "Mirror message bytes received");
            for (int i = 0; i < messages.Count; ++i)
            {
                w.WriteSample("network_message_receive_bytes_total", LABEL_MESSAGE, messages[i].Name, messages[i].ReceivedBytes);
            }

            w.WriteCounterHeader("network_message_receive_count_total", "Mirror messages received");
            for (int i = 0; i < messages.Count; ++i)
            {
                w.WriteSample("network_message_receive_count_total", LABEL_MESSAGE, messages[i].Name, messages[i].ReceivedCount);
            }
        }

        // 플랫폼이 값을 주지 않으면 한 번 실패한 뒤로 이 지표를 빼고 다시 시도하지 않는다
        private long ReadResidentBytes()
        {
            if (!_rssAvailable)
            {
                return -1;
            }

            try
            {
                _process.Refresh();
                long rss = _process.WorkingSet64;
                if (rss > 0)
                {
                    return rss;
                }

                // Windows의 Unity Mono는 0을 돌려준다. 0을 실제 값처럼 내보내지 않는다
                _rssAvailable = false;
                return -1;
            }
            catch (Exception)
            {
                _rssAvailable = false;
                return -1;
            }
        }

        private void ServeLoop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    // Stop이 수신 대기를 끊었다
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                using (client)
                {
                    try
                    {
                        Handle(client);
                    }
                    catch (IOException)
                    {
                        // 수집기가 먼저 끊었거나 시간이 넘었다. 다음 요청을 받는다
                    }
                    catch (SocketException)
                    {
                        // 위와 같다
                    }
                }
            }
        }

        private void Handle(TcpClient client)
        {
            client.ReceiveTimeout = IO_TIMEOUT_MS;
            client.SendTimeout    = IO_TIMEOUT_MS;
            NetworkStream stream = client.GetStream();

            string requestLine = ReadRequestLine(stream);
            if (requestLine == null)
            {
                return;
            }

            string[] parts = requestLine.Split(' ');
            if ((parts.Length < 2) || (!string.Equals(parts[0], METHOD_GET, StringComparison.Ordinal)))
            {
                WriteResponse(stream, "405 Method Not Allowed", Array.Empty<byte>(), "Allow: GET" + LINE_END);
                return;
            }

            string path  = parts[1];
            int    query = path.IndexOf('?');
            if (query >= 0)
            {
                path = path.Substring(0, query);
            }

            if (!string.Equals(path, METRICS_PATH, StringComparison.Ordinal))
            {
                WriteResponse(stream, "404 Not Found", Array.Empty<byte>(), null);
                return;
            }

            WriteResponse(stream, "200 OK", _payload, null);
        }

        // 헤더 끝까지 읽고 첫 줄만 돌려준다. 상한을 넘거나 헤더가 끝나지 않으면 null
        private static string ReadRequestLine(NetworkStream stream)
        {
            var buffer = new byte[MAX_REQUEST_BYTES];
            int length = 0;
            while (length < buffer.Length)
            {
                int read = stream.Read(buffer, length, buffer.Length - length);
                if (read <= 0)
                {
                    return null;
                }

                length += read;
                string text = Encoding.ASCII.GetString(buffer, 0, length);
                if (text.IndexOf(HEADER_END, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                return text.Substring(0, text.IndexOf(LINE_END, StringComparison.Ordinal));
            }

            return null;
        }

        private static void WriteResponse(NetworkStream stream, string status, byte[] body, string extraHeaders)
        {
            var header = new StringBuilder();
            header.Append("HTTP/1.1 ").Append(status).Append(LINE_END);
            header.Append("Content-Type: ").Append(PrometheusTextWriter.CONTENT_TYPE).Append(LINE_END);
            header.Append("Content-Length: ").Append(body.Length).Append(LINE_END);
            header.Append("Connection: close").Append(LINE_END);
            if (extraHeaders != null)
            {
                header.Append(extraHeaders);
            }

            header.Append(LINE_END);
            byte[] headerBytes = Encoding.ASCII.GetBytes(header.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }
    }
}
