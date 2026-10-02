// System
using System;
using System.Collections.Generic;

// Third-party
using Newtonsoft.Json.Linq;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Patch;

namespace Shinshi.Launcher.Tests.Core
{
    /// <summary> 매니페스트와 채널 포인터의 서명 왕복, 필드 검증, 포인터와 매니페스트의 짝 검사 </summary>
    public sealed class PatchDocumentsTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void TryOpenManifest_SealedManifest_RoundTripsFieldsAndPayloadHash()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            ReleaseManifest source = PatchTestFactory.CreateManifest();

            Assert.True(PatchDocuments.TrySealManifest(source, key, out byte[]? envelope, out string? sealedSha, out EPatchError sealError));
            Assert.Equal(EPatchError.None, sealError);

            bool opened = PatchDocuments.TryOpenManifest(envelope, PatchTestFactory.Trust(key), out ReleaseManifest? manifest,
                out string? openedSha, out EPatchError error);

            Assert.True(opened);
            Assert.Equal(EPatchError.None, error);
            Assert.Equal(sealedSha, openedSha);
            Assert.True(PatchHash.IsSha256Hex(openedSha));
            Assert.Equal(source.ReleaseId, manifest!.ReleaseId);
            Assert.Equal(source.Product, manifest.Product);
            Assert.Equal(source.Channel, manifest.Channel);
            Assert.Equal(source.BuildId, manifest.BuildId);
            Assert.Equal(source.BuildKind, manifest.BuildKind);
            Assert.Equal(source.MinLauncherVersion, manifest.MinLauncherVersion);
            Assert.Equal(source.CreatedAtUnixMs, manifest.CreatedAtUnixMs);
            Assert.Equal(source.Files.Count, manifest.Files.Count);
            Assert.Equal(source.Files[1].Path, manifest.Files[1].Path);
            Assert.Equal(source.Files[1].Size, manifest.Files[1].Size);
            Assert.Equal(source.Files[1].Sha256, manifest.Files[1].Sha256);
            Assert.Equal(source.GetTotalSize(), manifest.GetTotalSize());
        }

        [Fact]
        public void TryOpenManifest_PayloadHash_MatchesHashOfVerifiedPayloadBytes()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            Assert.True(PatchDocuments.TrySealManifest(PatchTestFactory.CreateManifest(), key, out byte[]? envelope, out _, out _));
            Assert.True(PatchEnvelope.TryOpen(envelope, PatchFormat.KIND_MANIFEST, PatchTestFactory.Trust(key), out byte[]? payload, out _));

            Assert.True(PatchDocuments.TryOpenManifest(envelope, PatchTestFactory.Trust(key), out _, out string? sha, out _));

            Assert.Equal(PatchHash.Compute(payload), sha);
        }

        [Fact]
        public void TryOpenChannelPointer_SealedPointer_RoundTripsFields()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            ChannelPointer source = PatchTestFactory.CreatePointer(PatchTestFactory.SHA_A);

            Assert.True(PatchDocuments.TrySealChannelPointer(source, key, out byte[]? envelope, out _));

            bool opened = PatchDocuments.TryOpenChannelPointer(envelope, PatchTestFactory.Trust(key), out ChannelPointer? pointer,
                out EPatchError error);

            Assert.True(opened);
            Assert.Equal(EPatchError.None, error);
            Assert.Equal(source.Product, pointer!.Product);
            Assert.Equal(source.Channel, pointer.Channel);
            Assert.Equal(source.ReleaseId, pointer.ReleaseId);
            Assert.Equal(source.Sequence, pointer.Sequence);
            Assert.Equal(source.ManifestSha256, pointer.ManifestSha256);
            Assert.Equal(source.PublishedAtUnixMs, pointer.PublishedAtUnixMs);
        }

        [Fact]
        public void TryOpenManifest_ChannelPointerEnvelope_ReturnsKindMismatch()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            Assert.True(PatchDocuments.TrySealChannelPointer(PatchTestFactory.CreatePointer(PatchTestFactory.SHA_A), key,
                out byte[]? envelope, out _));

            bool opened = PatchDocuments.TryOpenManifest(envelope, PatchTestFactory.Trust(key), out _, out _, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(EPatchError.KindMismatch, error);
        }

        public static IEnumerable<object[]> InvalidManifests()
        {
            yield return new object[] { "unknown product", PatchTestFactory.CreateManifest(product: "tool"), EPatchError.InvalidField };
            yield return new object[] { "unknown channel", PatchTestFactory.CreateManifest(channel: "beta"), EPatchError.InvalidField };
            yield return new object[] { "release id dot", PatchTestFactory.CreateManifest(releaseId: "."), EPatchError.InvalidField };
            yield return new object[] { "release id dotdot", PatchTestFactory.CreateManifest(releaseId: ".."), EPatchError.InvalidField };
            yield return new object[] { "release id slash", PatchTestFactory.CreateManifest(releaseId: "a/b"), EPatchError.InvalidField };
            yield return new object[] { "release id too long", PatchTestFactory.CreateManifest(releaseId: new string('r', 65)), EPatchError.InvalidField };
            yield return new object[] { "empty build id", PatchTestFactory.CreateManifest(buildId: ""), EPatchError.InvalidField };
            yield return new object[] { "build id with space", PatchTestFactory.CreateManifest(buildId: "a b"), EPatchError.InvalidField };
            yield return new object[] { "unknown build kind", PatchTestFactory.CreateManifest(buildKind: "Debug"), EPatchError.InvalidField };
            yield return new object[] { "stable development", PatchTestFactory.CreateManifest(channel: PatchFormat.CHANNEL_STABLE, buildKind: BuildIdentity.KIND_DEVELOPMENT), EPatchError.InvalidField };
            yield return new object[] { "display version control char", PatchTestFactory.CreateManifest(displayVersion: "1.0\n"), EPatchError.InvalidField };
            yield return new object[] { "launcher version two parts", PatchTestFactory.CreateManifest(minLauncher: "1.0"), EPatchError.InvalidField };
            yield return new object[] { "launcher version leading zero", PatchTestFactory.CreateManifest(minLauncher: "1.01.0"), EPatchError.InvalidField };
            yield return new object[] { "launcher version letters", PatchTestFactory.CreateManifest(minLauncher: "1.0.x"), EPatchError.InvalidField };
            yield return new object[] { "negative created at", PatchTestFactory.CreateManifest(createdAt: -1), EPatchError.InvalidField };
            yield return new object[] { "no files", PatchTestFactory.CreateManifest(files: Array.Empty<ManifestFile>()), EPatchError.InvalidField };
            yield return new object[] { "negative size", PatchTestFactory.CreateManifest(files: new[] { new ManifestFile("a.txt", -1, PatchTestFactory.SHA_A) }), EPatchError.InvalidField };
            yield return new object[] { "uppercase sha", PatchTestFactory.CreateManifest(files: new[] { new ManifestFile("a.txt", 1, PatchTestFactory.SHA_A.ToUpperInvariant()) }), EPatchError.InvalidField };
            yield return new object[] { "short sha", PatchTestFactory.CreateManifest(files: new[] { new ManifestFile("a.txt", 1, "abc") }), EPatchError.InvalidField };
            yield return new object[] { "total size overflow", PatchTestFactory.CreateManifest(files: new[] { new ManifestFile("a.txt", long.MaxValue, PatchTestFactory.SHA_A), new ManifestFile("b.txt", 1, PatchTestFactory.SHA_B) }), EPatchError.InvalidField };
            yield return new object[] { "bad path", PatchTestFactory.CreateManifest(files: new[] { new ManifestFile("../evil.dll", 1, PatchTestFactory.SHA_A) }), EPatchError.InvalidPath };
            yield return new object[] { "case duplicate", PatchTestFactory.CreateManifest(files: new[] { new ManifestFile("Data/A.dll", 1, PatchTestFactory.SHA_A), new ManifestFile("data/a.DLL", 1, PatchTestFactory.SHA_B) }), EPatchError.DuplicatePath };
        }

        [Theory]
        [MemberData(nameof(InvalidManifests))]
        public void TrySealManifest_InvalidField_ReturnsError(string caseName, ReleaseManifest manifest, EPatchError expected)
        {
            PatchSigningKey key = PatchSigningKey.Generate();

            bool sealedOk = PatchDocuments.TrySealManifest(manifest, key, out byte[]? envelope, out _, out EPatchError error);

            Assert.False(sealedOk, caseName);
            Assert.Null(envelope);
            Assert.Equal(expected, error);
        }

        [Fact]
        public void TryOpenManifest_SignedPayloadWithBadPath_ReturnsInvalidPath()
        {
            // 서명이 맞아도 필드 규칙을 어기면 연다고 보지 않는다
            PatchSigningKey key = PatchSigningKey.Generate();
            JObject payload = JObject.FromObject(new
            {
                formatVersion = PatchFormat.FORMAT_VERSION,
                product = PatchFormat.PRODUCT_GAME,
                releaseId = PatchTestFactory.RELEASE_ID,
                channel = PatchFormat.CHANNEL_INTERNAL,
                buildId = PatchTestFactory.BUILD_ID,
                buildKind = BuildIdentity.KIND_RELEASE,
                displayVersion = "0.1.0",
                minLauncherVersion = PatchTestFactory.MIN_LAUNCHER,
                createdAtUnixMs = PatchTestFactory.CREATED_AT,
                files = new[] { new { path = "C:/Windows/evil.dll", size = 1, sha256 = PatchTestFactory.SHA_A } }
            });
            byte[] envelope = PatchEnvelope.Seal(PatchFormat.KIND_MANIFEST, PatchTestFactory.Utf8(payload.ToString()), key);

            bool opened = PatchDocuments.TryOpenManifest(envelope, PatchTestFactory.Trust(key), out ReleaseManifest? manifest, out _,
                out EPatchError error);

            Assert.False(opened);
            Assert.Null(manifest);
            Assert.Equal(EPatchError.InvalidPath, error);
        }

        [Theory]
        [InlineData("{\"formatVersion\":2}", EPatchError.UnsupportedFormat)]
        [InlineData("{\"formatVersion\":1,\"files\":[null]}", EPatchError.InvalidField)]
        [InlineData("[1,2,3]", EPatchError.Malformed)]
        [InlineData("{\"formatVersion\":\"one\"}", EPatchError.Malformed)]
        public void TryOpenManifest_SignedOddPayload_ReturnsError(string payloadJson, EPatchError expected)
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            byte[] envelope = PatchEnvelope.Seal(PatchFormat.KIND_MANIFEST, PatchTestFactory.Utf8(payloadJson), key);

            bool opened = PatchDocuments.TryOpenManifest(envelope, PatchTestFactory.Trust(key), out _, out _, out EPatchError error);

            Assert.False(opened);
            Assert.Equal(expected, error);
        }

        [Fact]
        public void TryOpenManifest_NullFileEntryAmongValid_ReturnsInvalidField()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            JObject payload = JObject.Parse(System.Text.Encoding.UTF8.GetString(SealedPayload(key)));
            ((JArray)payload["files"]!).Add(JValue.CreateNull());
            byte[] envelope = PatchEnvelope.Seal(PatchFormat.KIND_MANIFEST, PatchTestFactory.Utf8(payload.ToString()), key);

            Assert.False(PatchDocuments.TryOpenManifest(envelope, PatchTestFactory.Trust(key), out _, out _, out EPatchError error));
            Assert.Equal(EPatchError.InvalidField, error);
        }

        public static IEnumerable<object[]> InvalidPointers()
        {
            yield return new object[] { PatchTestFactory.CreatePointer(PatchTestFactory.SHA_A, sequence: 0) };
            yield return new object[] { PatchTestFactory.CreatePointer(PatchTestFactory.SHA_A, sequence: -5) };
            yield return new object[] { PatchTestFactory.CreatePointer("not-a-sha") };
            yield return new object[] { PatchTestFactory.CreatePointer(PatchTestFactory.SHA_A, channel: "nightly") };
            yield return new object[] { PatchTestFactory.CreatePointer(PatchTestFactory.SHA_A, product: "server") };
            yield return new object[] { PatchTestFactory.CreatePointer(PatchTestFactory.SHA_A, releaseId: "..") };
        }

        [Theory]
        [MemberData(nameof(InvalidPointers))]
        public void TrySealChannelPointer_InvalidField_ReturnsInvalidField(ChannelPointer pointer)
        {
            PatchSigningKey key = PatchSigningKey.Generate();

            bool sealedOk = PatchDocuments.TrySealChannelPointer(pointer, key, out byte[]? envelope, out EPatchError error);

            Assert.False(sealedOk);
            Assert.Null(envelope);
            Assert.Equal(EPatchError.InvalidField, error);
        }

        [Fact]
        public void CheckPointerMatchesManifest_MatchingPair_ReturnsNone()
        {
            PatchSigningKey key = PatchSigningKey.Generate();
            ReleaseManifest manifest = PatchTestFactory.CreateManifest();
            Assert.True(PatchDocuments.TrySealManifest(manifest, key, out _, out string? sha, out _));

            EPatchError error = PatchValidation.CheckPointerMatchesManifest(PatchTestFactory.CreatePointer(sha), manifest, sha);

            Assert.Equal(EPatchError.None, error);
        }

        [Fact]
        public void CheckPointerMatchesManifest_HashDiffers_ReturnsManifestHashMismatch()
        {
            ReleaseManifest manifest = PatchTestFactory.CreateManifest();

            EPatchError error = PatchValidation.CheckPointerMatchesManifest(PatchTestFactory.CreatePointer(PatchTestFactory.SHA_A),
                manifest, PatchTestFactory.SHA_B);

            Assert.Equal(EPatchError.ManifestHashMismatch, error);
        }

        [Fact]
        public void CheckPointerMatchesManifest_ProductDiffers_ReturnsProductMismatch()
        {
            ReleaseManifest manifest = PatchTestFactory.CreateManifest();
            ChannelPointer pointer = PatchTestFactory.CreatePointer(PatchTestFactory.SHA_A, product: PatchFormat.PRODUCT_LAUNCHER);

            Assert.Equal(EPatchError.ProductMismatch, PatchValidation.CheckPointerMatchesManifest(pointer, manifest, PatchTestFactory.SHA_A));
        }

        [Fact]
        public void CheckPointerMatchesManifest_ChannelDiffers_ReturnsChannelMismatch()
        {
            ReleaseManifest manifest = PatchTestFactory.CreateManifest();
            ChannelPointer pointer = PatchTestFactory.CreatePointer(PatchTestFactory.SHA_A, channel: PatchFormat.CHANNEL_STABLE);

            Assert.Equal(EPatchError.ChannelMismatch, PatchValidation.CheckPointerMatchesManifest(pointer, manifest, PatchTestFactory.SHA_A));
        }

        [Fact]
        public void CheckPointerMatchesManifest_ReleaseDiffers_ReturnsReleaseMismatch()
        {
            ReleaseManifest manifest = PatchTestFactory.CreateManifest();
            ChannelPointer pointer = PatchTestFactory.CreatePointer(PatchTestFactory.SHA_A, releaseId: "other-release");

            Assert.Equal(EPatchError.ReleaseMismatch, PatchValidation.CheckPointerMatchesManifest(pointer, manifest, PatchTestFactory.SHA_A));
        }

        [Theory]
        [InlineData(5, 4, EPatchError.SequenceRollback)]
        [InlineData(5, 5, EPatchError.None)]
        [InlineData(5, 6, EPatchError.None)]
        [InlineData(0, 1, EPatchError.None)]
        public void CheckSequence_ComparedToLastSeen_ReturnsExpected(long lastSeen, long received, EPatchError expected)
        {
            Assert.Equal(expected, PatchValidation.CheckSequence(lastSeen, received));
        }

        private static byte[] SealedPayload(PatchSigningKey key)
        {
            Assert.True(PatchDocuments.TrySealManifest(PatchTestFactory.CreateManifest(), key, out byte[]? envelope, out _, out _));
            Assert.True(PatchEnvelope.TryOpen(envelope, PatchFormat.KIND_MANIFEST, PatchTestFactory.Trust(key), out byte[]? payload, out _));
            return payload;
        }
    }
}
