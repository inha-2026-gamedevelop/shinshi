namespace Shinshi.Release
{
    /// <summary> 게시 도구의 종료 코드, 0이 아니면 게시가 끝나지 않았다 </summary>
    public static class PackagerExitCodes
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int SUCCESS           = 0;
        public const int USAGE             = 1;
        public const int INVALID_BUILD     = 2;
        public const int RELEASE_REFUSED   = 3;
        public const int KEY_ERROR         = 4;
        public const int OUTPUT_CONFLICT   = 5;
        public const int SEQUENCE_REJECTED = 6;
        public const int IO_FAILURE        = 7;
    }
}
