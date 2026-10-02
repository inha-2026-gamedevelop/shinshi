// System
using System;
using System.Collections.Generic;
using System.Linq;

namespace Shinshi.AdminCli
{
    /// <summary> --name value, --name=value, --flag, 위치 인자를 나눈다. 외부 패키지 없이 BCL만 쓴다 </summary>
    internal static class CliArgumentParser
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string OPTION_PREFIX   = "--";
        private const string END_OF_OPTIONS  = "--";
        private const string SHORT_HELP      = "-h";
        private const char   VALUE_SEPARATOR = '=';

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 플래그 이름은 CliArguments.FlagNames로 정해 둔다. 나머지 --name은 다음 토큰을 값으로 받는다.
        /// "--" 뒤는 모두 위치 인자다 </summary>
        public static CliArguments Parse(IReadOnlyList<string> args)
        {
            ArgumentNullException.ThrowIfNull(args);

            List<string> positionals = new List<string>();
            Dictionary<string, List<string>> options = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            HashSet<string> flags = new HashSet<string>(StringComparer.Ordinal);
            bool optionsEnded = false;
            for (int i = 0; i < args.Count; ++i)
            {
                string token = args[i] ?? string.Empty;
                if (optionsEnded)
                {
                    positionals.Add(token);
                    continue;
                }

                if (string.Equals(token, END_OF_OPTIONS, StringComparison.Ordinal))
                {
                    optionsEnded = true;
                    continue;
                }

                if (string.Equals(token, SHORT_HELP, StringComparison.Ordinal))
                {
                    flags.Add(CliArguments.FLAG_HELP);
                    continue;
                }

                if (!token.StartsWith(OPTION_PREFIX, StringComparison.Ordinal))
                {
                    positionals.Add(token);
                    continue;
                }

                string body = token.Substring(OPTION_PREFIX.Length);
                int separator = body.IndexOf(VALUE_SEPARATOR);
                string name = separator >= 0 ? body.Substring(0, separator) : body;
                ValidateName(name, token);
                bool isFlag = CliArguments.FlagNames.Contains(name, StringComparer.Ordinal);
                if (isFlag)
                {
                    if (separator >= 0)
                    {
                        throw new CliUsageException("--" + name + " 플래그는 값을 받지 않습니다.");
                    }

                    flags.Add(name);
                    continue;
                }

                string value;
                if (separator >= 0)
                {
                    value = body.Substring(separator + 1);
                }
                else
                {
                    // 다음 토큰이 또 다른 옵션이면 값을 빠뜨린 것이다. 음수 delta(-5)는 "--"로 시작하지 않아 값으로 받는다
                    if ((i + 1 >= args.Count) || (args[i + 1] ?? string.Empty).StartsWith(OPTION_PREFIX,
                        StringComparison.Ordinal))
                    {
                        throw new CliUsageException("--" + name + " 옵션에 값이 필요합니다.");
                    }

                    ++i;
                    value = args[i] ?? string.Empty;
                }

                if (!options.TryGetValue(name, out List<string>? values))
                {
                    values = new List<string>();
                    options.Add(name, values);
                }

                values.Add(value);
            }

            return new CliArguments(positionals, options, flags);
        }

        private static void ValidateName(string name, string token)
        {
            if (name.Length == 0)
            {
                throw new CliUsageException("옵션 이름이 비어 있습니다: " + token);
            }

            foreach (char character in name)
            {
                bool allowed = ((character >= 'a') && (character <= 'z')) || ((character >= '0') && (character <= '9'))
                    || (character == '-');
                if (!allowed)
                {
                    throw new CliUsageException("옵션 이름은 소문자, 숫자, 하이픈만 씁니다: " + token);
                }
            }
        }
    }
}
