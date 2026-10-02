// System
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Shinshi.Launcher.Account
{
    /// <summary> .NET이 감싸지 않은 Named Pipe Win32 호출 </summary>
    public static class NamedPipeNative
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 쪽 파이프에 접속한 클라이언트 프로세스 ID, 실패하면 Win32Exception </summary>
        public static int GetClientProcessId(SafePipeHandle pipe)
        {
            if (!GetNamedPipeClientProcessId(pipe, out uint processId))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return (int)processId;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);
    }
}
