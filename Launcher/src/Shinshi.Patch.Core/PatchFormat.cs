namespace Shinshi.Patch
{
    /// <summary> 봉투와 문서의 형식 상수, 게시 도구와 런처가 같은 값을 쓴다 </summary>
    public static class PatchFormat
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int    FORMAT_VERSION   = 1;

        public const string KIND_MANIFEST    = "manifest";
        public const string KIND_CHANNEL     = "channel";

        public const string PRODUCT_GAME     = "game";
        public const string PRODUCT_LAUNCHER = "launcher";

        public const string CHANNEL_INTERNAL = "internal";
        public const string CHANNEL_STABLE   = "stable";

        // 매니페스트는 파일 수에 비례해 커지고 채널 포인터는 고정 크기다.
        // 상한은 파싱 전에 걸러 거대한 응답으로 메모리를 소모시키는 것을 막는다
        public const int    MAX_MANIFEST_ENVELOPE_BYTES = 16 * 1024 * 1024;
        public const int    MAX_CHANNEL_ENVELOPE_BYTES  = 64 * 1024;

        /****************************************
        *                Methods
        ****************************************/

        public static bool IsKnownKind(string? kind) { return (kind == KIND_MANIFEST) || (kind == KIND_CHANNEL); }

        public static bool IsKnownProduct(string? product) { return (product == PRODUCT_GAME) || (product == PRODUCT_LAUNCHER); }

        public static bool IsKnownChannel(string? channel) { return (channel == CHANNEL_INTERNAL) || (channel == CHANNEL_STABLE); }

        /// <summary> 종류별 봉투 크기 상한, 모르는 종류는 0 </summary>
        public static int GetMaxEnvelopeBytes(string kind)
        {
            switch (kind)
            {
                case KIND_MANIFEST:
                    return MAX_MANIFEST_ENVELOPE_BYTES;
                case KIND_CHANNEL:
                    return MAX_CHANNEL_ENVELOPE_BYTES;
                default:
                    return 0;
            }
        }
    }
}
