// System
using System;
using System.Globalization;
using System.IO;

namespace Shinshi.Release
{
    /// <summary> 명령줄을 읽어 keygen과 publish로 나눈다. Main은 이것만 부른다 </summary>
    public static class PackagerCommands
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string COMMAND_KEYGEN  = "keygen";
        public const string COMMAND_PUBLISH = "publish";

        private static readonly string[] KEYGEN_OPTIONS =
        {
            PackagerOptionNames.OUT
        };

        private static readonly string[] PUBLISH_OPTIONS =
        {
            PackagerOptionNames.BUILD, PackagerOptionNames.PRODUCT, PackagerOptionNames.CHANNEL, PackagerOptionNames.SEQUENCE,
            PackagerOptionNames.KEY_FILE, PackagerOptionNames.OUT, PackagerOptionNames.RELEASE_ID, PackagerOptionNames.MIN_LAUNCHER,
            PackagerOptionNames.BUILD_ID, PackagerOptionNames.BUILD_KIND, PackagerOptionNames.DISPLAY_VERSION
        };

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 종료 코드를 돌려준다 </summary>
        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(error);

            if (args.Length == 0)
            {
                return Usage(error, "No command given.");
            }

            switch (args[0])
            {
                case COMMAND_KEYGEN:
                    return RunKeygen(args, output, error);
                case COMMAND_PUBLISH:
                    return RunPublish(args, output, error);
                default:
                    return Usage(error, "Unknown command: " + args[0]);
            }
        }

        private static int RunKeygen(string[] args, TextWriter output, TextWriter error)
        {
            if (!OptionBag.TryParse(args, 1, KEYGEN_OPTIONS, out OptionBag? bag, out string? parseError))
            {
                return Usage(error, parseError);
            }

            string? outFolder = bag.Get(PackagerOptionNames.OUT);
            if (outFolder == null)
            {
                return Usage(error, "Missing option " + PackagerOptionNames.OUT);
            }

            return KeyGenerator.Run(new KeygenOptions(outFolder), output, error);
        }

        private static int RunPublish(string[] args, TextWriter output, TextWriter error)
        {
            if (!OptionBag.TryParse(args, 1, PUBLISH_OPTIONS, out OptionBag? bag, out string? parseError))
            {
                return Usage(error, parseError);
            }

            string? build     = bag.Get(PackagerOptionNames.BUILD);
            string? product   = bag.Get(PackagerOptionNames.PRODUCT);
            string? channel   = bag.Get(PackagerOptionNames.CHANNEL);
            string? sequence  = bag.Get(PackagerOptionNames.SEQUENCE);
            string? keyFile   = bag.Get(PackagerOptionNames.KEY_FILE);
            string? outFolder = bag.Get(PackagerOptionNames.OUT);
            if ((build == null) || (product == null) || (channel == null) || (sequence == null) || (keyFile == null) || (outFolder == null))
            {
                return Usage(error, "Missing required option, publish needs --build --product --channel --sequence --key-file --out.");
            }

            if (!long.TryParse(sequence, NumberStyles.None, CultureInfo.InvariantCulture, out long sequenceNumber))
            {
                return Usage(error, "Sequence must be a positive integer: " + sequence);
            }

            PublishOptions options = new PublishOptions(build, product, channel, sequenceNumber, keyFile, outFolder)
            {
                ReleaseId      = bag.Get(PackagerOptionNames.RELEASE_ID),
                MinLauncher    = bag.Get(PackagerOptionNames.MIN_LAUNCHER) ?? PublishOptions.DEFAULT_MIN_LAUNCHER,
                BuildId        = bag.Get(PackagerOptionNames.BUILD_ID),
                BuildKind      = bag.Get(PackagerOptionNames.BUILD_KIND),
                DisplayVersion = bag.Get(PackagerOptionNames.DISPLAY_VERSION)
            };
            return new ReleasePublisher().Run(options, output, error);
        }

        private static int Usage(TextWriter error, string message)
        {
            error.WriteLine("error: " + message);
            error.WriteLine("usage:");
            error.WriteLine("  keygen  --out <dir>");
            error.WriteLine("  publish --build <folder> --product game|launcher --channel internal|stable --sequence <n>");
            error.WriteLine("          --key-file <private key> --out <folder> [--release-id <id>] [--min-launcher 1.0.0]");
            error.WriteLine("          [--build-id <id> --build-kind Development|Release --display-version <v>]  (launcher only)");
            return PackagerExitCodes.USAGE;
        }
    }
}
