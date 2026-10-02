// System
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class LauncherSessionRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string HEX_32           = "0123456789abcdef0123456789ABCDEF";
        private const string ACCOUNT_ID       = "acc-1";
        private const string TEST_TOKEN       = "test-game-session-token";
        private const string API_BASE_URL     = "https://api.example.test";
        private const long   SERVER_TIME      = 1700000000000L;
        private const long   LIFETIME_MS      = 600000L;
        private const int    LAUNCHER_PID     = 4321;
        private const int    GAME_PID         = 8765;
        private const long   FREQUENCY        = 10000000L;
        private const long   RECEIVED_AT      = 5000L;
        private const long   REMAINING_MS     = 5000L;
        private const long   STEP_MS          = 2000L;
        private const long   MS_PER_SECOND    = 1000L;
        private const int    PIPE_TIMEOUT_MS  = 5000;
        private const int    MISSING_PIPE_MS  = 200;
        private const int    TRUNCATED_LENGTH = 3;

        private static readonly string VALID_PIPE = LauncherPipeProtocol.PIPE_NAME_PREFIX + HEX_32;

        /****************************************
        *            Launch Args Tests
        ****************************************/

        [Test]
        public void ParseLaunchArgs_NullArgs_ReturnsAbsent()
        {
            Assert.AreEqual(ELauncherArgsStatus.Absent, LauncherSessionRules.ParseLaunchArgs(null, out _, out _));
        }

        [Test]
        public void ParseLaunchArgs_NoLauncherArgs_ReturnsAbsent()
        {
            string[] args = { "Shinshi.exe", "-screen-fullscreen", "0" };

            Assert.AreEqual(ELauncherArgsStatus.Absent, LauncherSessionRules.ParseLaunchArgs(args, out _, out _));
        }

        [Test]
        public void ParseLaunchArgs_BothArgs_ReturnsValidWithValues()
        {
            string[] args = { "Shinshi.exe", LauncherPipeProtocol.ARG_PIPE, VALID_PIPE, LauncherPipeProtocol.ARG_PID, "4321" };

            ELauncherArgsStatus status = LauncherSessionRules.ParseLaunchArgs(args, out string pipeName, out int pid);

            Assert.AreEqual(ELauncherArgsStatus.Valid, status);
            Assert.AreEqual(VALID_PIPE, pipeName);
            Assert.AreEqual(LAUNCHER_PID, pid);
        }

        [Test]
        public void ParseLaunchArgs_OnlyPipeArg_ReturnsInvalid()
        {
            string[] args = { "Shinshi.exe", LauncherPipeProtocol.ARG_PIPE, VALID_PIPE };

            Assert.AreEqual(ELauncherArgsStatus.Invalid, LauncherSessionRules.ParseLaunchArgs(args, out _, out _));
        }

        [Test]
        public void ParseLaunchArgs_OnlyPidArg_ReturnsInvalid()
        {
            string[] args = { "Shinshi.exe", LauncherPipeProtocol.ARG_PID, "4321" };

            Assert.AreEqual(ELauncherArgsStatus.Invalid, LauncherSessionRules.ParseLaunchArgs(args, out _, out _));
        }

        [TestCase("abc")]
        [TestCase("12a")]
        [TestCase("-5")]
        [TestCase("+12")]
        [TestCase(" 12")]
        [TestCase("0")]
        [TestCase("99999999999")]
        [TestCase("")]
        public void ParseLaunchArgs_BadPid_ReturnsInvalid(string pidValue)
        {
            string[] args = { LauncherPipeProtocol.ARG_PIPE, VALID_PIPE, LauncherPipeProtocol.ARG_PID, pidValue };

            ELauncherArgsStatus status = LauncherSessionRules.ParseLaunchArgs(args, out string pipeName, out int pid);

            Assert.AreEqual(ELauncherArgsStatus.Invalid, status);
            Assert.IsNull(pipeName);
            Assert.AreEqual(0, pid);
        }

        [Test]
        public void ParseLaunchArgs_PidFlagWithoutValue_ReturnsInvalid()
        {
            string[] args = { LauncherPipeProtocol.ARG_PIPE, VALID_PIPE, LauncherPipeProtocol.ARG_PID };

            Assert.AreEqual(ELauncherArgsStatus.Invalid, LauncherSessionRules.ParseLaunchArgs(args, out _, out _));
        }

        [Test]
        public void ParseLaunchArgs_DuplicatePipeArg_ReturnsInvalid()
        {
            string[] args =
            {
                LauncherPipeProtocol.ARG_PIPE, VALID_PIPE, LauncherPipeProtocol.ARG_PIPE, VALID_PIPE,
                LauncherPipeProtocol.ARG_PID, "4321"
            };

            Assert.AreEqual(ELauncherArgsStatus.Invalid, LauncherSessionRules.ParseLaunchArgs(args, out _, out _));
        }

        [Test]
        public void ParseLaunchArgs_BadPipeName_ReturnsInvalid()
        {
            string[] args = { LauncherPipeProtocol.ARG_PIPE, @"..\..\pipe\other", LauncherPipeProtocol.ARG_PID, "4321" };

            Assert.AreEqual(ELauncherArgsStatus.Invalid, LauncherSessionRules.ParseLaunchArgs(args, out _, out _));
        }

        /****************************************
        *             Pipe Name Tests
        ****************************************/

        [Test]
        public void IsValidPipeName_PrefixAnd32Hex_ReturnsTrue()
        {
            Assert.IsTrue(LauncherSessionRules.IsValidPipeName(VALID_PIPE));
        }

        [Test]
        public void IsValidPipeName_Null_ReturnsFalse()
        {
            Assert.IsFalse(LauncherSessionRules.IsValidPipeName(null));
        }

        [TestCase("")]
        [TestCase("shinshi-launcher-")]
        [TestCase("shinshi-launcher-0123456789abcdef0123456789abcde")]
        [TestCase("shinshi-launcher-0123456789abcdef0123456789abcdef0")]
        [TestCase("shinshi-launcher-0123456789abcdef0123456789abcdeg")]
        [TestCase("SHINSHI-LAUNCHER-0123456789abcdef0123456789abcdef")]
        [TestCase("other-launcher-00-0123456789abcdef0123456789abcdef")]
        [TestCase("shinshi-launcher-0123456789abcdef0123456789abcd\\x")]
        [TestCase("shinshi-launcher-0123456789abcdef0123456789abcd/x")]
        [TestCase("shinshi-launcher-../../0123456789abcdef0123456789")]
        [TestCase(@"\\.\pipe\shinshi-launcher-0123456789abcdef0123456789abcdef")]
        [TestCase("shinshi-launcher-0123456789abcdef 123456789abcdef")]
        public void IsValidPipeName_BadName_ReturnsFalse(string pipeName)
        {
            Assert.IsFalse(LauncherSessionRules.IsValidPipeName(pipeName));
        }

        /****************************************
        *           Handoff Payload Tests
        ****************************************/

        [Test]
        public void TryDeserializeHandoff_ContractJson_ReadsAllFields()
        {
            Assert.IsTrue(LauncherSessionRules.TryDeserializeHandoff(Utf8(BuildHandoffJson(LauncherPipeProtocol.PROTOCOL_VERSION)),
                out LauncherHandoff handoff));

            Assert.AreEqual(LauncherPipeProtocol.PROTOCOL_VERSION, handoff.ProtocolVersion);
            Assert.AreEqual(ACCOUNT_ID, handoff.AccountId);
            Assert.AreEqual(TEST_TOKEN, handoff.GameSessionToken);
            Assert.AreEqual(SERVER_TIME + LIFETIME_MS, handoff.ExpiresAtUnixMs);
            Assert.AreEqual(SERVER_TIME, handoff.ServerTimeUnixMs);
            Assert.AreEqual(API_BASE_URL, handoff.ApiBaseUrl);
        }

        [Test]
        public void TryDeserializeHandoff_MalformedJson_ReturnsFalse()
        {
            Assert.IsFalse(LauncherSessionRules.TryDeserializeHandoff(Utf8("{\"protocolVersion\":"), out LauncherHandoff handoff));
            Assert.IsNull(handoff);
        }

        [Test]
        public void TryDeserializeHandoff_InvalidUtf8_ReturnsFalse()
        {
            byte[] body = { (byte)'{', 0xC3, 0x28, (byte)'}' };

            Assert.IsFalse(LauncherSessionRules.TryDeserializeHandoff(body, out _));
        }

        [Test]
        public void TryDeserializeHandoff_EmptyBody_ReturnsFalse()
        {
            Assert.IsFalse(LauncherSessionRules.TryDeserializeHandoff(new byte[0], out _));
            Assert.IsFalse(LauncherSessionRules.TryDeserializeHandoff(null, out _));
        }

        [Test]
        public void ValidateHandoff_ValidSession_ReturnsNone()
        {
            Assert.AreEqual(ELauncherSessionFailure.None, LauncherSessionRules.ValidateHandoff(CreateHandoff()));
        }

        [Test]
        public void ValidateHandoff_Null_ReturnsInvalidPayload()
        {
            Assert.AreEqual(ELauncherSessionFailure.InvalidPayload, LauncherSessionRules.ValidateHandoff(null));
        }

        [Test]
        public void ValidateHandoff_WrongProtocol_ReturnsProtocolMismatch()
        {
            var handoff = new LauncherHandoff(LauncherPipeProtocol.PROTOCOL_VERSION + 1, ACCOUNT_ID, TEST_TOKEN,
                SERVER_TIME + LIFETIME_MS, SERVER_TIME, API_BASE_URL);

            Assert.AreEqual(ELauncherSessionFailure.ProtocolMismatch, LauncherSessionRules.ValidateHandoff(handoff));
        }

        [Test]
        public void ValidateHandoff_MissingProtocolField_ReturnsProtocolMismatch()
        {
            string json = "{\"accountId\":\"acc-1\",\"gameSessionToken\":\"t\",\"expiresAtUnixMs\":2,\"serverTimeUnixMs\":1,"
                + "\"apiBaseUrl\":\"https://api.example.test\"}";
            Assert.IsTrue(LauncherSessionRules.TryDeserializeHandoff(Utf8(json), out LauncherHandoff handoff));

            Assert.AreEqual(ELauncherSessionFailure.ProtocolMismatch, LauncherSessionRules.ValidateHandoff(handoff));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void ValidateHandoff_EmptyToken_ReturnsEmptyToken(string token)
        {
            var handoff = new LauncherHandoff(LauncherPipeProtocol.PROTOCOL_VERSION, ACCOUNT_ID, token,
                SERVER_TIME + LIFETIME_MS, SERVER_TIME, API_BASE_URL);

            Assert.AreEqual(ELauncherSessionFailure.EmptyToken, LauncherSessionRules.ValidateHandoff(handoff));
        }

        [Test]
        public void ValidateHandoff_EmptyAccount_ReturnsEmptyAccount()
        {
            var handoff = new LauncherHandoff(LauncherPipeProtocol.PROTOCOL_VERSION, string.Empty, TEST_TOKEN,
                SERVER_TIME + LIFETIME_MS, SERVER_TIME, API_BASE_URL);

            Assert.AreEqual(ELauncherSessionFailure.EmptyAccount, LauncherSessionRules.ValidateHandoff(handoff));
        }

        [TestCase(0L)]
        [TestCase(-1L)]
        [TestCase(-600000L)]
        public void ValidateHandoff_ExpiredAgainstServerTime_ReturnsExpired(long lifetimeMs)
        {
            var handoff = new LauncherHandoff(LauncherPipeProtocol.PROTOCOL_VERSION, ACCOUNT_ID, TEST_TOKEN,
                SERVER_TIME + lifetimeMs, SERVER_TIME, API_BASE_URL);

            Assert.AreEqual(ELauncherSessionFailure.Expired, LauncherSessionRules.ValidateHandoff(handoff));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("not a url")]
        [TestCase("file:///C:/Windows")]
        [TestCase("/v1/auth")]
        public void ValidateHandoff_BadApiBaseUrl_ReturnsInvalidApiBaseUrl(string apiBaseUrl)
        {
            var handoff = new LauncherHandoff(LauncherPipeProtocol.PROTOCOL_VERSION, ACCOUNT_ID, TEST_TOKEN,
                SERVER_TIME + LIFETIME_MS, SERVER_TIME, apiBaseUrl);

            Assert.AreEqual(ELauncherSessionFailure.InvalidApiBaseUrl, LauncherSessionRules.ValidateHandoff(handoff));
        }

        /****************************************
        *               Expiry Tests
        ****************************************/

        [Test]
        public void ComputeRemainingMs_AfterMonotonicStep_ReturnsReducedTime()
        {
            long expiry = LauncherSessionRules.ComputeExpiryTimestamp(RECEIVED_AT, REMAINING_MS, FREQUENCY);
            long later  = RECEIVED_AT + ((STEP_MS * FREQUENCY) / MS_PER_SECOND);

            Assert.AreEqual(REMAINING_MS, LauncherSessionRules.ComputeRemainingMs(expiry, RECEIVED_AT, FREQUENCY));
            Assert.AreEqual(REMAINING_MS - STEP_MS, LauncherSessionRules.ComputeRemainingMs(expiry, later, FREQUENCY));
        }

        [Test]
        public void ComputeRemainingMs_PastExpiry_ReturnsZero()
        {
            long expiry = LauncherSessionRules.ComputeExpiryTimestamp(RECEIVED_AT, REMAINING_MS, FREQUENCY);

            Assert.AreEqual(0, LauncherSessionRules.ComputeRemainingMs(expiry, expiry, FREQUENCY));
            Assert.AreEqual(0, LauncherSessionRules.ComputeRemainingMs(expiry, expiry + FREQUENCY, FREQUENCY));
        }

        [Test]
        public void ComputeExpiryTimestamp_HugeRemaining_DoesNotOverflow()
        {
            long expiry = LauncherSessionRules.ComputeExpiryTimestamp(RECEIVED_AT, long.MaxValue, FREQUENCY);

            Assert.AreEqual(long.MaxValue, expiry);
            Assert.Greater(LauncherSessionRules.ComputeRemainingMs(expiry, RECEIVED_AT, FREQUENCY), 0);
        }

        /****************************************
        *               Frame Tests
        ****************************************/

        [Test]
        public void ReadFrame_HandoffWrittenByProtocol_RoundTripsThroughRules()
        {
            using (var stream = new MemoryStream())
            {
                LauncherPipeProtocol.WriteFrame(stream, Utf8(BuildHandoffJson(LauncherPipeProtocol.PROTOCOL_VERSION)));
                stream.Position = 0;

                Assert.IsTrue(LauncherPipeProtocol.TryReadFrame(stream, out byte[] body));
                Assert.IsTrue(LauncherSessionRules.TryDeserializeHandoff(body, out LauncherHandoff handoff));
                Assert.AreEqual(ELauncherSessionFailure.None, LauncherSessionRules.ValidateHandoff(handoff));
                Assert.AreEqual(TEST_TOKEN, handoff.GameSessionToken);
            }
        }

        [Test]
        public void SerializeAck_WrittenAsFrame_ContainsContractFields()
        {
            using (var stream = new MemoryStream())
            {
                byte[] ackBody = LauncherSessionRules.SerializeAck(
                    new LauncherHandoffAck(LauncherPipeProtocol.PROTOCOL_VERSION, GAME_PID));
                LauncherPipeProtocol.WriteFrame(stream, ackBody);
                stream.Position = 0;

                Assert.IsTrue(LauncherPipeProtocol.TryReadFrame(stream, out byte[] body));
                string json = Encoding.UTF8.GetString(body);
                StringAssert.Contains("\"protocolVersion\":" + Invariant(LauncherPipeProtocol.PROTOCOL_VERSION), json);
                StringAssert.Contains("\"gamePid\":" + Invariant(GAME_PID), json);
            }
        }

        [Test]
        public void ReadFrame_TruncatedStream_ReturnsFalse()
        {
            using (var stream = new MemoryStream())
            {
                LauncherPipeProtocol.WriteFrame(stream, Utf8(BuildHandoffJson(LauncherPipeProtocol.PROTOCOL_VERSION)));
                stream.SetLength(stream.Length - TRUNCATED_LENGTH);
                stream.Position = 0;

                Assert.IsFalse(LauncherPipeProtocol.TryReadFrame(stream, out _));
            }
        }

        /****************************************
        *            Real Pipe Tests
        ****************************************/

        [Test]
        public void ReceiveHandoff_RealPipeFromLauncherPid_ReturnsReceivedAndSendsAck()
        {
            string    pipeName    = NewPipeName();
            int       currentPid  = CurrentProcessId();
            byte[]    ackBody     = null;
            bool      isAckRead   = false;
            Exception serverError = null;

            using (var server = CreateServer(pipeName))
            {
                Thread serverThread = StartServer(() =>
                {
                    server.WaitForConnection();
                    LauncherPipeProtocol.WriteFrame(server, Utf8(BuildHandoffJson(LauncherPipeProtocol.PROTOCOL_VERSION)));
                    isAckRead = LauncherPipeProtocol.TryReadFrame(server, out ackBody);
                }, e => serverError = e);

                // 서버가 같은 프로세스라 런처 PID 자리에 현재 PID를 넣으면 실제 커널 PID 검사를 그대로 통과해야 한다
                LauncherReceiveResult result = LauncherSessionReceiver.ReceiveHandoff(pipeName, currentPid, GAME_PID,
                    PIPE_TIMEOUT_MS, LauncherSessionReceiver.VerifyServerProcess);

                Assert.IsTrue(serverThread.Join(PIPE_TIMEOUT_MS));
                Assert.IsNull(serverError, serverError?.ToString());
                Assert.IsTrue(result.IsReceived, result.Failure + " " + result.Detail);
                Assert.AreEqual(TEST_TOKEN, result.Handoff.GameSessionToken);
                Assert.IsTrue(isAckRead);
                StringAssert.Contains("\"gamePid\":" + Invariant(GAME_PID), Encoding.UTF8.GetString(ackBody));
            }
        }

        [Test]
        public void ReceiveHandoff_ServerPidDiffersFromLauncherPid_ReturnsMismatchWithoutAck()
        {
            string    pipeName    = NewPipeName();
            int       currentPid  = CurrentProcessId();
            bool      isRead      = false;
            Exception serverError = null;

            using (var server = CreateServer(pipeName))
            {
                Thread serverThread = StartServer(() =>
                {
                    server.WaitForConnection();
                    isRead = LauncherPipeProtocol.TryReadFrame(server, out _);
                }, e => serverError = e);

                LauncherReceiveResult result = LauncherSessionReceiver.ReceiveHandoff(pipeName, currentPid + 1, GAME_PID,
                    PIPE_TIMEOUT_MS, LauncherSessionReceiver.VerifyServerProcess);

                Assert.IsTrue(serverThread.Join(PIPE_TIMEOUT_MS));
                Assert.AreEqual(ELauncherSessionFailure.ServerProcessMismatch, result.Failure);
                Assert.IsFalse(isRead);
                Assert.IsTrue((serverError == null) || (serverError is IOException), serverError?.ToString());
            }
        }

        [Test]
        public void ReceiveHandoff_ProtocolMismatchOverPipe_ReturnsMismatchWithoutAck()
        {
            string    pipeName    = NewPipeName();
            bool      isAckRead   = false;
            Exception serverError = null;

            using (var server = CreateServer(pipeName))
            {
                Thread serverThread = StartServer(() =>
                {
                    server.WaitForConnection();
                    LauncherPipeProtocol.WriteFrame(server, Utf8(BuildHandoffJson(LauncherPipeProtocol.PROTOCOL_VERSION + 1)));
                    isAckRead = LauncherPipeProtocol.TryReadFrame(server, out _);
                }, e => serverError = e);

                LauncherReceiveResult result = LauncherSessionReceiver.ReceiveHandoff(pipeName, LAUNCHER_PID, GAME_PID,
                    PIPE_TIMEOUT_MS, (pipe, pid) => true);

                Assert.IsTrue(serverThread.Join(PIPE_TIMEOUT_MS));
                Assert.AreEqual(ELauncherSessionFailure.ProtocolMismatch, result.Failure);
                Assert.IsFalse(isAckRead);
                Assert.IsTrue((serverError == null) || (serverError is IOException), serverError?.ToString());
            }
        }

        [Test]
        public void ReceiveHandoff_NoPipeServer_ReturnsConnectFailed()
        {
            LauncherReceiveResult result = LauncherSessionReceiver.ReceiveHandoff(NewPipeName(), LAUNCHER_PID, GAME_PID,
                MISSING_PIPE_MS, (pipe, pid) => true);

            Assert.AreEqual(ELauncherSessionFailure.ConnectFailed, result.Failure);
        }

        [Test]
        public void ReceiveHandoff_BadPipeName_ReturnsInvalidArgsWithoutConnecting()
        {
            LauncherReceiveResult result = LauncherSessionReceiver.ReceiveHandoff(@"..\other", LAUNCHER_PID, GAME_PID,
                MISSING_PIPE_MS, (pipe, pid) => true);

            Assert.AreEqual(ELauncherSessionFailure.InvalidArgs, result.Failure);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static LauncherHandoff CreateHandoff()
        {
            return new LauncherHandoff(LauncherPipeProtocol.PROTOCOL_VERSION, ACCOUNT_ID, TEST_TOKEN,
                SERVER_TIME + LIFETIME_MS, SERVER_TIME, API_BASE_URL);
        }

        // 계약 이름이 바뀌면 깨지도록 런처가 보낼 JSON을 직접 쓴다
        private static string BuildHandoffJson(int protocolVersion)
        {
            return "{\"protocolVersion\":" + Invariant(protocolVersion)
                + ",\"accountId\":\"" + ACCOUNT_ID + "\""
                + ",\"gameSessionToken\":\"" + TEST_TOKEN + "\""
                + ",\"expiresAtUnixMs\":" + Invariant(SERVER_TIME + LIFETIME_MS)
                + ",\"serverTimeUnixMs\":" + Invariant(SERVER_TIME)
                + ",\"apiBaseUrl\":\"" + API_BASE_URL + "\"}";
        }

        private static string Invariant(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static byte[] Utf8(string value)
        {
            return Encoding.UTF8.GetBytes(value);
        }

        private static string NewPipeName()
        {
            return LauncherPipeProtocol.PIPE_NAME_PREFIX + Guid.NewGuid().ToString("N");
        }

        private static int CurrentProcessId()
        {
            using (Process process = Process.GetCurrentProcess())
            {
                return process.Id;
            }
        }

        private static NamedPipeServerStream CreateServer(string pipeName)
        {
            return new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None);
        }

        private static Thread StartServer(Action body, Action<Exception> onError)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    body();
                }
                catch (Exception e)
                {
                    onError(e);
                }
            });
            thread.IsBackground = true;
            thread.Start();
            return thread;
        }
    }
}
