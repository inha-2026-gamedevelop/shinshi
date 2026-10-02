// System
using System;
using System.IO;

// Third-party
using Xunit;

// Project
using Shinshi.Patch;
using Shinshi.Release;

namespace Shinshi.Launcher.Tests.Packager
{
    public sealed class PackagerCommandsTests : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PackagerTestFactory _factory = new PackagerTestFactory();

        /****************************************
        *                Methods
        ****************************************/

        public void Dispose() { _factory.Dispose(); }

        private static int Run(out string output, out string error, params string[] args)
        {
            using StringWriter outWriter = new StringWriter();
            using StringWriter errWriter = new StringWriter();
            int code = PackagerCommands.Run(args, outWriter, errWriter);
            output = outWriter.ToString();
            error  = errWriter.ToString();
            return code;
        }

        [Fact]
        public void Run_NoArguments_IsUsageError()
        {
            Assert.Equal(PackagerExitCodes.USAGE, Run(out _, out string error));
            Assert.Contains("usage:", error);
        }

        [Fact]
        public void Run_UnknownCommand_IsUsageError()
        {
            Assert.Equal(PackagerExitCodes.USAGE, Run(out _, out _, "upload"));
        }

        [Fact]
        public void Run_UnknownOption_IsUsageError()
        {
            Assert.Equal(PackagerExitCodes.USAGE, Run(out _, out _, "keygen", "--out", _factory.Root, "--bogus", "x"));
        }

        [Fact]
        public void Run_OptionWithoutValue_IsUsageError()
        {
            Assert.Equal(PackagerExitCodes.USAGE, Run(out _, out _, "keygen", "--out"));
        }

        [Fact]
        public void Run_DuplicateOption_IsUsageError()
        {
            Assert.Equal(PackagerExitCodes.USAGE, Run(out _, out _, "keygen", "--out", "a", "--out", "b"));
        }

        [Fact]
        public void Run_PublishMissingRequiredOption_IsUsageError()
        {
            Assert.Equal(PackagerExitCodes.USAGE, Run(out _, out _, "publish", "--build", _factory.Root));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("abc")]
        public void Run_PublishBadSequence_IsUsageError(string sequence)
        {
            int code = Run(out _, out _, "publish", "--build", _factory.Root, "--product", "game", "--channel", "internal", "--sequence", sequence,
                "--key-file", "k", "--out", "o");

            Assert.Equal(PackagerExitCodes.USAGE, code);
        }

        [Fact]
        public void Run_KeygenThenPublish_WorksEndToEndThroughArguments()
        {
            string keys   = Path.Combine(_factory.Root, "keys");
            string build  = _factory.CreateGameBuild();
            string outDir = Path.Combine(_factory.Root, "out");

            Assert.Equal(PackagerExitCodes.SUCCESS, Run(out _, out _, "keygen", "--out", keys));
            int code = Run(out string output, out string error, "publish", "--build", build, "--product", "game", "--channel", "internal",
                "--sequence", "1", "--key-file", Path.Combine(keys, KeyGenerator.PRIVATE_KEY_FILE_NAME), "--out", outDir,
                "--release-id", "rel-1", "--min-launcher", "1.2.3");

            Assert.Equal(PackagerExitCodes.SUCCESS, code);
            Assert.Equal(string.Empty, error);
            Assert.Contains("sequence:  1", output);
            Assert.True(PatchSigningKey.TryFromBase64Seed(File.ReadAllText(Path.Combine(keys, KeyGenerator.PRIVATE_KEY_FILE_NAME)),
                out PatchSigningKey? key));
            ReleaseManifest manifest = PackagerTestFactory.OpenManifest(outDir, PatchFormat.PRODUCT_GAME, "rel-1", key, out _);
            Assert.Equal("1.2.3", manifest.MinLauncherVersion);
        }

        [Fact]
        public void Run_LauncherPublishThroughArguments_UsesIdentityOptions()
        {
            string keys   = Path.Combine(_factory.Root, "keys");
            string build  = _factory.CreateLauncherBuild();
            string outDir = Path.Combine(_factory.Root, "out");
            Assert.Equal(PackagerExitCodes.SUCCESS, Run(out _, out _, "keygen", "--out", keys));

            int code = Run(out _, out string error, "publish", "--build", build, "--product", "launcher", "--channel", "stable",
                "--sequence", "5", "--key-file", Path.Combine(keys, KeyGenerator.PRIVATE_KEY_FILE_NAME), "--out", outDir,
                "--release-id", "l-1", "--build-id", "launcher-9", "--build-kind", "Release", "--display-version", "1.0.0");

            Assert.Equal(PackagerExitCodes.SUCCESS, code);
            Assert.Equal(string.Empty, error);
        }
    }
}
