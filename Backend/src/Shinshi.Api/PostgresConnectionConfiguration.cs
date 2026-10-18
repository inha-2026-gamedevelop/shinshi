// System
using System;

// Third-party
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Shinshi.Backend
{
    /// <summary> API 실행과 스키마 변경에 각각 지정된 연결만 사용해 DB 권한을 분리한다 </summary>
    public static class PostgresConnectionConfiguration
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string RUNTIME_CONNECTION_NAME  = "Postgres";
        private const string MIGRATOR_CONNECTION_NAME = "PostgresMigrator";
        private const string MAX_POOL_SIZE_KEY        = "Postgres:MaxPoolSize";

        // Npgsql 기본값은 100이고 PostgreSQL 기본 max_connections도 100이라(관리자용 예약 3개 포함) API 한 개가 풀을 다 채우면
        // 마이그레이터, 관리 도구, 다른 API 복제본이 연결을 못 얻는다. 풀을 30으로 낮추면 복제본 3개까지 기본 설정에서도
        // 100을 넘지 않는다. 한 요청이 트랜잭션 하나에 연결 하나를 쓰고 대부분 수 ms 안에 끝나므로 복제본당 30이면
        // 동시 요청 수십 개를 감당한다. DB 설정이 다르면 Postgres:MaxPoolSize(환경 변수 Postgres__MaxPoolSize)로 바꾼다
        public const int DEFAULT_MAX_POOL_SIZE = 30;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 연결 문자열에 풀 크기가 없으면 설정값(없으면 기본값)을 적용한다. 설정 키가 있으면 연결 문자열보다 우선한다 </summary>
        public static string ApplyPoolSize(string connectionString, IConfiguration configuration)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            ArgumentNullException.ThrowIfNull(configuration);

            NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder(connectionString);
            string? configured = configuration[MAX_POOL_SIZE_KEY];
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (!int.TryParse(configured, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int size) || (size < 1))
                {
                    throw new InvalidOperationException($"{MAX_POOL_SIZE_KEY} must be a positive integer.");
                }

                builder.MaxPoolSize = size;
            }
            else if (!HasExplicitPoolSize(connectionString))
            {
                builder.MaxPoolSize = DEFAULT_MAX_POOL_SIZE;
            }

            return builder.ConnectionString;
        }

        // NpgsqlConnectionStringBuilder는 기본값이 있는 키도 포함한다고 답하므로 원문 키로 확인한다
        private static bool HasExplicitPoolSize(string connectionString)
        {
            System.Data.Common.DbConnectionStringBuilder raw = new System.Data.Common.DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };
            return raw.ContainsKey("Maximum Pool Size") || raw.ContainsKey("MaxPoolSize");
        }

        /// <summary> 선택한 연결이 없으면 다른 계정으로 대체하지 않고 설정 키만 포함한 오류를 반환한다 </summary>
        public static string GetRequiredConnectionString(IConfiguration configuration, bool migrate)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            string name = migrate ? MIGRATOR_CONNECTION_NAME : RUNTIME_CONNECTION_NAME;
            string? connectionString = configuration.GetConnectionString(name);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"Set ConnectionStrings__{name} in the environment.");
            }

            return connectionString;
        }
    }
}
