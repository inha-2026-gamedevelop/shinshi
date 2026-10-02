// System
using System;
using System.IO;

namespace Shinshi.Contracts
{
    /// <summary> 런처가 게임에 세션을 한 번 넘기는 Named Pipe 규약, 런처와 게임 클라이언트가 같은 소스를 쓴다 </summary>
    public static class LauncherPipeProtocol
    {
        // 순서: 런처가 파이프를 연 뒤 게임을 실행한다 -> 게임이 접속해 서버 PID가 -launcher-pid와 같은지 본다 ->
        // 런처는 클라이언트 PID가 자신이 띄운 게임인지 본다 -> 런처가 LauncherHandoff 한 프레임을 보낸다 ->
        // 게임이 LauncherHandoffAck 한 프레임으로 답한다 -> 양쪽이 파이프를 닫는다.
        // 토큰은 이 파이프로만 오가며 실행 인자, 환경 변수, 로그에 넣지 않는다

        /****************************************
        *                Fields
        ****************************************/

        public const int PROTOCOL_VERSION = 1;

        public const string ARG_PIPE = "-launcher-pipe";
        public const string ARG_PID  = "-launcher-pid";

        // 무작위 32자 hex를 붙인다. 이름만으로 다른 사용자가 끼어들 수 없도록 현재 사용자 전용으로 연다
        public const string PIPE_NAME_PREFIX = "shinshi-launcher-";

        // 게임 시작부터 수신 확인까지 전체 제한
        public const int HANDOFF_TIMEOUT_MS = 120000;

        // 한 프레임 본문 상한, 토큰 하나와 짧은 필드만 담는다
        public const int MAX_FRAME_BYTES = 16384;

        private const int LENGTH_PREFIX_BYTES = 4;
        private const int BITS_PER_BYTE       = 8;
        private const int BYTE_MASK           = 0xFF;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 4바이트 리틀엔디언 길이 다음에 본문을 쓴다 </summary>
        public static void WriteFrame(Stream stream, byte[] body)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if ((body == null) || (body.Length == 0) || (body.Length > MAX_FRAME_BYTES))
            {
                throw new ArgumentException("Frame body is empty or too large.", nameof(body));
            }

            byte[] prefix = new byte[LENGTH_PREFIX_BYTES];
            for (int i = 0; i < LENGTH_PREFIX_BYTES; ++i)
            {
                prefix[i] = (byte)((body.Length >> (i * BITS_PER_BYTE)) & BYTE_MASK);
            }

            stream.Write(prefix, 0, LENGTH_PREFIX_BYTES);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        /// <summary> 한 프레임을 읽는다, 길이가 상한을 넘거나 스트림이 중간에 끝나면 false </summary>
        public static bool TryReadFrame(Stream stream, out byte[] body)
        {
            body = null;
            if (stream == null)
            {
                return false;
            }

            byte[] prefix = new byte[LENGTH_PREFIX_BYTES];
            if (!TryReadExactly(stream, prefix))
            {
                return false;
            }

            int length = 0;
            for (int i = 0; i < LENGTH_PREFIX_BYTES; ++i)
            {
                length |= prefix[i] << (i * BITS_PER_BYTE);
            }

            if ((length <= 0) || (length > MAX_FRAME_BYTES))
            {
                return false;
            }

            byte[] buffer = new byte[length];
            if (!TryReadExactly(stream, buffer))
            {
                return false;
            }

            body = buffer;
            return true;
        }

        private static bool TryReadExactly(Stream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read <= 0)
                {
                    return false;
                }

                offset += read;
            }

            return true;
        }
    }
}
