// System
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Shinshi.Network
{
    /// <summary> 형식과 바이트 순서를 고정한 콘텐츠 해시 입력을 만들고 SHA256을 계산한다 </summary>
    public sealed class ContentHashWriter : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    HEX_DIGITS_PER_BYTE = 2;
        private const int    HEX_NIBBLE_BITS     = 4;
        private const int    HEX_NIBBLE_MASK     = 15;
        private const string HEX_DIGITS          = "0123456789abcdef";

        private readonly MemoryStream _stream   = new MemoryStream();
        private readonly UTF8Encoding _encoding = new UTF8Encoding(false, true);
        private readonly BinaryWriter _writer;
        private bool _finished;
        private bool _disposed;

        /****************************************
        *                Methods
        ****************************************/

        public ContentHashWriter()
        {
            _writer = new BinaryWriter(_stream, _encoding, true);
        }

        // BinaryWriter의 정수와 IEEE754 쓰기는 little endian이다. 문자열은 길이를 별도로 넣어
        // 필드 경계가 같아 보이는 연결 문자열과 null/빈 문자열의 충돌을 막는다.
        public void WriteBoolean(bool value)
        {
            RequireWritable();
            _writer.Write(value);
        }

        public void WriteInt32(int value)
        {
            RequireWritable();
            _writer.Write(value);
        }

        public void WriteInt64(long value)
        {
            RequireWritable();
            _writer.Write(value);
        }

        public void WriteUInt64(ulong value)
        {
            RequireWritable();
            _writer.Write(value);
        }

        public void WriteSingle(float value)
        {
            RequireWritable();
            _writer.Write(value);
        }

        public void WriteDouble(double value)
        {
            RequireWritable();
            _writer.Write(value);
        }

        public void WriteString(string value)
        {
            RequireWritable();
            _writer.Write(value != null);

            if (value == null)
            {
                return;
            }

            byte[] bytes = _encoding.GetBytes(value);
            _writer.Write(bytes.Length);
            _writer.Write(bytes);
        }

        /// <summary> 입력을 닫고 문화권과 무관한 소문자 64자리 SHA256을 반환한다 </summary>
        public string Complete()
        {
            RequireWritable();
            _writer.Flush();
            _finished = true;

            byte[] digest;
            using (SHA256 algorithm = SHA256.Create())
            {
                digest = algorithm.ComputeHash(_stream.GetBuffer(), 0, checked((int)_stream.Length));
            }

            var characters = new char[digest.Length * HEX_DIGITS_PER_BYTE];
            for (int i = 0; i < digest.Length; ++i)
            {
                int characterIndex = i * HEX_DIGITS_PER_BYTE;
                characters[characterIndex]     = HEX_DIGITS[digest[i] >> HEX_NIBBLE_BITS];
                characters[characterIndex + 1] = HEX_DIGITS[digest[i] & HEX_NIBBLE_MASK];
            }

            return new string(characters);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
            _stream.Dispose();
        }

        private void RequireWritable()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ContentHashWriter));
            }

            if (_finished)
            {
                throw new InvalidOperationException("콘텐츠 해시 입력이 이미 완료됐다");
            }
        }
    }
}
