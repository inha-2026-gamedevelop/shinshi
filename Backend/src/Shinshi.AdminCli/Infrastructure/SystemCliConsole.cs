// System
using System;
using System.IO;
using System.Text;

namespace Shinshi.AdminCli
{
    /// <summary> 실제 터미널. 비밀 입력은 ReadKey(intercept)로 받아 화면과 셸 기록에 남지 않는다 </summary>
    internal sealed class SystemCliConsole : ICliConsole
    {
        /****************************************
        *              Properties
        ****************************************/

        public TextWriter Out => Console.Out;
        public TextWriter Error => Console.Error;

        /****************************************
        *                Methods
        ****************************************/

        public string? ReadLine()
        {
            return Console.In.ReadLine();
        }

        public string? ReadSecret(string prompt)
        {
            Console.Error.Write(prompt);
            if (Console.IsInputRedirected)
            {
                string? line = Console.In.ReadLine();
                Console.Error.WriteLine();
                return line;
            }

            StringBuilder builder = new StringBuilder();
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter)
                {
                    break;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (builder.Length > 0)
                    {
                        builder.Length = builder.Length - 1;
                    }

                    continue;
                }

                if (!char.IsControl(key.KeyChar))
                {
                    builder.Append(key.KeyChar);
                }
            }

            Console.Error.WriteLine();
            return builder.ToString();
        }
    }
}
