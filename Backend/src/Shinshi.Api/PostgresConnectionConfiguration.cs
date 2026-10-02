// System
using System;

// Third-party
using Microsoft.Extensions.Configuration;

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

        /****************************************
        *                Methods
        ****************************************/

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
