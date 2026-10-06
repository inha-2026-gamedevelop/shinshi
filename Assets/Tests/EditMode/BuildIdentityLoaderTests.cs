// System
using System.IO;
using System.Text;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class BuildIdentityLoaderTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    PROTOCOL     = 7;
        private const int    OTHER_PROTOCOL = 8;
        private const string CONTENT_HASH = "abc123";
        private const string BUILD_ID     = "6f0e6f3c-build";
        private const string ARG_BUILD_ID = "arg-build";

        private string _tempPath;

        /****************************************
        *              Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _tempPath = Path.Combine(Path.GetTempPath(), "shinshi-build-identity-" + System.Guid.NewGuid().ToString("N") + ".json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempPath))
            {
                File.Delete(_tempPath);
            }
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Load_ValidFile_ReturnsIdentity()
        {
            File.WriteAllText(_tempPath, CreateJson(BUILD_ID, PROTOCOL, CONTENT_HASH, BuildIdentity.KIND_RELEASE), new UTF8Encoding(false));

            BuildIdentity identity = BuildIdentityLoader.Load(_tempPath, PROTOCOL, CONTENT_HASH, out EBuildIdentityStatus status);

            Assert.AreEqual(EBuildIdentityStatus.Loaded, status);
            Assert.IsNotNull(identity);
            Assert.AreEqual(BUILD_ID, identity.BuildId);
            Assert.IsTrue(identity.IsRelease);
        }

        [Test]
        public void Load_MissingFile_ReturnsMissing()
        {
            BuildIdentity identity = BuildIdentityLoader.Load(_tempPath, PROTOCOL, CONTENT_HASH, out EBuildIdentityStatus status);

            Assert.AreEqual(EBuildIdentityStatus.Missing, status);
            Assert.IsNull(identity);
        }

        [Test]
        public void Load_NullPath_ReturnsMissing()
        {
            Assert.IsNull(BuildIdentityLoader.Load(null, PROTOCOL, CONTENT_HASH, out EBuildIdentityStatus status));
            Assert.AreEqual(EBuildIdentityStatus.Missing, status);
        }

        [Test]
        public void Load_InvalidUtf8_ReturnsCorrupt()
        {
            File.WriteAllBytes(_tempPath, new byte[] { 0xC3, 0x28, 0x7B });

            Assert.IsNull(BuildIdentityLoader.Load(_tempPath, PROTOCOL, CONTENT_HASH, out EBuildIdentityStatus status));
            Assert.AreEqual(EBuildIdentityStatus.Corrupt, status);
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("{\"buildId\":")]
        [TestCase("[]")]
        [TestCase("null")]
        [TestCase("{\"$type\":\"System.Object\"}")]
        public void Parse_BrokenJson_ReturnsCorrupt(string json)
        {
            Assert.IsNull(BuildIdentityLoader.Parse(json, PROTOCOL, CONTENT_HASH, out EBuildIdentityStatus status));
            Assert.AreEqual(EBuildIdentityStatus.Corrupt, status);
        }

        [Test]
        public void Parse_EmptyBuildId_ReturnsCorrupt()
        {
            string json = CreateJson(string.Empty, PROTOCOL, CONTENT_HASH, BuildIdentity.KIND_RELEASE);

            Assert.IsNull(BuildIdentityLoader.Parse(json, PROTOCOL, CONTENT_HASH, out EBuildIdentityStatus status));
            Assert.AreEqual(EBuildIdentityStatus.Corrupt, status);
        }

        [Test]
        public void Parse_UnknownBuildKind_ReturnsCorrupt()
        {
            string json = CreateJson(BUILD_ID, PROTOCOL, CONTENT_HASH, "Nightly");

            Assert.IsNull(BuildIdentityLoader.Parse(json, PROTOCOL, CONTENT_HASH, out EBuildIdentityStatus status));
            Assert.AreEqual(EBuildIdentityStatus.Corrupt, status);
        }

        [Test]
        public void Parse_ProtocolMismatch_ReturnsProtocolMismatch()
        {
            string json = CreateJson(BUILD_ID, OTHER_PROTOCOL, CONTENT_HASH, BuildIdentity.KIND_RELEASE);

            Assert.IsNull(BuildIdentityLoader.Parse(json, PROTOCOL, CONTENT_HASH, out EBuildIdentityStatus status));
            Assert.AreEqual(EBuildIdentityStatus.ProtocolMismatch, status);
        }

        [Test]
        public void Parse_ContentHashMismatch_ReturnsContentHashMismatch()
        {
            string json = CreateJson(BUILD_ID, PROTOCOL, "other-hash", BuildIdentity.KIND_DEVELOPMENT);

            Assert.IsNull(BuildIdentityLoader.Parse(json, PROTOCOL, CONTENT_HASH, out EBuildIdentityStatus status));
            Assert.AreEqual(EBuildIdentityStatus.ContentHashMismatch, status);
        }

        [Test]
        public void LoadForRuntime_InEditor_ReturnsEditorSkipped()
        {
            Assert.IsNull(BuildIdentityLoader.LoadForRuntime(CONTENT_HASH, out EBuildIdentityStatus status));
            Assert.AreEqual(EBuildIdentityStatus.EditorSkipped, status);
        }

        /****************************************
        *           Build Id Choice Tests
        ****************************************/

        [Test]
        public void ResolveBuildId_ReleaseWithIdentity_UsesIdentityAndIgnoresArgument()
        {
            BuildIdentity identity = CreateIdentity();

            Assert.AreEqual(BUILD_ID, LobbyFlow.ResolveBuildId(false, ARG_BUILD_ID, identity, EBuildIdentityStatus.Loaded));
        }

        [TestCase(EBuildIdentityStatus.Missing)]
        [TestCase(EBuildIdentityStatus.Corrupt)]
        [TestCase(EBuildIdentityStatus.ProtocolMismatch)]
        [TestCase(EBuildIdentityStatus.ContentHashMismatch)]
        public void ResolveBuildId_ReleaseWithoutUsableIdentity_ReturnsNull(EBuildIdentityStatus status)
        {
            Assert.IsNull(LobbyFlow.ResolveBuildId(false, ARG_BUILD_ID, null, status));
        }

        [Test]
        public void ResolveBuildId_DevelopmentWithArgument_UsesArgument()
        {
            Assert.AreEqual(ARG_BUILD_ID, LobbyFlow.ResolveBuildId(true, ARG_BUILD_ID, CreateIdentity(), EBuildIdentityStatus.Loaded));
        }

        [Test]
        public void ResolveBuildId_DevelopmentWithFileOnly_UsesIdentity()
        {
            Assert.AreEqual(BUILD_ID, LobbyFlow.ResolveBuildId(true, null, CreateIdentity(), EBuildIdentityStatus.Loaded));
        }

        [Test]
        public void ResolveBuildId_DevelopmentWithoutAnything_UsesDevLocal()
        {
            Assert.AreEqual(LobbyFlow.DEV_BUILD_ID, LobbyFlow.ResolveBuildId(true, " ", null, EBuildIdentityStatus.ContentHashMismatch));
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static BuildIdentity CreateIdentity()
        {
            return new BuildIdentity(BUILD_ID, PROTOCOL, CONTENT_HASH, "0.1.0", BuildIdentity.KIND_RELEASE, 1, "abc");
        }

        private static string CreateJson(string buildId, int protocol, string contentHash, string buildKind)
        {
            return "{\"buildId\":\"" + buildId + "\",\"protocolVersion\":" + protocol + ",\"contentHash\":\"" + contentHash +
                "\",\"displayVersion\":\"0.1.0\",\"buildKind\":\"" + buildKind + "\",\"createdAtUnixMs\":1700000000000," +
                "\"commit\":\"abc123def456\"}";
        }
    }
}
