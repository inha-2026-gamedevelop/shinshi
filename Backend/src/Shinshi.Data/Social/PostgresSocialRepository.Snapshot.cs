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
using Shinshi.Contracts;

namespace Shinshi.Data.Social
{
    /// <summary> 소셜 스냅샷. 한 REPEATABLE READ 읽기 전용 트랜잭션의 고정 질의 묶음이며 반복문 안에서 질의하지 않는다.
    /// 다른 캐릭터의 접속 위치는 친구, 같은 길드원 관계 행으로 고른 대상만 담는다 </summary>
    public sealed partial class PostgresSocialRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        // 원문 SQL 조각을 이어 붙일 때 줄을 나눈다
        private const string NEWLINE = "\n";

        // 대상 캐릭터 c의 살아 있는 임대. 없으면 presence 열이 NULL이다
        private const string PRESENCE_JOIN =
            """
            LEFT JOIN LATERAL (
                SELECT lease.instance_id AS lease_instance, lease.role AS lease_role FROM account_leases AS lease
                JOIN server_instances AS instance ON instance.instance_id = lease.instance_id AND instance.role = lease.role
                WHERE lease.character_id = c.character_id AND lease.expires_at > clock_timestamp()
                    AND instance.state <> 'Offline' AND instance.heartbeat_at > clock_timestamp() - @Heartbeat
            ) AS presence ON true
            """;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 행위 캐릭터의 친구, 요청, 길드 상태. 행위 캐릭터의 임대가 호출 인스턴스에 없으면 null이다 </summary>
        public async Task<SocialSnapshotRecord?> ReadSnapshotAsync(SocialCaller caller, Guid actor,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead,
                cancellationToken);
            await using (NpgsqlCommand readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY;", connection, transaction))
            {
                await readOnly.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!await IsConnectedHereAsync(connection, transaction, caller, actor, cancellationToken))
            {
                return null;
            }

            object parameters = new
            {
                Actor = actor, Heartbeat, Invite = KIND_INVITE, MaxFriends = SocialLimits.MAX_SNAPSHOT_FRIENDS,
                MaxRequests = SocialLimits.MAX_SNAPSHOT_REQUESTS, MaxInvites = SocialLimits.MAX_SNAPSHOT_INVITES
            };
            DateTime now = await connection.QuerySingleAsync<DateTime>(new CommandDefinition("SELECT clock_timestamp();",
                null, transaction, cancellationToken: cancellationToken));
            IEnumerable<PersonRow> friends = await connection.QueryAsync<PersonRow>(new CommandDefinition(
                """
                SELECT c.character_id AS "CharacterId", coalesce(c.name, '') AS "Name", c.class_id AS "ClassId",
                    presence.lease_instance AS "LeaseInstance", presence.lease_role AS "LeaseRole"
                FROM friendships AS friend
                JOIN characters AS c ON c.character_id = friend.friend_character_id AND c.deleted_at IS NULL
                """ + NEWLINE + PRESENCE_JOIN + NEWLINE +
                """
                WHERE friend.character_id = @Actor
                ORDER BY c.name_key, c.character_id
                LIMIT @MaxFriends;
                """, parameters, transaction, cancellationToken: cancellationToken));
            IEnumerable<RequestRow> incoming = await connection.QueryAsync<RequestRow>(new CommandDefinition(
                """
                SELECT c.character_id AS "CharacterId", coalesce(c.name, '') AS "Name", c.class_id AS "ClassId",
                    request.expires_at AS "ExpiresAt"
                FROM friend_requests AS request
                JOIN characters AS c ON c.character_id = request.requester_character_id AND c.deleted_at IS NULL
                WHERE request.target_character_id = @Actor AND request.expires_at > clock_timestamp()
                ORDER BY request.created_at, c.character_id
                LIMIT @MaxRequests;
                """, parameters, transaction, cancellationToken: cancellationToken));
            IEnumerable<RequestRow> outgoing = await connection.QueryAsync<RequestRow>(new CommandDefinition(
                """
                SELECT c.character_id AS "CharacterId", coalesce(c.name, '') AS "Name", c.class_id AS "ClassId",
                    request.expires_at AS "ExpiresAt"
                FROM friend_requests AS request
                JOIN characters AS c ON c.character_id = request.target_character_id AND c.deleted_at IS NULL
                WHERE request.requester_character_id = @Actor AND request.expires_at > clock_timestamp()
                ORDER BY request.created_at, c.character_id
                LIMIT @MaxRequests;
                """, parameters, transaction, cancellationToken: cancellationToken));
            GuildViewRow? guild = await connection.QuerySingleOrDefaultAsync<GuildViewRow>(new CommandDefinition(
                """
                SELECT guild.guild_id AS "GuildId", guild.name AS "Name", guild.member_count AS "MemberCount",
                    member.role AS "MyRole", member.membership_id AS "MyMembershipId", guild.leader_version AS "LeaderVersion"
                FROM guild_members AS member JOIN guilds AS guild ON guild.guild_id = member.guild_id
                WHERE member.character_id = @Actor;
                """, parameters, transaction, cancellationToken: cancellationToken));
            IEnumerable<MemberRow> members = Array.Empty<MemberRow>();
            IEnumerable<RequestRow> applications = Array.Empty<RequestRow>();
            if (guild != null)
            {
                object guildParameters = new
                {
                    guild.GuildId, Heartbeat, Application = KIND_APPLICATION, MaxMembers = SocialLimits.MAX_SNAPSHOT_MEMBERS,
                    MaxApplications = SocialLimits.MAX_SNAPSHOT_APPLICATIONS
                };
                members = await connection.QueryAsync<MemberRow>(new CommandDefinition(
                    """
                    SELECT c.character_id AS "CharacterId", member.membership_id AS "MembershipId",
                        coalesce(c.name, '') AS "Name", c.class_id AS "ClassId", member.role AS "Role",
                        presence.lease_instance AS "LeaseInstance", presence.lease_role AS "LeaseRole"
                    FROM guild_members AS member
                    JOIN characters AS c ON c.character_id = member.character_id AND c.deleted_at IS NULL
                    """ + NEWLINE + PRESENCE_JOIN + NEWLINE +
                    """
                    WHERE member.guild_id = @GuildId
                    ORDER BY member.role DESC, member.joined_at, c.character_id
                    LIMIT @MaxMembers;
                    """, guildParameters, transaction,
                    cancellationToken: cancellationToken));
                if (guild.MyRole == SocialCodes.GuildRole.LEADER)
                {
                    applications = await connection.QueryAsync<RequestRow>(new CommandDefinition(
                        """
                        SELECT c.character_id AS "CharacterId", coalesce(c.name, '') AS "Name", c.class_id AS "ClassId",
                            request.expires_at AS "ExpiresAt"
                        FROM guild_join_requests AS request
                        JOIN characters AS c ON c.character_id = request.character_id AND c.deleted_at IS NULL
                        WHERE request.guild_id = @GuildId AND request.kind = @Application
                            AND request.expires_at > clock_timestamp()
                        ORDER BY request.created_at, c.character_id
                        LIMIT @MaxApplications;
                        """, guildParameters, transaction,
                        cancellationToken: cancellationToken));
                }
            }

            IEnumerable<InviteRow> invites = await connection.QueryAsync<InviteRow>(new CommandDefinition(
                """
                SELECT guild.guild_id AS "GuildId", guild.name AS "GuildName",
                    CASE WHEN inviter.deleted_at IS NULL THEN coalesce(inviter.name, '') ELSE '' END AS "InviterName",
                    request.expires_at AS "ExpiresAt"
                FROM guild_join_requests AS request
                JOIN guilds AS guild ON guild.guild_id = request.guild_id AND guild.disbanded_at IS NULL
                LEFT JOIN characters AS inviter ON inviter.character_id = request.inviter_character_id
                WHERE request.character_id = @Actor AND request.kind = @Invite AND request.expires_at > clock_timestamp()
                ORDER BY request.created_at, guild.guild_id
                LIMIT @MaxInvites;
                """, parameters, transaction, cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);

            return new SocialSnapshotRecord(Utc(now),
                friends.Select(row => new SocialPersonRecord(row.CharacterId, row.Name, row.ClassId, row.ToPresence(caller)))
                    .ToArray(),
                incoming.Select(ToRequest).ToArray(), outgoing.Select(ToRequest).ToArray(),
                guild == null ? null : new SocialGuildRecord(guild.GuildId, guild.Name, guild.MemberCount, guild.MyRole,
                    guild.MyMembershipId, guild.LeaderVersion),
                members.Select(row => new SocialMemberRecord(row.CharacterId, row.MembershipId, row.Name, row.ClassId, row.Role,
                    row.ToPresence(caller))).ToArray(),
                applications.Select(ToRequest).ToArray(),
                invites.Select(row => new SocialInviteRecord(row.GuildId, row.GuildName, row.InviterName, Utc(row.ExpiresAt)))
                    .ToArray());
        }

        private static SocialRequestRecord ToRequest(RequestRow row)
        {
            return new SocialRequestRecord(row.CharacterId, row.Name, row.ClassId, Utc(row.ExpiresAt));
        }

        private static DateTime Utc(DateTime value)
        {
            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private class PersonRow
        {
            public Guid CharacterId { get; set; }
            public string Name { get; set; } = string.Empty;
            public int ClassId { get; set; }
            public Guid? LeaseInstance { get; set; }
            public int? LeaseRole { get; set; }

            // 임대 role 0 마을, 1 던전. 없으면 오프라인
            public SocialPresence ToPresence(SocialCaller caller)
            {
                if ((!LeaseInstance.HasValue) || (!LeaseRole.HasValue))
                {
                    return new SocialPresence(SocialCodes.Presence.OFFLINE, false);
                }

                int presence = LeaseRole.Value == ContractCodes.ServerRole.DUNGEON
                    ? SocialCodes.Presence.DUNGEON
                    : SocialCodes.Presence.TOWN;
                return new SocialPresence(presence, LeaseInstance.Value == caller.InstanceId);
            }
        }

        private sealed class MemberRow : PersonRow
        {
            public Guid MembershipId { get; set; }
            public int Role { get; set; }
        }

        private sealed class RequestRow
        {
            public Guid CharacterId { get; set; }
            public string Name { get; set; } = string.Empty;
            public int ClassId { get; set; }
            public DateTime ExpiresAt { get; set; }
        }

        private sealed class GuildViewRow
        {
            public Guid GuildId { get; set; }
            public string Name { get; set; } = string.Empty;
            public int MemberCount { get; set; }
            public int MyRole { get; set; }
            public Guid MyMembershipId { get; set; }
            public long LeaderVersion { get; set; }
        }

        private sealed class InviteRow
        {
            public Guid GuildId { get; set; }
            public string GuildName { get; set; } = string.Empty;
            public string InviterName { get; set; } = string.Empty;
            public DateTime ExpiresAt { get; set; }
        }
    }
}
