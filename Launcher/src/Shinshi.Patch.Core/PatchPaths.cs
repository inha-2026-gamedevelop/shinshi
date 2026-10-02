// System
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Shinshi.Patch
{
    /// <summary> 매니페스트 경로 검사, 설치 폴더 밖이나 Windows가 다르게 해석하는 이름을 막는다 </summary>
    public static class PatchPaths
    {
        /****************************************
        *                Fields
        ****************************************/

        public const char SEPARATOR          = '/';
        public const int  MAX_PATH_LENGTH    = 240;
        public const int  MAX_SEGMENT_LENGTH = 255;

        private const char FIRST_PRINTABLE = ' ';
        private const char DELETE_CHAR     = '\u007F';

        // ':'는 드라이브 문자와 NTFS 대체 데이터 스트림("a.txt:x")을 함께 막는다
        private const string FORBIDDEN_CHARS = "<>\"|?*:\\";

        // Windows는 확장자가 붙어도 이 이름을 장치로 연다. 위첨자 숫자 COM/LPT도 장치로 취급된다
        private static readonly HashSet<string> G_ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
            "COM\u00B9", "COM\u00B2", "COM\u00B3", "LPT\u00B9", "LPT\u00B2", "LPT\u00B3"
        };

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 경로 하나가 규칙을 모두 지키는지 확인한다 </summary>
        public static bool IsValidRelativePath([NotNullWhen(true)] string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if ((path.Length > MAX_PATH_LENGTH) || char.IsWhiteSpace(path[0]) || char.IsWhiteSpace(path[path.Length - 1]))
            {
                return false;
            }

            if (path[0] == SEPARATOR)
            {
                return false;
            }

            foreach (char character in path)
            {
                if ((character < FIRST_PRINTABLE) || (character == DELETE_CHAR) || (FORBIDDEN_CHARS.IndexOf(character) >= 0))
                {
                    return false;
                }
            }

            foreach (string segment in path.Split(SEPARATOR))
            {
                if (!IsValidSegment(segment))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary> 목록 전체를 검사한다. 하나라도 틀리거나 대소문자만 다른 중복이 있으면 false </summary>
        public static bool TryValidateAll(IEnumerable<string?> paths, out EPatchError error)
        {
            ArgumentNullException.ThrowIfNull(paths);

            // Windows 파일시스템은 대소문자를 구분하지 않아 "A.dll"과 "a.dll"이 같은 파일을 덮어쓴다
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? path in paths)
            {
                if (!IsValidRelativePath(path))
                {
                    error = EPatchError.InvalidPath;
                    return false;
                }

                if (!seen.Add(path))
                {
                    error = EPatchError.DuplicatePath;
                    return false;
                }
            }

            error = EPatchError.None;
            return true;
        }

        /// <summary> 검사를 통과한 상대 경로를 root 아래 전체 경로로 바꾼다. 결과가 root 밖이면 false </summary>
        public static bool TryResolveUnderRoot(string root, string? relative, [NotNullWhen(true)] out string? fullPath)
        {
            ArgumentException.ThrowIfNullOrEmpty(root);

            fullPath = null;
            if (!IsValidRelativePath(relative))
            {
                return false;
            }

            // 문자열 검사를 통과해도 결합 결과를 다시 확인한다. 검사 규칙에 빠진 해석 차이가 있어도 root를 벗어나지 못한다
            string rootFull = Path.GetFullPath(root);
            if (!Path.EndsInDirectorySeparator(rootFull))
            {
                rootFull += Path.DirectorySeparatorChar;
            }

            string native   = relative.Replace(SEPARATOR, Path.DirectorySeparatorChar);
            string combined = Path.GetFullPath(Path.Combine(rootFull, native));

            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if ((combined.Length <= rootFull.Length) || !combined.StartsWith(rootFull, comparison))
            {
                return false;
            }

            fullPath = combined;
            return true;
        }

        /// <summary> 상대 경로를 OS 구분자로 바꾼다. 검사를 통과한 경로에만 쓴다 </summary>
        public static string ToNative(string relative)
        {
            ArgumentNullException.ThrowIfNull(relative);
            return relative.Replace(SEPARATOR, Path.DirectorySeparatorChar);
        }

        private static bool IsValidSegment(string segment)
        {
            if ((segment.Length == 0) || (segment.Length > MAX_SEGMENT_LENGTH))
            {
                return false;
            }

            if ((segment == ".") || (segment == ".."))
            {
                return false;
            }

            // Windows는 끝의 점과 공백을 지워 다른 이름과 같은 파일로 만든다
            char last = segment[segment.Length - 1];
            if ((last == '.') || char.IsWhiteSpace(last))
            {
                return false;
            }

            return !IsReservedName(segment);
        }

        private static bool IsReservedName(string segment)
        {
            int dot = segment.IndexOf('.');
            string stem = (dot < 0) ? segment : segment.Substring(0, dot);
            return G_ReservedNames.Contains(stem.TrimEnd());
        }
    }
}
