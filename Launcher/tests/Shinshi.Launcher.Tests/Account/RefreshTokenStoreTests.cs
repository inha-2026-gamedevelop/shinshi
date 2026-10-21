// System
using System;
using System.IO;
using System.Text;

// Third-party
using Xunit;

// Project
using Shinshi.Launcher.Account;

namespace Shinshi.Launcher.Tests.Account
{
    /// <summary> DPAPI 갱신 토큰 파일의 저장, 읽기, 지우기 </summary>
    public sealed class RefreshTokenStoreTests : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TOKEN       = "refresh-token-plaintext-0123456789";
        private const string OTHER_TOKEN = "refresh-token-rotated-abcdef";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "shinshi-launcher-tests", Guid.NewGuid().ToString("N"));

        /****************************************
        *                Methods
        ****************************************/

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        [Fact]
        public void TrySave_ThenTryLoad_RoundTrips()
        {
            RefreshTokenStore store = CreateStore();

            Assert.True(store.TrySave(TOKEN));

            Assert.True(store.HasToken);
            Assert.True(new RefreshTokenStore(store.FilePath).TryLoad(out string? loaded));
            Assert.Equal(TOKEN, loaded);
        }

        [Fact]
        public void TryPrepareRefresh_Restart_PreservesRequestUntilRotatedTokenIsSaved()
        {
            RefreshTokenStore store = CreateStore();
            Assert.True(store.TrySave(TOKEN));
            Assert.True(store.TryPrepareRefresh(TOKEN, out string first));
            RefreshTokenStore restarted = new RefreshTokenStore(store.FilePath);
            Assert.True(restarted.TryLoad(out string? token));
            Assert.Equal(TOKEN, token);
            Assert.True(restarted.TryPrepareRefresh(TOKEN, out string retry));
            Assert.Equal(first, retry);
            Assert.False(Contains(File.ReadAllBytes(store.FilePath), Encoding.UTF8.GetBytes(first)));
            Assert.True(restarted.TrySave(OTHER_TOKEN));
            Assert.True(restarted.TryPrepareRefresh(OTHER_TOKEN, out string next));
            Assert.NotEqual(first, next);
        }

        [Fact]
        public void TrySave_FileDoesNotContainPlaintext()
        {
            RefreshTokenStore store = CreateStore();

            Assert.True(store.TrySave(TOKEN));

            byte[] file = File.ReadAllBytes(store.FilePath);
            Assert.False(Contains(file, Encoding.UTF8.GetBytes(TOKEN)));
            Assert.False(Contains(file, Encoding.Unicode.GetBytes(TOKEN)));
            Assert.False(File.Exists(store.FilePath + ".tmp"));
        }

        [Fact]
        public void TrySave_Twice_KeepsOnlyLatest()
        {
            RefreshTokenStore store = CreateStore();

            Assert.True(store.TrySave(TOKEN));
            Assert.True(store.TrySave(OTHER_TOKEN));

            Assert.True(store.TryLoad(out string? loaded));
            Assert.Equal(OTHER_TOKEN, loaded);
        }

        [Fact]
        public void Clear_RemovesFile()
        {
            RefreshTokenStore store = CreateStore();
            Assert.True(store.TrySave(TOKEN));

            store.Clear();

            Assert.False(store.HasToken);
            Assert.False(store.TryLoad(out string? _));
        }

        [Fact]
        public void TryLoad_Missing_ReturnsFalse()
        {
            RefreshTokenStore store = CreateStore();

            Assert.False(store.TryLoad(out string? loaded));
            Assert.Null(loaded);
        }

        [Fact]
        public void TryLoad_CorruptFile_ReturnsFalseAndDeletes()
        {
            RefreshTokenStore store = CreateStore();
            Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
            File.WriteAllBytes(store.FilePath, Encoding.UTF8.GetBytes(TOKEN));

            Assert.False(store.TryLoad(out string? loaded));

            Assert.Null(loaded);
            Assert.False(File.Exists(store.FilePath));
        }

        private RefreshTokenStore CreateStore()
        {
            return new RefreshTokenStore(Path.Combine(_root, "Account", RefreshTokenStore.FILE_NAME));
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; ++i)
            {
                if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
