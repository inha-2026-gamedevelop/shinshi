// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Third Party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Tests.Sessions
{
    /// <summary> 던전당 단조 이벤트 순번, 물약 소비 영수증, 종료 이벤트와 체크포인트를 검사한다 </summary>
    public sealed class SessionEventTests
    {
        /****************************************
        *                Fields
        ****************************************/

        [Fact]
        public void ExperienceEvent_NullSettingsHash_IsRejectedWithoutThrowing()
        {
            var entry = new SessionEventInput
            {
                EventId = Guid.NewGuid(), EventSeq = 1, ParticipantId = Guid.NewGuid(),
                EventKind = GameSessionCodes.EventKind.EXPERIENCE_GRANTED, Amount = 5,
                ExperienceSourceKind = CharacterExperienceSource.NORMAL_ENEMY,
                ExperienceSourceId = "enemy-1", ExperienceSettingsHash = null!
            };
            Assert.False(SessionEventValidator.IsValid(new[] { entry }));
        }

        private const long TICKETS          = 2;
        private const long POTIONS          = 5;
        private const long POTIONS_RESERVED = 2;
        private const long ESSENCE          = 30;
        private const int  SCHEMA_VERSION   = 1;
        private const long CHECKPOINT_SEQ   = 1;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 순번이 이어진 배치는 한 번 저장되고 물약 사용은 예약분에서 한 번만 소비된다 </summary>
        [PostgresFact]
        public async Task Append_InOrderBatch_StoresOnceAndConsumesPotion()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid participant = session.Participants[0].ParticipantId;
            Guid operationId = Guid.NewGuid();
            List<SessionEventInput> batch = new List<SessionEventInput>
            {
                Essence(1, participant, ESSENCE),
                Loot(2),
                Potion(3, participant)
            };

            OperationResult first = await environment.Sessions.AppendEventsAsync(session.InstanceId, session.SessionId,
                operationId, batch);
            OperationResult replay = await environment.Sessions.AppendEventsAsync(session.InstanceId, session.SessionId,
                operationId, batch);

            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, first.Status);
            Assert.Equal(3L, first.CommittedRevision);
            Assert.Equal(first.ReceiptId, replay.ReceiptId);
            Assert.Equal(3L, await environment.ScalarAsync("SELECT count(*) FROM session_events;"));
            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(POTIONS - 1, wallet.Potions);
            Assert.Equal(POTIONS_RESERVED - 1, wallet.PotionsReserved);
            Assert.Equal(-1L, await environment.LedgerSumAsync(player.AccountId, EconomyLedgerCodes.ASSET_ITEM));
            Assert.Equal(ESSENCE, await environment.ScalarAsync(
                "SELECT essence_accrued FROM session_participants WHERE participant_id = @Id;", new { Id = participant }));
            Assert.Equal(0L, wallet.Essence);
        }

        /// <summary> 새 operationId로 보낸 중복, 역행, 건너뛴 순번은 모두 거절하고 저장된 마지막 순번을 알려준다 </summary>
        [PostgresFact]
        public async Task Append_DuplicateRegressedOrGappedSequence_RejectsWholeBatch()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid participant = session.Participants[0].ParticipantId;
            await AppendAsync(environment, session, new[] { Loot(1), Loot(2), Loot(3) });

            OperationResult duplicate = await AppendAsync(environment, session, new[] { Loot(3), Loot(4) });
            OperationResult regressed = await AppendAsync(environment, session, new[] { Loot(2) });
            OperationResult gapped = await AppendAsync(environment, session, new[] { Potion(5, participant) });
            SessionEventInput reused = Loot(4);
            reused.EventId = Guid.Parse(await environment.TextAsync(
                "SELECT event_id::text FROM session_events WHERE event_seq = 1;"));
            OperationResult reusedId = await AppendAsync(environment, session, new[] { reused });
            OperationResult next = await AppendAsync(environment, session, new[] { Loot(4) });

            foreach (OperationResult rejected in new[] { duplicate, regressed, gapped, reusedId })
            {
                Assert.Equal(ContractCodes.OperationStatus.REJECTED, rejected.Status);
                Assert.Equal(ContractCodes.RejectReason.STALE_REVISION, rejected.ReasonCode);
                Assert.Equal(3L, rejected.CommittedRevision);
            }
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, next.Status);
            Assert.Equal(4L, await environment.ScalarAsync("SELECT count(*) FROM session_events;"));
            Assert.Equal(POTIONS_RESERVED, (await environment.ReadWalletAsync(player.AccountId)).PotionsReserved);
        }

        /// <summary> 예약분을 넘는 물약 사용은 배치 전체를 거절하고 이벤트도 저장하지 않는다 </summary>
        [PostgresFact]
        public async Task Append_PotionBeyondReservation_RejectsWithoutPartialEffect()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid participant = session.Participants[0].ParticipantId;

            OperationResult result = await AppendAsync(environment, session, new[]
            {
                Essence(1, participant, ESSENCE), Potion(2, participant), Potion(3, participant), Potion(4, participant)
            });

            Assert.Equal(ContractCodes.RejectReason.POTION_LIMIT, result.ReasonCode);
            Assert.Equal(0L, result.CommittedRevision);
            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM session_events;"));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT essence_accrued FROM session_participants;"));
            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(POTIONS, wallet.Potions);
            Assert.Equal(POTIONS_RESERVED, wallet.PotionsReserved);
        }

        /// <summary> 누적 정기 상한을 넘은 배치의 물약 소비와 다른 참가자 정기까지 함께 되돌린다 </summary>
        [PostgresFact]
        public async Task Append_CumulativeEssenceAboveLimit_RejectsWithLimitCodeAndRollsBackBatch()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer[] players =
            {
                await environment.CreatePlayerAsync(TICKETS, POTIONS),
                await environment.CreatePlayerAsync(TICKETS, POTIONS)
            };
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, players,
                POTIONS_RESERVED);
            Guid fullParticipant = session.Participants[0].ParticipantId;
            Guid otherParticipant = session.Participants[1].ParticipantId;
            long previousEssence = SettlementSanityLimits.MAX_SESSION_ESSENCE - 1;
            await AppendAsync(environment, session, new[]
            {
                Essence(1, fullParticipant, previousEssence), Essence(2, otherParticipant, 1)
            });
            Guid operationId = Guid.NewGuid();
            SessionEventInput[] rejectedBatch =
            {
                Potion(3, fullParticipant), Essence(4, fullParticipant, 2), Essence(5, otherParticipant, 1)
            };

            OperationResult rejected = await environment.Sessions.AppendEventsAsync(session.InstanceId, session.SessionId,
                operationId, rejectedBatch);
            OperationResult replay = await environment.Sessions.AppendEventsAsync(session.InstanceId, session.SessionId,
                operationId, rejectedBatch);

            Assert.Equal(ContractCodes.OperationStatus.REJECTED, rejected.Status);
            Assert.Equal(ContractCodes.RejectReason.SETTLEMENT_LIMIT_EXCEEDED, rejected.ReasonCode);
            Assert.Equal(2L, rejected.CommittedRevision);
            Assert.Equal(rejected.ReasonCode, replay.ReasonCode);
            Assert.Equal(rejected.ServerTimeUnixMs, replay.ServerTimeUnixMs);
            Assert.Equal(2L, await environment.ScalarAsync("SELECT count(*) FROM session_events;"));
            Assert.Equal(2L, await environment.ScalarAsync("SELECT last_event_seq FROM game_sessions;"));
            Assert.Equal(previousEssence, await environment.ScalarAsync(
                "SELECT essence_accrued FROM session_participants WHERE participant_id = @Id;",
                new { Id = fullParticipant }));
            Assert.Equal(1L, await environment.ScalarAsync(
                "SELECT essence_accrued FROM session_participants WHERE participant_id = @Id;",
                new { Id = otherParticipant }));
            foreach (TestPlayer player in players)
            {
                WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
                Assert.Equal(POTIONS, wallet.Potions);
                Assert.Equal(POTIONS_RESERVED, wallet.PotionsReserved);
                Assert.Equal(0L, wallet.Essence);
                Assert.Equal(0L, await environment.LedgerSumAsync(player.AccountId, EconomyLedgerCodes.ASSET_ITEM));
            }
        }

        /// <summary> 티켓을 정산 아이템으로 지급하는 입력은 저장 전에 거절되어 지갑과 재고를 바꾸지 않는다 </summary>
        [PostgresFact]
        public async Task Append_SessionEndedGrantingTicket_RejectsWithoutMinting()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            Guid participant = session.Participants[0].ParticipantId;
            SettlementItemInput ticket = new SettlementItemInput
            {
                DefinitionId = SettlementSanityLimits.TICKET_DEFINITION_ID,
                BindingKind = SessionTestEnvironment.BINDING,
                Quantity = 1
            };

            await Assert.ThrowsAsync<ArgumentException>(() => AppendAsync(environment, session, new[]
            {
                Essence(1, participant, ESSENCE),
                Ended(2, Guid.NewGuid(), new[] { Grant(participant, ESSENCE, ticket) })
            }));

            Assert.Equal(0L, await environment.ScalarAsync("SELECT count(*) FROM session_events;"));
            Assert.Equal(0L, await environment.ScalarAsync("SELECT essence_accrued FROM session_participants;"));
            Assert.Equal(0L, await environment.ScalarAsync(
                "SELECT count(*) FROM account_items WHERE definition_id = @DefinitionId;",
                new { DefinitionId = SettlementSanityLimits.TICKET_DEFINITION_ID }));
            Assert.Equal("Running", await environment.TextAsync("SELECT state FROM game_sessions;"));
            WalletSnapshot wallet = await environment.ReadWalletAsync(player.AccountId);
            Assert.Equal(TICKETS - 1, wallet.Tickets);
            Assert.Equal(0L, wallet.Essence);
        }

        /// <summary> SessionEnded 뒤 세션은 Settling이며 추가 이벤트를 받지 않는다. 정산안은 활성 참가자 전원을 덮어야 한다 </summary>
        [PostgresFact]
        public async Task Append_SessionEnded_MovesToSettlingAndClosesStream()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer[] players =
            {
                await environment.CreatePlayerAsync(TICKETS, POTIONS),
                await environment.CreatePlayerAsync(TICKETS, POTIONS)
            };
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, players,
                POTIONS_RESERVED);

            OperationResult partial = await AppendAsync(environment, session, new[]
            {
                Ended(1, Guid.NewGuid(), session.Participants.Take(1).Select(row => Grant(row.ParticipantId, ESSENCE)))
            });
            OperationResult ended = await AppendAsync(environment, session, new[]
            {
                Ended(1, Guid.NewGuid(), session.Participants.Select(row => Grant(row.ParticipantId, ESSENCE)))
            });
            OperationResult after = await AppendAsync(environment, session, new[] { Loot(2) });

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, partial.ReasonCode);
            Assert.Equal(ContractCodes.OperationStatus.COMMITTED, ended.Status);
            Assert.Equal(ContractCodes.RejectReason.SESSION_TERMINAL, after.ReasonCode);
            Assert.Equal("Settling", await environment.TextAsync(
                "SELECT state FROM game_sessions WHERE session_id = @Id;", new { Id = session.SessionId }));
        }

        /// <summary> 다른 던전은 이 세션에 이벤트를 쓸 수 없다 </summary>
        [PostgresFact]
        public async Task Append_OtherInstance_RejectsSessionMismatch()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);

            OperationResult result = await environment.Sessions.AppendEventsAsync(Guid.NewGuid(), session.SessionId,
                Guid.NewGuid(), new[] { Loot(1) });

            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH, result.ReasonCode);
        }

        /// <summary> 체크포인트는 같은 순번 재전송을 중복으로, 저장 이벤트보다 앞선 순번을 거절로 처리한다 </summary>
        [PostgresFact]
        public async Task SaveCheckpoint_DuplicateAndAhead_AreDistinguished()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            await AppendAsync(environment, session, new[] { Loot(1) });
            byte[] state = { 1, 2, 3 };

            ECheckpointStatus saved = await environment.Sessions.SaveCheckpointAsync(session.InstanceId,
                session.SessionId, new CheckpointInput(CHECKPOINT_SEQ, 1, SCHEMA_VERSION, state));
            ECheckpointStatus duplicate = await environment.Sessions.SaveCheckpointAsync(session.InstanceId,
                session.SessionId, new CheckpointInput(CHECKPOINT_SEQ, 1, SCHEMA_VERSION, state));
            ECheckpointStatus ahead = await environment.Sessions.SaveCheckpointAsync(session.InstanceId,
                session.SessionId, new CheckpointInput(CHECKPOINT_SEQ + 1, 2, SCHEMA_VERSION, state));

            Assert.Equal(ECheckpointStatus.Saved, saved);
            Assert.Equal(ECheckpointStatus.Duplicate, duplicate);
            Assert.Equal(ECheckpointStatus.AheadOfEvents, ahead);
            Assert.Equal(1L, await environment.ScalarAsync("SELECT count(*) FROM session_checkpoints;"));
        }

        /// <summary> session_events는 추가 전용이라 수정, 삭제가 DB에서 막힌다 </summary>
        [PostgresFact]
        public async Task SessionEvents_UpdateOrDelete_IsRejectedByDatabase()
        {
            await using SessionTestEnvironment environment = await SessionTestEnvironment.CreateAsync();
            TestPlayer player = await environment.CreatePlayerAsync(TICKETS, POTIONS);
            ReservedSessionView session = await environment.StartAsync(GameSessionCodes.Mode.BOSS, new[] { player },
                POTIONS_RESERVED);
            await AppendAsync(environment, session, new[] { Loot(1) });

            await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                environment.ExecuteAsync("UPDATE session_events SET event_kind = 1;"));
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                environment.ExecuteAsync("DELETE FROM session_events;"));
        }

        internal static Task<OperationResult> AppendAsync(SessionTestEnvironment environment, ReservedSessionView session,
            IReadOnlyList<SessionEventInput> events)
        {
            return environment.Sessions.AppendEventsAsync(session.InstanceId, session.SessionId, Guid.NewGuid(), events);
        }

        internal static SessionEventInput Loot(long seq)
        {
            return new SessionEventInput
            {
                EventSeq = seq,
                EventId = Guid.NewGuid(),
                EventKind = GameSessionCodes.EventKind.LOOT,
                PayloadJson = "{\"drop\":\"test\"}"
            };
        }

        internal static SessionEventInput Potion(long seq, Guid participantId)
        {
            return new SessionEventInput
            {
                EventSeq = seq,
                EventId = Guid.NewGuid(),
                EventKind = GameSessionCodes.EventKind.POTION_USED,
                ParticipantId = participantId,
                DefinitionId = SessionTestEnvironment.POTION_ID,
                BindingKind = SessionTestEnvironment.BINDING
            };
        }

        internal static SessionEventInput Essence(long seq, Guid participantId, long amount)
        {
            return new SessionEventInput
            {
                EventSeq = seq,
                EventId = Guid.NewGuid(),
                EventKind = GameSessionCodes.EventKind.ESSENCE_ACCRUED,
                ParticipantId = participantId,
                Amount = amount
            };
        }

        internal static SessionEventInput Ended(long seq, Guid settlementOperationId, IEnumerable<SettlementGrantInput> grants)
        {
            return new SessionEventInput
            {
                EventSeq = seq,
                EventId = Guid.NewGuid(),
                EventKind = GameSessionCodes.EventKind.SESSION_ENDED,
                EndReason = GameSessionCodes.EndReason.SUCCESS,
                SettlementOperationId = settlementOperationId,
                Grants = grants.ToList()
            };
        }

        internal static SettlementGrantInput Grant(Guid participantId, long essence, params SettlementItemInput[] items)
        {
            return new SettlementGrantInput
            {
                ParticipantId = participantId,
                Essence = essence,
                Items = items.ToList()
            };
        }
    }
}
