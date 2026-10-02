// System
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

// Third Party
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Npgsql;

// Project
using Shinshi.Backend.Admin;
using Shinshi.Contracts;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Tests.Admin
{
    /// <summary> 어드민 테스트 공용 도구. 실제 회수 대신 하한 회수를 써서 PBKDF2 시간을 줄인다 </summary>
    internal static class AdminTestSupport
    {
        /****************************************
        *                Fields
        ****************************************/

        // TestServer는 원격 주소가 비어 있으므로 이 헤더 값으로 RemoteIpAddress를 채운다
        public const string REMOTE_IP_HEADER = "X-Test-Remote-Ip";
        public const string LOOPBACK         = "127.0.0.1";
        public const string TEST_PASSWORD    = "correct horse battery staple";
        public const int    TEST_ITERATIONS  = AdminSecuritySettings.MIN_PASSWORD_ITERATIONS;
        public const string BASE32_ALPHABET  = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        // 잠금 대기를 기다리는 시험이 대기를 확인하는 간격과 한도
        private const int LOCK_WAIT_POLL_MS    = 25;
        private const int LOCK_WAIT_TIMEOUT_MS = 5000;

        private const int BASE32_BITS = 5;
        private const int BYTE_BITS   = 8;
        private const int BYTE_MASK   = 0xFF;

        /****************************************
        *                Methods
        ****************************************/

        public static string CreateKeyBase64()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(AdminSecretProtector.KEY_BYTES));
        }

        public static AdminOptions CreateOptions(string? keyBase64 = null)
        {
            return new AdminOptions
            {
                TotpKeyBase64      = keyBase64 ?? CreateKeyBase64(),
                PasswordIterations = TEST_ITERATIONS
            };
        }

        /// <summary> 알고 있는 비밀번호와 TOTP 비밀로 운영자 행을 만든다 </summary>
        public static AdminNewOperator CreateOperator(AdminSecretProtector protector, string loginName, EAdminRole role,
            byte[] totpSecret, Guid? createdBy)
        {
            Guid adminId = Guid.NewGuid();
            AdminPasswordHash password = new AdminPasswordHasher(TEST_ITERATIONS).Hash(TEST_PASSWORD);
            return new AdminNewOperator(adminId, loginName, role, password, protector.Protect(totpSecret, adminId),
                createdBy);
        }

        public static string CodeAt(byte[] secret, DateTimeOffset time)
        {
            return AdminTotp.ComputeCode(secret, AdminTotp.StepOf(time));
        }

        public static byte[] FromBase32(string value)
        {
            List<byte> bytes = new List<byte>();
            int buffer = 0;
            int bits = 0;
            foreach (char character in value)
            {
                buffer = (buffer << BASE32_BITS) | BASE32_ALPHABET.IndexOf(character);
                bits += BASE32_BITS;
                if (bits >= BYTE_BITS)
                {
                    bits -= BYTE_BITS;
                    bytes.Add((byte)((buffer >> bits) & BYTE_MASK));
                }
            }

            return bytes.ToArray();
        }

        public static StringContent Json(object value)
        {
            return new StringContent(JsonConvert.SerializeObject(value, ContractJson.CreateSettings()), Encoding.UTF8,
                "application/json");
        }

        public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        {
            return JsonConvert.DeserializeObject<T>(await response.Content.ReadAsStringAsync(),
                ContractJson.CreateSettings())!;
        }

        /// <summary> blockerPid 백엔드가 쥔 잠금을 기다리는 다른 백엔드가 생길 때까지 기다린다. 한도 안에 없으면 시험 실패다 </summary>
        public static async Task WaitUntilBlockedByAsync(NpgsqlDataSource dataSource, int blockerPid)
        {
            for (int waited = 0; waited < LOCK_WAIT_TIMEOUT_MS; waited += LOCK_WAIT_POLL_MS)
            {
                long blocked = await CountAsync(dataSource,
                    "SELECT count(*) FROM pg_stat_activity WHERE @Pid = ANY(pg_blocking_pids(pid));",
                    new { Pid = blockerPid });
                if (blocked > 0)
                {
                    return;
                }

                await Task.Delay(LOCK_WAIT_POLL_MS);
            }

            throw new TimeoutException("No backend waited for the expected lock.");
        }

        /// <summary> waiterPid 백엔드가 다른 백엔드의 잠금을 기다리기 시작할 때까지 기다린다. 한도 안에 없으면 시험 실패다 </summary>
        public static async Task WaitUntilWaitingAsync(NpgsqlDataSource dataSource, int waiterPid)
        {
            for (int waited = 0; waited < LOCK_WAIT_TIMEOUT_MS; waited += LOCK_WAIT_POLL_MS)
            {
                long blockers = await CountAsync(dataSource, "SELECT cardinality(pg_blocking_pids(@Pid))::bigint;",
                    new { Pid = waiterPid });
                if (blockers > 0)
                {
                    return;
                }

                await Task.Delay(LOCK_WAIT_POLL_MS);
            }

            throw new TimeoutException("The backend did not start waiting for a lock.");
        }

        public static async Task<long> CountAsync(NpgsqlDataSource dataSource, string sql, object? parameters = null)
        {
            await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
            return await Dapper.SqlMapper.QuerySingleAsync<long>(connection, sql, parameters);
        }
    }

    /// <summary> 테스트가 정한 시각을 돌려주는 시계 </summary>
    internal sealed class ManualTimeProvider : TimeProvider
    {
        /****************************************
        *              Properties
        ****************************************/

        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

        /****************************************
        *                Methods
        ****************************************/

        public override DateTimeOffset GetUtcNow() { return Now; }
    }

    /// <summary> 앱 파이프라인보다 먼저 원격 주소를 채운다. 헤더가 없으면 루프백이다 </summary>
    internal sealed class TestRemoteIpStartupFilter : Microsoft.AspNetCore.Hosting.IStartupFilter
    {
        /****************************************
        *                Methods
        ****************************************/

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    string header = context.Request.Headers[AdminTestSupport.REMOTE_IP_HEADER].ToString();
                    context.Connection.RemoteIpAddress = IPAddress.Parse(
                        string.IsNullOrEmpty(header) ? AdminTestSupport.LOOPBACK : header);
                    await nextMiddleware(context);
                });
                next(app);
            };
        }
    }
}
