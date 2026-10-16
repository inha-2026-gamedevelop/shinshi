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
    /// <summary> 귓속말, 길드 채팅과 전달함 꺼내기. 모든 줄은 전달함 행으로 먼저 기록하고 클라이언트 수신 확인을 받은 행만 지운다.
    /// 로컬 전달은 호출 인스턴스에 있는 수신자의 남은 행(새 행 이하)을 오름차순으로 함께 돌려줘 꺼내기와 같은 순서를 지킨다 </summary>
    public sealed partial class PostgresSocialRepository
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 귓속말. 대상이 없으면 SOCIAL_TARGET_NOT_FOUND, 접속 중이 아니면 WHISPER_TARGET_OFFLINE이다.
        /// text는 게임 서버가 정리를 끝낸 1~100자 본문이다(길이는 호출자가 검사) </summary>
        public Task<SocialChatOutcome> WhisperAsync(SocialCaller caller, Guid actor, string? targetNameKey, string text,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(text);
            return RunAsync(async (connection, transaction, token) =>
            {
                if (!await IsConnectedHereAsync(connection, transaction, caller, actor, token))
                {
                    return SocialChatOutcome.Rejected(ContractCodes.RejectReason.SOCIAL_NOT_CONNECTED);
                }

                Guid? target = await FindCharacterByNameAsync(connection, transaction, targetNameKey, token);
                if (!target.HasValue)
                {
                    return SocialChatOutcome.Rejected(ContractCodes.RejectReason.SOCIAL_TARGET_NOT_FOUND);
                }
                if (target.Value == actor)
                {
                    return SocialChatOutcome.Rejected(ContractCodes.RejectReason.SOCIAL_TARGET_SELF);
                }

                List<LiveLeaseRow> live = await ReadLiveLeasesAsync(connection, transaction, new[] { target.Value }, token);
                if (live.Count == 0)
                {
                    return SocialChatOutcome.Rejected(ContractCodes.RejectReason.WHISPER_TARGET_OFFLINE);
                }

                string senderName = await ReadNameAsync(connection, transaction, actor, token);
                string targetName = await ReadNameAsync(connection, transaction, target.Value, token);
                IReadOnlyList<SocialDeliveryRecord> local = await InsertChatAsync(connection, transaction, caller, live,
                    SocialCodes.DeliveryKind.WHISPER, SocialCodes.ChatChannel.WHISPER, actor, senderName, text, token);
                return new SocialChatOutcome(NONE, targetName, local);
            }, outcome => outcome.ReasonCode == NONE, cancellationToken);
        }

        /// <summary> 길드 채팅. 길드원이 아니면 GUILD_NOT_MEMBER다. 접속 중인 길드원(보낸 사람 포함)마다 행을 넣는다 </summary>
        public Task<SocialChatOutcome> GuildChatAsync(SocialCaller caller, Guid actor, string text,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(text);
            return RunAsync(async (connection, transaction, token) =>
            {
                if (!await IsConnectedHereAsync(connection, transaction, caller, actor, token))
                {
                    return SocialChatOutcome.Rejected(ContractCodes.RejectReason.SOCIAL_NOT_CONNECTED);
                }

                MembershipRow? mine = await ReadMembershipAsync(connection, transaction, actor, token);
                if (mine == null)
                {
                    return SocialChatOutcome.Rejected(ContractCodes.RejectReason.GUILD_NOT_MEMBER);
                }

                // 추방, 탈퇴(길드 행 FOR UPDATE)가 끝나기를 기다린 뒤 소속을 다시 읽어, 커밋된 추방 뒤의 줄이 나간 캐릭터에게 가지 않게 한다
                await connection.ExecuteAsync(new CommandDefinition(
                    "SELECT 1 FROM guilds WHERE guild_id = @GuildId FOR SHARE;", new { mine.GuildId }, transaction,
                    cancellationToken: token));
                mine = await ReadMembershipAsync(connection, transaction, actor, token);
                if (mine == null)
                {
                    return SocialChatOutcome.Rejected(ContractCodes.RejectReason.GUILD_NOT_MEMBER);
                }

                List<Guid> members = await ReadMemberIdsAsync(connection, transaction, mine.GuildId, token);
                List<LiveLeaseRow> live = await ReadLiveLeasesAsync(connection, transaction, members, token);
                string senderName = await ReadNameAsync(connection, transaction, actor, token);
                IReadOnlyList<SocialDeliveryRecord> local = await InsertChatAsync(connection, transaction, caller, live,
                    SocialCodes.DeliveryKind.GUILD_CHAT, SocialCodes.ChatChannel.GUILD, actor, senderName, text, token);
                return new SocialChatOutcome(NONE, senderName, local);
            }, outcome => outcome.ReasonCode == NONE, cancellationToken);
        }

        /// <summary> 수신 확인 행을 지우고 호출 인스턴스에 임대가 있는 캐릭터 앞의 꺼낼 행을 돌려준다.
        /// 확인은 delivery_id와 수신 캐릭터가 모두 맞고 그 캐릭터의 임대가 호출 인스턴스에 있는 행만 지운다.
        /// 꺼내기는 next_attempt_at이 지난 행을 수신자당 최대 20개, 전체 최대 200개 delivery_id 순으로 고르고
        /// 같은 문장에서 next_attempt_at을 재전송 간격 뒤로 미룬다. 한 수신자의 미확인 행이 쌓여도 다른 수신자를 막지 않는다 </summary>
        public Task<SocialPullOutcome> PullAsync(SocialCaller caller, IReadOnlyCollection<SocialDeliveryAckInput> acks,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(acks);
            long[] ackIds = acks.Select(ack => ack.DeliveryId).ToArray();
            Guid[] ackTargets = acks.Select(ack => ack.RecipientCharacterId).ToArray();
            return RunAsync(async (connection, transaction, token) =>
            {
                int acknowledged = 0;
                if (ackIds.Length > 0)
                {
                    acknowledged = await connection.ExecuteAsync(new CommandDefinition(
                        """
                        DELETE FROM social_deliveries AS delivery
                        USING unnest(@Ids, @Targets) AS ack(delivery_id, target_character_id), account_leases AS lease
                        WHERE delivery.delivery_id = ack.delivery_id AND delivery.target_character_id = ack.target_character_id
                            AND lease.character_id = delivery.target_character_id AND lease.instance_id = @InstanceId
                            AND lease.expires_at > clock_timestamp();
                        """, new { Ids = ackIds, Targets = ackTargets, caller.InstanceId }, transaction,
                        cancellationToken: token));
                }

                IEnumerable<DeliveryRow> rows = await connection.QueryAsync<DeliveryRow>(new CommandDefinition(
                    """
                    WITH ranked AS (
                        SELECT delivery.delivery_id,
                            row_number() OVER (PARTITION BY delivery.target_character_id ORDER BY delivery.delivery_id) AS position
                        FROM social_deliveries AS delivery
                        JOIN account_leases AS lease ON lease.character_id = delivery.target_character_id
                        WHERE lease.instance_id = @InstanceId AND lease.expires_at > clock_timestamp()
                            AND delivery.expires_at > clock_timestamp() AND delivery.next_attempt_at <= clock_timestamp()
                    ),
                    picked AS (
                        SELECT delivery.delivery_id FROM social_deliveries AS delivery
                        WHERE delivery.delivery_id IN (
                            SELECT delivery_id FROM ranked WHERE position <= @PerRecipient
                            ORDER BY delivery_id LIMIT @Limit)
                        FOR UPDATE SKIP LOCKED
                    )
                    UPDATE social_deliveries AS delivery SET next_attempt_at = clock_timestamp() + @Resend
                    FROM picked WHERE delivery.delivery_id = picked.delivery_id
                    RETURNING delivery.delivery_id AS "DeliveryId", delivery.target_character_id AS "TargetCharacterId",
                        delivery.kind AS "Kind", delivery.channel AS "Channel",
                        delivery.sender_character_id AS "SenderCharacterId", delivery.sender_name AS "SenderName",
                        delivery.payload AS "Payload", delivery.created_at AS "CreatedAt";
                    """, new
                    {
                        caller.InstanceId, PerRecipient = SocialLimits.MAX_DELIVERIES_PER_RECIPIENT,
                        Limit = SocialLimits.MAX_DELIVERIES_PER_PULL, Resend
                    }, transaction, cancellationToken: token));
                return new SocialPullOutcome(acknowledged, ToRecords(rows).OrderBy(row => row.DeliveryId).ToArray());
            }, _ => true, cancellationToken);
        }

        // 수신자마다 행을 넣고, 호출 인스턴스에 임대가 있는 수신자의 남은 행(새 행 이하)을 오름차순으로 돌려준다.
        // 남은 행이 수신자당 상한을 넘으면 그 수신자는 로컬 전달 없이 꺼내기에 맡긴다. 돌려준 행은 꺼내기에서 재전송 간격만큼 빠진다
        private static async Task<IReadOnlyList<SocialDeliveryRecord>> InsertChatAsync(DbConnection connection,
            DbTransaction transaction, SocialCaller caller, IReadOnlyList<LiveLeaseRow> recipients, int kind, int channel,
            Guid sender, string senderName, string text, CancellationToken cancellationToken)
        {
            Guid[] targets = recipients.Select(row => row.CharacterId).OrderBy(id => id).ToArray();
            IEnumerable<(long DeliveryId, Guid TargetCharacterId)> inserted =
                await connection.QueryAsync<(long, Guid)>(new CommandDefinition(
                    """
                    INSERT INTO social_deliveries (target_character_id, kind, channel, sender_character_id, sender_name, payload,
                        expires_at)
                    SELECT target.id, @Kind, @Channel, @Sender, @SenderName, @Payload, clock_timestamp() + @Ttl
                    FROM unnest(@Targets) WITH ORDINALITY AS target(id, position)
                    ORDER BY target.position
                    RETURNING delivery_id, target_character_id;
                    """, new
                    {
                        Kind = kind, Channel = channel, Sender = sender, SenderName = senderName, Payload = text,
                        Ttl = DeliveryTtl, Targets = targets
                    }, transaction, cancellationToken: cancellationToken));

            HashSet<Guid> local = recipients.Where(row => row.InstanceId == caller.InstanceId)
                .Select(row => row.CharacterId).ToHashSet();
            (long DeliveryId, Guid TargetCharacterId)[] bounds = inserted.Where(row => local.Contains(row.TargetCharacterId))
                .ToArray();
            if (bounds.Length == 0)
            {
                return Array.Empty<SocialDeliveryRecord>();
            }

            IEnumerable<DeliveryRow> rows = await connection.QueryAsync<DeliveryRow>(new CommandDefinition(
                """
                WITH bound AS (
                    SELECT * FROM unnest(@Targets, @Bounds) AS bound(target_character_id, delivery_id)
                ),
                remaining AS (
                    SELECT delivery.delivery_id,
                        count(*) OVER (PARTITION BY delivery.target_character_id) AS total
                    FROM social_deliveries AS delivery
                    JOIN bound ON bound.target_character_id = delivery.target_character_id
                    WHERE delivery.delivery_id <= bound.delivery_id AND delivery.expires_at > clock_timestamp()
                )
                UPDATE social_deliveries AS delivery SET next_attempt_at = clock_timestamp() + @Resend
                FROM remaining
                WHERE delivery.delivery_id = remaining.delivery_id AND remaining.total <= @PerRecipient
                RETURNING delivery.delivery_id AS "DeliveryId", delivery.target_character_id AS "TargetCharacterId",
                    delivery.kind AS "Kind", delivery.channel AS "Channel",
                    delivery.sender_character_id AS "SenderCharacterId", delivery.sender_name AS "SenderName",
                    delivery.payload AS "Payload", delivery.created_at AS "CreatedAt";
                """, new
                {
                    Targets = bounds.Select(row => row.TargetCharacterId).ToArray(),
                    Bounds = bounds.Select(row => row.DeliveryId).ToArray(),
                    PerRecipient = SocialLimits.MAX_DELIVERIES_PER_RECIPIENT, Resend
                }, transaction, cancellationToken: cancellationToken));
            return ToRecords(rows);
        }

        // 살아 있는 임대(접속 판정 조건)가 있는 캐릭터와 그 임대 인스턴스
        private static async Task<List<LiveLeaseRow>> ReadLiveLeasesAsync(DbConnection connection, DbTransaction transaction,
            IReadOnlyCollection<Guid> characterIds, CancellationToken cancellationToken)
        {
            if (characterIds.Count == 0)
            {
                return new List<LiveLeaseRow>();
            }

            IEnumerable<LiveLeaseRow> rows = await connection.QueryAsync<LiveLeaseRow>(new CommandDefinition(
                """
                SELECT lease.character_id AS "CharacterId", lease.instance_id AS "InstanceId", lease.role AS "Role"
                FROM account_leases AS lease
                JOIN server_instances AS instance ON instance.instance_id = lease.instance_id AND instance.role = lease.role
                JOIN characters AS character_row ON character_row.character_id = lease.character_id
                WHERE lease.character_id = ANY(@Ids) AND lease.expires_at > clock_timestamp()
                    AND character_row.deleted_at IS NULL
                    AND instance.state <> 'Offline' AND instance.heartbeat_at > clock_timestamp() - @Heartbeat
                ORDER BY lease.character_id;
                """, new { Ids = characterIds.ToArray(), Heartbeat }, transaction, cancellationToken: cancellationToken));
            return rows.ToList();
        }

        // 수신자, delivery_id 오름차순. RETURNING의 순서는 보장되지 않아 여기서 정렬한다
        private static IReadOnlyList<SocialDeliveryRecord> ToRecords(IEnumerable<DeliveryRow> rows)
        {
            return rows.OrderBy(row => row.TargetCharacterId).ThenBy(row => row.DeliveryId)
                .Select(row => new SocialDeliveryRecord(row.DeliveryId, row.TargetCharacterId, row.Kind, row.Channel,
                    row.SenderCharacterId, row.SenderName ?? string.Empty, row.Payload ?? string.Empty,
                    DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)))
                .ToArray();
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class DeliveryRow
        {
            public long DeliveryId { get; set; }
            public Guid TargetCharacterId { get; set; }
            public int Kind { get; set; }
            public int Channel { get; set; }
            public Guid? SenderCharacterId { get; set; }
            public string? SenderName { get; set; }
            public string? Payload { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        private sealed class LiveLeaseRow
        {
            public Guid CharacterId { get; set; }
            public Guid InstanceId { get; set; }
            public int Role { get; set; }
        }
    }
}
