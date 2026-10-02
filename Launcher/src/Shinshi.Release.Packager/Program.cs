// System
using System;

namespace Shinshi.Release
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            return PackagerCommands.Run(args, Console.Out, Console.Error);
        }
    }
}
