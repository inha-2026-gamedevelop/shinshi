// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Social
{
    /// <summary> 길드 생성, 가입 신청, 초대, 수락, 거절, 탈퇴, 추방, 위임. member_count는 길드 행 잠금 아래 guild_members와 함께 바꾼다 </summary>
    public sealed partial class PostgresSocialRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int KIND_APPLICATION = 0;
        private const int KIND_INVITE      = 1;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 길드를 만든다. 같은 생성자, 같은 createRequestId가 이미 있으면 그 길드로 성공한다.
        /// name, nameKey가 null이면(이름 규칙 위반) GUILD_NAME_INVALID다 </summary>
        public Task<SocialActionOutcome> CreateGuildAsync(SocialCaller caller, Guid actor, string? name, string? nameKey,
            Guid createRequestId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return RunActionAsync(async (connection, transaction, token) =>
            {
                (int reason, HashSet<Guid> _) = await LockActorAsync(connection, transaction, caller, actor,
                    Array.Empty<Guid>(), token);
                if (reason != NONE)
                {
                    return SocialActionOutcome.Rejected(reason);
                }

                bool replay = await connection.QuerySingleAsync<bool>(new CommandDefinition(
                    """
                    SELECT EXISTS (SELECT 1 FROM guilds
                        WHERE creator_character_id = @Actor AND create_request_id = @RequestId);
                    """, new { Actor = actor, RequestId = createRequestId }, transaction, cancellationToken: token));
                if (replay)
                {
                    return SocialActionOutcome.Committed();
                }
                if ((string.IsNullOrEmpty(name)) || (string.IsNullOrEmpty(nameKey)))
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_NAME_INVALID);
                }
                if (await ReadMembershipAsync(connection, transaction, actor, token) != null)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_ALREADY_MEMBER);
                }

                // 다른 생성자가 같은 이름 키를 동시에 넣으면 먼저 커밋한 쪽을 기다린 뒤 아무것도 넣지 않는다.
                // 예외 대신 결과로 판정해 트랜잭션이 깨지지 않는다(캐릭터 생성의 이름 키와 같은 방식)
                Guid guildId = Guid.NewGuid();
                Guid? inserted = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                    """
                    INSERT INTO guilds (guild_id, name, name_key, leader_character_id, member_count, creator_character_id,
                        create_request_id)
                    VALUES (@GuildId, @Name, @NameKey, @Actor, 1, @Actor, @RequestId)
                    ON CONFLICT (name_key) WHERE disbanded_at IS NULL DO NOTHING
                    RETURNING guild_id;
                    """, new { GuildId = guildId, Name = name, NameKey = nameKey, Actor = actor, RequestId = createRequestId },
                    transaction, cancellationToken: token));
                if (!inserted.HasValue)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_NAME_TAKEN);
                }

                await InsertMemberAsync(connection, transaction, guildId, actor, SocialCodes.GuildRole.LEADER, token);
                return SocialActionOutcome.Committed();
            }, cancellationToken);
        }

        /// <summary> 길드 이름으로 가입 신청. 이미 초대받았으면 곧바로 가입한다. guildNameKey가 null이면 GUILD_NOT_FOUND다 </summary>
        public Task<SocialActionOutcome> ApplyToGuildAsync(SocialCaller caller, Guid actor, string? guildNameKey,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return RunActionAsync(async (connection, transaction, token) =>
            {
                (int reason, HashSet<Guid> _) = await LockActorAsync(connection, transaction, caller, actor,
                    Array.Empty<Guid>(), token);
                if (reason != NONE)
                {
                    return SocialActionOutcome.Rejected(reason);
                }

                Guid? guildId = string.IsNullOrEmpty(guildNameKey) ? null : await connection.QuerySingleOrDefaultAsync<Guid?>(
                    new CommandDefinition("SELECT guild_id FROM guilds WHERE name_key = @NameKey AND disbanded_at IS NULL;",
                        new { NameKey = guildNameKey }, transaction, cancellationToken: token));
                GuildRow? guild = guildId.HasValue ? await LockGuildAsync(connection, transaction, guildId.Value, token) : null;
                if ((guild == null) || (guild.Disbanded))
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_NOT_FOUND);
                }

                MembershipRow? mine = await ReadMembershipAsync(connection, transaction, actor, token);
                if (mine != null)
                {
                    // 같은 길드면 곧바로 가입한 요청의 재전송이다
                    return mine.GuildId == guild.GuildId
                        ? SocialActionOutcome.Committed()
                        : SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_ALREADY_MEMBER);
                }

                string actorName = await ReadNameAsync(connection, transaction, actor, token);
                if (await HasPendingJoinRequestAsync(connection, transaction, guild.GuildId, actor, KIND_INVITE, token))
                {
                    return await JoinAndNotifyAsync(connection, transaction, guild, actor, actorName, token);
                }
                if (await RefreshJoinRequestAsync(connection, transaction, guild.GuildId, actor, KIND_APPLICATION, token))
                {
                    return SocialActionOutcome.Committed();
                }

                (long mineCount, long guildCount) = await connection.QuerySingleAsync<(long, long)>(new CommandDefinition(
                    """
                    SELECT
                        (SELECT count(*) FROM guild_join_requests
                            WHERE character_id = @Actor AND kind = @Application AND expires_at > clock_timestamp()),
                        (SELECT count(*) FROM guild_join_requests
                            WHERE guild_id = @GuildId AND expires_at > clock_timestamp());
                    """, new { Actor = actor, guild.GuildId, Application = KIND_APPLICATION }, transaction,
                    cancellationToken: token));
                if (mineCount >= SocialLimits.MAX_PENDING_APPLICATIONS)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_APPLICATION_LIMIT);
                }
                if (guildCount >= SocialLimits.MAX_GUILD_PENDING)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_PENDING_FULL);
                }

                await UpsertJoinRequestAsync(connection, transaction, guild.GuildId, actor, KIND_APPLICATION, null, token);
                return SocialActionOutcome.Committed(await NotifyAsync(connection, transaction,
                    new[] { guild.LeaderCharacterId!.Value },
                    SocialCodes.Notice.GUILD_APPLIED, actor, actorName, guild.Name, token));
            }, cancellationToken);
        }

        /// <summary> 길드장이 신청을 수락한다. 신청자가 이미 이 길드원이면 성공이다 </summary>
        public Task<SocialActionOutcome> AcceptApplicationAsync(SocialCaller caller, Guid actor, Guid applicant,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return RunActionAsync(async (connection, transaction, token) =>
            {
                (int reason, HashSet<Guid> locked) = await LockActorAsync(connection, transaction, caller, actor,
                    new[] { applicant }, token);
                if (reason != NONE)
                {
                    return SocialActionOutcome.Rejected(reason);
                }

                (int leaderReason, GuildRow? guild) = await LockLedGuildAsync(connection, transaction, actor, null, token);
                if (leaderReason != NONE)
                {
                    return SocialActionOutcome.Rejected(leaderReason);
                }

                MembershipRow? target = await ReadMembershipAsync(connection, transaction, applicant, token);
                if ((target != null) && (target.GuildId == guild!.GuildId))
                {
                    return SocialActionOutcome.Committed();
                }
                if ((!locked.Contains(applicant))
                    || (!await HasPendingJoinRequestAsync(connection, transaction, guild!.GuildId, applicant, KIND_APPLICATION,
                        token)))
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_APPLICATION_NOT_FOUND);
                }
                if (target != null)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_TARGET_IN_GUILD);
                }

                string applicantName = await ReadNameAsync(connection, transaction, applicant, token);
                return await JoinAndNotifyAsync(connection, transaction, guild, applicant, applicantName, token,
                    includeJoiner: true, excludeActor: actor);
            }, cancellationToken);
        }

        /// <summary> 길드장이 신청을 거절한다. 없으면 성공이다 </summary>
        public Task<SocialActionOutcome> DeclineApplicationAsync(SocialCaller caller, Guid actor, Guid applicant,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return RunActionAsync(async (connection, transaction, token) =>
            {
                (int reason, HashSet<Guid> _) = await LockActorAsync(connection, transaction, caller, actor,
                    new[] { applicant }, token);
                if (reason != NONE)
                {
                    return SocialActionOutcome.Rejected(reason);
                }

                (int leaderReason, GuildRow? guild) = await LockLedGuildAsync(connection, transaction, actor, null, token);
                if (leaderReason != NONE)
                {
                    return SocialActionOutcome.Rejected(leaderReason);
                }

                await DeleteJoinRequestAsync(connection, transaction, guild!.GuildId, applicant, KIND_APPLICATION, token);
                return SocialActionOutcome.Committed();
            }, cancellationToken);
        }

        /// <summary> 길드장이 이름으로 초대한다. 대상이 이미 신청했으면 곧바로 가입한다. targetNameKey가 null이면 대상 없음이다 </summary>
        public Task<SocialActionOutcome> InviteToGuildAsync(SocialCaller caller, Guid actor, string? targetNameKey,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return RunActionAsync(async (connection, transaction, token) =>
            {
                Guid? target = await FindCharacterByNameAsync(connection, transaction, targetNameKey, token);
                (int reason, HashSet<Guid> locked) = await LockActorAsync(connection, transaction, caller, actor,
                    target.HasValue ? new[] { target.Value } : Array.Empty<Guid>(), token);
                if (reason != NONE)
                {
                    return SocialActionOutcome.Rejected(reason);
                }

                (int leaderReason, GuildRow? guild) = await LockLedGuildAsync(connection, transaction, actor, null, token);
                if (leaderReason != NONE)
                {
                    return SocialActionOutcome.Rejected(leaderReason);
                }
                if ((!target.HasValue) || (!locked.Contains(target.Value)))
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.SOCIAL_TARGET_NOT_FOUND);
                }
                if (target.Value == actor)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.SOCIAL_TARGET_SELF);
                }

                MembershipRow? theirs = await ReadMembershipAsync(connection, transaction, target.Value, token);
                if (theirs != null)
                {
                    // 같은 길드면 곧바로 가입한 초대의 재전송이다
                    return theirs.GuildId == guild!.GuildId
                        ? SocialActionOutcome.Committed()
                        : SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_TARGET_IN_GUILD);
                }
                if (await HasPendingJoinRequestAsync(connection, transaction, guild!.GuildId, target.Value, KIND_APPLICATION,
                    token))
                {
                    string targetName = await ReadNameAsync(connection, transaction, target.Value, token);
                    return await JoinAndNotifyAsync(connection, transaction, guild, target.Value, targetName, token,
                        includeJoiner: true, excludeActor: actor);
                }
                if (await RefreshJoinRequestAsync(connection, transaction, guild.GuildId, target.Value, KIND_INVITE, token))
                {
                    return SocialActionOutcome.Committed();
                }

                // 대상 캐릭터를 잠근 채 세므로 다른 길드의 동시 초대가 함께 대상의 받은 초대 상한을 넘지 못한다
                (long guildCount, long invites) = await connection.QuerySingleAsync<(long, long)>(new CommandDefinition(
                    """
                    SELECT
                        (SELECT count(*) FROM guild_join_requests
                            WHERE guild_id = @GuildId AND expires_at > clock_timestamp()),
                        (SELECT count(*) FROM guild_join_requests
                            WHERE character_id = @Target AND kind = @Invite AND expires_at > clock_timestamp());
                    """, new { guild.GuildId, Target = target.Value, Invite = KIND_INVITE }, transaction,
                    cancellationToken: token));
                if (guildCount >= SocialLimits.MAX_GUILD_PENDING)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_PENDING_FULL);
                }
                if (invites >= SocialLimits.MAX_PENDING_GUILD_INVITES)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_TARGET_INVITES_FULL);
                }

                await UpsertJoinRequestAsync(connection, transaction, guild.GuildId, target.Value, KIND_INVITE, actor, token);
                string actorName = await ReadNameAsync(connection, transaction, actor, token);
                return SocialActionOutcome.Committed(await NotifyAsync(connection, transaction, new[] { target.Value },
                    SocialCodes.Notice.GUILD_INVITED, actor, actorName, guild.Name, token));
            }, cancellationToken);
        }

        /// <summary> 받은 초대를 수락한다. 이미 그 길드원이면 성공이다 </summary>
        public Task<SocialActionOutcome> AcceptInviteAsync(SocialCaller caller, Guid actor, Guid guildId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return RunActionAsync(async (connection, transaction, token) =>
            {
                (int reason, HashSet<Guid> _) = await LockActorAsync(connection, transaction, caller, actor,
                    Array.Empty<Guid>(), token);
                if (reason != NONE)
                {
                    return SocialActionOutcome.Rejected(reason);
                }

                MembershipRow? mine = await ReadMembershipAsync(connection, transaction, actor, token);
                if ((mine != null) && (mine.GuildId == guildId))
                {
                    return SocialActionOutcome.Committed();
                }

                GuildRow? guild = await LockGuildAsync(connection, transaction, guildId, token);
                if ((guild == null)
                    || (!await HasPendingJoinRequestAsync(connection, transaction, guildId, actor, KIND_INVITE, token)))
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_INVITE_NOT_FOUND);
                }
                if (mine != null)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_ALREADY_MEMBER);
                }
                if (guild.Disbanded)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_NOT_FOUND);
                }

                string actorName = await ReadNameAsync(connection, transaction, actor, token);
                return await JoinAndNotifyAsync(connection, transaction, guild, actor, actorName, token);
            }, cancellationToken);
        }

        /// <summary> 받은 초대를 거절한다. 없으면 성공이다 </summary>
        public Task<SocialActionOutcome> DeclineInviteAsync(SocialCaller caller, Guid actor, Guid guildId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return RunActionAsync(async (connection, transaction, token) =>
            {
                (int reason, HashSet<Guid> _) = await LockActorAsync(connection, transaction, caller, actor,
                    Array.Empty<Guid>(), token);
                if (reason != NONE)
                {
                    return SocialActionOutcome.Rejected(reason);
                }

                await DeleteJoinRequestAsync(connection, transaction, guildId, actor, KIND_INVITE, token);
                return SocialActionOutcome.Committed();
            }, cancellationToken);
        }

        /// <summary> 탈퇴. 내 현재 가입이 (guildId, membershipId)와 다르면 효과 없이 성공이다. 혼자인 길드장이면 길드를 해체한다 </summary>
        public Task<SocialActionOutcome> LeaveGuildAsync(SocialCaller caller, Guid actor, Guid guildId, Guid membershipId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return RunActionAsync(async (connection, transaction, token) =>
            {
                (int reason, HashSet<Guid> _) = await LockActorAsync(connection, transaction, caller, actor,
                    Array.Empty<Guid>(), token);
                if (reason != NONE)
                {
                    return SocialActionOutcome.Rejected(reason);
                }

                MembershipRow? mine = await ReadMembershipAsync(connection, transaction, actor, token);
                if ((mine == null) || (mine.GuildId != guildId) || (mine.MembershipId != membershipId))
                {
                    return SocialActionOutcome.Committed();
                }

                GuildRow guild = (await LockGuildAsync(connection, transaction, guildId, token))!;
                string actorName = await ReadNameAsync(connection, transaction, actor, token);
                if (mine.Role == SocialCodes.GuildRole.LEADER)
                {
                    if (guild.MemberCount > 1)
                    {
                        return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_LEADER_MUST_DELEGATE);
                    }

                    return SocialActionOutcome.Committed(await DisbandAsync(connection, transaction, guild, actor, actorName,
                        token));
                }

                await RemoveMemberAsync(connection, transaction, guild.GuildId, actor, token);
                return SocialActionOutcome.Committed(await NotifyMembersAsync(connection, transaction, guild,
                    SocialCodes.Notice.GUILD_MEMBER_LEFT, actor, actorName, token));
            }, cancellationToken);
        }

        /// <summary> 추방. 대상의 현재 가입이 (guildId, membershipId)와 다르면 효과 없이 성공이다 </summary>
        public Task<SocialActionOutcome> KickMemberAsync(SocialCaller caller, Guid actor, Guid member, Guid guildId,
            Guid membershipId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return RunActionAsync(async (connection, transaction, token) =>
            {
                (int reason, HashSet<Guid> _) = await LockActorAsync(connection, transaction, caller, actor, new[] { member },
                    token);
                if (reason != NONE)
                {
                    return SocialActionOutcome.Rejected(reason);
                }
                if (member == actor)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.SOCIAL_TARGET_SELF);
                }

                (int leaderReason, GuildRow? guild) = await LockLedGuildAsync(connection, transaction, actor, guildId, token);
                if (leaderReason != NONE)
                {
                    return SocialActionOutcome.Rejected(leaderReason);
                }

                MembershipRow? target = await ReadMembershipAsync(connection, transaction, member, token);
                if ((target == null) || (target.GuildId != guildId) || (target.MembershipId != membershipId))
                {
                    return SocialActionOutcome.Committed();
                }

                await RemoveMemberAsync(connection, transaction, guildId, member, token);
                string actorName = await ReadNameAsync(connection, transaction, actor, token);
                string memberName = await ReadNameAsync(connection, transaction, member, token);
                List<Guid> changed = new List<Guid>(await NotifyAsync(connection, transaction, new[] { member },
                    SocialCodes.Notice.GUILD_KICKED, actor, actorName, guild!.Name, token));
                changed.AddRange(await NotifyMembersAsync(connection, transaction, guild, SocialCodes.Notice.GUILD_MEMBER_LEFT,
                    member, memberName, token, actor));
                return SocialActionOutcome.Committed(changed.Distinct().OrderBy(id => id).ToArray());
            }, cancellationToken);
        }

        /// <summary> 길드장 위임. leader_version이 기대 값일 때만 적용한다. 이미 이 요청이 적용됐으면(버전 +1, 대상이 길드장) 성공이다 </summary>
        public Task<SocialActionOutcome> DelegateLeaderAsync(SocialCaller caller, Guid actor, Guid guildId, Guid newLeader,
            Guid membershipId, long expectedLeaderVersion, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return RunActionAsync(async (connection, transaction, token) =>
            {
                (int reason, HashSet<Guid> _) = await LockActorAsync(connection, transaction, caller, actor,
                    new[] { newLeader }, token);
                if (reason != NONE)
                {
                    return SocialActionOutcome.Rejected(reason);
                }
                if (newLeader == actor)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.SOCIAL_TARGET_SELF);
                }

                GuildRow? guild = await LockGuildAsync(connection, transaction, guildId, token);
                if ((guild == null) || (guild.Disbanded))
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_NOT_LEADER);
                }

                MembershipRow? target = await ReadMembershipAsync(connection, transaction, newLeader, token);
                bool sameTarget = (target != null) && (target.GuildId == guildId) && (target.MembershipId == membershipId);
                if ((guild.LeaderVersion == expectedLeaderVersion + 1) && (sameTarget)
                    && (guild.LeaderCharacterId == newLeader))
                {
                    return SocialActionOutcome.Committed();
                }
                if (guild.LeaderVersion != expectedLeaderVersion)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_LEADER_CHANGED);
                }

                MembershipRow? mine = await ReadMembershipAsync(connection, transaction, actor, token);
                if ((mine == null) || (mine.GuildId != guildId) || (mine.Role != SocialCodes.GuildRole.LEADER))
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_NOT_LEADER);
                }
                if (!sameTarget)
                {
                    return SocialActionOutcome.Rejected(ContractCodes.RejectReason.GUILD_NOT_MEMBER);
                }

                // ux_guild_members_leader(길드당 길드장 1명) 때문에 강등 -> 승격 -> 길드 행 순서로 쓴다
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    UPDATE guild_members SET role = @Member WHERE character_id = @Actor AND guild_id = @GuildId;
                    UPDATE guild_members SET role = @Leader WHERE character_id = @NewLeader AND guild_id = @GuildId;
                    UPDATE guilds SET leader_character_id = @NewLeader, leader_version = leader_version + 1,
                        revision = revision + 1
                    WHERE guild_id = @GuildId;
                    """, new
                    {
                        Member = SocialCodes.GuildRole.MEMBER, Leader = SocialCodes.GuildRole.LEADER, Actor = actor,
                        NewLeader = newLeader, GuildId = guildId
                    }, transaction, cancellationToken: token));
                string leaderName = await ReadNameAsync(connection, transaction, newLeader, token);
                return SocialActionOutcome.Committed(await NotifyMembersAsync(connection, transaction, guild,
                    SocialCodes.Notice.GUILD_LEADER_CHANGED, newLeader, leaderName, token, actor, includeSubject: true));
            }, cancellationToken);
        }

        /****************************************
        *            Shared Helpers
        ****************************************/

        internal static Task<MembershipRow?> ReadMembershipAsync(DbConnection connection, DbTransaction transaction,
            Guid characterId, CancellationToken cancellationToken)
        {
            return connection.QuerySingleOrDefaultAsync<MembershipRow?>(new CommandDefinition(
                """
                SELECT guild_id AS "GuildId", membership_id AS "MembershipId", role AS "Role"
                FROM guild_members WHERE character_id = @CharacterId;
                """, new { CharacterId = characterId }, transaction, cancellationToken: cancellationToken));
        }

        internal static Task<GuildRow?> LockGuildAsync(DbConnection connection, DbTransaction transaction, Guid guildId,
            CancellationToken cancellationToken)
        {
            return connection.QuerySingleOrDefaultAsync<GuildRow?>(new CommandDefinition(
                """
                SELECT guild_id AS "GuildId", name AS "Name", leader_character_id AS "LeaderCharacterId",
                    member_count AS "MemberCount", leader_version AS "LeaderVersion",
                    (disbanded_at IS NOT NULL) AS "Disbanded"
                FROM guilds WHERE guild_id = @GuildId FOR UPDATE;
                """, new { GuildId = guildId }, transaction, cancellationToken: cancellationToken));
        }

        // 행위 캐릭터(이미 잠금)가 길드장인 살아 있는 길드를 잠근다. expectedGuild가 있으면 그 길드여야 한다
        private static async Task<(int Reason, GuildRow? Guild)> LockLedGuildAsync(DbConnection connection,
            DbTransaction transaction, Guid actor, Guid? expectedGuild, CancellationToken cancellationToken)
        {
            MembershipRow? mine = await ReadMembershipAsync(connection, transaction, actor, cancellationToken);
            if ((mine == null) || (mine.Role != SocialCodes.GuildRole.LEADER)
                || ((expectedGuild.HasValue) && (mine.GuildId != expectedGuild.Value)))
            {
                return (ContractCodes.RejectReason.GUILD_NOT_LEADER, null);
            }

            // 길드장의 역할은 그 캐릭터 행을 잠가야 바뀌므로(위임, 삭제) 길드 행을 잠근 뒤에도 그대로다
            GuildRow? guild = await LockGuildAsync(connection, transaction, mine.GuildId, cancellationToken);
            if ((guild == null) || (guild.Disbanded) || (guild.LeaderCharacterId != actor))
            {
                return (ContractCodes.RejectReason.GUILD_NOT_LEADER, null);
            }

            return (NONE, guild);
        }

        private static Task<bool> HasPendingJoinRequestAsync(DbConnection connection, DbTransaction transaction, Guid guildId,
            Guid characterId, int kind, CancellationToken cancellationToken)
        {
            return connection.QuerySingleAsync<bool>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM guild_join_requests
                    WHERE guild_id = @GuildId AND character_id = @CharacterId AND kind = @Kind
                        AND expires_at > clock_timestamp());
                """, new { GuildId = guildId, CharacterId = characterId, Kind = kind }, transaction,
                cancellationToken: cancellationToken));
        }

        // 같은 종류의 만료 전 요청이 있으면 기한만 늘린다
        private static async Task<bool> RefreshJoinRequestAsync(DbConnection connection, DbTransaction transaction, Guid guildId,
            Guid characterId, int kind, CancellationToken cancellationToken)
        {
            int updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE guild_join_requests SET expires_at = clock_timestamp() + @Ttl
                WHERE guild_id = @GuildId AND character_id = @CharacterId AND kind = @Kind
                    AND expires_at > clock_timestamp();
                """, new { GuildId = guildId, CharacterId = characterId, Kind = kind, Ttl = GuildRequestTtl }, transaction,
                cancellationToken: cancellationToken));
            return updated > 0;
        }

        // (길드, 캐릭터)당 한 행. 남은 만료 행(어느 종류든)은 새 요청으로 덮어쓴다
        private static Task UpsertJoinRequestAsync(DbConnection connection, DbTransaction transaction, Guid guildId,
            Guid characterId, int kind, Guid? inviter, CancellationToken cancellationToken)
        {
            return connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO guild_join_requests (guild_id, character_id, kind, inviter_character_id, expires_at)
                VALUES (@GuildId, @CharacterId, @Kind, @Inviter, clock_timestamp() + @Ttl)
                ON CONFLICT (guild_id, character_id) DO UPDATE SET kind = EXCLUDED.kind,
                    inviter_character_id = EXCLUDED.inviter_character_id, created_at = clock_timestamp(),
                    expires_at = EXCLUDED.expires_at;
                """, new { GuildId = guildId, CharacterId = characterId, Kind = kind, Inviter = inviter, Ttl = GuildRequestTtl },
                transaction, cancellationToken: cancellationToken));
        }

        private static Task DeleteJoinRequestAsync(DbConnection connection, DbTransaction transaction, Guid guildId,
            Guid characterId, int kind, CancellationToken cancellationToken)
        {
            return connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM guild_join_requests WHERE guild_id = @GuildId AND character_id = @CharacterId AND kind = @Kind;
                """, new { GuildId = guildId, CharacterId = characterId, Kind = kind }, transaction,
                cancellationToken: cancellationToken));
        }

        // 가입마다 새 membership_id를 만들고 그 캐릭터의 다른 신청, 초대를 모두 지운다
        private static Task InsertMemberAsync(DbConnection connection, DbTransaction transaction, Guid guildId,
            Guid characterId, int role, CancellationToken cancellationToken)
        {
            return connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO guild_members (character_id, guild_id, membership_id, role)
                VALUES (@CharacterId, @GuildId, @MembershipId, @Role);
                DELETE FROM guild_join_requests WHERE character_id = @CharacterId;
                """, new { CharacterId = characterId, GuildId = guildId, MembershipId = Guid.NewGuid(), Role = role },
                transaction, cancellationToken: cancellationToken));
        }

        // 정원 증가를 조건부 갱신의 영향 행 수로 판정하고 가입시킨다. 기존 길드원(참여자 제외)에게 가입 알림을 넣는다
        private static async Task<SocialActionOutcome> JoinAndNotifyAsync(DbConnection connection, DbTransaction transaction,
            GuildRow guild, Guid joiner, string joinerName, CancellationToken cancellationToken, bool includeJoiner = false,
            Guid? excludeActor = null)
        {
            int updated = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE guilds SET member_count = member_count + 1, revision = revision + 1
                WHERE guild_id = @GuildId AND disbanded_at IS NULL AND member_count < @Max;
                """, new { guild.GuildId, Max = SocialLimits.MAX_GUILD_MEMBERS }, transaction,
                cancellationToken: cancellationToken));
            if (updated == 0)
            {
                return SocialActionOutcome.Rejected(guild.Disbanded
                    ? ContractCodes.RejectReason.GUILD_NOT_FOUND
                    : ContractCodes.RejectReason.GUILD_FULL);
            }

            await InsertMemberAsync(connection, transaction, guild.GuildId, joiner, SocialCodes.GuildRole.MEMBER,
                cancellationToken);
            List<Guid> targets = await ReadMemberIdsAsync(connection, transaction, guild.GuildId, cancellationToken);
            targets.RemoveAll(id => ((id == joiner) && (!includeJoiner)) || ((excludeActor.HasValue) && (id == excludeActor.Value)));
            // 알림 행위자 이름은 가입한 캐릭터다. 가입한 본인은 자기 이름의 가입 알림으로 "길드에 가입했다"를 표시한다
            IReadOnlyList<Guid> changed = await InsertNoticeRowsAsync(connection, transaction, targets,
                SocialCodes.Notice.GUILD_JOINED, joiner, joinerName, guild.Name, cancellationToken);
            return SocialActionOutcome.Committed(changed);
        }

        // 호출자가 대상을 이미 골랐으므로 행위자 자신(가입 본인, 새 길드장)도 받을 수 있다
        private static Task<IReadOnlyList<Guid>> InsertNoticeRowsAsync(DbConnection connection, DbTransaction transaction,
            IReadOnlyCollection<Guid> targets, int notice, Guid actor, string actorName, string guildName,
            CancellationToken cancellationToken)
        {
            return NotifyAsync(connection, transaction, targets, notice, actor, actorName, guildName, cancellationToken,
                includeActor: true);
        }

        internal static async Task<List<Guid>> ReadMemberIdsAsync(DbConnection connection, DbTransaction transaction,
            Guid guildId, CancellationToken cancellationToken)
        {
            IEnumerable<Guid> ids = await connection.QueryAsync<Guid>(new CommandDefinition(
                """
                SELECT member.character_id FROM guild_members AS member
                JOIN characters AS character_row ON character_row.character_id = member.character_id
                WHERE member.guild_id = @GuildId AND character_row.deleted_at IS NULL
                ORDER BY member.character_id;
                """, new { GuildId = guildId }, transaction, cancellationToken: cancellationToken));
            return ids.ToList();
        }

        // 길드원 행을 지우고 정원을 1 줄인다(길드 행 잠금 아래)
        internal static Task RemoveMemberAsync(DbConnection connection, DbTransaction transaction, Guid guildId,
            Guid characterId, CancellationToken cancellationToken)
        {
            return connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM guild_members WHERE character_id = @CharacterId AND guild_id = @GuildId;
                UPDATE guilds SET member_count = member_count - 1, revision = revision + 1 WHERE guild_id = @GuildId;
                """, new { GuildId = guildId, CharacterId = characterId }, transaction, cancellationToken: cancellationToken));
        }

        // 남은 길드원 모두에게 알림(except 제외, includeSubject가 아니면 subject도 뺀다)
        internal static async Task<IReadOnlyList<Guid>> NotifyMembersAsync(DbConnection connection, DbTransaction transaction,
            GuildRow guild, int notice, Guid subject, string subjectName, CancellationToken cancellationToken,
            Guid? except = null, bool includeSubject = false)
        {
            List<Guid> targets = await ReadMemberIdsAsync(connection, transaction, guild.GuildId, cancellationToken);
            targets.RemoveAll(id => ((except.HasValue) && (id == except.Value)) || ((id == subject) && (!includeSubject)));
            return await InsertNoticeRowsAsync(connection, transaction, targets, notice, subject, subjectName, guild.Name,
                cancellationToken);
        }

        /// <summary> 혼자 남은 길드장의 탈퇴(또는 캐릭터 삭제)로 길드를 해체한다. 대기 신청, 초대를 지우고 그 대상에게 해체 알림을 넣는다 </summary>
        internal static async Task<IReadOnlyList<Guid>> DisbandAsync(DbConnection connection, DbTransaction transaction,
            GuildRow guild, Guid leader, string leaderName, CancellationToken cancellationToken)
        {
            IEnumerable<Guid?> pending = await connection.QueryAsync<Guid?>(new CommandDefinition(
                """
                DELETE FROM guild_join_requests WHERE guild_id = @GuildId
                RETURNING CASE WHEN expires_at > clock_timestamp() THEN character_id END;
                """, new { guild.GuildId }, transaction, cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM guild_members WHERE character_id = @Leader AND guild_id = @GuildId;
                UPDATE guilds SET disbanded_at = clock_timestamp(), leader_character_id = NULL, member_count = 0,
                    revision = revision + 1
                WHERE guild_id = @GuildId;
                """, new { guild.GuildId, Leader = leader }, transaction, cancellationToken: cancellationToken));
            return await NotifyAsync(connection, transaction, pending.Where(id => id.HasValue).Select(id => id!.Value),
                SocialCodes.Notice.GUILD_DISBANDED, leader, leaderName, guild.Name, cancellationToken);
        }

        /****************************************
        *               DB Rows
        ****************************************/

        internal sealed class MembershipRow
        {
            public Guid GuildId { get; set; }
            public Guid MembershipId { get; set; }
            public int Role { get; set; }
        }

        internal sealed class GuildRow
        {
            public Guid GuildId { get; set; }
            public string Name { get; set; } = string.Empty;
            public Guid? LeaderCharacterId { get; set; }
            public int MemberCount { get; set; }
            public long LeaderVersion { get; set; }
            public bool Disbanded { get; set; }
        }
    }
}
