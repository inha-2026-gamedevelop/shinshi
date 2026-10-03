// System
using System;
using System.Collections.Generic;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class ServerLaunchConfigTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string EXECUTABLE       = "Shinshi.exe";
        private const ushort TEST_PORT        = 7780;
        private const string TEST_PORT_TEXT   = "7780";
        private const string TEST_INSTANCE    = "3f2b6c1e-8a4d-4c2b-9e7f-1a2b3c4d5e6f";
        private const string TEST_BACKEND_URL = "https://localhost:5001/api/";
        private const string TEST_VERSION     = "0.1.0-test+1";
        private const int    TEST_POOL_SIZE   = 4;
        private const string TEST_POOL_TEXT   = "4";
        private const string PRIVATE_VALUE    = "private-value-for-test";
        private const int    TOO_LONG_LENGTH  = 129;
        private const string MUCHEOL_ID       = "boss_mucheol";
        private const string UNKNOWN_BOSS_ID  = "boss_unknown_for_test";

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryParse_AllArgs_ReadsAllWithoutEnvironment()
        {
            string[] args =
            {
                EXECUTABLE,
                Constants.Network.ARG_ROLE, Constants.Network.SERVER_ROLE_DUNGEON,
                Constants.Network.ARG_PORT, TEST_PORT_TEXT,
                Constants.Network.ARG_INSTANCE, TEST_INSTANCE,
                Constants.Network.ARG_BACKEND_BASE_URL, TEST_BACKEND_URL,
                Constants.Network.ARG_CONTENT_VERSION, TEST_VERSION,
                Constants.Network.ARG_DUNGEON_POOL_SIZE, TEST_POOL_TEXT
            };

            bool parsed = ServerLaunchConfig.TryParse(args, name => throw new InvalidOperationException("환경 변수에 접근하면 안 된다"),
                out ServerLaunchConfig config, out string error);

            Assert.IsTrue(parsed, error);
            Assert.AreEqual(EServerRole.Dungeon, config.Role);
            Assert.AreEqual(TEST_PORT, config.Port);
            Assert.AreEqual(TEST_INSTANCE, config.InstanceId);
            Assert.AreEqual(TEST_BACKEND_URL, config.BackendBaseUrl);
            Assert.AreEqual(TEST_VERSION, config.ContentVersion);
            Assert.AreEqual(TEST_POOL_SIZE, config.DungeonPoolSize);
        }

        [Test]
        public void TryParse_NoRole_ReturnsFalseWithoutError()
        {
            bool parsed = ServerLaunchConfig.TryParse(new[] { EXECUTABLE }, null, out _, out string error);

            Assert.IsFalse(parsed);
            Assert.IsNull(error);
        }

        [Test]
        public void TryParse_AllEnvironment_UsesDefaultPortAndInstance()
        {
            Dictionary<string, string> environment = ValidEnvironment();
            bool                       parsed      = Parse(new[] { EXECUTABLE }, environment, out ServerLaunchConfig config, out string error);

            Assert.IsTrue(parsed, error);
            Assert.AreEqual(EServerRole.Town, config.Role);
            Assert.AreEqual(Constants.Network.DEFAULT_GAME_PORT, config.Port);
            Assert.AreEqual(SessionContractIds.CreateDefaultInstanceId(Constants.Network.SERVER_ROLE_TOWN, Constants.Network.DEFAULT_GAME_PORT), config.InstanceId);
            Assert.AreEqual(TEST_BACKEND_URL, config.BackendBaseUrl);
            Assert.AreEqual(TEST_VERSION, config.ContentVersion);
            Assert.AreEqual(TEST_POOL_SIZE, config.DungeonPoolSize);
        }

        [Test]
        public void TryParse_ArgsAndEnvironment_ArgsWinForEverySetting()
        {
            Dictionary<string, string> environment = ValidEnvironment();
            foreach (string name in new List<string>(environment.Keys))
            {
                environment[name] = PRIVATE_VALUE;
            }
            environment[Constants.Network.ENV_GAME_PORT]   = PRIVATE_VALUE;
            environment[Constants.Network.ENV_INSTANCE_ID] = PRIVATE_VALUE;

            string[] args =
            {
                EXECUTABLE,
                Constants.Network.ARG_ROLE, Constants.Network.SERVER_ROLE_DUNGEON,
                Constants.Network.ARG_PORT, TEST_PORT_TEXT,
                Constants.Network.ARG_INSTANCE, TEST_INSTANCE,
                Constants.Network.ARG_BACKEND_BASE_URL, TEST_BACKEND_URL,
                Constants.Network.ARG_CONTENT_VERSION, TEST_VERSION,
                Constants.Network.ARG_DUNGEON_POOL_SIZE, TEST_POOL_TEXT
            };

            Assert.IsTrue(Parse(args, environment, out ServerLaunchConfig config, out string error), error);
            Assert.AreEqual(EServerRole.Dungeon, config.Role);
            Assert.AreEqual(TEST_PORT, config.Port);
            Assert.AreEqual(TEST_INSTANCE, config.InstanceId);
            Assert.AreEqual(TEST_BACKEND_URL, config.BackendBaseUrl);
            Assert.AreEqual(TEST_VERSION, config.ContentVersion);
            Assert.AreEqual(TEST_POOL_SIZE, config.DungeonPoolSize);
        }

        [TestCase(Constants.Network.ENV_BACKEND_BASE_URL)]
        [TestCase(Constants.Network.ENV_CONTENT_VERSION)]
        [TestCase(Constants.Network.ENV_DUNGEON_POOL_SIZE)]
        public void TryParse_MissingRequiredEnvironment_ReturnsNamedError(string name)
        {
            Dictionary<string, string> environment = ValidEnvironment();
            environment.Remove(name);

            Assert.IsFalse(Parse(null, environment, out ServerLaunchConfig config, out string error));
            StringAssert.Contains(name, error);
            Assert.IsNull(config.ContentVersion);
        }

        [TestCase(Constants.Network.ENV_BACKEND_BASE_URL)]
        [TestCase(Constants.Network.ENV_CONTENT_VERSION)]
        [TestCase(Constants.Network.ENV_DUNGEON_POOL_SIZE)]
        public void TryParse_WhitespaceRequiredEnvironment_ReturnsNamedError(string name)
        {
            Dictionary<string, string> environment = ValidEnvironment();
            environment[name] = " \t";

            Assert.IsFalse(Parse(null, environment, out _, out string error));
            StringAssert.Contains(name, error);
        }

        [TestCase(Constants.Network.ARG_ROLE)]
        [TestCase(Constants.Network.ARG_PORT)]
        [TestCase(Constants.Network.ARG_INSTANCE)]
        [TestCase(Constants.Network.ARG_BACKEND_BASE_URL)]
        [TestCase(Constants.Network.ARG_CONTENT_VERSION)]
        [TestCase(Constants.Network.ARG_DUNGEON_POOL_SIZE)]
        public void TryParse_ArgWithoutValue_DoesNotFallBackToEnvironment(string arg)
        {
            Assert.IsFalse(Parse(new[] { EXECUTABLE, arg }, ValidEnvironment(), out _, out string error));
            StringAssert.Contains(arg, error);
        }

        [TestCase(Constants.Network.ARG_BACKEND_BASE_URL)]
        [TestCase(Constants.Network.ARG_CONTENT_VERSION)]
        [TestCase(Constants.Network.ARG_DUNGEON_POOL_SIZE)]
        public void TryParse_ArgFollowedByAnotherFlag_ReturnsError(string arg)
        {
            string[] args = { EXECUTABLE, arg, "-logFile", "player.log" };

            Assert.IsFalse(Parse(args, ValidEnvironment(), out _, out string error));
            StringAssert.Contains(arg, error);
        }

        [TestCase(Constants.Network.ARG_ROLE, Constants.Network.SERVER_ROLE_TOWN)]
        [TestCase(Constants.Network.ARG_PORT, TEST_PORT_TEXT)]
        [TestCase(Constants.Network.ARG_INSTANCE, TEST_INSTANCE)]
        [TestCase(Constants.Network.ARG_BACKEND_BASE_URL, TEST_BACKEND_URL)]
        [TestCase(Constants.Network.ARG_CONTENT_VERSION, TEST_VERSION)]
        [TestCase(Constants.Network.ARG_DUNGEON_POOL_SIZE, TEST_POOL_TEXT)]
        public void TryParse_DuplicateArg_ReturnsError(string arg, string value)
        {
            string[] args = { EXECUTABLE, arg, value, arg.ToUpperInvariant(), value };

            Assert.IsFalse(Parse(args, ValidEnvironment(), out _, out string error));
            StringAssert.Contains(arg, error);
        }

        [TestCase("abc")]
        [TestCase("0")]
        [TestCase("70000")]
        [TestCase("-1")]
        [TestCase(" 7780")]
        public void TryParse_InvalidPort_ReturnsNamedError(string port)
        {
            Assert.IsFalse(Parse(new[] { Constants.Network.ARG_PORT, port }, ValidEnvironment(), out _, out string error));
            StringAssert.Contains(Constants.Network.ENV_GAME_PORT, error);
        }

        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("1.5")]
        [TestCase("2147483648")]
        [TestCase(" 4")]
        [TestCase(PRIVATE_VALUE)]
        public void TryParse_InvalidPoolSize_ReturnsSafeError(string poolSize)
        {
            Assert.IsFalse(Parse(new[] { Constants.Network.ARG_DUNGEON_POOL_SIZE, poolSize }, ValidEnvironment(), out _, out string error));
            StringAssert.Contains(Constants.Network.ENV_DUNGEON_POOL_SIZE, error);
            StringAssert.DoesNotContain(PRIVATE_VALUE, error);
        }

        [TestCase("https://localhost:5001/api/")]
        [TestCase("http://127.0.0.1:5000")]
        [TestCase("https://[::1]:5001/")]
        public void TryParse_AbsoluteHttpUrl_AcceptsUrl(string url)
        {
            Assert.IsTrue(Parse(new[] { Constants.Network.ARG_BACKEND_BASE_URL, url }, ValidEnvironment(), out _, out string error), error);
        }

        [TestCase("/relative")]
        [TestCase("ftp://localhost/")]
        [TestCase("https://private-value-for-test@localhost/")]
        [TestCase("https://localhost/?token=private-value-for-test")]
        [TestCase("https://localhost/#private-value-for-test")]
        [TestCase("https://localhost/path with space")]
        [TestCase("https://localhost/\nprivate-value-for-test")]
        public void TryParse_InvalidBackendUrl_ReturnsSafeError(string url)
        {
            Assert.IsFalse(Parse(new[] { Constants.Network.ARG_BACKEND_BASE_URL, url }, ValidEnvironment(), out _, out string error));
            StringAssert.Contains(Constants.Network.ENV_BACKEND_BASE_URL, error);
            StringAssert.DoesNotContain(PRIVATE_VALUE, error);
        }

        [TestCase("version with space")]
        [TestCase("0.1.0\nprivate-value-for-test")]
        [TestCase("..")]
        [TestCase("+version")]
        public void TryParse_InvalidContentVersion_ReturnsSafeError(string version)
        {
            Assert.IsFalse(Parse(new[] { Constants.Network.ARG_CONTENT_VERSION, version }, ValidEnvironment(), out _, out string error));
            StringAssert.Contains(Constants.Network.ENV_CONTENT_VERSION, error);
            StringAssert.DoesNotContain(PRIVATE_VALUE, error);
        }

        [Test]
        public void TryParse_VersionTooLong_ReturnsError()
        {
            Assert.IsFalse(Parse(new[] { Constants.Network.ARG_CONTENT_VERSION, new string('v', TOO_LONG_LENGTH) },
                ValidEnvironment(), out _, out string error));
            StringAssert.Contains(Constants.Network.ENV_CONTENT_VERSION, error);
        }

        [Test]
        public void TryParse_InstanceContainsNewline_ReturnsSafeError()
        {
            Assert.IsFalse(Parse(new[] { Constants.Network.ARG_INSTANCE, "instance\n" + PRIVATE_VALUE },
                ValidEnvironment(), out _, out string error));
            StringAssert.Contains(Constants.Network.ENV_INSTANCE_ID, error);
            StringAssert.DoesNotContain(PRIVATE_VALUE, error);
        }

        [Test]
        public void TryParse_UnknownRole_ReturnsSafeError()
        {
            Assert.IsFalse(Parse(new[] { Constants.Network.ARG_ROLE, PRIVATE_VALUE }, ValidEnvironment(), out _, out string error));
            StringAssert.Contains(Constants.Network.ENV_SERVER_ROLE, error);
            StringAssert.DoesNotContain(PRIVATE_VALUE, error);
        }

        // N6 회귀: 출시 빌드는 역할과 포트로 만든 기본 인스턴스 ID를 쓰지 않는다. 다른 호스트의 같은 포트와 겹친다
        [Test]
        public void TryParse_ReleaseWithoutInstance_ReturnsNamedError()
        {
            Dictionary<string, string> environment = ValidEnvironment();
            bool                       parsed      = ServerLaunchConfig.TryParse(new[] { EXECUTABLE }, Reader(environment), false, out _, out string error);

            Assert.IsFalse(parsed);
            StringAssert.Contains(Constants.Network.ENV_INSTANCE_ID, error);
        }

        [Test]
        public void TryParse_ReleaseWithInstance_Accepts()
        {
            Dictionary<string, string> environment = ValidEnvironment();
            environment[Constants.Network.ENV_INSTANCE_ID] = TEST_INSTANCE;
            bool parsed = ServerLaunchConfig.TryParse(new[] { EXECUTABLE }, Reader(environment), false,
                out ServerLaunchConfig config, out string error);

            Assert.IsTrue(parsed, error);
            Assert.AreEqual(TEST_INSTANCE, config.InstanceId);
        }

        [Test]
        public void TryParse_DevelopmentWithoutInstance_UsesDefaultInstance()
        {
            bool parsed = ServerLaunchConfig.TryParse(new[] { EXECUTABLE }, Reader(ValidEnvironment()), true,
                out ServerLaunchConfig config, out string error);

            Assert.IsTrue(parsed, error);
            Assert.AreEqual(SessionContractIds.CreateDefaultInstanceId(Constants.Network.SERVER_ROLE_TOWN,
                Constants.Network.DEFAULT_GAME_PORT), config.InstanceId);
        }

        [Test]
        public void TryReadBossId_Missing_UsesDevBoss()
        {
            bool read = ServerLaunchConfig.TryReadBossId(new[] { EXECUTABLE }, Reader(ValidEnvironment()), IsTestBoss,
                out string bossId, out string error);

            Assert.IsTrue(read, error);
            Assert.AreEqual(Constants.Combat.DEV_BOSS_ID, bossId);
        }

        [Test]
        public void TryReadBossId_ArgOverridesEnvironment()
        {
            Dictionary<string, string> environment = ValidEnvironment();
            environment[Constants.Network.ENV_BOSS_ID] = Constants.Combat.DEV_BOSS_ID;

            bool read = ServerLaunchConfig.TryReadBossId(new[] { EXECUTABLE, Constants.Network.ARG_BOSS_ID, MUCHEOL_ID },
                Reader(environment), IsTestBoss, out string bossId, out string error);

            Assert.IsTrue(read, error);
            Assert.AreEqual(MUCHEOL_ID, bossId);
        }

        [Test]
        public void TryReadBossId_UnknownBoss_FailsWithoutEchoingValue()
        {
            bool read = ServerLaunchConfig.TryReadBossId(new[] { EXECUTABLE, Constants.Network.ARG_BOSS_ID, UNKNOWN_BOSS_ID },
                Reader(ValidEnvironment()), IsTestBoss, out string bossId, out string error);

            Assert.IsFalse(read);
            Assert.IsNull(bossId);
            StringAssert.Contains(Constants.Network.ARG_BOSS_ID, error);
            StringAssert.DoesNotContain(UNKNOWN_BOSS_ID, error, "오류에 입력 값을 넣지 않는다");
        }

        /****************************************
        *                Methods
        ****************************************/

        private static bool IsTestBoss(string bossId)
        {
            return (bossId == Constants.Combat.DEV_BOSS_ID) || (bossId == MUCHEOL_ID);
        }

        private static Func<string, string> Reader(Dictionary<string, string> environment)
        {
            return name => environment.TryGetValue(name, out string value) ? value : null;
        }

        private static Dictionary<string, string> ValidEnvironment()
        {
            return new Dictionary<string, string>
            {
                { Constants.Network.ENV_SERVER_ROLE, Constants.Network.SERVER_ROLE_TOWN },
                { Constants.Network.ENV_BACKEND_BASE_URL, TEST_BACKEND_URL },
                { Constants.Network.ENV_CONTENT_VERSION, TEST_VERSION },
                { Constants.Network.ENV_DUNGEON_POOL_SIZE, TEST_POOL_TEXT }
            };
        }

        private static bool Parse(string[] args, Dictionary<string, string> environment,
            out ServerLaunchConfig config, out string error)
        {
            return ServerLaunchConfig.TryParse(args, name => environment.TryGetValue(name, out string value) ? value : null,
                out config, out error);
        }
    }
}
