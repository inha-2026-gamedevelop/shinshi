// System
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts.Admin;
using Shinshi.Data.Ledger;

namespace Shinshi.Data.Admin
{
    /// <summary> 어드민 계정 검색, 상세 조회와 강제 종료. 조회는 읽기 전용 스냅샷이고 강제 종료는 어드민 작업 트랜잭션 안에서 감사와 함께 쓴다.
    /// 조회 결과는 어드민 계약 DTO로 바로 만든다(같은 모양의 데이터 기록을 한 벌 더 두지 않는다) </summary>
    public sealed class PostgresAdminAccountRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int RECENT_SESSION_LIMIT = 20;
        public const int INVITE_LIMIT         = 50;

        // 한 UUID가 계정 하나와 캐릭터 하나에 동시에 맞을 수는 있지만 그 이상은 없다. 초대 해시는 유일하다
        private const string SEARCH_SQL =
            """
            SELECT hit.account_id AS "AccountId", hit.matched_by AS "MatchedBy", hit.matched_id AS "MatchedId",
                account.created_at AS "CreatedAt",
                (SELECT count(*) FROM characters AS c WHERE c.account_id = hit.account_id)::integer AS "CharacterCount",
                EXISTS (SELECT 1 FROM account_leases AS lease WHERE lease.account_id = hit.account_id
                    AND lease.expires_at > clock_timestamp()) AS "Online"
            FROM hits AS hit JOIN accounts AS account ON account.account_id = hit.account_id
            ORDER BY hit.matched_by, hit.account_id;
            """;

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresAdminAccountRepository(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 같은 UUID를 계정 ID와 캐릭터 ID로 함께 찾는다 </summary>
        public Task<IReadOnlyList<AdminAccountSummary>> SearchByIdAsync(Guid id,
            CancellationToken cancellationToken = default)
        {
            return SearchAsync(
                """
                WITH hits AS (
                    SELECT account_id, CAST(@MatchAccount AS text) AS matched_by, account_id AS matched_id
                    FROM accounts WHERE account_id = @Id
                    UNION ALL
                    SELECT account_id, CAST(@MatchCharacter AS text), character_id
                    FROM characters WHERE character_id = @Id)
                """, new
                {
                    Id             = id,
                    MatchAccount   = AdminAccountQuery.MATCH_ACCOUNT_ID,
                    MatchCharacter = AdminAccountQuery.MATCH_CHARACTER_ID
                }, cancellationToken);
        }

        /// <summary> 초대 코드 HMAC으로 찾는다. 원문은 이 계층까지 오지 않는다 </summary>
        public Task<IReadOnlyList<AdminAccountSummary>> SearchByInviteHashAsync(byte[] codeHash,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(codeHash);
            return SearchAsync(
                """
                WITH hits AS (
                    SELECT account_id, CAST(@MatchInvite AS text) AS matched_by, invite_id AS matched_id
                    FROM auth_invites WHERE code_hash = @Hash)
                """, new { Hash = codeHash, MatchInvite = AdminAccountQuery.MATCH_INVITE_CODE }, cancellationToken);
        }

        public async Task<bool> ExistsAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            return await connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM accounts WHERE account_id = @AccountId);",
                new { AccountId = accountId }, cancellationToken: cancellationToken));
        }

        /// <summary> 한 REPEATABLE READ 읽기 전용 스냅샷에서 계정 상세를 모은다. 계정이 없으면 null이다 </summary>
        public async Task<AdminAccountDetailResponse?> ReadDetailAsync(Guid accountId,
            CancellationToken cancellationToken = default)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
                IsolationLevel.RepeatableRead, cancellationToken);
            // 트랜잭션 특성은 첫 문장 전에만 바꿀 수 있다
            await connection.ExecuteAsync(new CommandDefinition("SET TRANSACTION READ ONLY;", transaction: transaction,
                cancellationToken: cancellationToken));
            await PostgresTransactionPolicy.ApplyAsync(connection, transaction, cancellationToken);
            object parameters = new
            {
                AccountId    = accountId,
                SessionLimit = RECENT_SESSION_LIMIT,
                InviteLimit  = INVITE_LIMIT
            };

            AccountRow? account = await QuerySingleOrDefaultAsync<AccountRow>(connection, transaction,
                "SELECT account_id AS \"AccountId\", created_at AS \"CreatedAt\", revision AS \"Revision\" FROM accounts WHERE account_id = @AccountId;",
                parameters, cancellationToken);
            if (account == null)
            {
                return null;
            }

            WalletRow? wallet = await QuerySingleOrDefaultAsync<WalletRow>(connection, transaction,
                """
                SELECT currency AS "Currency", tickets AS "Tickets", tickets_reserved AS "TicketsReserved",
                    action_value AS "ActionValue", action_anchor AS "ActionAnchor", revision AS "Revision"
                FROM account_wallets WHERE account_id = @AccountId;
                """, parameters, cancellationToken);
            IEnumerable<CharacterRow> characters = await QueryAsync<CharacterRow>(connection, transaction,
                """
                SELECT character_id AS "CharacterId", class_id AS "ClassId", essence AS "Essence", shards AS "Shards",
                    revision AS "Revision"
                FROM characters WHERE account_id = @AccountId ORDER BY character_id;
                """, parameters, cancellationToken);
            IEnumerable<ItemRow> items = await QueryAsync<ItemRow>(connection, transaction,
                """
                SELECT definition_id AS "DefinitionId", binding_kind AS "BindingKind", quantity AS "Quantity",
                    reserved AS "Reserved"
                FROM account_items WHERE account_id = @AccountId ORDER BY definition_id, binding_kind;
                """, parameters, cancellationToken);
            LeaseRow? lease = await QuerySingleOrDefaultAsync<LeaseRow>(connection, transaction,
                """
                SELECT lease_id AS "LeaseId", instance_id AS "InstanceId", role AS "Role", expires_at AS "ExpiresAt",
                    expires_at > clock_timestamp() AS "Online"
                FROM account_leases WHERE account_id = @AccountId;
                """, parameters, cancellationToken);
            IEnumerable<SessionRow> sessions = await QueryAsync<SessionRow>(connection, transaction,
                """
                SELECT session.session_id AS "SessionId", participant.participant_id AS "ParticipantId",
                    participant.character_id AS "CharacterId", session.mode AS "Mode", session.map_id AS "MapId",
                    session.state AS "SessionState", participant.state AS "ParticipantState", session.result AS "Result",
                    session.created_at AS "CreatedAt", session.started_at AS "StartedAt", session.closed_at AS "ClosedAt",
                    participant.essence_accrued AS "EssenceAccrued",
                    participant.base_essence_accrued AS "BaseEssenceAccrued"
                FROM session_participants AS participant
                JOIN game_sessions AS session ON session.session_id = participant.session_id
                WHERE participant.account_id = @AccountId
                ORDER BY session.created_at DESC, session.session_id
                LIMIT @SessionLimit;
                """, parameters, cancellationToken);
            int activeFamilies = await connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                SELECT count(*)::integer FROM auth_token_families AS family
                WHERE family.account_id = @AccountId AND family.revoked_at IS NULL
                    AND EXISTS (SELECT 1 FROM auth_refresh_tokens AS refresh
                        WHERE refresh.account_id = family.account_id AND refresh.family_id = family.family_id
                            AND refresh.revoked_at IS NULL AND refresh.consumed_at IS NULL
                            AND refresh.expires_at > clock_timestamp());
                """, parameters, transaction, cancellationToken: cancellationToken));
            IEnumerable<PostgresAdminInviteRepository.InviteRow> invites =
                await QueryAsync<PostgresAdminInviteRepository.InviteRow>(connection, transaction,
                    """
                    SELECT invite_id AS "InviteId", account_id AS "AccountId", expires_at AS "ExpiresAt",
                        max_uses AS "MaxUses", used_count AS "UsedCount", used_at AS "UsedAt", revoked_at AS "RevokedAt"
                    FROM auth_invites WHERE account_id = @AccountId
                    ORDER BY expires_at DESC, invite_id
                    LIMIT @InviteLimit;
                    """, parameters, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new AdminAccountDetailResponse(account.AccountId.ToString("D"), ToUnixMs(account.CreatedAt),
                account.Revision,
                wallet == null ? null : new AdminWalletView(wallet.Currency, wallet.Tickets, wallet.TicketsReserved,
                    wallet.ActionValue, ToUnixMs(wallet.ActionAnchor), wallet.Revision),
                characters.Select(row => new AdminCharacterView(row.CharacterId.ToString("D"), row.ClassId,
                    row.Essence, row.Shards, row.Revision)).ToArray(),
                items.Select(row => new AdminAccountItemView(row.DefinitionId, row.BindingKind, row.Quantity,
                    row.Reserved)).ToArray(),
                lease == null ? null : new AdminAccountLeaseView(lease.LeaseId.ToString("D"),
                    lease.InstanceId.ToString("D"), lease.Role, ToUnixMs(lease.ExpiresAt), lease.Online),
                sessions.Select(row => row.ToView()).ToArray(),
                activeFamilies,
                invites.Select(row => row.ToRecord()).Select(invite => new AdminInviteView(
                    invite.InviteId.ToString("D"), invite.ExpiresAt.ToUnixTimeMilliseconds(), invite.MaxUses,
                    invite.UsedCount, invite.UsedAt?.ToUnixTimeMilliseconds(),
                    invite.RevokedAt?.ToUnixTimeMilliseconds())).ToArray());
        }

        /// <summary> 계정을 잠그고 모든 로그인 묶음, 접근, 갱신, 게임 세션, 미소비 입장 토큰을 폐기한다. revokeInvites면 아직 폐기하지
        /// 않은 초대도 폐기해 남은 사용 횟수로 교환하지 못하게 한다. 현재 접속은 유지한다: 계정 접속 임대는 게임 서버가 쥐고 있으므로
        /// 지우지도 만료시키지도 않는다. 비밀번호 로그인은 막지 않으므로 같은 아이디와 비밀번호로 새 토큰을 받을 수 있다(로그인 차단은
        /// 제재 P1). 계정이 없으면 아무것도 쓰지 않는다 </summary>
        public async Task<AdminKickOutcome> KickAsync(AdminOperationContext context, Guid accountId, bool revokeInvites,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            NpgsqlConnection connection = OperationTransactionGuard.Require(context.Execution,
                out NpgsqlTransaction transaction);
            if (!await AdminAccountTokenRevoker.LockAccountAsync(connection, transaction, accountId, cancellationToken))
            {
                return AdminKickOutcome.NotFound();
            }

            // 임대를 만료시키면 갱신 실패 시 게임 서버가 플레이어를 끊는다는 계약이 아직 없어 기존 접속과 새 로그인이 함께 살 수 있다
            AdminRevokedTokens revoked = await AdminAccountTokenRevoker.RevokeAsync(connection, transaction, accountId,
                revokeInvites, cancellationToken);
            Guid? connectedInstanceId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                """
                SELECT instance_id FROM account_leases
                WHERE account_id = @AccountId AND expires_at > clock_timestamp();
                """, new { AccountId = accountId }, transaction, cancellationToken: cancellationToken));
            await context.AuditAsync(AdminAccountAuditActions.TARGET_ACCOUNT, accountId.ToString("D"),
                new { ActiveTokenFamilies = revoked.Families },
                new
                {
                    ActiveTokenFamilies = 0, Revoked = revoked, RevokeInvites = revokeInvites,
                    ConnectedInstanceId = connectedInstanceId, CurrentSessionKept = true
                },
                cancellationToken);
            return AdminKickOutcome.Kicked(revoked, connectedInstanceId);
        }

        private async Task<IReadOnlyList<AdminAccountSummary>> SearchAsync(string hitsSql, object parameters,
            CancellationToken cancellationToken)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            IEnumerable<SummaryRow> rows = await connection.QueryAsync<SummaryRow>(new CommandDefinition(
                hitsSql + "\n" + SEARCH_SQL, parameters, cancellationToken: cancellationToken));
            return rows.Select(row => new AdminAccountSummary(row.AccountId.ToString("D"), row.MatchedBy,
                row.MatchedId.ToString("D"), ToUnixMs(row.CreatedAt), row.CharacterCount, row.Online)).ToList();
        }

        private static Task<T?> QuerySingleOrDefaultAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction,
            string sql, object parameters, CancellationToken cancellationToken)
        {
            return connection.QuerySingleOrDefaultAsync<T?>(new CommandDefinition(sql, parameters, transaction,
                cancellationToken: cancellationToken));
        }

        private static Task<IEnumerable<T>> QueryAsync<T>(NpgsqlConnection connection, NpgsqlTransaction transaction,
            string sql, object parameters, CancellationToken cancellationToken)
        {
            return connection.QueryAsync<T>(new CommandDefinition(sql, parameters, transaction,
                cancellationToken: cancellationToken));
        }

        private static long ToUnixMs(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        }

        private static long? ToUnixMs(DateTime? value)
        {
            return value.HasValue ? ToUnixMs(value.Value) : null;
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class SummaryRow
        {
            public Guid AccountId { get; set; }
            public string MatchedBy { get; set; } = string.Empty;
            public Guid MatchedId { get; set; }
            public DateTime CreatedAt { get; set; }
            public int CharacterCount { get; set; }
            public bool Online { get; set; }
        }

        private sealed class AccountRow
        {
            public Guid AccountId { get; set; }
            public DateTime CreatedAt { get; set; }
            public long Revision { get; set; }
        }

        private sealed class WalletRow
        {
            public long Currency { get; set; }
            public long Tickets { get; set; }
            public long TicketsReserved { get; set; }
            public long ActionValue { get; set; }
            public DateTime ActionAnchor { get; set; }
            public long Revision { get; set; }
        }

        private sealed class CharacterRow
        {
            public Guid CharacterId { get; set; }
            public int ClassId { get; set; }
            public long Essence { get; set; }
            public long Shards { get; set; }
            public long Revision { get; set; }
        }

        private sealed class ItemRow
        {
            public string DefinitionId { get; set; } = string.Empty;
            public string BindingKind { get; set; } = string.Empty;
            public long Quantity { get; set; }
            public long Reserved { get; set; }
        }

        private sealed class LeaseRow
        {
            public Guid LeaseId { get; set; }
            public Guid InstanceId { get; set; }
            public int Role { get; set; }
            public DateTime ExpiresAt { get; set; }
            public bool Online { get; set; }
        }

        private sealed class SessionRow
        {
            /****************************************
            *              Properties
            ****************************************/

            public Guid SessionId { get; set; }
            public Guid ParticipantId { get; set; }
            public Guid CharacterId { get; set; }
            public string Mode { get; set; } = string.Empty;
            public string MapId { get; set; } = string.Empty;
            public string SessionState { get; set; } = string.Empty;
            public string ParticipantState { get; set; } = string.Empty;
            public string? Result { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime? StartedAt { get; set; }
            public DateTime? ClosedAt { get; set; }
            public long EssenceAccrued { get; set; }
            public long BaseEssenceAccrued { get; set; }

            /****************************************
            *                Methods
            ****************************************/

            public AdminAccountSessionView ToView()
            {
                return new AdminAccountSessionView(SessionId.ToString("D"), ParticipantId.ToString("D"),
                    CharacterId.ToString("D"), Mode, MapId, SessionState, ParticipantState, Result,
                    ToUnixMs(CreatedAt), ToUnixMs(StartedAt), ToUnixMs(ClosedAt), EssenceAccrued, BaseEssenceAccrued);
            }
        }
    }
}
