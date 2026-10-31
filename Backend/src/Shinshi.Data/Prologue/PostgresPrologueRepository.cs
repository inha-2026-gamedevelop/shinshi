// System
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Npgsql;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Data.Growth;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Prologue
{
    /// <summary> 프롤로그 상태 조회, 입장(세션, 던전 접속 임대, 입장권을 한 트랜잭션), 건너뛰기, 던전 서버의 단계 저장과 완료.
    /// 모든 쓰기는 IdempotentOperationExecutor를 거치고 계정 -> 캐릭터 -> 아이템 순서로 잠근다. 서버 경로는 세션을 먼저 잠근다 </summary>
    public sealed class PostgresPrologueRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ENTRY_KIND    = "prologue.entry";
        public const string SKIP_KIND     = "prologue.skip";
        public const string STAGE_KIND    = "prologue.stage";
        public const string COMPLETE_KIND = "prologue.complete";

        // prologue_completions.source
        private const string SOURCE_PLAYED  = "Played";
        private const string SOURCE_SKIPPED = "Skipped";

        // 프롤로그는 1인 출정이다(0023 ck_game_sessions_prologue_solo)
        private const int STARTING_COUNT = 1;

        private readonly NpgsqlDataSource _dataSource;
        private readonly IdempotentOperationExecutor _executor;
        // 입장 경로만 쓴다. 서버 경로(단계, 완료)만 쓰는 호스트가 인증 설정 없이도 이 저장소를 만들 수 있게 처음 쓸 때 만든다
        private readonly Lazy<PostgresAuthRepository> _auth;
        private readonly PostgresAccountProgressRepository _progress;
        private readonly InitialGrantService _grants;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresPrologueRepository(NpgsqlDataSource dataSource, IdempotentOperationExecutor executor,
            PostgresAuthRepository auth, PostgresAccountProgressRepository progress, InitialGrantService grants)
            : this(dataSource, executor, new Lazy<PostgresAuthRepository>(auth ?? throw new ArgumentNullException(nameof(auth))),
                progress, grants)
        {
        }

        public PostgresPrologueRepository(NpgsqlDataSource dataSource, IdempotentOperationExecutor executor,
            Lazy<PostgresAuthRepository> auth, PostgresAccountProgressRepository progress, InitialGrantService grants)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(executor);
            ArgumentNullException.ThrowIfNull(auth);
            ArgumentNullException.ThrowIfNull(progress);
            ArgumentNullException.ThrowIfNull(grants);
            _dataSource = dataSource;
            _executor   = executor;
            _auth       = auth;
            _progress   = progress;
            _grants     = grants;
        }

        /// <summary> 이 계정의 살아 있는 캐릭터의 프롤로그 상태. 없거나 다른 계정 캐릭터, 삭제한 캐릭터면 null이다 </summary>
        public async Task<PrologueStateResponse?> ReadStateAsync(Guid accountId, Guid characterId,
            CancellationToken cancellationToken = default)
        {
            if ((accountId == Guid.Empty) || (characterId == Guid.Empty))
            {
                return null;
            }

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            StateRow? row = await connection.QuerySingleOrDefaultAsync<StateRow>(new CommandDefinition(
                """
                SELECT hero.prologue_stage AS "Stage", hero.prologue_wolbaek_potions AS "WolbaekPotions",
                    hero.prologue_talisman_checked AS "TalismanChecked",
                    COALESCE((SELECT progress.intro_completed_at IS NOT NULL FROM account_progress AS progress
                        WHERE progress.account_id = hero.account_id), false) AS "AccountCompleted",
                    clock_timestamp() AS "Now"
                FROM characters AS hero
                WHERE hero.character_id = @CharacterId AND hero.account_id = @AccountId AND hero.deleted_at IS NULL;
                """, new { AccountId = accountId, CharacterId = characterId }, cancellationToken: cancellationToken));
            return row == null ? null : new PrologueStateResponse(characterId.ToString("D"), row.Stage,
                row.WolbaekPotions, row.TalismanChecked, row.AccountCompleted, UnixMilliseconds(row.Now));
        }

        /// <summary> 프롤로그 던전 입장권을 발급한다. 같은 operationId 재전송은 처음 세션의 같은 입장권(교환, 만료 전)을 돌려준다.
        /// 같은 캐릭터의 시작 전(Reserved) 프롤로그 출정이 살아 있으면 새로 만들지 않고 그 출정의 새 입장권을 발급한다 </summary>
        public async Task<PrologueEntryOutcome> EnterAsync(PlayerAuthIdentity identity, Guid characterId, Guid operationId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(identity);
            OperationRequest request = CreateEntryRequest(identity.AccountId, characterId, operationId);
            OperationResult result = await _executor.ExecuteAsync(request,
                (context, token) => EntryEffectAsync(identity, characterId, operationId, context, token), cancellationToken);
            return await ToEntryOutcomeAsync(identity, operationId, result, cancellationToken);
        }

        /// <summary> 점검, 기능 차단 중에는 같은 operationId의 저장 결과만 돌려주고 아무것도 실행하지 않는다. 처음 보는 ID면 null이다 </summary>
        public async Task<PrologueEntryOutcome?> FindStoredEntryAsync(PlayerAuthIdentity identity, Guid characterId,
            Guid operationId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(identity);
            OperationResult? stored = await _executor.FindStoredAsync(
                CreateEntryRequest(identity.AccountId, characterId, operationId), cancellationToken);
            return stored == null ? null : await ToEntryOutcomeAsync(identity, operationId, stored, cancellationToken);
        }

        /// <summary> 이미 프롤로그를 마친 계정이 이 캐릭터의 프롤로그를 건너뛴다. 완료 표식과 기본 지급품은 직접 완료와 같다 </summary>
        public Task<OperationResult> SkipAsync(Guid accountId, Guid characterId, Guid operationId,
            CancellationToken cancellationToken = default)
        {
            RequireIds(accountId, characterId, operationId);
            OperationRequest request = new OperationRequest(accountId, operationId, SKIP_KIND,
                SessionDb.ToRequestJson(new { characterId = characterId.ToString("D") }));
            return _executor.ExecuteAsync(request,
                (context, token) => SkipEffectAsync(accountId, characterId, operationId, context, token), cancellationToken);
        }

        /// <summary> 던전 서버가 구간이 바뀔 때 캐릭터의 저장 단계를 앞으로만 갱신한다. 같은 단계 재전송은 확정이다 </summary>
        public Task<OperationResult> SaveStageAsync(PrologueStageInput input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            OperationRequest request = new OperationRequest(input.DungeonInstanceId, input.OperationId, STAGE_KIND,
                SessionDb.ToRequestJson(new
                {
                    sessionId = input.SessionId.ToString("D"),
                    characterId = input.CharacterId.ToString("D"),
                    stage = input.Stage,
                    wolbaekPotions = input.WolbaekPotions,
                    talismanChecked = input.TalismanChecked
                }));
            return _executor.ExecuteAsync(request, (context, token) => StageEffectAsync(input, context, token),
                cancellationToken);
        }

        /// <summary> 던전 서버가 마무리 구간 뒤 완료를 기록한다. 캐릭터 단계 완료, 계정 완료(신시 안내 완료), 완료 표식과
        /// 기본 지급품을 한 트랜잭션에 확정한다. 다른 operationId로 다시 보내도 지급은 캐릭터마다 한 번이다 </summary>
        public Task<OperationResult> CompleteAsync(Guid dungeonInstanceId, Guid sessionId, Guid characterId,
            Guid operationId, CancellationToken cancellationToken = default)
        {
            RequireIds(dungeonInstanceId, sessionId, operationId);
            if (characterId == Guid.Empty)
            {
                throw new ArgumentException("Character ID is required.", nameof(characterId));
            }

            OperationRequest request = new OperationRequest(dungeonInstanceId, operationId, COMPLETE_KIND,
                SessionDb.ToRequestJson(new
                {
                    sessionId = sessionId.ToString("D"),
                    characterId = characterId.ToString("D")
                }));
            return _executor.ExecuteAsync(request, (context, token) => CompleteEffectAsync(dungeonInstanceId, sessionId,
                characterId, operationId, context, token), cancellationToken);
        }

        private static OperationRequest CreateEntryRequest(Guid accountId, Guid characterId, Guid operationId)
        {
            RequireIds(accountId, characterId, operationId);

            // actor는 인증된 계정이다. 로그인 묶음은 해시에 넣지 않아 토큰을 갱신한 클라이언트의 재전송도 처음 결과를 받는다
            return new OperationRequest(accountId, operationId, ENTRY_KIND,
                SessionDb.ToRequestJson(new { characterId = characterId.ToString("D") }));
        }

        private async Task<PrologueEntryOutcome> ToEntryOutcomeAsync(PlayerAuthIdentity identity, Guid operationId,
            OperationResult result, CancellationToken cancellationToken)
        {
            if (result.Status != ContractCodes.OperationStatus.COMMITTED)
            {
                return PrologueEntryOutcome.Rejected(result.Status == ContractCodes.OperationStatus.REJECTED
                    ? result.ReasonCode : ContractCodes.RejectReason.RESERVATION_CONFLICT);
            }

            // 처음 응답과 재전송이 같은 경로로 파생 원문을 다시 만든다. 원문은 DB에 없다
            AuthValidationOutcome<JoinTicket> ticket = await _auth.Value.ReadPrologueTicketAsync(identity, operationId,
                cancellationToken);
            return ticket.IsSuccess ? PrologueEntryOutcome.Issued(ticket.Identity!)
                : PrologueEntryOutcome.Rejected(ticket.ReasonCode);
        }

        private async Task<OperationDecision> EntryEffectAsync(PlayerAuthIdentity identity, Guid characterId,
            Guid operationId, OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            Guid accountId = identity.AccountId;

            // 잠금 순서: 계정 -> 캐릭터 -> 계정 접속 임대 -> 인스턴스(SKIP LOCKED) -> 로그인 묶음 -> 서버 임대
            if (await SessionDb.LockAccountsAsync(connection, transaction, new[] { accountId }, cancellationToken) != 1)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.CHARACTER_NOT_FOUND);
            }

            CharacterRow? character = await LockCharacterAsync(connection, transaction, accountId, characterId,
                cancellationToken);
            int? characterReason = CheckCharacter(character);
            if (characterReason.HasValue)
            {
                return OperationDecision.Rejected(characterReason.Value);
            }
            if (character!.Stage == PrologueCodes.Stage.DONE)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.PROLOGUE_ALREADY_DONE);
            }

            // 열린 출정은 계정당 하나다(예약과 같은 검사). 같은 캐릭터의 시작 전 프롤로그만 이어서 쓴다.
            // 시작한(Running) 프롤로그의 재접속은 기존 복귀 입장권 경로(rejoin-ticket)가 맡는다
            OpenSessionRow? open = await connection.QuerySingleOrDefaultAsync<OpenSessionRow>(new CommandDefinition(
                """
                SELECT session.session_id AS "SessionId", session.mode AS "Mode", session.state AS "State",
                    session.instance_id AS "InstanceId", session.revision AS "Revision",
                    COALESCE(session.expires_at > clock_timestamp(), false) AS "NotExpired",
                    participant.participant_id AS "ParticipantId", participant.character_id AS "CharacterId",
                    participant.state AS "ParticipantState"
                FROM session_participants AS participant
                JOIN game_sessions AS session ON session.session_id = participant.session_id
                WHERE participant.account_id = @AccountId AND participant.state IN ('Reserved', 'Active')
                ORDER BY session.created_at DESC, session.session_id
                LIMIT 1;
                """, new { AccountId = accountId }, transaction, cancellationToken: cancellationToken));
            bool reuse = (open != null) && (open.Mode == GameSessionStates.MODE_PROLOGUE)
                && (open.CharacterId == characterId) && (open.State == GameSessionStates.RESERVED)
                && (open.ParticipantState == GameSessionStates.PARTICIPANT_RESERVED) && open.NotExpired;
            if ((open != null) && !reuse)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.RESERVATION_CONFLICT);
            }

            // 로비 단계라 마을 임대가 없어야 한다. 이어 쓰는 출정이 이미 만든 그 던전의 임대만 그대로 둔다.
            // 만료됐거나 소실된 서버의 임대는 접속 중으로 보지 않는다(계정 임대 획득과 같은 판정)
            LeaseRow? lease = await connection.QuerySingleOrDefaultAsync<LeaseRow>(new CommandDefinition(
                """
                SELECT lease.instance_id AS "InstanceId",
                    (lease.expires_at > clock_timestamp() AND EXISTS (SELECT 1 FROM server_instances AS instance
                        WHERE instance.instance_id = lease.instance_id AND instance.role = lease.role
                            AND instance.state IN ('Idle', 'Reserved', 'Active', 'Draining')
                            AND instance.heartbeat_at > clock_timestamp() - @HeartbeatTimeout)) AS "Alive"
                FROM account_leases AS lease WHERE lease.account_id = @AccountId
                FOR UPDATE OF lease;
                """, new { AccountId = accountId, HeartbeatTimeout = ServerPoolTimings.HeartbeatTimeout }, transaction,
                cancellationToken: cancellationToken));
            if ((lease != null) && lease.Alive && !(reuse && (lease.InstanceId == open!.InstanceId)))
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.ALREADY_CONNECTED);
            }

            Guid sessionId;
            Guid participantId;
            Guid instanceId;
            long revision;
            if (reuse)
            {
                sessionId     = open!.SessionId;
                participantId = open.ParticipantId;
                instanceId    = open.InstanceId;
                revision      = open.Revision;

                // 그 던전의 임대가 만료됐으면(배정 던전 heartbeat 공백 등) 이관 원본이 없으므로 예약 만료까지 다시 만든다
                bool leased = (lease != null) && lease.Alive;
                if (!leased && !await AcquireDungeonLeaseAsync(connection, transaction, accountId, characterId, sessionId,
                    instanceId, cancellationToken))
                {
                    return OperationDecision.Rejected(ContractCodes.RejectReason.ALREADY_CONNECTED);
                }
            }
            else
            {
                GrowthContent content = GrowthContent.LoadEmbedded();
                AllocatedDungeon? dungeon = await PostgresServerPoolRepository.TryAllocateIdleDungeonAsync(connection,
                    transaction, content.ContentVersion, cancellationToken);
                if (dungeon == null)
                {
                    return OperationDecision.Rejected(ContractCodes.RejectReason.NO_CAPACITY);
                }

                // 일반 예약과 같이 캐릭터의 전투 장비, 수련, 부적을 고정한다. 던전 입장 교환이 이 값을 돌려준다
                Dictionary<Guid, string> loadouts = await CombatLoadoutBuilder.BuildAsync(connection, transaction,
                    new[] { characterId }, content.ContentVersion, cancellationToken);
                sessionId     = Guid.NewGuid();
                participantId = Guid.NewGuid();
                instanceId    = dungeon.InstanceId;
                revision      = await InsertSessionAsync(connection, transaction, sessionId, participantId, accountId,
                    characterId, dungeon, content, loadouts[characterId], cancellationToken);
                if (!await AcquireDungeonLeaseAsync(connection, transaction, accountId, characterId, sessionId,
                    instanceId, cancellationToken))
                {
                    return OperationDecision.Rejected(ContractCodes.RejectReason.ALREADY_CONNECTED);
                }
            }

            int tokenReason = await _auth.Value.InsertPrologueJoinTokenAsync(connection, transaction, identity, operationId,
                sessionId, participantId, characterId, instanceId, cancellationToken);
            if (tokenReason != ContractCodes.RejectReason.NONE)
            {
                return OperationDecision.Rejected(tokenReason);
            }

            return OperationDecision.Committed(revision, sessionId.ToString("D"));
        }

        private static async Task<long> InsertSessionAsync(DbConnection connection, DbTransaction transaction,
            Guid sessionId, Guid participantId, Guid accountId, Guid characterId, AllocatedDungeon dungeon,
            GrowthContent content, string loadout, CancellationToken cancellationToken)
        {
            // reserved_by_instance_id는 마을이 아니라 배정 던전 자신이다. 접속 임대 이관(lease-transfer)이 예약 주체에 있는
            // 임대를 옮기므로, 이 트랜잭션이 그 던전에 만든 임대가 그대로 이관 원본이 된다. 티켓, 물약 예약은 없다.
            // experience_settings는 입장 교환의 경험치 표 대조를 위해 일반 예약과 같은 값을 넣는다(프롤로그 경험치 이벤트는 거절된다)
            long revision = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                INSERT INTO game_sessions (session_id, mode, map_id, state, starting_count, content_version, instance_id,
                    server_lease, reserved_by_instance_id, instance_boot_id, expires_at, battle_id, boss_id, difficulty,
                    experience_settings)
                VALUES (@SessionId, @Mode, @MapId, 'Reserved', @StartingCount, @ContentVersion, @InstanceId,
                    @ServerLease, @InstanceId, @BootId, clock_timestamp() + @Lifetime, @BattleId, @BossId, @Difficulty,
                    CAST(@ExperienceSettings AS jsonb))
                RETURNING revision;
                """, new
                {
                    SessionId = sessionId,
                    Mode = GameSessionStates.MODE_PROLOGUE,
                    MapId = PrologueCodes.MAP_ID,
                    StartingCount = STARTING_COUNT,
                    content.ContentVersion,
                    dungeon.InstanceId,
                    ServerLease = dungeon.ServerLeaseId,
                    dungeon.BootId,
                    Lifetime = ServerPoolTimings.ReservationLifetime,
                    BattleId = PrologueCodes.BATTLE_ID,
                    BossId = PrologueCodes.BOSS_ID,
                    Difficulty = (short)GameSessionCodes.Difficulty.EASY,
                    ExperienceSettings = Newtonsoft.Json.JsonConvert.SerializeObject(content.CharacterLevel)
                }, transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO session_participants (session_id, participant_id, account_id, character_id, state, loadout_snapshot)
                VALUES (@SessionId, @ParticipantId, @AccountId, @CharacterId, 'Reserved', CAST(@Loadout AS jsonb));
                """, new
                {
                    SessionId = sessionId, ParticipantId = participantId, AccountId = accountId,
                    CharacterId = characterId, Loadout = loadout
                }, transaction, cancellationToken: cancellationToken));
            return revision;
        }

        // 계정 접속 임대를 배정 던전에 직접 만든다. 만료는 예약 만료와 같아 150초 안에 접속하지 않으면 예약 만료 해제
        // (ReleaseReservedAsync -> ReleaseForClosedSessionAsync)가 함께 푼다. 살아 있는 다른 임대는 덮어쓰지 않는다
        private static async Task<bool> AcquireDungeonLeaseAsync(DbConnection connection, DbTransaction transaction,
            Guid accountId, Guid characterId, Guid sessionId, Guid instanceId, CancellationToken cancellationToken)
        {
            Guid? acquired = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                """
                INSERT INTO account_leases AS held (account_id, lease_id, instance_id, role, expires_at, character_id)
                SELECT @AccountId, @LeaseId, @InstanceId, @Dungeon, session.expires_at, @CharacterId
                FROM game_sessions AS session WHERE session.session_id = @SessionId
                ON CONFLICT (account_id) DO UPDATE SET lease_id = EXCLUDED.lease_id, instance_id = EXCLUDED.instance_id,
                    role = EXCLUDED.role, expires_at = EXCLUDED.expires_at, character_id = EXCLUDED.character_id
                    WHERE held.expires_at <= clock_timestamp()
                        OR NOT EXISTS (SELECT 1 FROM server_instances AS previous_instance
                            WHERE previous_instance.instance_id = held.instance_id AND previous_instance.role = held.role
                                AND previous_instance.state IN ('Idle', 'Reserved', 'Active', 'Draining')
                                AND previous_instance.heartbeat_at > clock_timestamp() - @HeartbeatTimeout)
                RETURNING held.lease_id;
                """, new
                {
                    AccountId = accountId, LeaseId = Guid.NewGuid(), InstanceId = instanceId,
                    Dungeon = ContractCodes.ServerRole.DUNGEON, CharacterId = characterId, SessionId = sessionId,
                    HeartbeatTimeout = ServerPoolTimings.HeartbeatTimeout
                }, transaction, cancellationToken: cancellationToken));
            return acquired.HasValue;
        }

        private async Task<OperationDecision> SkipEffectAsync(Guid accountId, Guid characterId, Guid operationId,
            OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            if (await SessionDb.LockAccountsAsync(connection, transaction, new[] { accountId }, cancellationToken) != 1)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.CHARACTER_NOT_FOUND);
            }

            CharacterRow? character = await LockCharacterAsync(connection, transaction, accountId, characterId,
                cancellationToken);
            int? characterReason = CheckCharacter(character);
            if (characterReason.HasValue)
            {
                return OperationDecision.Rejected(characterReason.Value);
            }
            if (character!.Stage == PrologueCodes.Stage.DONE)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.PROLOGUE_ALREADY_DONE);
            }

            // 계정 행을 잠근 뒤 읽으므로 같은 계정의 프롤로그 완료 기록과 엇갈리지 않는다
            SkipGuardRow guard = await connection.QuerySingleAsync<SkipGuardRow>(new CommandDefinition(
                """
                SELECT EXISTS (SELECT 1 FROM account_progress
                        WHERE account_id = @AccountId AND intro_completed_at IS NOT NULL) AS "AccountCompleted",
                    EXISTS (SELECT 1 FROM session_participants
                        WHERE account_id = @AccountId AND state IN ('Reserved', 'Active')) AS "Busy",
                    EXISTS (SELECT 1 FROM account_leases AS lease JOIN server_instances AS instance
                            ON instance.instance_id = lease.instance_id AND instance.role = lease.role
                        WHERE lease.account_id = @AccountId AND lease.expires_at > clock_timestamp()
                            AND instance.state IN ('Idle', 'Reserved', 'Active', 'Draining')
                            AND instance.heartbeat_at > clock_timestamp() - @HeartbeatTimeout) AS "Connected";
                """, new { AccountId = accountId, HeartbeatTimeout = ServerPoolTimings.HeartbeatTimeout }, transaction,
                cancellationToken: cancellationToken));
            if (!guard.AccountCompleted)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.PROLOGUE_REQUIRED);
            }
            if (guard.Connected)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.ALREADY_CONNECTED);
            }
            if (guard.Busy)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.RESERVATION_CONFLICT);
            }

            await FinishCharacterAsync(context, accountId, characterId, character.ClassId, accountId, null,
                SOURCE_SKIPPED, operationId, cancellationToken);
            return OperationDecision.Committed(PrologueCodes.Stage.DONE, characterId.ToString("D"));
        }

        private async Task<OperationDecision> StageEffectAsync(PrologueStageInput input, OperationExecutionContext context,
            CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            (int? sessionReason, Guid accountId) = await CheckServerSessionAsync(connection, transaction,
                input.DungeonInstanceId, input.SessionId, input.CharacterId, allowSettling: false, cancellationToken);
            if (sessionReason.HasValue)
            {
                return OperationDecision.Rejected(sessionReason.Value);
            }
            if (!input.IsInRange())
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.PROLOGUE_STAGE_INVALID);
            }

            // 잠금 순서: 세션(공유) -> 계정 -> 캐릭터. /start, 정산과 같은 방향이다
            await SessionDb.LockAccountsAsync(connection, transaction, new[] { accountId }, cancellationToken);
            CharacterRow? character = await LockCharacterAsync(connection, transaction, accountId, input.CharacterId,
                cancellationToken);
            if ((character == null) || character.Deleted)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }
            if (character.Stage == PrologueCodes.Stage.DONE)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.PROLOGUE_ALREADY_DONE);
            }
            if (input.Stage < character.Stage)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.PROLOGUE_STAGE_INVALID);
            }

            // 검사와 갱신을 조건부 갱신 한 문장으로 묶는다. 부적 조사 기록은 한 번 참이면 되돌리지 않는다
            short? saved = await connection.QuerySingleOrDefaultAsync<short?>(new CommandDefinition(
                """
                UPDATE characters
                SET prologue_stage = @Stage, prologue_wolbaek_potions = @Potions,
                    prologue_talisman_checked = prologue_talisman_checked OR @Talisman
                WHERE character_id = @CharacterId AND prologue_stage <= @Stage AND prologue_stage < @Done
                RETURNING prologue_stage;
                """, new
                {
                    Stage = (short)input.Stage, Potions = (short)input.WolbaekPotions, Talisman = input.TalismanChecked,
                    input.CharacterId, Done = (short)PrologueCodes.Stage.DONE
                }, transaction, cancellationToken: cancellationToken));
            if (!saved.HasValue)
            {
                // 같은 행을 잠그고 읽었으므로 여기 오면 다른 경로가 불변 조건을 깼다. 전체를 되돌린다
                throw new InvalidOperationException("Prologue stage update lost its locked row.");
            }

            return OperationDecision.Committed(saved.Value, input.CharacterId.ToString("D"));
        }

        private async Task<OperationDecision> CompleteEffectAsync(Guid dungeonInstanceId, Guid sessionId, Guid characterId,
            Guid operationId, OperationExecutionContext context, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);

            // 종료 이벤트(SESSION_ENDED)를 먼저 저장한 Settling 단계에서도 받는다. 정산(/settlement) 뒤에는 참가자가 Settled라 거절된다
            (int? sessionReason, Guid accountId) = await CheckServerSessionAsync(connection, transaction,
                dungeonInstanceId, sessionId, characterId, allowSettling: true, cancellationToken);
            if (sessionReason.HasValue)
            {
                return OperationDecision.Rejected(sessionReason.Value);
            }

            await SessionDb.LockAccountsAsync(connection, transaction, new[] { accountId }, cancellationToken);
            CharacterRow? character = await LockCharacterAsync(connection, transaction, accountId, characterId,
                cancellationToken);
            if ((character == null) || character.Deleted)
            {
                return OperationDecision.Rejected(ContractCodes.RejectReason.SESSION_MISMATCH);
            }

            // 계정 완료는 기존 신시 안내 완료와 같은 기록이다. 이미 완료한 계정이면 바꾸지 않는다
            await _progress.RecordAsync(context, accountId, EAccountProgressKind.IntroCompleted, string.Empty, 0,
                operationId, cancellationToken);
            await FinishCharacterAsync(context, accountId, characterId, character.ClassId, dungeonInstanceId, sessionId,
                SOURCE_PLAYED, operationId, cancellationToken);
            return OperationDecision.Committed(PrologueCodes.Stage.DONE, characterId.ToString("D"));
        }

        // 캐릭터 단계를 완료로 두고 완료 표식을 넣는다. 표식을 처음 넣은 쪽만 기본 지급품을 받는다(직접 완료와 건너뛰기 공통).
        // actorId는 원장이 결속할 이 작업의 operations actor다
        private async Task FinishCharacterAsync(OperationExecutionContext context, Guid accountId, Guid characterId,
            int classId, Guid actorId, Guid? sessionId, string source, Guid operationId, CancellationToken cancellationToken)
        {
            (DbConnection connection, DbTransaction transaction) = SessionDb.Require(context);
            await connection.ExecuteAsync(new CommandDefinition(
                "UPDATE characters SET prologue_stage = @Done WHERE character_id = @CharacterId;",
                new { Done = (short)PrologueCodes.Stage.DONE, CharacterId = characterId }, transaction,
                cancellationToken: cancellationToken));
            Guid? inserted = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                """
                INSERT INTO prologue_completions (character_id, account_id, session_id, source)
                VALUES (@CharacterId, @AccountId, @SessionId, @Source)
                ON CONFLICT (character_id) DO NOTHING
                RETURNING character_id;
                """, new
                {
                    CharacterId = characterId, AccountId = accountId, SessionId = sessionId, Source = source
                }, transaction, cancellationToken: cancellationToken));
            if (inserted.HasValue)
            {
                await _grants.ApplyPrologueCompletionAsync(context, actorId, accountId, characterId, classId, operationId,
                    cancellationToken);
            }
        }

        // 서버 경로 공통 검사. 세션을 공유 잠금한 뒤 호출 던전 소유의 프롤로그 출정인지, 캐릭터가 떠나지 않은 활성 참가자인지 본다
        private static async Task<(int? Reason, Guid AccountId)> CheckServerSessionAsync(DbConnection connection,
            DbTransaction transaction, Guid dungeonInstanceId, Guid sessionId, Guid characterId, bool allowSettling,
            CancellationToken cancellationToken)
        {
            ServerSessionRow? session = await connection.QuerySingleOrDefaultAsync<ServerSessionRow>(new CommandDefinition(
                """
                SELECT session.mode AS "Mode", session.state AS "State", session.instance_id AS "InstanceId",
                    EXISTS (SELECT 1 FROM server_instances AS instance
                        WHERE instance.instance_id = session.instance_id AND instance.boot_id = session.instance_boot_id
                            AND instance.state <> 'Offline') AS "IsBootOnline"
                FROM game_sessions AS session WHERE session.session_id = @SessionId
                FOR SHARE OF session;
                """, new { SessionId = sessionId }, transaction, cancellationToken: cancellationToken));
            if ((session == null) || (session.InstanceId != dungeonInstanceId)
                || (session.Mode != GameSessionStates.MODE_PROLOGUE) || !session.IsBootOnline)
            {
                return (ContractCodes.RejectReason.SESSION_MISMATCH, Guid.Empty);
            }
            if (session.State == GameSessionStates.RESERVED)
            {
                return (ContractCodes.RejectReason.WRONG_PHASE, Guid.Empty);
            }
            bool open = (session.State == GameSessionStates.RUNNING)
                || (allowSettling && (session.State == GameSessionStates.SETTLING));
            if (!open)
            {
                return (ContractCodes.RejectReason.SESSION_TERMINAL, Guid.Empty);
            }

            ParticipantRow? participant = await connection.QuerySingleOrDefaultAsync<ParticipantRow>(new CommandDefinition(
                """
                SELECT account_id AS "AccountId", state AS "State", departed_at IS NOT NULL AS "Departed"
                FROM session_participants WHERE session_id = @SessionId AND character_id = @CharacterId;
                """, new { SessionId = sessionId, CharacterId = characterId }, transaction,
                cancellationToken: cancellationToken));
            if ((participant == null) || (participant.State != GameSessionStates.PARTICIPANT_ACTIVE) || participant.Departed)
            {
                return (ContractCodes.RejectReason.SESSION_MISMATCH, Guid.Empty);
            }

            return (null, participant.AccountId);
        }

        private static Task<CharacterRow?> LockCharacterAsync(DbConnection connection, DbTransaction transaction,
            Guid accountId, Guid characterId, CancellationToken cancellationToken)
        {
            return connection.QuerySingleOrDefaultAsync<CharacterRow>(new CommandDefinition(
                """
                SELECT class_id AS "ClassId", prologue_stage AS "Stage", deleted_at IS NOT NULL AS "Deleted",
                    delete_requested_at IS NOT NULL AS "DeletePending"
                FROM characters WHERE character_id = @CharacterId AND account_id = @AccountId
                FOR UPDATE;
                """, new { AccountId = accountId, CharacterId = characterId }, transaction,
                cancellationToken: cancellationToken));
        }

        private static int? CheckCharacter(CharacterRow? character)
        {
            if ((character == null) || character.Deleted)
            {
                return ContractCodes.RejectReason.CHARACTER_NOT_FOUND;
            }
            if (character.DeletePending)
            {
                return ContractCodes.RejectReason.CHARACTER_DELETE_PENDING;
            }

            return null;
        }

        private static void RequireIds(Guid first, Guid second, Guid operationId)
        {
            if ((first == Guid.Empty) || (second == Guid.Empty) || (operationId == Guid.Empty))
            {
                throw new ArgumentException("Prologue requests need non-empty IDs.");
            }
        }

        private static long UnixMilliseconds(DateTime value)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        }

        /****************************************
        *               DB Rows
        ****************************************/

        private sealed class StateRow
        {
            public short    Stage            { get; set; }
            public short    WolbaekPotions   { get; set; }
            public bool     TalismanChecked  { get; set; }
            public bool     AccountCompleted { get; set; }
            public DateTime Now              { get; set; }
        }

        private sealed class CharacterRow
        {
            public int   ClassId       { get; set; }
            public short Stage         { get; set; }
            public bool  Deleted       { get; set; }
            public bool  DeletePending { get; set; }
        }

        private sealed class OpenSessionRow
        {
            public Guid   SessionId        { get; set; }
            public string Mode             { get; set; } = string.Empty;
            public string State            { get; set; } = string.Empty;
            public Guid   InstanceId       { get; set; }
            public long   Revision         { get; set; }
            public bool   NotExpired       { get; set; }
            public Guid   ParticipantId    { get; set; }
            public Guid   CharacterId      { get; set; }
            public string ParticipantState { get; set; } = string.Empty;
        }

        private sealed class LeaseRow
        {
            public Guid InstanceId { get; set; }
            public bool Alive      { get; set; }
        }

        private sealed class SkipGuardRow
        {
            public bool AccountCompleted { get; set; }
            public bool Busy             { get; set; }
            public bool Connected        { get; set; }
        }

        private sealed class ServerSessionRow
        {
            public string Mode         { get; set; } = string.Empty;
            public string State        { get; set; } = string.Empty;
            public Guid   InstanceId   { get; set; }
            public bool   IsBootOnline { get; set; }
        }

        private sealed class ParticipantRow
        {
            public Guid   AccountId { get; set; }
            public string State     { get; set; } = string.Empty;
            public bool   Departed  { get; set; }
        }
    }
}
