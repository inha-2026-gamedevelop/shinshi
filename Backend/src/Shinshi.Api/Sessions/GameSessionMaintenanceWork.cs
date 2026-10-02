// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Data.Auth;
using Shinshi.Data.Reservations;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Sessions
{
    /// <summary> 유지 작업 한 주기가 부르는 DB 작업. 시험에서 DB 단절을 흉내 낼 수 있게 서비스와 분리한다 </summary>
    public interface IGameSessionMaintenanceWork
    {
        /****************************************
        *                Methods
        ****************************************/

        Task<int> ReleaseExpiredAsync(int maxSessions, CancellationToken cancellationToken);
        Task<SessionRecoveryResult> RecoverLostSessionsAsync(int maxSessions, CancellationToken cancellationToken);
        Task<int> PurgeExpiredJoinTokensAsync(int maxRows, CancellationToken cancellationToken);
    }

    /// <summary> 예약, 정산, 인증 저장소에 그대로 넘기는 기본 구현 </summary>
    public sealed class GameSessionMaintenanceWork : IGameSessionMaintenanceWork
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresReservationRepository _reservations;
        private readonly PostgresSettlementRepository _settlement;
        private readonly PostgresAuthRepository _auth;

        /****************************************
        *                Methods
        ****************************************/

        public GameSessionMaintenanceWork(PostgresReservationRepository reservations,
            PostgresSettlementRepository settlement, PostgresAuthRepository auth)
        {
            ArgumentNullException.ThrowIfNull(reservations);
            ArgumentNullException.ThrowIfNull(settlement);
            ArgumentNullException.ThrowIfNull(auth);
            _reservations = reservations;
            _settlement   = settlement;
            _auth         = auth;
        }

        public Task<int> ReleaseExpiredAsync(int maxSessions, CancellationToken cancellationToken)
        {
            return _reservations.ReleaseExpiredAsync(maxSessions, cancellationToken);
        }

        public Task<SessionRecoveryResult> RecoverLostSessionsAsync(int maxSessions, CancellationToken cancellationToken)
        {
            return _settlement.RecoverLostSessionsAsync(maxSessions, cancellationToken);
        }

        public Task<int> PurgeExpiredJoinTokensAsync(int maxRows, CancellationToken cancellationToken)
        {
            return _auth.PurgeExpiredJoinTokensAsync(maxRows, cancellationToken);
        }
    }
}
