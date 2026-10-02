// System
using System;
using System.IO;
using System.Text;

// Third-party
using Newtonsoft.Json;

// Project
using Shinshi.Contracts;
using Shinshi.Patch;
using Shinshi.Release;

namespace Shinshi.Launcher.Tests.Packager
{
    // 테스트마다 임시 폴더와 새 키를 만들고, Dispose에서 폴더를 지운다. 키 값은 파일이나 고정값으로 남기지 않는다
    internal sealed class PackagerTestFactory : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string BUILD_ID       = "build-abc123";
        public const string DISPLAY        = "0.1.0";
        public const string EXE_NAME       = "Shinshi.exe";
        public const string DATA_FOLDER    = "Shinshi_Data";
        public const string BOOT_CONFIG    = "Shinshi_Data/boot.config";
        public const string IDENTITY_PATH  = "Shinshi_Data/StreamingAssets/build-identity.json";
        public const string DATA_FILE      = "Shinshi_Data/data.bin";

        private readonly string _root;

        /****************************************
        *              Properties
        ****************************************/

        public string Root => _root;

        /****************************************
        *                Methods
        ****************************************/

        public PackagerTestFactory()
        {
            _root = Path.Combine(Path.GetTempPath(), "shinshi-packager-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        public string NewFolder(string name)
        {
            string path = Path.Combine(_root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        // Unity 플레이어 빌드와 같은 모양의 가짜 빌드 폴더를 만든다
        public string CreateGameBuild(string name = "build", string buildKind = BuildIdentity.KIND_RELEASE, string buildId = BUILD_ID,
            string bootConfig = "gfx-enable-native-jobs=1\n", string exeContent = "exe-v1", string dataContent = "data-v1")
        {
            string build = NewFolder(name);
            WriteText(build, EXE_NAME, exeContent);
            WriteText(build, DATA_FILE, dataContent);
            WriteText(build, BOOT_CONFIG, bootConfig);

            BuildIdentity identity = new BuildIdentity(buildId, 1, "content-hash", DISPLAY, buildKind, 1790000000000, "abc1234");
            WriteText(build, IDENTITY_PATH, JsonConvert.SerializeObject(identity, ContractJson.CreateSettings()));
            return build;
        }

        // 런처 빌드는 Unity 빌드가 아니라 신원 파일이 없다
        public string CreateLauncherBuild(string name = "launcher-build")
        {
            string build = NewFolder(name);
            WriteText(build, "ShinshiLauncher.exe", "launcher-exe");
            WriteText(build, "ShinshiLauncher.dll", "launcher-dll");
            return build;
        }

        public void WriteText(string folder, string relative, string content)
        {
            string path = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(content));
        }

        public string WriteKeyFile(PatchSigningKey key, string name = "private.key")
        {
            string path = Path.Combine(_root, name);
            File.WriteAllText(path, key.ExportBase64Seed());
            return path;
        }

        public PublishOptions GameOptions(string build, string keyFile, string outFolder, long sequence = 1,
            string channel = PatchFormat.CHANNEL_INTERNAL, string? releaseId = "rel-1")
        {
            return new PublishOptions(build, PatchFormat.PRODUCT_GAME, channel, sequence, keyFile, outFolder)
            {
                ReleaseId = releaseId
            };
        }

        public PublishOptions LauncherOptions(string build, string keyFile, string outFolder, long sequence = 1,
            string channel = PatchFormat.CHANNEL_INTERNAL, string buildKind = BuildIdentity.KIND_RELEASE, string? releaseId = "launcher-1")
        {
            return new PublishOptions(build, PatchFormat.PRODUCT_LAUNCHER, channel, sequence, keyFile, outFolder)
            {
                ReleaseId      = releaseId,
                BuildId        = "launcher-build-1",
                BuildKind      = buildKind,
                DisplayVersion = "1.0.0"
            };
        }

        public static int Publish(PublishOptions options, out string output, out string error, TimeProvider? timeProvider = null)
        {
            using StringWriter outWriter = new StringWriter();
            using StringWriter errWriter = new StringWriter();
            ReleasePublisher publisher = (timeProvider == null) ? new ReleasePublisher() : new ReleasePublisher(timeProvider);
            int code = publisher.Run(options, outWriter, errWriter);
            output = outWriter.ToString();
            error  = errWriter.ToString();
            return code;
        }

        public static string PointerPath(string outFolder, string product, string channel)
        {
            return Path.Combine(outFolder, product, "channels", channel + ".json");
        }

        public static string ManifestPath(string outFolder, string product, string releaseId)
        {
            return Path.Combine(outFolder, product, "releases", releaseId, "manifest.json");
        }

        public static string BlobPath(string outFolder, string product, string sha256)
        {
            return Path.Combine(outFolder, product, "blobs", sha256.Substring(0, 2), sha256);
        }

        public static ChannelPointer OpenPointer(string outFolder, string product, string channel, PatchSigningKey key)
        {
            byte[] envelope = File.ReadAllBytes(PointerPath(outFolder, product, channel));
            if (!PatchDocuments.TryOpenChannelPointer(envelope, new TrustedKeySet(key.PublicKey), out ChannelPointer? pointer, out EPatchError error))
            {
                throw new InvalidOperationException("Pointer did not open: " + error);
            }

            return pointer;
        }

        public static ReleaseManifest OpenManifest(string outFolder, string product, string releaseId, PatchSigningKey key,
            out string payloadSha256)
        {
            byte[] envelope = File.ReadAllBytes(ManifestPath(outFolder, product, releaseId));
            if (!PatchDocuments.TryOpenManifest(envelope, new TrustedKeySet(key.PublicKey), out ReleaseManifest? manifest,
                out string? sha, out EPatchError error))
            {
                throw new InvalidOperationException("Manifest did not open: " + error);
            }

            payloadSha256 = sha;
            return manifest;
        }

        public static int CountFiles(string folder)
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length : 0;
        }
    }
}
