// System
using System;

namespace Shinshi.Release
{
    // 게시 중간에 중단하고 종료 코드와 메시지를 호출부까지 전달한다
    internal sealed class PackagerException : Exception
    {
        /****************************************
        *              Properties
        ****************************************/

        public int ExitCode { get; }

        /****************************************
        *                Methods
        ****************************************/

        public PackagerException(int exitCode, string message)
            : base(message)
        {
            ExitCode = exitCode;
        }
    }
}
