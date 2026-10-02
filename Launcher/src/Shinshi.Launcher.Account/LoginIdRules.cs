// System
using System;

namespace Shinshi.Launcher.Account
{
    /// <summary> 아이디 형식 규칙. 서버가 최종 판정하고 런처는 보내기 전에 걸러 안내만 한다 </summary>
    public static class LoginIdRules
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int MIN_LENGTH = 4;
        public const int MAX_LENGTH = 16;

        private const char UNDERSCORE = '_';

        /****************************************
        *                Methods
        ****************************************/

        // 서버가 소문자로 저장하고 비교하므로 보내는 값도 소문자로 맞춘다
        public static string Normalize(string? loginId) { return (loginId ?? string.Empty).Trim().ToLowerInvariant(); }

        /// <summary> 정규화한 아이디가 4~16자 영문 소문자, 숫자, _인지 본다 </summary>
        public static bool IsValid(string normalized)
        {
            ArgumentNullException.ThrowIfNull(normalized);

            if ((normalized.Length < MIN_LENGTH) || (normalized.Length > MAX_LENGTH))
            {
                return false;
            }

            foreach (char c in normalized)
            {
                if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && (c != UNDERSCORE))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
