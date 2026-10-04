// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Inbox;
using Shinshi.Data.Ledger;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Sessions
{
    /// <summary> 출정 계약 필드 저장과 형식 검사, 포기 이탈 이벤트의 기록, 임대 해제, 재전송, 이탈 뒤 정산 1회를 검사한다 </summary>
    public sealed class SessionContractTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string BATTLE_ID     = "battle_forest_1";
        private const long   TICKETS       = 2;
        private const long   POTIONS       = 2;
        private const long   ESSENCE_GRANT = 30;
        private const string MIGRATION_0012 = "0012_session_contract_departure";

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void IsValid_BossNeedsBossIdAndDifficulty_NormalRejectsBossFields()
        {
            Guid town = Guid.NewGuid();
            ParticipantReservationInput[] party =
            {
                new ParticipantReservationInput(Guid.NewGuid(), Guid.NewGuid(), 0, Array.Empty<PotionReservationInput>())
            };

            Assert.False(Input(town, GameSessionCodes.Mode.BOSS, party, null, null, GameSessionCodes.Difficulty.EASY)
                .IsValid());
            Assert.False(Input(town, GameSessionCodes.Mode.BOSS, party, null, SessionTestEnvironment.BOSS_ID,
                GameSessionCodes.Difficulty.NONE).IsValid());
            Assert.False(Input(town, GameSessionCodes.Mode.NORMAL, party, null, SessionTestEnvironment.BOSS_ID,
                GameSessionCodes.Difficulty.NONE).IsValid());
            Assert.False(Input(town, GameSessionCodes.Mode.NORMAL, party, null, null, GameSessionCodes.Difficulty.HARD)
                .IsValid());
            Assert.True(Input(town, GameSessionCodes.Mode.BOSS, party, null, SessionTestEnvironment.BOSS_ID,
                GameSessionCodes.Difficulty.HARD).IsValid());
            Assert.True(Input(town, GameSessionCodes.Mode.NORMAL, party, BATTLE_ID, null,
                GameSessionCodes.Difficulty.NONE).IsValid());

            // 계약 필드가 없는 요청은 0012 전과 같은 해시 본문을 쓴다(저장된 같은 요청의 재전송이 충돌하지 않는다)
            ReservationInput legacy = new ReservationInput(town, Guid.NewGuid(), GameSessionCodes.Mode.NORMAL, "m", "c",
                party);
            Assert.DoesNotContain("battleId", legacy.ToRequestJson());
            Assert.Contains("battleId", Input(town, GameSessionCodes.Mode.NORMAL, party, BATTLE_ID, null,
                GameSessionCodes.Difficulty.NONE).ToRequestJson());
        }

        /// <summary> 예약이 battleId, bossId, 난이도를 세션 행에 저장한다 </summary>
        [PostgresFact]
        public async Task Reserve_ContractFields_AreStoredOnSession()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync(2);
            TestPlayer boss = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer normal = await environment.CreatePlayerAsync(TICKETS, POTIONS);

            ReservedSessionView bossSession = await environment.ReserveAsync(GameSessionCodes.Mode.BOSS, new[] { boss }, 0);
            ReservationOutcome normalOutcome = await environment.Reservations.ReserveAsync(Input(environment.TownId,
                GameSessionCodes.Mode.NORMAL, new[]
                {
                    new ParticipantReservationInput(normal.AccountId, normal.CharacterId, 0,
                        Array.Empty<PotionReservationInput>())
                }, BATTLE_ID, null, GameSessionCodes.Difficulty.NONE));

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, normalOutcome.Result.Status);
            Assert.Equal(1L, await environment.ScalarAsync(
                """
                SELECT count(*) FROM game_sessions WHERE session_id = @Id AND boss_id = @Boss AND difficulty = @Easy
                    AND battle_id IS NULL;
                """, new { Id = bossSession.SessionId, Boss = SessionTestEnvironment.BOSS_ID,
                    Easy = GameSessionCodes.Difficulty.EASY }));
            Assert.Equal(1L, await environment.ScalarAsync(
                """
                SELECT count(*) FROM game_sessions WHERE session_id = @Id AND battle_id = @Battle AND boss_id IS NULL
                    AND difficulty IS NULL;
                """, new { Id = normalOutcome.Session!.SessionId, Battle = BATTLE_ID }));
        }

        /// <summary> 이탈 이벤트는 처음 한 번만 기록하고 그 계정의 던전 임대만 푼다. 같은 참가자의 두 번째 이탈은 순번만 남긴다.
        /// 이탈한 참가자도 SESSION_ENDED 정산안으로 한 번 정산된다 </summary>
        [PostgresFact]
        public async Task Departed_RecordsOnceReleasesOnlyThatLeaseAndSettlesOnce()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer leaving = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            TestPlayer staying = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.NORMAL,
                new[] { leaving, staying }, 0);
            await environment.ExecuteAsync("UPDATE account_leases SET instance_id = @Dungeon, role = 1;",
                new { Dungeon = session.InstanceId });
            Guid leavingParticipant = session.Participants.Single(row => row.AccountId == leaving.AccountId).ParticipantId;
            Guid operationId = Guid.NewGuid();
            SessionEventInput[] first = { Departed(1, leavingParticipant, GameSessionCodes.EndReason.DISCONNECT_EXPIRED) };

            OperationResult departed = await environment.Sessions.AppendEventsAsync(session.InstanceId,
                session.SessionId, operationId, first);
            OperationResult replay = await environment.Sessions.AppendEventsAsync(session.InstanceId,
                session.SessionId, operationId, first);
            OperationResult again = await SessionEventTests.AppendAsync(environment, session,
                new[] { Departed(2, leavingParticipant, GameSessionCodes.EndReason.ABANDON) });

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, departed.Status);
            Assert.Equal(departed.ReceiptId, replay.ReceiptId);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, again.Status);
            Assert.Equal(GameSessionCodes.EndReason.DISCONNECT_EXPIRED, await environment.ScalarAsync(
                "SELECT depart_reason::bigint FROM session_participants WHERE participant_id = @Id;",
                new { Id = leavingParticipant }));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM account_leases WHERE account_id = @AccountId;", new { leaving.AccountId }));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT count(*) FROM account_leases WHERE account_id = @AccountId;", new { staying.AccountId }));
            Assert.Equal(2L, await environment.ScalarAsync(
                "SELECT count(*) FROM session_participants WHERE session_id = @Id AND state = 'Active';",
                new { Id = session.SessionId }));

            // 떠난 참가자도 정산안에 포함하고 정산은 한 번만 지급한다
            Guid settlementId = Guid.NewGuid();
            OperationResult ended = await SessionEventTests.AppendAsync(environment, session, new[]
            {
                SessionEventTests.Ended(3, settlementId, session.Participants
                    .Select(row => SessionEventTests.Grant(row.ParticipantId, ESSENCE_GRANT)))
            });
            OperationResult settled = await environment.Settlement.SettleAsync(session.InstanceId, session.SessionId,
                settlementId);
            OperationResult resettled = await environment.Settlement.SettleAsync(session.InstanceId, session.SessionId,
                settlementId);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, ended.Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, settled.Status);
            Assert.Equal(settled.ReceiptId, resettled.ReceiptId);
            Assert.Equal(ESSENCE_GRANT, (await environment.ReadWalletAsync(leaving.AccountId)).Essence);
            Assert.Equal(ESSENCE_GRANT, await environment.LedgerSumAsync(leaving.AccountId, "Essence"));
        }

        /// <summary> 0012 down은 저장된 이탈 이벤트(6)를 남긴 채 이전 범위 CHECK를 NOT VALID로 걸고 회수함 행을 지운다.
        /// 다시 up하면 기존 행과 함께 제약이 복원된다 </summary>
        [PostgresFact]
        public async Task Migration0012_DownThenUp_KeepsDepartedEventAndDropsRecoveryRows()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.NORMAL, new[] { player }, 0);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await SessionEventTests.AppendAsync(environment,
                session, new[] { Departed(1, session.Participants[0].ParticipantId, GameSessionCodes.EndReason.ABANDON) }))
                .Status);
            await using NpgsqlConnection connection = await environment.DataSource.OpenConnectionAsync();
            await using (NpgsqlTransaction transaction = await connection.BeginTransactionAsync())
            {
                await RecoveryInbox.InsertAsync(connection, transaction, RecoveryInbox.SOURCE_SESSION_REWARD,
                    session.SessionId.ToString("D"), new[]
                    {
                        new RecoveryLine(player.AccountId, player.CharacterId, EconomyAsset.SHARDS, null, null, 1)
                    }, CancellationToken.None);
                await transaction.CommitAsync();
            }
            (long version, string name) = MigrationTestResources.All.Single(entry => entry.Name == MIGRATION_0012);

            await MigrationTestResources.RollBackAboveAsync(connection, version - 1);

            Assert.Equal(1L, await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM session_events WHERE event_kind = @Kind;",
                new { Kind = GameSessionCodes.EventKind.PARTICIPANT_DEPARTED }));
            Assert.Equal(0L, await connection.QuerySingleAsync<long>("SELECT count(*) FROM reward_inbox;"));
            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM information_schema.columns
                WHERE table_schema = current_schema() AND column_name IN ('departed_at', 'deleted_at', 'boss_id', 'acquired_at');
                """));

            await connection.ExecuteAsync(await MigrationTestResources.ReadUpAsync(name));

            Assert.Equal(1L, await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM session_participants WHERE departed_at IS NULL AND session_id = @Id;",
                new { Id = session.SessionId }));
            Assert.Equal(1L, await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM characters WHERE deleted_at IS NULL AND character_id = @Id;",
                new { Id = player.CharacterId }));
        }

        /// <summary> 이탈 이유가 포기, 접속 만료가 아니거나 참가자가 없으면 배치 형식 오류다 </summary>
        [Fact]
        public void Validator_DepartedNeedsParticipantAndDepartReason()
        {
            Guid participant = Guid.NewGuid();

            Assert.True(SessionEventValidator.IsValid(new[]
            {
                Departed(1, participant, GameSessionCodes.EndReason.ABANDON)
            }));
            Assert.False(SessionEventValidator.IsValid(new[]
            {
                Departed(1, participant, GameSessionCodes.EndReason.SUCCESS)
            }));
            SessionEventInput anonymous = Departed(1, participant, GameSessionCodes.EndReason.ABANDON);
            anonymous.ParticipantId = null;
            Assert.False(SessionEventValidator.IsValid(new[] { anonymous }));
        }

        private static SessionEventInput Departed(long seq, Guid participantId, int reason)
        {
            return new SessionEventInput
            {
                EventSeq = seq,
                EventId = Guid.NewGuid(),
                EventKind = GameSessionCodes.EventKind.PARTICIPANT_DEPARTED,
                ParticipantId = participantId,
                EndReason = reason
            };
        }

        private static ReservationInput Input(Guid town, int mode, ParticipantReservationInput[] party, string? battleId,
            string? bossId, int difficulty)
        {
            return new ReservationInput(town, Guid.NewGuid(), mode, SessionTestEnvironment.MAP_ID,
                SessionTestEnvironment.CONTENT_VERSION, party, battleId, bossId, difficulty);
        }
    }
}
