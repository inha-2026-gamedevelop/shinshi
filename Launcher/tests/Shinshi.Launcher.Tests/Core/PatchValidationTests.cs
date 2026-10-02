// System
using System;

// Third-party
using Xunit;

// Project
using Shinshi.Patch;

namespace Shinshi.Launcher.Tests.Core
{
    /// <summary> releaseId, 런처 버전 형식과 비교 규칙 </summary>
    public sealed class PatchValidationTests
    {
        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        [InlineData("2026.10.02-1", true)]
        [InlineData("a", true)]
        [InlineData("A_b-c.d", true)]
        [InlineData(".a", true)]
        [InlineData("...", false)]
        [InlineData("a.", false)]
        [InlineData(".", false)]
        [InlineData("..", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("a b", false)]
        [InlineData("a/b", false)]
        [InlineData("a\\b", false)]
        [InlineData("\uD55C\uAE00", false)]
        public void IsValidReleaseId_Input_ReturnsExpected(string? releaseId, bool expected)
        {
            Assert.Equal(expected, PatchValidation.IsValidReleaseId(releaseId));
        }

        [Fact]
        public void IsValidReleaseId_LengthLimit_AcceptsMaxAndRejectsOver()
        {
            Assert.True(PatchValidation.IsValidReleaseId(new string('r', PatchValidation.MAX_RELEASE_ID_LENGTH)));
            Assert.False(PatchValidation.IsValidReleaseId(new string('r', PatchValidation.MAX_RELEASE_ID_LENGTH + 1)));
        }

        [Theory]
        [InlineData("1.0.0", true)]
        [InlineData("0.0.0", true)]
        [InlineData("10.20.30", true)]
        [InlineData("1.0", false)]
        [InlineData("1.0.0.0", false)]
        [InlineData("01.0.0", false)]
        [InlineData("1.-1.0", false)]
        [InlineData("1.0.0 ", false)]
        [InlineData("v1.0.0", false)]
        [InlineData("9999999999.0.0", false)]
        [InlineData("", false)]
        public void TryParseLauncherVersion_Input_ReturnsExpected(string text, bool expected)
        {
            Assert.Equal(expected, PatchValidation.TryParseLauncherVersion(text, out _));
        }

        [Theory]
        [InlineData("1.2.3", "1.2.3", true)]
        [InlineData("1.2.3", "1.2.4", true)]
        [InlineData("1.2.3", "1.10.0", true)]
        [InlineData("1.2.3", "1.2.2", false)]
        [InlineData("1.2.0", "1.2", true)]
        [InlineData("bad", "9.9.9", false)]
        public void IsLauncherVersionSufficient_Compared_ReturnsExpected(string minimum, string current, bool expected)
        {
            Assert.Equal(expected, PatchValidation.IsLauncherVersionSufficient(minimum, Version.Parse(current)));
        }
    }
}
