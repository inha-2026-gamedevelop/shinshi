// System
using System;
using System.IO;

// Third-party
using Newtonsoft.Json;
using Xunit;

// Project
using Shinshi.Contracts;
using Shinshi.Patch;

namespace Shinshi.Launcher.Tests.Core
{
    /// <summary> 빌드 폴더에서 build-identity.json을 정확히 하나 찾아 읽는지 검사한다 </summary>
    public sealed class BuildIdentityReaderTests : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int  PROTOCOL_VERSION = 3;
        private const long CREATED_AT       = 1790000000000;

        private readonly string _buildFolder;

        /****************************************
        *                Methods
        ****************************************/

        public BuildIdentityReaderTests()
        {
            _buildFolder = PatchTestFactory.CreateTempDirectory();
        }

        public void Dispose()
        {
            PatchTestFactory.DeleteDirectory(_buildFolder);
        }

        [Fact]
        public void TryRead_SingleIdentity_ReturnsParsedIdentity()
        {
            WriteIdentity("Shinshi_Data", CreateIdentity(BuildIdentity.KIND_RELEASE));

            bool read = BuildIdentityReader.TryRead(_buildFolder, out BuildIdentity? identity, out EPatchError error);

            Assert.True(read);
            Assert.Equal(EPatchError.None, error);
            Assert.Equal("build-1", identity!.BuildId);
            Assert.Equal(PROTOCOL_VERSION, identity.ProtocolVersion);
            Assert.True(identity.IsRelease);
        }

        [Fact]
        public void TryRead_NoIdentity_ReturnsBuildIdentityMissing()
        {
            Directory.CreateDirectory(Path.Combine(_buildFolder, "Shinshi_Data", "StreamingAssets"));

            bool read = BuildIdentityReader.TryRead(_buildFolder, out BuildIdentity? identity, out EPatchError error);

            Assert.False(read);
            Assert.Null(identity);
            Assert.Equal(EPatchError.BuildIdentityMissing, error);
        }

        [Fact]
        public void TryRead_MissingFolder_ReturnsBuildIdentityMissing()
        {
            bool read = BuildIdentityReader.TryRead(Path.Combine(_buildFolder, "nope"), out _, out EPatchError error);

            Assert.False(read);
            Assert.Equal(EPatchError.BuildIdentityMissing, error);
        }

        [Fact]
        public void TryRead_TwoDataFolders_ReturnsBuildIdentityAmbiguous()
        {
            WriteIdentity("Shinshi_Data", CreateIdentity(BuildIdentity.KIND_RELEASE));
            WriteIdentity("Other_Data", CreateIdentity(BuildIdentity.KIND_RELEASE));

            bool read = BuildIdentityReader.TryRead(_buildFolder, out _, out EPatchError error);

            Assert.False(read);
            Assert.Equal(EPatchError.BuildIdentityAmbiguous, error);
        }

        [Fact]
        public void TryRead_BrokenJson_ReturnsMalformed()
        {
            string folder = Path.Combine(_buildFolder, "Shinshi_Data", "StreamingAssets");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, BuildIdentity.FILE_NAME), "{ not json");

            bool read = BuildIdentityReader.TryRead(_buildFolder, out _, out EPatchError error);

            Assert.False(read);
            Assert.Equal(EPatchError.Malformed, error);
        }

        [Fact]
        public void TryRead_UnknownBuildKind_ReturnsInvalidField()
        {
            WriteIdentity("Shinshi_Data", CreateIdentity("Debug"));

            bool read = BuildIdentityReader.TryRead(_buildFolder, out _, out EPatchError error);

            Assert.False(read);
            Assert.Equal(EPatchError.InvalidField, error);
        }

        private static BuildIdentity CreateIdentity(string buildKind)
        {
            return new BuildIdentity("build-1", PROTOCOL_VERSION, "content-hash", "0.1.0", buildKind, CREATED_AT, "abc1234");
        }

        private void WriteIdentity(string dataFolderName, BuildIdentity identity)
        {
            string folder = Path.Combine(_buildFolder, dataFolderName, "StreamingAssets");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, BuildIdentity.FILE_NAME), JsonConvert.SerializeObject(identity, ContractJson.CreateSettings()));
        }
    }
}
