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
    /// <summary> 캐릭터 삭제의 소셜 정리. 삭제 효과가 계정 -> 삭제 캐릭터(FOR NO KEY UPDATE) 잠금을 잡은 같은 트랜잭션에서 부른다.
    /// 잠금 순서는 계정 -> 삭제 캐릭터 -> 길드 행이고 다른 캐릭터 행은 잠그지 않는다(알림 행의 FK 검사 FOR KEY SHARE만).
    /// 관계 행은 키 순서로 잠근 뒤 지워 서로 친구인 두 캐릭터의 동시 삭제가 교착되지 않는다 </summary>
    public static class SocialCharacterCleanup
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 길드원이 있는 길드장이면 CHARACTER_IS_GUILD_LEADER로 아무것도 바꾸지 않는다. 아니면 NONE과 함께 탈퇴(혼자인 길드장은 해체),
        /// 친구, 친구 요청, 본인의 길드 신청과 초대, 본인 앞 전달함 행을 지운다 </summary>
        public static async Task<int> ApplyAsync(DbConnection connection, DbTransaction transaction, Guid characterId,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(transaction);
            PostgresSocialRepository.MembershipRow? membership = await PostgresSocialRepository.ReadMembershipAsync(connection,
                transaction, characterId, cancellationToken);
            if (membership != null)
            {
                PostgresSocialRepository.GuildRow guild = (await PostgresSocialRepository.LockGuildAsync(connection,
                    transaction, membership.GuildId, cancellationToken))!;
                string name = await PostgresSocialRepository.ReadNameAsync(connection, transaction, characterId,
                    cancellationToken);
                if (membership.Role == SocialCodes.GuildRole.LEADER)
                {
                    if (guild.MemberCount > 1)
                    {
                        return ContractCodes.RejectReason.CHARACTER_IS_GUILD_LEADER;
                    }

                    await PostgresSocialRepository.DisbandAsync(connection, transaction, guild, characterId, name,
                        cancellationToken);
                }
                else
                {
                    await PostgresSocialRepository.RemoveMemberAsync(connection, transaction, guild.GuildId, characterId,
                        cancellationToken);
                    await PostgresSocialRepository.NotifyMembersAsync(connection, transaction, guild,
                        SocialCodes.Notice.GUILD_MEMBER_LEFT, characterId, name, cancellationToken);
                }
            }

            // 상대가 동시에 삭제되어도 같은 키 순서로 잠그므로 교착이 생기지 않는다
            await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM friendships WHERE (character_id, friend_character_id) IN (
                    SELECT character_id, friend_character_id FROM friendships
                    WHERE character_id = @CharacterId OR friend_character_id = @CharacterId
                    ORDER BY character_id, friend_character_id
                    FOR UPDATE);
                DELETE FROM friend_requests WHERE (requester_character_id, target_character_id) IN (
                    SELECT requester_character_id, target_character_id FROM friend_requests
                    WHERE requester_character_id = @CharacterId OR target_character_id = @CharacterId
                    ORDER BY requester_character_id, target_character_id
                    FOR UPDATE);
                DELETE FROM guild_join_requests WHERE character_id = @CharacterId;
                DELETE FROM social_deliveries WHERE target_character_id = @CharacterId;
                """, new { CharacterId = characterId }, transaction, cancellationToken: cancellationToken));
            return ContractCodes.RejectReason.NONE;
        }
    }
}
