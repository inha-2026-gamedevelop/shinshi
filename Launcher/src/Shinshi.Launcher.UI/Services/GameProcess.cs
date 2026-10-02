// System
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

// Project
using Shinshi.Launcher.Diagnostics;

namespace Shinshi.Launcher.Services
{
    /// <summary> 런처가 시작한 게임 프로세스 하나를 추적한다. IsRunning은 패치 엔진이 다른 스레드에서 불러도 된다 </summary>
    public sealed class GameProcess : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string EXECUTABLE_NAME = "Shinshi.exe";

        private const string PROCESS_NAME = "Shinshi";

        private Process? _process;
        private int      _isRunning;

        /****************************************
        *              Properties
        ****************************************/

        // 런처가 시작하지 않은 게임(직접 실행, 런처 재시작 전 실행)도 업데이트를 막아야 하므로 이름으로도 확인한다
        public bool IsRunning => (Volatile.Read(ref _isRunning) != 0) || IsAnyGameProcessAlive();

        // 스레드 풀에서 불린다. 받는 쪽이 UI 스레드로 넘긴다
        public event Action<int>? Exited;

        /****************************************
        *                Methods
        ****************************************/

        // arguments에는 세션 전달용 파이프 이름과 런처 PID만 싣는다. 토큰은 같은 사용자의 다른 프로세스가 명령줄로 읽을 수 있어 넣지 않는다
        public bool TryStart(string versionPath, string arguments, out int processId, out string error)
        {
            ArgumentNullException.ThrowIfNull(arguments);

            processId = 0;
            error     = string.Empty;
            if (Volatile.Read(ref _isRunning) != 0)
            {
                error = "게임이 이미 실행 중입니다.";
                return false;
            }

            string executablePath = Path.Combine(versionPath, EXECUTABLE_NAME);
            if (!File.Exists(executablePath))
            {
                error = "게임 실행 파일을 찾을 수 없습니다. 파일 검증 및 복구를 실행해 주세요.";
                return false;
            }

            Process process = new Process();
            process.StartInfo           = new ProcessStartInfo(executablePath, arguments)
            {
                WorkingDirectory = versionPath,
                UseShellExecute  = false
            };
            process.EnableRaisingEvents = true;
            process.Exited             += OnProcessExited;

            // 바로 끝나는 프로세스는 Start가 돌아오기 전에 Exited가 올 수 있어 실행 표시를 먼저 켠다
            _process = process;
            Volatile.Write(ref _isRunning, 1);

            bool isStarted;
            try
            {
                isStarted = process.Start();
            }
            catch (Win32Exception exception)
            {
                LauncherLog.Error("Game start failed.", exception);
                isStarted = false;
            }

            if (isStarted)
            {
                isStarted = TryReadProcessId(process, out processId);
            }

            if (!isStarted)
            {
                Interlocked.CompareExchange(ref _process, null, process);
                Volatile.Write(ref _isRunning, 0);
                process.Exited -= OnProcessExited;
                process.Dispose();
                error = "게임을 시작하지 못했습니다.";
                return false;
            }

            // 이미 끝나 Exited 처리에서 Dispose됐을 수 있어 process의 속성은 더 읽지 않는다
            LauncherLog.Info("Game started. pid=" + processId + " path=" + executablePath);
            return true;
        }

        /// <summary> 이 객체가 시작한 게임이 아직 돌고 있고 PID가 같으면 끝낸다. 세션 전달 실패 때 부른다 </summary>
        public void Kill(int processId)
        {
            Process? process = Volatile.Read(ref _process);
            if (process == null)
            {
                return;
            }

            try
            {
                if ((process.Id != processId) || process.HasExited)
                {
                    return;
                }

                process.Kill(true);
                LauncherLog.Info("Game killed after handoff failure. pid=" + processId);
            }
            catch (Exception exception) when ((exception is InvalidOperationException) || (exception is Win32Exception)
                || (exception is NotSupportedException))
            {
                // 그 사이 끝났거나 Exited 처리에서 Dispose됐다
                LauncherLog.Warn("Game kill skipped: " + exception.Message);
            }
        }

        public void Dispose()
        {
            Process? process = Interlocked.Exchange(ref _process, null);
            if (process == null)
            {
                return;
            }

            // 런처를 닫아도 게임은 계속 돈다. 핸들만 놓는다
            process.Exited -= OnProcessExited;
            process.Dispose();
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            int exitCode = 0;
            Process? process = Interlocked.Exchange(ref _process, null);
            if (process != null)
            {
                exitCode = process.ExitCode;
                process.Exited -= OnProcessExited;
                process.Dispose();
            }

            Volatile.Write(ref _isRunning, 0);
            LauncherLog.Info("Game exited. code=" + exitCode);
            Exited?.Invoke(exitCode);
        }

        // Start 직후 끝난 프로세스는 Exited 처리에서 Dispose돼 Id를 읽지 못한다. 그때는 시작 실패로 본다
        private static bool TryReadProcessId(Process process, out int processId)
        {
            try
            {
                processId = process.Id;
                return true;
            }
            catch (InvalidOperationException)
            {
                processId = 0;
                return false;
            }
        }

        private static bool IsAnyGameProcessAlive()
        {
            Process[] processes = Process.GetProcessesByName(PROCESS_NAME);
            bool isAlive = processes.Length > 0;
            foreach (Process process in processes)
            {
                process.Dispose();
            }

            return isAlive;
        }
    }
}
