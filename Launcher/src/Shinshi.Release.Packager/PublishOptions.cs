namespace Shinshi.Release
{
    /// <summary> publish 명령 옵션, BuildId와 BuildKind와 DisplayVersion은 launcher 제품에서만 쓴다 </summary>
    public sealed class PublishOptions
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string DEFAULT_MIN_LAUNCHER = "1.0.0";

        /****************************************
        *              Properties
        ****************************************/

        public string  BuildFolder    { get; }
        public string  Product        { get; }
        public string  Channel        { get; }
        public long    Sequence       { get; }
        public string  KeyFile        { get; }
        public string  OutFolder      { get; }
        public string? ReleaseId      { get; init; }
        public string  MinLauncher    { get; init; } = DEFAULT_MIN_LAUNCHER;
        public string? BuildId        { get; init; }
        public string? BuildKind      { get; init; }
        public string? DisplayVersion { get; init; }

        /****************************************
        *                Methods
        ****************************************/

        public PublishOptions(string buildFolder, string product, string channel, long sequence, string keyFile, string outFolder)
        {
            BuildFolder = buildFolder;
            Product     = product;
            Channel     = channel;
            Sequence    = sequence;
            KeyFile     = keyFile;
            OutFolder   = outFolder;
        }
    }
}
