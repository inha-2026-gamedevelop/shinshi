// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

// Third Party
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data;
using Shinshi.Data.Admin;
using Shinshi.Data.Ledger;
using Shinshi.Data.Reservations;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 실제 PostgreSQL에서 원장 필터와 keyset 페이지, 사유별 합계, 아이템 이력 순서, 세션 재생, 작업 연결 행,
    /// 감사 로그 필터를 검사한다 </summary>
    public sealed class AdminLogsPostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long   GRANT_AMOUNT   = 10;
        private const int    GRANT_COUNT    = 5;
        private const long   ESSENCE_AMOUNT = 7;
        private const int    ESSENCE_COUNT  = 2;
        private const long   REVOKE_AMOUNT  = -3;
        private const int    PAGE_SIZE      = 3;
        private const int    LARGE_PAGE     = 100;
        private const string GRANT_SOURCE   = "AdminGrant";
        private const string GRANT_ID       = "grant-one";
        private const string REVOKE_ID      = "grant-two";
        private const string SEED_SOURCE    = "Seed";
        private const string SEED_ID        = "seed-one";

        private const long   TICKETS        = 3;
        private const long   POTIONS        = 4;
        private const long   ACCRUED        = 30;
        private const long   SETTLED        = 70;

        private const string TARGET_KIND     = "game_session";
        private const string AUDIT_ACTION    = "test.audit";
        private const string AUDIT_REASON    = "check audit filters";
        private const int    IP_HASH_BYTES   = 32;
        private const int    IP_PREFIX_LEN   = 8;
        private const int    IP_PREFIX_BYTES = 4;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 작은 페이지로 끝까지 이어 받아도 계정 원장이 시간순으로 한 번씩만 나오고, 합계는 첫 페이지에만 있다 </summary>
        [PostgresFact]
        public async Task ReadPage_CursorPaging_ReturnsEveryRowOnceWithFirstPageTotals()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            (Guid account, Guid character, List<Guid> expected) = await SeedLedgerAsync(database);
            PostgresAdminLedgerReader reader = new PostgresAdminLedgerReader(database.DataSource);

            List<AdminLedgerEntry> collected = new List<AdminLedgerEntry>();
            List<AdminLedgerPage> pages = new List<AdminLedgerPage>();
            AdminLogCursor? cursor = null;
            do
            {
                AdminLedgerPage page = await reader.ReadPageAsync(new AdminLedgerQuery
                {
                    AccountId = account, Cursor = cursor, Limit = PAGE_SIZE
                });
                pages.Add(page);
                collected.AddRange(page.Entries);
                cursor = null;
                if (page.NextCursor != null)
                {
                    Assert.True(AdminLogCursor.TryDecode(page.NextCursor, out cursor));
                }
            }
            while (cursor != null);

            Assert.Equal(expected.Select(id => id.ToString("D")), collected.Select(entry => entry.OperationId));
            Assert.Equal(collected.Count, collected.Select(entry => entry.LedgerId).Distinct().Count());
            Assert.True(collected.Zip(collected.Skip(1)).All(pair => pair.First.OccurredAtUnixMs <= pair.Second.OccurredAtUnixMs));
            Assert.All(pages.Take(pages.Count - 1), page => Assert.Equal(PAGE_SIZE, page.Entries.Length));
            Assert.Null(pages[pages.Count - 1].NextCursor);

            // 합계는 첫 페이지에서만 필터 전체로 계산하고 다음 페이지는 null이다
            Assert.True(pages.Count > 1);
            AdminLedgerPage first = pages[0];
            Assert.Equal(3, first.Totals.Length);
            AdminLedgerReasonTotal grant = Assert.Single(first.Totals,
                total => total.Reason == ContractCodes.EconomyReason.ADMIN_GRANT);
            Assert.Equal((GRANT_AMOUNT * GRANT_COUNT).ToString(), grant.DeltaTotal);
            Assert.Equal(GRANT_COUNT, grant.Count);
            Assert.Equal(EconomyAsset.CURRENCY, grant.AssetKind);
            AdminLedgerReasonTotal essence = Assert.Single(first.Totals,
                total => total.Reason == ContractCodes.EconomyReason.INITIAL_GRANT);
            Assert.Equal((ESSENCE_AMOUNT * ESSENCE_COUNT).ToString(), essence.DeltaTotal);
            AdminLedgerReasonTotal revoke = Assert.Single(first.Totals,
                total => total.Reason == ContractCodes.EconomyReason.ADMIN_REVOKE);
            Assert.Equal(REVOKE_AMOUNT.ToString(), revoke.DeltaTotal);
            Assert.All(pages.Skip(1), page => Assert.Null(page.Totals));
            Assert.Equal(character.ToString("D"), collected.First(entry => entry.AssetKind == EconomyAsset.ESSENCE).CharacterId);
        }

        /// <summary> 자산, 캐릭터, 사유, 원인, 기간 필터가 각자 조건으로 걸리고, 원인만으로도 계정 없이 조회된다 </summary>
        [PostgresFact]
        public async Task ReadPage_Filters_NarrowRowsAndRequireIndexedScope()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            (Guid account, Guid character, List<Guid> expected) = await SeedLedgerAsync(database);
            PostgresAdminLedgerReader reader = new PostgresAdminLedgerReader(database.DataSource);
            DateTimeOffset now = DateTimeOffset.UtcNow;

            AdminLedgerPage essence = await reader.ReadPageAsync(new AdminLedgerQuery
            {
                AccountId = account, AssetKind = EconomyAsset.ESSENCE, Limit = LARGE_PAGE
            });
            AdminLedgerPage byCharacter = await reader.ReadPageAsync(new AdminLedgerQuery
            {
                AccountId = account, CharacterId = character, Limit = LARGE_PAGE
            });
            AdminLedgerPage revoke = await reader.ReadPageAsync(new AdminLedgerQuery
            {
                AccountId = account, ReasonCode = ContractCodes.EconomyReason.ADMIN_REVOKE, Limit = LARGE_PAGE
            });
            AdminLedgerPage bySource = await reader.ReadPageAsync(new AdminLedgerQuery
            {
                SourceType = GRANT_SOURCE, SourceId = GRANT_ID, Limit = LARGE_PAGE
            });
            AdminLedgerPage past = await reader.ReadPageAsync(new AdminLedgerQuery
            {
                AccountId = account, To = now.AddHours(-1), Limit = LARGE_PAGE
            });
            AdminLedgerPage window = await reader.ReadPageAsync(new AdminLedgerQuery
            {
                AccountId = account, From = now.AddHours(-1), To = now.AddHours(1), Limit = LARGE_PAGE
            });

            Assert.Equal(ESSENCE_COUNT, essence.Entries.Length);
            Assert.Single(essence.Totals);
            Assert.Equal(ESSENCE_COUNT, byCharacter.Entries.Length);
            Assert.Equal(REVOKE_AMOUNT, Assert.Single(revoke.Entries).Delta);
            // 원인 조회에는 다른 계정의 같은 지급 줄도 나온다
            Assert.Equal(GRANT_COUNT + 1, bySource.Entries.Length);
            Assert.Equal(2, bySource.Entries.Select(entry => entry.AccountId).Distinct().Count());
            Assert.Empty(past.Entries);
            Assert.Empty(past.Totals);
            Assert.Equal(expected.Count, window.Entries.Length);
            await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadPageAsync(new AdminLedgerQuery
            {
                SourceType = GRANT_SOURCE, Limit = LARGE_PAGE
            }));
        }

        /// <summary> 이력을 seq 역순으로 써도 seq 오름차순으로 읽고, 상태 JSON을 저장값 그대로 싣는다 </summary>
        [PostgresFact]
        public async Task ReadItemHistory_OutOfOrderWrites_ReturnsSeqOrderWithStoredJson()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid account = Guid.NewGuid();
            Guid actor = Guid.NewGuid();
            Guid item = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, account);
            IdempotentOperationExecutor executor = CreateExecutor(database);
            PostgresItemHistoryWriter writer = new PostgresItemHistoryWriter();
            Guid moved = await AdminLogTestSupport.RunAsync(executor, actor, async (operationId, context, token) =>
            {
                Assert.True(await writer.AppendAsync(context, new ItemHistoryEntry(item, 2, "Moved",
                    "{\"slot\":\"bag\"}", "{\"slot\":\"vault\"}", account, actor, operationId), token));
            });
            Guid created = await AdminLogTestSupport.RunAsync(executor, actor, async (operationId, context, token) =>
            {
                Assert.True(await writer.AppendAsync(context, new ItemHistoryEntry(item, 0, "Created", null,
                    "{\"level\":0,\"note\":\"2026-10-01T00:00:00Z\"}", account, actor, operationId), token));
                Assert.True(await writer.AppendAsync(context, new ItemHistoryEntry(item, 1, "Enhanced",
                    "{\"level\":0}", "{\"level\":1}", account, actor, operationId), token));
            });
            PostgresAdminTraceReader reader = new PostgresAdminTraceReader(database.DataSource);

            AdminItemHistoryResponse? history = await reader.ReadItemHistoryAsync(item);
            AdminItemHistoryResponse? missing = await reader.ReadItemHistoryAsync(Guid.NewGuid());

            Assert.NotNull(history);
            Assert.Null(missing);
            Assert.Equal(new long[] { 0, 1, 2 }, history.Entries.Select(entry => entry.Seq));
            Assert.Equal(new[] { created, created, moved }.Select(id => id.ToString("D")),
                history.Entries.Select(entry => entry.OperationId));
            Assert.Null(history.Entries[0].BeforeState);
            // 날짜처럼 보이는 문자열도 바뀌지 않고 그대로 나와야 한다
            Assert.True(JToken.DeepEquals(JObject.Parse("{\"level\":0,\"note\":\"2026-10-01T00:00:00Z\"}"),
                JObject.Parse(history.Entries[0].AfterState.ToString())));
            Assert.Contains("2026-10-01T00:00:00Z", history.Entries[0].AfterState.ToString());
            Assert.Equal(1, JObject.Parse(history.Entries[1].AfterState.ToString()).Value<int>("level"));
        }

        /// <summary> 세션 재생은 요약, 참가자, seq순 이벤트, 정산 작업과 그 원장 줄을 함께 돌려준다 </summary>
        [PostgresFact]
        public async Task ReadSessionEvents_SettledSession_ReturnsEventsParticipantsAndSettlement()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.NORMAL, new[] { player }, 0);
            Guid participant = session.Participants[0].ParticipantId;
            Guid settlementId = Guid.NewGuid();
            OperationResult appended = await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Loot(1),
                SessionEventTests.Essence(2, participant, ACCRUED),
                SessionEventTests.Ended(3, settlementId, new[] { SessionEventTests.Grant(participant, SETTLED) })
            });
            PostgresSettlementRepository settlement = new PostgresSettlementRepository(environment.DataSource,
                environment.Executor, new LedgerWriter(new PostgresEconomyLedgerWriter()));
            OperationResult settled = await settlement.SettleAsync(session.InstanceId, session.SessionId, settlementId);
            PostgresAdminTraceReader reader = new PostgresAdminTraceReader(environment.DataSource);

            AdminSessionEventsResponse? replay = await reader.ReadSessionEventsAsync(session.SessionId);
            AdminSessionEventsResponse? missing = await reader.ReadSessionEventsAsync(Guid.NewGuid());

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, appended.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, settled.Status);
            Assert.NotNull(replay);
            Assert.Null(missing);
            Assert.Equal("Closed", replay.Session.State);
            Assert.Equal("Settled", replay.Session.Result);
            Assert.Equal(settlementId.ToString("D"), replay.Session.SettlementOperationId);
            Assert.Equal(3L, replay.Session.EndedEventSeq);
            Assert.NotNull(replay.Session.ClosedAtUnixMs);
            Assert.Equal(new long[] { 1, 2, 3 }, replay.Events.Select(item => item.EventSeq));
            Assert.Equal(GameSessionCodes.EventKind.SESSION_ENDED, replay.Events[2].EventKind);
            Assert.Equal(settlementId.ToString("D"),
                JObject.Parse(replay.Events[2].Payload.ToString()).Value<string>("settlementOperationId"));
            AdminSessionParticipant only = Assert.Single(replay.Participants);
            Assert.Equal(player.AccountId.ToString("D"), only.AccountId);
            Assert.Equal(ACCRUED, only.EssenceAccrued);
            Assert.Equal("Settled", only.State);
            AdminLedgerEntry granted = Assert.Single(replay.Ledger);
            Assert.Equal(SETTLED, granted.Delta);
            Assert.Equal(ContractCodes.EconomyReason.SESSION_SETTLEMENT, granted.Reason);
            Assert.Equal(settlementId.ToString("D"), granted.OperationId);
            AdminOperationRecord operation = Assert.Single(replay.Operations);
            Assert.Equal(session.InstanceId.ToString("D"), operation.ActorId);
            Assert.Equal(settlementId.ToString("D"), operation.OperationId);
            Assert.Equal("Committed", operation.Status);
            Assert.Equal(settled.ReceiptId, operation.ReceiptId);
        }

        /// <summary> 작업 조회는 같은 작업 키의 원장, 아이템 이력, outbox 줄을 함께 싣고, 없는 작업은 null이다 </summary>
        [PostgresFact]
        public async Task ReadOperation_WithLinkedRows_ReturnsLedgerHistoryAndOutbox()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            Guid account = Guid.NewGuid();
            Guid actor = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, account);
            IdempotentOperationExecutor executor = CreateExecutor(database);
            Guid operationId = await AdminLogTestSupport.RunAsync(executor, actor, async (id, context, token) =>
            {
                EconomyAsset currency = EconomyAsset.Currency(account);
                Assert.True(await new PostgresEconomyLedgerWriter().AppendAsync(context, new EconomyLedgerEntry(
                    Guid.NewGuid(), currency.Kind, currency, GRANT_AMOUNT, GRANT_AMOUNT,
                    ContractCodes.EconomyReason.ADMIN_GRANT, GRANT_SOURCE, GRANT_ID, AdminLogTestSupport.SYSTEM_ACTOR,
                    actor, id), token));
                Assert.True(await new PostgresItemHistoryWriter().AppendAsync(context, new ItemHistoryEntry(Guid.NewGuid(),
                    0, "Created", null, "{\"level\":0}", account, actor, id), token));
                Assert.True(await new PostgresOutboxWriter().EnqueueAsync(context, new OutboxMessage(Guid.NewGuid(),
                    account, LedgerTestSupport.CONSUMER_KEY, LedgerTestSupport.EVENT_KIND,
                    LedgerTestSupport.PAYLOAD_JSON, actor, id), token));
            });
            PostgresAdminTraceReader reader = new PostgresAdminTraceReader(database.DataSource);

            AdminOperationDetailResponse? detail = await reader.ReadOperationAsync(actor, operationId);
            AdminOperationDetailResponse? missing = await reader.ReadOperationAsync(actor, Guid.NewGuid());

            Assert.NotNull(detail);
            Assert.Null(missing);
            Assert.Equal("Committed", detail.Operation.Status);
            Assert.Equal(AdminLogTestSupport.OPERATION_KIND, detail.Operation.OperationKind);
            Assert.Equal(operationId.ToString("D"), detail.Operation.ReceiptId);
            Assert.Equal(operationId.ToString("D"),
                JObject.Parse(detail.Operation.Result.ToString()).Value<string>("receiptId"));
            Assert.Equal(GRANT_AMOUNT, Assert.Single(detail.Ledger).Delta);
            Assert.Equal("Created", Assert.Single(detail.ItemHistory).EventKind);
            Assert.Equal(LedgerTestSupport.CONSUMER_KEY, Assert.Single(detail.Outbox).ConsumerKey);
            Assert.Empty(detail.Audit);
        }

        /// <summary> 운영자, 대상, 기간 필터와 페이지가 맞게 걸리고, 요청 IP는 HMAC 앞 8자만, 상태에는 비밀값 키가 없다 </summary>
        [PostgresFact]
        public async Task ReadAuditPage_Filters_ReturnFilteredPagesWithHashPrefixOnly()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            AdminIdentity owner = await AdminLogTestSupport.BootstrapOwnerAsync(database.DataSource);
            PostgresAdminAuditWriter writer = new PostgresAdminAuditWriter(database.DataSource);
            byte[] ipHash = RandomNumberGenerator.GetBytes(IP_HASH_BYTES);
            string first = Guid.NewGuid().ToString("D");
            string second = Guid.NewGuid().ToString("D");
            foreach (string target in new[] { first, second, first })
            {
                await writer.AppendStandaloneAsync(AdminAuditEntry.ForAdmin(owner, AUDIT_ACTION, TARGET_KIND, target,
                    AUDIT_REASON, null, "{\"state\":\"checked\"}", requestIpHash: ipHash));
            }
            PostgresAdminAuditReader reader = new PostgresAdminAuditReader(database.DataSource);

            AdminAuditPage all = await reader.ReadPageAsync(new AdminAuditQuery { Limit = LARGE_PAGE });
            AdminAuditPage byAdmin = await reader.ReadPageAsync(new AdminAuditQuery
            {
                AdminId = owner.AdminId, Limit = LARGE_PAGE
            });
            AdminAuditPage byTarget = await reader.ReadPageAsync(new AdminAuditQuery
            {
                TargetKind = TARGET_KIND, TargetId = first, Limit = LARGE_PAGE
            });
            AdminAuditPage future = await reader.ReadPageAsync(new AdminAuditQuery
            {
                From = DateTimeOffset.UtcNow.AddHours(1), Limit = LARGE_PAGE
            });
            List<string> paged = new List<string>();
            AdminLogCursor? cursor = null;
            do
            {
                AdminAuditPage page = await reader.ReadPageAsync(new AdminAuditQuery
                {
                    AdminId = owner.AdminId, Cursor = cursor, Limit = 1
                });
                paged.AddRange(page.Entries.Select(entry => entry.AuditId));
                cursor = null;
                if (page.NextCursor != null)
                {
                    Assert.True(AdminLogCursor.TryDecode(page.NextCursor, out cursor));
                }
            }
            while (cursor != null);

            // 부트스트랩 System 행 하나와 운영자 행 셋
            Assert.Equal(4, all.Entries.Length);
            AdminAuditRecord system = all.Entries[0];
            Assert.Equal("System", system.ActorKind);
            Assert.Null(system.AdminId);
            Assert.Null(system.AdminRole);
            Assert.Equal(new[] { "loginName", "role" },
                JObject.Parse(system.AfterState.ToString()).Properties().Select(property => property.Name).OrderBy(name => name));
            Assert.Equal(3, byAdmin.Entries.Length);
            Assert.Equal(2, byTarget.Entries.Length);
            Assert.All(byTarget.Entries, entry => Assert.Equal(first, entry.TargetId));
            Assert.Empty(future.Entries);
            Assert.Equal(byAdmin.Entries.Select(entry => entry.AuditId), paged);
            AdminAuditRecord sample = byAdmin.Entries[0];
            Assert.Equal(IP_PREFIX_LEN, sample.RequestIpHashPrefix.Length);
            Assert.Equal(Convert.ToHexString(ipHash, 0, IP_PREFIX_BYTES).ToLowerInvariant(), sample.RequestIpHashPrefix);
            Assert.Equal((int)EAdminRole.Owner, sample.AdminRole);
            Assert.Equal("checked", JObject.Parse(sample.AfterState.ToString()).Value<string>("state"));
            await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadPageAsync(new AdminAuditQuery
            {
                TargetId = first, Limit = LARGE_PAGE
            }));
        }

        // 계정 A: 엽전 지급 5줄, 캐릭터 정기 2줄, 엽전 회수 1줄. 계정 B: 같은 지급 원인의 엽전 1줄
        private static async Task<(Guid Account, Guid Character, List<Guid> Operations)> SeedLedgerAsync(
            PostgresTestDatabase database)
        {
            Guid account = Guid.NewGuid();
            Guid character = Guid.NewGuid();
            Guid other = Guid.NewGuid();
            Guid actor = Guid.NewGuid();
            await LedgerTestSupport.SeedAccountAsync(database, account, character);
            await LedgerTestSupport.SeedAccountAsync(database, other);
            IdempotentOperationExecutor executor = CreateExecutor(database);
            List<Guid> operations = new List<Guid>();
            for (int index = 1; index <= GRANT_COUNT; ++index)
            {
                operations.Add(await AdminLogTestSupport.WriteLedgerAsync(executor, actor, EconomyAsset.Currency(account),
                    GRANT_AMOUNT, GRANT_AMOUNT * index, ContractCodes.EconomyReason.ADMIN_GRANT, GRANT_SOURCE, GRANT_ID));
                if (index <= ESSENCE_COUNT)
                {
                    operations.Add(await AdminLogTestSupport.WriteLedgerAsync(executor, actor,
                        EconomyAsset.Essence(account, character), ESSENCE_AMOUNT, ESSENCE_AMOUNT * index,
                        ContractCodes.EconomyReason.INITIAL_GRANT, SEED_SOURCE, SEED_ID));
                }
            }
            operations.Add(await AdminLogTestSupport.WriteLedgerAsync(executor, actor, EconomyAsset.Currency(account),
                REVOKE_AMOUNT, (GRANT_AMOUNT * GRANT_COUNT) + REVOKE_AMOUNT, ContractCodes.EconomyReason.ADMIN_REVOKE,
                GRANT_SOURCE, REVOKE_ID));
            await AdminLogTestSupport.WriteLedgerAsync(executor, actor, EconomyAsset.Currency(other), GRANT_AMOUNT,
                GRANT_AMOUNT, ContractCodes.EconomyReason.ADMIN_GRANT, GRANT_SOURCE, GRANT_ID);
            return (account, character, operations);
        }

        private static IdempotentOperationExecutor CreateExecutor(PostgresTestDatabase database)
        {
            return new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource));
        }
    }
}
