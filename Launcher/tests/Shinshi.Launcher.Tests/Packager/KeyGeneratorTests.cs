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
    public sealed class KeyGeneratorTests : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PackagerTestFactory _factory = new PackagerTestFactory();

        /****************************************
        *                Methods
        ****************************************/

        public void Dispose() { _factory.Dispose(); }

        [Fact]
        public void Run_NewFolder_WritesKeyPairThatMatchesPrintedKeyId()
        {
            string outDir = Path.Combine(_factory.Root, "keys");
            using StringWriter output = new StringWriter();
            using StringWriter error  = new StringWriter();

            int code = KeyGenerator.Run(new KeygenOptions(outDir), output, error);

            Assert.Equal(PackagerExitCodes.SUCCESS, code);
            Assert.Equal(string.Empty, error.ToString());
            string privateText = File.ReadAllText(Path.Combine(outDir, KeyGenerator.PRIVATE_KEY_FILE_NAME));
            string publicText  = File.ReadAllText(Path.Combine(outDir, KeyGenerator.PUBLIC_KEY_FILE_NAME));
            Assert.True(PatchSigningKey.TryFromBase64Seed(privateText, out PatchSigningKey? key));
            Assert.Equal(key.PublicKey.ToBase64(), publicText);
            Assert.Contains(key.KeyId, output.ToString());
            Assert.Contains(publicText, output.ToString());
        }

        [Fact]
        public void Run_Output_DoesNotContainTheSeed()
        {
            string outDir = Path.Combine(_factory.Root, "keys");
            using StringWriter output = new StringWriter();
            using StringWriter error  = new StringWriter();

            KeyGenerator.Run(new KeygenOptions(outDir), output, error);

            string seed = File.ReadAllText(Path.Combine(outDir, KeyGenerator.PRIVATE_KEY_FILE_NAME));
            Assert.DoesNotContain(seed, output.ToString() + error.ToString());
        }

        [Fact]
        public void Run_PrivateKeyFileExists_RefusesAndKeepsOriginal()
        {
            string outDir = _factory.NewFolder("keys");
            string path   = Path.Combine(outDir, KeyGenerator.PRIVATE_KEY_FILE_NAME);
            File.WriteAllText(path, "original");
            using StringWriter output = new StringWriter();
            using StringWriter error  = new StringWriter();

            int code = KeyGenerator.Run(new KeygenOptions(outDir), output, error);

            Assert.Equal(PackagerExitCodes.OUTPUT_CONFLICT, code);
            Assert.Equal("original", File.ReadAllText(path));
            Assert.False(File.Exists(Path.Combine(outDir, KeyGenerator.PUBLIC_KEY_FILE_NAME)));
            Assert.Equal(string.Empty, output.ToString());
        }

        [Fact]
        public void Run_PublicKeyFileExists_RefusesAndWritesNoPrivateKey()
        {
            string outDir = _factory.NewFolder("keys");
            File.WriteAllText(Path.Combine(outDir, KeyGenerator.PUBLIC_KEY_FILE_NAME), "original");
            using StringWriter output = new StringWriter();
            using StringWriter error  = new StringWriter();

            int code = KeyGenerator.Run(new KeygenOptions(outDir), output, error);

            Assert.Equal(PackagerExitCodes.OUTPUT_CONFLICT, code);
            Assert.False(File.Exists(Path.Combine(outDir, KeyGenerator.PRIVATE_KEY_FILE_NAME)));
        }

        [Fact]
        public void Run_TwiceInDifferentFolders_ProducesDifferentKeys()
        {
            using StringWriter output = new StringWriter();
            using StringWriter error  = new StringWriter();
            KeyGenerator.Run(new KeygenOptions(Path.Combine(_factory.Root, "a")), output, error);
            KeyGenerator.Run(new KeygenOptions(Path.Combine(_factory.Root, "b")), output, error);

            Assert.NotEqual(File.ReadAllText(Path.Combine(_factory.Root, "a", KeyGenerator.PRIVATE_KEY_FILE_NAME)),
                File.ReadAllText(Path.Combine(_factory.Root, "b", KeyGenerator.PRIVATE_KEY_FILE_NAME)));
        }
    }
}
