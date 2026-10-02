// System
using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

// Third Party
using Xunit;

// Project
using Shinshi.AdminCli;

namespace Shinshi.Backend.Tests.AdminCli
{
    /// <summary> 세션 파일 저장, 읽기, 권한, 삭제 </summary>
    public sealed class AdminSessionStoreTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TOKEN = "test-token-value";

        /****************************************
        *                Methods
        ****************************************/

        [Fact]
        public void Save_ThenLoad_RoundTripsAndRestrictsToCurrentUser()
        {
            using CliHarness harness = new CliHarness(() => throw new InvalidOperationException("No HTTP expected."));
            DateTimeOffset expires = DateTimeOffset.UtcNow.AddMinutes(30);
            harness.SaveSession(TOKEN, expires);
            harness.SaveSession(TOKEN, expires);

            AdminSession? loaded = harness.Store.Load();

            Assert.NotNull(loaded);
            Assert.Equal(CliHarness.API, loaded!.Api);
            Assert.Equal(TOKEN, loaded.AccessToken);
            Assert.Equal(expires.ToUnixTimeMilliseconds(), loaded.ExpiresAtUnixMs);
            Assert.Single(Directory.GetFiles(harness.DirectoryPath));
            if (OperatingSystem.IsWindows())
            {
                AssertWindowsOwnerOnly(harness.Store.FilePath);
            }
            else
            {
                AssertUnixOwnerOnly(harness.Store.FilePath);
            }
        }

        [Fact]
        public void Load_MissingOrCorruptFile_ReturnsNull()
        {
            using CliHarness harness = new CliHarness(() => throw new InvalidOperationException("No HTTP expected."));
            Assert.Null(harness.Store.Load());

            Directory.CreateDirectory(harness.DirectoryPath);
            File.WriteAllText(harness.Store.FilePath, "{not json");
            Assert.Null(harness.Store.Load());

            File.WriteAllText(harness.Store.FilePath, "{\"api\":\"http://localhost\"}");
            Assert.Null(harness.Store.Load());
        }

        [Fact]
        public void Delete_ExistingFile_RemovesIt()
        {
            using CliHarness harness = new CliHarness(() => throw new InvalidOperationException("No HTTP expected."));
            harness.SaveSession(TOKEN, DateTimeOffset.UtcNow.AddMinutes(1));

            Assert.True(harness.Store.Delete());
            Assert.False(File.Exists(harness.Store.FilePath));
            Assert.False(harness.Store.Delete());
        }

        [SupportedOSPlatform("windows")]
        private static void AssertWindowsOwnerOnly(string path)
        {
            FileSecurity security = new FileInfo(path).GetAccessControl();
            AuthorizationRuleCollection rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
            Assert.True(security.AreAccessRulesProtected);
            FileSystemAccessRule rule = Assert.IsType<FileSystemAccessRule>(Assert.Single(rules));
            Assert.Equal(WindowsIdentity.GetCurrent().User, rule.IdentityReference);
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        }

        [UnsupportedOSPlatform("windows")]
        private static void AssertUnixOwnerOnly(string path)
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }
}
