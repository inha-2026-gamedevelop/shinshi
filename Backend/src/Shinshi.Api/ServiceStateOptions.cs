// System
using System;

namespace Shinshi.Backend
{
    /// <summary> 외부 설정으로 제공하는 초기 서비스 상태, 허용 빌드는 명시적으로 등록한다 </summary>
    public sealed class ServiceStateOptions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SECTION_NAME = "ServiceState";

        /****************************************
        *              Properties
        ****************************************/

        public bool Maintenance { get; set; } = true;
        public string Notice { get; set; } = "서비스 준비 중";
        public string[] DisabledFeatures { get; set; } = Array.Empty<string>();
        public string[] AllowedBuildIds { get; set; } = Array.Empty<string>();
    }
}
