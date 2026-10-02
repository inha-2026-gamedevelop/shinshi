// System
using System;

namespace Shinshi.Data.Auth
{
    /// <summary> 한 번 소비한 참가자 접속 자격의 영구 ID 결합 </summary>
    public sealed class JoinAuthIdentity
    {
        /****************************************
        *              Properties
        ****************************************/

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

        /****************************************
        *                Methods
        ****************************************/

        public JoinAuthIdentity(Guid accountId, Guid characterId, int classId, Guid participantId, Guid sessionId,
            Guid instanceId, long serverTimeUnixMs, Guid accountLeaseId = default)
        {
            AccountId        = accountId;
            CharacterId      = characterId;
            ClassId          = classId;
            ParticipantId    = participantId;
            SessionId        = sessionId;
            InstanceId       = instanceId;
            ServerTimeUnixMs = serverTimeUnixMs;
            AccountLeaseId   = accountLeaseId;
        }
    }
}
