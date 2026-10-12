// System
using System;
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Data.Auth
{
    /// <summary> 한 번 소비한 참가자 접속 자격의 영구 ID 결합 </summary>
    public sealed class JoinAuthIdentity
    {
        /****************************************
        *              Properties
        ****************************************/

        public CharacterExperienceSnapshot? Experience { get; }
        public Guid AccountId { get; }
        public Guid CharacterId { get; }

        // 토큰이 발급된 캐릭터의 직업(ContractCodes.PlayerClass). 게임 서버는 클라이언트가 아니라 이 값으로 직업을 정한다
        public int ClassId { get; }
        public Guid ParticipantId { get; }
        public Guid SessionId { get; }
        public Guid InstanceId { get; }
        public long ServerTimeUnixMs { get; }

        // 마을 입장 교환이 같은 트랜잭션에서 얻은 계정 접속 임대. 던전 입장은 이관 경로가 임대를 옮기므로 빈 값이다
        public Guid AccountLeaseId { get; }

        // 캐릭터 표시 이름. 이름 컬럼이 생기기 전의 캐릭터면 null이다
        public string? CharacterName { get; }

        // 던전 입장, 재입장의 출정 계약. 마을 입장이면 null이다
        public JoinSortieContract? Sortie { get; }

        // 던전 입장, 재입장의 출정 예약 때 고정한 CharacterCombatLoadout JSON. 마을 입장이나 0011 전 출정이면 null이다
        public string? CombatLoadoutJson { get; }

        // 던전 입장, 재입장 때 이 참가자의 남은 물약 예약분(예약 수량 - 확정 사용). 마을 입장이면 null이다
        public IReadOnlyList<ReservationPotion>? Potions { get; }

        // DB 소유 캐릭터의 외형 원문(JSON), 명시적 레거시 캐릭터는 null이다. 토큰 소비 트랜잭션이 채운다
        public string? AppearanceJson { get; }

        /****************************************
        *                Methods
        ****************************************/

        public JoinAuthIdentity(Guid accountId, Guid characterId, int classId, Guid participantId, Guid sessionId,
            Guid instanceId, long serverTimeUnixMs, Guid accountLeaseId = default, string? characterName = null,
            JoinSortieContract? sortie = null, string? combatLoadoutJson = null,
            IReadOnlyList<ReservationPotion>? potions = null,
            string? appearanceJson = null, CharacterExperienceSnapshot? experience = null)
        {
            Experience       = experience;
            AccountId        = accountId;
            CharacterId      = characterId;
            ClassId          = classId;
            ParticipantId    = participantId;
            SessionId        = sessionId;
            InstanceId       = instanceId;
            ServerTimeUnixMs = serverTimeUnixMs;
            AccountLeaseId   = accountLeaseId;
            CharacterName    = characterName;
            Sortie           = sortie;
            CombatLoadoutJson = combatLoadoutJson;
            Potions          = potions;
            AppearanceJson   = appearanceJson;
        }
    }
}
