namespace Shinshi.Release
{
    /// <summary> keygen 명령 옵션 </summary>
    public sealed class KeygenOptions
    {
        /****************************************
        *              Properties
        ****************************************/

        public string OutFolder { get; }

        /****************************************
        *                Methods
        ****************************************/

        public KeygenOptions(string outFolder)
        {
            OutFolder = outFolder;
        }
    }
}
