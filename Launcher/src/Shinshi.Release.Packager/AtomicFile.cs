// System
using System;
using System.IO;
using System.Security.Cryptography;

namespace Shinshi.Release
{
    // 임시 파일에 다 쓴 뒤 같은 폴더 안에서 옮긴다. 도중에 죽어도 목적지에는 완성된 파일만 있다
    internal static class AtomicFile
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    COPY_BUFFER_BYTES = 81920;
        private const string TEMP_SUFFIX       = ".tmp";

        /****************************************
        *                Methods
        ****************************************/

        public static void WriteBytes(string destination, byte[] bytes, bool overwrite)
        {
            string temp = CreateTempPath(destination);
            try
            {
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                File.Move(temp, destination, overwrite);
            }
            finally
            {
                DeleteQuietly(temp);
            }
        }

        // 원본을 복사하면서 해시와 길이를 함께 구한다. 해시한 뒤 파일이 바뀐 경우를 잡아내려고
        // 매니페스트에 적은 값과 복사본을 대조한다. 어긋나면 false를 돌려주고 복사본은 남기지 않는다
        public static bool TryCopyVerified(string source, string destination, string expectedSha256, long expectedSize)
        {
            string temp = CreateTempPath(destination);
            try
            {
                using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long copied = 0;
                using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, COPY_BUFFER_BYTES,
                    FileOptions.SequentialScan))
                using (FileStream output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[COPY_BUFFER_BYTES];
                    int    read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        hasher.AppendData(buffer, 0, read);
                        output.Write(buffer, 0, read);
                        copied += read;
                    }

                    output.Flush(true);
                }

                string actual = Convert.ToHexStringLower(hasher.GetHashAndReset());
                if ((copied != expectedSize) || (actual != expectedSha256))
                {
                    return false;
                }

                File.Move(temp, destination);
                return true;
            }
            finally
            {
                DeleteQuietly(temp);
            }
        }

        private static string CreateTempPath(string destination)
        {
            return destination + "." + Guid.NewGuid().ToString("N") + TEMP_SUFFIX;
        }

        private static void DeleteQuietly(string path)
        {
            // 정리 실패가 원래 오류를 가리면 안 된다. 남은 임시 파일은 게시 결과에 영향이 없다
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}
