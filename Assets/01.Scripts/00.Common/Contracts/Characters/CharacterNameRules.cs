// System
using System;
using System.Text;

namespace Shinshi.Contracts
{
    /// <summary> 캐릭터 이름(닉네임) 처리 순서. 게임, 홈페이지, API의 입력 검사와 조회, 생성이 모두 이 순서를 쓴다 </summary>
    public static class CharacterNameRules
    {
        // 앞뒤 공백 제거 -> NFKC 정규화 -> 정규화한 값으로 문자, 길이 검사 -> 정규화한 값이 표시 이름 ->
        // 표시 이름의 문화권 독립 소문자가 중복 비교 키다. 그래서 전각, 반각과 대소문자만 다른 이름은 같은 이름이다.
        // 홈페이지는 같은 순서를 옮겨 쓴다(String.prototype.normalize('NFKC'), toLowerCase).
        // TODO(민성): 금칙어 목록 형식과 관리 주체는 기획 미정

        /****************************************
        *                Fields
        ****************************************/

        public const int MIN_LENGTH = 2;
        public const int MAX_LENGTH = 12;

        // 정규화 전 입력 상한. NFKC는 한 글자를 여러 글자로 늘릴 수 있어 큰 입력을 정규화하기 전에 자른다
        public const int MAX_INPUT_LENGTH = 64;

        private const char HANGUL_SYLLABLE_FIRST = '가';
        private const char HANGUL_SYLLABLE_LAST  = '힣';

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 규칙을 통과하면 저장할 표시 이름과 중복 비교 키를 돌려준다 </summary>
        public static bool TryNormalize(string input, out string name, out string nameKey)
        {
            name = null;
            nameKey = null;
            if ((input == null) || (input.Length > MAX_INPUT_LENGTH))
            {
                return false;
            }

            string normalized;
            try
            {
                normalized = input.Trim().Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                // 짝이 맞지 않는 대리 문자처럼 정규화할 수 없는 입력
                return false;
            }

            if ((normalized.Length < MIN_LENGTH) || (normalized.Length > MAX_LENGTH))
            {
                return false;
            }

            for (int i = 0; i < normalized.Length; ++i)
            {
                if (!IsAllowedChar(normalized[i]))
                {
                    return false;
                }
            }

            name = normalized;
            nameKey = normalized.ToLowerInvariant();
            return true;
        }

        /// <summary> 정규화 뒤 허용 문자. 완성형 한글, 영문, 숫자뿐이며 공백과 기호는 받지 않는다 </summary>
        public static bool IsAllowedChar(char c)
        {
            return ((c >= HANGUL_SYLLABLE_FIRST) && (c <= HANGUL_SYLLABLE_LAST))
                || ((c >= 'a') && (c <= 'z')) || ((c >= 'A') && (c <= 'Z')) || ((c >= '0') && (c <= '9'));
        }
    }
}
