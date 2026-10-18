// System
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;

// Unity
using UnityEngine;

using Object = UnityEngine.Object;

namespace Shinshi.Network
{
    /// <summary> 서버 빌드의 Debug 로그를 배포 규약 JSON 한 줄로 바꿔 표준 출력에 쓴다, ServerBootstrap이 한 번 설치한다 </summary>
    public sealed class ServerJsonLogHandler : ILogHandler, IDisposable
    {
        // Unity 기본 처리기를 그대로 두면 같은 줄이 평문으로 한 번 더 나가 수집기가 JSON이 아닌 줄을 받는다. 그래서 교체한다.
        // 백엔드 HTTP 완료 콜백처럼 다른 스레드에서도 로그가 오므로, 호출 쪽은 기록 재료만 잠금 없는 큐에 넣고
        // 줄 생성과 표준 출력 쓰기는 전용 스레드 하나가 맡는다. 메인 스레드가 표준 출력 때문에 막히지 않게 하려는 것이다.
        // 큐 상한을 넘으면 새 줄을 버리고 센다. Flush는 큐를 비운 뒤나 일정 간격마다만 한다.
        // 백그라운드 쓰기를 켜지 않은 생성자는 호출 스레드에서 바로 쓴다. 테스트가 출력을 곧바로 읽기 때문이다.
        // 에디터는 콘솔 창이 평문을 기대하고 테스트가 로그를 검사하므로 설치하지 않는다

        /****************************************
        *                Fields
        ****************************************/

        public const int DEFAULT_QUEUE_LIMIT = 8192;

        private const int    FLUSH_INTERVAL_MS = 200;
        private const int    STOP_JOIN_MS      = 2000;
        private const string THREAD_NAME       = "ShinshiLogWriter";
        private const string DROPPED_MESSAGE   = "로그 큐가 가득 차 줄을 버렸다, 개수=";

        private static ServerJsonLogHandler _installed;

        // 줄 서식(BuildId 포함)과 출력 쓰기를 지키는 잠금. 호출 스레드는 백그라운드 모드에서 잡지 않는다
        private readonly object        _gate = new object();
        // enqueue 수락과 종료 전이를 직렬화하는 짧은 잠금. 잠금 구간은 큐 예약과 Enqueue 한 번뿐이다
        private readonly object        _acceptGate = new object();
        private readonly ServerLogLine _line;
        private readonly ILogHandler   _fallback;
        private readonly TextWriter    _output;
        private readonly Func<string>  _sessionId;

        private readonly ConcurrentQueue<Entry> _queue = new ConcurrentQueue<Entry>();
        private readonly ManualResetEventSlim   _wake  = new ManualResetEventSlim(false);
        private readonly int                    _queueLimit;
        private readonly Thread                 _thread;

        private int  _queued          = 0;
        private long _dropped         = 0;
        private long _reportedDropped = 0;
        private int  _lastFlushTick   = 0;

        private volatile bool _stopRequested = false;

        /****************************************
        *              Properties
        ****************************************/

        public static bool IsInstalled => _installed != null;

        /// <summary> 설치된 처리기가 큐 초과로 버린 줄의 누적 수, 설치 전이면 0이다 </summary>
        public static long InstalledDroppedCount => _installed?.DroppedCount ?? 0L;

        /// <summary> 설치된 처리기의 대기 줄 수, 설치 전이거나 동기 모드면 0이다 </summary>
        public static int InstalledQueuedCount => _installed?.QueuedCount ?? 0;

        public long DroppedCount => Interlocked.Read(ref _dropped);
        public int  QueuedCount  => Volatile.Read(ref _queued);
        public bool IsBackground => _thread != null;

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 처리기가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _installed = null;
        }

        /// <summary> output에 호출 스레드에서 바로 쓰고, 쓰기에 실패하면 fallback으로 넘긴다. sessionId는 줄마다 부르며 null을 돌려줄 수 있다 </summary>
        public ServerJsonLogHandler(ServerLogLine line, ILogHandler fallback, TextWriter output, Func<string> sessionId)
            : this(line, fallback, output, sessionId, false, DEFAULT_QUEUE_LIMIT)
        {
        }

        /// <summary> background가 참이면 전용 스레드가 쓴다. 큐가 queueLimit줄을 넘으면 새 줄을 버린다 </summary>
        public ServerJsonLogHandler(ServerLogLine line, ILogHandler fallback, TextWriter output, Func<string> sessionId,
            bool background, int queueLimit)
        {
            if (line == null)
            {
                throw new ArgumentNullException(nameof(line));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            _line       = line;
            _fallback   = fallback;
            _output     = output;
            _sessionId  = sessionId;
            _queueLimit = Math.Max(1, queueLimit);

            if (background)
            {
                _lastFlushTick = Environment.TickCount;
                _thread        = new Thread(WriteLoop) { IsBackground = true, Name = THREAD_NAME };
                _thread.Start();
            }
        }

        /// <summary> 서버 빌드에서 한 번 부른다, 에디터와 이미 설치된 경우는 아무것도 하지 않는다 </summary>
        public static void Install(string service, string instanceId, string contentVersion, Func<string> sessionId)
        {
            if ((Application.isEditor) || (_installed != null))
            {
                return;
            }

            var handler = new ServerJsonLogHandler(new ServerLogLine(service, instanceId, contentVersion),
                Debug.unityLogger.logHandler, Console.Out, sessionId, true, DEFAULT_QUEUE_LIMIT);
            Debug.unityLogger.logHandler = handler;
            _installed = handler;

            // 종료 때 큐에 남은 줄을 비운다. 둘 다 불릴 수 있어 Dispose는 여러 번 불려도 안전하다
            Application.quitting += StopInstalled;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        /// <summary> 설치된 처리기의 남은 줄을 쓰고 쓰기 스레드를 멈춘다. 이후 줄은 호출 스레드에서 바로 쓴다 </summary>
        public static void StopInstalled()
        {
            _installed?.Dispose();
        }

        private static void OnProcessExit(object sender, EventArgs args)
        {
            StopInstalled();
        }

        /// <summary> 빌드 신원을 읽은 뒤 부른다, 설치 전이면 무시한다 </summary>
        public static void SetBuildId(string buildId)
        {
            ServerJsonLogHandler handler = _installed;
            if (handler == null)
            {
                return;
            }

            lock (handler._gate)
            {
                handler._line.BuildId = buildId;
            }
        }

        /// <summary> 고정 이름의 이벤트 한 줄을 쓴다. 실제 상태 전이를 확인한 곳에서만 부르고, 설치 전이면 무시한다 </summary>
        public static void LogEvent(string level, string eventName, string operationId, string eventId, string dataJson,
            int reasonCode = ServerLogLine.NO_REASON)
        {
            _installed?.Write(level, eventName, null, operationId, eventId, reasonCode, dataJson);
        }

        /// <summary> 참가자 한 명에게 속한 이벤트 한 줄을 쓴다. participantId는 API 참가자 ID이며 없으면 null이다. 설치 전이면 무시하며, 그 판단은 문자열을 만들기 전에 IsInstalled로 한다 </summary>
        public static void LogParticipantEvent(string level, string eventName, string participantId, string dataJson)
        {
            ServerJsonLogHandler handler = _installed;
            if (handler == null)
            {
                return;
            }

            handler.Write(level, eventName, participantId, null, null, ServerLogLine.NO_REASON, dataJson);
        }

        public void LogFormat(LogType logType, Object context, string format, params object[] args)
        {
            string message = ((args == null) || (args.Length == 0))
                ? format
                : string.Format(CultureInfo.InvariantCulture, format, args);
            Write(LevelFor(logType), ServerLogLine.EVENT_UNITY_LOG, null, null, null, ServerLogLine.NO_REASON,
                ServerLogLine.FormatMessageData(message));
        }

        public void LogException(Exception exception, Object context)
        {
            Write(ServerLogLine.LEVEL_ERROR, ServerLogLine.EVENT_UNITY_EXCEPTION, null, null, null, ServerLogLine.NO_REASON,
                ServerLogLine.FormatExceptionData(exception));
        }

        /// <summary> Unity 로그 종류를 규약 수준으로 바꾼다 </summary>
        public static string LevelFor(LogType logType)
        {
            switch (logType)
            {
                case LogType.Warning:
                    return ServerLogLine.LEVEL_WARN;
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    return ServerLogLine.LEVEL_ERROR;
                default:
                    return ServerLogLine.LEVEL_INFO;
            }
        }

        /// <summary> 큐에 남은 줄을 모두 쓰고 스레드를 멈춘다. 시간이 넘으면 기다리지 않고 돌아온다 </summary>
        public void Dispose()
        {
            if (_thread == null)
            {
                return;
            }

            // 이 잠금을 지나면 수락된 줄은 모두 큐에 들어가 있고, 이후 줄은 동기 쓰기로 간다
            lock (_acceptGate)
            {
                _stopRequested = true;
            }

            _wake.Set();
            if ((_thread.IsAlive) && (_thread != Thread.CurrentThread))
            {
                // 멈춘 사이에 막 들어온 줄이 큐에 남지 않도록, 스레드가 끝났으면 한 번 더 비운다
                if (_thread.Join(STOP_JOIN_MS))
                {
                    Drain();
                }
            }
        }

        private void Write(string level, string eventName, string participantId, string operationId, string eventId, int reasonCode,
            string dataJson)
        {
            string sessionId = _sessionId?.Invoke();
            var    entry     = new Entry(DateTime.UtcNow, level, eventName, sessionId, participantId, operationId, eventId,
                reasonCode, dataJson);

            if (_thread != null)
            {
                bool accepted = false;
                lock (_acceptGate)
                {
                    if (!_stopRequested)
                    {
                        accepted = true;

                        // 자리를 먼저 예약하고 넘으면 되돌린다. 큐 길이를 세지 않아 호출 비용이 일정하다
                        if (Interlocked.Increment(ref _queued) > _queueLimit)
                        {
                            Interlocked.Decrement(ref _queued);
                            Interlocked.Increment(ref _dropped);
                        }
                        else
                        {
                            _queue.Enqueue(entry);
                        }
                    }
                }

                if (accepted)
                {
                    if (!_wake.IsSet)
                    {
                        _wake.Set();
                    }

                    return;
                }
            }

            // 동기 모드이거나 종료 뒤다. 종료 뒤의 줄도 잃지 않게 호출 스레드에서 바로 쓴다
            lock (_gate)
            {
                WriteEntry(entry);
                FlushOutput();
            }
        }

        private void WriteLoop()
        {
            while (true)
            {
                _wake.Wait(FLUSH_INTERVAL_MS);
                _wake.Reset();

                // 종료 여부를 먼저 읽고 Reset 뒤에 비워야 그 사이에 들어온 줄을 놓치지 않는다
                bool stopping = _stopRequested;
                Drain();
                if (stopping)
                {
                    return;
                }
            }
        }

        private void Drain()
        {
            lock (_gate)
            {
                bool wrote = false;
                while (_queue.TryDequeue(out Entry entry))
                {
                    Interlocked.Decrement(ref _queued);
                    WriteEntry(entry);
                    wrote = true;

                    // 줄이 계속 밀려 들어와도 일정 간격마다는 내보낸다
                    if (unchecked(Environment.TickCount - _lastFlushTick) >= FLUSH_INTERVAL_MS)
                    {
                        FlushOutput();
                    }
                }

                long dropped = Interlocked.Read(ref _dropped);
                if (dropped != _reportedDropped)
                {
                    long delta = dropped - _reportedDropped;
                    _reportedDropped = dropped;
                    WriteEntry(new Entry(DateTime.UtcNow, ServerLogLine.LEVEL_WARN, ServerLogLine.EVENT_UNITY_LOG,
                        _sessionId?.Invoke(), null, null, null, ServerLogLine.NO_REASON,
                        ServerLogLine.FormatMessageData(DROPPED_MESSAGE + delta.ToString(CultureInfo.InvariantCulture))));
                    wrote = true;
                }

                if (wrote)
                {
                    FlushOutput();
                }
            }
        }

        // 호출하는 쪽이 _gate를 잡고 있다
        private void WriteEntry(in Entry entry)
        {
            string text = null;
            try
            {
                text = _line.Format(entry.Timestamp, entry.Level, entry.EventName, entry.SessionId, entry.ParticipantId,
                    entry.OperationId, entry.EventId, entry.ReasonCode, entry.DataJson);
                _output.WriteLine(text);
            }
            catch (IOException exception)
            {
                WriteFallback(text, exception);
            }
            catch (ObjectDisposedException exception)
            {
                WriteFallback(text, exception);
            }
            catch (Exception exception)
            {
                // 서식 오류 하나가 쓰기 스레드를 죽이면 이후 로그가 모두 사라진다
                WriteFallback(text ?? entry.EventName, exception);
            }
        }

        private void FlushOutput()
        {
            _lastFlushTick = Environment.TickCount;
            try
            {
                _output.Flush();
            }
            catch (IOException)
            {
                // 줄 단위 쓰기에서 이미 대체 경로로 넘겼다
            }
            catch (ObjectDisposedException)
            {
                // 위와 같다
            }
        }

        // 표준 출력이 닫혀도 줄을 잃지 않게 Unity 기본 처리기(로그 파일)로 넘긴다. Debug를 부르면 이 처리기로 다시 들어온다
        private void WriteFallback(string text, Exception exception)
        {
            if (_fallback == null)
            {
                return;
            }

            _fallback.LogFormat(LogType.Error, null, "{0} ({1})", text ?? string.Empty, exception.GetType().Name);
        }

        /****************************************
        *               Records
        ****************************************/

        // 큐에 넣는 한 줄의 재료, 시각과 세션 ID는 기록한 순간 값을 쓴다
        private readonly struct Entry
        {
            public readonly DateTime Timestamp;
            public readonly string   Level;
            public readonly string   EventName;
            public readonly string   SessionId;
            public readonly string   ParticipantId;
            public readonly string   OperationId;
            public readonly string   EventId;
            public readonly int      ReasonCode;
            public readonly string   DataJson;

            public Entry(DateTime timestamp, string level, string eventName, string sessionId, string participantId,
                string operationId, string eventId, int reasonCode, string dataJson)
            {
                Timestamp     = timestamp;
                Level         = level;
                EventName     = eventName;
                SessionId     = sessionId;
                ParticipantId = participantId;
                OperationId   = operationId;
                EventId       = eventId;
                ReasonCode    = reasonCode;
                DataJson      = dataJson;
            }
        }
    }
}
