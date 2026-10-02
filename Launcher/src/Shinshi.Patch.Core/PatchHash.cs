// System
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Patch
{
    /// <summary> SHA-256 소문자 hex, 파일과 스트림은 나눠 읽어 전체를 메모리에 올리지 않는다 </summary>
    public static class PatchHash
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int SHA256_HEX_LENGTH = 64;

        private const int FILE_BUFFER_BYTES = 81920;

        /****************************************
        *                Methods
        ****************************************/

        public static string Compute(ReadOnlySpan<byte> data) { return Convert.ToHexStringLower(SHA256.HashData(data)); }

        public static string Compute(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }

        public static async Task<string> ComputeAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(stream);
            byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexStringLower(digest);
        }

        public static string ComputeFile(string path)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            using FileStream stream = OpenSequential(path, false);
            return Compute(stream);
        }

        public static async Task<string> ComputeFileAsync(string path, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            await using FileStream stream = OpenSequential(path, true);
            return await ComputeAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        /// <summary> 소문자 hex 64자인지 확인한다. 대문자는 같은 해시의 다른 표기라 거절한다 </summary>
        public static bool IsSha256Hex(string? value)
        {
            if ((value == null) || (value.Length != SHA256_HEX_LENGTH))
            {
                return false;
            }

            foreach (char character in value)
            {
                if (!(((character >= '0') && (character <= '9')) || ((character >= 'a') && (character <= 'f'))))
                {
                    return false;
                }
            }

            return true;
        }

        private static FileStream OpenSequential(string path, bool isAsync)
        {
            FileOptions options = FileOptions.SequentialScan;
            if (isAsync)
            {
                options |= FileOptions.Asynchronous;
            }

            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, FILE_BUFFER_BYTES, options);
        }
    }
}
