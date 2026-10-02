// System
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Data.Admin
{
    /// <summary> 추가 전용 운영자 감사 로그 쓰기 </summary>
    public interface IAdminAuditWriter
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 호출자의 활성 트랜잭션에 쓴다. 효과와 같은 콜백에서 부르면 효과가 롤백될 때 감사 행도 함께 사라진다 </summary>
        Task AppendAsync(OperationExecutionContext context, AdminAuditEntry entry, CancellationToken cancellationToken = default);

        /// <summary> 계정 상세 같은 민감 조회처럼 operation 없는 기록을 자체 트랜잭션으로 쓴다 </summary>
        Task AppendStandaloneAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default);
    }
}
