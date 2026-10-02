namespace Shinshi.Patch.Client
{
    /// <summary> 폴더 하나를 매니페스트대로 맞춘 결과 </summary>
    internal sealed class MaterializeStats
    {
        /****************************************
        *              Properties
        ****************************************/

        public EPatchClientError Error           { get; set; }
        public int               DownloadedFiles { get; set; }
        public long              DownloadedBytes { get; set; }
        public int               CopiedFiles     { get; set; }
        public int               RemovedFiles    { get; set; }
    }
}
