// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Third-party
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Patch;
using Shinshi.Patch.Client;

namespace Shinshi.Launcher.Tests.Client
{
    /// <summary> FileSystemPatchSource로 게시 폴더를 읽어 설치, 업데이트, 거절, 복구, 보관을 확인한다 </summary>
    public sealed class PatchClientTests : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string RELEASE_1 = "2026.10.02-1";
        private const string RELEASE_2 = "2026.10.02-2";
        private const string RELEASE_3 = "2026.10.02-3";

        private const string EXE_PATH      = "Shinshi.exe";
        private const string DATA_PATH     = "Shinshi_Data/data.bin";
        private const string IDENTITY_PATH = "Shinshi_Data/StreamingAssets/build-identity.json";
        private const string NEW_PATH      = "Shinshi_Data/new.bin";

        private const string EXE_V1      = "exe-v1";
        private const string DATA_V1     = "data-v1";
        private const string DATA_V2     = "data-v2-changed";
        private const string IDENTITY_V1 = "{\"buildId\":\"one\"}";
        private const string NEW_V2      = "new-in-v2";
        private const string EXE_V3      = "exe-v3";

        private const long TINY_FREE_SPACE = 1;
        private const int  OVERSIZE_EXTRA  = 1024 * 1024;
        private const byte CORRUPT_XOR     = 0xFF;
        private const int  FIRST_REQUEST   = 1;

        private static readonly Func<bool> G_GameNotRunning = () => false;

        private readonly PatchPublishFixture _fixture = new PatchPublishFixture();

        /****************************************
        *                Methods
        ****************************************/

        public void Dispose() { _fixture.Dispose(); }

        [Fact]
        public async Task UpdateAsync_FirstInstall_InstallsFilesAndActivatesRelease()
        {
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            PatchClient client = _fixture.CreateClient();

            PatchOperationResult result = await client.UpdateAsync(null, G_GameNotRunning);

            Assert.True(result.IsSuccess, result.Error.ToString());
            Assert.Equal(RELEASE_1, result.ReleaseId);
            Assert.Equal(3, result.DownloadedFiles);
            Assert.Equal(EXE_V1, PatchPublishFixture.ReadText(_fixture.GetInstalledFile(RELEASE_1, EXE_PATH)));
            Assert.Equal(DATA_V1, PatchPublishFixture.ReadText(_fixture.GetInstalledFile(RELEASE_1, DATA_PATH)));
            Assert.Equal(IDENTITY_V1, PatchPublishFixture.ReadText(_fixture.GetInstalledFile(RELEASE_1, IDENTITY_PATH)));

            Assert.True(client.TryGetActiveInstall(out InstalledRelease? active));
            Assert.Equal(RELEASE_1, active.ReleaseId);
            Assert.Equal(Path.GetFullPath(_fixture.GetVersionPath(RELEASE_1)), active.VersionPath);
            Assert.True(File.Exists(Path.Combine(client.Layout.ManifestsPath, RELEASE_1 + ".json")));
            Assert.True(File.Exists(client.Layout.ChannelStatePath));
            Assert.True(_fixture.IsStagingEmpty());
            Assert.Equal(new[] { RELEASE_1 }, _fixture.GetVersionFolderNames());
        }

        [Fact]
        public async Task CheckAsync_NothingInstalled_ReturnsNotInstalledWithAllBytes()
        {
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            PatchClient client = _fixture.CreateClient();

            PatchCheckResult check = await client.CheckAsync();

            Assert.True(check.IsSuccess);
            Assert.Equal(EPatchCheckStatus.NotInstalled, check.Status);
            Assert.Equal(RELEASE_1, check.TargetReleaseId);
            Assert.Equal(check.TotalBytes, check.BytesToDownload);
            Assert.Equal(EXE_V1.Length + DATA_V1.Length + IDENTITY_V1.Length, check.TotalBytes);
            Assert.False(Directory.Exists(client.Layout.ProductPath));
        }

        [Fact]
        public async Task CheckAsync_InstalledAndSameSequence_ReturnsUpToDate()
        {
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            PatchClient client = _fixture.CreateClient();
            Assert.True((await client.UpdateAsync(null, G_GameNotRunning)).IsSuccess);

            PatchCheckResult check = await client.CheckAsync();
            PatchOperationResult again = await client.UpdateAsync(null, G_GameNotRunning);

            Assert.True(check.IsSuccess);
            Assert.Equal(EPatchCheckStatus.UpToDate, check.Status);
            Assert.Equal(0, check.BytesToDownload);
            Assert.Equal(RELEASE_1, check.ActiveReleaseId);
            Assert.True(again.IsSuccess);
            Assert.True(again.WasUpToDate);
            Assert.Equal(0, again.DownloadedFiles);
        }

        [Fact]
        public async Task UpdateAsync_SomeFilesChanged_DownloadsOnlyChangedFiles()
        {
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            PatchClient client = _fixture.CreateClient();
            Assert.True((await client.UpdateAsync(null, G_GameNotRunning)).IsSuccess);

            _fixture.Publish(RELEASE_2, 2, CreateRelease2());
            _fixture.Source.ResetCounts();

            PatchCheckResult check = await client.CheckAsync();
            PatchOperationResult result = await client.UpdateAsync(null, G_GameNotRunning);

            Assert.Equal(EPatchCheckStatus.UpdateAvailable, check.Status);
            Assert.Equal(DATA_V2.Length + NEW_V2.Length, check.BytesToDownload);
            Assert.True(result.IsSuccess, result.Error.ToString());
            Assert.Equal(2, result.DownloadedFiles);
            Assert.Equal(2, result.CopiedFiles);
            Assert.Equal(2, _fixture.Source.GetBlobOpenCount());
            Assert.Equal(1, _fixture.Source.GetOpenCount(PatchPublishFixture.BlobKeyOf(DATA_V2)));
            Assert.Equal(1, _fixture.Source.GetOpenCount(PatchPublishFixture.BlobKeyOf(NEW_V2)));
            Assert.Equal(0, _fixture.Source.GetOpenCount(PatchPublishFixture.BlobKeyOf(EXE_V1)));
            Assert.Equal(DATA_V2, PatchPublishFixture.ReadText(_fixture.GetInstalledFile(RELEASE_2, DATA_PATH)));
            Assert.Equal(EXE_V1, PatchPublishFixture.ReadText(_fixture.GetInstalledFile(RELEASE_2, EXE_PATH)));

            Assert.True(client.TryGetActiveInstall(out InstalledRelease? active));
            Assert.Equal(RELEASE_2, active.ReleaseId);
            Assert.Equal(new[] { RELEASE_1, RELEASE_2 }, _fixture.GetVersionFolderNames());
        }

        [Fact]
        public async Task UpdateAsync_CorruptBlobEveryAttempt_FailsAfterRetriesAndKeepsPreviousInstall()
        {
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            PatchClient client = _fixture.CreateClient();
            Assert.True((await client.UpdateAsync(null, G_GameNotRunning)).IsSuccess);

            _fixture.Publish(RELEASE_2, 2, CreateRelease2());
            string corruptKey = PatchPublishFixture.BlobKeyOf(DATA_V2);
            _fixture.Source.Tamper = (key, request, bytes) => (key == corruptKey) ? Corrupt(bytes) : bytes;

            PatchOperationResult result = await client.UpdateAsync(null, G_GameNotRunning);

            Assert.Equal(EPatchClientError.HashMismatch, result.Error);
            Assert.Equal(PatchClient.MAX_FILE_ATTEMPTS, _fixture.Source.GetOpenCount(corruptKey));
            AssertRelease1StillActive(client);
        }

        [Fact]
        public async Task UpdateAsync_CorruptBlobOnFirstAttempt_RetriesAndSucceeds()
        {
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            string corruptKey = PatchPublishFixture.BlobKeyOf(DATA_V1);
            _fixture.Source.Tamper = (key, request, bytes) => ((key == corruptKey) && (request == FIRST_REQUEST)) ? Corrupt(bytes) : bytes;
            PatchClient client = _fixture.CreateClient();

            PatchOperationResult result = await client.UpdateAsync(null, G_GameNotRunning);

            Assert.True(result.IsSuccess, result.Error.ToString());
            Assert.Equal(2, _fixture.Source.GetOpenCount(corruptKey));
            Assert.Equal(DATA_V1, PatchPublishFixture.ReadText(_fixture.GetInstalledFile(RELEASE_1, DATA_PATH)));
        }

        [Fact]
        public async Task UpdateAsync_BlobLargerThanManifestSize_StopsReadingAtSizePlusOne()
        {
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            string oversizedKey = PatchPublishFixture.BlobKeyOf(DATA_V1);
            _fixture.Source.Tamper = (key, request, bytes) => (key == oversizedKey) ? Append(bytes, OVERSIZE_EXTRA) : bytes;
            PatchClient client = _fixture.CreateClient();

            PatchOperationResult result = await client.UpdateAsync(null, G_GameNotRunning);

            Assert.Equal(EPatchClientError.HashMismatch, result.Error);
            long maxExpected = PatchClient.MAX_FILE_ATTEMPTS * (DATA_V1.Length + 1L);
            Assert.True(_fixture.Source.GetBytesRead(oversizedKey) <= maxExpected);
            Assert.False(client.TryGetActiveInstall(out _));
            Assert.Empty(_fixture.GetVersionFolderNames());
            Assert.True(_fixture.IsStagingEmpty());
        }

        [Fact]
        public async Task CheckAsync_SequenceLowerThanSeen_RejectsAsRollback()
        {
            string release1Sha = _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            _fixture.Publish(RELEASE_2, 5, CreateRelease2());
            PatchClient client = _fixture.CreateClient();
            Assert.True((await client.UpdateAsync(null, G_GameNotRunning)).IsSuccess);

            _fixture.WritePointer(RELEASE_1, 4, release1Sha);

            PatchCheckResult check = await client.CheckAsync();
            PatchOperationResult update = await client.UpdateAsync(null, G_GameNotRunning);

            Assert.Equal(EPatchCheckStatus.Failed, check.Status);
            Assert.Equal(EPatchClientError.SequenceRollback, check.Error);
            Assert.Equal(EPatchError.SequenceRollback, check.PatchError);
            Assert.Equal(EPatchClientError.SequenceRollback, update.Error);
            Assert.True(client.TryGetActiveInstall(out InstalledRelease? active));
            Assert.Equal(RELEASE_2, active.ReleaseId);
        }

        [Fact]
        public async Task CheckAsync_PointerManifestShaMismatch_Rejects()
        {
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            _fixture.WritePointer(RELEASE_1, 1, new string('0', PatchHash.SHA256_HEX_LENGTH));
            PatchClient client = _fixture.CreateClient();

            PatchCheckResult check = await client.CheckAsync();
            PatchOperationResult update = await client.UpdateAsync(null, G_GameNotRunning);

            Assert.Equal(EPatchClientError.ManifestMismatch, check.Error);
            Assert.Equal(EPatchError.ManifestHashMismatch, check.PatchError);
            Assert.Equal(EPatchClientError.ManifestMismatch, update.Error);
            Assert.Equal(0, _fixture.Source.GetBlobOpenCount());
            Assert.False(client.TryGetActiveInstall(out _));
        }

        [Fact]
        public async Task CheckAsync_PointerSignedByUntrustedKey_Rejects()
        {
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            PatchClientOptions options = new PatchClientOptions(_fixture.InstallRoot, PatchPublishFixture.PRODUCT, PatchPublishFixture.CHANNEL);
            PatchClient client = new PatchClient(options, _fixture.Source, new TrustedKeySet(PatchSigningKey.Generate().PublicKey));

            PatchCheckResult check = await client.CheckAsync();

            Assert.Equal(EPatchClientError.DocumentRejected, check.Error);
            Assert.Equal(EPatchError.UntrustedKey, check.PatchError);
        }

        [Fact]
        public async Task CheckAsync_ManifestPathEscapesRoot_RejectsWholeManifest()
        {
            ReleaseManifest manifest = new ReleaseManifest(PatchPublishFixture.PRODUCT, RELEASE_1, PatchPublishFixture.CHANNEL,
                PatchPublishFixture.BUILD_ID, BuildIdentity.KIND_RELEASE, PatchPublishFixture.DISPLAY_VERSION, PatchPublishFixture.MIN_LAUNCHER,
                PatchPublishFixture.CREATED_AT, new[] { new ManifestFile(EXE_PATH, EXE_V1.Length, PatchHash.Compute(Encoding.UTF8.GetBytes(EXE_V1))) });

            // 게시 도구는 이런 매니페스트를 봉하지 않으므로 payload를 직접 고쳐 서명한다
            JObject payload = JObject.Parse(JsonConvert.SerializeObject(manifest, ContractJson.CreateSettings()));
            payload["files"]![0]!["path"] = "../../escape.txt";
            byte[] payloadBytes = new UTF8Encoding(false).GetBytes(payload.ToString(Formatting.None));
            _fixture.WriteManifestEnvelope(RELEASE_1, PatchEnvelope.Seal(PatchFormat.KIND_MANIFEST, payloadBytes, _fixture.Key));
            _fixture.WriteBlob(PatchHash.Compute(Encoding.UTF8.GetBytes(EXE_V1)), Encoding.UTF8.GetBytes(EXE_V1));
            _fixture.WritePointer(RELEASE_1, 1, PatchHash.Compute(payloadBytes));
            PatchClient client = _fixture.CreateClient();

            PatchCheckResult check = await client.CheckAsync();
            PatchOperationResult update = await client.UpdateAsync(null, G_GameNotRunning);

            Assert.Equal(EPatchClientError.DocumentRejected, check.Error);
            Assert.Equal(EPatchError.InvalidPath, check.PatchError);
            Assert.Equal(EPatchClientError.DocumentRejected, update.Error);
            Assert.Equal(0, _fixture.Source.GetBlobOpenCount());
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_fixture.InstallRoot)!, "escape.txt", SearchOption.AllDirectories));
        }

        [Fact]
        public async Task VerifyAndRepairAsync_CorruptAndExtraFiles_RedownloadsOnlyCorruptAndDeletesExtras()
        {
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            PatchClient client = _fixture.CreateClient();
            Assert.True((await client.UpdateAsync(null, G_GameNotRunning)).IsSuccess);

            File.WriteAllText(_fixture.GetInstalledFile(RELEASE_1, DATA_PATH), "tampered");
            string extraFile = _fixture.GetInstalledFile(RELEASE_1, "Shinshi_Data/cheat.dll");
            string extraFolder = Path.Combine(_fixture.GetVersionPath(RELEASE_1), "Mods");
            File.WriteAllText(extraFile, "extra");
            Directory.CreateDirectory(extraFolder);
            File.WriteAllText(Path.Combine(extraFolder, "mod.txt"), "extra");
            _fixture.Source.ResetCounts();

            PatchOperationResult result = await client.VerifyAndRepairAsync(null, G_GameNotRunning);

            Assert.True(result.IsSuccess, result.Error.ToString());
            Assert.Equal(1, result.RepairedFiles);
            Assert.Equal(2, result.RemovedFiles);
            Assert.Equal(1, _fixture.Source.GetBlobOpenCount());
            Assert.Equal(1, _fixture.Source.GetOpenCount(PatchPublishFixture.BlobKeyOf(DATA_V1)));
            Assert.Equal(DATA_V1, PatchPublishFixture.ReadText(_fixture.GetInstalledFile(RELEASE_1, DATA_PATH)));
            Assert.False(File.Exists(extraFile));
            Assert.False(Directory.Exists(extraFolder));
            Assert.True(File.Exists(_fixture.GetInstalledFile(RELEASE_1, IDENTITY_PATH)));
        }

        [Fact]
        public async Task VerifyAndRepairAsync_NothingInstalled_ReturnsNotInstalled()
        {
            PatchClient client = _fixture.CreateClient();

            PatchOperationResult result = await client.VerifyAndRepairAsync(null, G_GameNotRunning);

            Assert.Equal(EPatchClientError.NotInstalled, result.Error);
        }

        [Fact]
        public async Task UpdateAsync_ThirdRelease_KeepsOnlyActiveAndPreviousVersion()
        {
            PatchClient client = _fixture.CreateClient();
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            Assert.True((await client.UpdateAsync(null, G_GameNotRunning)).IsSuccess);
            _fixture.Publish(RELEASE_2, 2, CreateRelease2());
            Assert.True((await client.UpdateAsync(null, G_GameNotRunning)).IsSuccess);
            _fixture.Publish(RELEASE_3, 3, CreateRelease3());

            PatchOperationResult result = await client.UpdateAsync(null, G_GameNotRunning);

            Assert.True(result.IsSuccess, result.Error.ToString());
            Assert.Equal(1, result.RemovedVersions);
            Assert.False(result.IsRetentionCleanupFailed);
            Assert.Equal(new[] { RELEASE_2, RELEASE_3 }, _fixture.GetVersionFolderNames());
            Assert.False(File.Exists(Path.Combine(client.Layout.ManifestsPath, RELEASE_1 + ".json")));
            Assert.True(File.Exists(Path.Combine(client.Layout.ManifestsPath, RELEASE_2 + ".json")));
            Assert.Equal(EXE_V3, PatchPublishFixture.ReadText(_fixture.GetInstalledFile(RELEASE_3, EXE_PATH)));
        }

        [Fact]
        public async Task UpdateAsync_ChannelPointsBackToKeptPreviousVersion_VerifiesWithoutDownloading()
        {
            PatchClient client = _fixture.CreateClient();
            string release1Sha = _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            Assert.True((await client.UpdateAsync(null, G_GameNotRunning)).IsSuccess);
            _fixture.Publish(RELEASE_2, 2, CreateRelease2());
            Assert.True((await client.UpdateAsync(null, G_GameNotRunning)).IsSuccess);

            _fixture.WritePointer(RELEASE_1, 3, release1Sha);
            _fixture.Source.ResetCounts();

            PatchOperationResult result = await client.UpdateAsync(null, G_GameNotRunning);

            Assert.True(result.IsSuccess, result.Error.ToString());
            Assert.Equal(0, _fixture.Source.GetBlobOpenCount());
            Assert.True(client.TryGetActiveInstall(out InstalledRelease? active));
            Assert.Equal(RELEASE_1, active.ReleaseId);
            Assert.Equal(new[] { RELEASE_1, RELEASE_2 }, _fixture.GetVersionFolderNames());
        }

        [Fact]
        public async Task UpdateAsync_GameRunning_RefusesWithoutTouchingSourceOrInstall()
        {
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            PatchClient client = _fixture.CreateClient();

            PatchOperationResult update = await client.UpdateAsync(null, () => true);
            PatchOperationResult repair = await client.VerifyAndRepairAsync(null, () => true);

            Assert.Equal(EPatchClientError.GameRunning, update.Error);
            Assert.Equal(EPatchClientError.GameRunning, repair.Error);
            Assert.Equal(0, _fixture.Source.GetTotalOpenCount());
            Assert.False(Directory.Exists(client.Layout.ProductPath));
        }

        [Fact]
        public async Task UpdateAsync_CancelledMidDownload_KeepsPreviousInstallAndLeavesNoPartialFolder()
        {
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            PatchClient client = _fixture.CreateClient();
            Assert.True((await client.UpdateAsync(null, G_GameNotRunning)).IsSuccess);

            _fixture.Publish(RELEASE_2, 2, CreateRelease2());
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            _fixture.Source.OnOpen = key =>
            {
                if (key.Contains("/blobs/", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                }
            };

            PatchOperationResult result = await client.UpdateAsync(null, G_GameNotRunning, cancellation.Token);

            Assert.Equal(EPatchClientError.Cancelled, result.Error);
            AssertRelease1StillActive(client);
        }

        [Fact]
        public async Task UpdateAsync_NotEnoughDiskSpace_FailsBeforeDownloading()
        {
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            PatchClient client = _fixture.CreateClient(_ => TINY_FREE_SPACE);

            PatchOperationResult result = await client.UpdateAsync(null, G_GameNotRunning);

            Assert.Equal(EPatchClientError.DiskSpace, result.Error);
            Assert.Equal(0, _fixture.Source.GetBlobOpenCount());
        }

        [Fact]
        public async Task UpdateAsync_LauncherOlderThanManifestMinimum_RefusesUpdate()
        {
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            PatchClient client = _fixture.CreateClient(null, new Version(0, 9, 0));

            PatchCheckResult check = await client.CheckAsync();
            PatchOperationResult result = await client.UpdateAsync(null, G_GameNotRunning);

            Assert.Equal(EPatchClientError.LauncherOutdated, check.Error);
            Assert.Equal(PatchPublishFixture.MIN_LAUNCHER, check.MinLauncherVersion);
            Assert.Equal(EPatchClientError.LauncherOutdated, result.Error);
            Assert.Equal(0, _fixture.Source.GetBlobOpenCount());
        }

        [Fact]
        public async Task UpdateAsync_WithProgress_ReportsCompletedStageLast()
        {
            _fixture.Publish(RELEASE_1, 1, CreateRelease1());
            PatchClient client = _fixture.CreateClient();
            List<PatchProgress> reports = new List<PatchProgress>();

            PatchOperationResult result = await client.UpdateAsync(new SynchronousProgress(reports.Add), G_GameNotRunning);

            Assert.True(result.IsSuccess);
            Assert.Contains(reports, report => (report.Stage == EPatchStage.Downloading) && (report.CompletedBytes == report.TotalBytes)
                && (report.TotalBytes > 0));
            Assert.Equal(EPatchStage.Completed, reports[reports.Count - 1].Stage);
        }

        private void AssertRelease1StillActive(PatchClient client)
        {
            Assert.True(client.TryGetActiveInstall(out InstalledRelease? active));
            Assert.Equal(RELEASE_1, active.ReleaseId);
            Assert.Equal(EXE_V1, PatchPublishFixture.ReadText(_fixture.GetInstalledFile(RELEASE_1, EXE_PATH)));
            Assert.Equal(DATA_V1, PatchPublishFixture.ReadText(_fixture.GetInstalledFile(RELEASE_1, DATA_PATH)));
            Assert.Equal(new[] { RELEASE_1 }, _fixture.GetVersionFolderNames());
            Assert.True(_fixture.IsStagingEmpty());
        }

        private static Dictionary<string, string> CreateRelease1()
        {
            return new Dictionary<string, string>
            {
                { EXE_PATH, EXE_V1 },
                { DATA_PATH, DATA_V1 },
                { IDENTITY_PATH, IDENTITY_V1 }
            };
        }

        private static Dictionary<string, string> CreateRelease2()
        {
            return new Dictionary<string, string>
            {
                { EXE_PATH, EXE_V1 },
                { DATA_PATH, DATA_V2 },
                { IDENTITY_PATH, IDENTITY_V1 },
                { NEW_PATH, NEW_V2 }
            };
        }

        private static Dictionary<string, string> CreateRelease3()
        {
            return new Dictionary<string, string>
            {
                { EXE_PATH, EXE_V3 },
                { DATA_PATH, DATA_V2 },
                { IDENTITY_PATH, IDENTITY_V1 }
            };
        }

        private static byte[] Corrupt(byte[] bytes)
        {
            byte[] copy = (byte[])bytes.Clone();
            copy[0] ^= CORRUPT_XOR;
            return copy;
        }

        private static byte[] Append(byte[] bytes, int extra)
        {
            byte[] longer = new byte[bytes.Length + extra];
            Buffer.BlockCopy(bytes, 0, longer, 0, bytes.Length);
            return longer;
        }

        // Progress<T>는 동기화 문맥으로 보내 순서가 보장되지 않는다. 테스트는 보고 순서를 그대로 받는다
        private sealed class SynchronousProgress : IProgress<PatchProgress>
        {
            private readonly Action<PatchProgress> _onReport;

            public SynchronousProgress(Action<PatchProgress> onReport) { _onReport = onReport; }

            public void Report(PatchProgress value) { _onReport(value); }
        }
    }
}
