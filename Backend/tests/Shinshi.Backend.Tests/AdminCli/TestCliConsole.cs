// System
using System.Collections.Generic;
using System.IO;

// Project
using Shinshi.AdminCli;

namespace Shinshi.Backend.Tests.AdminCli
{
    /// <summary> 입력 줄을 미리 넣고 표준 출력과 오류를 문자열로 모으는 콘솔 </summary>
    internal sealed class TestCliConsole : ICliConsole
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Queue<string?> _lines;
        private readonly StringWriter _out = new StringWriter();
        private readonly StringWriter _error = new StringWriter();

        /****************************************
        *              Properties
        ****************************************/

        public TextWriter Out => _out;
        public TextWriter Error => _error;
        public string OutText => _out.ToString();
        public string ErrorText => _error.ToString();
        public int SecretReads { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        public TestCliConsole(params string?[] lines)
        {
            _lines = new Queue<string?>(lines);
        }

        public string? ReadLine()
        {
            return _lines.Count > 0 ? _lines.Dequeue() : null;
        }

        public string? ReadSecret(string prompt)
        {
            ++SecretReads;
            _error.Write(prompt);
            return _lines.Count > 0 ? _lines.Dequeue() : null;
        }
    }
}
