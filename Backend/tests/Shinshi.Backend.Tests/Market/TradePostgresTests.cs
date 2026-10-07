// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Tests.Growth;
using Shinshi.Contracts;
using Shinshi.Data.Growth;
using Shinshi.Data.Trade;

namespace Shinshi.Backend.Tests.Market
{
    /// <summary> 1:1 거래 prepare, commit, resolve를 실제 PostgreSQL에서 검사한다: 양방향 원자 이동, 전체 롤백, 지연 보정, 경합 </summary>
    public sealed class TradePostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int CONCURRENT_CALLS = 4;
        private const int PAST_TTL_SECONDS = TradeCodes.TRADE_PREPARE_TTL_SECONDS + 10;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task Commit_BothDirections_MovesEverythingAtomicallyWithScopedLedger()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync(currency: 500, items: Items((MarketTestEnvironment.HERB, 20)));
            GrowthPlayer b = await environment.CreatePlayerAsync(currency: 300,
                items: Items((MarketTestEnvironment.TALISMAN, 6)));
            Guid weapon = await environment.Growth.InsertEquipmentAsync(a, MarketTestEnvironment.WEAPON, 0);
            Guid helm = await environment.Growth.InsertEquipmentAsync(b, "equip_head_common", 0);
            TradeOutcome prepared = await environment.PrepareCommittedAsync(
                MarketTestEnvironment.Offer(a, 100, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 5),
                    MarketTestEnvironment.Equipment(weapon)),
                MarketTestEnvironment.Offer(b, 40, MarketTestEnvironment.Item(MarketTestEnvironment.TALISMAN, 2),
                    new TradeItemLine(MarketCodes.ASSET_EQUIPMENT, "equip_head_common", helm.ToString("D"), 1)));
            TradeRowSnapshot row = await environment.ExecutionAsync(prepared.Execution!.ExecutionId);

            TradeOutcome committed = await environment.Trades.CommitAsync(environment.TownId,
                Guid.Parse(prepared.Execution.ExecutionId), row.CommitOperationId, false);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, committed.Result!.Status);
            Assert.Equal(TradeCodes.ExecutionState.COMMITTED, committed.Execution!.State);
            Assert.Equal(500 - 100 + 40, await environment.CurrencyAsync(a));
            Assert.Equal(300 + 100 - 40, await environment.CurrencyAsync(b));
            Assert.Equal(15, await environment.ItemQuantityAsync(a, MarketTestEnvironment.HERB));
            Assert.Equal(5, await environment.ItemQuantityAsync(b, MarketTestEnvironment.HERB));
            Assert.Equal(2, await environment.ItemQuantityAsync(a, MarketTestEnvironment.TALISMAN));
            Assert.Equal(4, await environment.ItemQuantityAsync(b, MarketTestEnvironment.TALISMAN));
            Assert.Equal(b.CharacterId, await OwnerCharacterAsync(environment, weapon));
            Assert.Equal(a.CharacterId, await OwnerCharacterAsync(environment, helm));
            Assert.Equal(new[] { GrowthAssets.HISTORY_TRADED }, await EventKindsAsync(environment, weapon));

            // 방향별 원장: 주는 쪽 TradeGive(-), 받는 쪽 TradeReceive(+), 같은 계정이 같은 자산을 양방향으로 쓰면 줄이 둘이다
            Assert.Equal(2L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason = @Reason AND asset_kind = 'Currency' AND account_id = @AccountId;",
                new { Reason = ContractCodes.EconomyReason.TRADE_GIVE, a.AccountId }) + await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason = @Reason AND asset_kind = 'Currency' AND account_id = @AccountId;",
                new { Reason = ContractCodes.EconomyReason.TRADE_RECEIVE, a.AccountId }));
            Assert.Equal(1L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE entry_key LIKE 'a2b:%' AND reason = @Reason AND delta = -100;",
                new { Reason = ContractCodes.EconomyReason.TRADE_GIVE }));
            Assert.Equal(1L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE entry_key LIKE 'b2a:%' AND reason = @Reason AND delta = 40 AND account_id = @AccountId;",
                new { Reason = ContractCodes.EconomyReason.TRADE_RECEIVE, a.AccountId }));
            Assert.Equal(0L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason IN (@Give, @Receive) AND actor_kind <> 'Server';",
                new { Give = ContractCodes.EconomyReason.TRADE_GIVE, Receive = ContractCodes.EconomyReason.TRADE_RECEIVE }));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Commit_SameAssetBothDirections_BalancesNetAndLedgerReconciles()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync(currency: 100, items: Items((MarketTestEnvironment.HERB, 9999)));
            GrowthPlayer b = await environment.CreatePlayerAsync(currency: 100, items: Items((MarketTestEnvironment.HERB, 3)));
            TradeOutcome prepared = await environment.PrepareCommittedAsync(
                MarketTestEnvironment.Offer(a, 70, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 5)),
                MarketTestEnvironment.Offer(b, 30, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 3)));

            TradeOutcome committed = await CommitAsync(environment, prepared);

            // a는 가득 찬 상태에서 5를 주고 3을 받으므로 순증감 기준으로 들어간다
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, committed.Result!.Status);
            Assert.Equal(9999 - 5 + 3, await environment.ItemQuantityAsync(a, MarketTestEnvironment.HERB));
            Assert.Equal(3 + 5 - 3, await environment.ItemQuantityAsync(b, MarketTestEnvironment.HERB));
            Assert.Equal(100 - 70 + 30, await environment.CurrencyAsync(a));
            Assert.Equal(100 + 70 - 30, await environment.CurrencyAsync(b));
            Assert.Equal(4L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason IN (@Give, @Receive) AND asset_kind = 'AccountItem';",
                new { Give = ContractCodes.EconomyReason.TRADE_GIVE, Receive = ContractCodes.EconomyReason.TRADE_RECEIVE }));
            Assert.Equal(4L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason IN (@Give, @Receive) AND asset_kind = 'Currency';",
                new { Give = ContractCodes.EconomyReason.TRADE_GIVE, Receive = ContractCodes.EconomyReason.TRADE_RECEIVE }));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Commit_ZeroCurrency_WritesNoCurrencyLedgerLines()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync(items: Items((MarketTestEnvironment.HERB, 4)));
            GrowthPlayer b = await environment.CreatePlayerAsync();
            TradeOutcome prepared = await environment.PrepareCommittedAsync(
                MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 4)),
                MarketTestEnvironment.Offer(b, 0));

            TradeOutcome committed = await CommitAsync(environment, prepared);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, committed.Result!.Status);
            Assert.Equal(4, await environment.ItemQuantityAsync(b, MarketTestEnvironment.HERB));
            Assert.Equal(0L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason IN (@Give, @Receive) AND asset_kind = 'Currency';",
                new { Give = ContractCodes.EconomyReason.TRADE_GIVE, Receive = ContractCodes.EconomyReason.TRADE_RECEIVE }));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Commit_OneSideInsufficient_RollsBackEverythingAndRejectionIsVisible()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync(currency: 500, items: Items((MarketTestEnvironment.HERB, 20)));
            GrowthPlayer b = await environment.CreatePlayerAsync(currency: 10, items: Items((MarketTestEnvironment.TALISMAN, 6)));
            Guid weapon = await environment.Growth.InsertEquipmentAsync(a, MarketTestEnvironment.WEAPON, 0);
            TradeOutcome prepared = await environment.PrepareCommittedAsync(
                MarketTestEnvironment.Offer(a, 100, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 5),
                    MarketTestEnvironment.Equipment(weapon)),
                MarketTestEnvironment.Offer(b, 40, MarketTestEnvironment.Item(MarketTestEnvironment.TALISMAN, 2)));

            TradeOutcome committed = await CommitAsync(environment, prepared);

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, committed.Result!.Status);
            Assert.Equal(ContractCodes.RejectReason.TRADE_INSUFFICIENT_CURRENCY, committed.Result.ReasonCode);
            Assert.Equal((TradeCodes.ExecutionState.REJECTED, ContractCodes.RejectReason.TRADE_INSUFFICIENT_CURRENCY),
                (committed.Execution!.State, committed.Execution.ReasonCode));
            await AssertUntouchedAsync(environment, a, b, weapon);

            // resolve도 같은 확정 거절을 보여 준다
            TradeOutcome resolved = await environment.Trades.ResolveAsync(environment.TownId,
                Guid.Parse(prepared.Execution!.ExecutionId));
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, resolved.Result!.Status);
            Assert.Equal(TradeCodes.ExecutionState.REJECTED, resolved.Execution!.State);
        }

        [PostgresFact]
        public async Task Resolve_LostTerminalWrite_ReconcilesFromStoredCommitResult()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync(items: Items((MarketTestEnvironment.HERB, 2)));
            GrowthPlayer b = await environment.CreatePlayerAsync(currency: 1);
            TradeOutcome prepared = await environment.PrepareCommittedAsync(
                MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 2)),
                MarketTestEnvironment.Offer(b, 50));
            await CommitAsync(environment, prepared);
            string executionId = prepared.Execution!.ExecutionId;

            // 확정 거절을 실행 행에 적는 별도 트랜잭션이 응답 유실로 빠진 상황: 행을 Prepared로 되돌린다
            await environment.Growth.ExecuteAsync(
                """
                UPDATE player_trade_executions SET state = 'Prepared', reason_code = 0, finished_at = NULL
                WHERE execution_id = @ExecutionId;
                """, new { ExecutionId = Guid.Parse(executionId) });
            Assert.Equal(TradeCodes.ExecutionState.PREPARED, (await environment.ExecutionAsync(executionId)).State);

            TradeOutcome resolved = await environment.Trades.ResolveAsync(environment.TownId, Guid.Parse(executionId));

            Assert.Equal(TradeCodes.ExecutionState.REJECTED, resolved.Execution!.State);
            Assert.Equal(ContractCodes.RejectReason.TRADE_INSUFFICIENT_CURRENCY, resolved.Execution.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, resolved.Result!.Status);
            Assert.Equal(2, await environment.ItemQuantityAsync(a, MarketTestEnvironment.HERB));
        }

        [PostgresFact]
        public async Task Prepare_OtherPreparedForEitherAccount_IsBusyUntilItEndsOrExpires()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync(items: Items((MarketTestEnvironment.HERB, 20)));
            GrowthPlayer b = await environment.CreatePlayerAsync(currency: 100);
            GrowthPlayer c = await environment.CreatePlayerAsync(currency: 100);
            TradeOffers first = Offers(a, b);
            TradeOutcome prepared = await environment.PrepareCommittedAsync(first.A, first.B);

            // a가 b 쪽(account_b)으로 들어가는 새 거래도 Busy다
            TradeOutcome busy = await environment.Trades.PrepareAsync(environment.TownId,
                environment.PrepareInput(MarketTestEnvironment.Offer(c, 5), MarketTestEnvironment.Offer(a, 0,
                    MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 1))), false);
            TradeOutcome busySameSide = await environment.Trades.PrepareAsync(environment.TownId,
                environment.PrepareInput(MarketTestEnvironment.Offer(b, 5), MarketTestEnvironment.Offer(c, 0,
                    MarketTestEnvironment.Item("T01-1A", 1))), false);
            Assert.Equal(ContractCodes.RejectReason.TRADE_BUSY, busy.Result!.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TRADE_BUSY, busySameSide.Result!.ReasonCode);
            Assert.Null(busy.Execution);

            // 첫 거래가 만료되면 지연 보정이 Abandoned로 끝내고 새 거래는 통과한다
            await environment.AgePreparedAsync(prepared.Execution!.ExecutionId, PAST_TTL_SECONDS);
            TradeOutcome next = await environment.Trades.PrepareAsync(environment.TownId,
                environment.PrepareInput(MarketTestEnvironment.Offer(c, 5), MarketTestEnvironment.Offer(a, 0,
                    MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 1))), false);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, next.Result!.Status);
            TradeRowSnapshot ended = await environment.ExecutionAsync(prepared.Execution.ExecutionId);
            Assert.Equal((TradeCodes.ExecutionState.ABANDONED, ContractCodes.RejectReason.TRADE_EXPIRED),
                (ended.State, ended.ReasonCode));
        }

        [PostgresFact]
        public async Task Prepare_ConcurrentForSameAccount_OnlyOneBecomesPrepared()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync(items: Items((MarketTestEnvironment.HERB, 20)));
            List<GrowthPlayer> others = new List<GrowthPlayer>();
            for (int index = 0; index < CONCURRENT_CALLS; ++index)
            {
                others.Add(await environment.CreatePlayerAsync(currency: 100));
            }

            TradeOutcome[] outcomes = await Task.WhenAll(others.Select((other, index) =>
                environment.Trades.PrepareAsync(environment.TownId, environment.PrepareInput(
                    index % 2 == 0
                        ? MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 1))
                        : MarketTestEnvironment.Offer(other, 5),
                    index % 2 == 0
                        ? MarketTestEnvironment.Offer(other, 5)
                        : MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 1))),
                    false)));

            Assert.Single(outcomes, outcome => outcome.Result!.Status == ContractCodes.OperationStatus.COMMITTED);
            Assert.All(outcomes.Where(outcome => outcome.Result!.Status == ContractCodes.OperationStatus.REJECTED),
                outcome => Assert.Equal(ContractCodes.RejectReason.TRADE_BUSY, outcome.Result!.ReasonCode));
            Assert.Equal(1L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM player_trade_executions WHERE state = 'Prepared';"));
        }

        [PostgresFact]
        public async Task Commit_AfterTtl_RejectsAsExpiredAndResolveShowsAbandoned()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync(items: Items((MarketTestEnvironment.HERB, 5)));
            GrowthPlayer b = await environment.CreatePlayerAsync(currency: 100);
            TradeOutcome prepared = await environment.PrepareCommittedAsync(
                MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 5)),
                MarketTestEnvironment.Offer(b, 100));
            await environment.AgePreparedAsync(prepared.Execution!.ExecutionId, PAST_TTL_SECONDS);

            TradeOutcome committed = await CommitAsync(environment, prepared);

            Assert.Equal(ContractCodes.RejectReason.TRADE_EXPIRED, committed.Result!.ReasonCode);
            Assert.Equal(TradeCodes.ExecutionState.ABANDONED, committed.Execution!.State);
            Assert.Equal(5, await environment.ItemQuantityAsync(a, MarketTestEnvironment.HERB));
            Assert.Equal(100L, await environment.CurrencyAsync(b));
            TradeOutcome resolved = await environment.Trades.ResolveAsync(environment.TownId,
                Guid.Parse(prepared.Execution.ExecutionId));
            Assert.Equal(TradeCodes.ExecutionState.ABANDONED, resolved.Execution!.State);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, resolved.Result!.Status);
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task AbandonVersusCommit_ExpiredExecution_NothingMovesAndStateIsAbandoned()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            for (int round = 0; round < 3; ++round)
            {
                GrowthPlayer a = await environment.CreatePlayerAsync(items: Items((MarketTestEnvironment.HERB, 5)));
                GrowthPlayer b = await environment.CreatePlayerAsync(currency: 100);
                TradeOutcome prepared = await environment.PrepareCommittedAsync(
                    MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 5)),
                    MarketTestEnvironment.Offer(b, 100));
                await environment.AgePreparedAsync(prepared.Execution!.ExecutionId, PAST_TTL_SECONDS);

                Task<int> sweep = environment.Trades.AbandonExpiredAsync(50);
                Task<TradeOutcome> commit = CommitAsync(environment, prepared);
                await Task.WhenAll(sweep, commit);

                TradeRowSnapshot row = await environment.ExecutionAsync(prepared.Execution.ExecutionId);
                Assert.Equal((TradeCodes.ExecutionState.ABANDONED, ContractCodes.RejectReason.TRADE_EXPIRED),
                    (row.State, row.ReasonCode));
                Assert.Equal(5, await environment.ItemQuantityAsync(a, MarketTestEnvironment.HERB));
                Assert.Equal(100L, await environment.CurrencyAsync(b));
            }

            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task AbandonVersusCommit_NearTtlBoundary_ExactlyOneTerminalOutcomeConsistentWithMoney()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            for (int round = 0; round < 6; ++round)
            {
                GrowthPlayer a = await environment.CreatePlayerAsync(items: Items((MarketTestEnvironment.HERB, 5)));
                GrowthPlayer b = await environment.CreatePlayerAsync(currency: 100);
                TradeOutcome prepared = await environment.PrepareCommittedAsync(
                    MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 5)),
                    MarketTestEnvironment.Offer(b, 100));
                // 만료까지 약 0.3초 남겨 두 작업이 경계에서 겹치게 한다
                await environment.AgePreparedAsync(prepared.Execution!.ExecutionId, TradeCodes.TRADE_PREPARE_TTL_SECONDS - 1);
                await Task.Delay(round * 60);

                Task<int> sweep = environment.Trades.AbandonExpiredAsync(50);
                Task<TradeOutcome> commit = CommitAsync(environment, prepared);
                await Task.WhenAll(sweep, commit);

                TradeRowSnapshot row = await environment.ExecutionAsync(prepared.Execution.ExecutionId);
                OperationResult result = commit.Result.Result!;
                if (row.State == TradeCodes.ExecutionState.COMMITTED)
                {
                    Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
                    Assert.Equal(5, await environment.ItemQuantityAsync(b, MarketTestEnvironment.HERB));
                    Assert.Equal(100L, await environment.CurrencyAsync(a));
                }
                else
                {
                    Assert.Equal(TradeCodes.ExecutionState.ABANDONED, row.State);
                    Assert.Equal(ContractCodes.RejectReason.TRADE_EXPIRED, result.ReasonCode);
                    Assert.Equal(5, await environment.ItemQuantityAsync(a, MarketTestEnvironment.HERB));
                    Assert.Equal(100L, await environment.CurrencyAsync(b));
                }
            }

            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Commit_LeaseMissing_RejectsWithoutMovingAnything()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync(items: Items((MarketTestEnvironment.HERB, 5)));
            GrowthPlayer b = await environment.CreatePlayerAsync(currency: 100);
            TradeOutcome prepared = await environment.PrepareCommittedAsync(
                MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 5)),
                MarketTestEnvironment.Offer(b, 100));
            await environment.Growth.ExecuteAsync("DELETE FROM account_leases WHERE account_id = @AccountId;", new { b.AccountId });

            TradeOutcome committed = await CommitAsync(environment, prepared);

            Assert.Equal(ContractCodes.RejectReason.TRADE_NOT_IN_TOWN, committed.Result!.ReasonCode);
            Assert.Equal(TradeCodes.ExecutionState.REJECTED, committed.Execution!.State);
            Assert.Equal(5, await environment.ItemQuantityAsync(a, MarketTestEnvironment.HERB));
            Assert.Equal(100L, await environment.CurrencyAsync(b));

            // prepare도 임대가 없으면 거절한다
            TradeOutcome blocked = await environment.Trades.PrepareAsync(environment.TownId, environment.PrepareInput(
                MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 1)),
                MarketTestEnvironment.Offer(b, 1)), false);
            Assert.Equal(ContractCodes.RejectReason.TRADE_NOT_IN_TOWN, blocked.Result!.ReasonCode);

            // 다른 마을이 호출해도 그 행을 보거나 확정할 수 없다
            Assert.Equal(ETradeLookup.NotFound, (await environment.Trades.CommitAsync(Guid.NewGuid(),
                Guid.Parse(prepared.Execution!.ExecutionId), (await environment.ExecutionAsync(prepared.Execution.ExecutionId))
                    .CommitOperationId, false)).Lookup);
            Assert.Equal(ETradeLookup.NotFound, (await environment.Trades.ResolveAsync(Guid.NewGuid(),
                Guid.Parse(prepared.Execution.ExecutionId))).Lookup);
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Commit_ReplayAndConcurrentCalls_ApplyOnceAndReturnStoredResult()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync(items: Items((MarketTestEnvironment.HERB, 5)));
            GrowthPlayer b = await environment.CreatePlayerAsync(currency: 100);
            TradeOutcome prepared = await environment.PrepareCommittedAsync(
                MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 5)),
                MarketTestEnvironment.Offer(b, 100));

            TradeOutcome[] outcomes = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_CALLS).Select(_ =>
                CommitAsync(environment, prepared)));
            TradeOutcome replay = await CommitAsync(environment, prepared);

            Assert.All(outcomes.Append(replay), outcome =>
            {
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED, outcome.Result!.Status);
                Assert.Equal(outcomes[0].Result!.ReceiptId, outcome.Result.ReceiptId);
                Assert.Equal(TradeCodes.ExecutionState.COMMITTED, outcome.Execution!.State);
            });
            Assert.Equal(5, await environment.ItemQuantityAsync(b, MarketTestEnvironment.HERB));
            Assert.Equal(0, await environment.ItemQuantityAsync(a, MarketTestEnvironment.HERB));
            Assert.Equal(100L, await environment.CurrencyAsync(a));
            Assert.Equal(2L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason IN (@Give, @Receive) AND asset_kind = 'AccountItem';",
                new { Give = ContractCodes.EconomyReason.TRADE_GIVE, Receive = ContractCodes.EconomyReason.TRADE_RECEIVE }));

            // resolve는 확정된 실행을 그대로 돌려준다
            TradeOutcome resolved = await environment.Trades.ResolveAsync(environment.TownId,
                Guid.Parse(prepared.Execution!.ExecutionId));
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, resolved.Result!.Status);
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Commit_WrongOperationId_IsMismatchAndStoresNothing()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync(items: Items((MarketTestEnvironment.HERB, 5)));
            GrowthPlayer b = await environment.CreatePlayerAsync(currency: 100);
            TradeOutcome prepared = await environment.PrepareCommittedAsync(
                MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 5)),
                MarketTestEnvironment.Offer(b, 100));
            long operations = await environment.Growth.ScalarAsync("SELECT count(*) FROM operations;");

            TradeOutcome mismatch = await environment.Trades.CommitAsync(environment.TownId,
                Guid.Parse(prepared.Execution!.ExecutionId), Guid.NewGuid(), false);

            Assert.Equal(ETradeLookup.OperationMismatch, mismatch.Lookup);
            Assert.Equal(operations, await environment.Growth.ScalarAsync("SELECT count(*) FROM operations;"));
            Assert.Equal(TradeCodes.ExecutionState.PREPARED, (await environment.ExecutionAsync(
                prepared.Execution.ExecutionId)).State);

            // resolve는 만료 전 Prepared를 그대로(Pending) 돌려준다
            TradeOutcome resolved = await environment.Trades.ResolveAsync(environment.TownId,
                Guid.Parse(prepared.Execution.ExecutionId));
            Assert.Equal(ContractCodes.OperationStatus.PENDING, resolved.Result!.Status);
            Assert.Equal(TradeCodes.ExecutionState.PREPARED, resolved.Execution!.State);
        }

        [PostgresFact]
        public async Task Prepare_SameIdReplayAndChangedBody_ReturnStoredAndConflict()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync(items: Items((MarketTestEnvironment.HERB, 5)));
            GrowthPlayer b = await environment.CreatePlayerAsync(currency: 100);
            TradeSideOffer sideA = MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 5));
            TradeSideOffer sideB = MarketTestEnvironment.Offer(b, 100);
            Guid execution = Guid.NewGuid();
            Guid commitOperation = Guid.NewGuid();
            TradePrepareInput input = environment.PrepareInput(sideA, sideB, execution, commitOperation);

            TradeOutcome first = await environment.Trades.PrepareAsync(environment.TownId, input, false);
            TradeOutcome replay = await environment.Trades.PrepareAsync(environment.TownId, input, false);
            TradeOutcome stored = await environment.Trades.PrepareAsync(environment.TownId, input, true);
            TradeOutcome changed = await environment.Trades.PrepareAsync(environment.TownId, environment.PrepareInput(sideA,
                MarketTestEnvironment.Offer(b, 99), execution, commitOperation), false);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Result!.Status);
            Assert.Equal(first.Result.ReceiptId, replay.Result!.ReceiptId);
            Assert.Equal(first.Result.ReceiptId, stored.Result!.ReceiptId);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, changed.Result!.ReasonCode);
            Assert.Equal(1L, await environment.Growth.ScalarAsync("SELECT count(*) FROM player_trade_executions;"));

            // 처음 보는 operationId는 replayOnly에서 결과가 없다(기능 차단 중 재전송 경로)
            TradeOutcome unseen = await environment.Trades.PrepareAsync(environment.TownId,
                environment.PrepareInput(sideA, sideB), true);
            Assert.Null(unseen.Result);
        }

        [PostgresFact]
        public async Task Prepare_InvalidOffers_AreRejectedWithoutRow()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync(currency: 100,
                items: Items((MarketTestEnvironment.HERB, 5), (MarketTestEnvironment.POTION, 5)));
            GrowthPlayer b = await environment.CreatePlayerAsync(currency: 100);
            Guid enhanced = await environment.Growth.InsertEquipmentAsync(a, MarketTestEnvironment.WEAPON, 2);
            Guid granted = await environment.Growth.InsertEquipmentAsync(a, MarketTestEnvironment.WEAPON, 0,
                origin: GrowthCodes.EquipmentOrigin.INITIAL_GRANT);
            Guid stranger = await environment.Growth.InsertEquipmentAsync(b, MarketTestEnvironment.WEAPON, 0);
            TradeSideOffer empty = MarketTestEnvironment.Offer(b, 0);

            (TradeSideOffer Offer, TradeSideOffer Other, int Reason)[] cases =
            {
                // 물약은 거래 불가
                (MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.POTION, 1)), empty,
                    ContractCodes.RejectReason.TRADE_NOT_TRADABLE),
                (MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Equipment(enhanced)), empty,
                    ContractCodes.RejectReason.TRADE_NOT_TRADABLE),
                (MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Equipment(granted)), empty,
                    ContractCodes.RejectReason.TRADE_NOT_TRADABLE),
                // 남의 장비
                (MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Equipment(stranger)), empty,
                    ContractCodes.RejectReason.TRADE_ITEM_CHANGED),
                // 중복, 수량 0, 음수 엽전, 칸 수 초과, 양쪽 빈 제안
                (MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 1),
                    MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 1)), empty, ContractCodes.RejectReason.TRADE_ITEM_CHANGED),
                (MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 0)), empty,
                    ContractCodes.RejectReason.TRADE_ITEM_CHANGED),
                (MarketTestEnvironment.Offer(a, -1), empty, ContractCodes.RejectReason.TRADE_ITEM_CHANGED),
                (MarketTestEnvironment.Offer(a, 0, Enumerable.Range(0, TradeCodes.TRADE_MAX_ITEMS + 1).Select(_ =>
                    MarketTestEnvironment.Equipment(Guid.NewGuid())).ToArray()), empty,
                    ContractCodes.RejectReason.TRADE_ITEM_CHANGED),
                (MarketTestEnvironment.Offer(a, 0), empty, ContractCodes.RejectReason.TRADE_ITEM_CHANGED)
            };
            foreach ((TradeSideOffer offer, TradeSideOffer other, int reason) in cases)
            {
                TradeOutcome outcome = await environment.Trades.PrepareAsync(environment.TownId,
                    environment.PrepareInput(offer, other), false);
                Assert.Equal((ContractCodes.OperationStatus.REJECTED, reason), (outcome.Result!.Status, outcome.Result.ReasonCode));
                Assert.Null(outcome.Execution);
            }

            TradeOutcome same = await environment.Trades.PrepareAsync(environment.TownId, environment.PrepareInput(
                MarketTestEnvironment.Offer(a, 5), new TradeSideOffer(a.AccountId.ToString("D"), a.CharacterId.ToString("D"),
                    Array.Empty<TradeItemLine>(), 5)), false);
            Assert.Equal(ContractCodes.RejectReason.TRADE_SAME_ACCOUNT, same.Result!.ReasonCode);
            Assert.Equal(0L, await environment.Growth.ScalarAsync("SELECT count(*) FROM player_trade_executions;"));
        }

        [PostgresFact]
        public async Task Commit_ReceiverFull_RejectsWithoutPartialMove()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync(items: Items((MarketTestEnvironment.HERB, 5)));
            GrowthPlayer fullHerbs = await environment.CreatePlayerAsync(currency: 100,
                items: Items((MarketTestEnvironment.HERB, 9999)));
            GrowthPlayer fullSlots = await environment.CreatePlayerAsync(currency: 100);
            Guid weapon = await environment.Growth.InsertEquipmentAsync(a, MarketTestEnvironment.WEAPON, 0);
            for (int index = 0; index < environment.Growth.Content.Equipment.StorageSlotsPerCharacter; ++index)
            {
                await environment.Growth.InsertEquipmentAsync(fullSlots, "equip_head_common", 0);
            }

            TradeOutcome herbs = await CommitAsync(environment, await environment.PrepareCommittedAsync(
                MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 5)),
                MarketTestEnvironment.Offer(fullHerbs, 100)));
            TradeOutcome slots = await CommitAsync(environment, await environment.PrepareCommittedAsync(
                MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Equipment(weapon)),
                MarketTestEnvironment.Offer(fullSlots, 100)));

            Assert.Equal(ContractCodes.RejectReason.TRADE_INVENTORY_FULL, herbs.Result!.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.TRADE_INVENTORY_FULL, slots.Result!.ReasonCode);
            Assert.Equal(5, await environment.ItemQuantityAsync(a, MarketTestEnvironment.HERB));
            Assert.Equal(100L, await environment.CurrencyAsync(fullHerbs));
            Assert.Equal(100L, await environment.CurrencyAsync(fullSlots));
            Assert.Equal(a.CharacterId, await OwnerCharacterAsync(environment, weapon));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Commit_ItemsChangedAfterPrepare_RejectsAndEscrowedEquipmentIsNotTradable()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync(items: Items((MarketTestEnvironment.HERB, 5)));
            GrowthPlayer b = await environment.CreatePlayerAsync(currency: 100);
            Guid weapon = await environment.Growth.InsertEquipmentAsync(a, MarketTestEnvironment.WEAPON, 0);
            TradeOutcome prepared = await environment.PrepareCommittedAsync(
                MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 5),
                    MarketTestEnvironment.Equipment(weapon)),
                MarketTestEnvironment.Offer(b, 100));

            // prepare 뒤 경매장에 등록해 보관 중이 되었다. 보유 수량도 줄었다
            MarketTestEnvironment.Listed(await environment.ListEquipmentAsync(a, weapon, 100));
            MarketTestEnvironment.Listed(await environment.ListAsync(a, MarketTestEnvironment.HERB, 3, 10));

            TradeOutcome committed = await CommitAsync(environment, prepared);

            Assert.Equal(ContractCodes.RejectReason.TRADE_NOT_TRADABLE, committed.Result!.ReasonCode);
            Assert.Equal(TradeCodes.ExecutionState.REJECTED, committed.Execution!.State);
            Assert.Equal(2, await environment.ItemQuantityAsync(a, MarketTestEnvironment.HERB));
            Assert.Equal(100L, await environment.CurrencyAsync(b));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Prepare_EquipmentDefinitionIdMismatch_RejectsWithoutMovement()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync();
            GrowthPlayer b = await environment.CreatePlayerAsync(currency: 100);
            Guid weapon = await environment.Growth.InsertEquipmentAsync(a, MarketTestEnvironment.WEAPON, 0);

            TradeOutcome prepared = await environment.Trades.PrepareAsync(environment.TownId,
                environment.PrepareInput(MarketTestEnvironment.Offer(a, 0,
                    new TradeItemLine(MarketCodes.ASSET_EQUIPMENT, "equip_head_common", weapon.ToString("D"), 1)),
                    MarketTestEnvironment.Offer(b, 100)), false);

            Assert.Equal(ContractCodes.RejectReason.TRADE_ITEM_CHANGED, prepared.Result!.ReasonCode);
            Assert.Equal(a.CharacterId, await OwnerCharacterAsync(environment, weapon));
            Assert.Equal(100L, await environment.CurrencyAsync(b));
            await environment.AssertReconciledAsync();
        }

        [PostgresFact]
        public async Task Commit_EquipmentDefinitionIdMismatch_RejectsWithoutMovement()
        {
            await using MarketTestEnvironment environment = await MarketTestEnvironment.CreateAsync();
            GrowthPlayer a = await environment.CreatePlayerAsync();
            GrowthPlayer b = await environment.CreatePlayerAsync(currency: 100);
            Guid weapon = await environment.Growth.InsertEquipmentAsync(a, MarketTestEnvironment.WEAPON, 0);
            TradeOutcome prepared = await environment.PrepareCommittedAsync(
                MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Equipment(weapon)),
                MarketTestEnvironment.Offer(b, 100));

            // prepare 뒤 실제 장비의 정의가 저장된 제안과 달라진 상황을 만든다
            await environment.Growth.ExecuteAsync(
                "UPDATE equipment_instances SET definition_id = 'equip_head_common' WHERE item_id = @ItemId;",
                new { ItemId = weapon });

            TradeOutcome committed = await CommitAsync(environment, prepared);

            Assert.Equal(ContractCodes.RejectReason.TRADE_ITEM_CHANGED, committed.Result!.ReasonCode);
            Assert.Equal(TradeCodes.ExecutionState.REJECTED, committed.Execution!.State);
            Assert.Equal(a.CharacterId, await OwnerCharacterAsync(environment, weapon));
            Assert.Equal(100L, await environment.CurrencyAsync(b));
            await environment.AssertReconciledAsync();
        }

        [Fact]
        public void OfferRules_InvalidDefinitionId_IsRejected()
        {
            TradeSideOffer empty = new TradeSideOffer(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
                Array.Empty<TradeItemLine>(), 5);
            string tooLong = new string('x', 65);
            foreach (string definitionId in new[] { "", null!, tooLong })
            {
                TradeSideOffer equipment = new TradeSideOffer(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
                    new[] { new TradeItemLine(MarketCodes.ASSET_EQUIPMENT, definitionId, Guid.NewGuid().ToString("D"), 1) }, 0);
                TradeSideOffer account = new TradeSideOffer(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
                    new[] { new TradeItemLine(MarketCodes.ASSET_ACCOUNT_ITEM, definitionId, null, 1) }, 0);
                Assert.Equal(ContractCodes.RejectReason.TRADE_ITEM_CHANGED, TradeOfferRules.Validate(equipment, empty));
                Assert.Equal(ContractCodes.RejectReason.TRADE_ITEM_CHANGED, TradeOfferRules.Validate(account, empty));
            }
        }

        [Fact]
        public void OfferRules_FormatChecks_MatchContract()
        {
            TradeSideOffer valid = new TradeSideOffer(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
                new[] { MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 1) }, 1);
            TradeSideOffer empty = new TradeSideOffer(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
                Array.Empty<TradeItemLine>(), 0);
            TradeSideOffer overMax = new TradeSideOffer(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"),
                Array.Empty<TradeItemLine>(), TradeOfferRules.MAX_CURRENCY + 1);

            Assert.Equal(ContractCodes.RejectReason.NONE, TradeOfferRules.Validate(valid, empty));
            Assert.Equal(ContractCodes.RejectReason.TRADE_ITEM_CHANGED, TradeOfferRules.Validate(empty, empty));
            Assert.Equal(ContractCodes.RejectReason.TRADE_ITEM_CHANGED, TradeOfferRules.Validate(valid, overMax));
            Assert.Equal(TradeCodes.SCOPE_A_TO_B, TradeOfferRules.ScopeOf(true));
            Assert.Equal(TradeCodes.SCOPE_B_TO_A, TradeOfferRules.ScopeOf(false));
        }

        private static Task<TradeOutcome> CommitAsync(MarketTestEnvironment environment, TradeOutcome prepared)
        {
            return CommitCoreAsync(environment, prepared);
        }

        private static async Task<TradeOutcome> CommitCoreAsync(MarketTestEnvironment environment, TradeOutcome prepared)
        {
            TradeRowSnapshot row = await environment.ExecutionAsync(prepared.Execution!.ExecutionId);
            return await environment.Trades.CommitAsync(environment.TownId, Guid.Parse(prepared.Execution.ExecutionId),
                row.CommitOperationId, false);
        }

        private static TradeOffers Offers(GrowthPlayer a, GrowthPlayer b)
        {
            return new TradeOffers(MarketTestEnvironment.Offer(a, 0, MarketTestEnvironment.Item(MarketTestEnvironment.HERB, 1)),
                MarketTestEnvironment.Offer(b, 10));
        }

        private static Dictionary<string, long> Items(params (string Id, long Quantity)[] items)
        {
            return items.ToDictionary(item => item.Id, item => item.Quantity);
        }

        private static async Task AssertUntouchedAsync(MarketTestEnvironment environment, GrowthPlayer a, GrowthPlayer b,
            Guid weapon)
        {
            Assert.Equal(500L, await environment.CurrencyAsync(a));
            Assert.Equal(10L, await environment.CurrencyAsync(b));
            Assert.Equal(20, await environment.ItemQuantityAsync(a, MarketTestEnvironment.HERB));
            Assert.Equal(0, await environment.ItemQuantityAsync(b, MarketTestEnvironment.HERB));
            Assert.Equal(6, await environment.ItemQuantityAsync(b, MarketTestEnvironment.TALISMAN));
            Assert.Equal(a.CharacterId, await OwnerCharacterAsync(environment, weapon));
            Assert.Equal(0L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason IN (@Give, @Receive);",
                new { Give = ContractCodes.EconomyReason.TRADE_GIVE, Receive = ContractCodes.EconomyReason.TRADE_RECEIVE }));
            Assert.Equal(0L, await environment.Growth.ScalarAsync(
                "SELECT count(*) FROM item_history WHERE event_kind = @Kind;", new { Kind = GrowthAssets.HISTORY_TRADED }));
            await environment.AssertReconciledAsync();
        }

        private static async Task<Guid> OwnerCharacterAsync(MarketTestEnvironment environment, Guid itemId)
        {
            await using NpgsqlConnection connection = await environment.DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<Guid>("SELECT character_id FROM equipment_instances WHERE item_id = @ItemId;",
                new { ItemId = itemId });
        }

        private static async Task<string[]> EventKindsAsync(MarketTestEnvironment environment, Guid itemId)
        {
            await using NpgsqlConnection connection = await environment.DataSource.OpenConnectionAsync();
            return (await connection.QueryAsync<string>(
                "SELECT event_kind FROM item_history WHERE item_id = @ItemId ORDER BY seq;", new { ItemId = itemId })).ToArray();
        }

        private sealed class TradeOffers
        {
            public TradeSideOffer A { get; }
            public TradeSideOffer B { get; }

            public TradeOffers(TradeSideOffer a, TradeSideOffer b)
            {
                A = a;
                B = b;
            }
        }
    }
}
