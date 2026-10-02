namespace Shinshi.Patch.Client
{
    /// <summary> 게시 폴더 배치의 상대 키, 게시 도구가 만드는 배치와 같아야 한다 </summary>
    public static class PatchKeys
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CHANNELS_FOLDER = "channels";
        public const string RELEASES_FOLDER = "releases";
        public const string BLOBS_FOLDER    = "blobs";
        public const string MANIFEST_FILE   = "manifest.json";
        public const string JSON_EXTENSION  = ".json";

        // blob은 해시 앞 두 글자 폴더로 나눠 한 폴더에 파일이 몰리지 않게 한다
        public const int    BLOB_PREFIX_LENGTH = 2;

        /****************************************
        *                Methods
        ****************************************/

        public static string Channel(string product, string channel)
        {
            return product + PatchPaths.SEPARATOR + CHANNELS_FOLDER + PatchPaths.SEPARATOR + channel + JSON_EXTENSION;
        }

        public static string Manifest(string product, string releaseId)
        {
            return product + PatchPaths.SEPARATOR + RELEASES_FOLDER + PatchPaths.SEPARATOR + releaseId + PatchPaths.SEPARATOR
                + MANIFEST_FILE;
        }

        public static string Blob(string product, string sha256)
        {
            return product + PatchPaths.SEPARATOR + BLOBS_FOLDER + PatchPaths.SEPARATOR + sha256.Substring(0, BLOB_PREFIX_LENGTH)
                + PatchPaths.SEPARATOR + sha256;
        }
    }
}
