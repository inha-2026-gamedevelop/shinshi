// System
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

        private const string EXECUTABLE     = "Shinshi.exe";
        private const ushort TEST_PORT      = 7780;
        private const string TEST_PORT_TEXT = "7780";
        private const string TEST_INSTANCE  = "dungeon-01";

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryParse_ArgsWithRolePortInstance_ReadsAll()
        {
            string[] args =
            {
                EXECUTABLE,
                Constants.Network.ARG_ROLE, Constants.Network.SERVER_ROLE_DUNGEON,
                Constants.Network.ARG_PORT, TEST_PORT_TEXT,
                Constants.Network.ARG_INSTANCE, TEST_INSTANCE
            };

            bool parsed = ServerLaunchConfig.TryParse(args, NoEnvironment, out ServerLaunchConfig config, out string error);

            Assert.IsTrue(parsed, error);
            Assert.AreEqual(EServerRole.Dungeon, config.Role);
            Assert.AreEqual(TEST_PORT, config.Port);
            Assert.AreEqual(TEST_INSTANCE, config.InstanceId);
        }

        [Test]
        public void TryParse_NoRole_ReturnsFalseWithoutError()
        {
            string[] args = { EXECUTABLE };

            bool parsed = ServerLaunchConfig.TryParse(args, NoEnvironment, out _, out string error);

            Assert.IsFalse(parsed);
            Assert.IsNull(error);
        }

        [Test]
        public void TryParse_EnvironmentRoleOnly_UsesDefaultPortAndInstance()
        {
            var environment = new Dictionary<string, string>
            {
                { Constants.Network.ENV_SERVER_ROLE, Constants.Network.SERVER_ROLE_TOWN }
            };

            bool parsed = ServerLaunchConfig.TryParse(new[] { EXECUTABLE }, name => Read(environment, name),
                out ServerLaunchConfig config, out string error);

            Assert.IsTrue(parsed, error);
            Assert.AreEqual(EServerRole.Town, config.Role);
            Assert.AreEqual(Constants.Network.DEFAULT_GAME_PORT, config.Port);
            Assert.AreEqual($"{Constants.Network.SERVER_ROLE_TOWN}-{Constants.Network.DEFAULT_GAME_PORT}", config.InstanceId);
        }

        [Test]
        public void TryParse_ArgsAndEnvironment_ArgsWin()
        {
            var environment = new Dictionary<string, string>
            {
                { Constants.Network.ENV_SERVER_ROLE, Constants.Network.SERVER_ROLE_TOWN }
            };
            string[] args = { EXECUTABLE, Constants.Network.ARG_ROLE, Constants.Network.SERVER_ROLE_DUNGEON };

            bool parsed = ServerLaunchConfig.TryParse(args, name => Read(environment, name), out ServerLaunchConfig config, out string error);

            Assert.IsTrue(parsed, error);
            Assert.AreEqual(EServerRole.Dungeon, config.Role);
        }

        [Test]
        public void TryParse_UnknownRole_ReturnsError()
        {
            string[] args = { EXECUTABLE, Constants.Network.ARG_ROLE, "lobby" };

            bool parsed = ServerLaunchConfig.TryParse(args, NoEnvironment, out _, out string error);

            Assert.IsFalse(parsed);
            Assert.IsNotNull(error);
        }

        [TestCase("abc")]
        [TestCase("0")]
        [TestCase("70000")]
        public void TryParse_InvalidPort_ReturnsError(string port)
        {
            string[] args = { EXECUTABLE, Constants.Network.ARG_ROLE, Constants.Network.SERVER_ROLE_TOWN, Constants.Network.ARG_PORT, port };

            bool parsed = ServerLaunchConfig.TryParse(args, NoEnvironment, out _, out string error);

            Assert.IsFalse(parsed);
            Assert.IsNotNull(error);
        }

        /****************************************
        *                Methods
        ****************************************/

        private static string NoEnvironment(string name)
        {
            return null;
        }

        private static string Read(Dictionary<string, string> environment, string name)
        {
            return environment.TryGetValue(name, out string value) ? value : null;
        }
    }
}
