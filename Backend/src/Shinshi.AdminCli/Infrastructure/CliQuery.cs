// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Shinshi.AdminCli
{
    /// <summary> 조회 문자열을 만든다. 값이 null이면 그 이름을 빼서 서버 기본값을 쓰게 한다 </summary>
    internal sealed class CliQuery
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly List<KeyValuePair<string, string>> _pairs = new List<KeyValuePair<string, string>>();

        /****************************************
        *                Methods
        ****************************************/

        public CliQuery Add(string name, string? value)
        {
            if (value != null)
            {
                _pairs.Add(new KeyValuePair<string, string>(name, value));
            }

            return this;
        }

        public CliQuery Add(string name, long? value)
        {
            return Add(name, value?.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary> 앞 페이지와 같은 조건에 cursor만 바꾼 사본 </summary>
        public CliQuery WithCursor(string cursorName, string? cursor)
        {
            CliQuery copy = new CliQuery();
            foreach (KeyValuePair<string, string> pair in _pairs)
            {
                if (!string.Equals(pair.Key, cursorName, StringComparison.Ordinal))
                {
                    copy._pairs.Add(pair);
                }
            }

            return copy.Add(cursorName, cursor);
        }

        public string Append(string path)
        {
            if (_pairs.Count == 0)
            {
                return path;
            }

            StringBuilder builder = new StringBuilder(path);
            for (int i = 0; i < _pairs.Count; ++i)
            {
                builder.Append(i == 0 ? '?' : '&');
                builder.Append(Uri.EscapeDataString(_pairs[i].Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(_pairs[i].Value));
            }

            return builder.ToString();
        }
    }
}
