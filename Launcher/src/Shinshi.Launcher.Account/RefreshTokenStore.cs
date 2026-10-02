// System
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Shinshi.Launcher.Account
{
    /// <summary> %LOCALAPPDATA%\Shinshi\Account\refresh.bin에 갱신 토큰을 DPAPI(CurrentUser)로 암호화해 둔다 </summary>
    public sealed class RefreshTokenStore : IRefreshTokenStore
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string FILE_NAME = "refresh.bin";

        private const string PRODUCT_FOLDER = "Shinshi";
        private const string ACCOUNT_FOLDER = "Account";
        private const string TEMP_SUFFIX    = ".tmp";

        // 토큰 하나를 암호화한 크기는 수백 바이트다. 이보다 크면 다른 파일로 본다
        private const int MAX_FILE_BYTES = 16 * 1024;

        // 다른 프로그램이 같은 사용자 DPAPI로 만든 값을 이 파일에 넣어도 풀리지 않게 하는 고정 엔트로피.
        // 비밀이 아니다. 같은 사용자 권한의 악성 프로그램까지 막는 경계는 아니다
        private static readonly byte[] G_Entropy = Encoding.ASCII.GetBytes("shinshi.launcher.refresh.v1");

        private static readonly UTF8Encoding G_Utf8 = new UTF8Encoding(false, true);

        private readonly string _filePath;
        private readonly object _lock = new object();

        /****************************************
        *              Properties
        ****************************************/

        public static string DefaultFilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), PRODUCT_FOLDER, ACCOUNT_FOLDER, FILE_NAME);

        public string FilePath => _filePath;

        public bool HasToken => File.Exists(_filePath);

        /****************************************
        *                Methods
        ****************************************/

        public RefreshTokenStore(string? filePath = null)
        {
            _filePath = Path.GetFullPath(filePath ?? DefaultFilePath);
        }

        public bool TryLoad([NotNullWhen(true)] out string? refreshToken)
        {
            refreshToken = null;
            byte[] cipher;
            lock (_lock)
            {
                try
                {
                    FileInfo info = new FileInfo(_filePath);
                    if (!info.Exists)
                    {
                        return false;
                    }

                    if ((info.Length == 0) || (info.Length > MAX_FILE_BYTES))
                    {
                        DeleteQuietly(_filePath);
                        return false;
                    }

                    cipher = File.ReadAllBytes(_filePath);
                }
                catch (Exception exception) when ((exception is IOException) || (exception is UnauthorizedAccessException))
                {
                    return false;
                }

                // 다른 사용자, 다른 PC에서 복사해 온 파일은 풀리지 않는다. 다시 로그인하게 지운다
                if (!Dpapi.TryUnprotect(cipher, G_Entropy, out byte[] plain))
                {
                    DeleteQuietly(_filePath);
                    return false;
                }

                try
                {
                    refreshToken = G_Utf8.GetString(plain);
                }
                catch (DecoderFallbackException)
                {
                    DeleteQuietly(_filePath);
                    return false;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                }
            }

            if (refreshToken.Length == 0)
            {
                refreshToken = null;
                return false;
            }

            return true;
        }

        public bool TrySave(string refreshToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(refreshToken);

            byte[] plain = G_Utf8.GetBytes(refreshToken);
            byte[] cipher;
            try
            {
                cipher = Dpapi.Protect(plain, G_Entropy);
            }
            catch (CryptographicException)
            {
                return false;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }

            // 쓰다가 끊겨도 이전 파일이 반쯤 덮이지 않게 같은 폴더의 임시 파일에 쓴 뒤 옮긴다
            string tempPath = _filePath + TEMP_SUFFIX;
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                    File.WriteAllBytes(tempPath, cipher);
                    File.Move(tempPath, _filePath, true);
                    return true;
                }
                catch (Exception exception) when ((exception is IOException) || (exception is UnauthorizedAccessException))
                {
                    DeleteQuietly(tempPath);
                    return false;
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                DeleteQuietly(_filePath + TEMP_SUFFIX);
                DeleteQuietly(_filePath);
            }
        }

        // 지우지 못해도 다음 로그인에서 덮어쓴다. 로그아웃 자체를 실패로 만들지 않는다
        private static void DeleteQuietly(string path)
        {
            try
            {
                File.Delete(path);
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
