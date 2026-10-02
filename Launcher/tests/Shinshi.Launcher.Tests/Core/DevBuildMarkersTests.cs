// System
using System;
using System.Collections.Generic;
using System.IO;

// Third-party
using Xunit;

// Project
using Shinshi.Patch;

namespace Shinshi.Launcher.Tests.Core
{
    /// <summary> boot.config의 개발 빌드 표식 탐지 </summary>
    public sealed class DevBuildMarkersTests : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        // 실측한 개발 빌드 boot.config에서 연결 정보 값만 바꿨다
        private const string DEVELOPMENT_BOOT_CONFIG =
            "player-connection-mode=Listen\n" +
            "player-connection-guid=1\n" +
            "player-connection-debug=0\n" +
            "gfx-enable-gfx-jobs=1\n" +
            "wait-for-native-debugger=0\n" +
            "build-guid=0123\n";

        private const string CLEAN_BOOT_CONFIG =
            "gfx-enable-gfx-jobs=1\r\n" +
            "player-connection-debug=0\r\n" +
            "wait-for-native-debugger=0\r\n" +
            "hdr-display-enabled=0\r\n";

        private readonly string _buildFolder;

        /****************************************
        *                Methods
        ****************************************/

        public DevBuildMarkersTests()
        {
            _buildFolder = PatchTestFactory.CreateTempDirectory();
        }

        public void Dispose()
        {
            PatchTestFactory.DeleteDirectory(_buildFolder);
        }

        [Fact]
        public void FindInText_MeasuredDevelopmentConfig_FindsConnectionMode()
        {
            IReadOnlyList<string> markers = DevBuildMarkers.FindInText(DEVELOPMENT_BOOT_CONFIG);

            Assert.Equal(new[] { "player-connection-mode=Listen" }, markers);
        }

        [Fact]
        public void FindInText_CleanConfig_FindsNothing()
        {
            Assert.Empty(DevBuildMarkers.FindInText(CLEAN_BOOT_CONFIG));
        }

        [Theory]
        [InlineData("player-connection-debug=1")]
        [InlineData("wait-for-native-debugger=1")]
        [InlineData("wait-for-managed-debugger=0")]
        [InlineData("wait-for-managed-debugger")]
        [InlineData("player-connection-mode=Connect\r")]
        public void FindInText_SingleMarkerLine_IsDetected(string line)
        {
            Assert.Single(DevBuildMarkers.FindInText(CLEAN_BOOT_CONFIG + line + "\n"));
        }

        [Fact]
        public void TryFind_DevelopmentBuildFolder_ReturnsMarkers()
        {
            WriteBootConfig("Shinshi_Data", DEVELOPMENT_BOOT_CONFIG);

            bool found = DevBuildMarkers.TryFind(_buildFolder, out IReadOnlyList<string> markers, out EPatchError error);

            Assert.True(found);
            Assert.Equal(EPatchError.None, error);
            Assert.NotEmpty(markers);
        }

        [Fact]
        public void TryFind_CleanBuildFolder_ReturnsEmptyMarkers()
        {
            WriteBootConfig("Shinshi_Data", CLEAN_BOOT_CONFIG);

            bool found = DevBuildMarkers.TryFind(_buildFolder, out IReadOnlyList<string> markers, out EPatchError error);

            Assert.True(found);
            Assert.Equal(EPatchError.None, error);
            Assert.Empty(markers);
        }

        [Fact]
        public void TryFind_NoBootConfig_ReturnsBootConfigMissing()
        {
            Directory.CreateDirectory(Path.Combine(_buildFolder, "Shinshi_Data"));

            bool found = DevBuildMarkers.TryFind(_buildFolder, out _, out EPatchError error);

            Assert.False(found);
            Assert.Equal(EPatchError.BootConfigMissing, error);
        }

        [Fact]
        public void TryFind_TwoBootConfigs_ReturnsBootConfigAmbiguous()
        {
            WriteBootConfig("Shinshi_Data", CLEAN_BOOT_CONFIG);
            WriteBootConfig("Other_Data", CLEAN_BOOT_CONFIG);

            bool found = DevBuildMarkers.TryFind(_buildFolder, out _, out EPatchError error);

            Assert.False(found);
            Assert.Equal(EPatchError.BootConfigAmbiguous, error);
        }

        private void WriteBootConfig(string dataFolderName, string text)
        {
            string folder = Path.Combine(_buildFolder, dataFolderName);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, DevBuildMarkers.BOOT_CONFIG_FILE_NAME), text);
        }
    }
}
