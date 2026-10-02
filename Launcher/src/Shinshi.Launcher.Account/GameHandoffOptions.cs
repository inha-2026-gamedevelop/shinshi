// System
using System;
using Microsoft.Win32.SafeHandles;

// Project
using Shinshi.Contracts;

namespace Shinshi.Launcher.Account
{
    /// <summary> 게임 세션 전달 설정. 기본값이 설계 규칙이고 시험에서만 바꾼다 </summary>
    public sealed class GameHandoffOptions
    {
        /****************************************
        *              Properties
        ****************************************/

        // 게임 시작부터 수신 확인까지 전체 제한
        public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(LauncherPipeProtocol.HANDOFF_TIMEOUT_MS);

        // -launcher-pid로 넘기는 값. 게임은 파이프 서버 PID가 이것과 같은지 본다
        public int LauncherProcessId { get; init; } = Environment.ProcessId;

        // 파이프에 접속한 클라이언트의 PID. 확인하지 못하면 Win32Exception
        public Func<SafePipeHandle, int> ClientProcessIdReader { get; init; } = NamedPipeNative.GetClientProcessId;
    }
}
