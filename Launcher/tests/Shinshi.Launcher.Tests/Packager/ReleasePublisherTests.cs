// System
using System;
using System.IO;

// Third-party
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Patch;
using Shinshi.Release;

namespace Shinshi.Launcher.Tests.Packager
{
    public sealed class ReleasePublisherTests : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PackagerTestFactory _factory = new PackagerTestFactory();
        private readonly PatchSigningKey     _key     = PatchSigningKey.Generate();

        // 고정 시각을 돌려주는 시계
        private sealed class FixedTimeProvider : TimeProvider
        {
            private readonly DateTimeOffset _now;

            public FixedTimeProvider(DateTimeOffset now) { _now = now; }

            public override DateTimeOffset GetUtcNow() { return _now; }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Dispose() { _factory.Dispose(); }

        [Fact]
        public void Run_GameInternal_WritesSignedLayoutThatOpensWithCore()
        {
            string build   = _factory.CreateGameBuild();
            string outDir  = _factory.NewFolder("out");
            string keyFile = _factory.WriteKeyFile(_key);

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir, 3), out string output, out string error);

            Assert.Equal(PackagerExitCodes.SUCCESS, code);
            Assert.Equal(string.Empty, error);
            ChannelPointer pointer = PackagerTestFactory.OpenPointer(outDir, PatchFormat.PRODUCT_GAME, PatchFormat.CHANNEL_INTERNAL, _key);
            Assert.Equal(3, pointer.Sequence);
            Assert.Equal("rel-1", pointer.ReleaseId);

            ReleaseManifest manifest = PackagerTestFactory.OpenManifest(outDir, PatchFormat.PRODUCT_GAME, "rel-1", _key, out string payloadSha);
            Assert.Equal(pointer.ManifestSha256, payloadSha);
            Assert.Equal(PackagerTestFactory.BUILD_ID, manifest.BuildId);
            Assert.Equal(BuildIdentity.KIND_RELEASE, manifest.BuildKind);
            Assert.Equal(PackagerTestFactory.DISPLAY, manifest.DisplayVersion);
            Assert.Equal(4, manifest.Files.Count);
            foreach (ManifestFile file in manifest.Files)
            {
                string blob = PackagerTestFactory.BlobPath(outDir, PatchFormat.PRODUCT_GAME, file.Sha256);
                Assert.True(File.Exists(blob), file.Path);
                Assert.Equal(file.Sha256, PatchHash.ComputeFile(blob));
                Assert.Equal(file.Size, new FileInfo(blob).Length);
            }

            Assert.Contains("rel-1", output);
            Assert.Contains("newBlobs:  4", output);
        }

        [Fact]
        public void Run_BuildWithDoNotShipFolders_ExcludesOnlyRootDebugFolders()
        {
            string build   = _factory.CreateGameBuild();
            string outDir  = _factory.NewFolder("out");
            string keyFile = _factory.WriteKeyFile(_key);
            _factory.WriteText(build, "Shinshi_BackUpThisFolder_ButDontShipItWithYourGame/GameAssembly.pdb", "symbols");
            _factory.WriteText(build, "Shinshi_BurstDebugInformation_DoNotShip/lib.txt", "burst");
            _factory.WriteText(build, "Shinshi_Data/Shinshi_BurstDebugInformation_DoNotShip/kept.txt", "nested");

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir), out _, out string error);

            Assert.Equal(PackagerExitCodes.SUCCESS, code);
            Assert.Equal(string.Empty, error);
            ReleaseManifest manifest = PackagerTestFactory.OpenManifest(outDir, PatchFormat.PRODUCT_GAME, "rel-1", _key, out _);
            Assert.Equal(5, manifest.Files.Count);
            Assert.DoesNotContain(manifest.Files, file => file.Path.Contains("DontShip", StringComparison.Ordinal));
            Assert.Contains(manifest.Files, file => file.Path == "Shinshi_Data/Shinshi_BurstDebugInformation_DoNotShip/kept.txt");
        }

        [Fact]
        public void Run_Output_DoesNotPrintPrivateKey()
        {
            string build   = _factory.CreateGameBuild();
            string keyFile = _factory.WriteKeyFile(_key);

            PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, _factory.NewFolder("out")), out string output, out string error);

            Assert.DoesNotContain(_key.ExportBase64Seed(), output + error);
        }

        [Fact]
        public void Run_StableWithDevelopmentBuild_IsRefusedAndWritesNothing()
        {
            string build   = _factory.CreateGameBuild(buildKind: BuildIdentity.KIND_DEVELOPMENT);
            string outDir  = _factory.NewFolder("out");
            string keyFile = _factory.WriteKeyFile(_key);

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir, channel: PatchFormat.CHANNEL_STABLE),
                out _, out string error);

            Assert.Equal(PackagerExitCodes.RELEASE_REFUSED, code);
            Assert.Contains("Release", error);
            Assert.Equal(0, PackagerTestFactory.CountFiles(outDir));
        }

        [Fact]
        public void Run_StableWithBootConfigMarker_IsRefused()
        {
            string build   = _factory.CreateGameBuild(bootConfig: "gfx-enable-native-jobs=1\nplayer-connection-mode=1\n");
            string outDir  = _factory.NewFolder("out");
            string keyFile = _factory.WriteKeyFile(_key);

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir, channel: PatchFormat.CHANNEL_STABLE),
                out _, out string error);

            Assert.Equal(PackagerExitCodes.RELEASE_REFUSED, code);
            Assert.Contains("player-connection-mode", error);
            Assert.Equal(0, PackagerTestFactory.CountFiles(outDir));
        }

        [Fact]
        public void Run_InternalWithBootConfigMarker_Succeeds()
        {
            string build   = _factory.CreateGameBuild(buildKind: BuildIdentity.KIND_DEVELOPMENT, bootConfig: "player-connection-mode=1\n");
            string keyFile = _factory.WriteKeyFile(_key);

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, _factory.NewFolder("out")), out _, out _);

            Assert.Equal(PackagerExitCodes.SUCCESS, code);
        }

        [Fact]
        public void Run_StableWithCleanReleaseBuild_Succeeds()
        {
            string build   = _factory.CreateGameBuild();
            string outDir  = _factory.NewFolder("out");
            string keyFile = _factory.WriteKeyFile(_key);

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir, channel: PatchFormat.CHANNEL_STABLE),
                out _, out string error);

            Assert.Equal(PackagerExitCodes.SUCCESS, code);
            Assert.Equal(string.Empty, error);
            Assert.True(File.Exists(PackagerTestFactory.PointerPath(outDir, PatchFormat.PRODUCT_GAME, PatchFormat.CHANNEL_STABLE)));
        }

        [Fact]
        public void Run_StableWithoutBootConfig_IsRefusedBecauseItCannotBeVerified()
        {
            string build = _factory.CreateGameBuild();
            File.Delete(Path.Combine(build, "Shinshi_Data", "boot.config"));
            string keyFile = _factory.WriteKeyFile(_key);

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, _factory.NewFolder("out"),
                channel: PatchFormat.CHANNEL_STABLE), out _, out _);

            Assert.Equal(PackagerExitCodes.RELEASE_REFUSED, code);
        }

        [Fact]
        public void Run_GameWithoutBuildIdentity_IsRejected()
        {
            string build = _factory.CreateGameBuild();
            File.Delete(Path.Combine(build, "Shinshi_Data", "StreamingAssets", "build-identity.json"));
            string keyFile = _factory.WriteKeyFile(_key);

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, _factory.NewFolder("out")), out _, out _);

            Assert.Equal(PackagerExitCodes.INVALID_BUILD, code);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void Run_SequenceNotGreaterThanPublished_IsRejectedAndPointerUnchanged(long secondSequence)
        {
            string build   = _factory.CreateGameBuild();
            string outDir  = _factory.NewFolder("out");
            string keyFile = _factory.WriteKeyFile(_key);
            Assert.Equal(PackagerExitCodes.SUCCESS,
                PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir, 2, releaseId: "rel-1"), out _, out _));
            byte[] before = File.ReadAllBytes(PackagerTestFactory.PointerPath(outDir, PatchFormat.PRODUCT_GAME, PatchFormat.CHANNEL_INTERNAL));

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir, secondSequence, releaseId: "rel-2"),
                out _, out string error);

            Assert.Equal(PackagerExitCodes.SEQUENCE_REJECTED, code);
            Assert.Contains("Sequence", error);
            byte[] after = File.ReadAllBytes(PackagerTestFactory.PointerPath(outDir, PatchFormat.PRODUCT_GAME, PatchFormat.CHANNEL_INTERNAL));
            Assert.Equal(before, after);
            Assert.False(File.Exists(PackagerTestFactory.ManifestPath(outDir, PatchFormat.PRODUCT_GAME, "rel-2")));
        }

        [Fact]
        public void Run_SecondPublishWithOneChangedFile_WritesOneNewBlob()
        {
            string build   = _factory.CreateGameBuild();
            string outDir  = _factory.NewFolder("out");
            string keyFile = _factory.WriteKeyFile(_key);
            Assert.Equal(PackagerExitCodes.SUCCESS,
                PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir, 1, releaseId: "rel-1"), out _, out _));
            int blobsAfterFirst = PackagerTestFactory.CountFiles(Path.Combine(outDir, PatchFormat.PRODUCT_GAME, "blobs"));

            _factory.WriteText(build, PackagerTestFactory.DATA_FILE, "data-v2");
            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir, 2, releaseId: "rel-2"),
                out string output, out string error);

            Assert.Equal(PackagerExitCodes.SUCCESS, code);
            Assert.Equal(string.Empty, error);
            Assert.Contains("newBlobs:  1", output);
            Assert.Equal(blobsAfterFirst + 1, PackagerTestFactory.CountFiles(Path.Combine(outDir, PatchFormat.PRODUCT_GAME, "blobs")));
            Assert.Equal(2, PackagerTestFactory.OpenPointer(outDir, PatchFormat.PRODUCT_GAME, PatchFormat.CHANNEL_INTERNAL, _key).Sequence);
        }

        [Fact]
        public void Run_ExistingPointerSignedByOtherKey_IsRefused()
        {
            string build    = _factory.CreateGameBuild();
            string outDir   = _factory.NewFolder("out");
            string otherKey = _factory.WriteKeyFile(PatchSigningKey.Generate(), "other.key");
            Assert.Equal(PackagerExitCodes.SUCCESS,
                PackagerTestFactory.Publish(_factory.GameOptions(build, otherKey, outDir, 1, releaseId: "rel-1"), out _, out _));

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, _factory.WriteKeyFile(_key), outDir, 2, releaseId: "rel-2"),
                out _, out string error);

            Assert.Equal(PackagerExitCodes.OUTPUT_CONFLICT, code);
            Assert.Contains("Refusing to overwrite", error);
        }

        [Fact]
        public void Run_ExistingPointerCorrupted_IsRefused()
        {
            string build   = _factory.CreateGameBuild();
            string outDir  = _factory.NewFolder("out");
            string keyFile = _factory.WriteKeyFile(_key);
            Assert.Equal(PackagerExitCodes.SUCCESS,
                PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir, 1, releaseId: "rel-1"), out _, out _));
            File.WriteAllText(PackagerTestFactory.PointerPath(outDir, PatchFormat.PRODUCT_GAME, PatchFormat.CHANNEL_INTERNAL), "not json");

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir, 2, releaseId: "rel-2"), out _, out _);

            Assert.Equal(PackagerExitCodes.OUTPUT_CONFLICT, code);
        }

        [Fact]
        public void Run_ReleaseIdAlreadyPublished_IsRefused()
        {
            string build   = _factory.CreateGameBuild();
            string outDir  = _factory.NewFolder("out");
            string keyFile = _factory.WriteKeyFile(_key);
            Assert.Equal(PackagerExitCodes.SUCCESS,
                PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir, 1, releaseId: "rel-1"), out _, out _));

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir, 2, releaseId: "rel-1"),
                out _, out string error);

            Assert.Equal(PackagerExitCodes.OUTPUT_CONFLICT, code);
            Assert.Contains("already exists", error);
        }

        [Fact]
        public void Run_ExistingBlobWithWrongContent_FailsBeforeWritingPointer()
        {
            string build   = _factory.CreateGameBuild();
            string outDir  = _factory.NewFolder("out");
            string keyFile = _factory.WriteKeyFile(_key);
            Assert.Equal(PackagerExitCodes.SUCCESS,
                PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir, 1, releaseId: "rel-1"), out _, out _));
            ReleaseManifest manifest = PackagerTestFactory.OpenManifest(outDir, PatchFormat.PRODUCT_GAME, "rel-1", _key, out _);
            string blob = PackagerTestFactory.BlobPath(outDir, PatchFormat.PRODUCT_GAME, manifest.Files[0].Sha256);
            File.WriteAllText(blob, "tampered");

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir, 2, releaseId: "rel-2"),
                out _, out string error);

            Assert.Equal(PackagerExitCodes.OUTPUT_CONFLICT, code);
            Assert.Contains("does not match", error);
            Assert.Equal(1, PackagerTestFactory.OpenPointer(outDir, PatchFormat.PRODUCT_GAME, PatchFormat.CHANNEL_INTERNAL, _key).Sequence);
            Assert.False(File.Exists(PackagerTestFactory.ManifestPath(outDir, PatchFormat.PRODUCT_GAME, "rel-2")));
        }

        [Fact]
        public void Run_LauncherProduct_UsesOptionsInsteadOfBuildIdentity()
        {
            string build   = _factory.CreateLauncherBuild();
            string outDir  = _factory.NewFolder("out");
            string keyFile = _factory.WriteKeyFile(_key);

            int code = PackagerTestFactory.Publish(_factory.LauncherOptions(build, keyFile, outDir), out _, out string error);

            Assert.Equal(PackagerExitCodes.SUCCESS, code);
            Assert.Equal(string.Empty, error);
            ReleaseManifest manifest = PackagerTestFactory.OpenManifest(outDir, PatchFormat.PRODUCT_LAUNCHER, "launcher-1", _key, out _);
            Assert.Equal("launcher-build-1", manifest.BuildId);
            Assert.Equal("1.0.0", manifest.DisplayVersion);
            Assert.Equal(2, manifest.Files.Count);
        }

        [Fact]
        public void Run_LauncherWithoutIdentityOptions_IsUsageError()
        {
            string build   = _factory.CreateLauncherBuild();
            string keyFile = _factory.WriteKeyFile(_key);
            PublishOptions options = new PublishOptions(build, PatchFormat.PRODUCT_LAUNCHER, PatchFormat.CHANNEL_INTERNAL, 1, keyFile,
                _factory.NewFolder("out"));

            int code = PackagerTestFactory.Publish(options, out _, out string error);

            Assert.Equal(PackagerExitCodes.USAGE, code);
            Assert.Contains("--build-id", error);
        }

        [Fact]
        public void Run_LauncherStableWithDevelopmentKind_IsRefused()
        {
            string build   = _factory.CreateLauncherBuild();
            string keyFile = _factory.WriteKeyFile(_key);

            int code = PackagerTestFactory.Publish(_factory.LauncherOptions(build, keyFile, _factory.NewFolder("out"),
                channel: PatchFormat.CHANNEL_STABLE, buildKind: BuildIdentity.KIND_DEVELOPMENT), out _, out _);

            Assert.Equal(PackagerExitCodes.RELEASE_REFUSED, code);
        }

        [Fact]
        public void Run_GameWithIdentityOptions_IsUsageError()
        {
            string build   = _factory.CreateGameBuild();
            string keyFile = _factory.WriteKeyFile(_key);
            PublishOptions options = new PublishOptions(build, PatchFormat.PRODUCT_GAME, PatchFormat.CHANNEL_INTERNAL, 1, keyFile,
                _factory.NewFolder("out"))
            {
                BuildId = "override"
            };

            int code = PackagerTestFactory.Publish(options, out _, out _);

            Assert.Equal(PackagerExitCodes.USAGE, code);
        }

        [Fact]
        public void Run_NoReleaseId_UsesUtcTimestampAndSanitizedBuildPrefix()
        {
            string build   = _factory.CreateGameBuild(buildId: "ab+c:def_ghijkl");
            string outDir  = _factory.NewFolder("out");
            string keyFile = _factory.WriteKeyFile(_key);
            FixedTimeProvider clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 2, 3, 4, 5, TimeSpan.Zero));

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, outDir, releaseId: null), out _, out string error, clock);

            Assert.Equal(PackagerExitCodes.SUCCESS, code);
            Assert.Equal(string.Empty, error);
            Assert.Equal("20261002-030405-abcdef_g", PackagerTestFactory.OpenPointer(outDir, PatchFormat.PRODUCT_GAME,
                PatchFormat.CHANNEL_INTERNAL, _key).ReleaseId);
        }

        [Fact]
        public void Run_InvalidReleaseId_IsUsageError()
        {
            string build   = _factory.CreateGameBuild();
            string keyFile = _factory.WriteKeyFile(_key);

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, _factory.NewFolder("out"), releaseId: ".."),
                out _, out _);

            Assert.Equal(PackagerExitCodes.USAGE, code);
        }

        [Fact]
        public void Run_MissingKeyFile_IsKeyError()
        {
            string build = _factory.CreateGameBuild();

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, Path.Combine(_factory.Root, "none.key"),
                _factory.NewFolder("out")), out _, out _);

            Assert.Equal(PackagerExitCodes.KEY_ERROR, code);
        }

        [Fact]
        public void Run_KeyFileWithGarbage_IsKeyErrorWithoutEchoingContent()
        {
            string build   = _factory.CreateGameBuild();
            string keyFile = Path.Combine(_factory.Root, "bad.key");
            File.WriteAllText(keyFile, "secret-looking-garbage");

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, _factory.NewFolder("out")), out string output, out string error);

            Assert.Equal(PackagerExitCodes.KEY_ERROR, code);
            Assert.DoesNotContain("secret-looking-garbage", output + error);
        }

        [Fact]
        public void Run_OutputInsideBuildFolder_IsRejected()
        {
            string build   = _factory.CreateGameBuild();
            string keyFile = _factory.WriteKeyFile(_key);

            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, keyFile, Path.Combine(build, "out")), out _, out string error);

            Assert.Equal(PackagerExitCodes.USAGE, code);
            Assert.Contains("inside the build folder", error);
        }

        [Fact]
        public void Run_MissingBuildFolder_IsInvalidBuild()
        {
            string keyFile = _factory.WriteKeyFile(_key);

            int code = PackagerTestFactory.Publish(_factory.GameOptions(Path.Combine(_factory.Root, "nope"), keyFile,
                _factory.NewFolder("out")), out _, out _);

            Assert.Equal(PackagerExitCodes.INVALID_BUILD, code);
        }

        [Fact]
        public void Run_DirectorySymlinkInBuild_IsRejected()
        {
            string build   = _factory.CreateGameBuild();
            string target  = _factory.NewFolder("elsewhere");
            _factory.WriteText(target, "secret.txt", "outside");
            if (!TryCreateDirectoryLink(Path.Combine(build, "link"), target))
            {
                return;
            }

            string outDir = _factory.NewFolder("out");
            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, _factory.WriteKeyFile(_key), outDir), out _, out string error);

            Assert.Equal(PackagerExitCodes.INVALID_BUILD, code);
            Assert.Contains("link", error);
            Assert.Equal(0, PackagerTestFactory.CountFiles(outDir));
        }

        [Fact]
        public void Run_FileSymlinkInBuild_IsRejected()
        {
            string build  = _factory.CreateGameBuild();
            string target = Path.Combine(_factory.Root, "outside.txt");
            File.WriteAllText(target, "outside");
            if (!TryCreateFileLink(Path.Combine(build, "linked.txt"), target))
            {
                return;
            }

            string outDir = _factory.NewFolder("out");
            int code = PackagerTestFactory.Publish(_factory.GameOptions(build, _factory.WriteKeyFile(_key), outDir), out _, out string error);

            Assert.Equal(PackagerExitCodes.INVALID_BUILD, code);
            Assert.Contains("linked.txt", error);
            Assert.Equal(0, PackagerTestFactory.CountFiles(outDir));
        }

        // 심볼릭 링크는 권한이 없으면 만들 수 없다. 만들 수 없는 환경에서는 검증을 건너뛴다
        private static bool TryCreateDirectoryLink(string link, string target)
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool TryCreateFileLink(string link, string target)
        {
            try
            {
                File.CreateSymbolicLink(link, target);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
