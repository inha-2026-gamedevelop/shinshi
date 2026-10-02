namespace Shinshi.Backend
{
    /// <summary> 인증 교환에 허용할 프로토콜과 콘텐츠 신원, 배포 환경에서 명시적으로 설정한다 </summary>
    public sealed class AuthBuildOptions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string SECTION_NAME = "AuthBuild";

        /****************************************
        *              Properties
        ****************************************/

        public int ProtocolVersion { get; set; }
        public string ContentHash { get; set; } = string.Empty;
    }
}
