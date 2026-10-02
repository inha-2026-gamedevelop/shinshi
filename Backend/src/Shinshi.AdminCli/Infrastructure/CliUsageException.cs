// System
using System;

namespace Shinshi.AdminCli
{
    /// <summary> 인자나 입력 형식 오류. 서버에 보내기 전에 멈추며 종료 코드 1이다 </summary>
    internal sealed class CliUsageException : Exception
    {
        /****************************************
        *                Methods
        ****************************************/

        public CliUsageException(string message) : base(message)
        {
        }
    }
}
