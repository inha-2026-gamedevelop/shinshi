// System
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Patch.Client
{
    /// <summary> 로컬 게시 폴더에서 키를 연다. 테스트와 사내 공유 폴더 배포에 쓴다 </summary>
    public sealed class FileSystemPatchSource : IPatchSource
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int FILE_BUFFER_BYTES = 81920;

        private readonly string _root;

        /****************************************
        *              Properties
        ****************************************/

        public string Root => _root;

        /****************************************
        *                Methods
        ****************************************/

        public FileSystemPatchSource(string root)
        {
            ArgumentException.ThrowIfNullOrEmpty(root);
            _root = Path.GetFullPath(root);
        }

        public Task<Stream> OpenAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 키도 매니페스트 경로와 같은 규칙으로 검사해 게시 폴더 밖 파일을 열지 못하게 한다
            if (!PatchPaths.TryResolveUnderRoot(_root, key, out string? fullPath))
            {
                throw new PatchSourceException(key ?? string.Empty, false, "Patch source key is not a valid relative path.");
            }

            if (!File.Exists(fullPath))
            {
                throw new PatchSourceException(key, true, "Patch source key was not found.");
            }

            Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, FILE_BUFFER_BYTES,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult(stream);
        }
    }
}
