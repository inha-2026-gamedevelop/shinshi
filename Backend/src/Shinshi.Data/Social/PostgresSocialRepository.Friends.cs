// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Social
{
    /// <summary> 친구 요청, 수락, 거절, 취소, 삭제. 관계 두 행은 같은 트랜잭션에서 넣고 지운다 </summary>
    public sealed partial class PostgresSocialRepository
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 이름으로 친구 요청. 반대 방향 요청이 있으면 곧바로 친구가 되고, 같은 요청이 이미 있으면 기한만 늘린다.
        /// nameKey가 null이면(이름 규칙 위반) 대상 없음이다 </summary>
        public Task<SocialActionOutcome> RequestFriendAsync(SocialCaller caller, Guid actor, string? nameKey,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return RunActionAsync(async (connection, transaction, token) =>
            {
                Guid? target = await FindCharacterByNameAsync(connection, transaction, nameKey, token);
                (int reason, HashSet<Guid> locked) = await LockActorAsync(connection, transaction, caller, actor,
                    target.HasValue ? new[] { target.Value } : Array.Empty<Guid>(), token);
                if (reason != NONE)
                {
                    return SocialActionOutcome.Rejected(reason);
                }
                if ((!target.HasValue) || (!locked.Contains(target.Value)))
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.SOCIAL_TARGET_NOT_FOUND);
                }
                if (target.Value == actor)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.SOCIAL_TARGET_SELF);
                }
                if (await AreFriendsAsync(connection, transaction, actor, target.Value, token))
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.FRIEND_ALREADY);
                }

                string actorName = await ReadNameAsync(connection, transaction, actor, token);
                if (await HasPendingFriendRequestAsync(connection, transaction, target.Value, actor, token))
                {
                    // 서로 요청한 셈이라 곧바로 친구가 된다
                    int limit = await CheckFriendLimitsAsync(connection, transaction, actor, target.Value, token);
                    if (limit != NONE)
                    {
                        return SocialActionOutcome.Rejected(limit);
                    }

                    await MakeFriendsAsync(connection, transaction, actor, target.Value, token);
                    return SocialActionOutcome.Committed(await NotifyAsync(connection, transaction, new[] { target.Value },
                        SocialCodes.Notice.FRIEND_ACCEPTED, actor, actorName, string.Empty, token));
                }

                int refreshed = await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE friend_requests SET expires_at = clock_timestamp() + @Ttl
                    WHERE requester_character_id = @Actor AND target_character_id = @Target
                        AND expires_at > clock_timestamp();
                    """, new { Actor = actor, Target = target.Value, Ttl = FriendRequestTtl }, transaction,
                    cancellationToken: token));
                if (refreshed > 0)
                {
                    // 결과 불명 재전송이거나 다시 누른 요청. 알림은 다시 보내지 않는다
                    return SocialActionOutcome.Committed();
                }

                (long outgoing, long incoming) = await connection.QuerySingleAsync<(long, long)>(new CommandDefinition(
                    """
                    SELECT
                        (SELECT count(*) FROM friend_requests
                            WHERE requester_character_id = @Actor AND expires_at > clock_timestamp()),
                        (SELECT count(*) FROM friend_requests
                            WHERE target_character_id = @Target AND expires_at > clock_timestamp());
                    """, new { Actor = actor, Target = target.Value }, transaction, cancellationToken: token));
                if (outgoing >= SocialLimits.MAX_OUTGOING_FRIEND_REQUESTS)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.FRIEND_REQUEST_LIMIT);
                }
                if (incoming >= SocialLimits.MAX_INCOMING_FRIEND_REQUESTS)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.FRIEND_TARGET_REQUESTS_FULL);
                }

                // 만료된 같은 방향 행이 남아 있으면 새 요청으로 덮어쓴다
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO friend_requests (requester_character_id, target_character_id, expires_at)
                    VALUES (@Actor, @Target, clock_timestamp() + @Ttl)
                    ON CONFLICT (requester_character_id, target_character_id)
                    DO UPDATE SET created_at = clock_timestamp(), expires_at = EXCLUDED.expires_at;
                    """, new { Actor = actor, Target = target.Value, Ttl = FriendRequestTtl }, transaction,
                    cancellationToken: token));
                return SocialActionOutcome.Committed(await NotifyAsync(connection, transaction, new[] { target.Value },
                    SocialCodes.Notice.FRIEND_REQUESTED, actor, actorName, string.Empty, token));
            }, cancellationToken);
        }

        /// <summary> 상대가 보낸 요청을 수락한다. 이미 친구면 성공이다 </summary>
        public Task<SocialActionOutcome> AcceptFriendAsync(SocialCaller caller, Guid actor, Guid other,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return RunActionAsync(async (connection, transaction, token) =>
            {
                (int reason, HashSet<Guid> locked) = await LockActorAsync(connection, transaction, caller, actor,
                    new[] { other }, token);
                if (reason != NONE)
                {
                    return SocialActionOutcome.Rejected(reason);
                }
                if (other == actor)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.SOCIAL_TARGET_SELF);
                }
                if (await AreFriendsAsync(connection, transaction, actor, other, token))
                {
                    return SocialActionOutcome.Committed();
                }
                if ((!locked.Contains(other)) || (!await HasPendingFriendRequestAsync(connection, transaction, other, actor, token)))
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.FRIEND_REQUEST_NOT_FOUND);
                }

                int limit = await CheckFriendLimitsAsync(connection, transaction, actor, other, token);
                if (limit != NONE)
                {
                    return SocialActionOutcome.Rejected(limit);
                }

                await MakeFriendsAsync(connection, transaction, actor, other, token);
                string actorName = await ReadNameAsync(connection, transaction, actor, token);
                return SocialActionOutcome.Committed(await NotifyAsync(connection, transaction, new[] { other },
                    SocialCodes.Notice.FRIEND_ACCEPTED, actor, actorName, string.Empty, token));
            }, cancellationToken);
        }

        /// <summary> 상대가 보낸 요청을 거절한다. 없으면 성공이다 </summary>
        public Task<SocialActionOutcome> DeclineFriendAsync(SocialCaller caller, Guid actor, Guid other,
            CancellationToken cancellationToken = default)
        {
            return DeletePairAsync(caller, actor, other,
                """
                DELETE FROM friend_requests WHERE requester_character_id = @Other AND target_character_id = @Actor;
                """, cancellationToken);
        }

        /// <summary> 내가 보낸 요청을 취소한다. 없으면 성공이다 </summary>
        public Task<SocialActionOutcome> CancelFriendRequestAsync(SocialCaller caller, Guid actor, Guid target,
            CancellationToken cancellationToken = default)
        {
            return DeletePairAsync(caller, actor, target,
                """
                DELETE FROM friend_requests WHERE requester_character_id = @Actor AND target_character_id = @Other;
                """, cancellationToken);
        }

        /// <summary> 친구를 삭제한다(양방향). 이미 아니면 성공이다 </summary>
        public Task<SocialActionOutcome> RemoveFriendAsync(SocialCaller caller, Guid actor, Guid friend,
            CancellationToken cancellationToken = default)
        {
            return DeletePairAsync(caller, actor, friend,
                """
                DELETE FROM friendships
                WHERE (character_id = @Actor AND friend_character_id = @Other)
                    OR (character_id = @Other AND friend_character_id = @Actor);
                """, cancellationToken);
        }

        private Task<SocialActionOutcome> DeletePairAsync(SocialCaller caller, Guid actor, Guid other, string sql,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return RunActionAsync(async (connection, transaction, token) =>
            {
                // 두 캐릭터를 잠가 같은 쌍의 요청, 수락과 줄을 세운다
                (int reason, HashSet<Guid> _) = await LockActorAsync(connection, transaction, caller, actor, new[] { other },
                    token);
                if (reason != NONE)
                {
                    return SocialActionOutcome.Rejected(reason);
                }
                if (other == actor)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.SOCIAL_TARGET_SELF);
                }

                await connection.ExecuteAsync(new CommandDefinition(sql, new { Actor = actor, Other = other }, transaction,
                    cancellationToken: token));
                return SocialActionOutcome.Committed();
            }, cancellationToken);
        }

        private static Task<bool> AreFriendsAsync(DbConnection connection, DbTransaction transaction, Guid actor, Guid other,
            CancellationToken cancellationToken)
        {
            return connection.QuerySingleAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM friendships WHERE character_id = @Actor AND friend_character_id = @Other);
                """, new { Actor = actor, Other = other }, transaction, cancellationToken: cancellationToken));
        }

        private static Task<bool> HasPendingFriendRequestAsync(DbConnection connection, DbTransaction transaction,
            Guid requester, Guid target, CancellationToken cancellationToken)
        {
            return connection.QuerySingleAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM friend_requests
                    WHERE requester_character_id = @Requester AND target_character_id = @Target
                        AND expires_at > clock_timestamp());
                """, new { Requester = requester, Target = target }, transaction, cancellationToken: cancellationToken));
        }

        // 두 캐릭터를 잠근 뒤 세므로 동시 수락이 함께 상한을 넘지 못한다
        private static async Task<int> CheckFriendLimitsAsync(DbConnection connection, DbTransaction transaction, Guid actor,
            Guid other, CancellationToken cancellationToken)
        {
            (long mine, long theirs) = await connection.QuerySingleAsync<(long, long)>(new CommandDefinition(
                """
                SELECT (SELECT count(*) FROM friendships WHERE character_id = @Actor),
                    (SELECT count(*) FROM friendships WHERE character_id = @Other);
                """, new { Actor = actor, Other = other }, transaction, cancellationToken: cancellationToken));
            if (mine >= SocialLimits.MAX_FRIENDS)
            {
                return ContractCodes.RejectReason.FRIEND_LIMIT;
            }

            return theirs >= SocialLimits.MAX_FRIENDS ? ContractCodes.RejectReason.FRIEND_TARGET_LIMIT : NONE;
        }

        // 양방향 요청 행을 지우고 관계 두 행을 넣는다
        private static async Task MakeFriendsAsync(DbConnection connection, DbTransaction transaction, Guid actor, Guid other,
            CancellationToken cancellationToken)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM friend_requests
                WHERE (requester_character_id = @Actor AND target_character_id = @Other)
                    OR (requester_character_id = @Other AND target_character_id = @Actor);
                INSERT INTO friendships (character_id, friend_character_id)
                VALUES (@Actor, @Other), (@Other, @Actor)
                ON CONFLICT DO NOTHING;
                """, new { Actor = actor, Other = other }, transaction, cancellationToken: cancellationToken));
        }
    }
}
