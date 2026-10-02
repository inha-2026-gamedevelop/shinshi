// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts;

namespace Shinshi.Data.Sessions
{
    /// <summary> 참가자별 일회용 던전 접속 토큰 발급. 원문은 저장하지 않고 해시만 저장하는 구현을 인증 작업이 제공한다 </summary>
    public interface IJoinTokenIssuer
    {
        /****************************************
        *                Methods
        ****************************************/

        // 예약 커밋 뒤 트랜잭션 밖에서 부른다. 같은 참가자에게 다시 발급하면 아직 교환하지 않은 이전 토큰을 폐기한다
        Task<JoinTicket> IssueDungeonTicketAsync(DungeonJoinGrant grant, CancellationToken cancellationToken);
    }

    /// <summary> 토큰에 묶을 출정, 참가자, 배정 인스턴스. 토큰은 이 인스턴스와 참가자 밖에서 쓸 수 없다 </summary>
    public sealed class DungeonJoinGrant
    {
        /****************************************
        *              Properties
        ****************************************/

        public Guid           SessionId            { get; }
        public Guid           ParticipantId        { get; }
        public Guid           AccountId            { get; }
        public Guid           CharacterId          { get; }
        public Guid           InstanceId           { get; }
        public string         Address              { get; }
        public int            Port                 { get; }
        public TimeSpan       TokenLifetime        { get; }
        public DateTimeOffset ReservationExpiresAt { get; }

        /****************************************
        *                Methods
        ****************************************/

        public DungeonJoinGrant(Guid sessionId, Guid participantId, Guid accountId, Guid characterId, Guid instanceId,
            string address, int port, TimeSpan tokenLifetime, DateTimeOffset reservationExpiresAt)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(address);
            if (tokenLifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(tokenLifetime));
            }

            SessionId            = sessionId;
            ParticipantId        = participantId;
            AccountId            = accountId;
            CharacterId          = characterId;
            InstanceId           = instanceId;
            Address              = address;
            Port                 = port;
            TokenLifetime        = tokenLifetime;
            ReservationExpiresAt = reservationExpiresAt.ToUniversalTime();
        }
    }
}
