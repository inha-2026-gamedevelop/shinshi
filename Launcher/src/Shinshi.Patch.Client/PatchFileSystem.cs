// System
using System;
using System.IO;
using System.Security.Cryptography;

namespace Shinshi.Patch.Client
{
    /// <summary> 설치 폴더용 파일 작업, 원자적 쓰기와 읽기 전용 속성을 풀어 지우기 </summary>
    internal static class PatchFileSystem
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    RANDOM_NAME_BYTES = 8;
        private const string TEMP_SUFFIX       = ".tmp-";

        /****************************************
        *                Methods
        ****************************************/

        public static string NewRandomName(string prefix)
        {
            return prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(RANDOM_NAME_BYTES));
        }

        // 같은 폴더의 임시 파일에 다 쓰고 디스크에 내린 뒤 이름을 바꾼다.
        // 쓰는 도중 꺼져도 원래 파일은 옛 내용 그대로 남는다
        public static void WriteAllBytesAtomic(string path, byte[] bytes)
        {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory))
            {
                throw new ArgumentException("Atomic write target has no parent directory.", nameof(path));
            }

            Directory.CreateDirectory(directory);
            string tempPath = path + NewRandomName(TEMP_SUFFIX);
            try
            {
                using (FileStream stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                File.Move(tempPath, path, true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        /// <summary> 파일 크기가 상한을 넘으면 null </summary>
        public static byte[]? ReadAllBytesLimited(string path, int maxBytes)
        {
            using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > maxBytes)
            {
                return null;
            }

            byte[] bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return bytes;
        }

        // 빌드 파일에 읽기 전용 속성이 붙어 있으면 File.Delete와 덮어쓰기 이동이 거절된다
        public static void ClearReadOnly(string path)
        {
            FileInfo file = new FileInfo(path);
            if (file.Exists && ((file.Attributes & FileAttributes.ReadOnly) != 0))
            {
                file.Attributes &= ~FileAttributes.ReadOnly;
            }
        }

        public static void DeleteFile(string path)
        {
            ClearReadOnly(path);
            File.Delete(path);
        }

        // 연결 지점(junction, 심볼릭 링크)은 따라 들어가지 않고 링크만 지운다. 설치 폴더 밖 파일을 지우지 않기 위해서다
        public static void DeleteDirectory(string path)
        {
            DirectoryInfo directory = new DirectoryInfo(path);
            if (!directory.Exists)
            {
                return;
            }

            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                directory.Delete(false);
                return;
            }

            foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
            {
                if (entry is DirectoryInfo child)
                {
                    DeleteDirectory(child.FullName);
                }
                else
                {
                    DeleteFile(entry.FullName);
                }
            }

            directory.Delete(false);
        }
    }
}
