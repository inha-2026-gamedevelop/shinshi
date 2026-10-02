// System
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Shinshi.Release
{
    // "--name value" 쌍만 받는다. 모르는 이름, 중복, 값 누락은 모두 오류다
    internal sealed class OptionBag
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string OPTION_PREFIX = "--";

        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

        /****************************************
        *                Methods
        ****************************************/

        public static bool TryParse(string[] args, int start, IReadOnlyCollection<string> allowed, [NotNullWhen(true)] out OptionBag? bag,
            [NotNullWhen(false)] out string? error)
        {
            bag   = null;
            error = null;

            OptionBag parsed = new OptionBag();
            int       index  = start;
            while (index < args.Length)
            {
                string name = args[index];
                if (!name.StartsWith(OPTION_PREFIX, StringComparison.Ordinal) || !Contains(allowed, name))
                {
                    error = "Unknown option: " + name;
                    return false;
                }

                if ((index + 1 >= args.Length) || args[index + 1].StartsWith(OPTION_PREFIX, StringComparison.Ordinal))
                {
                    error = "Option " + name + " needs a value.";
                    return false;
                }

                if (!parsed._values.TryAdd(name, args[index + 1]))
                {
                    error = "Option " + name + " was given more than once.";
                    return false;
                }

                index += 2;
            }

            bag = parsed;
            return true;
        }

        public string? Get(string name)
        {
            return _values.TryGetValue(name, out string? value) ? value : null;
        }

        private static bool Contains(IReadOnlyCollection<string> allowed, string name)
        {
            foreach (string candidate in allowed)
            {
                if (candidate == name)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
