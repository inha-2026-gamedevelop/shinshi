// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Shinshi.Backend.Health
{
    /// <summary> DB 연결과 SELECT 1로 API의 준비 상태를 확인한다 </summary>
    public sealed class PostgresReadinessProbe : IReadinessProbe
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IServiceProvider _services;

        /****************************************
        *                Methods
        ****************************************/

        public PostgresReadinessProbe(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);
            _services = services;
        }

        public async Task CheckAsync(CancellationToken cancellationToken)
        {
            // 연결 문자열이 없는 호스트에서도 live는 DB를 만들지 않고 응답해야 한다.
            // 준비 상태를 요청한 순간에만 데이터 소스를 가져온다.
            NpgsqlDataSource dataSource = _services.GetRequiredService<NpgsqlDataSource>();
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText    = "SELECT 1;";
            command.CommandTimeout = HealthController.READINESS_TIMEOUT_SECONDS;
            object? result = await command.ExecuteScalarAsync(cancellationToken);
            if (!(result is int value) || (value != 1))
            {
                throw new InvalidOperationException("Readiness probe returned an unexpected value.");
            }
        }
    }
}
