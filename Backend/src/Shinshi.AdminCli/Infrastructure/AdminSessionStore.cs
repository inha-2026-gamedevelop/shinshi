// System
using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

// Third Party
using Newtonsoft.Json;

// Project
using Shinshi.Contracts;

namespace Shinshi.AdminCli
{
    /// <summary> 어드민 토큰 파일. Windows는 현재 사용자만 접근하는 ACL, 유닉스는 0600(폴더 0700)으로 만들고
    /// 임시 파일에 쓴 뒤 바꿔 끼워 반쯤 쓴 파일이나 권한이 넓은 순간을 남기지 않는다 </summary>
    internal sealed class AdminSessionStore
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string DIRECTORY_NAME = "shinshi-admin";
        public const string FILE_NAME      = "session.json";

        private const string TEMP_SUFFIX         = ".tmp-";
        private const string XDG_STATE_VARIABLE  = "XDG_STATE_HOME";
        private const string HOME_VARIABLE       = "HOME";
        private const string LOCAL_STATE_SEGMENT = ".local";
        private const string STATE_SEGMENT       = "state";

        private const UnixFileMode USER_FILE_MODE      = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        private const UnixFileMode USER_DIRECTORY_MODE = UnixFileMode.UserRead | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute;

        private static readonly Encoding G_Utf8 = new UTF8Encoding(false);

        private readonly string _directory;

        /****************************************
        *              Properties
        ****************************************/

        public string DirectoryPath => _directory;
        public string FilePath => Path.Combine(_directory, FILE_NAME);

        /****************************************
        *                Methods
        ****************************************/

        public AdminSessionStore(string directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            _directory = directory;
        }

        /// <summary> Windows는 %LOCALAPPDATA%\shinshi-admin, 그 밖은 $XDG_STATE_HOME 또는 ~/.local/state 아래 shinshi-admin </summary>
        public static AdminSessionStore CreateDefault(Func<string, string?> readEnvironment)
        {
            ArgumentNullException.ThrowIfNull(readEnvironment);
            if (OperatingSystem.IsWindows())
            {
                return new AdminSessionStore(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DIRECTORY_NAME));
            }

            string? stateHome = readEnvironment(XDG_STATE_VARIABLE);
            if (string.IsNullOrWhiteSpace(stateHome))
            {
                string? home = readEnvironment(HOME_VARIABLE);
                if (string.IsNullOrWhiteSpace(home))
                {
                    home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }

                stateHome = Path.Combine(home, LOCAL_STATE_SEGMENT, STATE_SEGMENT);
            }

            return new AdminSessionStore(Path.Combine(stateHome, DIRECTORY_NAME));
        }

        /// <summary> 저장된 세션. 파일이 없거나 손상됐으면 null이다 </summary>
        public AdminSession? Load()
        {
            string path = FilePath;
            if (!File.Exists(path))
            {
                return null;
            }

            AdminSession? session;
            try
            {
                session = JsonConvert.DeserializeObject<AdminSession>(File.ReadAllText(path, G_Utf8),
                    ContractJson.CreateSettings());
            }
            catch (JsonException)
            {
                return null;
            }

            return ((session != null) && session.IsWellFormed()) ? session : null;
        }

        public void Save(AdminSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            string content = JsonConvert.SerializeObject(session, ContractJson.CreateSettings());
            string temp = FilePath + TEMP_SUFFIX + Guid.NewGuid().ToString("N");
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    WriteWindows(temp, content);
                }
                else
                {
                    WriteUnix(temp, content);
                }

                File.Move(temp, FilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }

        /// <summary> 파일이 있었으면 지우고 true </summary>
        public bool Delete()
        {
            string path = FilePath;
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }

        [SupportedOSPlatform("windows")]
        private void WriteWindows(string temp, string content)
        {
            SecurityIdentifier user = WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException("Current Windows user has no SID.");

            // 상속을 끊고 현재 사용자 한 명만 남긴다. 폴더를 먼저 잠가 그 안에 새로 생기는 파일도 같은 권한만 상속한다
            DirectorySecurity directorySecurity = new DirectorySecurity();
            directorySecurity.SetAccessRuleProtection(true, false);
            directorySecurity.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
                AccessControlType.Allow));
            DirectoryInfo directory = new DirectoryInfo(_directory);
            if (directory.Exists)
            {
                directory.SetAccessControl(directorySecurity);
            }
            else
            {
                directory.Create(directorySecurity);
            }

            File.WriteAllText(temp, content, G_Utf8);
            FileSecurity fileSecurity = new FileSecurity();
            fileSecurity.SetAccessRuleProtection(true, false);
            fileSecurity.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                AccessControlType.Allow));
            new FileInfo(temp).SetAccessControl(fileSecurity);
        }

        [UnsupportedOSPlatform("windows")]
        private void WriteUnix(string temp, string content)
        {
            Directory.CreateDirectory(_directory, USER_DIRECTORY_MODE);
            File.SetUnixFileMode(_directory, USER_DIRECTORY_MODE);
            FileStreamOptions options = new FileStreamOptions
            {
                Mode           = FileMode.CreateNew,
                Access         = FileAccess.Write,
                Share          = FileShare.None,
                UnixCreateMode = USER_FILE_MODE
            };
            using (FileStream stream = new FileStream(temp, options))
            {
                byte[] bytes = G_Utf8.GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }

            // umask가 생성 권한을 더 좁힐 수는 있어도 넓히지는 않지만, 의도를 분명히 하려고 다시 맞춘다
            File.SetUnixFileMode(temp, USER_FILE_MODE);
        }
    }
}
