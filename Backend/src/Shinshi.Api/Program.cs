// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Backend.Growth;
using Shinshi.Backend.Health;
using Shinshi.Backend.Life;
using Shinshi.Backend.Observability;
using Shinshi.Backend.Sessions;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Ledger;
using Shinshi.Data.Sessions;
using Shinshi.Data.Settlement;

namespace Shinshi.Backend
{
    public sealed class Program
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MIGRATE_ARGUMENT         = "--migrate";
        private const int    MIGRATION_ARGUMENT_COUNT = 1;
        private const int    EXIT_SUCCESS             = 0;
        private const int    EXIT_FAILURE             = 1;

        /****************************************
        *                Methods
        ****************************************/

        public static async Task<int> Main(string[] args)
        {
            bool migrate = (args.Length == MIGRATION_ARGUMENT_COUNT)
                && (args[0] == MIGRATE_ARGUMENT);
            bool bootstrapOwner = (args.Length == MIGRATION_ARGUMENT_COUNT)
                && (args[0] == AdminBootstrapCommand.ARGUMENT);
            WebApplicationBuilder builder = WebApplication.CreateBuilder(
                (migrate || bootstrapOwner) ? Array.Empty<string>() : args);
            if (migrate)
            {
                return await ApplyMigrationsAsync(builder.Configuration);
            }
            if (bootstrapOwner)
            {
                return await AdminBootstrapCommand.RunAsync(builder.Configuration, Console.Out, Console.Error);
            }

            builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
            builder.Services.AddSingleton<IReadinessProbe, PostgresReadinessProbe>();
            builder.Services.Configure<ServiceStateOptions>(
                builder.Configuration.GetSection(ServiceStateOptions.SECTION_NAME));
            builder.Services.AddSingleton<BackendUnavailableExceptionFilter>();
            builder.Services.AddControllers(options => options.Filters.AddService<BackendUnavailableExceptionFilter>())
                .AddNewtonsoftJson(options => ContractJson.Configure(options.SerializerSettings));

            // DB가 필요 없는 공개 상태 조회는 연결 문자열 없이도 실행할 수 있다
            builder.Services.AddSingleton<NpgsqlDataSource>(services =>
            {
                IConfiguration configuration = services.GetRequiredService<IConfiguration>();
                string connectionString = PostgresConnectionConfiguration.GetRequiredConnectionString(
                    configuration, migrate: false);
                return NpgsqlDataSource.Create(connectionString);
            });
            builder.Services.AddSingleton<IOperationStore, PostgresOperationStore>();
            builder.Services.AddSingleton<IdempotentOperationExecutor>();
            builder.Services.AddSingleton<PostgresAccountLeaseRepository>();
            builder.Services.AddShinshiAuth(builder.Configuration);
            builder.Services.AddShinshiLedger();
            builder.Services.AddSingleton<IEconomyLedgerEntryWriter>(services =>
                services.GetRequiredService<PostgresEconomyLedgerWriter>());
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<IServerCallerAuthenticator, ServerCallerAuthenticator>();
            builder.Services.AddSingleton<IJoinTokenIssuer, JoinTokenIssuer>();
            builder.Services.AddSingleton<IEconomyLedgerWriter, LedgerWriter>();
            builder.Services.AddShinshiGameSessions();
            builder.Services.AddShinshiLife();
            builder.Services.AddShinshiGrowth(builder.Configuration);
            builder.Services.AddShinshiObservability(builder.Configuration,
                !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Postgres")));
            builder.Services.AddShinshiAdmin(builder.Configuration);
            if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Postgres")))
            {
                builder.Services.AddShinshiGameSessionMaintenance();
                builder.Services.AddShinshiEconomyReconciliation();
            }

            await using WebApplication app = builder.Build();
            app.UseShinshiForwardedHeaders(app.Configuration);
            app.UseShinshiRequestLogging();
            app.UseRouting();
            app.UseShinshiAdminAuthentication();
            app.UseShinshiAuthentication();
            app.MapControllers();
            await app.RunAsync();
            return EXIT_SUCCESS;
        }

        private static async Task<int> ApplyMigrationsAsync(IConfiguration configuration)
        {
            string connectionString;
            try
            {
                connectionString = PostgresConnectionConfiguration.GetRequiredConnectionString(
                    configuration, migrate: true);
            }
            catch (InvalidOperationException exception)
            {
                // 이 검증 오류에는 설정 키만 들어가며 연결 문자열은 포함하지 않는다
                Console.Error.WriteLine(exception.Message);
                return EXIT_FAILURE;
            }

            try
            {
                await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
                int applied = await new SqlMigrationRunner(dataSource).ApplyAsync(CancellationToken.None);
                Console.WriteLine($"Applied migrations: {applied}");
                return EXIT_SUCCESS;
            }
            catch (Exception exception)
            {
                // 연결 문자열이나 PostgreSQL 상세 오류에 비밀값이 들어갈 수 있다
                Console.Error.WriteLine($"Migration failed ({exception.GetType().Name}). No credentials were logged.");
                return EXIT_FAILURE;
            }
        }
    }
}
