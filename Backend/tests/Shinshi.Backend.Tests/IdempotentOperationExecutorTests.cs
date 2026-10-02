using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Xunit;

using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Backend.Tests
{
    /// <summary> 멱등 실행기의 재시도, 행위자 경계, 실패 시 효과 취소를 검사한다 </summary>
    public sealed class IdempotentOperationExecutorTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int  SINGLE_EFFECT    = 1;
        private const int  TWO_EFFECTS      = 2;
        private const int  TEST_TIMEOUT_SEC = 5;
        private const long TEST_REVISION    = 9007199254740993L;
        private const long TEST_SERVER_TIME = 1700000000000L;
        private const string OPERATION_KIND = "test-operation";
        private const string ORIGINAL_BODY  = "{\"quantity\":1}";
        private const string DIFFERENT_BODY = "{\"quantity\":2}";
        private const string RECEIPT_ID     = "receipt-placeholder";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 동일 요청 재전송은 최초 결과를 반환하고 효과를 다시 실행하지 않는다 </summary>
        [Fact]
        public async Task ExecuteAsync_SameActorAndOperation_ReturnsOriginalResultOnce()
        {
            using MemoryOperationStore store = new MemoryOperationStore();
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(store);
            OperationRequest request = CreateRequest();
            int calls = 0;

            Task<OperationDecision> Effect(OperationExecutionContext context, CancellationToken cancellationToken)
            {
                ++calls;
                store.StageEffect();
                return Task.FromResult(OperationDecision.Committed(TEST_REVISION, RECEIPT_ID));
            }

            OperationResult first = await executor.ExecuteAsync(request, Effect);
            OperationResult replay = await executor.ExecuteAsync(request, Effect);

            Assert.Equal(SINGLE_EFFECT, calls);
            Assert.Equal(SINGLE_EFFECT, store.CommittedEffects);
            Assert.Equal(SINGLE_EFFECT, store.StoredCount);
            AssertResultEqual(first, replay);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, replay.Status);
            Assert.Equal(TEST_REVISION, replay.CommittedRevision);
        }

        /// <summary> 같은 키의 다른 본문은 원래 효과와 결과를 보존하고 충돌로 거절한다 </summary>
        [Fact]
        public async Task ExecuteAsync_SameKeyDifferentBody_RejectsWithoutReplacingOriginal()
        {
            using MemoryOperationStore store = new MemoryOperationStore();
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(store);
            OperationRequest original = CreateRequest();
            OperationRequest changed = new OperationRequest(original.ActorId, original.OperationId,
                OPERATION_KIND, DIFFERENT_BODY);
            OperationResult first = await executor.ExecuteAsync(original, (context, cancellationToken) =>
            {
                store.StageEffect();
                return Task.FromResult(OperationDecision.Committed(TEST_REVISION, RECEIPT_ID));
            });

            OperationResult conflict = await executor.ExecuteAsync(changed, (context, cancellationToken) =>
                throw new InvalidOperationException("Conflicting requests must not run effects."));
            OperationResult replay = await executor.ExecuteAsync(original, (context, cancellationToken) =>
                throw new InvalidOperationException("Replayed requests must not run effects."));

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, conflict.Status);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, conflict.ReasonCode);
            Assert.Equal(SINGLE_EFFECT, store.CommittedEffects);
            Assert.Equal(SINGLE_EFFECT, store.StoredCount);
            AssertResultEqual(first, replay);
        }

        /// <summary> 같은 operationId도 다른 행위자이면 서로의 결과를 조회하거나 재사용하지 않는다 </summary>
        [Fact]
        public async Task ExecuteAsync_SameOperationDifferentActors_KeepsSeparateResults()
        {
            using MemoryOperationStore store = new MemoryOperationStore();
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(store);
            OperationRequest firstRequest = CreateRequest();
            OperationRequest otherActor = new OperationRequest(Guid.NewGuid(), firstRequest.OperationId,
                OPERATION_KIND, DIFFERENT_BODY);

            OperationResult first = await executor.ExecuteAsync(firstRequest, (context, cancellationToken) =>
            {
                store.StageEffect();
                return Task.FromResult(OperationDecision.Committed(TEST_REVISION, "first-receipt"));
            });
            OperationResult second = await executor.ExecuteAsync(otherActor, (context, cancellationToken) =>
            {
                store.StageEffect();
                return Task.FromResult(OperationDecision.Committed(TEST_REVISION, "other-receipt"));
            });

            Assert.Equal(TWO_EFFECTS, store.CommittedEffects);
            Assert.Equal(TWO_EFFECTS, store.StoredCount);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, second.Status);
            Assert.NotEqual(first.ReceiptId, second.ReceiptId);
        }

        /// <summary> 동시에 온 같은 키의 요청은 효과를 한 번 실행한 뒤 같은 영수증을 받는다 </summary>
        [Fact]
        public async Task ExecuteAsync_ConcurrentSameKey_CommitsExactlyOneEffect()
        {
            using MemoryOperationStore store = new MemoryOperationStore();
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(store);
            OperationRequest request = CreateRequest();
            TaskCompletionSource<bool> entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int calls = 0;

            async Task<OperationDecision> Effect(OperationExecutionContext context, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref calls);
                store.StageEffect();
                entered.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
                return OperationDecision.Committed(TEST_REVISION, RECEIPT_ID);
            }

            Task<OperationResult> first = executor.ExecuteAsync(request, Effect);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(TEST_TIMEOUT_SEC));
            Task<OperationResult> second = executor.ExecuteAsync(request, Effect);
            release.SetResult(true);
            OperationResult[] results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(TEST_TIMEOUT_SEC));

            Assert.Equal(SINGLE_EFFECT, calls);
            Assert.Equal(SINGLE_EFFECT, store.CommittedEffects);
            Assert.Equal(SINGLE_EFFECT, store.StoredCount);
            AssertResultEqual(results[0], results[1]);
        }

        /// <summary> 처리 도중 예외가 나면 작업과 효과가 남지 않아 같은 ID로 다시 시도할 수 있다 </summary>
        [Fact]
        public async Task ExecuteAsync_EffectThrows_RollsBackAndAllowsRetry()
        {
            using MemoryOperationStore store = new MemoryOperationStore();
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(store);
            OperationRequest request = CreateRequest();

            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(request,
                (context, cancellationToken) =>
                {
                    store.StageEffect();
                    throw new InvalidOperationException("Test effect failed.");
                }));

            Assert.Equal(0, store.CommittedEffects);
            Assert.Equal(0, store.StoredCount);
            OperationResult retried = await executor.ExecuteAsync(request, (context, cancellationToken) =>
            {
                store.StageEffect();
                return Task.FromResult(OperationDecision.Committed(TEST_REVISION, RECEIPT_ID));
            });

            Assert.Equal(SINGLE_EFFECT, store.CommittedEffects);
            Assert.Equal(SINGLE_EFFECT, store.StoredCount);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, retried.Status);
        }

        /// <summary> 결과 저장 실패도 효과와 최초 작업 기록을 함께 취소한다 </summary>
        [Fact]
        public async Task ExecuteAsync_SaveResultThrows_RollsBackEffectAndRecord()
        {
            using MemoryOperationStore store = new MemoryOperationStore { FailNextSave = true };
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(store);
            OperationRequest request = CreateRequest();

            Task<OperationDecision> Effect(OperationExecutionContext context, CancellationToken cancellationToken)
            {
                store.StageEffect();
                return Task.FromResult(OperationDecision.Committed(TEST_REVISION, RECEIPT_ID));
            }

            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(request, Effect));
            Assert.Equal(0, store.CommittedEffects);
            Assert.Equal(0, store.StoredCount);

            OperationResult retried = await executor.ExecuteAsync(request, Effect);

            Assert.Equal(SINGLE_EFFECT, store.CommittedEffects);
            Assert.Equal(SINGLE_EFFECT, store.StoredCount);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, retried.Status);
        }

        /// <summary> 거절 전에 쓴 효과도 취소하고 거절 결과만 멱등하게 저장한다 </summary>
        [Fact]
        public async Task ExecuteAsync_RejectedAfterStagedEffect_SavesRejectionWithoutEffect()
        {
            using MemoryOperationStore store = new MemoryOperationStore();
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(store);
            OperationRequest request = CreateRequest();
            int calls = 0;

            Task<OperationDecision> Effect(OperationExecutionContext context, CancellationToken cancellationToken)
            {
                ++calls;
                store.StageEffect();
                return Task.FromResult(OperationDecision.Rejected(ContractCodes.RejectReason.INSUFFICIENT_ITEM));
            }

            OperationResult first = await executor.ExecuteAsync(request, Effect);
            OperationResult replay = await executor.ExecuteAsync(request, Effect);

            Assert.Equal(SINGLE_EFFECT, calls);
            Assert.Equal(0, store.CommittedEffects);
            Assert.Equal(SINGLE_EFFECT, store.StoredCount);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, replay.Status);
            Assert.Equal(ContractCodes.RejectReason.INSUFFICIENT_ITEM, replay.ReasonCode);
            AssertResultEqual(first, replay);
        }

        /// <summary> 취소된 처리에서 쓰던 효과와 키를 남기지 않는다 </summary>
        [Fact]
        public async Task ExecuteAsync_CancelledDuringEffect_RollsBackAndAllowsRetry()
        {
            using MemoryOperationStore store = new MemoryOperationStore();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(store);
            OperationRequest request = CreateRequest();
            TaskCompletionSource<bool> entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<OperationResult> executing = executor.ExecuteAsync(request, async (context, cancellationToken) =>
            {
                store.StageEffect();
                entered.SetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return OperationDecision.Committed(TEST_REVISION, RECEIPT_ID);
            }, cancellation.Token);

            await entered.Task.WaitAsync(TimeSpan.FromSeconds(TEST_TIMEOUT_SEC));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executing);
            Assert.Equal(0, store.CommittedEffects);
            Assert.Equal(0, store.StoredCount);

            OperationResult retried = await executor.ExecuteAsync(request, (context, cancellationToken) =>
            {
                store.StageEffect();
                return Task.FromResult(OperationDecision.Committed(TEST_REVISION, RECEIPT_ID));
            });

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, retried.Status);
            Assert.Equal(SINGLE_EFFECT, store.CommittedEffects);
        }

        /// <summary> 객체 키 순서는 정규화하지만 효과 종류와 값이 다르면 해시가 달라진다 </summary>
        [Fact]
        public void ComputeHash_ReorderedObjectKeys_CanonicalizesWithoutLosingInt64()
        {
            string first = OperationRequestHasher.ComputeHash(OPERATION_KIND,
                "{\"revision\":9007199254740993,\"nested\":{\"a\":1,\"b\":2}}");
            string reordered = OperationRequestHasher.ComputeHash(OPERATION_KIND,
                " { \"nested\": { \"b\":2, \"a\":1 }, \"revision\":9007199254740993 } ");
            string changed = OperationRequestHasher.ComputeHash(OPERATION_KIND,
                "{\"revision\":9007199254740992,\"nested\":{\"a\":1,\"b\":2}}");
            string otherKind = OperationRequestHasher.ComputeHash("other-operation",
                "{\"revision\":9007199254740993,\"nested\":{\"a\":1,\"b\":2}}");

            Assert.Equal(first, reordered);
            Assert.NotEqual(first, changed);
            Assert.NotEqual(first, otherKind);
        }

        /// <summary> 저장소가 재시도 가능한 오류로 분류하면 전체 실행을 제한된 횟수로 다시 시도한다 </summary>
        [Fact]
        public async Task ExecuteAsync_RetryableStoreFailure_RetriesBeforeOneCommittedEffect()
        {
            using MemoryOperationStore store = new MemoryOperationStore { BeginFailuresRemaining = TWO_EFFECTS };
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(store);

            OperationResult result = await executor.ExecuteAsync(CreateRequest(), (context, cancellationToken) =>
            {
                store.StageEffect();
                return Task.FromResult(OperationDecision.Committed(TEST_REVISION, RECEIPT_ID));
            });

            Assert.Equal(TWO_EFFECTS + SINGLE_EFFECT, store.BeginCalls);
            Assert.Equal(SINGLE_EFFECT, store.CommittedEffects);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
        }

        /// <summary> 재시도 한도를 넘긴 오류는 성공으로 바꾸지 않고 호출자에게 전달한다 </summary>
        [Fact]
        public async Task ExecuteAsync_RetryLimitReached_PropagatesWithoutEffect()
        {
            using MemoryOperationStore store = new MemoryOperationStore { BeginFailuresRemaining = TWO_EFFECTS + SINGLE_EFFECT };
            IdempotentOperationExecutor executor = new IdempotentOperationExecutor(store, maxAttempts: TWO_EFFECTS);

            await Assert.ThrowsAsync<RetryableStoreException>(() => executor.ExecuteAsync(CreateRequest(),
                (context, cancellationToken) =>
                    throw new InvalidOperationException("Effects must not run when transactions cannot begin.")));

            Assert.Equal(TWO_EFFECTS, store.BeginCalls);
            Assert.Equal(0, store.CommittedEffects);
            Assert.Equal(0, store.StoredCount);
        }

        private static OperationRequest CreateRequest()
        {
            return new OperationRequest(Guid.NewGuid(), Guid.NewGuid(), OPERATION_KIND, ORIGINAL_BODY);
        }

        private static void AssertResultEqual(OperationResult expected, OperationResult actual)
        {
            Assert.Equal(expected.OperationId, actual.OperationId);
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.ReasonCode, actual.ReasonCode);
            Assert.Equal(expected.CommittedRevision, actual.CommittedRevision);
            Assert.Equal(expected.ServerTimeUnixMs, actual.ServerTimeUnixMs);
            Assert.Equal(expected.ReceiptId, actual.ReceiptId);
        }

        private sealed class RetryableStoreException : Exception { }

        private sealed class MemoryOperationStore : IOperationStore, IDisposable
        {
            private readonly SemaphoreSlim _gate = new SemaphoreSlim(SINGLE_EFFECT, SINGLE_EFFECT);
            private readonly Dictionary<(Guid ActorId, Guid OperationId), StoredOperation> _stored = new();
            private MemoryOperationScope? _activeScope;

            public int CommittedEffects { get; private set; }
            public int StoredCount => _stored.Count;
            public bool FailNextSave { get; set; }
            public int BeginFailuresRemaining { get; set; }
            public int BeginCalls { get; private set; }

            public async Task<IOperationScope> BeginAsync(OperationRequest request, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ++BeginCalls;
                if (BeginFailuresRemaining > 0)
                {
                    --BeginFailuresRemaining;
                    throw new RetryableStoreException();
                }

                await _gate.WaitAsync(cancellationToken);
                _stored.TryGetValue((request.ActorId, request.OperationId), out StoredOperation? existing);
                _activeScope = new MemoryOperationScope(this, request, existing);
                return _activeScope;
            }

            public bool IsRetryable(Exception exception)
            {
                return exception is RetryableStoreException;
            }

            public void StageEffect()
            {
                Assert.NotNull(_activeScope);
                _activeScope.StageEffect();
            }

            public void Dispose()
            {
                _gate.Dispose();
            }

            private sealed class MemoryOperationScope : IOperationScope
            {
                private readonly MemoryOperationStore _store;
                private readonly OperationRequest _request;
                private OperationResult? _result;
                private int _stagedEffects;
                private bool _disposed;

                public StoredOperation? ExistingOperation { get; }
                public OperationExecutionContext Context { get; } = new OperationExecutionContext(null, null,
                    DateTimeOffset.FromUnixTimeMilliseconds(TEST_SERVER_TIME));

                public MemoryOperationScope(MemoryOperationStore store, OperationRequest request, StoredOperation? existing)
                {
                    _store            = store;
                    _request          = request;
                    ExistingOperation = existing;
                }

                public void StageEffect()
                {
                    ++_stagedEffects;
                }

                public Task SaveResultAsync(OperationResult result, CancellationToken cancellationToken)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_store.FailNextSave)
                    {
                        _store.FailNextSave = false;
                        throw new InvalidOperationException("Test save failed.");
                    }

                    _result = result;
                    return Task.CompletedTask;
                }

                public Task RollbackEffectsAsync(CancellationToken cancellationToken)
                {
                    _stagedEffects = 0;
                    return Task.CompletedTask;
                }

                public Task CommitAsync(CancellationToken cancellationToken)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_result != null)
                    {
                        _store._stored[(_request.ActorId, _request.OperationId)] = new StoredOperation(_request.RequestHash, _result);
                    }

                    _store.CommittedEffects += _stagedEffects;
                    _stagedEffects = 0;
                    return Task.CompletedTask;
                }

                public ValueTask DisposeAsync()
                {
                    if (_disposed)
                    {
                        return ValueTask.CompletedTask;
                    }

                    _disposed           = true;
                    _stagedEffects      = 0;
                    _store._activeScope = null;
                    _store._gate.Release();
                    return ValueTask.CompletedTask;
                }
            }
        }
    }
}
