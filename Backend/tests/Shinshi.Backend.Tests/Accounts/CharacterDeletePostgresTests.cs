// System
using System;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Newtonsoft.Json;
using Npgsql;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Characters;

namespace Shinshi.Backend.Tests.Accounts
{
    /// <summary> 실제 PostgreSQL에서 캐릭터 소프트 삭제의 멱등, 거절 무효과, 목록과 슬롯, 동시 삭제와 생성을 검사한다 </summary>
    public sealed class CharacterDeletePostgresTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int  CONCURRENT_CALLS  = 4;
        // 삭제는 캐릭터 revision을 1 올린다. 생성 직후 revision은 초기 지급(0011)의 잔액 변경만큼 이미 올라 있어 생성 결과에서 읽는다
        private const long DELETE_REVISION_STEP = 1;
        private const int  HEARTBEAT_PORT    = 7777;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 같은 operationId 동시, 반복 삭제가 한 번만 적용되고 목록에서 빠지며 이름은 반환되지 않는다 </summary>
        [PostgresFact]
        public async Task Delete_SameOperationConcurrently_AppliesOnceAndKeepsName()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            Guid accountId = await AddAccountAsync(database);
            (Guid characterId, long initialRevision) = await CreateAsync(repository, accountId, "Deleted");
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await repository.RequestDeleteAsync(accountId, Guid.NewGuid(), characterId, initialRevision)).Status);
            await CharacterDeleteTestSupport.BackdateAsync(database.DataSource, characterId);
            initialRevision += DELETE_REVISION_STEP;
            long deletedRevision = initialRevision + DELETE_REVISION_STEP;
            Guid operationId = Guid.NewGuid();

            OperationResult[] results = await Task.WhenAll(Enumerable.Range(0, CONCURRENT_CALLS).Select(_ =>
                repository.DeleteAsync(accountId, operationId, characterId, initialRevision)));
            OperationResult replay = await repository.DeleteAsync(accountId, operationId, characterId, initialRevision);
            OperationResult conflict = await repository.DeleteAsync(accountId, operationId, characterId,
                deletedRevision);
            OperationResult again = await repository.DeleteAsync(accountId, Guid.NewGuid(), characterId,
                deletedRevision);

            Assert.All(results, result =>
            {
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED, result.Status);
                Assert.Equal(characterId.ToString("D"), result.ReceiptId);
                Assert.Equal(deletedRevision, result.CommittedRevision);
            });
            Assert.Equal(results[0].ServerTimeUnixMs, replay.ServerTimeUnixMs);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, conflict.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.CHARACTER_NOT_FOUND, again.ReasonCode);
            Assert.Empty(await repository.ListAsync(accountId));
            Assert.True(await repository.IsNameTakenAsync("deleted"));
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            Assert.Equal(1L, await connection.QuerySingleAsync<long>(
                """
                SELECT count(*) FROM characters
                WHERE character_id = @CharacterId AND deleted_at IS NOT NULL AND delete_operation_id = @OperationId
                    AND revision = @Revision;
                """, new { CharacterId = characterId, OperationId = operationId, Revision = deletedRevision }));
        }

        /// <summary> 접속 중, 열린 출정 참가, revision 불일치, 남의 캐릭터는 거절되고 캐릭터와 토큰이 그대로 남는다 </summary>
        [PostgresFact]
        public async Task Delete_ConnectedSortieStaleOrForeign_RejectsWithoutEffect()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            Guid accountId = await AddAccountAsync(database);
            Guid otherAccount = await AddAccountAsync(database);
            (Guid characterId, long initialRevision) = await CreateAsync(repository, accountId, "Guarded");
            long deletedRevision = initialRevision + DELETE_REVISION_STEP;
            Guid instanceId = Guid.NewGuid();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO server_instances (instance_id, role, address, port, build_id, content_version, state, boot_id)
                VALUES (@InstanceId, 0, '127.0.0.1', @Port, 'b', 'c', 'Idle', gen_random_uuid());
                INSERT INTO account_leases (account_id, lease_id, instance_id, role, expires_at)
                VALUES (@AccountId, gen_random_uuid(), @InstanceId, 0, clock_timestamp() + interval '1 minute');
                """, new { InstanceId = instanceId, AccountId = accountId, Port = HEARTBEAT_PORT });

            OperationResult connected = await repository.DeleteAsync(accountId, Guid.NewGuid(), characterId,
                initialRevision);

            // heartbeat가 끊긴 서버의 남은 임대는 막지 않으므로 출정 검사로 넘어간다
            await connection.ExecuteAsync(
                "UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '1 minute' WHERE instance_id = @Id;",
                new { Id = instanceId });
            await connection.ExecuteAsync(
                """
                INSERT INTO server_instances (instance_id, role, address, port, build_id, content_version, state, boot_id)
                VALUES (@Dungeon, 1, '127.0.0.1', @Port, 'b', 'c', 'Reserved', gen_random_uuid());
                INSERT INTO game_sessions (session_id, mode, map_id, state, starting_count, content_version, instance_id,
                    server_lease, expires_at)
                VALUES (@Session, 'Normal', 'map', 'Reserved', 1, 'c', @Dungeon, gen_random_uuid(),
                    clock_timestamp() + interval '1 minute');
                INSERT INTO session_participants (session_id, participant_id, account_id, character_id, state)
                VALUES (@Session, gen_random_uuid(), @AccountId, @CharacterId, 'Reserved');
                """, new { Dungeon = Guid.NewGuid(), Session = Guid.NewGuid(), AccountId = accountId,
                    CharacterId = characterId, Port = HEARTBEAT_PORT });
            OperationResult sortie = await repository.DeleteAsync(accountId, Guid.NewGuid(), characterId,
                initialRevision);

            await connection.ExecuteAsync(
                "UPDATE session_participants SET state = 'Released', settled_at = clock_timestamp();");
            OperationResult stale = await repository.DeleteAsync(accountId, Guid.NewGuid(), characterId,
                deletedRevision);
            OperationResult foreign = await repository.DeleteAsync(otherAccount, Guid.NewGuid(), characterId,
                initialRevision);

            Assert.Equal(ContractCodes.RejectReason.ALREADY_CONNECTED, connected.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.RESERVATION_CONFLICT, sortie.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.STALE_REVISION, stale.ReasonCode);
            Assert.Equal(initialRevision, stale.CommittedRevision);
            Assert.Equal(ContractCodes.RejectReason.CHARACTER_NOT_FOUND, foreign.ReasonCode);
            Assert.Single(await repository.ListAsync(accountId));
            Assert.Equal(0L, await connection.QuerySingleAsync<long>(
                "SELECT count(*) FROM characters WHERE deleted_at IS NOT NULL;"));
        }

        /// <summary> 슬롯이 가득 찬 계정에서 삭제와 생성이 동시에 와도 살아 있는 캐릭터는 상한을 넘지 않고, 삭제가 먼저면 생성이 슬롯을 받는다 </summary>
        [PostgresFact]
        public async Task DeleteAndCreate_Concurrent_KeepsLiveSlotLimit()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            Guid accountId = await AddAccountAsync(database);
            (Guid Id, long Revision)[] characters =
                new (Guid, long)[PostgresCharacterRepository.MAX_CHARACTERS_PER_ACCOUNT];
            for (int i = 0; i < characters.Length; ++i)
            {
                characters[i] = await CreateAsync(repository, accountId, "slot" + i);
            }

            await repository.RequestDeleteAsync(accountId, Guid.NewGuid(), characters[0].Id, characters[0].Revision);
            await CharacterDeleteTestSupport.BackdateAsync(database.DataSource, characters[0].Id);
            Task<OperationResult> deleting = repository.DeleteAsync(accountId, Guid.NewGuid(), characters[0].Id,
                characters[0].Revision + DELETE_REVISION_STEP);
            Task<CharacterCreateOutcome> creating = CreateOutcomeAsync(repository, accountId, Guid.NewGuid(), "slotnew");
            await Task.WhenAll(deleting, creating);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, deleting.Result.Status);
            CharacterCreateOutcome created = creating.Result;
            Assert.True((created.Result.Status == ContractCodes.OperationStatus.COMMITTED)
                || (created.Result.ReasonCode == ContractCodes.RejectReason.NO_CAPACITY));
            Assert.True((await repository.ListAsync(accountId)).Count <= PostgresCharacterRepository.MAX_CHARACTERS_PER_ACCOUNT);

            // 삭제가 끝난 뒤의 생성은 비워진 슬롯을 받는다
            CharacterCreateOutcome after = await CreateOutcomeAsync(repository, accountId, Guid.NewGuid(), "slotafter");
            int live = (await repository.ListAsync(accountId)).Count;
            Assert.Equal(PostgresCharacterRepository.MAX_CHARACTERS_PER_ACCOUNT, live);
            Assert.True((created.Result.Status == ContractCodes.OperationStatus.COMMITTED)
                != (after.Result.Status == ContractCodes.OperationStatus.COMMITTED));
        }

        /// <summary> 계정 행 잠금 뒤 최근 24시간 삭제 수가 상한이면 RATE_LIMITED로 거절하고 캐릭터는 그대로다.
        /// 24시간이 지난 삭제는 세지 않는다 </summary>
        [PostgresFact]
        public async Task Delete_OverDailyLimit_RejectsRateLimitedUntilWindowPasses()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            Guid accountId = await AddAccountAsync(database);
            string[] names = { "First", "Second", "Third", "Fourth" };
            for (int i = 0; i < PostgresCharacterRepository.MAX_DELETES_PER_WINDOW; ++i)
            {
                (Guid deletedId, long revision) = await CreateAsync(repository, accountId, names[i]);
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                    (await CharacterDeleteTestSupport.DeleteAfterGraceAsync(database.DataSource, repository, accountId,
                        deletedId, revision)).Status);
            }

            (Guid characterId, long current) = await CreateAsync(repository, accountId, names[^1]);
            await repository.RequestDeleteAsync(accountId, Guid.NewGuid(), characterId, current);
            await CharacterDeleteTestSupport.BackdateAsync(database.DataSource, characterId);
            current += DELETE_REVISION_STEP;
            OperationResult limited = await repository.DeleteAsync(accountId, Guid.NewGuid(), characterId, current);
            Assert.Equal(ContractCodes.RejectReason.RATE_LIMITED, limited.ReasonCode);
            Assert.Single(await repository.ListAsync(accountId));

            await using (NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync())
            {
                await connection.ExecuteAsync(
                    """
                    UPDATE characters SET deleted_at = deleted_at - make_interval(hours => @Hours)
                    WHERE account_id = @AccountId AND deleted_at IS NOT NULL;
                    """, new { AccountId = accountId, Hours = PostgresCharacterRepository.DELETE_WINDOW_HOURS + 1 });
            }

            OperationResult allowed = await repository.DeleteAsync(accountId, Guid.NewGuid(), characterId, current);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, allowed.Status);
            Assert.Empty(await repository.ListAsync(accountId));
        }

        /// <summary> 예약 없는 삭제는 CHARACTER_DELETE_NOT_REQUESTED, 유예 중 삭제는 CHARACTER_DELETE_PENDING으로 거절되고 캐릭터는 그대로다 </summary>
        [PostgresFact]
        public async Task Delete_WithoutRequestOrWithinGrace_RejectsWithoutEffect()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            Guid accountId = await AddAccountAsync(database);
            (Guid characterId, long revision) = await CreateAsync(repository, accountId, "Grace");

            OperationResult notRequested = await repository.DeleteAsync(accountId, Guid.NewGuid(), characterId, revision);
            OperationResult requested = await repository.RequestDeleteAsync(accountId, Guid.NewGuid(), characterId, revision);
            OperationResult pending = await repository.DeleteAsync(accountId, Guid.NewGuid(), characterId,
                requested.CommittedRevision);

            Assert.Equal(ContractCodes.RejectReason.CHARACTER_DELETE_NOT_REQUESTED, notRequested.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, requested.Status);
            Assert.Equal(revision + DELETE_REVISION_STEP, requested.CommittedRevision);
            Assert.Equal(ContractCodes.RejectReason.CHARACTER_DELETE_PENDING, pending.ReasonCode);
            Assert.Single(await repository.ListAsync(accountId));
        }

        /// <summary> 예약은 같은 작업 재전송과 새 작업 모두 시각과 revision을 바꾸지 않고, 다른 revision은 STALE_REVISION이다 </summary>
        [PostgresFact]
        public async Task RequestDelete_Repeated_KeepsTimestampAndRevision()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            Guid accountId = await AddAccountAsync(database);
            (Guid characterId, long revision) = await CreateAsync(repository, accountId, "Repeat");
            Guid operationId = Guid.NewGuid();

            OperationResult first = await repository.RequestDeleteAsync(accountId, operationId, characterId, revision);
            DateTime? requestedAt = await ReadRequestedAtAsync(database, characterId);
            OperationResult replay = await repository.RequestDeleteAsync(accountId, operationId, characterId, revision);
            OperationResult again = await repository.RequestDeleteAsync(accountId, Guid.NewGuid(), characterId, revision);
            OperationResult conflict = await repository.RequestDeleteAsync(accountId, operationId, characterId,
                revision + DELETE_REVISION_STEP);
            (Guid otherId, long otherRevision) = await CreateAsync(repository, accountId, "Stale");
            OperationResult stale = await repository.RequestDeleteAsync(accountId, Guid.NewGuid(), otherId,
                otherRevision + DELETE_REVISION_STEP);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Status);
            Assert.Equal(first.ServerTimeUnixMs, replay.ServerTimeUnixMs);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, again.Status);
            Assert.Equal(first.CommittedRevision, again.CommittedRevision);
            Assert.Equal(ContractCodes.RejectReason.IDEMPOTENCY_CONFLICT, conflict.ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.STALE_REVISION, stale.ReasonCode);
            Assert.Equal(requestedAt, await ReadRequestedAtAsync(database, characterId));
        }

        /// <summary> 취소는 예약을 지우고 revision을 올리며, 취소 뒤 삭제는 NOT_REQUESTED, 예약 없는 취소도 NOT_REQUESTED다 </summary>
        [PostgresFact]
        public async Task CancelDelete_ClearsRequestAndBlocksFinalDelete()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            Guid accountId = await AddAccountAsync(database);
            (Guid characterId, long revision) = await CreateAsync(repository, accountId, "Cancel");

            OperationResult early = await repository.CancelDeleteAsync(accountId, Guid.NewGuid(), characterId, revision);
            OperationResult requested = await repository.RequestDeleteAsync(accountId, Guid.NewGuid(), characterId, revision);
            await CharacterDeleteTestSupport.BackdateAsync(database.DataSource, characterId);
            OperationResult cancelled = await repository.CancelDeleteAsync(accountId, Guid.NewGuid(), characterId,
                requested.CommittedRevision);
            OperationResult deleted = await repository.DeleteAsync(accountId, Guid.NewGuid(), characterId,
                cancelled.CommittedRevision);

            Assert.Equal(ContractCodes.RejectReason.CHARACTER_DELETE_NOT_REQUESTED, early.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, cancelled.Status);
            Assert.Equal(requested.CommittedRevision + DELETE_REVISION_STEP, cancelled.CommittedRevision);
            Assert.Null(await ReadRequestedAtAsync(database, characterId));
            Assert.Equal(ContractCodes.RejectReason.CHARACTER_DELETE_NOT_REQUESTED, deleted.ReasonCode);
            Assert.Single(await repository.ListAsync(accountId));
        }

        /// <summary> 목록은 예약 여부와 DB 시각 기준 남은 초를 돌려준다. 예약이 없으면 false와 0이다 </summary>
        [PostgresFact]
        public async Task List_ShowsDeletePendingAndRemainingSeconds()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            PostgresCharacterRepository repository = CreateRepository(database);
            Guid accountId = await AddAccountAsync(database);
            (Guid characterId, long revision) = await CreateAsync(repository, accountId, "Listed");

            CharacterRecord before = Assert.Single(await repository.ListAsync(accountId));
            await repository.RequestDeleteAsync(accountId, Guid.NewGuid(), characterId, revision);
            CharacterRecord pending = Assert.Single(await repository.ListAsync(accountId));
            await CharacterDeleteTestSupport.BackdateAsync(database.DataSource, characterId);
            CharacterRecord elapsed = Assert.Single(await repository.ListAsync(accountId));

            long graceSeconds = PostgresCharacterRepository.DELETE_GRACE_MINUTES * 60L;
            Assert.False(before.DeletePending);
            Assert.Equal(0L, before.DeleteRemainingSeconds);
            Assert.True(pending.DeletePending);
            Assert.InRange(pending.DeleteRemainingSeconds, graceSeconds - 30, graceSeconds);
            Assert.True(elapsed.DeletePending);
            Assert.Equal(0L, elapsed.DeleteRemainingSeconds);
        }

        private static async Task<DateTime?> ReadRequestedAtAsync(PostgresTestDatabase database, Guid characterId)
        {
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            return await connection.QuerySingleAsync<DateTime?>(
                "SELECT delete_requested_at FROM characters WHERE character_id = @CharacterId;",
                new { CharacterId = characterId });
        }

        private static async Task<(Guid CharacterId, long Revision)> CreateAsync(PostgresCharacterRepository repository,
            Guid accountId, string name)
        {
            CharacterCreateOutcome created = await CreateOutcomeAsync(repository, accountId, Guid.NewGuid(), name);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, created.Result.Status);
            return (created.Character!.CharacterId, created.Character.Revision);
        }

        private static Task<CharacterCreateOutcome> CreateOutcomeAsync(PostgresCharacterRepository repository,
            Guid accountId, Guid operationId, string rawName)
        {
            Assert.True(CharacterNameRules.TryNormalize(rawName, out string name, out string nameKey));
            CharacterCreateRequest request = new CharacterCreateRequest(operationId.ToString("D"), rawName,
                ContractCodes.PlayerClass.WARRIOR, CharacterTestData.Appearance());
            string requestJson = JsonConvert.SerializeObject(request, ContractJson.CreateSettings());
            string appearanceJson = JsonConvert.SerializeObject(request.Appearance, ContractJson.CreateSettings());
            return repository.CreateAsync(accountId, operationId, name, nameKey, request.ClassId, appearanceJson,
                requestJson);
        }

        private static PostgresCharacterRepository CreateRepository(PostgresTestDatabase database)
        {
            return new PostgresCharacterRepository(database.DataSource,
                new IdempotentOperationExecutor(new PostgresOperationStore(database.DataSource)));
        }

        private static async Task<Guid> AddAccountAsync(PostgresTestDatabase database)
        {
            Guid accountId = Guid.NewGuid();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("INSERT INTO accounts(account_id) VALUES (@AccountId);",
                new { AccountId = accountId });
            return accountId;
        }
    }
}
