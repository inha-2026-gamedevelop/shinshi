// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Project
using Shinshi.Contracts;
using Shinshi.Patch;
using Shinshi.Patch.Client;

namespace Shinshi.Launcher.Tests.Client
{
    // 게시 도구 없이 Core만으로 게시 폴더를 만들고, 그 폴더를 원본으로 쓰는 PatchClient를 만든다.
    // 키는 테스트마다 새로 만들고 파일로 남기지 않는다
    internal sealed class PatchPublishFixture : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PRODUCT         = PatchFormat.PRODUCT_GAME;
        public const string CHANNEL         = PatchFormat.CHANNEL_INTERNAL;
        public const string BUILD_ID        = "build-test";
        public const string DISPLAY_VERSION = "0.1.0";
        public const string MIN_LAUNCHER    = "1.0.0";
        public const long   CREATED_AT      = 1790000000000;

        private static readonly UTF8Encoding G_Utf8 = new UTF8Encoding(false);

        private readonly string          _root;
        private readonly PatchSigningKey _key;

        /****************************************
        *              Properties
        ****************************************/

        public string              PublishRoot { get; }
        public string              InstallRoot { get; }
        public PatchSigningKey     Key         => _key;
        public TrustedKeySet       Keys        { get; }
        public CountingPatchSource Source      { get; }

        /****************************************
        *                Methods
        ****************************************/

        public PatchPublishFixture()
        {
            _root       = Path.Combine(Path.GetTempPath(), "shinshi-launcher-tests", Guid.NewGuid().ToString("N"));
            PublishRoot = Path.Combine(_root, "publish");
            InstallRoot = Path.Combine(_root, "install");
            Directory.CreateDirectory(PublishRoot);
            Directory.CreateDirectory(InstallRoot);

            _key   = PatchSigningKey.Generate();
            Keys   = new TrustedKeySet(_key.PublicKey);
            Source = new CountingPatchSource(new FileSystemPatchSource(PublishRoot));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        public PatchClient CreateClient(Func<string, long>? freeSpaceProbe = null, Version? launcherVersion = null)
        {
            PatchClientOptions options = new PatchClientOptions(InstallRoot, PRODUCT, CHANNEL)
            {
                FreeSpaceProbe  = freeSpaceProbe,
                LauncherVersion = launcherVersion
            };

            return new PatchClient(options, Source, Keys);
        }

        // 파일 내용은 텍스트로 받는다. blob, 매니페스트, 채널 포인터를 모두 쓰고 매니페스트 payload 해시를 돌려준다
        public string Publish(string releaseId, long sequence, IReadOnlyDictionary<string, string> files)
        {
            List<ManifestFile> entries = new List<ManifestFile>();
            foreach (KeyValuePair<string, string> pair in files)
            {
                byte[] bytes = G_Utf8.GetBytes(pair.Value);
                string sha256 = PatchHash.Compute(bytes);
                WriteBlob(sha256, bytes);
                entries.Add(new ManifestFile(pair.Key, bytes.Length, sha256));
            }

            ReleaseManifest manifest = new ReleaseManifest(PRODUCT, releaseId, CHANNEL, BUILD_ID, BuildIdentity.KIND_RELEASE, DISPLAY_VERSION,
                MIN_LAUNCHER, CREATED_AT, entries);
            if (!PatchDocuments.TrySealManifest(manifest, _key, out byte[]? envelope, out string? manifestSha256, out EPatchError error))
            {
                throw new InvalidOperationException("Test manifest could not be sealed: " + error);
            }

            WriteManifestEnvelope(releaseId, envelope);
            WritePointer(releaseId, sequence, manifestSha256);
            return manifestSha256;
        }

        public void WritePointer(string releaseId, long sequence, string manifestSha256)
        {
            ChannelPointer pointer = new ChannelPointer(PRODUCT, CHANNEL, releaseId, sequence, manifestSha256, CREATED_AT);
            if (!PatchDocuments.TrySealChannelPointer(pointer, _key, out byte[]? envelope, out EPatchError error))
            {
                throw new InvalidOperationException("Test pointer could not be sealed: " + error);
            }

            WritePublished(PatchKeys.Channel(PRODUCT, CHANNEL), envelope);
        }

        public void WriteManifestEnvelope(string releaseId, byte[] envelope)
        {
            WritePublished(PatchKeys.Manifest(PRODUCT, releaseId), envelope);
        }

        public void WriteBlob(string sha256, byte[] bytes)
        {
            WritePublished(PatchKeys.Blob(PRODUCT, sha256), bytes);
        }

        public static string BlobKeyOf(string content)
        {
            return PatchKeys.Blob(PRODUCT, PatchHash.Compute(G_Utf8.GetBytes(content)));
        }

        public string GetVersionPath(string releaseId)
        {
            return Path.Combine(InstallRoot, PRODUCT, PatchInstallLayout.VERSIONS_FOLDER, releaseId);
        }

        public string GetInstalledFile(string releaseId, string relativePath)
        {
            return Path.Combine(GetVersionPath(releaseId), PatchPaths.ToNative(relativePath));
        }

        public string[] GetVersionFolderNames()
        {
            string versions = Path.Combine(InstallRoot, PRODUCT, PatchInstallLayout.VERSIONS_FOLDER);
            if (!Directory.Exists(versions))
            {
                return Array.Empty<string>();
            }

            string[] folders = Directory.GetDirectories(versions);
            for (int i = 0; i < folders.Length; ++i)
            {
                folders[i] = Path.GetFileName(folders[i]);
            }

            Array.Sort(folders, StringComparer.Ordinal);
            return folders;
        }

        // staging 폴더가 없거나 비어 있으면 true
        public bool IsStagingEmpty()
        {
            string staging = Path.Combine(InstallRoot, PRODUCT, PatchInstallLayout.STAGING_FOLDER);
            return !Directory.Exists(staging) || (Directory.GetFileSystemEntries(staging).Length == 0);
        }

        public static string ReadText(string path) { return File.ReadAllText(path, G_Utf8); }

        private void WritePublished(string key, byte[] bytes)
        {
            string path = Path.Combine(PublishRoot, PatchPaths.ToNative(key));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
    }
}
