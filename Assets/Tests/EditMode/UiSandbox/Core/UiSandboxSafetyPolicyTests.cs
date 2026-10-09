using Mirror;
using NUnit.Framework;
using Shinshi.Editor;
using UnityEngine;

namespace Shinshi.Tests
{
    public sealed class UiSandboxSafetyPolicyTests
    {
        private const string SANDBOX_FILE = "Assets/00.Scenes/Dev_UiSandbox/UiSandbox_Login.unity";

        private class FakePlainView : MonoBehaviour
        {
        }

        private sealed class FakeLoginBinder : MonoBehaviour
        {
        }

        private sealed class PlayerInteractionPanelOpener : MonoBehaviour
        {
        }

        /****************************************
        *                Paths
        ****************************************/

        [TestCase(SANDBOX_FILE, true)]
        [TestCase("Assets\\00.Scenes\\Dev_UiSandbox\\UiSandbox_Login.unity", true)]
        [TestCase("Assets/00.Scenes/Dev_UiSandbox/Sub/UiSandbox_Login.unity", false)]
        [TestCase("Assets/00.Scenes/Dev_UiSandbox/../Release/ShinshiTown.unity", false)]
        [TestCase("Assets/00.Scenes/Release/ShinshiTown.unity", false)]
        [TestCase("Assets/00.Scenes/Dev_UiSandbox/UiSandbox_Login.txt", false)]
        [TestCase("Assets/00.Scenes/Dev_UiSandbox/.unity", false)]
        [TestCase("Assets/00.Scenes/Dev_UiSandbox2/UiSandbox_Login.unity", false)]
        [TestCase("Assets/00.Scenes/Dev_UiSandbox/", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsSandboxScenePath_Path_MatchesSandboxFolderRule(string path, bool expected)
        {
            Assert.AreEqual(expected, UiSandboxSafetyPolicy.IsSandboxScenePath(path));
        }

        [Test]
        public void BuildSandboxScenePath_NormalId_BuildsFileInsideSandboxFolder()
        {
            string path = UiSandboxSafetyPolicy.BuildSandboxScenePath("Login");

            Assert.AreEqual(SANDBOX_FILE, path);
            Assert.IsTrue(UiSandboxSafetyPolicy.IsSandboxScenePath(path));
        }

        [TestCase("../../evil/x")]
        [TestCase("a\\b:c")]
        [TestCase("로그인 화면")]
        [TestCase("Assets/00.Scenes/Release/ShinshiTown")]
        public void BuildSandboxScenePath_UnsafeId_StaysInsideSandboxFolder(string pageId)
        {
            string path = UiSandboxSafetyPolicy.BuildSandboxScenePath(pageId);

            Assert.IsTrue(UiSandboxSafetyPolicy.IsSandboxScenePath(path), path);
            StringAssert.DoesNotContain("..", path);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void BuildSandboxScenePath_EmptyId_UsesDefaultName(string pageId)
        {
            string path = UiSandboxSafetyPolicy.BuildSandboxScenePath(pageId);

            Assert.AreEqual("Assets/00.Scenes/Dev_UiSandbox/UiSandbox_Page.unity", path);
        }

        [Test]
        public void BuildSandboxScenePath_VeryLongId_IsTruncated()
        {
            string path = UiSandboxSafetyPolicy.BuildSandboxScenePath(new string('a', 500));

            Assert.Less(path.Length, 200);
            Assert.IsTrue(UiSandboxSafetyPolicy.IsSandboxScenePath(path));
        }

        [TestCase("Assets/00.Scenes/Release/ShinshiTown.unity", true)]
        [TestCase("Assets\\00.Scenes\\Release\\ShinshiTown.unity", true)]
        [TestCase("Assets/00.Scenes/Dev_UiSandbox/UiSandbox_Login.unity", false)]
        [TestCase("Packages/com.example/Scene.unity", false)]
        [TestCase("Assets/Prefabs/Panel.prefab", false)]
        [TestCase("Assets/../Outside.unity", false)]
        [TestCase("C:/Assets/Scene.unity", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsValidSourceScenePath_Path_ReturnsExpected(string path, bool expected)
        {
            bool valid = UiSandboxSafetyPolicy.IsValidSourceScenePath(path, out string error);

            Assert.AreEqual(expected, valid);
            Assert.AreEqual(expected, string.IsNullOrEmpty(error));
        }

        [Test]
        public void IsValidSourceScenePath_EmptyPath_ExplainsContractOnly()
        {
            UiSandboxSafetyPolicy.IsValidSourceScenePath(string.Empty, out string error);

            StringAssert.Contains("계약 전용", error);
        }

        /****************************************
        *               Ownership
        ****************************************/

        [Test]
        public void CanWriteScene_NewFileInSandbox_ReturnsTrue()
        {
            Assert.IsTrue(UiSandboxSafetyPolicy.CanWriteScene(SANDBOX_FILE, false, new string[0], out string error));
            Assert.IsNull(error);
        }

        [Test]
        public void CanWriteScene_ExistingFileWithoutLabel_ReturnsFalse()
        {
            bool allowed = UiSandboxSafetyPolicy.CanWriteScene(SANDBOX_FILE, true, new[] { "Other" }, out string error);

            Assert.IsFalse(allowed);
            StringAssert.Contains(SANDBOX_FILE, error);
        }

        [Test]
        public void CanWriteScene_ExistingFileWithNullLabels_ReturnsFalse()
        {
            Assert.IsFalse(UiSandboxSafetyPolicy.CanWriteScene(SANDBOX_FILE, true, null, out _));
        }

        [Test]
        public void CanWriteScene_ExistingFileWithOwnershipLabel_ReturnsTrue()
        {
            string[] labels = { "Other", UiSandboxSafetyPolicy.OWNERSHIP_LABEL };

            Assert.IsTrue(UiSandboxSafetyPolicy.CanWriteScene(SANDBOX_FILE, true, labels, out _));
        }

        [Test]
        public void CanWriteScene_OutsideSandboxEvenWithLabel_ReturnsFalse()
        {
            string[] labels = { UiSandboxSafetyPolicy.OWNERSHIP_LABEL };

            bool allowed = UiSandboxSafetyPolicy.CanWriteScene("Assets/00.Scenes/Release/ShinshiTown.unity", true, labels, out string error);

            Assert.IsFalse(allowed);
            StringAssert.Contains("밖", error);
        }

        [Test]
        public void HasOwnershipLabel_LabelIsCaseSensitive()
        {
            Assert.IsTrue(UiSandboxSafetyPolicy.HasOwnershipLabel(new[] { UiSandboxSafetyPolicy.OWNERSHIP_LABEL }));
            Assert.IsFalse(UiSandboxSafetyPolicy.HasOwnershipLabel(new[] { UiSandboxSafetyPolicy.OWNERSHIP_LABEL.ToLowerInvariant() }));
            Assert.IsFalse(UiSandboxSafetyPolicy.HasOwnershipLabel(null));
        }

        /****************************************
        *               Network
        ****************************************/

        [Test]
        public void IsNetworkActive_NoConnection_ReturnsFalse()
        {
            Assert.IsFalse(UiSandboxSafetyPolicy.IsNetworkActive(out string reason));
            Assert.IsNull(reason);
        }

        /****************************************
        *           Unsafe Components
        ****************************************/

        [TestCase(typeof(NetworkIdentity), true)]
        [TestCase(typeof(NetworkManager), true)]
        [TestCase(typeof(NetworkBehaviour), true)]
        [TestCase(typeof(FakeLoginBinder), true)]
        [TestCase(typeof(PlayerInteractionPanelOpener), true)]
        [TestCase(typeof(FakePlainView), false)]
        [TestCase(typeof(Camera), false)]
        [TestCase(typeof(Canvas), false)]
        [TestCase(typeof(UnityEngine.UI.Button), false)]
        [TestCase(typeof(Transform), false)]
        public void IsUnsafeComponentType_Type_ReturnsExpected(System.Type type, bool expected)
        {
            Assert.AreEqual(expected, UiSandboxSafetyPolicy.IsUnsafeComponentType(type));
        }

        [Test]
        public void IsUnsafeComponentType_Null_ReturnsFalse()
        {
            Assert.IsFalse(UiSandboxSafetyPolicy.IsUnsafeComponentType(null));
        }

        [Test]
        public void IsUnsafeComponentType_HudChatView_IsUnsafeBecauseItSendsChatDirectly()
        {
            Assert.IsTrue(UiSandboxSafetyPolicy.IsUnsafeComponentType(typeof(Shinshi.UI.HudChatView)));
        }

        [TestCase("Shinshi.Server", true)]
        [TestCase("Shinshi.Server.Combat", true)]
        [TestCase("Shinshi.Dev", true)]
        [TestCase("Shinshi.Runtime", false)]
        [TestCase("Shinshi.ServerLike", false)]
        [TestCase("Shinshi.Servers", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsUnsafeAssemblyName_Name_ReturnsExpected(string name, bool expected)
        {
            Assert.AreEqual(expected, UiSandboxSafetyPolicy.IsUnsafeAssemblyName(name));
        }

        [Test]
        public void IsUnsafeComponentType_RuntimeAssemblyType_IsNotFlaggedByAssemblyRule()
        {
            Assert.IsFalse(UiSandboxSafetyPolicy.IsUnsafeAssemblyName(typeof(FakePlainView).Assembly.GetName().Name));
        }

        [TestCase(true, true, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, true)]
        [TestCase(false, false, true)]
        public void IsHostContextSafe_ViewPageNeedsOwnedSandboxScene(bool hasView, bool ownedSceneActive, bool expected)
        {
            Assert.AreEqual(expected, UiSandboxSafetyPolicy.IsHostContextSafe(hasView, ownedSceneActive));
        }

        [TestCase("LoadScene", true)]
        [TestCase("GoToLobby", true)]
        [TestCase("OnClickLogin", true)]
        [TestCase("sendchat", true)]
        [TestCase("QuitGame", true)]
        [TestCase("RequestTrade", true)]
        [TestCase("SetActive", false)]
        [TestCase("OpenPanel", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsUnsafeMethodName_Name_ReturnsExpected(string name, bool expected)
        {
            Assert.AreEqual(expected, UiSandboxSafetyPolicy.IsUnsafeMethodName(name));
        }

        [Test]
        public void IsUnsafePersistentCall_NullTarget_IsUnsafe()
        {
            bool unsafeCall = UiSandboxSafetyPolicy.IsUnsafePersistentCall(null, string.Empty, "SetActive", out string reason);

            Assert.IsTrue(unsafeCall);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void IsUnsafePersistentCall_SafeSceneTargetAndMethod_IsSafe()
        {
            using (UiSandboxTestScene scene = UiSandboxTestScene.CreatePreview())
            {
                GameObject go = scene.Spawn("target");

                bool unsafeCall = UiSandboxSafetyPolicy.IsUnsafePersistentCall(
                    go.transform, "UnityEngine.Transform, UnityEngine.CoreModule", "SetParent", out string reason);

                Assert.IsFalse(unsafeCall);
                Assert.IsNull(reason);
            }
        }

        [Test]
        public void IsUnsafePersistentCall_UnsafeMethodOnSafeTarget_IsUnsafe()
        {
            using (UiSandboxTestScene scene = UiSandboxTestScene.CreatePreview())
            {
                GameObject go = scene.Spawn("target");

                bool unsafeCall = UiSandboxSafetyPolicy.IsUnsafePersistentCall(
                    go.transform, "UnityEngine.Transform, UnityEngine.CoreModule", "LoadScene", out string reason);

                Assert.IsTrue(unsafeCall);
                StringAssert.Contains("LoadScene", reason);
            }
        }

        [Test]
        public void IsUnsafePersistentCall_UnsafeDeclaredTypeName_IsUnsafe()
        {
            using (UiSandboxTestScene scene = UiSandboxTestScene.CreatePreview())
            {
                GameObject go = scene.Spawn("target");

                bool unsafeCall = UiSandboxSafetyPolicy.IsUnsafePersistentCall(
                    go.transform, "Mirror.NetworkIdentity, Mirror", "SetParent", out _);

                Assert.IsTrue(unsafeCall);
            }
        }

        /****************************************
        *        Ownership / Request Guard
        ****************************************/

        private static readonly string[] G_OwnedLabels = { "Other", UiSandboxSafetyPolicy.OWNERSHIP_LABEL };
        private const string SANDBOX_SCENE = "Assets/00.Scenes/Dev_UiSandbox/UiSandbox_Page.unity";

        [Test]
        public void IsOwnedSandboxScene_RequiresBothFolderPathAndLabel()
        {
            Assert.IsTrue(UiSandboxSafetyPolicy.IsOwnedSandboxScene(SANDBOX_SCENE, G_OwnedLabels));
            Assert.IsFalse(UiSandboxSafetyPolicy.IsOwnedSandboxScene(SANDBOX_SCENE, new[] { "Other" }));
            Assert.IsFalse(UiSandboxSafetyPolicy.IsOwnedSandboxScene(SANDBOX_SCENE, null));
            Assert.IsFalse(UiSandboxSafetyPolicy.IsOwnedSandboxScene("Assets/00.Scenes/Release/Town.unity", G_OwnedLabels));
            Assert.IsFalse(UiSandboxSafetyPolicy.IsOwnedSandboxScene(string.Empty, G_OwnedLabels));
        }

        [Test]
        public void IsHostContextSafe_UserFileInSandboxFolderWithoutLabel_IsNotSafeForViewPage()
        {
            Assert.IsFalse(UiSandboxSafetyPolicy.IsHostContextSafe(true, SANDBOX_SCENE, new string[0], false));
            Assert.IsTrue(UiSandboxSafetyPolicy.IsHostContextSafe(true, SANDBOX_SCENE, G_OwnedLabels, false));
        }

        [TestCase(true, true, true, false)]
        [TestCase(false, false, true, false)]
        [TestCase(true, false, false, false)]
        [TestCase(true, true, false, true)]
        [TestCase(false, false, false, true)]
        public void IsHostContextSafe_ActiveConnection_RejectsEveryPage(bool hasView, bool ownedSceneActive, bool networkActive, bool expected)
        {
            Assert.AreEqual(expected, UiSandboxSafetyPolicy.IsHostContextSafe(hasView, ownedSceneActive, networkActive));
        }

        [TestCase(false, true, null)]
        [TestCase(true, true, "Mirror")]
        [TestCase(true, false, "Mirror")]
        [TestCase(false, false, "샌드박스가 만든")]
        public void GetRequestBlockReason_NetworkOrUnownedScene_ReturnsReason(bool networkActive, bool owned, string expectedPart)
        {
            string reason = UiSandboxSafetyPolicy.GetRequestBlockReason(networkActive, owned);

            if (expectedPart == null)
            {
                Assert.IsNull(reason);
            }
            else
            {
                StringAssert.Contains(expectedPart, reason);
            }
        }

        [TestCase(false, null, false, true)]
        [TestCase(false, null, true, true)]
        [TestCase(true, SANDBOX_SCENE, true, true)]
        [TestCase(true, SANDBOX_SCENE, false, false)]
        [TestCase(true, "Assets/00.Scenes/Dev_UiSandbox/UiSandbox_Other.unity", true, false)]
        public void CanDiscardScene_OnlyOwnedDirtySceneWithExplicitIntent(bool isDirty, string path, bool discardIntent, bool expected)
        {
            string[] labels = (path == SANDBOX_SCENE) ? G_OwnedLabels : new string[0];

            Assert.AreEqual(expected, UiSandboxSafetyPolicy.CanDiscardScene(path, labels, isDirty, discardIntent));
        }

        [Test]
        public void CanDiscardScene_DirtyUserSceneInSandboxFolder_IsNeverDiscarded()
        {
            Assert.IsFalse(UiSandboxSafetyPolicy.CanDiscardScene(SANDBOX_SCENE, new[] { "Other" }, true, true));
            Assert.IsFalse(UiSandboxSafetyPolicy.CanDiscardScene("Assets/00.Scenes/Release/Town.unity", G_OwnedLabels, true, true));
        }
    }
}
