// System
using System;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Tests.Sessions;
using Shinshi.Backend.Tests.Auth;
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Growth;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Experience
{
    /// <summary> 실제 이벤트 트랜잭션과 경험치 원장의 중복 방지, 상한, 이탈 순서를 검사한다 </summary>
    public sealed class ExperiencePostgresTests
    {
        private static readonly GrowthContent CONTENT = GrowthContent.LoadEmbedded();
        private static CharacterLevelSettings Settings => CONTENT.CharacterLevel;
        private static string Map => Settings.Regions[0].RegionId;

        [PostgresFact]
        public async Task Start_UnknownMapVersionOrChangedSettings_RejectsAndLeavesReservationUnstarted()
        {
            foreach (string invalid in new[] { "map", "version", "settings" })
            {
                await using SessionTestEnvironment env = await SessionTestEnvironment.CreateAsync();
                ReservedSessionView session = await StartAsync(env, start: false);
                if (invalid == "map")
                {
                    await env.ExecuteAsync("UPDATE game_sessions SET map_id = 'unknown';");
                }
                if (invalid == "version")
                {
                    await env.ExecuteAsync("UPDATE game_sessions SET content_version = 'outdated';");
                }
                if (invalid == "settings")
                {
                    await env.ExecuteAsync("UPDATE game_sessions SET experience_settings = jsonb_set(experience_settings, '{firstLevelCost}', '301');");
                }
                OperationResult result = await env.Sessions.StartAsync(session.InstanceId, session.SessionId,
                    Guid.NewGuid(), session.Participants.Select(participant => participant.ParticipantId).ToArray());
                Assert.Equal(ContractCodes.OperationStatus.REJECTED, result.Status);
                Assert.Equal("Reserved", await env.TextAsync("SELECT state FROM game_sessions;"));
                await AssertBalanceAsync(env, 0, 0, 0);
            }
        }

        [PostgresFact]
        public async Task Rejoin_AfterCommitAndConcurrentAppend_ReturnsConsistentExperienceCursor()
        {
            await using SessionTestEnvironment env = await SessionTestEnvironment.CreateAsync();
            ReservedSessionView session = await StartAsync(env);
            PostgresAuthRepository auth = new PostgresAuthRepository(env.DataSource, AuthTokenHasherTests.CreateOptions());
            Guid account;
            await using (NpgsqlConnection connection = await env.DataSource.OpenConnectionAsync())
            {
                account = await connection.QuerySingleAsync<Guid>("SELECT account_id FROM characters;");
            }
            IssuedAuthToken invite = await auth.IssueInviteAsync(account);
            AuthExchangeResponse login = (await auth.ExchangeInviteAsync(invite.Token)).Response!;
            PlayerAuthIdentity identity = (await auth.ValidateAccessAsync(login.AccessToken)).Identity!;
            IssuedAuthToken lease = await auth.IssueServerLeaseAsync(Guid.NewGuid(), session.InstanceId,
                ContractCodes.ServerRole.DUNGEON, TimeSpan.FromMinutes(5));
            ServerAuthIdentity server = (await auth.ValidateServerLeaseAsync(lease.Token)).Identity!;
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await AppendAsync(env, session, Experience(1, session, "first"))).Status);
            JoinTicket ticket = Assert.IsType<JoinTicket>((await auth.IssueRejoinTicketAsync(identity, session.SessionId)).Identity);
            JoinAuthIdentity joined = Assert.IsType<JoinAuthIdentity>((await auth.ConsumeJoinTokenAsync(ticket.JoinToken,
                session.InstanceId, ContractCodes.ServerRole.DUNGEON, server.LeaseId)).Identity);
            long grant = Settings.GetGrant(Map, CharacterExperienceSource.NORMAL_ENEMY);
            Assert.Equal(grant, joined.Experience!.TotalExperience);
            Assert.Equal(1, joined.Experience.LastExperienceEventSeq);
            Assert.Equal(session.SessionId.ToString("D"), joined.Experience.SessionId);
            Assert.Equal(CharacterLevelRules.SettingsHash(Settings), joined.Experience.SettingsHash);

            // Join과 Append가 같은 계정에서 경합해도 누계와 cursor가 서로 다른 커밋을 보지 않는다.
            for (int seq = 2; seq <= 5; ++seq)
            {
                JoinTicket next = Assert.IsType<JoinTicket>((await auth.IssueRejoinTicketAsync(identity, session.SessionId)).Identity);
                Task<AuthValidationOutcome<JoinAuthIdentity>> join = auth.ConsumeJoinTokenAsync(next.JoinToken,
                    session.InstanceId, ContractCodes.ServerRole.DUNGEON, server.LeaseId);
                Task<OperationResult> append = AppendAsync(env, session, Experience(seq, session, "race-" + seq));
                await Task.WhenAll(join, append);
                Assert.Equal(ContractCodes.OperationStatus.COMMITTED, append.Result.Status);
                CharacterExperienceSnapshot snapshot = Assert.IsType<JoinAuthIdentity>(join.Result.Identity).Experience!;
                Assert.InRange(snapshot.LastExperienceEventSeq, seq - 1, seq);
                Assert.Equal(snapshot.LastExperienceEventSeq * grant, snapshot.TotalExperience);
            }
            Assert.Equal(0, env.Store.Deadlocks);
        }

        [PostgresFact]
        public async Task Append_ReplayAndNewOperationWithSameSource_CreditsExactlyOnce()
        {
            await using SessionTestEnvironment env = await SessionTestEnvironment.CreateAsync();
            ReservedSessionView session = await StartAsync(env);
            SessionEventInput entry = Experience(1, session, "enemy-1");
            Guid operation = Guid.NewGuid();
            OperationResult first = await env.Sessions.AppendEventsAsync(session.InstanceId, session.SessionId, operation, new[] { entry });
            OperationResult replay = await env.Sessions.AppendEventsAsync(session.InstanceId, session.SessionId, operation, new[] { entry });
            OperationResult duplicate = await AppendAsync(env, session, Experience(2, session, "enemy-1"));
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Status);
            Assert.Equal(first.ReceiptId, replay.ReceiptId);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, duplicate.Status);
            await AssertBalanceAsync(env, entry.Amount, 1, 1);
        }

        [PostgresFact]
        public async Task Append_EnemyReclassifiedAsElite_CannotCreditSameSourceAgain()
        {
            await using SessionTestEnvironment env = await SessionTestEnvironment.CreateAsync();
            ReservedSessionView session = await StartAsync(env);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await AppendAsync(env, session, Experience(1, session, "same"))).Status);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED,
                (await AppendAsync(env, session, Experience(2, session, "same", CharacterExperienceSource.ELITE_ENEMY))).Status);
            await AssertBalanceAsync(env, Settings.GetGrant(Map, CharacterExperienceSource.NORMAL_ENEMY), 1, 1);
        }

        [PostgresFact]
        public async Task Append_DuplicateWithinBatch_RollsBackEarlierCredit()
        {
            await using SessionTestEnvironment env = await SessionTestEnvironment.CreateAsync();
            ReservedSessionView session = await StartAsync(env);
            OperationResult result = await AppendAsync(env, session, Experience(1, session, "same"),
                Experience(2, session, "same", CharacterExperienceSource.ELITE_ENEMY));
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, result.Status);
            await AssertBalanceAsync(env, 0, 0, 0);
        }

        [PostgresFact]
        public async Task Append_StoredExperienceAboveReservedCap_RejectsWithoutChangingBalanceOrEvents()
        {
            await using SessionTestEnvironment env = await SessionTestEnvironment.CreateAsync();
            ReservedSessionView session = await StartAsync(env);
            long invalidTotal = CharacterLevelRules.RequiredExperience(Settings, Settings.MaxLevel) + 1;
            // 운영 계약 밖의 기존 값도 새 지급으로 조용히 삭감하지 않도록 변조 상태를 재현한다.
            await env.ExecuteAsync("UPDATE characters SET total_experience = @Total;", new { Total = invalidTotal });

            OperationResult result = await AppendAsync(env, session, Experience(1, session, "above-cap"));

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, result.Status);
            await AssertBalanceAsync(env, invalidTotal, 0, 0);
            Assert.Equal(0, await env.ScalarAsync("SELECT last_experience_event_seq FROM characters;"));
            Assert.Equal(0, await env.ScalarAsync("SELECT count(*) FROM characters WHERE last_experience_session_id IS NOT NULL;"));
        }

        [PostgresFact]
        public async Task Append_AtCap_RecordsRequestedAndAppliedAmountsAndConsumesZeroCreditSource()
        {
            await using SessionTestEnvironment env = await SessionTestEnvironment.CreateAsync();
            ReservedSessionView session = await StartAsync(env);
            long cap = CharacterLevelRules.RequiredExperience(Settings, Settings.MaxLevel);
            await env.ExecuteAsync("UPDATE characters SET initial_experience = @Initial, total_experience = @Initial;", new { Initial = cap - 2 });
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await AppendAsync(env, session,
                Experience(1, session, "last"), Experience(2, session, "capped"))).Status);
            await AssertBalanceAsync(env, cap, 2, 2);
            Assert.Equal(2, await env.ScalarAsync("SELECT amount FROM character_experience_events WHERE event_seq = 1;"));
            Assert.Equal(0, await env.ScalarAsync("SELECT amount FROM character_experience_events WHERE event_seq = 2;"));
            Assert.Equal(Settings.GetGrant(Map, CharacterExperienceSource.NORMAL_ENEMY),
                await env.ScalarAsync("SELECT requested_amount FROM character_experience_events WHERE event_seq = 2;"));
            Assert.Equal(ContractCodes.OperationStatus.REJECTED,
                (await AppendAsync(env, session, Experience(3, session, "capped"))).Status);
            Assert.Equal(0, await env.ScalarAsync("SELECT count(*) FROM characters WHERE total_experience <> initial_experience + (SELECT sum(amount) FROM character_experience_events);"));
        }

        [PostgresFact]
        public async Task Append_DepartureBeforeExperience_RollsBackWholeBatch()
        {
            await using SessionTestEnvironment env = await SessionTestEnvironment.CreateAsync();
            ReservedSessionView session = await StartAsync(env);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED, (await AppendAsync(env, session,
                Departed(1, session), Experience(2, session, "after"))).Status);
            await AssertBalanceAsync(env, 0, 0, 0);
            Assert.Equal(0, await env.ScalarAsync("SELECT count(*) FROM session_participants WHERE departed_at IS NOT NULL;"));
        }

        [PostgresFact]
        public async Task Append_ExperienceBeforeDeparture_PreservesCreditAndRejectsLaterGrant()
        {
            await using SessionTestEnvironment env = await SessionTestEnvironment.CreateAsync();
            ReservedSessionView session = await StartAsync(env);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await AppendAsync(env, session,
                Experience(1, session, "before"), Departed(2, session))).Status);
            Assert.Equal(ContractCodes.OperationStatus.REJECTED,
                (await AppendAsync(env, session, Experience(3, session, "after"))).Status);
            await AssertBalanceAsync(env, Settings.GetGrant(Map, CharacterExperienceSource.NORMAL_ENEMY), 1, 2);
        }

        [PostgresFact]
        public async Task Append_InvalidHashAmountMapAndBoss_RejectWithoutPartialWrites()
        {
            foreach (string invalid in new[] { "hash", "amount", "map", "boss", "legacy" })
            {
                await using SessionTestEnvironment env = await SessionTestEnvironment.CreateAsync();
                ReservedSessionView session = await StartAsync(env, invalid == "boss" ? GameSessionCodes.Mode.BOSS : GameSessionCodes.Mode.NORMAL);
                SessionEventInput entry = Experience(1, session, "invalid");
                if (invalid == "hash") { entry.ExperienceSettingsHash = new string('f', 64); }
                if (invalid == "amount") { ++entry.Amount; }
                if (invalid == "map") { await env.ExecuteAsync("UPDATE game_sessions SET map_id = 'unknown';"); }
                if (invalid == "legacy") { await env.ExecuteAsync("UPDATE game_sessions SET experience_settings = NULL;"); }
                Assert.Equal(ContractCodes.OperationStatus.REJECTED, (await AppendAsync(env, session, entry)).Status);
                await AssertBalanceAsync(env, 0, 0, 0);
            }
        }

        [PostgresFact]
        public async Task Append_LostServerCancellation_PreservesCommittedExperience()
        {
            await using SessionTestEnvironment env = await SessionTestEnvironment.CreateAsync();
            ReservedSessionView session = await StartAsync(env);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await AppendAsync(env, session, Experience(1, session, "kill"))).Status);
            await env.ExecuteAsync("UPDATE server_instances SET heartbeat_at = clock_timestamp() - interval '2 minutes' WHERE instance_id = @Id;", new { Id = session.InstanceId });
            var recovered = await env.Settlement.RecoverLostSessionsAsync(8);
            Assert.Equal(1, recovered.Cancelled);
            Assert.Empty(recovered.Failures);
            await AssertBalanceAsync(env, Settings.GetGrant(Map, CharacterExperienceSource.NORMAL_ENEMY), 1, 1);
        }

        [PostgresFact]
        public async Task Append_EndAndSettle_PreservesExperienceWithoutSecondGrant()
        {
            await using SessionTestEnvironment env = await SessionTestEnvironment.CreateAsync();
            ReservedSessionView session = await StartAsync(env);
            Guid settlement = Guid.NewGuid();
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await AppendAsync(env, session,
                Experience(1, session, "kill"), SessionEventTests.Ended(2, settlement,
                    new[] { SessionEventTests.Grant(session.Participants[0].ParticipantId, 0) }))).Status);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED,
                (await env.Settlement.SettleAsync(session.InstanceId, session.SessionId, settlement)).Status);
            await AssertBalanceAsync(env, Settings.GetGrant(Map, CharacterExperienceSource.NORMAL_ENEMY), 1, 2);
        }

        [PostgresFact]
        public async Task ExperienceLedger_UpdateAndDelete_AreRejected()
        {
            await using SessionTestEnvironment env = await SessionTestEnvironment.CreateAsync();
            ReservedSessionView session = await StartAsync(env);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await AppendAsync(env, session, Experience(1, session, "kill"))).Status);
            await Assert.ThrowsAsync<PostgresException>(() => env.ExecuteAsync("DELETE FROM character_experience_events;"));
            await Assert.ThrowsAsync<PostgresException>(() => env.ExecuteAsync("UPDATE character_experience_events SET source_id = 'changed';"));
        }

        private static async Task<ReservedSessionView> StartAsync(SessionTestEnvironment env,
            int mode = GameSessionCodes.Mode.NORMAL, bool start = true)
        {
            TestPlayer player = await env.CreatePlayerAsync(2, 5);
            await env.ExecuteAsync("UPDATE server_instances SET content_version = @Version;", new { Version = CONTENT.ContentVersion });
            ReservationInput request = new ReservationInput(env.TownId, Guid.NewGuid(), mode, Map, CONTENT.ContentVersion,
                new[] { new ParticipantReservationInput(player.AccountId, player.CharacterId, 0, Array.Empty<PotionReservationInput>()) },
                null, mode == GameSessionCodes.Mode.BOSS ? SessionTestEnvironment.BOSS_ID : null,
                mode == GameSessionCodes.Mode.BOSS ? GameSessionCodes.Difficulty.EASY : GameSessionCodes.Difficulty.NONE);
            ReservationOutcome reserved = await env.Reservations.ReserveAsync(request);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, reserved.Result.Status);
            ReservedSessionView session = Assert.IsType<ReservedSessionView>(reserved.Session);
            if (!start)
            {
                return session;
            }
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, (await env.Sessions.StartAsync(session.InstanceId,
                session.SessionId, Guid.NewGuid(), session.Participants.Select(participant => participant.ParticipantId).ToArray())).Status);
            return session;
        }

        private static SessionEventInput Experience(long seq, ReservedSessionView session, string source,
            int kind = CharacterExperienceSource.NORMAL_ENEMY)
        {
            return new SessionEventInput
            {
                EventSeq = seq, EventId = Guid.NewGuid(), EventKind = GameSessionCodes.EventKind.EXPERIENCE_GRANTED,
                ParticipantId = session.Participants[0].ParticipantId, Amount = Settings.GetGrant(Map, kind),
                ExperienceSourceKind = kind, ExperienceSourceId = source,
                ExperienceSettingsHash = CharacterLevelRules.SettingsHash(Settings)
            };
        }

        private static SessionEventInput Departed(long seq, ReservedSessionView session)
        {
            return new SessionEventInput
            {
                EventSeq = seq, EventId = Guid.NewGuid(), EventKind = GameSessionCodes.EventKind.PARTICIPANT_DEPARTED,
                ParticipantId = session.Participants[0].ParticipantId, EndReason = GameSessionCodes.EndReason.ABANDON
            };
        }

        private static Task<OperationResult> AppendAsync(SessionTestEnvironment env, ReservedSessionView session,
            params SessionEventInput[] entries)
        {
            return env.Sessions.AppendEventsAsync(session.InstanceId, session.SessionId, Guid.NewGuid(), entries);
        }

        private static async Task AssertBalanceAsync(SessionTestEnvironment env, long total, long ledgerCount, long eventCount)
        {
            Assert.Equal(total, await env.ScalarAsync("SELECT total_experience FROM characters;"));
            Assert.Equal(ledgerCount, await env.ScalarAsync("SELECT count(*) FROM character_experience_events;"));
            Assert.Equal(eventCount, await env.ScalarAsync("SELECT count(*) FROM session_events;"));
            Assert.Equal(eventCount, await env.ScalarAsync("SELECT last_event_seq FROM game_sessions;"));
        }
    }
}
