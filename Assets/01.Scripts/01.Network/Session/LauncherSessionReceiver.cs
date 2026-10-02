// System
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Contracts;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
#endif

using Debug = UnityEngine.Debug;

namespace Shinshi.Network
{
    /// <summary> 게임 시작 시 런처 파이프에서 게임 세션을 한 번 받아 LauncherSession에 넣는다 </summary>
    public static class LauncherSessionReceiver
    {
        // 파이프 연결과 읽기는 막히는 호출이라 별도 스레드에서 한다. 첫 씬 로드가 런처를 기다리지 않게 하려는 것이다.
        // Unity API와 LauncherSession 변경은 시작할 때 잡은 메인 스레드 SynchronizationContext로 넘겨서만 한다.
        // 막힌 읽기는 다른 스레드에서 확실히 풀 방법이 없어, 감시 타이머가 제한 시간에 상태만 실패로 바꾸고
        // 수신 스레드는 백그라운드 스레드로 두어 프로세스 종료를 막지 않게 한다

        /****************************************
        *                Fields
        ****************************************/

        private const string THREAD_NAME   = "LauncherSessionReceiver";
        private const string LOCAL_HOST    = ".";
        private const long   MS_PER_SECOND = 1000;

        private static Timer _watchdog;

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 감시 타이머가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            StopWatchdog();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeginOnStartup()
        {
#if !UNITY_SERVER
            ELauncherArgsStatus status = LauncherSessionRules.ParseLaunchArgs(Environment.GetCommandLineArgs(),
                out string pipeName, out int launcherPid);
            if (status == ELauncherArgsStatus.Absent)
            {
                return;
            }

            if (status == ELauncherArgsStatus.Invalid)
            {
                FailOnStartup(ELauncherSessionFailure.InvalidArgs);
                return;
            }

            if (!IsWindowsRuntime())
            {
                FailOnStartup(ELauncherSessionFailure.UnsupportedPlatform);
                return;
            }

            SynchronizationContext mainContext = SynchronizationContext.Current;
            if (mainContext == null)
            {
                FailOnStartup(ELauncherSessionFailure.NoMainThreadContext);
                return;
            }

            int attempt = LauncherSession.BeginReceiving();
            int gamePid = GetCurrentProcessId();

            var thread = new Thread(() => RunReceive(mainContext, attempt, pipeName, launcherPid, gamePid));
            thread.IsBackground = true;
            thread.Name         = THREAD_NAME;
            thread.Start();

            StopWatchdog();
            _watchdog = new Timer(_ => mainContext.Post(__ => OnWatchdogElapsed(attempt), null), null,
                LauncherPipeProtocol.HANDOFF_TIMEOUT_MS, Timeout.Infinite);
#endif
        }

        /// <summary> 파이프에 연결해 세션 한 프레임을 받고 수신 확인을 보낸다, 막히는 호출이라 메인 스레드에서 부르지 않는다 </summary>
        public static LauncherReceiveResult ReceiveHandoff(string pipeName, int launcherPid, int gamePid, int timeoutMs,
            Func<PipeStream, int, bool> verifyServerProcess)
        {
            if (verifyServerProcess == null)
            {
                throw new ArgumentNullException(nameof(verifyServerProcess));
            }

            if ((!LauncherSessionRules.IsValidPipeName(pipeName)) || (launcherPid <= 0) || (gamePid <= 0))
            {
                return LauncherReceiveResult.Failed(ELauncherSessionFailure.InvalidArgs);
            }

            if (timeoutMs <= 0)
            {
                return LauncherReceiveResult.Failed(ELauncherSessionFailure.Timeout);
            }

            long startedTimestamp = Stopwatch.GetTimestamp();
            using (var pipe = new NamedPipeClientStream(LOCAL_HOST, pipeName, PipeDirection.InOut, PipeOptions.None))
            {
                LauncherReceiveResult connectFailure = TryConnect(pipe, timeoutMs);
                if (connectFailure != null)
                {
                    return connectFailure;
                }

                // 같은 이름으로 먼저 파이프를 연 다른 프로세스에 토큰을 받거나 보내지 않도록, 읽기 전에 서버 PID를 본다
                if (!verifyServerProcess(pipe, launcherPid))
                {
                    return LauncherReceiveResult.Failed(ELauncherSessionFailure.ServerProcessMismatch);
                }

                return ReceiveAndAck(pipe, gamePid, timeoutMs, startedTimestamp);
            }
        }

        /// <summary> 파이프 서버 프로세스가 기대한 PID인지 본다, Windows가 아니면 항상 false </summary>
        public static bool VerifyServerProcess(PipeStream pipe, int expectedPid)
        {
            if ((pipe == null) || (expectedPid <= 0))
            {
                return false;
            }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint serverPid))
                {
                    return false;
                }

                return serverPid == (uint)expectedPid;
            }
            catch (InvalidOperationException)
            {
                // 연결 전이거나 이미 닫힌 파이프, ObjectDisposedException도 여기로 온다
                return false;
            }
#else
            return false;
#endif
        }

        private static LauncherReceiveResult TryConnect(NamedPipeClientStream pipe, int timeoutMs)
        {
            try
            {
                pipe.Connect(timeoutMs);
            }
            catch (TimeoutException)
            {
                return LauncherReceiveResult.Failed(ELauncherSessionFailure.ConnectFailed, nameof(TimeoutException));
            }
            catch (IOException e)
            {
                return LauncherReceiveResult.Failed(ELauncherSessionFailure.ConnectFailed, e.GetType().Name);
            }
            catch (UnauthorizedAccessException e)
            {
                return LauncherReceiveResult.Failed(ELauncherSessionFailure.ConnectFailed, e.GetType().Name);
            }

            return null;
        }

        private static LauncherReceiveResult ReceiveAndAck(PipeStream pipe, int gamePid, int timeoutMs, long startedTimestamp)
        {
            byte[] body;
            bool   isRead;
            try
            {
                isRead = LauncherPipeProtocol.TryReadFrame(pipe, out body);
            }
            catch (IOException e)
            {
                return LauncherReceiveResult.Failed(ELauncherSessionFailure.ReadFailed, e.GetType().Name);
            }

            if (!isRead)
            {
                return LauncherReceiveResult.Failed(ELauncherSessionFailure.ReadFailed);
            }

            long receivedTimestamp = Stopwatch.GetTimestamp();
            bool isParsed          = LauncherSessionRules.TryDeserializeHandoff(body, out LauncherHandoff handoff);

            // 본문에 토큰이 들어 있으므로 다 읽은 즉시 지운다
            Array.Clear(body, 0, body.Length);
            if (!isParsed)
            {
                return LauncherReceiveResult.Failed(ELauncherSessionFailure.InvalidPayload);
            }

            // 쓸 수 없는 세션이면 수신 확인을 보내지 않는다. 런처가 확인을 못 받으면 게임을 끝내고 세션을 폐기한다
            ELauncherSessionFailure rejection = LauncherSessionRules.ValidateHandoff(handoff);
            if (rejection != ELauncherSessionFailure.None)
            {
                return LauncherReceiveResult.Failed(rejection);
            }

            // 감시 타이머가 이미 실패로 바꿨을 시점이면 확인을 보내지 않아 런처와 판단을 맞춘다
            if (ElapsedMs(startedTimestamp) >= timeoutMs)
            {
                return LauncherReceiveResult.Failed(ELauncherSessionFailure.Timeout);
            }

            byte[] ackBody = LauncherSessionRules.SerializeAck(new LauncherHandoffAck(LauncherPipeProtocol.PROTOCOL_VERSION,
                gamePid));
            try
            {
                LauncherPipeProtocol.WriteFrame(pipe, ackBody);
            }
            catch (IOException e)
            {
                return LauncherReceiveResult.Failed(ELauncherSessionFailure.AckFailed, e.GetType().Name);
            }

            return LauncherReceiveResult.Received(handoff, receivedTimestamp);
        }

        private static void RunReceive(SynchronizationContext mainContext, int attempt, string pipeName, int launcherPid,
            int gamePid)
        {
            LauncherReceiveResult result;
            try
            {
                result = ReceiveHandoff(pipeName, launcherPid, gamePid, LauncherPipeProtocol.HANDOFF_TIMEOUT_MS,
                    VerifyServerProcess);
            }
            catch (Exception e)
            {
                // 수신 스레드의 예외는 메인 스레드 로그로 넘겨야 원인을 볼 수 있다. 예외 메시지는 담지 않는다
                result = LauncherReceiveResult.Failed(ELauncherSessionFailure.ReadFailed, e.GetType().Name);
            }

            mainContext.Post(_ => OnReceiveFinished(attempt, result), null);
        }

        private static void OnReceiveFinished(int attempt, LauncherReceiveResult result)
        {
            if (result == null)
            {
                return;
            }

            if (result.IsReceived)
            {
                if (LauncherSession.Complete(attempt, result.Handoff, result.ReceivedTimestamp))
                {
                    StopWatchdog();
                    Debug.Log("[Launcher] 런처 세션을 받았다");
                }

                return;
            }

            if (LauncherSession.Fail(attempt, result.Failure))
            {
                StopWatchdog();
                LogFailure(result.Failure, result.Detail);
            }
        }

        private static void OnWatchdogElapsed(int attempt)
        {
            StopWatchdog();
            if (LauncherSession.Fail(attempt, ELauncherSessionFailure.Timeout))
            {
                LogFailure(ELauncherSessionFailure.Timeout, null);
            }
        }

        private static void FailOnStartup(ELauncherSessionFailure reason)
        {
            LauncherSession.FailBeforeReceiving(reason);
            LogFailure(reason, null);
        }

        private static void LogFailure(ELauncherSessionFailure reason, string detail)
        {
            if (string.IsNullOrEmpty(detail))
            {
                Debug.LogError($"[Launcher] 런처 세션을 받지 못했다: {reason}");
                return;
            }

            Debug.LogError($"[Launcher] 런처 세션을 받지 못했다: {reason} ({detail})");
        }

        private static void StopWatchdog()
        {
            if (_watchdog == null)
            {
                return;
            }

            _watchdog.Dispose();
            _watchdog = null;
        }

        private static bool IsWindowsRuntime()
        {
            return (Application.platform == RuntimePlatform.WindowsPlayer)
                || (Application.platform == RuntimePlatform.WindowsEditor);
        }

        private static int GetCurrentProcessId()
        {
            using (Process process = Process.GetCurrentProcess())
            {
                return process.Id;
            }
        }

        private static long ElapsedMs(long startedTimestamp)
        {
            return ((Stopwatch.GetTimestamp() - startedTimestamp) * MS_PER_SECOND) / Stopwatch.Frequency;
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
#endif
    }
}
