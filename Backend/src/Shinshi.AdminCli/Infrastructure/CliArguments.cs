// System
using System;
using System.Collections.Generic;
using System.Linq;

namespace Shinshi.AdminCli
{
    /// <summary> 파싱한 명령줄. 위치 인자, 값 옵션(반복 가능), 값 없는 플래그로 나뉘고 명령마다 허용 목록으로 검사한다 </summary>
    internal sealed class CliArguments
    {
        /****************************************
        *                Fields
        ****************************************/

        // 모든 명령이 받는 전역 옵션
        public const string OPTION_API          = "api";
        public const string OPTION_OPERATION_ID = "operation-id";
        public const string FLAG_JSON           = "json";
        public const string FLAG_YES            = "yes";
        public const string FLAG_HELP           = "help";

        // 일부 명령만 받는 플래그. 파서가 값 옵션과 구분하려고 미리 알아야 한다
        public const string FLAG_ALL           = "all";
        public const string FLAG_KEEP_SESSIONS = "keep-sessions";
        public const string FLAG_KEEP_INVITES  = "keep-invites";

        private static readonly string[] G_GlobalOptions = { OPTION_API, OPTION_OPERATION_ID };
        private static readonly string[] G_GlobalFlags   = { FLAG_JSON, FLAG_YES, FLAG_HELP };

        private readonly List<string> _positionals;
        private readonly Dictionary<string, List<string>> _options;
        private readonly HashSet<string> _flags;

        /****************************************
        *              Properties
        ****************************************/

        public static IReadOnlyList<string> FlagNames { get; } = new[]
        {
            FLAG_JSON, FLAG_YES, FLAG_HELP, FLAG_ALL, FLAG_KEEP_SESSIONS, FLAG_KEEP_INVITES
        };

        public IReadOnlyList<string> Positionals => _positionals;

        /****************************************
        *                Methods
        ****************************************/

        public CliArguments(List<string> positionals, Dictionary<string, List<string>> options, HashSet<string> flags)
        {
            ArgumentNullException.ThrowIfNull(positionals);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(flags);
            _positionals = positionals;
            _options     = options;
            _flags       = flags;
        }

        /// <summary> 명령 이름 단어를 뺀 나머지 위치 인자로 같은 옵션을 보는 사본 </summary>
        public CliArguments SkipPositionals(int count)
        {
            return new CliArguments(_positionals.Skip(count).ToList(), _options, _flags);
        }

        public bool HasFlag(string name)
        {
            return _flags.Contains(name);
        }

        public bool HasOption(string name)
        {
            return _options.ContainsKey(name);
        }

        /// <summary> 한 번만 받는 값 옵션. 두 번 주면 어느 값을 쓸지 모호하므로 사용법 오류다 </summary>
        public string? Option(string name)
        {
            if (!_options.TryGetValue(name, out List<string>? values))
            {
                return null;
            }

            if (values.Count > 1)
            {
                throw new CliUsageException("--" + name + " 옵션은 한 번만 줄 수 있습니다.");
            }

            return values[0];
        }

        public IReadOnlyList<string> Options(string name)
        {
            return _options.TryGetValue(name, out List<string>? values) ? values : Array.Empty<string>();
        }

        public string RequireOption(string name)
        {
            string? value = Option(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new CliUsageException("--" + name + " 옵션이 필요합니다.");
            }

            return value;
        }

        public string Positional(int index, string label)
        {
            if ((index < 0) || (index >= _positionals.Count) || string.IsNullOrWhiteSpace(_positionals[index]))
            {
                throw new CliUsageException(label + " 인자가 필요합니다.");
            }

            return _positionals[index];
        }

        /// <summary> 허용하지 않은 옵션, 플래그와 남는 위치 인자를 사용법 오류로 막는다. 오타 옵션이 조용히 무시되면 의도와 다른 쓰기가 나간다 </summary>
        public void EnsureOnly(int positionalCount, IEnumerable<string> options, IEnumerable<string> flags)
        {
            if (_positionals.Count > positionalCount)
            {
                throw new CliUsageException("알 수 없는 인자입니다: " + _positionals[positionalCount]);
            }

            HashSet<string> allowedOptions = new HashSet<string>(options.Concat(G_GlobalOptions), StringComparer.Ordinal);
            foreach (string name in _options.Keys)
            {
                if (!allowedOptions.Contains(name))
                {
                    throw new CliUsageException("이 명령은 --" + name + " 옵션을 받지 않습니다.");
                }
            }

            HashSet<string> allowedFlags = new HashSet<string>(flags.Concat(G_GlobalFlags), StringComparer.Ordinal);
            foreach (string name in _flags)
            {
                if (!allowedFlags.Contains(name))
                {
                    throw new CliUsageException("이 명령은 --" + name + " 플래그를 받지 않습니다.");
                }
            }
        }
    }
}
