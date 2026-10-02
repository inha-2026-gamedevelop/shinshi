// System
using System;
using System.Globalization;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Newtonsoft.Json;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Launcher.Account;

namespace Shinshi.Launcher.Tests.Account
{
    /// <summary> 파이프 세션 전달을 한 프로세스 안에서 런처 쪽과 게임 쪽으로 나눠 확인한다 </summary>
    public sealed class GameSessionHandoffTests
    {
        // 게임 쪽 클라이언트는 이 시험 프로세스 자신이므로 가짜 게임의 PID를 현재 PID로 두면 실제 GetNamedPipeClientProcessId 확인을 통과한다

        /****************************************
        *                Fields
        ****************************************/

        private const string API_BASE_URL       = "http://127.0.0.1:5080";
        private const int    CONNECT_TIMEOUT_MS = 5000;
        private const int    WAIT_MS            = 10000;
        private const int    SHORT_TIMEOUT_MS   = 300;
        private const int    WRONG_PROTOCOL     = 99;
        private const int    ARG_COUNT          = 4;
        private const int    PIPE_ARG_INDEX     = 1;
        private const int    PID_ARG_INDEX      = 3;
        private const int    PIPE_HEX_LENGTH    = 32;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public async Task RunAsync_GameAcks_DeliversSessionOnceWithoutTokenInArguments()
        {
            FakeHttpHandler handler = RevokeHandler();
            FakeGame        game    = new FakeGame(Environment.ProcessId);
            string          args    = string.Empty;
            Task<LauncherHandoff?> client = Task.FromResult<LauncherHandoff?>(null);
            GameSessionHandoff handoff = CreateHandoff(handler, arguments =>
            {
                args   = arguments;
                client = RunGameClientAsync(ParsePipeName(arguments), _ => ValidAck());
                return game;
            });

            GameHandoffResult result = await handoff.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));
            LauncherHandoff? received = await client.WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));

            Assert.True(result.IsSuccess, result.Error.ToString());
            Assert.Same(game, result.Game);
            Assert.False(game.IsKilled);
            Assert.Empty(handler.Requests);

            Assert.NotNull(received);
            Assert.Equal(LauncherPipeProtocol.PROTOCOL_VERSION, received.ProtocolVersion);
            Assert.Equal(AccountTestData.ACCOUNT_ID, received.AccountId);
            Assert.Equal(AccountTestData.GAME_TOKEN, received.GameSessionToken);
            Assert.Equal(API_BASE_URL, received.ApiBaseUrl);

            string[] parts = args.Split(' ');
            Assert.Equal(ARG_COUNT, parts.Length);
            Assert.Equal(LauncherPipeProtocol.ARG_PIPE, parts[0]);
            Assert.Equal(LauncherPipeProtocol.ARG_PID, parts[2]);
            Assert.Equal(Environment.ProcessId.ToString(CultureInfo.InvariantCulture), parts[PID_ARG_INDEX]);
            Assert.DoesNotContain(AccountTestData.GAME_TOKEN, args, StringComparison.Ordinal);
            AssertPipeName(parts[PIPE_ARG_INDEX]);

            GameHandoffResult second = await handoff.RunAsync(CancellationToken.None);
            Assert.Equal(EGameHandoffError.AlreadyUsed, second.Error);
        }

        [Fact]
        public async Task RunAsync_GameNeverConnects_TimesOutKillsAndRevokes()
        {
            FakeHttpHandler handler = RevokeHandler();
            FakeGame        game    = new FakeGame(Environment.ProcessId);
            GameSessionHandoff handoff = CreateHandoff(handler, _ => game, TimeSpan.FromMilliseconds(SHORT_TIMEOUT_MS));

            GameHandoffResult result = await handoff.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));

            Assert.Equal(EGameHandoffError.Timeout, result.Error);
            Assert.True(game.IsKilled);
            AssertRevoked(handler, result);
        }

        [Fact]
        public async Task RunAsync_CallerCancels_IsCancelledKillsAndRevokes()
        {
            FakeHttpHandler handler = RevokeHandler();
            FakeGame        game    = new FakeGame(Environment.ProcessId);
            using CancellationTokenSource cancel = new CancellationTokenSource(SHORT_TIMEOUT_MS);
            GameSessionHandoff handoff = CreateHandoff(handler, _ => game);

            GameHandoffResult result = await handoff.RunAsync(cancel.Token).WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));

            Assert.Equal(EGameHandoffError.Cancelled, result.Error);
            Assert.True(game.IsKilled);
            AssertRevoked(handler, result);
        }

        [Fact]
        public async Task RunAsync_WrongAckProtocol_RejectedKillsAndRevokes()
        {
            FakeHttpHandler handler = RevokeHandler();
            FakeGame        game    = new FakeGame(Environment.ProcessId);
            Task<LauncherHandoff?> client = Task.FromResult<LauncherHandoff?>(null);
            GameSessionHandoff handoff = CreateHandoff(handler, arguments =>
            {
                client = RunGameClientAsync(ParsePipeName(arguments),
                    _ => new LauncherHandoffAck(WRONG_PROTOCOL, Environment.ProcessId));
                return game;
            });

            GameHandoffResult result = await handoff.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));
            await client.WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));

            Assert.Equal(EGameHandoffError.BadAck, result.Error);
            Assert.True(game.IsKilled);
            AssertRevoked(handler, result);
        }

        [Fact]
        public async Task RunAsync_AckWithOtherPid_Rejected()
        {
            FakeHttpHandler handler = RevokeHandler();
            FakeGame        game    = new FakeGame(Environment.ProcessId);
            Task<LauncherHandoff?> client = Task.FromResult<LauncherHandoff?>(null);
            GameSessionHandoff handoff = CreateHandoff(handler, arguments =>
            {
                client = RunGameClientAsync(ParsePipeName(arguments),
                    _ => new LauncherHandoffAck(LauncherPipeProtocol.PROTOCOL_VERSION, Environment.ProcessId + 1));
                return game;
            });

            GameHandoffResult result = await handoff.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));
            await client.WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));

            Assert.Equal(EGameHandoffError.BadAck, result.Error);
            Assert.True(game.IsKilled);
            AssertRevoked(handler, result);
        }

        [Fact]
        public async Task RunAsync_GameClosesWithoutAck_RejectedKillsAndRevokes()
        {
            FakeHttpHandler handler = RevokeHandler();
            FakeGame        game    = new FakeGame(Environment.ProcessId);
            Task<LauncherHandoff?> client = Task.FromResult<LauncherHandoff?>(null);
            GameSessionHandoff handoff = CreateHandoff(handler, arguments =>
            {
                client = RunGameClientAsync(ParsePipeName(arguments), _ => null);
                return game;
            });

            GameHandoffResult result = await handoff.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));
            await client.WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));

            Assert.True((result.Error == EGameHandoffError.BadAck) || (result.Error == EGameHandoffError.PipeBroken),
                result.Error.ToString());
            Assert.True(game.IsKilled);
            AssertRevoked(handler, result);
        }

        [Fact]
        public async Task RunAsync_ClientIsNotStartedGame_SendsNothing()
        {
            FakeHttpHandler handler = RevokeHandler();
            FakeGame        game    = new FakeGame(Environment.ProcessId + 1);
            Task<LauncherHandoff?> client = Task.FromResult<LauncherHandoff?>(null);
            GameSessionHandoff handoff = CreateHandoff(handler, arguments =>
            {
                client = RunGameClientAsync(ParsePipeName(arguments), _ => ValidAck());
                return game;
            });

            GameHandoffResult result = await handoff.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));
            LauncherHandoff? received = await client.WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));

            Assert.Equal(EGameHandoffError.PidMismatch, result.Error);
            Assert.Null(received);
            Assert.True(game.IsKilled);
            AssertRevoked(handler, result);
        }

        [Fact]
        public async Task RunAsync_PidReaderFails_TreatedAsMismatch()
        {
            FakeHttpHandler handler = RevokeHandler();
            FakeGame        game    = new FakeGame(Environment.ProcessId);
            Task<LauncherHandoff?> client = Task.FromResult<LauncherHandoff?>(null);
            GameHandoffOptions options = new GameHandoffOptions
            {
                ClientProcessIdReader = _ => throw new System.ComponentModel.Win32Exception(5)
            };
            GameSessionHandoff handoff = new GameSessionHandoff(AccountTestData.Client(handler), AccountTestData.GameSession(),
                API_BASE_URL, arguments =>
                {
                    client = RunGameClientAsync(ParsePipeName(arguments), _ => ValidAck());
                    return game;
                }, options);

            GameHandoffResult result = await handoff.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));
            LauncherHandoff? received = await client.WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));

            Assert.Equal(EGameHandoffError.PidMismatch, result.Error);
            Assert.Null(received);
            AssertRevoked(handler, result);
        }

        [Fact]
        public async Task RunAsync_StartFails_RevokesWithoutKill()
        {
            FakeHttpHandler handler = RevokeHandler();
            GameSessionHandoff handoff = CreateHandoff(handler, _ => null);

            GameHandoffResult result = await handoff.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(WAIT_MS));

            Assert.Equal(EGameHandoffError.StartFailed, result.Error);
            Assert.Null(result.Game);
            AssertRevoked(handler, result);
        }

        private static GameSessionHandoff CreateHandoff(FakeHttpHandler handler, Func<string, IStartedGame?> startGame,
            TimeSpan? timeout = null)
        {
            GameHandoffOptions options = (timeout == null) ? new GameHandoffOptions() : new GameHandoffOptions { Timeout = timeout.Value };
            return new GameSessionHandoff(AccountTestData.Client(handler), AccountTestData.GameSession(), API_BASE_URL, startGame,
                options);
        }

        private static FakeHttpHandler RevokeHandler()
        {
            return new FakeHttpHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        }

        private static void AssertRevoked(FakeHttpHandler handler, GameHandoffResult result)
        {
            Assert.True(result.IsRevoked);
            RecordedRequest request = Assert.Single(handler.Requests);
            Assert.Equal(ApiRoutes.AUTH_GAME_SESSION_REVOKE, request.Path);
            Assert.Equal(ApiRoutes.BEARER_PREFIX + AccountTestData.GAME_TOKEN, request.Authorization);
        }

        private static void AssertPipeName(string pipeName)
        {
            Assert.StartsWith(LauncherPipeProtocol.PIPE_NAME_PREFIX, pipeName, StringComparison.Ordinal);
            string hex = pipeName.Substring(LauncherPipeProtocol.PIPE_NAME_PREFIX.Length);
            Assert.Equal(PIPE_HEX_LENGTH, hex.Length);
            Assert.All(hex, c => Assert.True(char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c)));
        }

        private static string ParsePipeName(string arguments) { return arguments.Split(' ')[PIPE_ARG_INDEX]; }

        private static LauncherHandoffAck ValidAck() { return new LauncherHandoffAck(LauncherPipeProtocol.PROTOCOL_VERSION, Environment.ProcessId); }

        // 게임 쪽 수신기 역할. 받은 세션을 돌려주고, 전달받지 못했으면 null
        private static Task<LauncherHandoff?> RunGameClientAsync(string pipeName, Func<LauncherHandoff, LauncherHandoffAck?> makeAck)
        {
            return Task.Run(async () =>
            {
                using NamedPipeClientStream client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(CONNECT_TIMEOUT_MS);

                if (!LauncherPipeProtocol.TryReadFrame(client, out byte[] body))
                {
                    return null;
                }

                LauncherHandoff? handoff = JsonConvert.DeserializeObject<LauncherHandoff>(Encoding.UTF8.GetString(body),
                    ContractJson.CreateSettings());
                if (handoff == null)
                {
                    return null;
                }

                LauncherHandoffAck? ack = makeAck(handoff);
                if (ack != null)
                {
                    byte[] ackBody = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ack, ContractJson.CreateSettings()));
                    LauncherPipeProtocol.WriteFrame(client, ackBody);
                }

                return handoff;
            });
        }

        /// <summary> 프로세스 없이 PID와 종료 요청만 기록하는 게임 </summary>
        private sealed class FakeGame : IStartedGame
        {
            private int _killCount;

            public int  ProcessId { get; }
            public bool IsKilled  => Volatile.Read(ref _killCount) > 0;

            public FakeGame(int processId)
            {
                ProcessId = processId;
            }

            public void Kill() { Interlocked.Increment(ref _killCount); }
        }
    }
}
