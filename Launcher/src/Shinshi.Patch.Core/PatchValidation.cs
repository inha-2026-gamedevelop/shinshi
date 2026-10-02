// System
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

// Project
using Shinshi.Contracts;

namespace Shinshi.Patch
{
    /// <summary> 서명을 통과한 문서의 필드 규칙과 포인터, 매니페스트 짝 검사 </summary>
    public static class PatchValidation
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int  MAX_RELEASE_ID_LENGTH      = 64;
        public const int  MAX_BUILD_ID_LENGTH        = 128;
        public const int  MAX_DISPLAY_VERSION_LENGTH = 64;
        public const long MIN_SEQUENCE               = 1;

        private const int  VERSION_PARTS           = 3;
        private const int  MAX_VERSION_PART_DIGITS = 9;
        private const char VERSION_SEPARATOR       = '.';
        private const char FIRST_VISIBLE_ASCII     = '!';
        private const char LAST_VISIBLE_ASCII      = '~';
        private const char FIRST_PRINTABLE         = ' ';
        private const char DELETE_CHAR             = '\u007F';

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> [A-Za-z0-9._-]{1,64}, 설치 폴더 이름으로 쓰이므로 "."과 ".."과 점으로 끝나는 이름은 거절한다 </summary>
        public static bool IsValidReleaseId([NotNullWhen(true)] string? releaseId)
        {
            if (string.IsNullOrEmpty(releaseId) || (releaseId.Length > MAX_RELEASE_ID_LENGTH))
            {
                return false;
            }

            // Windows는 폴더 이름 끝의 점을 지워 "a."을 "a"와 같은 폴더로, "..."을 상위 폴더처럼 다룰 수 있다
            if ((releaseId == ".") || (releaseId == "..") || (releaseId[releaseId.Length - 1] == '.'))
            {
                return false;
            }

            foreach (char character in releaseId)
            {
                bool isLetter = ((character >= 'A') && (character <= 'Z')) || ((character >= 'a') && (character <= 'z'));
                bool isDigit  = (character >= '0') && (character <= '9');
                if (!(isLetter || isDigit || (character == '.') || (character == '_') || (character == '-')))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary> "major.minor.patch" 숫자 세 부분을 읽는다 </summary>
        public static bool TryParseLauncherVersion(string? text, [NotNullWhen(true)] out Version? version)
        {
            version = null;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            string[] parts = text.Split(VERSION_SEPARATOR);
            if (parts.Length != VERSION_PARTS)
            {
                return false;
            }

            int[] numbers = new int[VERSION_PARTS];
            for (int i = 0; i < VERSION_PARTS; ++i)
            {
                if (!IsVersionPart(parts[i]))
                {
                    return false;
                }

                numbers[i] = int.Parse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture);
            }

            version = new Version(numbers[0], numbers[1], numbers[2]);
            return true;
        }

        /// <summary> 현재 런처 버전이 매니페스트의 최소 버전 이상인지 확인한다. 최소 버전 형식이 틀리면 false </summary>
        public static bool IsLauncherVersionSufficient(string minLauncherVersion, Version currentLauncherVersion)
        {
            ArgumentNullException.ThrowIfNull(currentLauncherVersion);
            if (!TryParseLauncherVersion(minLauncherVersion, out Version? required))
            {
                return false;
            }

            // Version(1, 2)는 Build가 -1이라 그대로 비교하면 1.2.0보다 작게 나온다
            Version current = new Version(currentLauncherVersion.Major, currentLauncherVersion.Minor,
                Math.Max(currentLauncherVersion.Build, 0));
            return current >= required;
        }

        public static EPatchError ValidateManifest(ReleaseManifest manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest);

            if (manifest.FormatVersion != PatchFormat.FORMAT_VERSION)
            {
                return EPatchError.UnsupportedFormat;
            }

            if (!PatchFormat.IsKnownProduct(manifest.Product)
                || !PatchFormat.IsKnownChannel(manifest.Channel)
                || !IsValidReleaseId(manifest.ReleaseId)
                || !IsVisibleAscii(manifest.BuildId, MAX_BUILD_ID_LENGTH)
                || !IsKnownBuildKind(manifest.BuildKind)
                || !IsPrintableText(manifest.DisplayVersion, MAX_DISPLAY_VERSION_LENGTH)
                || !TryParseLauncherVersion(manifest.MinLauncherVersion, out _)
                || (manifest.CreatedAtUnixMs < 0))
            {
                return EPatchError.InvalidField;
            }

            // stable은 출시 빌드만 받는다. 게시 도구도 막지만 런처도 같은 규칙으로 한 번 더 거른다
            if ((manifest.Channel == PatchFormat.CHANNEL_STABLE) && (manifest.BuildKind != BuildIdentity.KIND_RELEASE))
            {
                return EPatchError.InvalidField;
            }

            return ValidateFiles(manifest.Files);
        }

        public static EPatchError ValidateChannelPointer(ChannelPointer pointer)
        {
            ArgumentNullException.ThrowIfNull(pointer);

            if (pointer.FormatVersion != PatchFormat.FORMAT_VERSION)
            {
                return EPatchError.UnsupportedFormat;
            }

            if (!PatchFormat.IsKnownProduct(pointer.Product)
                || !PatchFormat.IsKnownChannel(pointer.Channel)
                || !IsValidReleaseId(pointer.ReleaseId)
                || (pointer.Sequence < MIN_SEQUENCE)
                || !PatchHash.IsSha256Hex(pointer.ManifestSha256)
                || (pointer.PublishedAtUnixMs < 0))
            {
                return EPatchError.InvalidField;
            }

            return EPatchError.None;
        }

        /// <summary> 포인터가 가리킨 매니페스트가 맞는지 확인한다. manifestPayloadSha256은 검증된 매니페스트 payload 바이트의 해시 </summary>
        public static EPatchError CheckPointerMatchesManifest(ChannelPointer pointer, ReleaseManifest manifest,
            string manifestPayloadSha256)
        {
            ArgumentNullException.ThrowIfNull(pointer);
            ArgumentNullException.ThrowIfNull(manifest);
            ArgumentNullException.ThrowIfNull(manifestPayloadSha256);

            if (!string.Equals(pointer.ManifestSha256, manifestPayloadSha256, StringComparison.Ordinal))
            {
                return EPatchError.ManifestHashMismatch;
            }

            if (!string.Equals(pointer.Product, manifest.Product, StringComparison.Ordinal))
            {
                return EPatchError.ProductMismatch;
            }

            if (!string.Equals(pointer.Channel, manifest.Channel, StringComparison.Ordinal))
            {
                return EPatchError.ChannelMismatch;
            }

            if (!string.Equals(pointer.ReleaseId, manifest.ReleaseId, StringComparison.Ordinal))
            {
                return EPatchError.ReleaseMismatch;
            }

            return EPatchError.None;
        }

        /// <summary> 받은 sequence가 마지막으로 본 값보다 작으면 되돌리기로 거절한다. 같으면 정상(업데이트 없음) </summary>
        public static EPatchError CheckSequence(long lastSeenSequence, long receivedSequence)
        {
            return (receivedSequence < lastSeenSequence) ? EPatchError.SequenceRollback : EPatchError.None;
        }

        private static EPatchError ValidateFiles(IReadOnlyList<ManifestFile> files)
        {
            if (files.Count == 0)
            {
                return EPatchError.InvalidField;
            }

            List<string> paths = new List<string>(files.Count);
            long totalSize = 0;
            foreach (ManifestFile? file in files)
            {
                // JSON의 "files":[null]은 역직렬화 후 null 원소로 남는다
                if (file == null)
                {
                    return EPatchError.InvalidField;
                }

                if ((file.Size < 0) || (file.Size > (long.MaxValue - totalSize)) || !PatchHash.IsSha256Hex(file.Sha256))
                {
                    return EPatchError.InvalidField;
                }

                totalSize += file.Size;
                paths.Add(file.Path);
            }

            PatchPaths.TryValidateAll(paths, out EPatchError pathError);
            return pathError;
        }

        private static bool IsKnownBuildKind(string buildKind)
        {
            return (buildKind == BuildIdentity.KIND_DEVELOPMENT) || (buildKind == BuildIdentity.KIND_RELEASE);
        }

        // 앞자리 0을 허용하지 않아 "1.01.0"처럼 같은 버전의 다른 표기를 막는다
        private static bool IsVersionPart(string part)
        {
            if ((part.Length == 0) || (part.Length > MAX_VERSION_PART_DIGITS))
            {
                return false;
            }

            if ((part.Length > 1) && (part[0] == '0'))
            {
                return false;
            }

            foreach (char character in part)
            {
                if ((character < '0') || (character > '9'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsVisibleAscii(string value, int maxLength)
        {
            if ((value.Length == 0) || (value.Length > maxLength))
            {
                return false;
            }

            foreach (char character in value)
            {
                if ((character < FIRST_VISIBLE_ASCII) || (character > LAST_VISIBLE_ASCII))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsPrintableText(string value, int maxLength)
        {
            if (value.Length > maxLength)
            {
                return false;
            }

            foreach (char character in value)
            {
                if ((character < FIRST_PRINTABLE) || (character == DELETE_CHAR))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
