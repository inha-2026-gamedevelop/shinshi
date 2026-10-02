// System
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

// Third Party
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;
using Shinshi.Data.Admin;
using Shinshi.Data.Auth;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 실제 Program 파이프라인에 테스트 DB를 끼운 호스트. databaseServiceState면 연결 문자열 없이도 서비스 상태를 DB에서 읽는다 </summary>
    internal sealed class AdminApiTestHost : WebApplicationFactory<Program>
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int    PROTOCOL = 7;
        public const string CONTENT  = "admin-test-content";

        private readonly NpgsqlDataSource _dataSource;
        private readonly AuthTokenOptions _authOptions;
        private readonly AdminOptions _adminOptions;
        private readonly bool _databaseServiceState;
        private readonly Action<ServiceStateOptions>? _configureState;

        /****************************************
        *              Properties
        ****************************************/

        public AuthTokenOptions AuthOptions => _authOptions;
        public AdminOptions AdminOptions => _adminOptions;

        /****************************************
        *                Methods
        ****************************************/

        public AdminApiTestHost(NpgsqlDataSource dataSource, bool databaseServiceState,
            Action<ServiceStateOptions>? configureState = null)
        {
            _dataSource           = dataSource;
            _authOptions          = Auth.AuthTokenHasherTests.CreateOptions();
            _adminOptions         = AdminTestSupport.CreateOptions();
            _databaseServiceState = databaseServiceState;
            _configureState       = configureState;
        }

        public HttpClient CreateHttpsClient()
        {
            return CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        }

        /// <summary> 알려진 비밀번호와 TOTP 비밀로 운영자를 직접 넣는다(생성 API는 Owner 경로라 이 시험의 대상이 아니다) </summary>
        public async Task<(Guid AdminId, byte[] Secret)> CreateAdminAsync(string loginName, EAdminRole role)
        {
            byte[] secret = AdminTotp.CreateSecret();
            AdminNewOperator admin = AdminTestSupport.CreateOperator(_adminOptions.CreateSecretProtector(), loginName,
                role, secret, null);
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(
                """
                INSERT INTO admin_users (admin_id, login_name, password_hash, password_salt, password_iterations,
                    totp_secret_encrypted, role)
                VALUES (@AdminId, @LoginName, @Hash, @Salt, @Iterations, @Totp, @Role);
                """, new
                {
                    admin.AdminId, admin.LoginName, admin.Password.Hash, admin.Password.Salt,
                    admin.Password.Iterations, Totp = admin.TotpSecretEncrypted, Role = (int)role
                });
            return (admin.AdminId, secret);
        }

        public static async Task<string> LoginAsync(HttpClient client, string loginName, byte[] secret)
        {
            using HttpResponseMessage response = await client.PostAsync(AdminApiRoutes.AUTH_LOGIN, AdminTestSupport.Json(
                new AdminLoginRequest(loginName, AdminTestSupport.TEST_PASSWORD,
                    AdminTestSupport.CodeAt(secret, DateTimeOffset.UtcNow))));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await AdminTestSupport.ReadAsync<AdminLoginResponse>(response)).AccessToken;
        }

        public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string route,
            string? token, object? body = null)
        {
            using HttpRequestMessage request = new HttpRequestMessage(method, route)
            {
                Content = body == null ? null : AdminTestSupport.Json(body)
            };
            if (token != null)
            {
                request.Headers.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + token);
            }
            return await client.SendAsync(request);
        }

        public static ClientBuildInfo Build(string buildId)
        {
            return new ClientBuildInfo(buildId, PROTOCOL, CONTENT);
        }

        public static string NewOperationId() { return Guid.NewGuid().ToString("D"); }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<NpgsqlDataSource>();
                services.AddSingleton(_dataSource);
                services.PostConfigure<AuthTokenOptions>(options => options.HashKeyBase64 = _authOptions.HashKeyBase64);
                services.PostConfigure<AdminOptions>(options =>
                {
                    options.TotpKeyBase64      = _adminOptions.TotpKeyBase64;
                    options.PasswordIterations = _adminOptions.PasswordIterations;
                });
                services.PostConfigure<AuthBuildOptions>(options =>
                {
                    options.ProtocolVersion = PROTOCOL;
                    options.ContentHash     = CONTENT;
                });
                if (_configureState != null)
                {
                    services.PostConfigure(_configureState);
                }
                if (_databaseServiceState)
                {
                    // 호스트 구성에 연결 문자열을 넣지 않고 DB 모드 캐시만 바꿔 끼운다. AuthBuildPolicy와 기능 게이트가 이 인스턴스를 받는다
                    services.RemoveAll<ServiceStateStore>();
                    services.AddSingleton(provider => new ServiceStateStore(
                        provider.GetRequiredService<IOptionsMonitor<ServiceStateOptions>>(),
                        provider.GetRequiredService<TimeProvider>(),
                        provider.GetRequiredService<ILogger<ServiceStateStore>>(),
                        provider.GetRequiredService<PostgresServiceStateRepository>()));
                }
                services.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>();
            });
        }
    }
}
