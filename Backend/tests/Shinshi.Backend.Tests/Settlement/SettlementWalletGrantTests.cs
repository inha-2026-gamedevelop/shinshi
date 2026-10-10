// System
using System;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Contracts;
using Shinshi.Data.Ledger;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Settlement
{
    /// <summary> 정산안 지갑 칸(정련편, 티켓, 엽전)과 부적 칸의 1회 지급과 실제 원장, 장애 취소의 기본 + 추가 전액 지급을
    /// 실제 PostgreSQL에서 검사한다 </summary>
    public sealed class SettlementWalletGrantTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const long   TICKETS          = 3;
        private const long   POTIONS          = 4;
        private const long   POTIONS_RESERVED = 2;
        private const long   ESSENCE_GRANT    = 2100;
        private const long   SHARD_GRANT      = 120;
        private const long   TICKET_GRANT     = 2;
        private const long   CURRENCY_GRANT   = 120;
        private const long   MATERIAL_GRANT   = 3;
        private const long   TALISMAN_A_GRANT = 2;
        private const long   TALISMAN_B_GRANT = 1;
        private const string MATERIAL_ID      = "item_boss_material_common";
        private const string TALISMAN_A       = "T01-1A";
        private const string TALISMAN_B       = "T01-2B";
        private const int    CONCURRENT_CALLS = 4;
        private const int    RECOVERY_BATCH   = 8;

        // 정기, 정련편, 티켓, 엽전, 부적 두 효과, 보스 재료 하나
        private const long   SETTLEMENT_LINES = 7;

        // 장애 취소: 기본 + 추가 전액(홀수 적립도 내림 없음). 강제 종료는 추가 floor(50%)를 유지한다
        private const long   ODD_BASE         = 200;
        private const long   ODD_EXTRA        = 101;
        private const long   ODD_PAID         = 301;
        private const long   ODD_FORCE_PAID   = 250;
        private const long   EVEN_BASE        = 100;
        private const long   EVEN_EXTRA       = 0;
        private const long   EVEN_PAID        = 100;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 같은 operationId 정산을 동시에 여러 번 불러도 지갑, 정련편, 부적, 아이템이 한 번만 들어가고
        /// 실제 economy_ledger의 자산별 합계가 지급량과 같다 </summary>
        [PostgresFact]
        public async Task Settle_WalletAndTalismans_AppliesOnceWithRealLedger()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid participant = session.Participants[0].ParticipantId;
            Guid settlementId = Guid.NewGuid();
            SettlementGrantInput grant = SessionEventTests.Grant(participant, ESSENCE_GRANT, new SettlementItemInput
            {
                DefinitionId = MATERIAL_ID, BindingKind = SessionTestEnvironment.BINDING, Quantity = MATERIAL_GRANT
            });
            grant.RefineShards = SHARD_GRANT;
            grant.Tickets = TICKET_GRANT;
            grant.Currency = CURRENCY_GRANT;
            grant.Talismans.Add(SettlementWalletValidatorTests.Talisman(TALISMAN_A, TALISMAN_A_GRANT));
            grant.Talismans.Add(SettlementWalletValidatorTests.Talisman(TALISMAN_B, TALISMAN_B_GRANT));
            OperationResult appended = await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Ended(1, settlementId, new[] { grant })
            });
            PostgresSettlementRepository settlement = new PostgresSettlementRepository(environment.DataSource,
                environment.Executor, new LedgerWriter(new PostgresEconomyLedgerWriter()));

            OperationResult[] results = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_CALLS).Select(_ =>
                settlement.SettleAsync(session.InstanceId, session.SessionId, settlementId)));

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, appended.Status);
            Assert.All(results, result =>
            {
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
                Assert.Equal(results[0].ReceiptId, result.ReceiptId);
            });
            object owner = new { player.AccountId, player.CharacterId };
            Assert.Equal(ESSENCE_GRANT, await environment.ScalarAsync(
                "SELECT essence FROM characters WHERE character_id = @CharacterId;", owner));
            Assert.Equal(SHARD_GRANT, await environment.ScalarAsync(
                "SELECT shards FROM characters WHERE character_id = @CharacterId;", owner));
            Assert.Equal(TICKETS - 1 + TICKET_GRANT, await environment.ScalarAsync(
                "SELECT tickets FROM account_wallets WHERE account_id = @AccountId;", owner));
            Assert.Equal(CURRENCY_GRANT, await environment.ScalarAsync(
                "SELECT currency FROM account_wallets WHERE account_id = @AccountId;", owner));
            Assert.Equal(TALISMAN_A_GRANT, await ItemQuantityAsync(environment, player, TALISMAN_A));
            Assert.Equal(TALISMAN_B_GRANT, await ItemQuantityAsync(environment, player, TALISMAN_B));
            Assert.Equal(MATERIAL_GRANT, await ItemQuantityAsync(environment, player, MATERIAL_ID));

            // 시드 잔액은 원장 밖이고 시작 티켓 소비는 테스트 원장에 있으므로, 정산 operationId의 실제 원장만 본다
            Assert.Equal(SETTLEMENT_LINES, await environment.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE operation_id = @Id;", new { Id = settlementId }));
            Assert.Equal(SETTLEMENT_LINES, await environment.ScalarAsync(
                "SELECT count(*) FROM economy_ledger WHERE reason = @Reason;",
                new { Reason = ContractCodes.EconomyReason.SESSION_SETTLEMENT }));
            Assert.Equal(ESSENCE_GRANT, await RealLedgerSumAsync(environment, player, EconomyAsset.ESSENCE));
            Assert.Equal(SHARD_GRANT, await RealLedgerSumAsync(environment, player, EconomyAsset.SHARDS));
            Assert.Equal(TICKET_GRANT, await RealLedgerSumAsync(environment, player, EconomyAsset.TICKETS));
            Assert.Equal(CURRENCY_GRANT, await RealLedgerSumAsync(environment, player, EconomyAsset.CURRENCY));
            Assert.Equal(TALISMAN_A_GRANT + TALISMAN_B_GRANT + MATERIAL_GRANT,
                await RealLedgerSumAsync(environment, player, EconomyAsset.ACCOUNT_ITEM));
            Assert.Equal(SettlementSanityLimits.ACCOUNT_BINDING_KIND, await environment.TextAsync(
                "SELECT binding_kind FROM economy_ledger WHERE asset_id = @Id;", new { Id = TALISMAN_A }));

            // 원장 줄의 잔액 기록이 실제 잔액과 같다(원장 합계 = 잔액, 시드가 0인 자산)
            Assert.Equal(0L, await environment.ScalarAsync(
                """
                SELECT count(*) FROM economy_ledger AS ledger
                LEFT JOIN characters AS hero ON hero.character_id = ledger.character_id
                LEFT JOIN account_wallets AS wallet ON wallet.account_id = ledger.account_id
                LEFT JOIN account_items AS item ON item.account_id = ledger.account_id
                    AND item.definition_id = ledger.asset_id AND item.binding_kind = ledger.binding_kind
                WHERE ledger.balance_after <> CASE ledger.asset_kind
                    WHEN 'Essence' THEN hero.essence WHEN 'Shards' THEN hero.shards
                    WHEN 'Tickets' THEN wallet.tickets WHEN 'Currency' THEN wallet.currency
                    ELSE item.quantity END;
                """));
        }

        /// <summary> 종료 이벤트 없이 프로세스가 사라지면 기본 정기와 추가 정기를 전액 한 번 지급한다 </summary>
        [PostgresFact]
        public async Task ServiceCancel_BaseAndExtraAccrued_GrantsFullBaseAndFullExtraOnce()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer odd = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer even = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { odd, even },
                POTIONS_RESERVED);
            Guid oddParticipant = session.Participants.Single(row => row.AccountId == odd.AccountId).ParticipantId;
            Guid evenParticipant = session.Participants.Single(row => row.AccountId == even.AccountId).ParticipantId;
            OperationResult appended = await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SettlementWalletValidatorTests.BaseEssence(1, oddParticipant, ODD_BASE / 2),
                SessionEventTests.Essence(2, oddParticipant, ODD_EXTRA),
                SettlementWalletValidatorTests.BaseEssence(3, oddParticipant, ODD_BASE / 2),
                SettlementWalletValidatorTests.BaseEssence(4, evenParticipant, EVEN_BASE)
            });
            await environment.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '2 minutes' WHERE instance_id = @Id;",
                new { Id = session.InstanceId });

            SessionRecoveryResult recovered = await environment.Settlement.RecoverLostSessionsAsync(RECOVERY_BATCH);
            OperationResult replay = (await environment.Settlement.ServiceCancelAsync(session.SessionId))!;

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, appended.Status);
            Assert.Equal(1, recovered.Cancelled);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, replay.Status);
            Assert.Equal(ODD_BASE, await environment.ScalarAsync(
                "SELECT base_essence_accrued FROM session_participants WHERE participant_id = @Id;",
                new { Id = oddParticipant }));
            Assert.Equal(ODD_PAID, (await environment.ReadWalletAsync(odd.AccountId)).Essence);
            Assert.Equal(EVEN_PAID, (await environment.ReadWalletAsync(even.AccountId)).Essence);
            Assert.Equal(ODD_PAID, await environment.LedgerSumAsync(odd.AccountId, EconomyLedgerCodes.ASSET_ESSENCE));
            Assert.Equal(EVEN_PAID, await environment.LedgerSumAsync(even.AccountId, EconomyLedgerCodes.ASSET_ESSENCE));
            Assert.Equal(2L, await environment.ScalarAsync(
                "SELECT count(*) FROM test_economy_ledger WHERE reason = @Reason;",
                new { Reason = EconomyLedgerCodes.REASON_SERVICE_CANCEL_GRANT }));
        }

        /// <summary> 장애 취소 지급 정기는 기본 + 추가 전액이고 출정 상한을 넘지 않는다 </summary>
        [Theory]
        [InlineData(ODD_BASE, ODD_EXTRA, ODD_PAID)]
        [InlineData(EVEN_BASE, EVEN_EXTRA, EVEN_PAID)]
        [InlineData(0, 1, 1)]
        [InlineData(600, 1500, 2100)]
        [InlineData(SettlementSanityLimits.MAX_SESSION_ESSENCE, SettlementSanityLimits.MAX_SESSION_ESSENCE,
            SettlementSanityLimits.MAX_SESSION_ESSENCE)]
        public void CalculateServiceCancelEssence_FullBasePlusFullExtra(long baseAccrued, long extraAccrued, long expected)
        {
            Assert.Equal(expected, PostgresSettlementRepository.CalculateServiceCancelEssence(baseAccrued, extraAccrued));
        }

        /// <summary> 강제 종료는 정책 미정이라 기본 전액 + floor(추가 / 2)를 유지한다 </summary>
        [Theory]
        [InlineData(ODD_BASE, ODD_EXTRA, ODD_FORCE_PAID)]
        [InlineData(EVEN_BASE, EVEN_EXTRA, EVEN_PAID)]
        [InlineData(0, 1, 0)]
        [InlineData(600, 1500, 1350)]
        [InlineData(SettlementSanityLimits.MAX_SESSION_ESSENCE, SettlementSanityLimits.MAX_SESSION_ESSENCE,
            SettlementSanityLimits.MAX_SESSION_ESSENCE)]
        public void CalculateForceCloseEssence_FullBasePlusHalfExtraRoundedDown(long baseAccrued, long extraAccrued,
            long expected)
        {
            Assert.Equal(expected, PostgresSettlementRepository.CalculateForceCloseEssence(baseAccrued, extraAccrued));
        }

        /// <summary> 기본 정기 적립도 출정 누적 상한을 넘는 배치는 물약 소비까지 포함해 전부 거절한다. 추가 정기 상한과는 따로 센다 </summary>
        [PostgresFact]
        public async Task Append_BaseAccrualPastSessionCap_RejectsWholeBatchWithoutEffects()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid participant = session.Participants[0].ParticipantId;

            OperationResult first = await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SettlementWalletValidatorTests.BaseEssence(1, participant, SettlementSanityLimits.MAX_SESSION_ESSENCE),
                SessionEventTests.Essence(2, participant, SettlementSanityLimits.MAX_SESSION_ESSENCE)
            });
            OperationResult over = await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Potion(3, participant),
                SettlementWalletValidatorTests.BaseEssence(4, participant, 1)
            });

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Status);
            Assert.Equal(SettlementSanityLimits.REJECT_REASON, over.ReasonCode);
            Assert.Equal(SettlementSanityLimits.MAX_SESSION_ESSENCE, await environment.ScalarAsync(
                "SELECT base_essence_accrued FROM session_participants WHERE participant_id = @Id;",
                new { Id = participant }));
            Assert.Equal(2L, await environment.ScalarAsync(
                "SELECT last_event_seq FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(POTIONS, wallet.Potions);
            Assert.Equal(POTIONS_RESERVED, wallet.PotionsReserved);
        }

        /// <summary> 엽전은 보스 처치 성공에서만 발행하므로 일반전 정산안의 엽전은 배치 전체를 거절한다 </summary>
        [PostgresFact]
        public async Task Append_CurrencyInNormalSession_RejectsWithoutSettling()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.NORMAL, new[] { player },
                POTIONS_RESERVED);
            SettlementGrantInput grant = SessionEventTests.Grant(session.Participants[0].ParticipantId, ESSENCE_GRANT);
            grant.Currency = CURRENCY_GRANT;

            OperationResult rejected = await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Ended(1, Guid.NewGuid(), new[] { grant })
            });

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, rejected.Status);
            Assert.Equal(SettlementSanityLimits.REJECT_REASON, rejected.ReasonCode);
            Assert.Equal(GameSessionStates.RUNNING, await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM session_events;"));
        }

        /// <summary> 0007 이전 형식으로 저장된 종료 정산안(지갑, 부적 칸 없음)도 정기와 아이템만 그대로 정산한다 </summary>
        [PostgresFact]
        public async Task Settle_StoredPlanWithoutWalletFields_SettlesEssenceOnly()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid settlementId = Guid.NewGuid();
            string payload = "{\"endReason\":0,\"settlementOperationId\":\"" + settlementId.ToString("D")
                + "\",\"grants\":[{\"participantId\":\"" + session.Participants[0].ParticipantId.ToString("D")
                + "\",\"essence\":" + ESSENCE_GRANT + ",\"items\":[]}]}";
            await environment.ExecuteAsync(
                """
                INSERT INTO session_events (session_id, event_seq, event_id, event_kind, payload, operation_id)
                VALUES (@SessionId, 1, gen_random_uuid(), @Ended, CAST(@Payload AS jsonb), gen_random_uuid());
                UPDATE game_sessions SET state = 'Settling', last_event_seq = 1, ended_event_seq = 1,
                    settlement_operation_id = @SettlementId, end_reason = 0, settling_at = clock_timestamp()
                WHERE session_id = @SessionId;
                """, new { SessionId = session.SessionId, Ended = GameSessionCodes.EventKind.SESSION_ENDED,
                    Payload = payload, SettlementId = settlementId });

            OperationResult result = await environment.Settlement.SettleAsync(session.InstanceId, session.SessionId,
                settlementId);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
            Assert.Equal(ESSENCE_GRANT, (await environment.ReadWalletAsync(player.AccountId)).Essence);
            Assert.Equal(TICKETS - 1, (await environment.ReadWalletAsync(player.AccountId)).Tickets);
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT shards FROM characters WHERE character_id = @Id;", new { Id = player.CharacterId }));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM test_economy_ledger WHERE asset_kind IN ('Shards', 'Currency');"));
        }

        private static Task<long> ItemQuantityAsync(SessionTestEnvironment environment, TestPlayer player,
            string definitionId)
        {
            return environment.ScalarAsync(
                """
                SELECT quantity FROM account_items
                WHERE account_id = @AccountId AND definition_id = @DefinitionId AND binding_kind = @Binding;
                """, new { player.AccountId, DefinitionId = definitionId,
                    Binding = SettlementSanityLimits.ACCOUNT_BINDING_KIND });
        }

        private static Task<long> RealLedgerSumAsync(SessionTestEnvironment environment, TestPlayer player,
            string assetKind)
        {
            return environment.ScalarAsync(
                "SELECT COALESCE(sum(delta), 0)::bigint FROM economy_ledger WHERE account_id = @AccountId AND asset_kind = @Kind;",
                new { player.AccountId, Kind = assetKind });
        }
    }
}
