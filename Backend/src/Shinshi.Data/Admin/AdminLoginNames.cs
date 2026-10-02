// System
using System.Globalization;

namespace Shinshi.Data.Admin
{
    /// <summary> 운영자 로그인 이름 정규화. 대소문자만 다른 두 계정이 생기지 않게 소문자로 저장하고 찾는다 </summary>
    public static class AdminLoginNames
    {
        /****************************************
        *                Fields
        ****************************************/

        // DB CHECK '^[a-z0-9][a-z0-9._-]{2,63}$'와 같은 한도
        public const int MIN_LENGTH = 3;
        public const int MAX_LENGTH = 64;

        private const char MAX_ASCII = (char)0x7F;

        /****************************************
        *                Methods
        ****************************************/

        public static bool TryNormalize(string? input, out string normalized)
        {
            normalized = string.Empty;
            if ((input == null) || (input.Length < MIN_LENGTH) || (input.Length > MAX_LENGTH))
            {
                return false;
            }

            // ASCII만 받는다. 켈빈 기호처럼 소문자로 바꾸면 ASCII가 되는 문자가 다른 표기로 같은 계정을 가리키지 않게 한다
            for (int index = 0; index < input.Length; ++index)
            {
                if (input[index] > MAX_ASCII)
                {
                    return false;
                }
            }

            string lower = input.ToLower(CultureInfo.InvariantCulture);
            for (int index = 0; index < lower.Length; ++index)
            {
                char character = lower[index];
                bool alphanumeric = ((character >= 'a') && (character <= 'z')) || ((character >= '0') && (character <= '9'));
                bool punctuation = (character == '.') || (character == '_') || (character == '-');
                if (!alphanumeric && ((index == 0) || !punctuation))
                {
                    return false;
                }
            }

            normalized = lower;
            return true;
        }
    }
}
