namespace Shinshi.Contracts
{
    /// <summary> 아이디, 비밀번호, 이메일 형식 규칙. 런처, 홈페이지, API가 같은 규칙으로 검사하며 중복 확인은 API만 한다 </summary>
    public static class AccountCredentialRules
    {
        // 홈페이지는 이 파일과 같은 규칙을 옮겨 쓴다. 규칙을 바꾸면 홈페이지 검사도 함께 바꾼다.
        // 비밀번호는 정규화하지 않는다. 입력한 문자열의 UTF-8 바이트를 그대로 해시한다

        /****************************************
        *                Fields
        ****************************************/

        public const int LOGIN_ID_MIN_LENGTH = 4;
        public const int LOGIN_ID_MAX_LENGTH = 16;
        public const int PASSWORD_MIN_LENGTH = 8;
        public const int PASSWORD_MAX_LENGTH = 64;
        public const int EMAIL_MAX_LENGTH    = 254;

        // RFC 5321의 로컬 부분 상한
        public const int EMAIL_LOCAL_MAX_LENGTH = 64;

        private const char LOGIN_ID_SEPARATOR = '_';
        private const char EMAIL_AT           = '@';
        private const char DOMAIN_DOT         = '.';
        private const char DOMAIN_HYPHEN      = '-';
        private const string EMAIL_LOCAL_SYMBOLS = "!#$%&'*+/=?^_`{|}~.-";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 4~16자의 영문, 숫자, 밑줄만 받아 소문자로 바꾼다. 영문 대소문자 외 문자는 소문자 변환 전에 거절한다 </summary>
        public static bool TryNormalizeLoginId(string input, out string loginId)
        {
            loginId = null;
            if ((input == null) || (input.Length < LOGIN_ID_MIN_LENGTH) || (input.Length > LOGIN_ID_MAX_LENGTH))
            {
                return false;
            }

            char[] lowered = new char[input.Length];
            for (int i = 0; i < input.Length; ++i)
            {
                char c = input[i];
                if ((c >= 'A') && (c <= 'Z'))
                {
                    lowered[i] = (char)(c - 'A' + 'a');
                }
                else if (((c >= 'a') && (c <= 'z')) || ((c >= '0') && (c <= '9')) || (c == LOGIN_ID_SEPARATOR))
                {
                    lowered[i] = c;
                }
                else
                {
                    return false;
                }
            }

            loginId = new string(lowered);
            return true;
        }

        /// <summary> 8~64자, 영문과 숫자를 모두 포함하고 제어 문자가 없으며 아이디(대소문자 무시)와 다르다.
        /// normalizedLoginId는 TryNormalizeLoginId 결과다 </summary>
        public static bool IsAcceptablePassword(string password, string normalizedLoginId)
        {
            if ((password == null) || (password.Length < PASSWORD_MIN_LENGTH) || (password.Length > PASSWORD_MAX_LENGTH))
            {
                return false;
            }

            bool hasLetter = false;
            bool hasDigit = false;
            for (int i = 0; i < password.Length; ++i)
            {
                char c = password[i];
                if (char.IsControl(c))
                {
                    return false;
                }
                if (((c >= 'a') && (c <= 'z')) || ((c >= 'A') && (c <= 'Z')))
                {
                    hasLetter = true;
                }
                else if ((c >= '0') && (c <= '9'))
                {
                    hasDigit = true;
                }
            }

            if ((!hasLetter) || (!hasDigit))
            {
                return false;
            }

            return (normalizedLoginId == null)
                || (!string.Equals(password, normalizedLoginId, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary> 앞뒤 공백을 지운 이메일과 그 소문자 비교 키. 254자 이하, '@' 하나, 점이 있는 ASCII 도메인만 받는다 </summary>
        public static bool TryNormalizeEmail(string input, out string email, out string emailKey)
        {
            email = null;
            emailKey = null;
            if (input == null)
            {
                return false;
            }

            string trimmed = input.Trim();
            if ((trimmed.Length == 0) || (trimmed.Length > EMAIL_MAX_LENGTH))
            {
                return false;
            }

            int at = trimmed.IndexOf(EMAIL_AT);
            if ((at <= 0) || (at != trimmed.LastIndexOf(EMAIL_AT)) || (at > EMAIL_LOCAL_MAX_LENGTH))
            {
                return false;
            }

            if ((!IsValidLocalPart(trimmed, at)) || (!IsValidDomain(trimmed, at + 1)))
            {
                return false;
            }

            email = trimmed;
            emailKey = trimmed.ToLowerInvariant();
            return true;
        }

        private static bool IsValidLocalPart(string email, int end)
        {
            if ((email[0] == DOMAIN_DOT) || (email[end - 1] == DOMAIN_DOT))
            {
                return false;
            }

            for (int i = 0; i < end; ++i)
            {
                char c = email[i];
                if ((!IsAsciiLetterOrDigit(c)) && (EMAIL_LOCAL_SYMBOLS.IndexOf(c) < 0))
                {
                    return false;
                }
                if ((c == DOMAIN_DOT) && (email[i - 1] == DOMAIN_DOT))
                {
                    return false;
                }
            }

            return true;
        }

        // 라벨은 영문, 숫자, 하이픈이고 하이픈으로 시작하거나 끝나지 않는다. 라벨이 둘 이상이어야 한다
        private static bool IsValidDomain(string email, int start)
        {
            if (start >= email.Length)
            {
                return false;
            }

            int labels = 0;
            int labelStart = start;
            for (int i = start; i <= email.Length; ++i)
            {
                bool end = (i == email.Length) || (email[i] == DOMAIN_DOT);
                if (!end)
                {
                    if ((!IsAsciiLetterOrDigit(email[i])) && (email[i] != DOMAIN_HYPHEN))
                    {
                        return false;
                    }
                    continue;
                }

                if ((i == labelStart) || (email[labelStart] == DOMAIN_HYPHEN) || (email[i - 1] == DOMAIN_HYPHEN))
                {
                    return false;
                }
                ++labels;
                labelStart = i + 1;
            }

            return labels >= 2;
        }

        private static bool IsAsciiLetterOrDigit(char c)
        {
            return ((c >= 'a') && (c <= 'z')) || ((c >= 'A') && (c <= 'Z')) || ((c >= '0') && (c <= '9'));
        }
    }
}
