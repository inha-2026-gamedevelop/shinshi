// System
using System;
using System.IO;

// Third-party
using Xunit;

// Project
using Shinshi.Patch;

namespace Shinshi.Launcher.Tests.Core
{
    /// <summary> 매니페스트 경로 규칙 표, 대소문자 중복, root 아래 해석을 검사한다 </summary>
    public sealed class PatchPathsTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ROOT_NAME = "install-root";

        /****************************************
        *                Methods
        ****************************************/

        [Theory]
        // 허용
        [InlineData("Shinshi.exe", true)]
        [InlineData("Shinshi_Data/StreamingAssets/build-identity.json", true)]
        [InlineData("Shinshi_Data/Managed/Assembly-CSharp.dll", true)]
        [InlineData("MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll", true)]
        [InlineData("a b/c d.txt", true)]
        [InlineData(".hidden", true)]
        [InlineData("CONSOLE.txt", true)]
        [InlineData("COM10.txt", true)]
        [InlineData("LPT.dat", true)]
        [InlineData("Data/con1.txt", true)]
        // 빈 문자열, 앞뒤 공백
        [InlineData("", false)]
        [InlineData(" Shinshi.exe", false)]
        [InlineData("Shinshi.exe ", false)]
        [InlineData("\tShinshi.exe", false)]
        // 역슬래시, 절대 경로, 드라이브와 콜론
        [InlineData("Shinshi_Data\\boot.config", false)]
        [InlineData("/Shinshi.exe", false)]
        [InlineData("//server/share/a.txt", false)]
        [InlineData("C:/Windows/evil.dll", false)]
        [InlineData("C:evil.dll", false)]
        [InlineData("Shinshi.exe:Zone.Identifier", false)]
        // 점 세그먼트
        [InlineData("..", false)]
        [InlineData(".", false)]
        [InlineData("../evil.dll", false)]
        [InlineData("a/../../evil.dll", false)]
        [InlineData("a/./b.txt", false)]
        // 빈 세그먼트
        [InlineData("a//b", false)]
        [InlineData("a/", false)]
        // 세그먼트 끝 점, 공백
        [InlineData("a./b.txt", false)]
        [InlineData("a /b.txt", false)]
        [InlineData("a/b.", false)]
        // 제어 문자와 금지 문자
        [InlineData("a\u0001b.txt", false)]
        [InlineData("a\u007Fb.txt", false)]
        [InlineData("a<b.txt", false)]
        [InlineData("a>b.txt", false)]
        [InlineData("a\"b.txt", false)]
        [InlineData("a|b.txt", false)]
        [InlineData("a?b.txt", false)]
        [InlineData("a*b.txt", false)]
        // Windows 예약 이름, 확장자, 대소문자
        [InlineData("CON", false)]
        [InlineData("con.txt", false)]
        [InlineData("Data/PRN.log", false)]
        [InlineData("aux", false)]
        [InlineData("NUL.tar.gz", false)]
        [InlineData("COM1", false)]
        [InlineData("com9.dll", false)]
        [InlineData("LPT1", false)]
        [InlineData("lpt9.txt", false)]
        [InlineData("COM\u00B9.txt", false)]
        [InlineData("CON .txt", false)]
        public void IsValidRelativePath_RuleTable_ReturnsExpected(string path, bool expected)
        {
            Assert.Equal(expected, PatchPaths.IsValidRelativePath(path));
        }

        [Fact]
        public void IsValidRelativePath_Null_ReturnsFalse()
        {
            Assert.False(PatchPaths.IsValidRelativePath(null));
        }

        [Fact]
        public void IsValidRelativePath_TotalLengthLimit_AcceptsMaxAndRejectsOver()
        {
            string atLimit = new string('a', 200) + "/" + new string('b', PatchPaths.MAX_PATH_LENGTH - 201);
            string overLimit = atLimit + "c";

            Assert.Equal(PatchPaths.MAX_PATH_LENGTH, atLimit.Length);
            Assert.True(PatchPaths.IsValidRelativePath(atLimit));
            Assert.False(PatchPaths.IsValidRelativePath(overLimit));
        }

        [Fact]
        public void IsValidRelativePath_SegmentOverLimit_ReturnsFalse()
        {
            // 전체 길이 상한이 세그먼트 상한보다 작아 같이 걸리지만, 세그먼트 규칙도 독립적으로 있다
            string segment = new string('s', PatchPaths.MAX_SEGMENT_LENGTH + 1);

            Assert.False(PatchPaths.IsValidRelativePath(segment));
        }

        [Fact]
        public void TryValidateAll_CaseOnlyDuplicate_ReturnsDuplicatePath()
        {
            string[] paths = { "Shinshi_Data/Managed/Game.dll", "Shinshi.exe", "shinshi_data/managed/GAME.DLL" };

            bool valid = PatchPaths.TryValidateAll(paths, out EPatchError error);

            Assert.False(valid);
            Assert.Equal(EPatchError.DuplicatePath, error);
        }

        [Fact]
        public void TryValidateAll_OneInvalidPath_RejectsWholeList()
        {
            string[] paths = { "Shinshi.exe", "Shinshi_Data/ok.txt", "../escape.txt" };

            bool valid = PatchPaths.TryValidateAll(paths, out EPatchError error);

            Assert.False(valid);
            Assert.Equal(EPatchError.InvalidPath, error);
        }

        [Fact]
        public void TryValidateAll_DistinctValidPaths_ReturnsNone()
        {
            string[] paths = { "Shinshi.exe", "Shinshi_Data/StreamingAssets/build-identity.json", "UnityPlayer.dll" };

            bool valid = PatchPaths.TryValidateAll(paths, out EPatchError error);

            Assert.True(valid);
            Assert.Equal(EPatchError.None, error);
        }

        [Fact]
        public void TryResolveUnderRoot_ValidPath_ReturnsPathInsideRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), ROOT_NAME);

            bool resolved = PatchPaths.TryResolveUnderRoot(root, "Shinshi_Data/boot.config", out string? fullPath);

            Assert.True(resolved);
            string expected = Path.Combine(Path.GetFullPath(root), "Shinshi_Data", "boot.config");
            Assert.Equal(expected, fullPath);
        }

        [Fact]
        public void TryResolveUnderRoot_RootWithTrailingSeparator_ReturnsSamePath()
        {
            string root = Path.Combine(Path.GetTempPath(), ROOT_NAME);

            Assert.True(PatchPaths.TryResolveUnderRoot(root, "a/b.txt", out string? withoutSeparator));
            Assert.True(PatchPaths.TryResolveUnderRoot(root + Path.DirectorySeparatorChar, "a/b.txt", out string? withSeparator));

            Assert.Equal(withoutSeparator, withSeparator);
        }

        [Theory]
        [InlineData("../sibling/evil.dll")]
        [InlineData("C:/Windows/evil.dll")]
        [InlineData("a\\..\\..\\evil.dll")]
        [InlineData("")]
        public void TryResolveUnderRoot_EscapingPath_ReturnsFalse(string relative)
        {
            string root = Path.Combine(Path.GetTempPath(), ROOT_NAME);

            bool resolved = PatchPaths.TryResolveUnderRoot(root, relative, out string? fullPath);

            Assert.False(resolved);
            Assert.Null(fullPath);
        }

        [Fact]
        public void TryResolveUnderRoot_RootPrefixSibling_IsNotTreatedAsInside()
        {
            // "install-root2"가 "install-root" 접두어로 통과하지 않도록 구분자까지 비교한다
            string root = Path.Combine(Path.GetTempPath(), ROOT_NAME);

            Assert.True(PatchPaths.TryResolveUnderRoot(root, "x.txt", out string? fullPath));
            Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, fullPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
