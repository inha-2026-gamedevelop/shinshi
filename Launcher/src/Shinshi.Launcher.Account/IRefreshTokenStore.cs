// System
using System.Diagnostics.CodeAnalysis;

namespace Shinshi.Launcher.Account
{
    /// <summary> 갱신 토큰과 미완료 갱신 요청 ID의 암호화 보관소. 접근 토큰과 초대 코드는 담지 않는다 </summary>
    public interface IRefreshTokenStore
    {
        bool HasToken { get; }

        /// <summary> 저장된 토큰을 읽는다. 없거나 풀 수 없으면 false이고 풀 수 없던 파일은 지운다 </summary>
        bool TryLoad([NotNullWhen(true)] out string? refreshToken);

        /// <summary> 이전 값을 바꿔 쓴다. 디스크, 권한, 암호화 실패면 false </summary>
        bool TrySave(string refreshToken);
        bool TryPrepareRefresh(string refreshToken, out string requestId);

        void Clear();
    }
}
