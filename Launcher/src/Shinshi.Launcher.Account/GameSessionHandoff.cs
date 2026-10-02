// System
using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Newtonsoft.Json;

// Project
using Shinshi.Contracts;

namespace Shinshi.Launcher.Account
{
    /// <summary> 발급한 게임 세션 하나를 Named Pipe로 게임에 한 번 넘긴다. 실패하면 시작한 게임을 끝내고 세션 폐기를 시도한다 </summary>
    public sealed class GameSessionHandoff
    {
        // 토큰은 파이프로만 넘긴다. 실행 인자에는 파이프 이름과 런처 PID만 싣는다(같은 사용자는 다른 프로세스의 명령줄을 읽을 수 있다).
        // 파이프는 현재 사용자 전용, 인스턴스 1개로 열고 이름에 무작위 32자 hex를 붙여 다른 프로세스가 먼저 잡거나 끼어들기 어렵게 한다

        /****************************************
        *                Fields
        ****************************************/

        private const int    PIPE_NAME_RANDOM_CHARS = 32;
        private const int    LENGTH_PREFIX_BYTES    = 4;
        private const int    MAX_PIPE_INSTANCES     = 1;
        private const string ARGUMENT_SEPARATOR     = " ";

        private static readonly UTF8Encoding G_Utf8 = new UTF8Encoding(false, true);

        private readonly AuthApiClient               _api;
        private readonly GameSessionIssueResponse    _session;
        private readonly string                      _apiBaseUrl;
        private readonly Func<string, IStartedGame?> _startGame;
        private readonly GameHandoffOptions          _options;
        private readonly JsonSerializerSettings      _json = ContractJson.CreateSettings();

        private int _isUsed;

        /****************************************
        *                Methods
        ****************************************/

        // startGame은 받은 인자 문자열로 게임을 시작하고, 시작하지 못하면 null을 돌려준다
        public GameSessionHandoff(AuthApiClient api, GameSessionIssueResponse session, string apiBaseUrl,
            Func<string, IStartedGame?> startGame, GameHandoffOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(api);
            ArgumentNullException.ThrowIfNull(session);
            ArgumentException.ThrowIfNullOrEmpty(session.GameSessionToken, nameof(session));
            ArgumentException.ThrowIfNullOrEmpty(apiBaseUrl);
            ArgumentNullException.ThrowIfNull(startGame);

            _api        = api;
            _session    = session;
            _apiBaseUrl = apiBaseUrl;
            _startGame  = startGame;
            _options    = options ?? new GameHandoffOptions();
        }

        /// <summary> 파이프를 열고 게임을 시작해 세션을 넘긴다. 한 객체에서 한 번만 할 수 있다 </summary>
        public async Task<GameHandoffResult> RunAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _isUsed, 1) != 0)
            {
                return GameHandoffResult.Fail(EGameHandoffError.AlreadyUsed, false);
            }

            string pipeName = LauncherPipeProtocol.PIPE_NAME_PREFIX + RandomNumberGenerator.GetHexString(PIPE_NAME_RANDOM_CHARS, true);

            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, MAX_PIPE_INSTANCES, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (IOException)
            {
                return await FailAsync(EGameHandoffError.PipeCreateFailed, null).ConfigureAwait(false);
            }

            IStartedGame?     game;
            EGameHandoffError error;
            using (server)
            {
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.Timeout);

                game  = _startGame(BuildArguments(pipeName, _options.LauncherProcessId));
                error = (game == null)
                    ? EGameHandoffError.StartFailed
                    : await ExchangeAsync(server, game, timeout.Token, cancellationToken).ConfigureAwait(false);
            }

            if (error == EGameHandoffError.None)
            {
                return GameHandoffResult.Success(game!);
            }

            return await FailAsync(error, game).ConfigureAwait(false);
        }

        /// <summary> 게임 실행 인자. 토큰은 넣지 않는다 </summary>
        public static string BuildArguments(string pipeName, int launcherProcessId)
        {
            return LauncherPipeProtocol.ARG_PIPE + ARGUMENT_SEPARATOR + pipeName + ARGUMENT_SEPARATOR + LauncherPipeProtocol.ARG_PID
                + ARGUMENT_SEPARATOR + launcherProcessId.ToString(CultureInfo.InvariantCulture);
        }

        private async Task<EGameHandoffError> ExchangeAsync(NamedPipeServerStream server, IStartedGame game, CancellationToken token,
            CancellationToken callerToken)
        {
            try
            {
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                int clientProcessId;
                try
                {
                    clientProcessId = _options.ClientProcessIdReader(server.SafePipeHandle);
                }
                catch (Win32Exception)
                {
                    return EGameHandoffError.PidMismatch;
                }

                // 이름을 먼저 알아낸 다른 프로세스가 접속했으면 세션을 보내지 않는다
                if (clientProcessId != game.ProcessId)
                {
                    return EGameHandoffError.PidMismatch;
                }

                byte[] frame = BuildHandoffFrame();
                try
                {
                    await server.WriteAsync(frame, token).ConfigureAwait(false);
                    await server.FlushAsync(token).ConfigureAwait(false);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(frame);
                }

                byte[]? ack = await ReadFrameAsync(server, token).ConfigureAwait(false);
                return IsValidAck(ack, clientProcessId) ? EGameHandoffError.None : EGameHandoffError.BadAck;
            }
            catch (OperationCanceledException)
            {
                return callerToken.IsCancellationRequested ? EGameHandoffError.Cancelled : EGameHandoffError.Timeout;
            }
            catch (IOException)
            {
                return EGameHandoffError.PipeBroken;
            }
        }

        private byte[] BuildHandoffFrame()
        {
            LauncherHandoff handoff = new LauncherHandoff(LauncherPipeProtocol.PROTOCOL_VERSION, _session.AccountId,
                _session.GameSessionToken, _session.ExpiresAtUnixMs, _session.ServerTimeUnixMs, _apiBaseUrl);
            byte[] body = G_Utf8.GetBytes(JsonConvert.SerializeObject(handoff, _json));
            try
            {
                using MemoryStream frame = new MemoryStream(LENGTH_PREFIX_BYTES + body.Length);
                LauncherPipeProtocol.WriteFrame(frame, body);
                return frame.ToArray();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(body);
            }
        }

        // LauncherPipeProtocol.TryReadFrame과 같은 형식(4바이트 리틀엔디언 길이 + 본문)을 비동기로 읽는다.
        // 동기 읽기는 게임이 답하지 않을 때 제한 시간으로 끊을 수 없다
        private static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken token)
        {
            byte[] prefix = new byte[LENGTH_PREFIX_BYTES];
            int    read   = await stream.ReadAtLeastAsync(prefix, LENGTH_PREFIX_BYTES, false, token).ConfigureAwait(false);
            if (read < LENGTH_PREFIX_BYTES)
            {
                return null;
            }

            int length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
            if ((length <= 0) || (length > LauncherPipeProtocol.MAX_FRAME_BYTES))
            {
                return null;
            }

            byte[] body = new byte[length];
            read = await stream.ReadAtLeastAsync(body, length, false, token).ConfigureAwait(false);
            return (read < length) ? null : body;
        }

        private bool IsValidAck(byte[]? body, int clientProcessId)
        {
            if (body == null)
            {
                return false;
            }

            LauncherHandoffAck? ack;
            try
            {
                ack = JsonConvert.DeserializeObject<LauncherHandoffAck>(G_Utf8.GetString(body), _json);
            }
            catch (Exception exception) when ((exception is JsonException) || (exception is DecoderFallbackException))
            {
                return false;
            }

            return (ack != null) && (ack.ProtocolVersion == LauncherPipeProtocol.PROTOCOL_VERSION) && (ack.GamePid == clientProcessId);
        }

        private async Task<GameHandoffResult> FailAsync(EGameHandoffError error, IStartedGame? game)
        {
            game?.Kill();

            // 정리 단계라 호출자 취소와 무관하게 보낸다. 실패해도 세션은 짧은 수명으로 곧 만료된다
            AuthApiResult revoke = await _api.RevokeGameSessionAsync(_session.GameSessionToken, CancellationToken.None)
                .ConfigureAwait(false);
            return GameHandoffResult.Fail(error, revoke.IsSuccess);
        }
    }
}
