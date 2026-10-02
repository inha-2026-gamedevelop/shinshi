// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data
{
    /// <summary> 같은 actor와 operation ID의 실행을 직렬화하고 효과와 응답을 한 트랜잭션에 저장하는 경계 </summary>
    public interface IOperationStore
    {
        /****************************************
        *                Methods
        ****************************************/

        Task<IOperationScope> BeginAsync(OperationRequest request, CancellationToken cancellationToken);

        /// <summary> 실제 DB 공급자가 직렬화 실패나 데드락으로 분류한 오류만 전체 트랜잭션을 재시도한다 </summary>
        bool IsRetryable(Exception exception) { return false; }
    }

    /// <summary> 커밋 전 Dispose는 전체 작업을 되돌린다. 거절 시 효과만 되돌리고 응답을 커밋한다 </summary>
    public interface IOperationScope : IAsyncDisposable
    {
        /****************************************
        *              Properties
        ****************************************/

        StoredOperation? ExistingOperation { get; }
        OperationExecutionContext Context { get; }
        /****************************************
        *                Methods
        ****************************************/

        Task RollbackEffectsAsync(CancellationToken cancellationToken);
        Task SaveResultAsync(OperationResult result, CancellationToken cancellationToken);
        Task CommitAsync(CancellationToken cancellationToken);
    }
}


