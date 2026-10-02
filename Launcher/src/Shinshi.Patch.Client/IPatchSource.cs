// System
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Patch.Client
{
    /// <summary> 게시 폴더의 상대 키("game/channels/internal.json", "game/blobs/ab/abcd...")로 바이트 스트림을 연다 </summary>
    public interface IPatchSource
    {
        /// <summary> 키의 내용을 연다. 없는 키나 서버 오류는 PatchSourceException, 연결 실패는 IOException 계열로 알린다 </summary>
        // 받은 내용은 믿지 않는다. 크기, 해시, 서명은 PatchClient가 확인한다
        Task<Stream> OpenAsync(string key, CancellationToken cancellationToken);
    }
}
