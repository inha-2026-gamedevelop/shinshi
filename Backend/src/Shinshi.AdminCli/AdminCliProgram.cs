// System
using System.Threading.Tasks;

namespace Shinshi.AdminCli
{
    /// <summary> 실행 진입점. 실제 콘솔, 사용자 프로필의 세션 폴더, 소켓 HTTP 핸들러로 AdminCliApp을 돌린다 </summary>
    internal static class AdminCliProgram
    {
        /****************************************
        *                Methods
        ****************************************/

        // API 프로젝트의 최상위 Program과 이름이 겹치면 테스트의 WebApplicationFactory<Program>이 모호해지므로 이름을 달리했다
        public static Task<int> Main(string[] args)
        {
            return AdminCliApp.RunAsync(args, CliContext.CreateDefault());
        }
    }
}
