// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Shinshi.AdminCli
{
    /// <summary> 고정 폭 글꼴 터미널용 단순 표. 한글처럼 두 칸을 차지하는 글자를 세어 열을 맞춘다 </summary>
    internal sealed class CliTable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string COLUMN_GAP = "  ";
        private const int    WIDE_WIDTH = 2;

        // 동아시아 전각 글자 대역. 터미널이 두 칸으로 그린다
        private const int HANGUL_JAMO_START     = 0x1100;
        private const int HANGUL_JAMO_END       = 0x115F;
        private const int CJK_START             = 0x2E80;
        private const int CJK_END               = 0xA4CF;
        private const int HANGUL_SYLLABLE_START = 0xAC00;
        private const int HANGUL_SYLLABLE_END   = 0xD7A3;
        private const int FULLWIDTH_START       = 0xFF00;
        private const int FULLWIDTH_END         = 0xFF60;

        private readonly string[] _headers;
        private readonly List<string[]> _rows = new List<string[]>();

        /****************************************
        *              Properties
        ****************************************/

        public int RowCount => _rows.Count;

        /****************************************
        *                Methods
        ****************************************/

        public CliTable(params string[] headers)
        {
            ArgumentNullException.ThrowIfNull(headers);
            _headers = headers;
        }

        public void Add(params string[] cells)
        {
            ArgumentNullException.ThrowIfNull(cells);
            if (cells.Length != _headers.Length)
            {
                throw new ArgumentException("Row cell count must match the header count.", nameof(cells));
            }

            _rows.Add(cells);
        }

        public void Write(TextWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            int[] widths = new int[_headers.Length];
            Measure(_headers, widths);
            foreach (string[] row in _rows)
            {
                Measure(row, widths);
            }

            WriteRow(writer, _headers, widths);
            foreach (string[] row in _rows)
            {
                WriteRow(writer, row, widths);
            }
        }

        public static int DisplayWidth(string text)
        {
            int width = 0;
            foreach (char character in text)
            {
                width += IsWide(character) ? WIDE_WIDTH : 1;
            }

            return width;
        }

        private static bool IsWide(char character)
        {
            int code = character;
            return ((code >= HANGUL_JAMO_START) && (code <= HANGUL_JAMO_END))
                || ((code >= CJK_START) && (code <= CJK_END))
                || ((code >= HANGUL_SYLLABLE_START) && (code <= HANGUL_SYLLABLE_END))
                || ((code >= FULLWIDTH_START) && (code <= FULLWIDTH_END));
        }

        private static void Measure(string[] cells, int[] widths)
        {
            for (int i = 0; i < cells.Length; ++i)
            {
                widths[i] = Math.Max(widths[i], DisplayWidth(cells[i] ?? string.Empty));
            }
        }

        // 마지막 열은 채우지 않아 줄 끝 공백을 남기지 않는다
        private static void WriteRow(TextWriter writer, string[] cells, int[] widths)
        {
            StringBuilder line = new StringBuilder();
            for (int i = 0; i < cells.Length; ++i)
            {
                string cell = cells[i] ?? string.Empty;
                line.Append(cell);
                if (i < cells.Length - 1)
                {
                    line.Append(' ', widths[i] - DisplayWidth(cell));
                    line.Append(COLUMN_GAP);
                }
            }

            writer.WriteLine(line.ToString());
        }
    }
}
