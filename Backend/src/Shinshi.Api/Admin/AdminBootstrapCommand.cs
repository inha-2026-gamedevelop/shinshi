// System
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.Extensions.Configuration;
using Npgsql;

// Project
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> --bootstrap-owner 실행. admin_users가 비어 있을 때만 최초 Owner를 만들고 비밀번호와 TOTP 비밀을 표준 출력에 한 번만 쓴다 </summary>
    public static class AdminBootstrapCommand
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string ARGUMENT            = "--bootstrap-owner";
        public const string LOGIN_NAME_VARIABLE = "SHINSHI_BOOTSTRAP_OWNER";

        private const int EXIT_SUCCESS = 0;
        private const int EXIT_FAILURE = 1;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 런타임 연결 ConnectionStrings:Postgres와 Admin 설정, 환경 SHINSHI_BOOTSTRAP_OWNER를 읽는다. 오류 출력에 설정 값을 싣지 않는다 </summary>
        public static async Task<int> RunAsync(IConfiguration configuration, TextWriter output, TextWriter error,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(error);
            string connectionString;
            AdminOptions options;
            try
            {
                connectionString = PostgresConnectionConfiguration.GetRequiredConnectionString(configuration, migrate: false);
                options = configuration.GetSection(AdminOptions.SECTION_NAME).Get<AdminOptions>() ?? new AdminOptions();
            }
            catch (InvalidOperationException exception)
            {
                // 이 검증 오류에는 설정 키만 들어간다
                await error.WriteLineAsync(exception.Message);
                return EXIT_FAILURE;
            }

            await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
            return await RunAsync(dataSource, options, configuration[LOGIN_NAME_VARIABLE], output, error,
                cancellationToken);
        }

        public static async Task<int> RunAsync(NpgsqlDataSource dataSource, AdminOptions options, string? loginName,
            TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(error);
            if (!AdminLoginNames.TryNormalize(loginName, out string normalized))
            {
                await error.WriteLineAsync(
                    $"Set {LOGIN_NAME_VARIABLE} to a login name of 3-64 letters, digits, '.', '_' or '-'.");
                return EXIT_FAILURE;
            }

            AdminCredentialFactory factory;
            try
            {
                AdminSecuritySettings settings = options.CreateSecuritySettings();
                factory = new AdminCredentialFactory(new AdminPasswordHasher(settings.PasswordIterations),
                    options.CreateSecretProtector());
            }
            catch (InvalidOperationException exception)
            {
                await error.WriteLineAsync(exception.Message);
                return EXIT_FAILURE;
            }

            AdminIssuedCredentials issued = factory.Create(Guid.NewGuid(), normalized, EAdminRole.Owner, null);
            bool created;
            try
            {
                PostgresAdminUserRepository users = new PostgresAdminUserRepository(dataSource,
                    new PostgresAdminAuditWriter(dataSource));
                created = await users.BootstrapOwnerAsync(issued.Operator, cancellationToken);
            }
            catch (Exception exception)
            {
                // 연결 문자열이나 PostgreSQL 상세 오류에 비밀값이 들어갈 수 있다
                await error.WriteLineAsync($"Owner bootstrap failed ({exception.GetType().Name}). No credentials were logged.");
                return EXIT_FAILURE;
            }
            if (!created)
            {
                await error.WriteLineAsync("Admin users already exist. Bootstrap runs only on an empty admin_users table.");
                return EXIT_FAILURE;
            }

            // 원문 비밀은 여기서 한 번만 보인다. 서버는 해시와 암호문만 저장한다
            await output.WriteLineAsync($"adminId: {issued.Operator.AdminId:D}");
            await output.WriteLineAsync($"loginName: {issued.Operator.LoginName}");
            await output.WriteLineAsync($"password: {issued.Password}");
            await output.WriteLineAsync($"totpSecret: {issued.TotpSecretBase32}");
            await output.WriteLineAsync($"totpUri: {issued.TotpUri}");
            return EXIT_SUCCESS;
        }
    }
}
