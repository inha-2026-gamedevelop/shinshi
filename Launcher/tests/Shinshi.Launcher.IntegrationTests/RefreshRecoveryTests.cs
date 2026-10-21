// System
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json;
using Npgsql;
using Xunit;

// Project
using Shinshi.Backend;
using Shinshi.Backend.Tests;
using Shinshi.Contracts;
using Shinshi.Data.Auth;
using Shinshi.Launcher.Account;

namespace Shinshi.Launcher.IntegrationTests
{
    public sealed class RefreshRecoveryTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int KEY_BYTES = 32;
        private const int ROUNDS = 10;

        /****************************************
        *                Methods
        ****************************************/

        [PostgresFact]
        public async Task TryAutoLoginAsync_TwoClientsLoseCommittedResponses_TenRoundsRecover()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            AuthTokenOptions options = CreateOptions();
            using ApiFactory factory = new ApiFactory(database.DataSource, options);
            for (int round = 0; round < ROUNDS; ++round)
            {
                await Task.WhenAll(RecoverAsync(database, options, factory), RecoverAsync(database, options, factory));
            }
        }

        [PostgresFact]
        public async Task TryAutoLoginAsync_AllResponsesLostAndBothSidesRestart_RecoversPersistedRequest()
        {
            await using PostgresTestDatabase database = await PostgresTestDatabase.CreateAsync();
            AuthTokenOptions options = CreateOptions();
            string path = Path.Combine(Path.GetTempPath(), "shinshi-refresh-" + Guid.NewGuid().ToString("N"), "refresh.bin");
            try
            {
                AuthExchangeResponse initial = await SeedLoginAsync(database, options);
                RefreshTokenStore store = new RefreshTokenStore(path);
                Assert.True(store.TrySave(initial.RefreshToken));
                string expectedRefresh;
                string expectedAccess;
                using (ApiFactory firstServer = new ApiFactory(database.DataSource, options))
                using (DropResponseHandler loss = new DropResponseHandler(firstServer.Server.CreateHandler(), int.MaxValue))
                using (HttpClient http = new HttpClient(loss))
                using (LauncherSession first = new LauncherSession(new AuthApiClient(http, new Uri("http://localhost")), store))
                {
                    Assert.False((await first.TryAutoLoginAsync(CancellationToken.None)).IsSuccess);
                    Assert.Equal(2, loss.Dropped);
                    expectedRefresh = loss.LastResponse!.RefreshToken;
                    expectedAccess = loss.LastResponse.AccessToken;
                }

                using ApiFactory restartedServer = new ApiFactory(database.DataSource, options);
                using HttpClient retryHttp = restartedServer.CreateClient();
                RefreshTokenStore restartedStore = new RefreshTokenStore(path);
                using LauncherSession restarted = new LauncherSession(new AuthApiClient(retryHttp, retryHttp.BaseAddress!), restartedStore);
                Assert.True((await restarted.TryAutoLoginAsync(CancellationToken.None)).IsSuccess);
                Assert.True(restartedStore.TryLoad(out string? recovered));
                Assert.Equal(expectedRefresh, recovered);
                Assert.True((await new PostgresAuthRepository(database.DataSource, options).ValidateAccessAsync(expectedAccess)).IsSuccess);
            }
            finally
            {
                if (Directory.Exists(Path.GetDirectoryName(path)))
                {
                    Directory.Delete(Path.GetDirectoryName(path)!, true);
                }
            }
        }

        private static async Task RecoverAsync(PostgresTestDatabase database, AuthTokenOptions options, ApiFactory factory)
        {
            AuthExchangeResponse initial = await SeedLoginAsync(database, options);
            string directory = Path.Combine(Path.GetTempPath(), "shinshi-refresh-" + Guid.NewGuid().ToString("N"));
            try
            {
                RefreshTokenStore store = new RefreshTokenStore(Path.Combine(directory, "refresh.bin"));
                Assert.True(store.TrySave(initial.RefreshToken));
                using DropResponseHandler loss = new DropResponseHandler(factory.Server.CreateHandler(), 1);
                using HttpClient http = new HttpClient(loss);
                using LauncherSession session = new LauncherSession(new AuthApiClient(http, new Uri("http://localhost")), store);
                Assert.True((await session.TryAutoLoginAsync(CancellationToken.None)).IsSuccess);
                Assert.Equal(1, loss.Dropped);
                Assert.Equal(ELauncherSessionState.LoggedIn, session.State);
                Assert.True(store.TryLoad(out string? saved));
                Assert.Equal(loss.LastResponse!.RefreshToken, saved);
                Assert.True((await new PostgresAuthRepository(database.DataSource, options)
                    .ValidateAccessAsync(loss.LastResponse.AccessToken)).IsSuccess);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        private static async Task<AuthExchangeResponse> SeedLoginAsync(PostgresTestDatabase database, AuthTokenOptions options)
        {
            Guid accountId = Guid.NewGuid();
            await using NpgsqlConnection connection = await database.DataSource.OpenConnectionAsync();
            await connection.ExecuteAsync("INSERT INTO accounts(account_id) VALUES (@AccountId);", new { AccountId = accountId });
            PostgresAuthRepository repository = new PostgresAuthRepository(database.DataSource, options);
            IssuedAuthToken invite = await repository.IssueInviteAsync(accountId);
            return (await repository.ExchangeInviteAsync(invite.Token)).Response!;
        }

        private static AuthTokenOptions CreateOptions()
        {
            return new AuthTokenOptions { HashKeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KEY_BYTES)) };
        }

        private sealed class ApiFactory : WebApplicationFactory<Shinshi.Backend.Program>
        {
            private readonly NpgsqlDataSource _source;
            private readonly AuthTokenOptions _options;

            public ApiFactory(NpgsqlDataSource source, AuthTokenOptions options)
            {
                _source = source;
                _options = options;
            }

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.UseContentRoot(Path.GetTempPath());
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<NpgsqlDataSource>();
                    services.AddSingleton(_source);
                    services.PostConfigure<AuthTokenOptions>(options => options.HashKeyBase64 = _options.HashKeyBase64);
                    services.PostConfigure<ServiceStateOptions>(options => options.Maintenance = false);
                });
            }
        }

        private sealed class DropResponseHandler : DelegatingHandler
        {
            private readonly int _limit;
            public int Dropped { get; private set; }
            public AuthExchangeResponse? LastResponse { get; private set; }

            public DropResponseHandler(HttpMessageHandler inner, int limit) : base(inner) { _limit = limit; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode && (Dropped < _limit))
                {
                    LastResponse = JsonConvert.DeserializeObject<AuthExchangeResponse>(await response.Content.ReadAsStringAsync(cancellationToken),
                        ContractJson.CreateSettings());
                    ++Dropped;
                    response.Dispose();
                    throw new HttpRequestException(HttpRequestError.ResponseEnded, "Injected response loss after server commit.");
                }

                return response;
            }
        }
    }
}
