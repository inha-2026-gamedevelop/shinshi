// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

// Project
using Shinshi.Data;
using Shinshi.Data.Social;

namespace Shinshi.Backend.Social
{
    /// <summary> 유지 작업 한 주기가 부르는 DB 작업. 시험에서 DB 단절을 흉내 낼 수 있게 서비스와 분리한다 </summary>
    public interface ISocialMaintenanceWork
    {
        /****************************************
        *                Methods
        ****************************************/

        Task<SocialPurgeResult> PurgeExpiredAsync(int batchSize, CancellationToken cancellationToken);
    }

    /// <summary> 소셜 저장소에 그대로 넘기는 기본 구현 </summary>
    public sealed class SocialMaintenanceWork : ISocialMaintenanceWork
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PostgresSocialRepository _social;

        /****************************************
        *                Methods
        ****************************************/

        public SocialMaintenanceWork(PostgresSocialRepository social)
        {
            ArgumentNullException.ThrowIfNull(social);
            _social = social;
        }

        public Task<SocialPurgeResult> PurgeExpiredAsync(int batchSize, CancellationToken cancellationToken)
        {
            return _social.PurgeExpiredAsync(batchSize, cancellationToken);
        }
    }

    /// <summary> 60초마다 만료된 친구 요청, 길드 신청과 초대, 전달함 행을 지운다. 검사는 만료 행을 이미 없는 것으로 보므로
    /// 정리가 늦거나 여러 API 인스턴스가 함께 돌아도 결과가 바뀌지 않는다(표 크기만 줄인다) </summary>
    public sealed class SocialMaintenanceService : BackgroundService
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int INTERVAL_SECONDS = 60;
        public const int BATCH_SIZE       = 1000;

        private readonly ISocialMaintenanceWork _work;
        private readonly ILogger<SocialMaintenanceService> _logger;
        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public SocialMaintenanceService(ISocialMaintenanceWork work, ILogger<SocialMaintenanceService> logger,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(work);
            ArgumentNullException.ThrowIfNull(logger);
            ArgumentNullException.ThrowIfNull(timeProvider);
            _work         = work;
            _logger       = logger;
            _timeProvider = timeProvider;
        }

        /// <summary> 한 주기 실행. 실패해도 다음 주기에 다시 지운다 </summary>
        public async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            try
            {
                SocialPurgeResult result = await _work.PurgeExpiredAsync(BATCH_SIZE, cancellationToken);
                if (result.Total > 0)
                {
                    _logger.LogInformation(
                        "Social cleanup removed {FriendRequests} friend requests, {GuildRequests} guild requests, {Deliveries} deliveries.",
                        result.FriendRequests, result.GuildRequests, result.Deliveries);
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // DB 예외 메시지에 연결 정보가 섞일 수 있어 종류와 SQLSTATE만 남긴다
                string sqlState = (exception as PostgresException)?.SqlState ?? string.Empty;
                if (PostgresTransactionPolicy.IsConnectionFailure(exception))
                {
                    _logger.LogWarning("Social cleanup could not reach the database ({ExceptionType}, {SqlState}).",
                        exception.GetType().Name, sqlState);
                    return;
                }

                _logger.LogError("Social cleanup failed ({ExceptionType}, {SqlState}).", exception.GetType().Name, sqlState);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(INTERVAL_SECONDS), _timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
    }
}
