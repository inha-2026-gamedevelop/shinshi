// System
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Data.Auctions;
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
        private readonly PostgresPartyAuctionRepository? _auctions;

        /****************************************
        *                Methods
        ****************************************/

        // 경매 저장소가 없으면(이전 구성) 경매 단계 복구를 건너뛴다
        public GameSessionMaintenanceWork(PostgresReservationRepository reservations,
            PostgresSettlementRepository settlement, PostgresAuthRepository auth,
            PostgresPartyAuctionRepository? auctions = null)
        {
            ArgumentNullException.ThrowIfNull(reservations);
            ArgumentNullException.ThrowIfNull(settlement);
            ArgumentNullException.ThrowIfNull(auth);
            _reservations = reservations;
            _settlement   = settlement;
            _auth         = auth;
            _auctions     = auctions;
        }

        public Task<int> ReleaseExpiredAsync(int maxSessions, CancellationToken cancellationToken)
        {
            return _reservations.ReleaseExpiredAsync(maxSessions, cancellationToken);
        }

        // 소실 출정 복구와 경매 단계 복구는 같은 생존 판정 보류(API 시작, DB 장애 직후) 안에서만 돈다
        public async Task<SessionRecoveryResult> RecoverLostSessionsAsync(int maxSessions, CancellationToken cancellationToken)
        {
            SessionRecoveryResult lost = await _settlement.RecoverLostSessionsAsync(maxSessions, cancellationToken);
            if (_auctions == null)
            {
                return lost;
            }

            SessionRecoveryResult auctions = await _auctions.RecoverAsync(maxSessions, cancellationToken);
            return new SessionRecoveryResult(lost.Settled + auctions.Settled, lost.Cancelled + auctions.Cancelled,
                lost.Failures.Concat(auctions.Failures).ToList());
        }

        public Task<int> PurgeExpiredJoinTokensAsync(int maxRows, CancellationToken cancellationToken)
        {
            return _auth.PurgeExpiredJoinTokensAsync(maxRows, cancellationToken);
        }
    }
}
