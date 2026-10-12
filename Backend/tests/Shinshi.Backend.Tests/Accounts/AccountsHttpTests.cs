// System
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

// Third Party
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Project
using Shinshi.Backend.Accounts;
using Shinshi.Contracts;

namespace Shinshi.Backend.Tests.Accounts
{
    /// <summary> 가입, 아이디 비밀번호 로그인, 실패 제한, 자격 변경의 HTTP 계약을 대역 저장소로 검사한다 </summary>
    public sealed class AccountsHttpTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string LOGIN_ID = "alice_01";
        private const string PASSWORD = "Sentinel9Secret";
        private const string NEW_PASSWORD = "Another7Secret";
        private const string EMAIL = "alice@example.com";
        private const string OWNER_IP = "198.51.100.20";
        private const string ATTACKER_IP = "203.0.113.7";

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task CharacterList_RequiresAuthenticationAndIgnoresRequestedAccountId()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient();
            using HttpResponseMessage anonymous = await client.GetAsync(ApiRoutes.CHARACTERS);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            Assert.Null(host.Characters.ListedAccountId);
            await SignupAsync(client, LOGIN_ID, PASSWORD, EMAIL);
            AuthExchangeResponse tokens = await LoginTokensAsync(client, LOGIN_ID, PASSWORD);
            using HttpClient authenticated = host.CreateClient(tokens.AccessToken);
            using HttpResponseMessage response = await authenticated.GetAsync(ApiRoutes.CHARACTERS + "?accountId=" + Guid.NewGuid());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(Guid.Parse(tokens.AccountId), host.Characters.ListedAccountId);
            Assert.True(response.Headers.CacheControl!.NoStore);
            Assert.Empty(AccountApiHost.Read<CharacterListResponse>(await response.Content.ReadAsStringAsync()).Characters);
        }

        [Fact]
        public async Task Signup_ValidRequest_Returns201WithLowercaseLoginIdAndNoTokens()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient();

            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.ACCOUNTS,
                AccountApiHost.Json(new AccountSignupRequest("Alice_01", PASSWORD, "  Alice@Example.com ")));

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
            string body = await response.Content.ReadAsStringAsync();
            AccountSignupResponse result = AccountApiHost.Read<AccountSignupResponse>(body);
            Assert.Equal(LOGIN_ID, result.LoginId);
            Assert.False(result.EmailVerified);
            Assert.True(Guid.TryParse(result.AccountId, out _));
            Assert.DoesNotContain(PASSWORD, body, StringComparison.Ordinal);
            Assert.DoesNotContain("accessToken", body, StringComparison.Ordinal);
            Assert.Equal("Alice@Example.com", host.Backend.EmailOf(LOGIN_ID));
            Assert.Equal(1, host.Hasher.HashCalls);
        }

        [Theory]
        [InlineData("abc", PASSWORD, EMAIL, ContractCodes.RejectReason.INVALID_LOGIN_ID)]
        [InlineData("alice-01", PASSWORD, EMAIL, ContractCodes.RejectReason.INVALID_LOGIN_ID)]
        [InlineData(LOGIN_ID, "short1", EMAIL, ContractCodes.RejectReason.WEAK_PASSWORD)]
        [InlineData(LOGIN_ID, "onlyletters", EMAIL, ContractCodes.RejectReason.WEAK_PASSWORD)]
        [InlineData(LOGIN_ID, "12345678", EMAIL, ContractCodes.RejectReason.WEAK_PASSWORD)]
        [InlineData(LOGIN_ID, "ALICE_01", EMAIL, ContractCodes.RejectReason.WEAK_PASSWORD)]
        [InlineData("alice123", "Alice123", EMAIL, ContractCodes.RejectReason.WEAK_PASSWORD)]
        [InlineData(LOGIN_ID, PASSWORD, "not-an-email", ContractCodes.RejectReason.INVALID_EMAIL)]
        [InlineData(LOGIN_ID, PASSWORD, "", ContractCodes.RejectReason.INVALID_EMAIL)]
        public async Task Signup_InvalidField_Returns400WithFieldCodeWithoutHashing(string loginId, string password,
            string email, int expectedReason)
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient();

            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.ACCOUNTS,
                AccountApiHost.Json(new AccountSignupRequest(loginId, password, email)));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(expectedReason, AccountApiHost.Read<ApiRejection>(body).ReasonCode);
            Assert.DoesNotContain(password, body, StringComparison.Ordinal);
            Assert.Equal(0, host.Hasher.HashCalls);
        }

        [Fact]
        public async Task Signup_MalformedJson_Returns400WithoutEchoingInput()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient();

            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.ACCOUNTS, new StringContent(
                "{\"loginId\":\"alice\",\"password\":\"" + PASSWORD + "\"", System.Text.Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(ContractCodes.RejectReason.INVALID_LOGIN_ID, AccountApiHost.Read<ApiRejection>(body).ReasonCode);
            Assert.DoesNotContain(PASSWORD, body, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Signup_DuplicateLoginIdInOtherCase_Returns409LoginIdTaken()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient();
            await SignupAsync(client, LOGIN_ID, PASSWORD, EMAIL);

            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.ACCOUNTS,
                AccountApiHost.Json(new AccountSignupRequest("ALICE_01", PASSWORD, "other@example.com")));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.LOGIN_ID_TAKEN,
                AccountApiHost.Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(1, host.Hasher.HashCalls);
        }

        [Fact]
        public async Task Signup_SameUnverifiedEmail_IsAllowedForSecondAccount()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient();

            await SignupAsync(client, LOGIN_ID, PASSWORD, EMAIL);
            await SignupAsync(client, "bob_02", PASSWORD, "ALICE@example.com");

            Assert.Equal(2, host.Backend.CountWithEmailKey(EMAIL));
        }

        [Fact]
        public async Task Signup_SixthFormatValidAttemptFromOneIp_IsRateLimitedButInvalidAttemptsDoNotCount()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient(ip: ATTACKER_IP);
            for (int i = 0; i < AccountSignupLimiter.MAX_SIGNUPS_PER_WINDOW; ++i)
            {
                using HttpResponseMessage invalid = await client.PostAsync(ApiRoutes.ACCOUNTS,
                    AccountApiHost.Json(new AccountSignupRequest("bad id", PASSWORD, EMAIL)));
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            }
            for (int i = 0; i < AccountSignupLimiter.MAX_SIGNUPS_PER_WINDOW; ++i)
            {
                await SignupAsync(client, "user" + i, PASSWORD, EMAIL);
            }

            using HttpResponseMessage limited = await client.PostAsync(ApiRoutes.ACCOUNTS,
                AccountApiHost.Json(new AccountSignupRequest("user_extra", PASSWORD, EMAIL)));
            using HttpClient other = host.CreateClient(ip: OWNER_IP);
            using HttpResponseMessage otherIp = await other.PostAsync(ApiRoutes.ACCOUNTS,
                AccountApiHost.Json(new AccountSignupRequest("owner_1", PASSWORD, EMAIL)));

            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.RATE_LIMITED,
                AccountApiHost.Read<ApiRejection>(await limited.Content.ReadAsStringAsync()).ReasonCode);
            Assert.NotNull(limited.Headers.RetryAfter);
            Assert.Equal(HttpStatusCode.Created, otherIp.StatusCode);
        }

        [Fact]
        public async Task SignupAndLogin_Maintenance_AreRejected()
        {
            using AccountApiHost host = new AccountApiHost(maintenance: true);
            using HttpClient client = host.CreateClient();

            using HttpResponseMessage signup = await client.PostAsync(ApiRoutes.ACCOUNTS,
                AccountApiHost.Json(new AccountSignupRequest(LOGIN_ID, PASSWORD, EMAIL)));
            using HttpResponseMessage login = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE,
                AccountApiHost.Json(AuthExchangeRequest.ForCredentials(LOGIN_ID, PASSWORD, null)));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, signup.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.MAINTENANCE,
                AccountApiHost.Read<ApiRejection>(await signup.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.MAINTENANCE,
                AccountApiHost.Read<ApiRejection>(await login.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(0, host.Hasher.HashCalls + host.Hasher.VerifyCalls + host.Hasher.DummyCalls);
        }

        [Fact]
        public async Task Login_WithoutBuild_ReturnsTokensAndWithBuildValidatesIt()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient();
            await SignupAsync(client, LOGIN_ID, PASSWORD, EMAIL);

            using HttpResponseMessage website = await LoginAsync(client, "Alice_01", PASSWORD, null);
            using HttpResponseMessage launcher = await LoginAsync(client, LOGIN_ID, PASSWORD, AccountApiHost.Build());
            using HttpResponseMessage wrongBuild = await LoginAsync(client, LOGIN_ID, PASSWORD,
                AccountApiHost.Build("unknown-build"));

            Assert.Equal(HttpStatusCode.OK, website.StatusCode);
            AuthExchangeResponse tokens = AccountApiHost.Read<AuthExchangeResponse>(
                await website.Content.ReadAsStringAsync());
            Assert.False(string.IsNullOrEmpty(tokens.AccessToken));
            Assert.Equal(HttpStatusCode.OK, launcher.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, wrongBuild.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.BUILD_NOT_ALLOWED,
                AccountApiHost.Read<ApiRejection>(await wrongBuild.Content.ReadAsStringAsync()).ReasonCode);

            using HttpClient authenticated = host.CreateClient(tokens.AccessToken);
            using HttpResponseMessage me = await authenticated.GetAsync(ApiRoutes.ACCOUNT_ME);
            AccountProfileResponse profile = AccountApiHost.Read<AccountProfileResponse>(await me.Content.ReadAsStringAsync());
            Assert.Equal(LOGIN_ID, profile.LoginId);
            Assert.Equal(EMAIL, profile.Email);
            Assert.False(profile.EmailVerified);
        }

        [Fact]
        public async Task Login_UnknownIdAndWrongPassword_ReturnSameRejectionAndUnknownUsesDummyHash()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient();
            await SignupAsync(client, LOGIN_ID, PASSWORD, EMAIL);

            using HttpResponseMessage unknown = await LoginAsync(client, "nobody_here", PASSWORD, null);
            Assert.Equal(1, host.Hasher.DummyCalls);
            Assert.Equal(0, host.Hasher.VerifyCalls);
            using HttpResponseMessage wrong = await LoginAsync(client, LOGIN_ID, "Wrong9Password", null);
            Assert.Equal(1, host.Hasher.DummyCalls);
            Assert.Equal(1, host.Hasher.VerifyCalls);

            string unknownBody = await unknown.Content.ReadAsStringAsync();
            string wrongBody = await wrong.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.INVALID_CREDENTIALS, AccountApiHost.Read<ApiRejection>(unknownBody).ReasonCode);
            Assert.Equal(ContractCodes.RejectReason.INVALID_CREDENTIALS, AccountApiHost.Read<ApiRejection>(wrongBody).ReasonCode);
            Assert.Equal(0, host.Backend.IssueLoginCalls);
        }

        [Fact]
        public async Task Login_MalformedLoginId_RejectsWithoutHashing()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient();

            using HttpResponseMessage response = await LoginAsync(client, "bad id!", PASSWORD, null);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.INVALID_CREDENTIALS,
                AccountApiHost.Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(0, host.Hasher.VerifyCalls + host.Hasher.DummyCalls);
        }

        [Fact]
        public async Task Exchange_CredentialsWithInviteOrRefresh_Returns400()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient();
            AuthExchangeRequest both = AuthExchangeRequest.ForCredentials(LOGIN_ID, PASSWORD, null);
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(both, ContractJson.CreateSettings())
                .Replace("\"inviteCode\":null", "\"inviteCode\":\"code\"");

            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE,
                new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
            using HttpResponseMessage passwordOnly = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE,
                AccountApiHost.Json(AuthExchangeRequest.ForCredentials(null!, PASSWORD, null)));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, passwordOnly.StatusCode);
            Assert.Equal(0, host.Hasher.VerifyCalls + host.Hasher.DummyCalls);
        }

        [Fact]
        public async Task Login_FiveFailuresFromOneIp_LockOnlyThatIpAndOwnerElsewhereLogsIn()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient attacker = host.CreateClient(ip: ATTACKER_IP);
            using HttpClient owner = host.CreateClient(ip: OWNER_IP);
            await SignupAsync(owner, LOGIN_ID, PASSWORD, EMAIL);
            for (int i = 0; i < PasswordLoginLimiter.MAX_PAIR_FAILURES; ++i)
            {
                using HttpResponseMessage failed = await LoginAsync(attacker, LOGIN_ID, "Wrong9Password" + i, null);
                Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
            }

            int verifies = host.Hasher.VerifyCalls;
            using HttpResponseMessage locked = await LoginAsync(attacker, LOGIN_ID, PASSWORD, null);
            using HttpResponseMessage ownerLogin = await LoginAsync(owner, LOGIN_ID, PASSWORD, null);
            using HttpResponseMessage otherId = await LoginAsync(attacker, "someone_else", PASSWORD, null);

            Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.ACCOUNT_LOCKED,
                AccountApiHost.Read<ApiRejection>(await locked.Content.ReadAsStringAsync()).ReasonCode);
            Assert.NotNull(locked.Headers.RetryAfter);
            Assert.Equal(HttpStatusCode.OK, ownerLogin.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, otherId.StatusCode);
            Assert.Equal(verifies + 1, host.Hasher.VerifyCalls);
        }

        [Fact]
        public async Task Login_SuccessResetsPairFailures()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient(ip: OWNER_IP);
            await SignupAsync(client, LOGIN_ID, PASSWORD, EMAIL);

            for (int round = 0; round < 2; ++round)
            {
                for (int i = 0; i < PasswordLoginLimiter.MAX_PAIR_FAILURES - 1; ++i)
                {
                    using HttpResponseMessage failed = await LoginAsync(client, LOGIN_ID, "Wrong9Password", null);
                    Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
                }

                using HttpResponseMessage success = await LoginAsync(client, LOGIN_ID, PASSWORD, null);
                Assert.Equal(HttpStatusCode.OK, success.StatusCode);
            }
        }

        [Fact]
        public async Task Login_ManyFailuresAcrossIps_SlowsLoginIdToOneAttemptPerMinutePerIp()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient owner = host.CreateClient(ip: OWNER_IP);
            await SignupAsync(owner, LOGIN_ID, PASSWORD, EMAIL);
            int ips = PasswordLoginLimiter.MAX_LOGIN_ID_FAILURES_PER_WINDOW / PasswordLoginLimiter.MAX_PAIR_FAILURES;
            for (int ip = 0; ip < ips; ++ip)
            {
                using HttpClient attacker = host.CreateClient(ip: "10.1.0." + (ip + 1));
                for (int i = 0; i < PasswordLoginLimiter.MAX_PAIR_FAILURES; ++i)
                {
                    using HttpResponseMessage failed = await LoginAsync(attacker, LOGIN_ID, "Wrong9Password", null);
                    Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
                }
            }
            using HttpClient last = host.CreateClient(ip: "10.1.0.200");
            using HttpResponseMessage overLimit = await LoginAsync(last, LOGIN_ID, "Wrong9Password", null);
            Assert.Equal(HttpStatusCode.Unauthorized, overLimit.StatusCode);

            using HttpResponseMessage first = await LoginAsync(owner, LOGIN_ID, PASSWORD, null);
            using HttpResponseMessage second = await LoginAsync(owner, LOGIN_ID, PASSWORD, null);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.RATE_LIMITED,
                AccountApiHost.Read<ApiRejection>(await second.Content.ReadAsStringAsync()).ReasonCode);
            Assert.NotNull(second.Headers.RetryAfter);
        }

        [Fact]
        public async Task PasswordFailures_DoNotCountTowardBearerFailureLimiter()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient(ip: ATTACKER_IP);
            (Guid _, string access) = host.Backend.AddInviteAccount();
            int failures = AuthFailureLimiter.MAX_FAILURES_PER_WINDOW + PasswordLoginLimiter.MAX_PAIR_FAILURES;
            for (int i = 0; i < failures; ++i)
            {
                using HttpResponseMessage failed = await LoginAsync(client,
                    "victim_" + (i / PasswordLoginLimiter.MAX_PAIR_FAILURES), "Wrong9Password", null);
                Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
            }

            using HttpClient bearer = host.CreateClient(access, ATTACKER_IP);
            using HttpResponseMessage probe = await bearer.GetAsync(AuthActorProbeController.ROUTE);

            Assert.Equal(HttpStatusCode.OK, probe.StatusCode);
        }

        [Fact]
        public async Task Login_HashGateBusy_ReturnsRateLimitedWithoutCountingFailure()
        {
            using PasswordHashGate gate = new PasswordHashGate(1, 0, TimeSpan.Zero);
            using AccountApiHost host = new AccountApiHost(gate: gate);
            using HttpClient client = host.CreateClient();
            await SignupAsync(client, LOGIN_ID, PASSWORD, EMAIL);

            IDisposable? held = await gate.TryEnterAsync(CancellationToken.None);
            Assert.NotNull(held);
            using HttpResponseMessage busy = await LoginAsync(client, LOGIN_ID, PASSWORD, null);
            using HttpResponseMessage busySignup = await client.PostAsync(ApiRoutes.ACCOUNTS,
                AccountApiHost.Json(new AccountSignupRequest("bob_02", PASSWORD, EMAIL)));
            held!.Dispose();
            using HttpResponseMessage freed = await LoginAsync(client, LOGIN_ID, PASSWORD, null);

            Assert.Equal(HttpStatusCode.TooManyRequests, busy.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.RATE_LIMITED,
                AccountApiHost.Read<ApiRejection>(await busy.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, busySignup.StatusCode);
            Assert.Equal(HttpStatusCode.OK, freed.StatusCode);
        }

        [Fact]
        public async Task Me_AccessAndGameSessionTokens_ReturnProfileAndAnonymousIsRejected()
        {
            using AccountApiHost host = new AccountApiHost();
            (Guid accountId, string access) = host.Backend.AddInviteAccount();
            string gameSession = host.Backend.AddGameSession(accountId);
            using HttpClient accessClient = host.CreateClient(access);
            using HttpClient gameClient = host.CreateClient(gameSession);
            using HttpClient anonymous = host.CreateClient();

            using HttpResponseMessage viaAccess = await accessClient.GetAsync(ApiRoutes.ACCOUNT_ME);
            using HttpResponseMessage viaGame = await gameClient.GetAsync(ApiRoutes.ACCOUNT_ME);
            using HttpResponseMessage none = await anonymous.GetAsync(ApiRoutes.ACCOUNT_ME);

            Assert.Equal(HttpStatusCode.OK, viaAccess.StatusCode);
            Assert.Equal(HttpStatusCode.OK, viaGame.StatusCode);
            AccountProfileResponse profile = AccountApiHost.Read<AccountProfileResponse>(
                await viaGame.Content.ReadAsStringAsync());
            Assert.Equal(accountId.ToString("D"), profile.AccountId);
            Assert.Null(profile.LoginId);
            Assert.Null(profile.Email);
            Assert.Equal(HttpStatusCode.Unauthorized, none.StatusCode);
        }

        [Fact]
        public async Task Credentials_ChangePassword_RevokesSessionsAndOnlyNewPasswordLogsIn()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient();
            await SignupAsync(client, LOGIN_ID, PASSWORD, EMAIL);
            AuthExchangeResponse tokens = await LoginTokensAsync(client, LOGIN_ID, PASSWORD);
            AuthExchangeResponse second = await LoginTokensAsync(client, LOGIN_ID, PASSWORD);
            string gameSession = host.Backend.AddGameSession(Guid.Parse(tokens.AccountId));
            using HttpClient authenticated = host.CreateClient(tokens.AccessToken);

            using HttpResponseMessage changed = await authenticated.PostAsync(ApiRoutes.ACCOUNT_CREDENTIALS,
                AccountApiHost.Json(AccountCredentialsRequest.ForPasswordChange(PASSWORD, NEW_PASSWORD)));

            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
            using HttpResponseMessage oldAccess = await authenticated.GetAsync(ApiRoutes.ACCOUNT_ME);
            using HttpClient otherFamily = host.CreateClient(second.AccessToken);
            using HttpResponseMessage otherAccess = await otherFamily.GetAsync(ApiRoutes.ACCOUNT_ME);
            using HttpClient game = host.CreateClient(gameSession);
            using HttpResponseMessage oldGame = await game.GetAsync(ApiRoutes.ACCOUNT_ME);
            using HttpResponseMessage oldPassword = await LoginAsync(client, LOGIN_ID, PASSWORD, null);
            using HttpResponseMessage newPassword = await LoginAsync(client, LOGIN_ID, NEW_PASSWORD, null);
            Assert.Equal(HttpStatusCode.Unauthorized, oldAccess.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, otherAccess.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, oldGame.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
            Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
        }

        [Fact]
        public async Task Credentials_WrongCurrentPasswordGameSessionOrIdentityFields_AreRejected()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient();
            await SignupAsync(client, LOGIN_ID, PASSWORD, EMAIL);
            AuthExchangeResponse tokens = await LoginTokensAsync(client, LOGIN_ID, PASSWORD);
            using HttpClient authenticated = host.CreateClient(tokens.AccessToken);
            using HttpClient game = host.CreateClient(host.Backend.AddGameSession(Guid.Parse(tokens.AccountId)));

            using HttpResponseMessage wrong = await authenticated.PostAsync(ApiRoutes.ACCOUNT_CREDENTIALS,
                AccountApiHost.Json(AccountCredentialsRequest.ForPasswordChange("Wrong9Password", NEW_PASSWORD)));
            using HttpResponseMessage weak = await authenticated.PostAsync(ApiRoutes.ACCOUNT_CREDENTIALS,
                AccountApiHost.Json(AccountCredentialsRequest.ForPasswordChange(PASSWORD, "weak")));
            using HttpResponseMessage rename = await authenticated.PostAsync(ApiRoutes.ACCOUNT_CREDENTIALS,
                AccountApiHost.Json(new AccountCredentialsRequest(PASSWORD, NEW_PASSWORD, "new_name", null)));
            using HttpResponseMessage viaGame = await game.PostAsync(ApiRoutes.ACCOUNT_CREDENTIALS,
                AccountApiHost.Json(AccountCredentialsRequest.ForPasswordChange(PASSWORD, NEW_PASSWORD)));

            Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.INVALID_CREDENTIALS,
                AccountApiHost.Read<ApiRejection>(await wrong.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.WEAK_PASSWORD,
                AccountApiHost.Read<ApiRejection>(await weak.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.INVALID_LOGIN_ID,
                AccountApiHost.Read<ApiRejection>(await rename.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.Forbidden, viaGame.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                AccountApiHost.Read<ApiRejection>(await viaGame.Content.ReadAsStringAsync()).ReasonCode);

            using HttpResponseMessage stillValid = await authenticated.GetAsync(ApiRoutes.ACCOUNT_ME);
            Assert.Equal(HttpStatusCode.OK, stillValid.StatusCode);
        }

        [Fact]
        public async Task Credentials_InviteAccount_SetsLoginIdEmailAndPasswordThenLogsIn()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient anonymous = host.CreateClient();
            await SignupAsync(anonymous, "taken_id", PASSWORD, EMAIL);
            (Guid accountId, string access) = host.Backend.AddInviteAccount();
            using HttpClient invited = host.CreateClient(access);

            using HttpResponseMessage taken = await invited.PostAsync(ApiRoutes.ACCOUNT_CREDENTIALS,
                AccountApiHost.Json(AccountCredentialsRequest.ForInitialCredentials("Taken_ID", EMAIL, NEW_PASSWORD)));
            using HttpResponseMessage noEmail = await invited.PostAsync(ApiRoutes.ACCOUNT_CREDENTIALS,
                AccountApiHost.Json(AccountCredentialsRequest.ForInitialCredentials("invited_1", null!, NEW_PASSWORD)));
            using HttpResponseMessage set = await invited.PostAsync(ApiRoutes.ACCOUNT_CREDENTIALS,
                AccountApiHost.Json(AccountCredentialsRequest.ForInitialCredentials("Invited_1", EMAIL, NEW_PASSWORD)));

            Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.LOGIN_ID_TAKEN,
                AccountApiHost.Read<ApiRejection>(await taken.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.BadRequest, noEmail.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.INVALID_EMAIL,
                AccountApiHost.Read<ApiRejection>(await noEmail.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
            using HttpResponseMessage revoked = await invited.GetAsync(ApiRoutes.ACCOUNT_ME);
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
            AuthExchangeResponse tokens = await LoginTokensAsync(anonymous, "invited_1", NEW_PASSWORD);
            Assert.Equal(accountId.ToString("D"), tokens.AccountId);
        }

        [Fact]
        public async Task Credentials_RecoveryWithoutCurrentPassword_OnlyFromFreshInviteLogin()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient anonymous = host.CreateClient();
            await SignupAsync(anonymous, LOGIN_ID, PASSWORD, EMAIL);
            AuthExchangeResponse passwordLogin = await LoginTokensAsync(anonymous, LOGIN_ID, PASSWORD);
            Guid accountId = Guid.Parse(passwordLogin.AccountId);
            using HttpClient viaPassword = host.CreateClient(passwordLogin.AccessToken);
            (Guid staleAccount, string staleAccess) = host.Backend.AddInviteAccount();
            host.Backend.AgeFamilies(staleAccount, TimeSpan.FromMinutes(Shinshi.Data.Auth.PostgresAuthRepository.RECOVERY_WINDOW_MINUTES + 1));
            using HttpClient stale = host.CreateClient(staleAccess);

            using HttpResponseMessage noCurrent = await viaPassword.PostAsync(ApiRoutes.ACCOUNT_CREDENTIALS,
                AccountApiHost.Json(new AccountCredentialsRequest(null!, NEW_PASSWORD, null!, null!)));
            using HttpResponseMessage expired = await stale.PostAsync(ApiRoutes.ACCOUNT_CREDENTIALS,
                AccountApiHost.Json(AccountCredentialsRequest.ForInitialCredentials("stale_1", EMAIL, NEW_PASSWORD)));

            Assert.Equal(HttpStatusCode.Forbidden, noCurrent.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.INVALID_CREDENTIALS,
                AccountApiHost.Read<ApiRejection>(await noCurrent.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.INVALID_CREDENTIALS,
                AccountApiHost.Read<ApiRejection>(await expired.Content.ReadAsStringAsync()).ReasonCode);

            // 복구 코드 로그인은 현재 비밀번호 없이 비밀번호와 이메일을 바꾸고 모든 로그인을 끊는다
            using HttpClient recovery = host.CreateClient(host.Backend.AddInviteLogin(accountId));
            using HttpResponseMessage recovered = await recovery.PostAsync(ApiRoutes.ACCOUNT_CREDENTIALS,
                AccountApiHost.Json(new AccountCredentialsRequest(null!, NEW_PASSWORD, null!, "new@example.com")));
            Assert.Equal(HttpStatusCode.NoContent, recovered.StatusCode);
            using HttpResponseMessage oldSession = await viaPassword.GetAsync(ApiRoutes.ACCOUNT_ME);
            using HttpResponseMessage oldPassword = await LoginAsync(anonymous, LOGIN_ID, PASSWORD, null);
            Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
            await LoginTokensAsync(anonymous, LOGIN_ID, NEW_PASSWORD);
            Assert.Equal("new@example.com", host.Backend.EmailOf(LOGIN_ID));
        }

        [Fact]
        public async Task Credentials_CurrentPasswordOnAccountWithoutLoginId_IsRejected()
        {
            using AccountApiHost host = new AccountApiHost();
            (Guid _, string access) = host.Backend.AddInviteAccount();
            using HttpClient invited = host.CreateClient(access);

            using HttpResponseMessage response = await invited.PostAsync(ApiRoutes.ACCOUNT_CREDENTIALS,
                AccountApiHost.Json(new AccountCredentialsRequest(PASSWORD, NEW_PASSWORD, "invited_1", EMAIL)));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(0, host.Hasher.HashCalls);
        }

        [Fact]
        public async Task JoinTickets_AccessToken_IsForbiddenBeforeRepository()
        {
            using AccountApiHost host = new AccountApiHost();
            (Guid _, string access) = host.Backend.AddInviteAccount();
            using HttpClient player = host.CreateClient(access);

            using HttpResponseMessage town = await player.PostAsync(ApiRoutes.TOWN_JOIN_TICKET,
                AccountApiHost.Json(new TownJoinTicketRequest(Guid.NewGuid().ToString("D"))));
            using HttpResponseMessage rejoin = await player.PostAsync(
                string.Format(ApiRoutes.REJOIN_TICKET_FORMAT, Guid.NewGuid().ToString("D")), null);

            Assert.Equal(HttpStatusCode.Forbidden, town.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                AccountApiHost.Read<ApiRejection>(await town.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.Forbidden, rejoin.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.SESSION_MISMATCH,
                AccountApiHost.Read<ApiRejection>(await rejoin.Content.ReadAsStringAsync()).ReasonCode);
        }

        [Fact]
        public async Task Exchange_InviteWithoutBuild_IsVersionMismatchAndRefreshReachesRepository()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient();

            using HttpResponseMessage invite = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE,
                AccountApiHost.Json(new AuthExchangeRequest("invite-code", null!, null!)));
            using HttpResponseMessage refresh = await client.PostAsync(ApiRoutes.AUTH_EXCHANGE,
                AccountApiHost.Json(new AuthExchangeRequest(null!, "refresh-token", null!)));

            Assert.Equal(HttpStatusCode.Forbidden, invite.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.VERSION_MISMATCH,
                AccountApiHost.Read<ApiRejection>(await invite.Content.ReadAsStringAsync()).ReasonCode);
            // 갱신은 build가 없어도 빌드 검사를 건너뛰고 저장소까지 가서 모르는 토큰으로 거절된다
            Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.TOKEN_REVOKED,
                AccountApiHost.Read<ApiRejection>(await refresh.Content.ReadAsStringAsync()).ReasonCode);
        }

        [Fact]
        public async Task Login_UnknownId_RunsPbkdf2AtCurrentDefaultIterations()
        {
            const int CURRENT_ITERATIONS = 2000;
            using AccountApiHost host = new AccountApiHost(iterations: CURRENT_ITERATIONS);
            using HttpClient client = host.CreateClient();

            using HttpResponseMessage unknown = await LoginAsync(client, "nobody_here", PASSWORD, null);

            Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
            Assert.Equal(1, host.Hasher.Derivations);
            Assert.Equal(CURRENT_ITERATIONS, host.Hasher.LastDerivedIterations);
        }

        [Fact]
        public async Task Login_StoredHashBelowCurrentIterations_IsRehashedOnSuccess()
        {
            const int CURRENT_ITERATIONS = 2000;
            using AccountApiHost host = new AccountApiHost(iterations: CURRENT_ITERATIONS);
            using HttpClient client = host.CreateClient();
            string older = new Shinshi.Data.Accounts.AccountPasswordHasher(
                AccountPasswordHasherTests.TEST_ITERATIONS).Hash(PASSWORD);
            host.Backend.AddCredentialAccount(LOGIN_ID, older, EMAIL);

            await LoginTokensAsync(client, LOGIN_ID, PASSWORD);

            string upgraded = host.Backend.PasswordHashOf(LOGIN_ID)!;
            Assert.NotEqual(older, upgraded);
            Assert.Equal("2000", upgraded.Split('$')[1]);
            Assert.False(host.Hasher.NeedsRehash(upgraded));
            await LoginTokensAsync(client, LOGIN_ID, PASSWORD);
            Assert.Equal(upgraded, host.Backend.PasswordHashOf(LOGIN_ID));
        }

        [Fact]
        public async Task LoginIdAvailability_ReportsInvalidTakenAndFreeAndIsRateLimited()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient(ip: ATTACKER_IP);
            await SignupAsync(host.CreateClient(), LOGIN_ID, PASSWORD, EMAIL);

            AvailabilityResponse invalid = await AvailabilityAsync(client, "no!");
            AvailabilityResponse taken = await AvailabilityAsync(client, "ALICE_01");
            AvailabilityResponse free = await AvailabilityAsync(client, "free_name");
            Assert.False(invalid.Available);
            Assert.Equal(ContractCodes.RejectReason.INVALID_LOGIN_ID, invalid.ReasonCode);
            Assert.False(taken.Available);
            Assert.Equal(ContractCodes.RejectReason.LOGIN_ID_TAKEN, taken.ReasonCode);
            Assert.True(free.Available);
            Assert.Equal(ContractCodes.RejectReason.NONE, free.ReasonCode);

            for (int i = 3; i < AccountSignupLimiter.MAX_AVAILABILITY_REQUESTS_PER_WINDOW; ++i)
            {
                await AvailabilityAsync(client, "free_name");
            }
            using HttpResponseMessage limited = await client.GetAsync(
                ApiRoutes.ACCOUNT_LOGIN_ID_AVAILABILITY + "?loginId=free_name");
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        }

        [Fact]
        public async Task CharacterNameAvailability_RequiresBearerAndAppliesNameRules()
        {
            using AccountApiHost host = new AccountApiHost();
            host.Characters.TakenKeys.Add("shinshi");
            (Guid _, string access) = host.Backend.AddInviteAccount();
            using HttpClient anonymous = host.CreateClient();
            using HttpClient player = host.CreateClient(access);

            using HttpResponseMessage denied = await anonymous.GetAsync(
                ApiRoutes.CHARACTER_NAME_AVAILABILITY + "?name=fox");
            AvailabilityResponse fullwidth = await NameAvailabilityAsync(player, "ＳＨＩＮＳＨＩ");
            AvailabilityResponse invalid = await NameAvailabilityAsync(player, "a b");
            AvailabilityResponse free = await NameAvailabilityAsync(player, "여우");

            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.CHARACTER_NAME_TAKEN, fullwidth.ReasonCode);
            Assert.False(fullwidth.Available);
            Assert.Equal(ContractCodes.RejectReason.INVALID_CHARACTER_NAME, invalid.ReasonCode);
            Assert.True(free.Available);
        }

        [Fact]
        public async Task CharacterCreate_InvalidNameOrAppearance_Returns400BeforeRepository()
        {
            using AccountApiHost host = new AccountApiHost();
            (Guid _, string access) = host.Backend.AddInviteAccount();
            using HttpClient player = host.CreateClient(access);
            string operationId = Guid.NewGuid().ToString("D");

            using HttpResponseMessage badName = await player.PostAsync(ApiRoutes.CHARACTERS, AccountApiHost.Json(
                new CharacterCreateRequest(operationId, "x", ContractCodes.PlayerClass.WARRIOR, CharacterTestData.Appearance())));
            using HttpResponseMessage badAppearance = await player.PostAsync(ApiRoutes.CHARACTERS, AccountApiHost.Json(
                new CharacterCreateRequest(operationId, "여우", ContractCodes.PlayerClass.WARRIOR, null!)));
            using HttpResponseMessage badClass = await player.PostAsync(ApiRoutes.CHARACTERS, AccountApiHost.Json(
                new CharacterCreateRequest(operationId, "여우", 9, CharacterTestData.Appearance())));

            Assert.Equal(ContractCodes.RejectReason.INVALID_CHARACTER_NAME,
                AccountApiHost.Read<ApiRejection>(await badName.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.BadRequest, badAppearance.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.INVALID_APPEARANCE,
                AccountApiHost.Read<ApiRejection>(await badAppearance.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.BadRequest, badClass.StatusCode);
            Assert.Equal(0, host.Characters.CreateCalls);
        }

        [Theory]
        [InlineData(6)]
        [InlineData(-1)]
        public async Task CharacterCreate_ClassOutsideZeroToFive_Returns400BeforeRepository(int classId)
        {
            using AccountApiHost host = new AccountApiHost(enabledClassIds: "0,1,2,3,4,5");
            (Guid _, string access) = host.Backend.AddInviteAccount();
            using HttpClient player = host.CreateClient(access);

            using HttpResponseMessage response = await player.PostAsync(ApiRoutes.CHARACTERS, AccountApiHost.Json(
                new CharacterCreateRequest(Guid.NewGuid().ToString("D"), "여우", classId, CharacterTestData.Appearance())));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.INVALID_APPEARANCE,
                AccountApiHost.Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(0, host.Characters.CreateCalls);
        }

        /// <summary> 설정에서 빠진 새 직업은 저장소 호출 전에 거절된다 </summary>
        [Theory]
        [InlineData("0,1,2", 3)]
        [InlineData("0,1,2", 4)]
        [InlineData("0,1,2", 5)]
        [InlineData("3", 4)]
        [InlineData("3", 5)]
        public async Task CharacterCreate_NewClassWhileGateClosed_Returns409FeatureDisabled(string? enabledClassIds, int classId)
        {
            using AccountApiHost host = new AccountApiHost(enabledClassIds: enabledClassIds);
            (Guid _, string access) = host.Backend.AddInviteAccount();
            using HttpClient player = host.CreateClient(access);

            using HttpResponseMessage response = await player.PostAsync(ApiRoutes.CHARACTERS, AccountApiHost.Json(
                new CharacterCreateRequest(Guid.NewGuid().ToString("D"), "여우", classId, CharacterTestData.Appearance())));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.FEATURE_DISABLED,
                AccountApiHost.Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(0, host.Characters.CreateCalls);
        }

        [Theory]
        [InlineData(null, 3)]
        [InlineData(null, 4)]
        [InlineData(null, 5)]
        [InlineData("", 3)]
        [InlineData("", 4)]
        [InlineData("", 5)]
        [InlineData("0,1,2,3,4,5", 3)]
        [InlineData("0,1,2,3,4,5", 4)]
        [InlineData("0,1,2,3,4,5", 5)]
        public async Task CharacterCreate_NewClassWithDefaultOrExplicitGate_ReachesRepository(string? enabledClassIds,
            int classId)
        {
            using AccountApiHost host = new AccountApiHost(enabledClassIds: enabledClassIds);
            (Guid _, string access) = host.Backend.AddInviteAccount();
            using HttpClient player = host.CreateClient(access);

            using HttpResponseMessage response = await player.PostAsync(ApiRoutes.CHARACTERS, AccountApiHost.Json(
                new CharacterCreateRequest(Guid.NewGuid().ToString("D"), "여우", classId, CharacterTestData.Appearance())));

            // 대역 저장소는 슬롯 부족(409)으로 답한다. 게이트 거절이 아니라 저장소까지 갔는지는 호출 수로 본다
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(1, host.Characters.CreateCalls);
            Assert.Equal(classId, host.Characters.LastClassId);
        }

        /// <summary> 구직업은 기본 설정과 명시적인 구직업 재개방 설정 모두에서 생성되지 않는다 </summary>
        [Theory]
        [InlineData(null, ContractCodes.PlayerClass.WARRIOR)]
        [InlineData(null, ContractCodes.PlayerClass.SORCERER)]
        [InlineData(null, ContractCodes.PlayerClass.SUMMONER)]
        [InlineData("", ContractCodes.PlayerClass.WARRIOR)]
        [InlineData("0,1,2,3,4,5", ContractCodes.PlayerClass.WARRIOR)]
        [InlineData("0,1,2,3,4,5", ContractCodes.PlayerClass.SORCERER)]
        [InlineData("0,1,2,3,4,5", ContractCodes.PlayerClass.SUMMONER)]
        public async Task CharacterCreate_LegacyClassWithDefaultOrExplicitGate_Returns409BeforeRepository(
            string? enabledClassIds, int classId)
        {
            using AccountApiHost host = new AccountApiHost(enabledClassIds: enabledClassIds);
            (Guid _, string access) = host.Backend.AddInviteAccount();
            using HttpClient player = host.CreateClient(access);

            using HttpResponseMessage response = await player.PostAsync(ApiRoutes.CHARACTERS, AccountApiHost.Json(
                new CharacterCreateRequest(Guid.NewGuid().ToString("D"), "여우", classId, CharacterTestData.Appearance())));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.FEATURE_DISABLED,
                AccountApiHost.Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(0, host.Characters.CreateCalls);
        }

        /// <summary> 캐릭터 생성, 삭제는 계정별 1분 한도를 함께 쓴다. 넘으면 저장소 호출 없이 429이고, 저장된 24시간 삭제 상한
        /// 거절(RATE_LIMITED)도 429로 돌려준다 </summary>
        [Fact]
        public async Task CharacterWrites_OverAccountLimit_Return429()
        {
            using AccountApiHost host = new AccountApiHost();
            (Guid _, string access) = host.Backend.AddInviteAccount();
            using HttpClient player = host.CreateClient(access);
            string deletePath = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                ApiRoutes.CHARACTER_DELETE_FORMAT, Guid.NewGuid().ToString("D"));

            host.Characters.DeleteReason = ContractCodes.RejectReason.RATE_LIMITED;
            using HttpResponseMessage capped = await player.PostAsync(deletePath, AccountApiHost.Json(
                new CharacterDeleteRequest(Guid.NewGuid().ToString("D"), 0)));
            for (int i = 1; i < AccountRequestLimiter.MAX_CHARACTER_WRITES_PER_WINDOW; ++i)
            {
                using HttpResponseMessage created = await player.PostAsync(ApiRoutes.CHARACTERS, AccountApiHost.Json(
                    new CharacterCreateRequest(Guid.NewGuid().ToString("D"), "여우", ContractCodes.PlayerClass.SWORDSMAN,
                        CharacterTestData.Appearance())));
                Assert.Equal(HttpStatusCode.Conflict, created.StatusCode);
            }

            using HttpResponseMessage limited = await player.PostAsync(ApiRoutes.CHARACTERS, AccountApiHost.Json(
                new CharacterCreateRequest(Guid.NewGuid().ToString("D"), "여우", ContractCodes.PlayerClass.SWORDSMAN,
                    CharacterTestData.Appearance())));
            host.Characters.DeleteReason = ContractCodes.RejectReason.NONE;
            using HttpResponseMessage limitedDelete = await player.PostAsync(deletePath, AccountApiHost.Json(
                new CharacterDeleteRequest(Guid.NewGuid().ToString("D"), 0)));

            Assert.Equal(HttpStatusCode.TooManyRequests, capped.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.RATE_LIMITED,
                AccountApiHost.Read<OperationResult>(await capped.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal(ContractCodes.RejectReason.RATE_LIMITED,
                AccountApiHost.Read<ApiRejection>(await limited.Content.ReadAsStringAsync()).ReasonCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, limitedDelete.StatusCode);
            Assert.Equal(AccountRequestLimiter.MAX_CHARACTER_WRITES_PER_WINDOW - 1, host.Characters.CreateCalls);
            Assert.Equal(1, host.Characters.DeleteCalls);
        }

        // v2는 등록된 face/hair/outfit과 중립 슬라이더/색까지 엄격히 본다. 목록 바깥 ID, 비중립 슬라이더/피부/색,
        // 미지의 직업은 모두 저장소를 부르기 전에 400으로 거절된다
        [Fact]
        public async Task CharacterCreate_V2StrictCatalogRejectsOutOfRangePartsNonNeutralValuesAndUnknownClass_Returns400BeforeRepository()
        {
            using AccountApiHost host = new AccountApiHost();
            (Guid _, string access) = host.Backend.AddInviteAccount();
            using HttpClient player = host.CreateClient(access);

            (string Label, int ClassId, CharacterAppearance Appearance)[] cases =
            {
                ("Face0", ContractCodes.PlayerClass.WARRIOR, CharacterAppearanceCatalog.Create(0,
                    CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_WARRIOR)),
                ("Face2", ContractCodes.PlayerClass.WARRIOR, CharacterAppearanceCatalog.Create(2,
                    CharacterAppearanceCatalog.HAIR_WARRIOR, CharacterAppearanceCatalog.OUTFIT_WARRIOR)),
                ("Hair0", ContractCodes.PlayerClass.WARRIOR, CharacterAppearanceCatalog.Create(
                    CharacterAppearanceCatalog.FACE_CUTE, 0, CharacterAppearanceCatalog.OUTFIT_WARRIOR)),
                ("Hair4", ContractCodes.PlayerClass.WARRIOR, CharacterAppearanceCatalog.Create(
                    CharacterAppearanceCatalog.FACE_CUTE, 4, CharacterAppearanceCatalog.OUTFIT_WARRIOR)),
                ("Outfit0", ContractCodes.PlayerClass.WARRIOR, CharacterAppearanceCatalog.Create(
                    CharacterAppearanceCatalog.FACE_CUTE, CharacterAppearanceCatalog.HAIR_WARRIOR, 0)),
                ("Outfit4", ContractCodes.PlayerClass.WARRIOR, CharacterAppearanceCatalog.Create(
                    CharacterAppearanceCatalog.FACE_CUTE, CharacterAppearanceCatalog.HAIR_WARRIOR, 4)),
                ("FaceSlider", ContractCodes.PlayerClass.WARRIOR,
                    V2Appearance(face: MutatedSliders(CharacterAppearance.FACE_SLIDER_COUNT))),
                ("BodySlider", ContractCodes.PlayerClass.WARRIOR,
                    V2Appearance(body: MutatedSliders(CharacterAppearance.BODY_SLIDER_COUNT))),
                ("SkinTone", ContractCodes.PlayerClass.WARRIOR,
                    V2Appearance(skinTone: CharacterAppearance.SLIDER_NEUTRAL - 1)),
                ("EyeColor", ContractCodes.PlayerClass.WARRIOR, V2Appearance(eyeColor: 1)),
                ("UnknownClass", 9, V2Appearance()),
            };

            for (int i = 0; i < cases.Length; ++i)
            {
                (string label, int classId, CharacterAppearance appearance) = cases[i];
                using HttpResponseMessage response = await player.PostAsync(ApiRoutes.CHARACTERS, AccountApiHost.Json(
                    new CharacterCreateRequest(Guid.NewGuid().ToString("D"), "Ext" + i, classId, appearance)));
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Equal(ContractCodes.RejectReason.INVALID_APPEARANCE,
                    AccountApiHost.Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode);
            }

            Assert.Equal(0, host.Characters.CreateCalls);
        }

        // 허용된 v3(P09) 외형은 검증을 통과해 저장소까지 간다. 대역 저장소는 슬롯 부족(409)으로 답하므로
        // 400이 아니고 저장소 호출 1회와 v3 JSON 전달로 통과를 확인한다
        [Theory]
        [InlineData(ContractCodes.PlayerClass.SWORDSMAN, CharacterAppearance.BODY_TYPE_FEMALE, 1, 1, 1, 1, 1, 0, 2, 31)]
        [InlineData(ContractCodes.PlayerClass.MAGEKNIGHT, CharacterAppearance.BODY_TYPE_FEMALE, 3, 3, 9, 5, 3, 0, 3, 0)]
        [InlineData(ContractCodes.PlayerClass.ARCHER, CharacterAppearance.BODY_TYPE_MALE, 2, 2, 4, 2, 2, 8, 0, 21)]
        [InlineData(ContractCodes.PlayerClass.SWORDSMAN, CharacterAppearance.BODY_TYPE_MALE, 1, 3, 1, 1, 1, 0, 0, 31)]
        public async Task CharacterCreate_V3AllowedAppearance_ReachesRepository(int classId, int bodyType, int faceId, int hairId,
            int hairColor, int eyeColor, int skinTone, int facialHair, int bustSize, int outfitMask)
        {
            using AccountApiHost host = new AccountApiHost();
            (Guid _, string access) = host.Backend.AddInviteAccount();
            using HttpClient player = host.CreateClient(access);
            CharacterAppearance appearance = CharacterAppearanceCatalog.CreateP09(bodyType, faceId, hairId, hairColor, eyeColor,
                skinTone, facialHair, bustSize, outfitMask);
            Assert.True(CharacterAppearanceCatalog.IsAllowed(ContractCodes.PlayerClass.WARRIOR, appearance));

            using HttpResponseMessage response = await player.PostAsync(ApiRoutes.CHARACTERS, AccountApiHost.Json(
                new CharacterCreateRequest(Guid.NewGuid().ToString("D"), "여우", classId, appearance)));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(1, host.Characters.CreateCalls);
            CharacterAppearance forwarded = AccountApiHost.Read<CharacterAppearance>(host.Characters.LastAppearanceJson!);
            Assert.Equal(CharacterAppearance.VERSION_3, forwarded.Version);
            Assert.True(appearance.ContentEquals(forwarded));
        }

        [Fact]
        public async Task CharacterCreate_V3RejectsOutOfRangeGenderMismatchAndNonCanonicalValues_Returns400BeforeRepository()
        {
            using AccountApiHost host = new AccountApiHost();
            (Guid _, string access) = host.Backend.AddInviteAccount();
            using HttpClient player = host.CreateClient(access);
            const int FEMALE = CharacterAppearance.BODY_TYPE_FEMALE;
            const int MALE   = CharacterAppearance.BODY_TYPE_MALE;

            (string Label, CharacterAppearance Appearance)[] cases =
            {
                ("Face0", CharacterAppearanceCatalog.CreateP09(FEMALE, 0, 1, 1, 1, 1, 0, 2, 31)),
                ("Face4", CharacterAppearanceCatalog.CreateP09(FEMALE, 4, 1, 1, 1, 1, 0, 2, 31)),
                ("Hair0", CharacterAppearanceCatalog.CreateP09(FEMALE, 1, 0, 1, 1, 1, 0, 2, 31)),
                ("Hair4", CharacterAppearanceCatalog.CreateP09(FEMALE, 1, 4, 1, 1, 1, 0, 2, 31)),
                ("HairColor0", CharacterAppearanceCatalog.CreateP09(FEMALE, 1, 1, 0, 1, 1, 0, 2, 31)),
                ("HairColor10", CharacterAppearanceCatalog.CreateP09(FEMALE, 1, 1, 10, 1, 1, 0, 2, 31)),
                ("Eye0", CharacterAppearanceCatalog.CreateP09(FEMALE, 1, 1, 1, 0, 1, 0, 2, 31)),
                ("Eye6", CharacterAppearanceCatalog.CreateP09(FEMALE, 1, 1, 1, 6, 1, 0, 2, 31)),
                ("Skin0", CharacterAppearanceCatalog.CreateP09(FEMALE, 1, 1, 1, 1, 0, 0, 2, 31)),
                ("Skin4", CharacterAppearanceCatalog.CreateP09(FEMALE, 1, 1, 1, 1, 4, 0, 2, 31)),
                ("Outfit32", CharacterAppearanceCatalog.CreateP09(FEMALE, 1, 1, 1, 1, 1, 0, 2, 32)),
                ("MaleBust", CharacterAppearanceCatalog.CreateP09(MALE, 1, 1, 1, 1, 1, 0, 2, 31)),
                ("MaleFacialHair9", CharacterAppearanceCatalog.CreateP09(MALE, 1, 1, 1, 1, 1, 9, 0, 31)),
                ("FemaleFacialHair", CharacterAppearanceCatalog.CreateP09(FEMALE, 1, 1, 1, 1, 1, 1, 2, 31)),
                ("FemaleNoBust", CharacterAppearanceCatalog.CreateP09(FEMALE, 1, 1, 1, 1, 1, 0, 0, 31)),
                ("FemaleBust4", CharacterAppearanceCatalog.CreateP09(FEMALE, 1, 1, 1, 1, 1, 0, 4, 31)),
                ("BodyType2", CharacterAppearanceCatalog.CreateP09(2, 1, 1, 1, 1, 1, 0, 2, 31)),
                ("BrowColor", new CharacterAppearance(CharacterAppearance.VERSION_3, FEMALE,
                    NeutralSliders(CharacterAppearance.FACE_SLIDER_COUNT), NeutralSliders(CharacterAppearance.BODY_SLIDER_COUNT),
                    1, 1, 1, 0, 1, 1, 1, 31, 0, 2)),
                ("FaceSlider", new CharacterAppearance(CharacterAppearance.VERSION_3, FEMALE,
                    MutatedSliders(CharacterAppearance.FACE_SLIDER_COUNT), NeutralSliders(CharacterAppearance.BODY_SLIDER_COUNT),
                    1, 1, 0, 0, 1, 1, 1, 31, 0, 2)),
                ("Version4", new CharacterAppearance(CharacterAppearance.CURRENT_VERSION + 1, FEMALE,
                    NeutralSliders(CharacterAppearance.FACE_SLIDER_COUNT), NeutralSliders(CharacterAppearance.BODY_SLIDER_COUNT),
                    1, 1, 0, 0, 1, 1, 1, 31, 0, 2)),
            };

            for (int i = 0; i < cases.Length; ++i)
            {
                (string label, CharacterAppearance appearance) = cases[i];
                using HttpResponseMessage response = await player.PostAsync(ApiRoutes.CHARACTERS, AccountApiHost.Json(
                    new CharacterCreateRequest(Guid.NewGuid().ToString("D"), "Ext" + i, ContractCodes.PlayerClass.WARRIOR, appearance)));
                Assert.True(HttpStatusCode.BadRequest == response.StatusCode, label);
                Assert.True(ContractCodes.RejectReason.INVALID_APPEARANCE
                    == AccountApiHost.Read<ApiRejection>(await response.Content.ReadAsStringAsync()).ReasonCode, label);
            }

            Assert.Equal(0, host.Characters.CreateCalls);
        }

        // 기존 v2 요청의 재시도를 위해 v2도 계속 받는다
        [Fact]
        public async Task CharacterCreate_V2StillAccepted_ReachesRepositoryWithV2Appearance()
        {
            using AccountApiHost host = new AccountApiHost();
            (Guid _, string access) = host.Backend.AddInviteAccount();
            using HttpClient player = host.CreateClient(access);

            using HttpResponseMessage response = await player.PostAsync(ApiRoutes.CHARACTERS, AccountApiHost.Json(
                new CharacterCreateRequest(Guid.NewGuid().ToString("D"), "여우", ContractCodes.PlayerClass.SWORDSMAN,
                    CharacterTestData.AppearanceV2(CharacterAppearanceCatalog.FACE_CUTE, 2, 3))));

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(1, host.Characters.CreateCalls);
            Assert.Equal(CharacterAppearance.VERSION_2,
                AccountApiHost.Read<CharacterAppearance>(host.Characters.LastAppearanceJson!).Version);
        }

        [Fact]
        public async Task SignupLoginAndPasswordChange_NeverLogPasswordText()
        {
            using AccountApiHost host = new AccountApiHost();
            using HttpClient client = host.CreateClient();
            await SignupAsync(client, LOGIN_ID, PASSWORD, EMAIL);
            using HttpResponseMessage failed = await LoginAsync(client, LOGIN_ID, "Wrong9Password", null);
            AuthExchangeResponse tokens = await LoginTokensAsync(client, LOGIN_ID, PASSWORD);
            using HttpClient authenticated = host.CreateClient(tokens.AccessToken);
            using HttpResponseMessage changed = await authenticated.PostAsync(ApiRoutes.ACCOUNT_CREDENTIALS,
                AccountApiHost.Json(AccountCredentialsRequest.ForPasswordChange(PASSWORD, NEW_PASSWORD)));

            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
            Assert.NotEmpty(host.Logs.Entries);
            Assert.DoesNotContain(host.Logs.Entries, entry => entry.Message.Contains(PASSWORD, StringComparison.Ordinal)
                || entry.Message.Contains(NEW_PASSWORD, StringComparison.Ordinal)
                || entry.Message.Contains("Wrong9Password", StringComparison.Ordinal));
        }

        private static async Task SignupAsync(HttpClient client, string loginId, string password, string email)
        {
            using HttpResponseMessage response = await client.PostAsync(ApiRoutes.ACCOUNTS,
                AccountApiHost.Json(new AccountSignupRequest(loginId, password, email)));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string loginId, string password,
            ClientBuildInfo? build)
        {
            return client.PostAsync(ApiRoutes.AUTH_EXCHANGE,
                AccountApiHost.Json(AuthExchangeRequest.ForCredentials(loginId, password, build!)));
        }

        private static async Task<AuthExchangeResponse> LoginTokensAsync(HttpClient client, string loginId,
            string password)
        {
            using HttpResponseMessage response = await LoginAsync(client, loginId, password, null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return AccountApiHost.Read<AuthExchangeResponse>(await response.Content.ReadAsStringAsync());
        }

        private static async Task<AvailabilityResponse> AvailabilityAsync(HttpClient client, string loginId)
        {
            using HttpResponseMessage response = await client.GetAsync(ApiRoutes.ACCOUNT_LOGIN_ID_AVAILABILITY
                + "?loginId=" + Uri.EscapeDataString(loginId));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return AccountApiHost.Read<AvailabilityResponse>(await response.Content.ReadAsStringAsync());
        }

        private static async Task<AvailabilityResponse> NameAvailabilityAsync(HttpClient client, string name)
        {
            using HttpResponseMessage response = await client.GetAsync(ApiRoutes.CHARACTER_NAME_AVAILABILITY
                + "?name=" + Uri.EscapeDataString(name));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return AccountApiHost.Read<AvailabilityResponse>(await response.Content.ReadAsStringAsync());
        }

        // 기본은 전부 중립인 v2 외형. 지정한 값만 바꿔 거절 사례를 만든다
        private static CharacterAppearance V2Appearance(int[]? face = null, int[]? body = null,
            int skinTone = CharacterAppearance.SLIDER_NEUTRAL, int eyeColor = CharacterAppearance.VALUE_MIN,
            int browColor = CharacterAppearance.VALUE_MIN, int lipColor = CharacterAppearance.VALUE_MIN,
            int hairId = CharacterAppearanceCatalog.HAIR_WARRIOR, int hairColor = CharacterAppearance.VALUE_MIN,
            int faceId = CharacterAppearanceCatalog.FACE_CUTE, int outfitId = CharacterAppearanceCatalog.OUTFIT_WARRIOR)
        {
            return new CharacterAppearance(CharacterAppearance.VERSION_2, CharacterAppearance.BODY_TYPE_FEMALE,
                face ?? NeutralSliders(CharacterAppearance.FACE_SLIDER_COUNT),
                body ?? NeutralSliders(CharacterAppearance.BODY_SLIDER_COUNT), skinTone, eyeColor, browColor, lipColor,
                hairId, hairColor, faceId, outfitId);
        }

        private static int[] NeutralSliders(int count)
        {
            int[] values = new int[count];
            for (int i = 0; i < count; ++i)
            {
                values[i] = CharacterAppearance.SLIDER_NEUTRAL;
            }

            return values;
        }

        private static int[] MutatedSliders(int count)
        {
            int[] values = NeutralSliders(count);
            values[0] = CharacterAppearance.SLIDER_NEUTRAL - 1;
            return values;
        }
    }
}
