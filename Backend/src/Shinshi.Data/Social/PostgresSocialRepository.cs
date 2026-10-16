// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Social
{
    /// <summary> 친구, 길드, 귓속말, 길드 채팅, 전달함 저장소. 경제 효과가 없어 operations를 쓰지 않고 상태 기준으로 멱등이다.
    /// 쓰기는 모두 SessionDb.BeginAsync(READ COMMITTED) 한 트랜잭션이고 경합, 제한 초과만 SessionDb.RetryAsync로 다시 시도한다.
    /// 잠금 순서: 캐릭터 행(FOR NO KEY UPDATE, character_id 오름차순, 한 문장) -> 길드 행(FOR UPDATE).
    /// 캐릭터 삭제는 계정 -> 삭제 캐릭터 -> 길드 순서로 같은 규칙을 따른다(SocialCharacterCleanup).
    /// 업무 거절은 효과 없이 롤백하므로 부분 적용이 없다 </summary>
    public sealed partial class PostgresSocialRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int NONE = ContractCodes.RejectReason.NONE;

        // 다른 캐릭터의 접속 판정. 마을 입장 교환의 ALREADY_CONNECTED와 같은 조건(만료 전 임대, Offline 아님, heartbeat 15초 안)이다.
        // 바깥 질의가 @Heartbeat를 넘겨야 한다. {0}은 대상 캐릭터 ID 식
        internal const string LIVE_LEASE_FORMAT =
            """
            SELECT lease.instance_id, lease.role FROM account_leases AS lease
            JOIN server_instances AS instance ON instance.instance_id = lease.instance_id AND instance.role = lease.role
            WHERE lease.character_id = {0} AND lease.expires_at > clock_timestamp()
                AND instance.state <> 'Offline' AND instance.heartbeat_at > clock_timestamp() - @Heartbeat
            """;

        private readonly NpgsqlDataSource _dataSource;

        /****************************************
        *              Properties
        ****************************************/

        internal static TimeSpan Heartbeat => TimeSpan.FromSeconds(ServerPoolTimings.HEARTBEAT_TIMEOUT_SECONDS);
        internal static TimeSpan DeliveryTtl => TimeSpan.FromSeconds(SocialLimits.DELIVERY_TTL_SECONDS);
        internal static TimeSpan Resend => TimeSpan.FromSeconds(SocialLimits.DELIVERY_RESEND_SECONDS);
        private static TimeSpan FriendRequestTtl => TimeSpan.FromDays(SocialLimits.FRIEND_REQUEST_DAYS);
        private static TimeSpan GuildRequestTtl => TimeSpan.FromDays(SocialLimits.GUILD_REQUEST_DAYS);

        /****************************************
        *                Methods
        ****************************************/

        public PostgresSocialRepository(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        /// <summary> 만료된 친구 요청, 길드 신청과 초대, 전달함 행을 표마다 최대 batchSize개 지운다. 검사는 만료 행을 이미 없는 것으로 보므로
        /// 정리는 표 크기만 줄인다 </summary>
        public async Task<SocialPurgeResult> PurgeExpiredAsync(int batchSize, CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            }

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            int friends = await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM friend_requests WHERE (requester_character_id, target_character_id) IN (
                    SELECT requester_character_id, target_character_id FROM friend_requests
                    WHERE expires_at <= clock_timestamp() LIMIT @Batch);
                """, new { Batch = batchSize }, cancellationToken: cancellationToken));
            int guilds = await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM guild_join_requests WHERE (guild_id, character_id) IN (
                    SELECT guild_id, character_id FROM guild_join_requests
                    WHERE expires_at <= clock_timestamp() LIMIT @Batch);
                """, new { Batch = batchSize }, cancellationToken: cancellationToken));
            int deliveries = await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM social_deliveries WHERE delivery_id IN (
                    SELECT delivery_id FROM social_deliveries WHERE expires_at <= clock_timestamp() LIMIT @Batch);
                """, new { Batch = batchSize }, cancellationToken: cancellationToken));
            return new SocialPurgeResult(friends, guilds, deliveries);
        }

        // 한 트랜잭션을 열어 body를 실행한다. 업무 거절이면 롤백하고(효과 없음) 확정이면 커밋한다
        private Task<T> RunAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> body,
            Func<T, bool> commit, CancellationToken cancellationToken)
        {
            return SessionDb.RetryAsync(async token =>
            {
                (NpgsqlConnection connection, NpgsqlTransaction transaction) = await SessionDb.BeginAsync(_dataSource, token);
                await using (connection)
                await using (transaction)
                {
                    T result = await body(connection, transaction, token);
                    if (commit(result))
                    {
                        await transaction.CommitAsync(token);
                    }
                    else
                    {
                        await transaction.RollbackAsync(token);
                    }

                    return result;
                }
            }, cancellationToken);
        }

        private Task<SocialActionOutcome> RunActionAsync(
            Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<SocialActionOutcome>> body,
            CancellationToken cancellationToken)
        {
            return RunAsync(body, outcome => outcome.IsCommitted, cancellationToken);
        }

        /// <summary> 살아 있는 캐릭터 행을 DB uuid 오름차순으로 한 문장에서 잠근다. 삭제 중이거나 삭제된 캐릭터는 빠진다 </summary>
        internal static async Task<HashSet<Guid>> LockCharactersAsync(DbConnection connection, DbTransaction transaction,
            IEnumerable<Guid> characterIds, CancellationToken cancellationToken)
        {
            Guid[] ids = characterIds.Where(id => id != Guid.Empty).Distinct().ToArray();
            IEnumerable<Guid> locked = await connection.QueryAsync<Guid>(new CommandDefinition(
                """
                SELECT character_id FROM characters
                WHERE character_id = ANY(@Ids) AND deleted_at IS NULL
                ORDER BY character_id
                FOR NO KEY UPDATE;
                """, new { Ids = ids }, transaction, cancellationToken: cancellationToken));
            return locked.ToHashSet();
        }

        // 행위 캐릭터의 만료 전 임대가 호출 인스턴스, 역할에 있다. 호출 서버는 지금 요청을 보낸 당사자라 heartbeat는 보지 않는다
        private static Task<bool> IsConnectedHereAsync(DbConnection connection, DbTransaction transaction, SocialCaller caller,
            Guid characterId, CancellationToken cancellationToken)
        {
            return connection.QuerySingleAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM account_leases
                    WHERE character_id = @CharacterId AND instance_id = @InstanceId AND role = @Role
                        AND expires_at > clock_timestamp());
                """, new { CharacterId = characterId, caller.InstanceId, caller.Role }, transaction,
                cancellationToken: cancellationToken));
        }

        // 행위 캐릭터를 잠그고(다른 캐릭터와 함께) 호출 인스턴스 접속을 확인한다. 실패면 SOCIAL_NOT_CONNECTED
        private static async Task<(int Reason, HashSet<Guid> Locked)> LockActorAsync(DbConnection connection,
            DbTransaction transaction, SocialCaller caller, Guid actor, IEnumerable<Guid> others,
            CancellationToken cancellationToken)
        {
            HashSet<Guid> locked = await LockCharactersAsync(connection, transaction, others.Append(actor), cancellationToken);
            if ((!locked.Contains(actor)) || (!await IsConnectedHereAsync(connection, transaction, caller, actor, cancellationToken)))
            {
                return (ContractCodes.RejectReason.SOCIAL_NOT_CONNECTED, locked);
            }

            return (NONE, locked);
        }

        // 정규화한 이름 키의 살아 있는 캐릭터. 없으면 null
        private static Task<Guid?> FindCharacterByNameAsync(DbConnection connection, DbTransaction transaction, string? nameKey,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(nameKey))
            {
                return Task.FromResult<Guid?>(null);
            }

            return connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                "SELECT character_id FROM characters WHERE name_key = @NameKey AND deleted_at IS NULL;",
                new { NameKey = nameKey }, transaction, cancellationToken: cancellationToken));
        }

        // 표시 이름. 이름 없는 레거시 캐릭터와 없는 캐릭터는 빈 문자열
        internal static async Task<string> ReadNameAsync(DbConnection connection, DbTransaction transaction, Guid characterId,
            CancellationToken cancellationToken)
        {
            string? name = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
                "SELECT name FROM characters WHERE character_id = @CharacterId;",
                new { CharacterId = characterId }, transaction, cancellationToken: cancellationToken));
            return name ?? string.Empty;
        }

        /// <summary> 접속 중인 대상에게만 알림 전달함 행(kind 2)을 넣고 넣은 대상을 돌려준다. 기본으로 행위자 자신은 뺀다. 오프라인 대상은 다음 스냅샷에서 바뀐 상태를 본다 </summary>
        internal static async Task<IReadOnlyList<Guid>> NotifyAsync(DbConnection connection, DbTransaction transaction,
            IEnumerable<Guid> targets, int notice, Guid actor, string actorName, string guildName,
            CancellationToken cancellationToken, bool includeActor = false)
        {
            Guid[] ids = targets.Where(id => (id != Guid.Empty) && ((includeActor) || (id != actor))).Distinct().ToArray();
            if (ids.Length == 0)
            {
                return Array.Empty<Guid>();
            }

            IEnumerable<Guid> inserted = await connection.QueryAsync<Guid>(new CommandDefinition(
                "INSERT INTO social_deliveries (target_character_id, kind, channel, sender_character_id, sender_name, payload, expires_at)"
                + " SELECT target.id, @Kind, @Channel, @Sender, @SenderName, @Payload, clock_timestamp() + @Ttl"
                + " FROM unnest(@Targets) AS target(id)"
                + " WHERE EXISTS (" + string.Format(LIVE_LEASE_FORMAT, "target.id") + ")"
                + " ORDER BY target.id RETURNING target_character_id;",
                new
                {
                    Kind = SocialCodes.DeliveryKind.NOTICE, Channel = notice, Sender = actor, SenderName = actorName,
                    Payload = guildName, Ttl = DeliveryTtl, Targets = ids, Heartbeat
                }, transaction, cancellationToken: cancellationToken));
            return inserted.OrderBy(id => id).ToArray();
        }
    }
}
