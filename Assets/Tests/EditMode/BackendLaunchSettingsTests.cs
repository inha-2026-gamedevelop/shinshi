// System
using System.Collections.Generic;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class BackendLaunchSettingsTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TOKEN      = "svc-token-abc";
        private const string FILE_PATH  = "/run/secrets/backend";
        private const string FILE_TOKEN = "svc-token-from-file";

        private readonly Dictionary<string, string> _environment = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _files       = new Dictionary<string, string>();

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _environment.Clear();
            _files.Clear();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryParse_ReleaseWithoutToken_Fails()
        {
            Assert.IsFalse(Parse(new string[0], false, out _, out string error));
            StringAssert.Contains(Constants.Network.ENV_BACKEND_SERVICE_TOKEN, error);
        }

        [Test]
        public void TryParse_ReleaseDefault_UsesApiVerifier()
        {
            _environment[Constants.Network.ENV_BACKEND_SERVICE_TOKEN] = TOKEN;

            Assert.IsTrue(Parse(new string[0], false, out BackendLaunchSettings settings, out _));
            Assert.IsTrue(settings.UseApiVerifier);
            Assert.AreEqual(TOKEN, settings.ServiceToken);
        }

        [Test]
        public void TryParse_ReleaseAskingDevVerifier_Fails()
        {
            _environment[Constants.Network.ENV_BACKEND_SERVICE_TOKEN] = TOKEN;
            string[] args = { Constants.Network.ARG_JOIN_TOKEN_VERIFIER, Constants.Network.JOIN_TOKEN_VERIFIER_DEV };

            Assert.IsFalse(Parse(args, false, out _, out _));
        }

        [Test]
        public void TryParse_DevDefault_UsesDevVerifierWithoutToken()
        {
            Assert.IsTrue(Parse(new string[0], true, out BackendLaunchSettings settings, out _));
            Assert.IsFalse(settings.UseApiVerifier);
            Assert.IsFalse(settings.HasServiceToken);
        }

        [Test]
        public void TryParse_DevSelectingApi_RequiresToken()
        {
            _environment[Constants.Network.ENV_JOIN_TOKEN_VERIFIER] = Constants.Network.JOIN_TOKEN_VERIFIER_API;
            Assert.IsFalse(Parse(new string[0], true, out _, out _));

            _environment[Constants.Network.ENV_BACKEND_SERVICE_TOKEN] = TOKEN;
            Assert.IsTrue(Parse(new string[0], true, out BackendLaunchSettings settings, out _));
            Assert.IsTrue(settings.UseApiVerifier);
        }

        [Test]
        public void TryParse_TokenFileArgument_OverridesEnvironmentAndTrimsNewline()
        {
            _environment[Constants.Network.ENV_BACKEND_SERVICE_TOKEN] = TOKEN;
            _files[FILE_PATH]                                         = FILE_TOKEN + "\n";
            string[] args = { Constants.Network.ARG_BACKEND_SERVICE_TOKEN_FILE, FILE_PATH };

            Assert.IsTrue(Parse(args, false, out BackendLaunchSettings settings, out _));
            Assert.AreEqual(FILE_TOKEN, settings.ServiceToken);
        }

        [Test]
        public void TryParse_UnsafeToken_FailsWithoutEchoingValue()
        {
            const string BAD_TOKEN = "abc\r\nX-Injected: 1";
            _environment[Constants.Network.ENV_BACKEND_SERVICE_TOKEN] = BAD_TOKEN;

            Assert.IsFalse(Parse(new string[0], false, out _, out string error));
            StringAssert.DoesNotContain("abc", error);
            StringAssert.DoesNotContain("Injected", error);
        }

        [Test]
        public void IsBaseUrlAllowed_Release_RequiresHttpsUnlessLoopback()
        {
            Assert.IsTrue(BackendLaunchSettings.IsBaseUrlAllowed("https://api.example.com/", false));
            Assert.IsTrue(BackendLaunchSettings.IsBaseUrlAllowed("http://127.0.0.1:5000/", false));
            Assert.IsTrue(BackendLaunchSettings.IsBaseUrlAllowed("http://localhost:5000/", false));
            Assert.IsFalse(BackendLaunchSettings.IsBaseUrlAllowed("http://api.example.com/", false));
            Assert.IsTrue(BackendLaunchSettings.IsBaseUrlAllowed("http://192.168.0.10:5000/", true));
            Assert.IsFalse(BackendLaunchSettings.IsBaseUrlAllowed("ftp://api.example.com/", true));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private bool Parse(string[] args, bool allowDev, out BackendLaunchSettings settings, out string error)
        {
            return BackendLaunchSettings.TryParse(args, ReadEnvironment, ReadFile, allowDev, out settings, out error);
        }

        private string ReadEnvironment(string name)
        {
            return _environment.TryGetValue(name, out string value) ? value : null;
        }

        private string ReadFile(string path)
        {
            return _files.TryGetValue(path, out string value) ? value : null;
        }
    }
}
