// System
using System;

// Third-party
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

// Project
using Shinshi.Data;
using Shinshi.Data.Auctions;
using Shinshi.Data.Growth;
using Shinshi.Data.Reservations;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend.Sessions
{
    /// <summary> 서버 풀, 출정 예약, 세션 이벤트, 정산 저장소 등록 </summary>
    public static class GameSessionServiceCollectionExtensions
    {
        /****************************************
        *                Methods
        ****************************************/

        // NpgsqlDataSource, IdempotentOperationExecutor, PostgresAccountLeaseRepository, TimeProvider는 Program이 등록한다.
        // IServerCallerAuthenticator, IJoinTokenIssuer(인증 작업), IEconomyLedgerWriter(원장 작업)는 호출자가 따로 등록해야 한다
        public static IServiceCollection AddShinshiGameSessions(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddSingleton<PostgresServerPoolRepository>();
            // 예약의 해금 최종 판정에 성장 모듈(AddShinshiGrowth)의 SortieUnlockPolicy가 반드시 있어야 한다
            services.AddSingleton(provider => new PostgresReservationRepository(
                provider.GetRequiredService<NpgsqlDataSource>(), provider.GetRequiredService<IdempotentOperationExecutor>(),
                provider.GetRequiredService<PostgresAccountLeaseRepository>(), provider.GetRequiredService<IJoinTokenIssuer>(),
                provider.GetRequiredService<SortieUnlockPolicy>()));
            services.AddSingleton<PostgresGameSessionRepository>();
            // 성공 정산이 계정 진행을 기록하도록 성장 모듈(AddShinshiGrowth)의 PostgresAccountProgressRepository를 넘긴다
            services.AddSingleton(provider => new PostgresSettlementRepository(
                provider.GetRequiredService<NpgsqlDataSource>(), provider.GetRequiredService<IdempotentOperationExecutor>(),
                provider.GetRequiredService<IEconomyLedgerWriter>(),
                provider.GetRequiredService<PostgresAccountProgressRepository>()));
            services.AddSingleton<PostgresPartyAuctionRepository>();
            services.AddSingleton<PostgresReadinessRepository>();
            services.TryAddSingleton(provider => new RecoveryLivenessGate(provider.GetRequiredService<TimeProvider>()));
            return services;
        }

        /// <summary> 만료 해제, 소실 출정 복구, 만료 입장 토큰 정리 주기 작업. DB 연결이 있는 API 실행에만 등록한다.
        /// PostgresAuthRepository(AddShinshiAuth)가 먼저 등록돼 있어야 한다 </summary>
        public static IServiceCollection AddShinshiGameSessionMaintenance(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            services.AddSingleton<IGameSessionMaintenanceWork, GameSessionMaintenanceWork>();
            services.AddHostedService<GameSessionMaintenanceService>();
            return services;
        }
    }
}
