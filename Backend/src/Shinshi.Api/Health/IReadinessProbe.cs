// System
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Backend.Health
{
    /// <summary> 준비 상태 확인에 필요한 DB 왕복 한 번을 수행한다 </summary>
    public interface IReadinessProbe
    {
        Task CheckAsync(CancellationToken cancellationToken);
    }
}
