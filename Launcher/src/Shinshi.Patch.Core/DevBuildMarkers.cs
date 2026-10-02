// System
using System;
using System.Collections.Generic;
using System.IO;

namespace Shinshi.Patch
{
    /// <summary> 빌드의 boot.config에서 개발 빌드 표식을 찾는다, stable 게시를 막는 데 쓴다 </summary>
    public static class DevBuildMarkers
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string BOOT_CONFIG_FILE_NAME = "boot.config";
        public const int    MAX_BOOT_CONFIG_BYTES = 1024 * 1024;

        // 개발 빌드(D:/shinshi/Builds/windows-client)에서 player-connection-* 줄을 실측했다.
        // Release 빌드에 이 줄들이 정말 없는지는 아직 확인하지 않았다. Release 빌드로 확인한 뒤
        // 오탐이 있으면 키 목록을 고친다
        private const string PLAYER_CONNECTION_MODE    = "player-connection-mode";
        private const string PLAYER_CONNECTION_DEBUG   = "player-connection-debug";
        private const string WAIT_FOR_NATIVE_DEBUGGER  = "wait-for-native-debugger";
        private const string WAIT_FOR_MANAGED_DEBUGGER = "wait-for-managed-debugger";
        private const string ENABLED_VALUE             = "1";
        private const char   KEY_VALUE_SEPARATOR       = '=';
        private const char   LINE_SEPARATOR            = '\n';

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 빌드 폴더의 *_Data/boot.config 하나를 읽어 표식 줄을 돌려준다. 빈 목록이면 표식 없음 </summary>
        public static bool TryFind(string buildFolder, out IReadOnlyList<string> markers, out EPatchError error)
        {
            ArgumentException.ThrowIfNullOrEmpty(buildFolder);

            markers = Array.Empty<string>();
            List<string> found = BuildFolder.FindInDataFolders(buildFolder, BOOT_CONFIG_FILE_NAME);
            if (found.Count == 0)
            {
                error = EPatchError.BootConfigMissing;
                return false;
            }

            if (found.Count > 1)
            {
                error = EPatchError.BootConfigAmbiguous;
                return false;
            }

            if (new FileInfo(found[0]).Length > MAX_BOOT_CONFIG_BYTES)
            {
                error = EPatchError.TooLarge;
                return false;
            }

            markers = FindInText(File.ReadAllText(found[0]));
            error   = EPatchError.None;
            return true;
        }

        /// <summary> boot.config 내용에서 표식 줄을 찾는다 </summary>
        public static IReadOnlyList<string> FindInText(string bootConfigText)
        {
            ArgumentNullException.ThrowIfNull(bootConfigText);

            List<string> markers = new List<string>();
            foreach (string rawLine in bootConfigText.Split(LINE_SEPARATOR))
            {
                string line      = rawLine.Trim();
                int    separator = line.IndexOf(KEY_VALUE_SEPARATOR);
                string key       = ((separator < 0) ? line : line.Substring(0, separator)).Trim();
                string value     = (separator < 0) ? string.Empty : line.Substring(separator + 1).Trim();
                if (IsMarker(key, value))
                {
                    markers.Add(line);
                }
            }

            return markers;
        }

        private static bool IsMarker(string key, string value)
        {
            // 연결 모드와 managed 디버거 대기는 값과 무관하게 줄이 있는 것만으로 개발 빌드로 본다
            switch (key)
            {
                case PLAYER_CONNECTION_MODE:
                case WAIT_FOR_MANAGED_DEBUGGER:
                    return true;
                case PLAYER_CONNECTION_DEBUG:
                case WAIT_FOR_NATIVE_DEBUGGER:
                    return value == ENABLED_VALUE;
                default:
                    return false;
            }
        }
    }
}
