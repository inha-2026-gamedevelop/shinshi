// System
using System.IO;
using System.Threading.Tasks;

// Third-party
using Xunit;

// Project
using Shinshi.Patch;

namespace Shinshi.Launcher.Tests.Core
{
    /// <summary> SHA-256 소문자 hex 계산과 형식 검사 </summary>
    public sealed class PatchHashTests
    {
        /****************************************
        *                Fields
        ****************************************/

        // FIPS 180-2 예제 "abc"
        private const string ABC_SHA256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        private const int    LARGE_FILE_BYTES = 3 * 1024 * 1024 + 7;

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void Compute_KnownVector_ReturnsLowercaseHex()
        {
            Assert.Equal(ABC_SHA256, PatchHash.Compute(PatchTestFactory.Utf8("abc")));
        }

        [Fact]
        public async Task ComputeFile_LargeFile_MatchesInMemoryHash()
        {
            string folder = PatchTestFactory.CreateTempDirectory();
            try
            {
                byte[] data = new byte[LARGE_FILE_BYTES];
                for (int i = 0; i < data.Length; ++i)
                {
                    data[i] = (byte)i;
                }

                string path = Path.Combine(folder, "large.bin");
                await File.WriteAllBytesAsync(path, data);

                string expected = PatchHash.Compute(data);
                Assert.Equal(expected, PatchHash.ComputeFile(path));
                Assert.Equal(expected, await PatchHash.ComputeFileAsync(path));
                using MemoryStream stream = new MemoryStream(data);
                Assert.Equal(expected, PatchHash.Compute(stream));
            }
            finally
            {
                PatchTestFactory.DeleteDirectory(folder);
            }
        }

        [Theory]
        [InlineData(ABC_SHA256, true)]
        [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", false)]
        [InlineData("ba7816bf", false)]
        [InlineData("ga7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsSha256Hex_Input_ReturnsExpected(string? value, bool expected)
        {
            Assert.Equal(expected, PatchHash.IsSha256Hex(value));
        }
    }
}
