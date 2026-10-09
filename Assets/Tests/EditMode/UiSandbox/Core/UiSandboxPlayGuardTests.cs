using System.Collections.Generic;
using Mirror;
using NUnit.Framework;
using Shinshi.Common;
using Shinshi.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.Tests
{
    public sealed class UiSandboxPlayGuardTests
    {
        private readonly List<UiSandboxTestScene> _scenes = new List<UiSandboxTestScene>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _scenes.Count - 1; i >= 0; --i)
            {
                _scenes[i].Dispose();
            }

            _scenes.Clear();
        }

        private UiSandboxTestScene NewPreview()
        {
            UiSandboxTestScene scene = UiSandboxTestScene.CreatePreview();
            _scenes.Add(scene);
            return scene;
        }

        [Test]
        public void CheckPendingServerRole_EmptyValue_IsAllowed()
        {
            Assert.IsTrue(UiSandboxPlayGuard.CheckPendingServerRole(string.Empty, out string error));
            Assert.IsNull(error);
            Assert.IsTrue(UiSandboxPlayGuard.CheckPendingServerRole(null, out _));
        }

        [TestCase("1")]
        [TestCase("Combat")]
        [TestCase("Server")]
        public void CheckPendingServerRole_AnyPendingValue_IsRefusedWithGuidance(string value)
        {
            bool ok = UiSandboxPlayGuard.CheckPendingServerRole(value, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains(value, error);
            StringAssert.Contains("클라이언트", error);
        }

        [Test]
        public void CheckPendingServerRole_DoesNotTouchSessionFlag()
        {
            string key = Constants.Network.EDITOR_SERVER_ROLE_KEY;
            string before = SessionState.GetString(key, string.Empty);

            UiSandboxPlayGuard.CheckPendingServerRole("Combat", out _);
            UiSandboxPlayGuard.CheckPendingServerRole(string.Empty, out _);

            Assert.AreEqual(before, SessionState.GetString(key, string.Empty));
        }

        [Test]
        public void CheckScenes_UnsafeComponent_IsRefusedWithSceneAndTypeName()
        {
            UiSandboxTestScene unsafeScene = NewPreview();
            unsafeScene.Spawn("network_object").AddComponent<NetworkIdentity>();

            bool ok = UiSandboxPlayGuard.CheckScenes(new[] { unsafeScene.Scene }, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains("NetworkIdentity", error);
            StringAssert.Contains("network_object", error);
        }

        [Test]
        public void CheckScenes_ResultDoesNotDependOnListOrder()
        {
            UiSandboxTestScene safeScene = NewPreview();
            safeScene.Spawn("plain_object").AddComponent<BoxCollider>();
            UiSandboxTestScene unsafeScene = NewPreview();
            unsafeScene.Spawn("network_object").AddComponent<NetworkIdentity>();

            bool unsafeFirst = UiSandboxPlayGuard.CheckScenes(new[] { unsafeScene.Scene, safeScene.Scene }, out string firstError);
            bool unsafeLast  = UiSandboxPlayGuard.CheckScenes(new[] { safeScene.Scene, unsafeScene.Scene }, out string lastError);

            Assert.IsFalse(unsafeFirst);
            Assert.IsFalse(unsafeLast);
            StringAssert.Contains("NetworkIdentity", firstError);
            StringAssert.Contains("NetworkIdentity", lastError);
        }

        [Test]
        public void CheckScenes_OnlySafeScenes_IsAllowed()
        {
            UiSandboxTestScene safeScene = NewPreview();
            safeScene.Spawn("plain_object").AddComponent<BoxCollider>();

            bool ok = UiSandboxPlayGuard.CheckScenes(new[] { safeScene.Scene }, out string error);

            Assert.IsTrue(ok);
            Assert.IsNull(error);
        }

        [Test]
        public void CheckScenes_NullEmptyOrInvalidScenes_AreAllowed()
        {
            Assert.IsTrue(UiSandboxPlayGuard.CheckScenes(null, out _));
            Assert.IsTrue(UiSandboxPlayGuard.CheckScenes(new Scene[0], out _));
            Assert.IsTrue(UiSandboxPlayGuard.CheckScenes(new[] { default(Scene) }, out _));
        }

        // 씬을 새로 만들지 않고, 지금 열린 씬 목록을 CheckScenes에 그대로 넣은 결과와 같은지만 본다
        [Test]
        public void CheckAllLoadedScenes_MatchesCheckScenesOnCurrentlyLoadedScenes()
        {
            Scene active = SceneManager.GetActiveScene();
            int count = SceneManager.sceneCount;
            List<Scene> loaded = new List<Scene>(count);
            for (int i = 0; i < count; ++i)
            {
                loaded.Add(SceneManager.GetSceneAt(i));
            }

            bool expected = UiSandboxPlayGuard.CheckScenes(loaded, out string expectedError);
            bool actual   = UiSandboxPlayGuard.CheckAllLoadedScenes(out string actualError);

            Assert.AreEqual(expected, actual);
            Assert.AreEqual(expectedError, actualError);
            Assert.AreEqual(active, SceneManager.GetActiveScene());
            Assert.AreEqual(count, SceneManager.sceneCount);
        }

        [Test]
        public void CheckScenes_PreviewScenesDoNotAppearInLoadedSceneList()
        {
            UiSandboxTestScene unsafeScene = NewPreview();
            unsafeScene.Spawn("network_object").AddComponent<NetworkIdentity>();
            int count = SceneManager.sceneCount;

            Assert.IsFalse(UiSandboxPlayGuard.CheckScenes(new[] { unsafeScene.Scene }, out _));

            Assert.AreEqual(count, SceneManager.sceneCount);
            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                Assert.AreNotEqual(unsafeScene.Scene, SceneManager.GetSceneAt(i), "미리보기 씬은 열린 씬 목록에 나타나지 않는다");
            }
        }

        [Test]
        public void CheckScene_DiscoversUnsafeComponentWithoutTouchingOtherScenes()
        {
            UiSandboxTestScene unsafeScene = NewPreview();
            unsafeScene.Spawn("network_object").AddComponent<NetworkIdentity>();
            UiSandboxTestScene safeScene = NewPreview();
            safeScene.Spawn("plain_object");

            Assert.IsFalse(UiSandboxPlayGuard.CheckScene(unsafeScene.Scene, out string error));
            StringAssert.Contains("network_object", error);
            Assert.IsTrue(UiSandboxPlayGuard.CheckScene(safeScene.Scene, out _));
        }
    }
}
