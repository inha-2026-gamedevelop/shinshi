// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Third-party
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Project
using Shinshi.Contracts;
using Shinshi.Patch;

namespace Shinshi.Launcher.Tests.Core
{
    // 테스트마다 키를 새로 만든다. 키 값은 파일이나 고정값으로 남기지 않는다
    internal static class PatchTestFactory
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string RELEASE_ID   = "2026.10.02-1";
        public const string BUILD_ID     = "build-abc123";
        public const string MIN_LAUNCHER = "1.0.0";
        public const long   CREATED_AT   = 1790000000000;
        public const long   SEQUENCE     = 7;

        public static readonly string SHA_A = new string('a', PatchHash.SHA256_HEX_LENGTH);
        public static readonly string SHA_B = new string('b', PatchHash.SHA256_HEX_LENGTH);

        /****************************************
        *                Methods
        ****************************************/

        public static ReleaseManifest CreateManifest(string channel = PatchFormat.CHANNEL_INTERNAL, string product = PatchFormat.PRODUCT_GAME,
            string releaseId = RELEASE_ID, string buildKind = BuildIdentity.KIND_RELEASE, string minLauncher = MIN_LAUNCHER,
            IEnumerable<ManifestFile>? files = null, string buildId = BUILD_ID, string displayVersion = "0.1.0",
            long createdAt = CREATED_AT)
        {
            files ??= new[]
            {
                new ManifestFile("Shinshi.exe", 10, SHA_A),
                new ManifestFile("Shinshi_Data/StreamingAssets/build-identity.json", 20, SHA_B)
            };

            return new ReleaseManifest(product, releaseId, channel, buildId, buildKind, displayVersion, minLauncher, createdAt, files);
        }

        public static ChannelPointer CreatePointer(string manifestSha256, string channel = PatchFormat.CHANNEL_INTERNAL,
            string product = PatchFormat.PRODUCT_GAME, string releaseId = RELEASE_ID, long sequence = SEQUENCE)
        {
            return new ChannelPointer(product, channel, releaseId, sequence, manifestSha256, CREATED_AT);
        }

        public static TrustedKeySet Trust(params PatchSigningKey[] keys)
        {
            List<PatchPublicKey> publicKeys = new List<PatchPublicKey>();
            foreach (PatchSigningKey key in keys)
            {
                publicKeys.Add(key.PublicKey);
            }

            return new TrustedKeySet(publicKeys);
        }

        public static byte[] Utf8(string text) { return new UTF8Encoding(false).GetBytes(text); }

        public static JObject ParseEnvelope(byte[] envelope) { return JObject.Parse(Encoding.UTF8.GetString(envelope)); }

        public static byte[] WriteEnvelope(JObject envelope) { return Utf8(envelope.ToString(Formatting.None)); }

        public static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "shinshi-launcher-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        public static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}
