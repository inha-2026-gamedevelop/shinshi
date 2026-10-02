// System
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.AdminCli
{
    /// <summary> 최상위 명령 하나(하위 명령 묶음 포함). RunAsync는 명령 이름을 뺀 인자를 받는다 </summary>
    internal interface ICliCommand
    {
        /****************************************
        *              Properties
        ****************************************/

        string Name { get; }

        // 도움말에 그대로 찍는 사용법 줄
        IReadOnlyList<string> Usage { get; }

        /****************************************
        *                Methods
        ****************************************/

        // 종료 코드(CliExitCodes)를 돌려준다. 거절과 연결 실패는 예외로 올려 AdminCliApp이 한곳에서 출력한다
        Task<int> RunAsync(CliContext context, CliArguments arguments, CancellationToken cancellationToken);
    }
}
