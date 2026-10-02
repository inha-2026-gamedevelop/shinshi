// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Npgsql;

// Project
using Shinshi.Backend.Accounts;
using Shinshi.Backend.Tests.Admin;
using Shinshi.Backend.Tests.Auth;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Data.Accounts;
using Shinshi.Data.Auth;
using Shinshi.Data.Characters;
using Shinshi.Data.Sessions;

namespace Shinshi.Backend.Tests.Accounts
{
    /// <summary> 실제 해시기를 감싸 일반 비교, 더미 비교, 해시 호출 수를 센다 </summary>
    internal sealed class CountingPasswordHasher : IAccountPasswordHasher
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly AccountPasswordHasher _inner;
        private int _verifyCalls;
        private int _dummyCalls;
        private int _hashCalls;
        private int _derivations;
        private int _lastDerivedIterations;

        /****************************************
        *              Properties
        ****************************************/

        public int VerifyCalls => Volatile.Read(ref _verifyCalls);
        public int DummyCalls => Volatile.Read(ref _dummyCalls);
        public int HashCalls => Volatile.Read(ref _hashCalls);

        // 실제 PBKDF2 계산 수와 마지막 계산의 회수. 메서드 호출이 아니라 계산이 일어났는지 본다
        public int Derivations => Volatile.Read(ref _derivations);
        public int LastDerivedIterations => Volatile.Read(ref _lastDerivedIterations);
        public int Iterations => _inner.Iterations;

        /****************************************
        *                Methods
        ****************************************/

        public CountingPasswordHasher(int iterations = AccountPasswordHasherTests.TEST_ITERATIONS)
        {
            _inner = new AccountPasswordHasher(iterations, derived =>
            {
                Interlocked.Increment(ref _derivations);
                Volatile.Write(ref _lastDerivedIterations, derived);
            });
        }

        public bool NeedsRehash(string encodedHash)
        {
            return _inner.NeedsRehash(encodedHash);
        }

        public string Hash(string password)
        {
            Interlocked.Increment(ref _hashCalls);
            return _inner.Hash(password);
        }

        public bool Verify(string? password, string encodedHash)
        {
            Interlocked.Increment(ref _verifyCalls);
            return _inner.Verify(password, encodedHash);
        }

        public bool VerifyDummy(string? password)
        {
            Interlocked.Increment(ref _dummyCalls);
            return _inner.VerifyDummy(password);
        }
    }

    /// <summary> 계정, 자격, 접근 토큰을 메모리에 둔 대역. 토큰은 실제 해셔로 만들어 미들웨어의 태그 검사를 통과한다 </summary>
    internal sealed class FakeAccountBackend : IAuthRepository, IAccountRepository
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ORIGIN_INVITE   = "invite";
        private const string ORIGIN_PASSWORD = "password";

        private readonly AuthTokenHasher _tokens = new AuthTokenHasher(AuthTokenHasherTests.CreateOptions());
        private readonly object _gate = new object();
        private readonly Dictionary<Guid, AccountRow> _accounts = new Dictionary<Guid, AccountRow>();
        private readonly Dictionary<string, TokenRow> _issued = new Dictionary<string, TokenRow>(StringComparer.Ordinal);

        /****************************************
        *              Properties
        ****************************************/

        public int IssueLoginCalls { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 초대 코드로 만든 것과 같은 자격 없는 계정과 그 접근 토큰 </summary>
        public (Guid AccountId, string AccessToken) AddInviteAccount()
        {
            lock (_gate)
            {
                AccountRow row = new AccountRow(Guid.NewGuid());
                _accounts[row.AccountId] = row;
                return (row.AccountId, IssueToken(row.AccountId, false, ORIGIN_INVITE));
            }
        }

        public string AddGameSession(Guid accountId)
        {
            lock (_gate)
            {
                return IssueToken(accountId, true, ORIGIN_INVITE);
            }
        }

        public string? EmailOf(string loginId)
        {
            lock (_gate)
            {
                return _accounts.Values.SingleOrDefault(row => row.LoginId == loginId)?.Email;
            }
        }

        public int CountWithEmailKey(string emailKey)
        {
            lock (_gate)
            {
                return _accounts.Values.Count(row => row.EmailKey == emailKey);
            }
        }

        public bool IsAuthenticToken(string token)
        {
            return _tokens.IsAuthentic(token);
        }

        public Task<AuthValidationOutcome<PlayerAuthIdentity>> ValidateAccessAsync(string accessToken,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Validate(accessToken, false));
        }

        public Task<AuthValidationOutcome<PlayerAuthIdentity>> ValidateGameSessionAsync(string gameSessionToken,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Validate(gameSessionToken, true));
        }

        public Task<AccountCredentialRecord?> FindCredentialByLoginIdAsync(string loginId,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                AccountRow? row = _accounts.Values.SingleOrDefault(account => account.LoginId == loginId);
                return Task.FromResult(ToCredential(row));
            }
        }

        public Task<AuthExchangeOutcome> IssuePasswordLoginAsync(Guid accountId, string verifiedPasswordHash,
            string? upgradedPasswordHash = null, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                ++IssueLoginCalls;
                if (!_accounts.TryGetValue(accountId, out AccountRow? row) || (row.PasswordHash != verifiedPasswordHash))
                {
                    return Task.FromResult(AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.INVALID_CREDENTIALS));
                }
                if (upgradedPasswordHash != null)
                {
                    row.PasswordHash = upgradedPasswordHash;
                }

                string access = IssueToken(accountId, false, ORIGIN_PASSWORD);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                return Task.FromResult(AuthExchangeOutcome.Success(new AuthExchangeResponse(accountId.ToString("D"),
                    access, now.AddMinutes(15).ToUnixTimeMilliseconds(), _tokens.CreateToken(),
                    now.AddHours(24).ToUnixTimeMilliseconds(), now.ToUnixTimeMilliseconds())));
            }
        }

        public Task<AccountSignupOutcome> CreateAccountAsync(string loginId, string passwordHash, string email,
            string emailKey, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_accounts.Values.Any(row => row.LoginId == loginId))
                {
                    return Task.FromResult(AccountSignupOutcome.Rejected(ContractCodes.RejectReason.LOGIN_ID_TAKEN,
                        DateTimeOffset.UtcNow));
                }

                AccountRow created = new AccountRow(Guid.NewGuid())
                {
                    LoginId = loginId, PasswordHash = passwordHash, Email = email, EmailKey = emailKey
                };
                _accounts[created.AccountId] = created;
                return Task.FromResult(AccountSignupOutcome.Success(created.AccountId, DateTimeOffset.UtcNow));
            }
        }

        public Task<bool> IsLoginIdTakenAsync(string loginId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(_accounts.Values.Any(row => row.LoginId == loginId));
            }
        }

        public Task<AccountProfileRecord?> GetProfileAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(_accounts.TryGetValue(accountId, out AccountRow? row)
                    ? new AccountProfileRecord(row.AccountId, row.LoginId, row.Email, false, row.CreatedAt)
                    : null);
            }
        }

        public Task<AccountCredentialRecord?> FindCredentialByAccountAsync(Guid accountId,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _accounts.TryGetValue(accountId, out AccountRow? row);
                return Task.FromResult(ToCredential(row));
            }
        }

        public Task<ECredentialUpdateStatus> ChangePasswordAsync(Guid accountId, string expectedPasswordHash,
            string newPasswordHash, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                AccountRow row = _accounts[accountId];
                if (row.PasswordHash != expectedPasswordHash)
                {
                    return Task.FromResult(ECredentialUpdateStatus.Conflict);
                }

                row.PasswordHash = newPasswordHash;
                RevokeAll(accountId);
                return Task.FromResult(ECredentialUpdateStatus.Updated);
            }
        }

        public Task<ECredentialUpdateStatus> RecoverCredentialsAsync(Guid accountId, Guid familyId, string? loginId,
            string passwordHash, string? email, string? emailKey, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                TokenRow? family = _issued.Values.FirstOrDefault(token => (token.AccountId == accountId)
                    && (token.FamilyId == familyId));
                if ((family == null) || family.Revoked || (family.Origin != ORIGIN_INVITE)
                    || (DateTimeOffset.UtcNow - family.CreatedAt >= TimeSpan.FromMinutes(
                        PostgresAuthRepository.RECOVERY_WINDOW_MINUTES)))
                {
                    return Task.FromResult(ECredentialUpdateStatus.RecoveryNotAllowed);
                }

                AccountRow row = _accounts[accountId];
                if (row.LoginId == null)
                {
                    if (_accounts.Values.Any(account => account.LoginId == loginId))
                    {
                        return Task.FromResult(ECredentialUpdateStatus.LoginIdTaken);
                    }
                    row.LoginId = loginId;
                }

                row.PasswordHash = passwordHash;
                row.Email        = email ?? row.Email;
                row.EmailKey     = emailKey ?? row.EmailKey;
                RevokeAll(accountId);
                return Task.FromResult(ECredentialUpdateStatus.Updated);
            }
        }

        /// <summary> 비밀번호로 로그인할 수 있는 계정을 바로 만든다. 낮은 회수의 저장 해시를 시험할 때 쓴다 </summary>
        public Guid AddCredentialAccount(string loginId, string passwordHash, string email)
        {
            lock (_gate)
            {
                AccountRow row = new AccountRow(Guid.NewGuid())
                {
                    LoginId = loginId, PasswordHash = passwordHash, Email = email, EmailKey = email.ToLowerInvariant()
                };
                _accounts[row.AccountId] = row;
                return row.AccountId;
            }
        }

        public string? PasswordHashOf(string loginId)
        {
            lock (_gate)
            {
                return _accounts.Values.SingleOrDefault(row => row.LoginId == loginId)?.PasswordHash;
            }
        }

        /// <summary> 그 계정의 모든 로그인 묶음 시작 시각을 과거로 옮긴다 </summary>
        public void AgeFamilies(Guid accountId, TimeSpan age)
        {
            lock (_gate)
            {
                foreach (TokenRow row in _issued.Values.Where(row => row.AccountId == accountId))
                {
                    row.CreatedAt = row.CreatedAt.Subtract(age);
                }
            }
        }

        /// <summary> 복구 코드(초대) 로그인과 같은 시작 수단 invite의 접근 토큰 </summary>
        public string AddInviteLogin(Guid accountId)
        {
            lock (_gate)
            {
                return IssueToken(accountId, false, ORIGIN_INVITE);
            }
        }

        public Task<AuthExchangeOutcome> ExchangeInviteAsync(string inviteCode, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.INVALID_INVITE_CODE));
        }

        public Task<AuthExchangeOutcome> RotateRefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(AuthExchangeOutcome.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED));
        }

        public Task<AuthValidationOutcome<ServerAuthIdentity>> ValidateServerLeaseAsync(string serverLeaseToken,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(AuthValidationOutcome<ServerAuthIdentity>.Rejected(
                ContractCodes.RejectReason.TOKEN_REVOKED));
        }

        public Task<AuthValidationOutcome<JoinAuthIdentity>> ConsumeJoinTokenAsync(string joinToken, Guid instanceId,
            int serverRole, Guid serverLeaseId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AuthValidationOutcome<IssuedAuthToken>> IssueDungeonJoinTokenAsync(ServerAuthIdentity server,
            DungeonJoinGrant grant, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        private AuthValidationOutcome<PlayerAuthIdentity> Validate(string token, bool gameSession)
        {
            lock (_gate)
            {
                if (!_issued.TryGetValue(token, out TokenRow? row) || row.Revoked || (row.GameSession != gameSession))
                {
                    return AuthValidationOutcome<PlayerAuthIdentity>.Rejected(ContractCodes.RejectReason.TOKEN_REVOKED);
                }

                return AuthValidationOutcome<PlayerAuthIdentity>.Success(
                    new PlayerAuthIdentity(row.AccountId, row.FamilyId, row.FamilyId));
            }
        }

        private string IssueToken(Guid accountId, bool gameSession, string origin)
        {
            string token = _tokens.CreateToken();
            _issued[token] = new TokenRow(accountId, Guid.NewGuid(), gameSession, origin);
            return token;
        }

        private void RevokeAll(Guid accountId)
        {
            foreach (TokenRow row in _issued.Values.Where(row => row.AccountId == accountId))
            {
                row.Revoked = true;
            }
        }

        private static AccountCredentialRecord? ToCredential(AccountRow? row)
        {
            return (row?.LoginId == null) || (row.PasswordHash == null)
                ? null : new AccountCredentialRecord(row.AccountId, row.LoginId, row.PasswordHash);
        }

        /****************************************
        *                 Rows
        ****************************************/

        private sealed class AccountRow
        {
            public Guid AccountId { get; }
            public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
            public string? LoginId { get; set; }
            public string? PasswordHash { get; set; }
            public string? Email { get; set; }
            public string? EmailKey { get; set; }

            public AccountRow(Guid accountId)
            {
                AccountId = accountId;
            }
        }

        private sealed class TokenRow
        {
            public Guid AccountId { get; }
            public Guid FamilyId { get; }
            public bool GameSession { get; }
            public string Origin { get; }
            public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
            public bool Revoked { get; set; }

            public TokenRow(Guid accountId, Guid familyId, bool gameSession, string origin)
            {
                AccountId   = accountId;
                FamilyId    = familyId;
                GameSession = gameSession;
                Origin      = origin;
            }
        }
    }

    /// <summary> 이름 비교 키 집합만 둔 캐릭터 저장소 대역 </summary>
    internal sealed class FakeCharacterRepository : ICharacterRepository
    {
        /****************************************
        *              Properties
        ****************************************/

        public HashSet<string> TakenKeys { get; } = new HashSet<string>(StringComparer.Ordinal);
        public int CreateCalls { get; private set; }
        public Guid? ListedAccountId { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        public Task<CharacterCreateOutcome> CreateAsync(Guid accountId, Guid operationId, string name, string nameKey,
            int classId, string appearanceJson, string requestJson, CancellationToken cancellationToken = default)
        {
            ++CreateCalls;
            throw new NotSupportedException("HTTP tests only check validation before the repository.");
        }

        public Task<bool> IsNameTakenAsync(string nameKey, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(TakenKeys.Contains(nameKey));
        }

        public Task<IReadOnlyList<CharacterRecord>> ListAsync(Guid accountId, CancellationToken cancellationToken = default)
        {
            ListedAccountId = accountId;
            return Task.FromResult<IReadOnlyList<CharacterRecord>>(Array.Empty<CharacterRecord>());
        }
    }

    /// <summary> 계정 경로 HTTP 테스트 호스트. 원격 IP는 AdminTestSupport.REMOTE_IP_HEADER로 바꾼다 </summary>
    internal sealed class AccountApiHost : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string BUILD = "account-build";
        public const string CONTENT = "account-content";
        public const int PROTOCOL = 7;

        private readonly IHost _host;
        private readonly NpgsqlDataSource _unreachableSource;

        /****************************************
        *              Properties
        ****************************************/

        public FakeAccountBackend Backend { get; } = new FakeAccountBackend();
        public FakeCharacterRepository Characters { get; } = new FakeCharacterRepository();
        public CountingPasswordHasher Hasher { get; }
        public RecordingLoggerProvider Logs { get; } = new RecordingLoggerProvider();
        public IServiceProvider Services => _host.Services;

        /****************************************
        *                Methods
        ****************************************/

        public AccountApiHost(bool maintenance = false, PasswordHashGate? gate = null,
            int iterations = AccountPasswordHasherTests.TEST_ITERATIONS)
        {
            Hasher = new CountingPasswordHasher(iterations);

            // 입장권 컨트롤러가 받는 실제 저장소. 닫힌 포트를 가리키므로 토큰 종류 검사 전에 DB를 부르면 테스트가 실패한다
            _unreachableSource = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Timeout=1");
            PostgresAuthRepository unreachable = new PostgresAuthRepository(_unreachableSource,
                AuthTokenHasherTests.CreateOptions());
            _host = new HostBuilder().ConfigureWebHost(builder =>
            {
                builder.UseTestServer();
                builder.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(LogLevel.Trace);
                    logging.AddProvider(Logs);
                });
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, TestRemoteIpStartupFilter>();
                    services.AddShinshiAuth(new ConfigurationBuilder().Build());
                    services.AddSingleton<IAuthRepository>(Backend);
                    services.AddSingleton<IAccountRepository>(Backend);
                    services.AddSingleton<ICharacterRepository>(Characters);
                    services.AddSingleton<IAccountPasswordHasher>(Hasher);
                    services.AddSingleton(unreachable);
                    if (gate != null)
                    {
                        services.AddSingleton(gate);
                    }
                    services.PostConfigure<AuthBuildOptions>(options =>
                    {
                        options.ProtocolVersion = PROTOCOL;
                        options.ContentHash     = CONTENT;
                    });
                    services.PostConfigure<ServiceStateOptions>(options =>
                    {
                        options.Maintenance     = maintenance;
                        options.AllowedBuildIds = new[] { BUILD };
                    });
                    services.AddControllers().AddApplicationPart(typeof(AccountsController).Assembly)
                        .AddApplicationPart(typeof(AuthActorProbeController).Assembly)
                        .AddNewtonsoftJson(options => ContractJson.Configure(options.SerializerSettings));
                });
                builder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseShinshiAuthentication();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            }).Start();
        }

        public HttpClient CreateClient(string? bearer = null, string ip = AdminTestSupport.LOOPBACK)
        {
            HttpClient client = _host.GetTestClient();
            client.DefaultRequestHeaders.Add(AdminTestSupport.REMOTE_IP_HEADER, ip);
            if (bearer != null)
            {
                client.DefaultRequestHeaders.Add(ApiRoutes.AUTHORIZATION_HEADER, ApiRoutes.BEARER_PREFIX + bearer);
            }

            return client;
        }

        public static ClientBuildInfo Build(string build = BUILD)
        {
            return new ClientBuildInfo(build, PROTOCOL, CONTENT);
        }

        public static StringContent Json(object value)
        {
            return new StringContent(JsonConvert.SerializeObject(value, ContractJson.CreateSettings()), Encoding.UTF8,
                "application/json");
        }

        public static T Read<T>(string body)
        {
            return JsonConvert.DeserializeObject<T>(body, ContractJson.CreateSettings())!;
        }

        public void Dispose()
        {
            _host.Dispose();
            _unreachableSource.Dispose();
        }
    }
}
