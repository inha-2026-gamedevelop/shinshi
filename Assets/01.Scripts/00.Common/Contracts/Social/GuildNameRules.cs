// System
using System;
using System.Text;

namespace Shinshi.Contracts
{
    /// <summary> 길드 이름 규칙. 캐릭터 이름 규칙(CharacterNameRules)과 같은 순서이며 길이만 2~8자다 </summary>
    public static class GuildNameRules
    {
        // 앞뒤 공백 제거 -> NFKC 정규화 -> 정규화한 값으로 문자, 길이 검사 -> 정규화한 값이 표시 이름 ->
        // 표시 이름의 문화권 독립 소문자가 중복 비교 키다. 전각, 반각과 대소문자만 다른 이름은 같은 이름이다.
        // DB CHECK(guilds.name ~ '^[가-힣A-Za-z0-9]{2,8}$', name_key = lower(name))와 같은 규칙이다.
        // 금칙어는 1차 없음(사용자 결정). 캐릭터 이름 금칙어를 정할 때 함께 적용한다

        /****************************************
        *                Fields
        ****************************************/

        public const int MIN_LENGTH = 2;
        public const int MAX_LENGTH = 8;

        // 정규화 전 입력 상한. NFKC가 글자를 늘릴 수 있어 큰 입력은 정규화하기 전에 거절한다
        public const int MAX_INPUT_LENGTH = CharacterNameRules.MAX_INPUT_LENGTH;

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
                if (!CharacterNameRules.IsAllowedChar(normalized[i]))
                {
                    return false;
                }
            }

            name = normalized;
            nameKey = normalized.ToLowerInvariant();
            return true;
        }
    }
}
