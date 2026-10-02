// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data
{
    /// <summary> 인증된 actor의 멱등 실행 경로. 콜백은 같은 DB 트랜잭션만 쓰며 외부 I/O와 확률 재추첨을 하지 않는다 </summary>
    public sealed class IdempotentOperationExecutor
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int DEFAULT_MAX_ATTEMPTS = 3;
        private const int RETRY_DELAY_MS       = 50;

        private readonly IOperationStore _store;
        private readonly int _maxAttempts;

        /****************************************
        *                Methods
        ****************************************/

        public IdempotentOperationExecutor(IOperationStore store, int maxAttempts = DEFAULT_MAX_ATTEMPTS)
        {
            ArgumentNullException.ThrowIfNull(store);
            if (maxAttempts <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            }

            _store       = store;
            _maxAttempts = maxAttempts;
        }

        /// <summary> 저장된 결과는 그대로 반환한다. DB 경합과 제한 초과는 같은 작업 ID로 횟수를 제한해 재시도한다 </summary>
        public async Task<OperationResult> ExecuteAsync(OperationRequest request,
            Func<OperationExecutionContext, CancellationToken, Task<OperationDecision>> effect,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(effect);

            for (int attempt = 1; ; ++attempt)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await ExecuteOnceAsync(request, effect, cancellationToken);
                }
                catch (Exception exception) when (_store.IsRetryable(exception))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (attempt >= _maxAttempts)
                    {
                        if (PostgresTransactionPolicy.IsTimeout(exception))
                        {
                            throw new RetryableDatabaseException(exception);
                        }

                        throw;
                    }

                    await Task.Delay(RETRY_DELAY_MS * attempt, cancellationToken);
                }
            }
        }

        /// <summary> 효과 없이 저장된 결과만 찾는다. 없으면 null이고 작업 행을 남기지 않는다. 다른 본문의 같은 ID는 IDEMPOTENCY_CONFLICT다.
        /// 점검, 기능 차단처럼 새 작업만 막는 게이트가 재전송에는 처음 결과를 돌려줄 때 쓴다 </summary>
        public async Task<OperationResult?> FindStoredAsync(OperationRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // 새 작업이면 BeginAsync가 넣은 Pending 행은 커밋하지 않은 채 Dispose되어 롤백된다
            await using IOperationScope scope = await _store.BeginAsync(request, cancellationToken);
            StoredOperation? existing = scope.ExistingOperation;
            return existing == null ? null : Replay(request, existing, scope);
        }

        private async Task<OperationResult> ExecuteOnceAsync(OperationRequest request,
            Func<OperationExecutionContext, CancellationToken, Task<OperationDecision>> effect,
            CancellationToken cancellationToken)
        {
            await using IOperationScope scope = await _store.BeginAsync(request, cancellationToken);
            StoredOperation? existing = scope.ExistingOperation;
            if (existing != null)
            {
                return Replay(request, existing, scope);
            }

            OperationDecision decision = await effect(scope.Context, cancellationToken);
            if (decision == null)
            {
                throw new InvalidOperationException("Operation effect returned no decision.");
            }

            if (decision.Status == ContractCodes.OperationStatus.REJECTED)
            {
                await scope.RollbackEffectsAsync(cancellationToken);
            }

            OperationResult result = new OperationResult(request.OperationId.ToString("D"), decision.Status,
                decision.ReasonCode, decision.CommittedRevision, scope.Context.ServerTime.ToUnixTimeMilliseconds(),
                decision.ReceiptId);
            await scope.SaveResultAsync(result, cancellationToken);
            await scope.CommitAsync(cancellationToken);
            return result;
        }

        private static OperationResult Replay(OperationRequest request, StoredOperation existing, IOperationScope scope)
        {
            if (string.Equals(existing.RequestHash, request.RequestHash, StringComparison.Ordinal))
            {
                return existing.Result;
            }

            return new OperationResult(request.OperationId.ToString("D"),
                ContractCodes.OperationStatus.REJECTED, ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT,
                existing.Result.CommittedRevision, scope.Context.ServerTime.ToUnixTimeMilliseconds(), string.Empty);
        }
    }
}



