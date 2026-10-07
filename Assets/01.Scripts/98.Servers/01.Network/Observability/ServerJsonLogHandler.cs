// System
using System;
using System.Globalization;
using System.IO;

// Unity
using UnityEngine;

using Object = UnityEngine.Object;

namespace Shinshi.Network
{
    /// <summary> 서버 빌드의 Debug 로그를 배포 규약 JSON 한 줄로 바꿔 표준 출력에 쓴다, ServerBootstrap이 한 번 설치한다 </summary>
    public sealed class ServerJsonLogHandler : ILogHandler
    {
        // Unity 기본 처리기를 그대로 두면 같은 줄이 평문으로 한 번 더 나가 수집기가 JSON이 아닌 줄을 받는다. 그래서 교체한다.
        // 백엔드 HTTP 완료 콜백처럼 다른 스레드에서도 로그가 오므로 줄 생성과 쓰기를 한 잠금 안에서 한다.
        // 에디터는 콘솔 창이 평문을 기대하고 테스트가 로그를 검사하므로 설치하지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private static ServerJsonLogHandler _installed;

        private readonly object        _gate = new object();
        private readonly ServerLogLine _line;
        private readonly ILogHandler   _fallback;
        private readonly TextWriter    _output;
        private readonly Func<string>  _sessionId;

        /****************************************
        *              Properties
        ****************************************/

        public static bool IsInstalled => _installed != null;

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 처리기가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _installed = null;
        }

        /// <summary> output에 쓰고, 쓰기에 실패하면 fallback으로 넘긴다. sessionId는 줄마다 부르며 null을 돌려줄 수 있다 </summary>
        public ServerJsonLogHandler(ServerLogLine line, ILogHandler fallback, TextWriter output, Func<string> sessionId)
        {
            if (line == null)
            {
                throw new ArgumentNullException(nameof(line));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            _line      = line;
            _fallback  = fallback;
            _output    = output;
            _sessionId = sessionId;
        }

        /// <summary> 서버 빌드에서 한 번 부른다, 에디터와 이미 설치된 경우는 아무것도 하지 않는다 </summary>
        public static void Install(string service, string instanceId, string contentVersion, Func<string> sessionId)
        {
            if ((Application.isEditor) || (_installed != null))
            {
                return;
            }

            var handler = new ServerJsonLogHandler(new ServerLogLine(service, instanceId, contentVersion),
                Debug.unityLogger.logHandler, Console.Out, sessionId);
            Debug.unityLogger.logHandler = handler;
            _installed = handler;
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

        private void Write(string level, string eventName, string participantId, string operationId, string eventId, int reasonCode,
            string dataJson)
        {
            string sessionId = _sessionId?.Invoke();
            lock (_gate)
            {
                string text = _line.Format(DateTime.UtcNow, level, eventName, sessionId, participantId, operationId, eventId,
                    reasonCode, dataJson);
                try
                {
                    _output.WriteLine(text);
                    _output.Flush();
                }
                catch (IOException exception)
                {
                    WriteFallback(text, exception);
                }
                catch (ObjectDisposedException exception)
                {
                    WriteFallback(text, exception);
                }
            }
        }

        // 표준 출력이 닫혀도 줄을 잃지 않게 Unity 기본 처리기(로그 파일)로 넘긴다. Debug를 부르면 이 처리기로 다시 들어온다
        private void WriteFallback(string text, Exception exception)
        {
            if (_fallback == null)
            {
                return;
            }

            _fallback.LogFormat(LogType.Error, null, "{0} ({1})", text, exception.GetType().Name);
        }
    }
}
