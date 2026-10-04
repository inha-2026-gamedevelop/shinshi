// System
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Newtonsoft.Json;
using Npgsql;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Inbox;
using Shinshi.Data.Ledger;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Admin.Grants
{
    /// <summary> 운영자 개인 재화 조정. 증가는 AdminGrant, 감소는 AdminRevoke 원장으로 남기고, 감소는 가용량(잔액 - 예약분)까지만 뺀다.
    /// 잔액을 음수로 만들지 않고 모자란 양은 shortfall로 감사에 남긴다 </summary>
    public sealed class PostgresAdminAdjustmentRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        // 조정은 revision 계약이 없어 0으로 확정한다
        private const long   NO_REVISION = 0;
        private const string ADMIN_ACTOR = "Admin";
        private const string ENTRY_KEY   = "adjustment";

        private readonly NpgsqlDataSource _dataSource;
        private readonly IEconomyLedgerEntryWriter _ledger;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresAdminAdjustmentRepository(NpgsqlDataSource dataSource, IEconomyLedgerEntryWriter ledger)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(ledger);
            _dataSource = dataSource;
            _ledger     = ledger;
        }

        /// <summary> 계정이나 그 계정의 캐릭터가 없으면 NOT_FOUND다. 가용량이 0이라 아무것도 못 뺀 감소도 감사와 함께 확정한다 </summary>
        public async Task<OperationDecision> AdjustAsync(AdminOperationContext context, AdminAdjustment adjustment,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(adjustment);
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context.Execution);

            // 잠금 순서: 계정 -> 지갑 또는 캐릭터 또는 아이템. 정산, 우편 수령과 같다
            if (await SessionDb.LockAccountsAsync(connection, transaction, new[] { adjustment.AccountId },
                cancellationToken) != 1)
            {
                return OperationDecision.Rejected(AdminContractCodes.RejectReason.NOT_FOUND);
            }
            if ((adjustment.CharacterId != null) && !await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM characters
                    WHERE character_id = @CharacterId AND account_id = @AccountId AND deleted_at IS NULL);
                """,
                new { adjustment.CharacterId, adjustment.AccountId }, transaction, cancellationToken: cancellationToken)))
            {
                return OperationDecision.Rejected(AdminContractCodes.RejectReason.NOT_FOUND);
            }

            EconomyAsset asset = RewardLines.ToAsset(adjustment.AccountId, adjustment.CharacterId, adjustment.AssetKind,
                adjustment.AssetId, adjustment.BindingKind);
            AdminAdjustmentResult result = new AdminAdjustmentResult
            {
                AssetKind      = adjustment.AssetKind,
                AssetId        = adjustment.AssetId,
                BindingKind    = adjustment.BindingKind,
                CharacterId    = adjustment.CharacterId,
                RequestedDelta = adjustment.Delta
            };
            if (adjustment.Delta > 0)
            {
                long after = await RewardBalances.CreditAsync(connection, transaction, asset, adjustment.Delta,
                    cancellationToken);
                result.AppliedDelta  = adjustment.Delta;
                result.BalanceBefore = after - adjustment.Delta;
                result.BalanceAfter  = after;
            }
            else
            {
                BalanceDebit debit = await RewardBalances.DebitAvailableAsync(connection, transaction, asset,
                    -adjustment.Delta, cancellationToken);
                result.AppliedDelta  = -debit.Applied;
                result.Shortfall     = -adjustment.Delta - debit.Applied;
                result.BalanceBefore = debit.BalanceBefore;
                result.BalanceAfter  = debit.BalanceAfter;
                result.Reserved      = debit.Reserved;
            }

            // 원장 delta는 0일 수 없으므로 실제로 바뀐 양이 있을 때만 쓴다
            if (result.AppliedDelta != 0)
            {
                Guid ledgerId = Guid.NewGuid();
                int reason = result.AppliedDelta > 0
                    ? ContractCodes.EconomyReason.ADMIN_GRANT : ContractCodes.EconomyReason.ADMIN_REVOKE;
                await _ledger.AppendAsync(context.Execution, new EconomyLedgerEntry(ledgerId, ENTRY_KEY, asset,
                    result.AppliedDelta, result.BalanceAfter, reason, AdminGrantAuditActions.ADJUSTMENT_SOURCE_TYPE,
                    context.OperationId.ToString("D"), ADMIN_ACTOR, context.Admin.AdminId, context.OperationId),
                    cancellationToken);
                result.LedgerId = ledgerId;
            }

            string target = adjustment.AccountId.ToString("D");
            await context.AuditAsync(AdminGrantAuditActions.TARGET_ACCOUNT, target,
                new { Balance = result.BalanceBefore }, result, cancellationToken);
            return OperationDecision.Committed(NO_REVISION, target);
        }

        /// <summary> 확정한 조정 결과를 그 작업의 감사 행에서 읽는다. 거절했거나 다른 운영자의 작업이면 null </summary>
        public async Task<AdminAdjustmentResult?> ReadResultAsync(Guid adminId, Guid operationId,
            CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            string? json = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                """
                SELECT after_state::text FROM admin_audit_log
                WHERE admin_id = @AdminId AND operation_id = @OperationId AND action = @Action;
                """, new { AdminId = adminId, OperationId = operationId, Action = AdminGrantAuditActions.ACCOUNT_ADJUST },
                cancellationToken: cancellationToken));
            return json == null ? null : JsonConvert.DeserializeObject<AdminAdjustmentResult>(json);
        }
    }
}
