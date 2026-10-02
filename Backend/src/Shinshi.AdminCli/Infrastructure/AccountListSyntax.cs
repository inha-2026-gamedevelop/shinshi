// System
using System;
using System.Collections.Generic;
using System.IO;

// Project
using Shinshi.Contracts.Admin;

namespace Shinshi.AdminCli
{
    /// <summary> 지급 대상 목록 "id,id,..." 또는 "@파일". 파일은 쉼표, 공백, 줄바꿈으로 나누고 #으로 시작하는 줄은 주석이다 </summary>
    internal static class AccountListSyntax
    {
        /****************************************
        *                Fields
        ****************************************/

        private const char FILE_PREFIX    = '@';
        private const char COMMENT_PREFIX = '#';

        private static readonly char[] G_Separators = { ',', ' ', '\t', '\r', '\n' };

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 중복과 형식 오류, 한도 초과를 보내기 전에 막는다. 순서는 입력 순서를 지킨다 </summary>
        public static string[] Parse(string spec, Func<string, string> readFile)
        {
            ArgumentNullException.ThrowIfNull(readFile);
            if (string.IsNullOrWhiteSpace(spec))
            {
                throw new CliUsageException("--accounts가 비어 있습니다.");
            }

            string text = spec.Trim();
            if (text[0] == FILE_PREFIX)
            {
                string path = text.Substring(1);
                try
                {
                    text = StripComments(readFile(path));
                }
                catch (IOException exception)
                {
                    throw new CliUsageException("대상 파일을 읽지 못했습니다(" + path + "): " + exception.Message);
                }
                catch (UnauthorizedAccessException exception)
                {
                    throw new CliUsageException("대상 파일을 읽을 권한이 없습니다(" + path + "): " + exception.Message);
                }
            }

            List<string> ids = new List<string>();
            HashSet<Guid> seen = new HashSet<Guid>();
            foreach (string token in text.Split(G_Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                Guid id = CliFormat.ParseGuid(token, "대상 계정 ID");
                if (!seen.Add(id))
                {
                    throw new CliUsageException("대상 계정 ID가 중복됩니다: " + token);
                }

                ids.Add(id.ToString("D"));
            }

            if (ids.Count == 0)
            {
                throw new CliUsageException("대상 계정이 없습니다.");
            }

            if (ids.Count > AdminGrantCodes.MAX_TARGET_ACCOUNTS)
            {
                throw new CliUsageException("대상 계정은 한 지급에 " + AdminGrantCodes.MAX_TARGET_ACCOUNTS + "개까지입니다: "
                    + ids.Count + "개");
            }

            return ids.ToArray();
        }

        private static string StripComments(string content)
        {
            List<string> lines = new List<string>();
            foreach (string line in content.Split('\n'))
            {
                string trimmed = line.Trim();
                if ((trimmed.Length > 0) && (trimmed[0] != COMMENT_PREFIX))
                {
                    lines.Add(trimmed);
                }
            }

            return string.Join("\n", lines);
        }
    }
}
