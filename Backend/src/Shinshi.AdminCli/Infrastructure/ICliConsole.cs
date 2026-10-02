// System
using System.IO;

namespace Shinshi.AdminCli
{
    /// <summary> 표준 입출력 추상화. 테스트가 입력을 미리 넣고 출력을 모은다 </summary>
    internal interface ICliConsole
    {
        /****************************************
        *              Properties
        ****************************************/

        TextWriter Out { get; }

        // 안내, 확인 질문, 경고. --json에서도 표준 출력이 JSON만 담도록 여기로 보낸다
        TextWriter Error { get; }

        /****************************************
        *                Methods
        ****************************************/

        // 입력이 끝났으면 null
        string? ReadLine();

        // 비밀번호, TOTP처럼 화면에 남기지 않을 값을 에코 없이 읽는다. 입력이 리디렉션됐으면 한 줄을 그대로 읽는다
        string? ReadSecret(string prompt);
    }
}
