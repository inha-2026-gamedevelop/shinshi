// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Admin
{
    /// <summary> 어드민 작업 효과 트랜잭션의 첫 검사. 행위자를 다시 확인하고 운영자 작업 키 결속(admin_operation_claims)과 맞는지 본다.
    /// 거절이면 그 거절 코드, 통과면 ContractCodes.RejectReason.NONE이다 </summary>
    public static class AdminOperationGuard
    {
        // 인증은 요청 시작에 한 번 했으므로, 그 뒤 비활성화되거나 역할이 낮아진 운영자의 쓰기가 확정되지 않게
        // 효과와 같은 트랜잭션에서 행위자 행을 FOR SHARE로 잠그고 다시 읽는다. 잠금은 커밋까지 남아 그 사이의
        // 비활성화(FOR UPDATE)나 역할 변경(UPDATE)이 이 작업 뒤로 밀린다. FOR KEY SHARE는 역할 UPDATE와 충돌하지 않아 쓰지 않는다.
        // 역할은 인증 때의 역할보다 낮으면 거절한다. 경로의 최소 역할과 효과 안의 역할 판정(일괄 지급 실행 등)이 모두
        // 인증 때 역할로 이미 내려졌으므로 그보다 낮아졌으면 어느 판정도 믿을 수 없다

        /****************************************
        *                Fields
        ****************************************/

        private const string ACTOR_SQL =
            """
            SELECT role AS "Role", disabled_at IS NOT NULL AS "Disabled"
            FROM admin_users WHERE admin_id = @AdminId FOR SHARE;
            """;

        private const string CLAIM_SQL =
            """
            SELECT request_hash FROM admin_operation_claims WHERE admin_id = @AdminId AND operation_id = @OperationId;
            """;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> lockActor가 false면 행위자 검사를 효과가 직접 한다(운영자 비활성화처럼 행위자와 대상을 정해진 순서로 함께
        /// 잠가야 하는 효과). 결속 확인은 항상 한다 </summary>
        public static async Task<int> VerifyAsync(OperationExecutionContext execution, AdminIdentity admin,
            OperationRequest operation, bool lockActor, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(admin);
            ArgumentNullException.ThrowIfNull(operation);
            if (operation.ActorId != admin.AdminId)
            {
                throw new ArgumentException("The operation actor must be the admin.", nameof(operation));
            }

            NpgsqlConnection connection = OperationTransactionGuard.Require(execution, out NpgsqlTransaction transaction);
            if ((lockActor) && (!await LockActorAsync(connection, transaction, admin, cancellationToken)))
            {
                return AdminContractCodes.RejectReason.FORBIDDEN_ROLE;
            }

            string? claimedHash = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(CLAIM_SQL,
                new { admin.AdminId, operation.OperationId }, transaction, cancellationToken: cancellationToken));
            if ((claimedHash != null) && (!string.Equals(claimedHash, operation.RequestHash, StringComparison.Ordinal)))
            {
                return ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT;
            }

            return ContractCodes.RejectReason.NONE;
        }

        /// <summary> 행위자 행을 FOR SHARE로 잠그고 아직 활성이며 역할이 인증 때 이상인지 본다. 잠금은 트랜잭션 끝까지 남는다 </summary>
        internal static async Task<bool> LockActorAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
            AdminIdentity admin, CancellationToken cancellationToken)
        {
            ActorRow? actor = await connection.QuerySingleOrDefaultAsync<ActorRow>(new CommandDefinition(ACTOR_SQL,
                new { admin.AdminId }, transaction, cancellationToken: cancellationToken));
            return (actor != null) && (!actor.Disabled) && (actor.Role >= (int)admin.Role);
        }

        private sealed class ActorRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public int Role { get; set; }
            public bool Disabled { get; set; }
        }
    }
}
