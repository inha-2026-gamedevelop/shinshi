// System
using System;
using System.Collections.Generic;
using System.Linq;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Sessions;

namespace Shinshi.Data.Reservations
{
    /// <summary> 출정에 지참할 물약 한 종류 </summary>
    public sealed class PotionReservationInput
    {
        /****************************************
        *              Properties
        ****************************************/

        public string DefinitionId { get; }
        public string BindingKind  { get; }
        public long   Quantity     { get; }

        /****************************************
        *                Methods
        ****************************************/

        public PotionReservationInput(string definitionId, string bindingKind, long quantity)
        {
            DefinitionId = definitionId ?? string.Empty;
            BindingKind  = bindingKind ?? string.Empty;
            Quantity     = quantity;
        }
    }

    /// <summary> 예약할 참가자 한 명. 마을 서버가 준비 확인에 쓴 캐릭터 loadout_revision을 함께 보낸다 </summary>
    public sealed class ParticipantReservationInput
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid                                  AccountId         { get; }
        public Guid                                  CharacterId       { get; }
        public long                                  CharacterRevision { get; }
        public IReadOnlyList<PotionReservationInput> Potions           { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ParticipantReservationInput(Guid accountId, Guid characterId, long characterRevision,
            IReadOnlyList<PotionReservationInput> potions)
        {
            AccountId         = accountId;
            CharacterId       = characterId;
            CharacterRevision = characterRevision;
            Potions           = potions ?? Array.Empty<PotionReservationInput>();
        }
    }

    /// <summary> 파티 전원의 출정 예약 요청. actor는 인증된 마을 서버 인스턴스다 </summary>
    public sealed class ReservationInput
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int MIN_PARTY_SIZE = 1;
        public const int MAX_PARTY_SIZE = 4;

        // game_sessions.battle_id, boss_id CHECK(0012)와 같은 길이 상한
        public const int MAX_CONTRACT_ID_LENGTH = 64;

        /****************************************
        *              Properties
        ****************************************/

        public Guid                                       TownInstanceId { get; }
        public Guid                                       OperationId    { get; }
        public int                                        Mode           { get; }
        public string                                     MapId          { get; }
        public string                                     ContentVersion { get; }
        public IReadOnlyList<ParticipantReservationInput> Participants   { get; }

        // 출정 계약. 빈 문자열은 값 없음이고 difficulty는 GameSessionCodes.Difficulty다
        public string                                     BattleId       { get; }
        public string                                     BossId         { get; }
        public int                                        Difficulty     { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ReservationInput(Guid townInstanceId, Guid operationId, int mode, string mapId, string contentVersion,
            IReadOnlyList<ParticipantReservationInput> participants)
            : this(townInstanceId, operationId, mode, mapId, contentVersion, participants, null, null,
                GameSessionCodes.Difficulty.NONE)
        {
        }

        public ReservationInput(Guid townInstanceId, Guid operationId, int mode, string mapId, string contentVersion,
            IReadOnlyList<ParticipantReservationInput> participants, string? battleId, string? bossId, int difficulty)
        {
            TownInstanceId = townInstanceId;
            OperationId    = operationId;
            Mode           = mode;
            MapId          = mapId ?? string.Empty;
            ContentVersion = contentVersion ?? string.Empty;
            Participants   = participants ?? Array.Empty<ParticipantReservationInput>();
            BattleId       = battleId ?? string.Empty;
            BossId         = bossId ?? string.Empty;
            Difficulty     = difficulty;
        }

        /// <summary> 형식 검증. 계정, 캐릭터, 같은 참가자의 물약 종류가 중복되면 요청 형식 오류다 </summary>
        public bool IsValid()
        {
            if ((TownInstanceId == Guid.Empty) || (OperationId == Guid.Empty))
            {
                return false;
            }
            if ((Mode != GameSessionCodes.Mode.NORMAL) && (Mode != GameSessionCodes.Mode.BOSS))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(MapId) || string.IsNullOrWhiteSpace(ContentVersion))
            {
                return false;
            }
            if (!IsContractValid())
            {
                return false;
            }
            if ((Participants.Count < MIN_PARTY_SIZE) || (Participants.Count > MAX_PARTY_SIZE))
            {
                return false;
            }
            if ((Participants.Select(participant => participant.AccountId).Distinct().Count() != Participants.Count)
                || (Participants.Select(participant => participant.CharacterId).Distinct().Count() != Participants.Count))
            {
                return false;
            }

            foreach (ParticipantReservationInput participant in Participants)
            {
                if ((participant.AccountId == Guid.Empty) || (participant.CharacterId == Guid.Empty)
                    || (participant.CharacterRevision < 0))
                {
                    return false;
                }
                if (participant.Potions.Any(potion => string.IsNullOrWhiteSpace(potion.DefinitionId)
                    || string.IsNullOrWhiteSpace(potion.BindingKind) || (potion.Quantity <= 0)))
                {
                    return false;
                }
                if (participant.Potions.Select(potion => (potion.DefinitionId, potion.BindingKind)).Distinct().Count()
                    != participant.Potions.Count)
                {
                    return false;
                }
            }

            return true;
        }

        // 보스전은 bossId와 난이도가 필수이고 일반전은 둘 다 없어야 한다. battleId는 선택이다
        private bool IsContractValid()
        {
            if ((BattleId.Length > MAX_CONTRACT_ID_LENGTH) || (BossId.Length > MAX_CONTRACT_ID_LENGTH)
                || ((BattleId.Length > 0) && string.IsNullOrWhiteSpace(BattleId)))
            {
                return false;
            }
            if (Mode == GameSessionCodes.Mode.BOSS)
            {
                return !string.IsNullOrWhiteSpace(BossId) && ((Difficulty == GameSessionCodes.Difficulty.EASY)
                    || (Difficulty == GameSessionCodes.Difficulty.HARD));
            }

            return (BossId.Length == 0) && (Difficulty == GameSessionCodes.Difficulty.NONE);
        }

        /// <summary> 요청 해시용 본문. 참가자와 물약 순서는 요청 순서를 그대로 둔다.
        /// 출정 계약 필드는 값이 있을 때만 넣어 0012 전에 저장된 같은 요청의 해시가 바뀌지 않게 한다 </summary>
        public string ToRequestJson()
        {
            if ((BattleId.Length == 0) && (BossId.Length == 0) && (Difficulty == GameSessionCodes.Difficulty.NONE))
            {
                return SessionDb.ToRequestJson(RequestBody());
            }

            return SessionDb.ToRequestJson(new
            {
                body = RequestBody(),
                battleId = BattleId,
                bossId = BossId,
                difficulty = Difficulty
            });
        }

        private object RequestBody()
        {
            return new
            {
                mode = Mode,
                mapId = MapId,
                contentVersion = ContentVersion,
                participants = Participants.Select(participant => new
                {
                    accountId = participant.AccountId.ToString("D"),
                    characterId = participant.CharacterId.ToString("D"),
                    characterRevision = participant.CharacterRevision,
                    potions = participant.Potions.Select(potion => new
                    {
                        definitionId = potion.DefinitionId,
                        bindingKind = potion.BindingKind,
                        quantity = potion.Quantity
                    })
                })
            };
        }
    }

    /// <summary> 예약된 참가자의 영구 participantId </summary>
    public sealed class ReservedParticipantView
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid ParticipantId { get; set; }
        public Guid AccountId     { get; set; }
        public Guid CharacterId   { get; set; }
    }

    /// <summary> 예약된 출정과 배정 던전. 재전송 응답도 같은 세션을 다시 읽어 만든다 </summary>
    public sealed class ReservedSessionView
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid                                   SessionId            { get; }
        public Guid                                   InstanceId           { get; }
        public string                                 Address              { get; }
        public int                                    Port                 { get; }
        public string                                 State                { get; }
        public DateTimeOffset                         ExpiresAt            { get; }
        public bool                                   IsExpired            { get; }
        public Guid?                                  ReservedByInstanceId { get; }
        public IReadOnlyList<ReservedParticipantView> Participants         { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ReservedSessionView(Guid sessionId, Guid instanceId, string address, int port, string state,
            DateTimeOffset expiresAt, bool isExpired, Guid? reservedByInstanceId,
            IReadOnlyList<ReservedParticipantView> participants)
        {
            SessionId            = sessionId;
            InstanceId           = instanceId;
            Address              = address;
            Port                 = port;
            State                = state;
            ExpiresAt            = expiresAt.ToUniversalTime();
            IsExpired            = isExpired;
            ReservedByInstanceId = reservedByInstanceId;
            Participants         = participants;
        }
    }

    /// <summary> 예약 작업 결과. 확정된 경우에만 세션 정보가 있다 </summary>
    public sealed class ReservationOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public OperationResult      Result  { get; }
        public ReservedSessionView? Session { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ReservationOutcome(OperationResult result, ReservedSessionView? session)
        {
            ArgumentNullException.ThrowIfNull(result);
            Result  = result;
            Session = session;
        }
    }

    /// <summary> 참가자 전원의 입장권 발급 결과. 거절이면 입장권이 비어 있다 </summary>
    public sealed class JoinTicketsOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public int                       ReasonCode { get; }
        public IReadOnlyList<JoinTicket> Tickets    { get; }

        /****************************************
        *                Methods
        ****************************************/

        public JoinTicketsOutcome(int reasonCode, IReadOnlyList<JoinTicket> tickets)
        {
            ArgumentNullException.ThrowIfNull(tickets);
            ReasonCode = reasonCode;
            Tickets    = tickets;
        }
    }

    /// <summary> 접속 임대 이관 결과. IsRetryable이면 같은 newLeaseId로 다시 시도한다 </summary>
    public sealed class LeaseTransferOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public int            ReasonCode  { get; }
        public Guid           LeaseId     { get; }
        public DateTimeOffset ExpiresAt   { get; }
        public bool           IsRetryable { get; }

        /****************************************
        *                Methods
        ****************************************/

        private LeaseTransferOutcome(int reasonCode, Guid leaseId, DateTimeOffset expiresAt, bool isRetryable)
        {
            ReasonCode  = reasonCode;
            LeaseId     = leaseId;
            ExpiresAt   = expiresAt.ToUniversalTime();
            IsRetryable = isRetryable;
        }

        public static LeaseTransferOutcome Transferred(Guid leaseId, DateTimeOffset expiresAt)
        {
            return new LeaseTransferOutcome(ContractCodes.RejectReason.NONE, leaseId, expiresAt, false);
        }

        public static LeaseTransferOutcome Rejected(int reasonCode)
        {
            return new LeaseTransferOutcome(reasonCode, Guid.Empty, DateTimeOffset.UnixEpoch, false);
        }

        public static LeaseTransferOutcome Retry()
        {
            return new LeaseTransferOutcome(ContractCodes.RejectReason.NONE, Guid.Empty, DateTimeOffset.UnixEpoch, true);
        }
    }
}
