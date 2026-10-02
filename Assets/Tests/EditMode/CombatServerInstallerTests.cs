// System
using System;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Data;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class CombatServerInstallerTests
    {
        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Install_InitializeFails_LeavesNothingInstalled()
        {
            Assert.IsFalse(CombatServerInstaller.IsInstalled);
            var sessionConfig = ScriptableObject.CreateInstance<SessionConfigSO>();
            try
            {
                // 플레이어 설정이 없어 런타임 초기화가 개체를 만든 뒤에 실패한다
                Assert.Throws<ArgumentNullException>(() =>
                    CombatServerInstaller.Install(null, sessionConfig, new FakeRoster()));
            }
            finally
            {
                Object.DestroyImmediate(sessionConfig);
            }

            Assert.IsFalse(CombatServerInstaller.IsInstalled);
            Assert.IsNull(CombatServerInstaller.Runtime);
            Assert.AreEqual(0, Object.FindObjectsByType<CombatServerRuntime>().Length);
        }

        [Test]
        public void Install_MissingSessionConfig_ThrowsWithoutCreatingRuntime()
        {
            Assert.Throws<ArgumentNullException>(() => CombatServerInstaller.Install(null, null, new FakeRoster()));

            Assert.IsFalse(CombatServerInstaller.IsInstalled);
            Assert.AreEqual(0, Object.FindObjectsByType<CombatServerRuntime>().Length);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private sealed class FakeRoster : IParticipantRoster
        {
            public bool IsClosed(int participantId) { return false; }
            public void Close(int participantId) { }
        }
    }
}
