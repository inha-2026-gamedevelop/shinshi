// System
using System;
using System.Collections.Generic;

// Third-party
using Microsoft.Extensions.Configuration;
using Xunit;

// Project
using Shinshi.Backend;

namespace Shinshi.Backend.Tests
{
    /// <summary> 스키마 변경 연결이 없을 때 API 연결로 권한 경계를 우회하지 못한다 </summary>
    public sealed class MigratorConfigurationTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string RUNTIME_CONNECTION  = "runtime-connection-placeholder";
        private const string MIGRATOR_CONNECTION = "migrator-connection-placeholder";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 두 연결이 모두 제공되어도 실행 모드에 지정된 연결만 반환한다 </summary>
        [Theory]
        [InlineData(false, RUNTIME_CONNECTION)]
        [InlineData(true, MIGRATOR_CONNECTION)]
        public void GetRequiredConnectionString_BothConnectionsConfigured_SelectsModeConnection(
            bool migrate, string expected)
        {
            IConfiguration configuration = CreateConfiguration(RUNTIME_CONNECTION, MIGRATOR_CONNECTION);

            string selected = PostgresConnectionConfiguration.GetRequiredConnectionString(configuration, migrate);

            Assert.Equal(expected, selected);
        }

        /// <summary> 마이그레이션 연결이 비어 있으면 API 연결을 대체 사용하지 않는다 </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void GetRequiredConnectionString_MigratorMissing_RejectsRuntimeFallback(string? migratorConnection)
        {
            IConfiguration configuration = CreateConfiguration(RUNTIME_CONNECTION, migratorConnection);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                PostgresConnectionConfiguration.GetRequiredConnectionString(configuration, migrate: true));

            Assert.Equal("Set ConnectionStrings__PostgresMigrator in the environment.", error.Message);
            Assert.DoesNotContain(RUNTIME_CONNECTION, error.Message, StringComparison.Ordinal);
        }

        /// <summary> API 실행에도 스키마 변경 권한이 있는 연결을 대체 사용하지 않는다 </summary>
        [Fact]
        public void GetRequiredConnectionString_RuntimeMissing_RejectsMigratorFallback()
        {
            IConfiguration configuration = CreateConfiguration(null, MIGRATOR_CONNECTION);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                PostgresConnectionConfiguration.GetRequiredConnectionString(configuration, migrate: false));

            Assert.Equal("Set ConnectionStrings__Postgres in the environment.", error.Message);
            Assert.DoesNotContain(MIGRATOR_CONNECTION, error.Message, StringComparison.Ordinal);
        }

        private static IConfiguration CreateConfiguration(string? runtimeConnection, string? migratorConnection)
        {
            return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"]         = runtimeConnection,
                ["ConnectionStrings:PostgresMigrator"] = migratorConnection
            }).Build();
        }
    }
}
