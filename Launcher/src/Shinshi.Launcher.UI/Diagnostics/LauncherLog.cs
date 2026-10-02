// System
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Shinshi.Launcher.Diagnostics
{
    /// <summary> 날짜별 런처 로그 파일. 토큰, 초대 코드 같은 비밀값은 넘기지 않는다 </summary>
    public static class LauncherLog
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PRODUCT_FOLDER   = "Shinshi";
        private const string LOG_FOLDER       = "logs";
        private const string FILE_PREFIX      = "launcher-";
        private const string FILE_EXTENSION   = ".log";
        private const string DATE_FORMAT      = "yyyyMMdd";
        private const string TIMESTAMP_FORMAT = "yyyy-MM-dd HH:mm:ss.fff";
        private const string LEVEL_INFO       = "INFO";
        private const string LEVEL_WARN       = "WARN";
        private const string LEVEL_ERROR      = "ERROR";

        private static readonly object       G_Lock  = new object();
        private static readonly UTF8Encoding G_Utf8  = new UTF8Encoding(false);

        /****************************************
        *              Properties
        ****************************************/

        public static string LogFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), PRODUCT_FOLDER, LOG_FOLDER);

        /****************************************
        *                Methods
        ****************************************/

        public static void Info(string message) { Write(LEVEL_INFO, message); }

        public static void Warn(string message) { Write(LEVEL_WARN, message); }

        public static void Error(string message, Exception? exception = null)
        {
            if (exception == null)
            {
                Write(LEVEL_ERROR, message);
                return;
            }

            Write(LEVEL_ERROR, message + Environment.NewLine + exception);
        }

        private static void Write(string level, string message)
        {
            DateTime now  = DateTime.Now;
            string   line = now.ToString(TIMESTAMP_FORMAT, CultureInfo.InvariantCulture) + " [" + level + "] " + message
                + Environment.NewLine;
            string   path = Path.Combine(LogFolder, FILE_PREFIX + now.ToString(DATE_FORMAT, CultureInfo.InvariantCulture) + FILE_EXTENSION);

            lock (G_Lock)
            {
                try
                {
                    Directory.CreateDirectory(LogFolder);
                    File.AppendAllText(path, line, G_Utf8);
                }
                catch (IOException exception)
                {
                    // 로그를 못 남겨도 런처는 계속 돈다. 디버거가 붙어 있을 때만 볼 수 있게 남긴다
                    Debug.WriteLine("LauncherLog write failed: " + exception.Message);
                }
                catch (UnauthorizedAccessException exception)
                {
                    Debug.WriteLine("LauncherLog write failed: " + exception.Message);
                }
            }
        }
    }
}
