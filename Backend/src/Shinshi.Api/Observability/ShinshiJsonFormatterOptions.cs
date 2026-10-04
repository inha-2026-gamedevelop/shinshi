// Third-party
using Microsoft.Extensions.Logging.Console;

namespace Shinshi.Backend.Observability
{
    /// <summary> 공통 JSON 로그의 프로세스 단위 값. 배포가 Observability__InstanceId 같은 환경 변수로 넣고, 없으면 로그에 null로 남는다 </summary>
    public sealed class ShinshiJsonFormatterOptions : ConsoleFormatterOptions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SECTION_NAME = "Observability";

        /****************************************
        *              Properties
        ****************************************/

        // API 복제본 ID. 게임 서버의 INSTANCE_ID와 달리 API는 이 설정으로만 받는다
        public string? InstanceId     { get; set; }

        // 바이너리의 유일한 빌드 ID. 표시용 버전 문자열로 대체하지 않는다
        public string? BuildId        { get; set; }
        public string? ContentVersion { get; set; }
    }
}
