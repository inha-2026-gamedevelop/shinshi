// System
using System;

namespace Shinshi.AdminCli
{
    /// <summary> API에 닿지 못했거나 쓸 수 있는 로그인 세션이 없다. 종료 코드 3이며 메시지에 토큰을 싣지 않는다 </summary>
    internal sealed class CliUnavailableException : Exception
    {
        /****************************************
        *              Properties
        ****************************************/

        // 응답을 받지 못한 쓰기라 같은 operationId로 다시 보내야 하는지
        public bool ResponseLost { get; }

        /****************************************
        *                Methods
        ****************************************/

        public CliUnavailableException(string message, bool responseLost) : base(message)
        {
            ResponseLost = responseLost;
        }

        public CliUnavailableException(string message, bool responseLost, Exception inner) : base(message, inner)
        {
            ResponseLost = responseLost;
        }
    }
}
