// System
using System;
using System.Collections.Generic;

namespace Shinshi.Admin.Web.Models
{
    public enum ETargetKind { Uuid, Name }

    /// <summary> 입력 한 항목. Uuid는 소문자 D 형식으로 정규화한 값이다 </summary>
    public readonly record struct TargetToken(string Text, ETargetKind Kind);

    /// <summary> "캐릭터 이름 또는 계정 ID" 입력의 판별과 여러 대상 분리 </summary>
    public static class TargetInput
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly char[] G_Separators = { '\r', '\n', ',', ';', '\t' };

        /****************************************
        *                Methods
        ****************************************/

        public static bool IsUuid(string? value, out string normalized)
        {
            normalized = string.Empty;
            if ((!Guid.TryParse(value?.Trim(), out Guid id)) || (id == Guid.Empty))
            {
                return false;
            }
            normalized = id.ToString("D");
            return true;
        }

        public static TargetToken Classify(string value)
        {
            string trimmed = value.Trim();
            return IsUuid(trimmed, out string uuid) ? new TargetToken(uuid, ETargetKind.Uuid)
                : new TargetToken(trimmed, ETargetKind.Name);
        }

        /// <summary> 줄바꿈·쉼표·세미콜론으로 나눈다. 공백으로 이어 붙인 UUID 목록도 풀고, 중복은 한 번만 남긴다 </summary>
        public static IReadOnlyList<TargetToken> Parse(string? text)
        {
            List<TargetToken> tokens = new();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (string part in (text ?? string.Empty).Split(G_Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = part.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }
                string[] words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                bool allUuid = words.Length > 1;
                foreach (string word in words)
                {
                    allUuid &= IsUuid(word, out _);
                }
                foreach (string item in allUuid ? words : new[] { trimmed })
                {
                    TargetToken token = Classify(item);
                    if (seen.Add(token.Text))
                    {
                        tokens.Add(token);
                    }
                }
            }
            return tokens;
        }
    }
}
