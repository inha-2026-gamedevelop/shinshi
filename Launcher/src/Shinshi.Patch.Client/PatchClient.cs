// System
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Patch.Client
{
    /// <summary> 채널 확인, 설치와 업데이트, 검증과 복구를 하는 엔진. 예상한 실패는 예외 대신 결과 코드로 돌려준다 </summary>
    public sealed class PatchClient
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int    MAX_FILE_ATTEMPTS = 3;
        public const double DISK_SPACE_MARGIN = 1.1;

        private const int    COPY_BUFFER_BYTES = 81920;
        private const string BLOB_TEMP_PREFIX  = "blob-";
        private const string ASSEMBLY_PREFIX   = "build-";
        private const int    LOCK_BUFFER_BYTES = 1;

        private readonly PatchClientOptions _options;
        private readonly IPatchSource       _source;
        private readonly TrustedKeySet      _keys;
        private readonly PatchInstallLayout _layout;

        /****************************************
        *              Properties
        ****************************************/

        public PatchClientOptions Options => _options;
        public PatchInstallLayout Layout  => _layout;

        /****************************************
        *                Methods
        ****************************************/

        public PatchClient(PatchClientOptions options, IPatchSource source, TrustedKeySet keys)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(keys);

            _options = options;
            _source  = source;
            _keys    = keys;
            _layout  = new PatchInstallLayout(options.InstallRoot, options.Product);
        }

        /// <summary> current.json이 가리키는 활성 설치를 읽는다. 버전 폴더나 서명된 매니페스트 보관본이 없으면 false </summary>
        public bool TryGetActiveInstall([NotNullWhen(true)] out InstalledRelease? install)
        {
            install = null;
            try
            {
                return TryReadActiveInstall(out install);
            }
            catch (IOException)
            {
                // 읽는 도중 파일이 사라지거나 잠긴 경우, 활성 설치가 없는 것으로 보고 전체를 다시 받게 한다
                install = null;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                install = null;
                return false;
            }
        }

        /// <summary> 채널 포인터와 매니페스트를 받아 검증하고, 업데이트가 필요한지와 받을 바이트 수를 계산한다. 설치 폴더는 바꾸지 않는다 </summary>
        public async Task<PatchCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                (RemoteRelease? remote, EPatchClientError error, EPatchError patchError) =
                    await FetchRemoteAsync(cancellationToken).ConfigureAwait(false);
                if (remote == null)
                {
                    return PatchCheckResult.Fail(error, patchError);
                }

                TryGetActiveInstall(out InstalledRelease? active);
                if ((active != null) && IsReleaseConflict(active, remote))
                {
                    return PatchCheckResult.Fail(EPatchClientError.ReleaseConflict);
                }

                EPatchCheckStatus status = EPatchCheckStatus.UpdateAvailable;
                if (active == null)
                {
                    status = EPatchCheckStatus.NotInstalled;
                }
                else if (IsSameInstall(active, remote))
                {
                    status = EPatchCheckStatus.UpToDate;
                }

                ReleaseManifest manifest = remote.Manifest;
                return new PatchCheckResult
                {
                    Status             = status,
                    Error              = IsLauncherSufficient(manifest) ? EPatchClientError.None : EPatchClientError.LauncherOutdated,
                    ActiveReleaseId    = active?.ReleaseId,
                    TargetReleaseId    = remote.ReleaseId,
                    DisplayVersion     = manifest.DisplayVersion,
                    MinLauncherVersion = manifest.MinLauncherVersion,
                    Sequence           = remote.Pointer.Sequence,
                    BytesToDownload    = (status == EPatchCheckStatus.UpToDate) ? 0 : ComputeBytesToDownload(manifest, active),
                    TotalBytes         = manifest.GetTotalSize()
                };
            }
            catch (Exception exception) when (TryMapException(exception, cancellationToken, out EPatchClientError mapped))
            {
                return PatchCheckResult.Fail(mapped);
            }
        }

        /// <summary> 채널이 가리키는 릴리스를 설치하고 활성으로 바꾼다. 게임이 실행 중이면 시작하지 않는다 </summary>
        public async Task<PatchOperationResult> UpdateAsync(IProgress<PatchProgress>? progress, Func<bool> isGameRunning,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(isGameRunning);

            if (isGameRunning())
            {
                return PatchOperationResult.Fail(EPatchClientError.GameRunning);
            }

            FileStream? lockHandle = null;
            try
            {
                lockHandle = TryAcquireLock();
                if (lockHandle == null)
                {
                    return PatchOperationResult.Fail(EPatchClientError.Busy);
                }

                return await UpdateLockedAsync(progress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (TryMapException(exception, cancellationToken, out EPatchClientError mapped))
            {
                return PatchOperationResult.Fail(mapped);
            }
            finally
            {
                if (lockHandle != null)
                {
                    // 조립 중이던 폴더와 임시 파일을 지운다. 실패해도 다음 작업 시작 때 다시 지운다
                    TryCleanStaging();
                    lockHandle.Dispose();
                }
            }
        }

        /// <summary> 활성 버전 전체를 보관한 매니페스트와 비교해 다른 파일만 다시 받고, 매니페스트에 없는 파일은 지운다 </summary>
        public async Task<PatchOperationResult> VerifyAndRepairAsync(IProgress<PatchProgress>? progress, Func<bool> isGameRunning,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(isGameRunning);

            if (isGameRunning())
            {
                return PatchOperationResult.Fail(EPatchClientError.GameRunning);
            }

            FileStream? lockHandle = null;
            try
            {
                lockHandle = TryAcquireLock();
                if (lockHandle == null)
                {
                    return PatchOperationResult.Fail(EPatchClientError.Busy);
                }

                return await RepairLockedAsync(progress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (TryMapException(exception, cancellationToken, out EPatchClientError mapped))
            {
                return PatchOperationResult.Fail(mapped);
            }
            finally
            {
                if (lockHandle != null)
                {
                    TryCleanStaging();
                    lockHandle.Dispose();
                }
            }
        }

        private async Task<PatchOperationResult> UpdateLockedAsync(IProgress<PatchProgress>? progress, CancellationToken cancellationToken)
        {
            Report(progress, EPatchStage.Checking);
            (RemoteRelease? remote, EPatchClientError error, EPatchError patchError) =
                await FetchRemoteAsync(cancellationToken).ConfigureAwait(false);
            if (remote == null)
            {
                return PatchOperationResult.Fail(error, patchError);
            }

            if (!IsLauncherSufficient(remote.Manifest))
            {
                return PatchOperationResult.Fail(EPatchClientError.LauncherOutdated);
            }

            TryGetActiveInstall(out InstalledRelease? active);
            if ((active != null) && IsSameInstall(active, remote))
            {
                PatchStateStore.RaiseSequence(_layout, _options.Product, _options.Channel, remote.Pointer.Sequence);
                Report(progress, EPatchStage.Completed);
                return new PatchOperationResult { ReleaseId = remote.ReleaseId, WasUpToDate = true };
            }

            if ((active != null) && IsReleaseConflict(active, remote))
            {
                return PatchOperationResult.Fail(EPatchClientError.ReleaseConflict);
            }

            if (!_layout.TryGetVersionPath(remote.ReleaseId, out string? versionPath)
                || !_layout.TryGetManifestCopyPath(remote.ReleaseId, out string? manifestCopyPath))
            {
                return PatchOperationResult.Fail(EPatchClientError.InvalidPath);
            }

            Report(progress, EPatchStage.Preparing);
            if (!HasDiskSpace(remote.Manifest))
            {
                return PatchOperationResult.Fail(EPatchClientError.DiskSpace);
            }

            TryCleanStaging();

            // 보관해 둔 직전 버전처럼 완성된 적 있는 폴더가 있으면 다시 받지 않고 검사해서 고친다.
            // 활성 폴더가 아니므로 제자리에서 고쳐도 지금 실행 가능한 설치는 건드리지 않는다
            string? assemblyPath = null;
            string  targetPath   = versionPath;
            if (!Directory.Exists(versionPath))
            {
                assemblyPath = NewStagingPath(ASSEMBLY_PREFIX);
                Directory.CreateDirectory(assemblyPath);
                targetPath = assemblyPath;
            }

            MaterializeStats stats = await MaterializeAsync(targetPath, remote.Manifest, active, progress, EPatchStage.Downloading,
                cancellationToken).ConfigureAwait(false);
            if (stats.Error != EPatchClientError.None)
            {
                return PatchOperationResult.Fail(stats.Error);
            }

            if (!await VerifyFolderAsync(targetPath, remote.Manifest, progress, cancellationToken).ConfigureAwait(false))
            {
                return PatchOperationResult.Fail(EPatchClientError.HashMismatch);
            }

            Report(progress, EPatchStage.Finalizing);
            if (assemblyPath != null)
            {
                Directory.CreateDirectory(_layout.VersionsPath);
                Directory.Move(assemblyPath, versionPath);
            }

            // 매니페스트 보관본을 current.json보다 먼저 쓴다. 반대 순서로 그 사이에 꺼지면 활성 설치를 읽지 못해
            // 다음 실행이 전체를 다시 받는다. 아직 활성이 아닌 릴리스의 보관본은 있어도 해가 없다
            PatchFileSystem.WriteAllBytesAtomic(manifestCopyPath, remote.ManifestEnvelope);
            PatchStateStore.WriteCurrentReleaseId(_layout, remote.ReleaseId);
            PatchStateStore.RaiseSequence(_layout, _options.Product, _options.Channel, remote.Pointer.Sequence);

            int removedVersions = RemoveOldVersions(remote.ReleaseId, active?.ReleaseId, out bool isCleanupComplete);
            Report(progress, EPatchStage.Completed);
            return new PatchOperationResult
            {
                ReleaseId                = remote.ReleaseId,
                DownloadedFiles          = stats.DownloadedFiles,
                DownloadedBytes          = stats.DownloadedBytes,
                CopiedFiles              = stats.CopiedFiles,
                RemovedFiles             = stats.RemovedFiles,
                RemovedVersions          = removedVersions,
                IsRetentionCleanupFailed = !isCleanupComplete
            };
        }

        private async Task<PatchOperationResult> RepairLockedAsync(IProgress<PatchProgress>? progress, CancellationToken cancellationToken)
        {
            Report(progress, EPatchStage.Checking);
            if (!TryGetActiveInstall(out InstalledRelease? active))
            {
                return PatchOperationResult.Fail(EPatchClientError.NotInstalled);
            }

            TryCleanStaging();
            MaterializeStats stats = await MaterializeAsync(active.VersionPath, active.Manifest, null, progress, EPatchStage.Verifying,
                cancellationToken).ConfigureAwait(false);
            if (stats.Error != EPatchClientError.None)
            {
                return PatchOperationResult.Fail(stats.Error);
            }

            Report(progress, EPatchStage.Completed);
            return new PatchOperationResult
            {
                ReleaseId       = active.ReleaseId,
                DownloadedFiles = stats.DownloadedFiles,
                DownloadedBytes = stats.DownloadedBytes,
                CopiedFiles     = stats.CopiedFiles,
                RepairedFiles   = stats.DownloadedFiles + stats.CopiedFiles,
                RemovedFiles    = stats.RemovedFiles
            };
        }

        private bool TryReadActiveInstall([NotNullWhen(true)] out InstalledRelease? install)
        {
            install = null;
            if (!PatchStateStore.TryReadCurrentReleaseId(_layout, out string? releaseId))
            {
                return false;
            }

            if (!_layout.TryGetVersionPath(releaseId, out string? versionPath) || !Directory.Exists(versionPath))
            {
                return false;
            }

            if (!_layout.TryGetManifestCopyPath(releaseId, out string? manifestPath) || !File.Exists(manifestPath))
            {
                return false;
            }

            byte[]? envelope = PatchFileSystem.ReadAllBytesLimited(manifestPath, PatchFormat.MAX_MANIFEST_ENVELOPE_BYTES);
            if (envelope == null)
            {
                return false;
            }

            // 보관본도 서명을 다시 확인한다. 로컬에서 고친 매니페스트로 복구 기준을 바꾸지 못하게 한다
            if (!PatchDocuments.TryOpenManifest(envelope, _keys, out ReleaseManifest? manifest, out string? manifestSha256, out _))
            {
                return false;
            }

            if (!string.Equals(manifest.ReleaseId, releaseId, StringComparison.Ordinal)
                || !string.Equals(manifest.Product, _options.Product, StringComparison.Ordinal))
            {
                return false;
            }

            install = new InstalledRelease(releaseId, versionPath, manifest, manifestSha256);
            return true;
        }

        private async Task<(RemoteRelease? Release, EPatchClientError Error, EPatchError PatchError)> FetchRemoteAsync(
            CancellationToken cancellationToken)
        {
            string product = _options.Product;
            string channel = _options.Channel;

            byte[]? pointerEnvelope = await ReadDocumentAsync(PatchKeys.Channel(product, channel), PatchFormat.MAX_CHANNEL_ENVELOPE_BYTES,
                cancellationToken).ConfigureAwait(false);
            if (pointerEnvelope == null)
            {
                return (null, EPatchClientError.DocumentRejected, EPatchError.TooLarge);
            }

            if (!PatchDocuments.TryOpenChannelPointer(pointerEnvelope, _keys, out ChannelPointer? pointer, out EPatchError pointerError))
            {
                return (null, EPatchClientError.DocumentRejected, pointerError);
            }

            // 서명이 맞아도 다른 제품, 다른 채널의 포인터를 이 키 자리에 끼워 넣은 것일 수 있다
            if (!string.Equals(pointer.Product, product, StringComparison.Ordinal))
            {
                return (null, EPatchClientError.DocumentRejected, EPatchError.ProductMismatch);
            }

            if (!string.Equals(pointer.Channel, channel, StringComparison.Ordinal))
            {
                return (null, EPatchClientError.DocumentRejected, EPatchError.ChannelMismatch);
            }

            long lastSeenSequence = PatchStateStore.ReadSequence(_layout, product, channel);
            EPatchError sequenceError = PatchValidation.CheckSequence(lastSeenSequence, pointer.Sequence);
            if (sequenceError != EPatchError.None)
            {
                return (null, EPatchClientError.SequenceRollback, sequenceError);
            }

            byte[]? manifestEnvelope = await ReadDocumentAsync(PatchKeys.Manifest(product, pointer.ReleaseId),
                PatchFormat.MAX_MANIFEST_ENVELOPE_BYTES, cancellationToken).ConfigureAwait(false);
            if (manifestEnvelope == null)
            {
                return (null, EPatchClientError.DocumentRejected, EPatchError.TooLarge);
            }

            if (!PatchDocuments.TryOpenManifest(manifestEnvelope, _keys, out ReleaseManifest? manifest, out string? manifestSha256,
                out EPatchError manifestError))
            {
                return (null, EPatchClientError.DocumentRejected, manifestError);
            }

            EPatchError pairError = PatchValidation.CheckPointerMatchesManifest(pointer, manifest, manifestSha256);
            if (pairError != EPatchError.None)
            {
                return (null, EPatchClientError.ManifestMismatch, pairError);
            }

            return (new RemoteRelease(pointer, manifest, manifestEnvelope, manifestSha256), EPatchClientError.None, EPatchError.None);
        }

        // 폴더를 매니페스트와 똑같이 맞춘다. 맞는 파일은 두고, 같은 해시 파일이 있으면 복사하고, 없으면 받는다
        private async Task<MaterializeStats> MaterializeAsync(string folder, ReleaseManifest manifest, InstalledRelease? copySource,
            IProgress<PatchProgress>? progress, EPatchStage stage, CancellationToken cancellationToken)
        {
            MaterializeStats stats = new MaterializeStats();
            PatchProgressCounter counter = new PatchProgressCounter(progress, stage, manifest.GetTotalSize());
            Directory.CreateDirectory(_layout.StagingPath);

            // 남는 파일을 먼저 지운다. 매니페스트의 폴더 자리에 같은 이름의 파일이 있으면 폴더를 만들 수 없다
            stats.RemovedFiles = RemoveExtraFiles(folder, manifest);

            Dictionary<string, string> copySources = BuildCopySources(copySource);
            foreach (ManifestFile file in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!PatchPaths.TryResolveUnderRoot(folder, file.Path, out string? destination))
                {
                    stats.Error = EPatchClientError.InvalidPath;
                    return stats;
                }

                if (await IsFileValidAsync(destination, file, cancellationToken).ConfigureAwait(false))
                {
                    counter.Add(file.Size);
                    copySources.TryAdd(file.Sha256, destination);
                    continue;
                }

                PrepareDestination(destination);
                if (await TryCopyAsync(copySources, file, destination, cancellationToken).ConfigureAwait(false))
                {
                    ++stats.CopiedFiles;
                    counter.Add(file.Size);
                    copySources.TryAdd(file.Sha256, destination);
                    continue;
                }

                EPatchClientError downloadError = await DownloadAsync(file, destination, counter, cancellationToken).ConfigureAwait(false);
                if (downloadError != EPatchClientError.None)
                {
                    stats.Error = downloadError;
                    return stats;
                }

                ++stats.DownloadedFiles;
                stats.DownloadedBytes += file.Size;
                copySources.TryAdd(file.Sha256, destination);
            }

            counter.Flush();
            return stats;
        }

        private async Task<bool> VerifyFolderAsync(string folder, ReleaseManifest manifest, IProgress<PatchProgress>? progress,
            CancellationToken cancellationToken)
        {
            PatchProgressCounter counter = new PatchProgressCounter(progress, EPatchStage.Verifying, manifest.GetTotalSize());
            foreach (ManifestFile file in manifest.Files)
            {
                if (!PatchPaths.TryResolveUnderRoot(folder, file.Path, out string? path))
                {
                    return false;
                }

                if (!await IsFileValidAsync(path, file, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                counter.Add(file.Size);
            }

            counter.Flush();
            return true;
        }

        private async Task<EPatchClientError> DownloadAsync(ManifestFile file, string destination, PatchProgressCounter counter,
            CancellationToken cancellationToken)
        {
            string key = PatchKeys.Blob(_options.Product, file.Sha256);
            EPatchClientError lastError = EPatchClientError.HashMismatch;
            for (int attempt = 0; attempt < MAX_FILE_ATTEMPTS; ++attempt)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string tempPath = NewStagingPath(BLOB_TEMP_PREFIX);
                long countedBefore = counter.CompletedBytes;
                try
                {
                    long received = await DownloadToFileAsync(key, file.Size, tempPath, counter, cancellationToken).ConfigureAwait(false);

                    // 받은 그대로가 아니라 디스크에 내려간 파일을 해시해, 옮길 바이트가 확인한 바이트와 같게 한다
                    if ((received == file.Size)
                        && string.Equals(await PatchHash.ComputeFileAsync(tempPath, cancellationToken).ConfigureAwait(false), file.Sha256,
                            StringComparison.Ordinal))
                    {
                        File.Move(tempPath, destination, true);
                        return EPatchClientError.None;
                    }

                    lastError = EPatchClientError.HashMismatch;
                }
                catch (Exception exception) when (IsTransientSourceFailure(exception, cancellationToken))
                {
                    lastError = EPatchClientError.Network;
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }

                counter.Subtract(counter.CompletedBytes - countedBefore);
            }

            return lastError;
        }

        private async Task<long> DownloadToFileAsync(string key, long expectedSize, string tempPath, PatchProgressCounter counter,
            CancellationToken cancellationToken)
        {
            // 기대 크기보다 1바이트까지만 읽는다. 넘치면 그 자리에서 끊어 거대한 응답이 디스크를 채우지 못하게 한다
            long limit = expectedSize + 1;
            long received = 0;
            byte[] buffer = new byte[COPY_BUFFER_BYTES];

            await using Stream source = await OpenSourceAsync(key, cancellationToken).ConfigureAwait(false);
            await using FileStream target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, COPY_BUFFER_BYTES,
                FileOptions.Asynchronous);
            while (received < limit)
            {
                int request = (int)Math.Min(buffer.Length, limit - received);
                int read = await ReadSourceAsync(source, buffer, request, key, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                counter.Add(read);
            }

            return received;
        }

        // 크기가 상한을 넘으면 null, 상한까지만 읽고 멈춘다
        private async Task<byte[]?> ReadDocumentAsync(string key, int maxBytes, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[COPY_BUFFER_BYTES];

            await using Stream source = await OpenSourceAsync(key, cancellationToken).ConfigureAwait(false);
            using MemoryStream document = new MemoryStream();
            while (true)
            {
                int read = await ReadSourceAsync(source, buffer, buffer.Length, key, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if ((document.Length + read) > maxBytes)
                {
                    return null;
                }

                document.Write(buffer, 0, read);
            }

            return document.ToArray();
        }

        // 원본 쪽 IOException을 원본 실패로 바꿔, 로컬 디스크 오류(IoFailure)와 네트워크 오류(재시도 대상)를 구분한다
        private async Task<Stream> OpenSourceAsync(string key, CancellationToken cancellationToken)
        {
            try
            {
                return await _source.OpenAsync(key, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw new PatchSourceException(key, false, "Patch source could not open the key.", exception);
            }
        }

        private static async Task<int> ReadSourceAsync(Stream source, byte[] buffer, int count, string key,
            CancellationToken cancellationToken)
        {
            try
            {
                return await source.ReadAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw new PatchSourceException(key, false, "Patch source read failed.", exception);
            }
        }

        private async Task<bool> TryCopyAsync(Dictionary<string, string> copySources, ManifestFile file, string destination,
            CancellationToken cancellationToken)
        {
            if (!copySources.TryGetValue(file.Sha256, out string? sourcePath))
            {
                return false;
            }

            FileInfo sourceInfo = new FileInfo(sourcePath);
            if (!sourceInfo.Exists || (sourceInfo.Length != file.Size))
            {
                return false;
            }

            string tempPath = NewStagingPath(BLOB_TEMP_PREFIX);
            try
            {
                await using (FileStream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    COPY_BUFFER_BYTES, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (FileStream target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    COPY_BUFFER_BYTES, FileOptions.Asynchronous))
                {
                    await source.CopyToAsync(target, COPY_BUFFER_BYTES, cancellationToken).ConfigureAwait(false);
                }

                // 활성 폴더 파일이 손상됐을 수 있다. 맞지 않으면 복사를 버리고 받는다
                string copiedSha256 = await PatchHash.ComputeFileAsync(tempPath, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(copiedSha256, file.Sha256, StringComparison.Ordinal))
                {
                    return false;
                }

                File.Move(tempPath, destination, true);
                return true;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private Dictionary<string, string> BuildCopySources(InstalledRelease? copySource)
        {
            Dictionary<string, string> sources = new Dictionary<string, string>(StringComparer.Ordinal);
            if (copySource == null)
            {
                return sources;
            }

            foreach (ManifestFile file in copySource.Manifest.Files)
            {
                if (PatchPaths.TryResolveUnderRoot(copySource.VersionPath, file.Path, out string? path))
                {
                    sources.TryAdd(file.Sha256, path);
                }
            }

            return sources;
        }

        private static async Task<bool> IsFileValidAsync(string path, ManifestFile file, CancellationToken cancellationToken)
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists || (info.Length != file.Size))
            {
                return false;
            }

            string sha256 = await PatchHash.ComputeFileAsync(path, cancellationToken).ConfigureAwait(false);
            return string.Equals(sha256, file.Sha256, StringComparison.Ordinal);
        }

        private static void PrepareDestination(string destination)
        {
            if (Directory.Exists(destination))
            {
                PatchFileSystem.DeleteDirectory(destination);
            }

            PatchFileSystem.ClearReadOnly(destination);

            string? parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }
        }

        private static int RemoveExtraFiles(string folder, ReleaseManifest manifest)
        {
            if (!Directory.Exists(folder))
            {
                return 0;
            }

            // Windows 파일시스템처럼 대소문자를 구분하지 않는다. 매니페스트도 대소문자만 다른 중복을 거절한다
            HashSet<string> expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ManifestFile file in manifest.Files)
            {
                expected.Add(PatchPaths.ToNative(file.Path));
            }

            return RemoveExtraFilesUnder(folder, folder, expected);
        }

        private static int RemoveExtraFilesUnder(string root, string directory, HashSet<string> expected)
        {
            int removed = 0;
            foreach (FileSystemInfo entry in new DirectoryInfo(directory).GetFileSystemInfos())
            {
                bool isLink = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
                if (entry is DirectoryInfo child)
                {
                    // 매니페스트는 링크를 만들지 않는다. 링크 폴더는 따라 들어가지 않고 링크만 지워 폴더 밖 파일을 지킨다
                    if (isLink)
                    {
                        child.Delete(false);
                        ++removed;
                        continue;
                    }

                    removed += RemoveExtraFilesUnder(root, child.FullName, expected);
                    if (child.GetFileSystemInfos().Length == 0)
                    {
                        child.Delete(false);
                    }

                    continue;
                }

                if (expected.Contains(Path.GetRelativePath(root, entry.FullName)))
                {
                    continue;
                }

                PatchFileSystem.DeleteFile(entry.FullName);
                ++removed;
            }

            return removed;
        }

        // 활성 버전과 직전 버전 하나만 남긴다. 지우지 못한 폴더는 다음 업데이트에서 다시 지운다
        private int RemoveOldVersions(string activeReleaseId, string? previousReleaseId, out bool isComplete)
        {
            isComplete = true;
            HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { activeReleaseId };
            if (previousReleaseId != null)
            {
                keep.Add(previousReleaseId);
            }

            int removed = 0;
            foreach (string directory in Directory.GetDirectories(_layout.VersionsPath))
            {
                if (keep.Contains(Path.GetFileName(directory)))
                {
                    continue;
                }

                if (TryDelete(directory, true))
                {
                    ++removed;
                }
                else
                {
                    isComplete = false;
                }
            }

            if (!Directory.Exists(_layout.ManifestsPath))
            {
                return removed;
            }

            foreach (string file in Directory.GetFiles(_layout.ManifestsPath))
            {
                if (keep.Contains(Path.GetFileNameWithoutExtension(file)))
                {
                    continue;
                }

                if (!TryDelete(file, false))
                {
                    isComplete = false;
                }
            }

            return removed;
        }

        // 새 버전은 이미 활성이라 보관 정리 실패는 업데이트 실패로 돌리지 않는다. 결과에 표시만 한다
        private static bool TryDelete(string path, bool isDirectory)
        {
            try
            {
                if (isDirectory)
                {
                    PatchFileSystem.DeleteDirectory(path);
                }
                else
                {
                    PatchFileSystem.DeleteFile(path);
                }

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

        private bool TryCleanStaging()
        {
            return TryDelete(_layout.StagingPath, true);
        }

        private FileStream? TryAcquireLock()
        {
            Directory.CreateDirectory(_layout.ProductPath);
            try
            {
                return new FileStream(_layout.LockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, LOCK_BUFFER_BYTES,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                // 다른 런처 인스턴스가 같은 설치 폴더를 고치는 중이다
                return null;
            }
        }

        private string NewStagingPath(string prefix)
        {
            if (!_layout.TryGetStagingPath(PatchFileSystem.NewRandomName(prefix), out string? path))
            {
                throw new InvalidOperationException("Staging path cannot be resolved under the install root.");
            }

            return path;
        }

        private bool HasDiskSpace(ReleaseManifest manifest)
        {
            long required = (long)Math.Ceiling(manifest.GetTotalSize() * DISK_SPACE_MARGIN);
            long? available = GetAvailableFreeSpace();
            return (available == null) || (available.Value >= required);
        }

        private long? GetAvailableFreeSpace()
        {
            if (_options.FreeSpaceProbe != null)
            {
                return _options.FreeSpaceProbe(_layout.InstallRoot);
            }

            string? driveRoot = Path.GetPathRoot(_layout.InstallRoot);
            if (string.IsNullOrEmpty(driveRoot))
            {
                return null;
            }

            try
            {
                return new DriveInfo(driveRoot).AvailableFreeSpace;
            }
            catch (ArgumentException)
            {
                // UNC 경로처럼 DriveInfo가 다루지 못하는 위치는 확인을 건너뛰고, 공간이 모자라면 쓰기 실패(IoFailure)로 알린다
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private bool IsLauncherSufficient(ReleaseManifest manifest)
        {
            if (_options.LauncherVersion == null)
            {
                return true;
            }

            return PatchValidation.IsLauncherVersionSufficient(manifest.MinLauncherVersion, _options.LauncherVersion);
        }

        private static long ComputeBytesToDownload(ReleaseManifest manifest, InstalledRelease? active)
        {
            HashSet<string> available = new HashSet<string>(StringComparer.Ordinal);
            if (active != null)
            {
                foreach (ManifestFile file in active.Manifest.Files)
                {
                    available.Add(file.Sha256);
                }
            }

            // 같은 해시 파일은 한 번 받고 나머지는 복사한다
            long bytes = 0;
            foreach (ManifestFile file in manifest.Files)
            {
                if (available.Add(file.Sha256))
                {
                    bytes += file.Size;
                }
            }

            return bytes;
        }

        private static bool IsSameInstall(InstalledRelease active, RemoteRelease remote)
        {
            return string.Equals(active.ReleaseId, remote.ReleaseId, StringComparison.Ordinal)
                && string.Equals(active.ManifestSha256, remote.ManifestSha256, StringComparison.Ordinal);
        }

        // 같은 releaseId(대소문자 무시, 같은 폴더)인데 매니페스트가 다르면 활성 폴더를 제자리에서 바꿔야 해 원자성이 깨진다.
        // 게시 도구는 releaseId를 다시 쓰지 않으므로 게시 실수로 보고 멈춘다
        private static bool IsReleaseConflict(InstalledRelease active, RemoteRelease remote)
        {
            return string.Equals(active.ReleaseId, remote.ReleaseId, StringComparison.OrdinalIgnoreCase) && !IsSameInstall(active, remote);
        }

        private static bool IsTransientSourceFailure(Exception exception, CancellationToken cancellationToken)
        {
            if (exception is OperationCanceledException)
            {
                // 호출자 취소가 아닌 취소는 HttpClient 시간 초과다
                return !cancellationToken.IsCancellationRequested;
            }

            return (exception is PatchSourceException) || (exception is HttpRequestException);
        }

        private static bool TryMapException(Exception exception, CancellationToken cancellationToken, out EPatchClientError error)
        {
            if ((exception is OperationCanceledException) && cancellationToken.IsCancellationRequested)
            {
                error = EPatchClientError.Cancelled;
                return true;
            }

            if (IsTransientSourceFailure(exception, cancellationToken))
            {
                error = EPatchClientError.Network;
                return true;
            }

            if ((exception is IOException) || (exception is UnauthorizedAccessException))
            {
                error = EPatchClientError.IoFailure;
                return true;
            }

            error = EPatchClientError.None;
            return false;
        }

        private static void Report(IProgress<PatchProgress>? progress, EPatchStage stage)
        {
            progress?.Report(new PatchProgress(stage, 0, 0));
        }
    }
}
