using NUnit.Framework;
using Shinshi.Editor;
using UnityEngine.SceneManagement;

namespace Shinshi.Tests
{
    public sealed class UiSandboxSceneBuilderTests
    {
        private sealed class FakePage : IUiSandboxPage
        {
            private readonly string _id;
            private readonly string _source;
            private readonly string _requirement;
            private readonly bool _hasView;

            public FakePage(string id, string source, string requirement = "", bool? hasView = null)
            {
                _id = id;
                _source = source;
                _requirement = requirement;
                _hasView = hasView ?? !string.IsNullOrEmpty(source);
            }

            public string Id { get { return _id; } }
            public string Title { get { return "가짜 화면"; } }
            public string SourceScenePath { get { return _source; } }
            public string Requirement { get { return _requirement; } }
            public bool HasView { get { return _hasView; } }

            public void Attach(UiSandboxContext context) { }
            public void Apply(UiSandboxContext context) { }
            public void DrawControls(UiSandboxContext context) { }
            public void Detach() { }
        }

        [Test]
        public void CheckPreconditions_NullPage_ReturnsFalseWithGuidance()
        {
            bool ok = UiSandboxSceneBuilder.CheckPreconditions(null, out string error, out string source, out string target);

            Assert.IsFalse(ok);
            Assert.IsNotEmpty(error);
            Assert.AreEqual(string.Empty, source);
            Assert.AreEqual(string.Empty, target);
        }

        [Test]
        public void CheckPreconditions_ContractOnlyPage_ExplainsAndIncludesRequirement()
        {
            FakePage page = new FakePage("contract_only", string.Empty, "상점 화면이 필요하다");

            bool ok = UiSandboxSceneBuilder.CheckPreconditions(page, out string error, out _, out _);

            Assert.IsFalse(ok);
            StringAssert.Contains("계약 전용", error);
            StringAssert.Contains("상점 화면이 필요하다", error);
        }

        [Test]
        public void CheckPreconditions_SourceInsideSandboxFolder_IsRejected()
        {
            FakePage page = new FakePage("loop", "Assets/00.Scenes/Dev_UiSandbox/UiSandbox_Other.unity");

            bool ok = UiSandboxSceneBuilder.CheckPreconditions(page, out string error, out _, out _);

            Assert.IsFalse(ok);
            StringAssert.Contains("샌드박스", error);
        }

        [Test]
        public void CheckPreconditions_SourceIsNotScene_IsRejected()
        {
            FakePage page = new FakePage("prefab", "Assets/Prefabs/Panel.prefab");

            bool ok = UiSandboxSceneBuilder.CheckPreconditions(page, out string error, out _, out _);

            Assert.IsFalse(ok);
            StringAssert.Contains("올바르지", error);
        }

        [Test]
        public void CheckPreconditions_SourceTraversal_IsRejected()
        {
            FakePage page = new FakePage("traversal", "Assets/../Outside.unity");

            Assert.IsFalse(UiSandboxSceneBuilder.CheckPreconditions(page, out _, out _, out _));
        }

        [Test]
        public void CheckPreconditions_MissingSourceScene_NamesPathAndComputesTarget()
        {
            string missing = "Assets/00.Scenes/Release/__UiSandboxMissingScene__.unity";
            FakePage page = new FakePage("missing", missing);

            bool ok = UiSandboxSceneBuilder.CheckPreconditions(page, out string error, out string source, out string target);

            Assert.IsFalse(ok);
            StringAssert.Contains(missing, error);
            Assert.AreEqual(missing, source);
            Assert.AreEqual(UiSandboxSafetyPolicy.BuildSandboxScenePath("missing"), target);
            Assert.IsTrue(UiSandboxSafetyPolicy.IsSandboxScenePath(target));
        }

        [Test]
        public void BuildAndOpen_ContractOnlyPage_FailsWithoutChangingOpenScenes()
        {
            int sceneCount = SceneManager.sceneCount;
            string activePath = SceneManager.GetActiveScene().path;
            FakePage page = new FakePage("contract_only", string.Empty);

            UiSandboxBuildResult result = UiSandboxSceneBuilder.BuildAndOpen(page);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("계약 전용", result.Message);
            Assert.AreEqual(0, result.Roots.Length);
            Assert.AreEqual(sceneCount, SceneManager.sceneCount);
            Assert.AreEqual(activePath, SceneManager.GetActiveScene().path);
        }

        [Test]
        public void CheckEmptyPreconditions_NullPage_ReturnsFalseWithGuidance()
        {
            bool ok = UiSandboxSceneBuilder.CheckEmptyPreconditions(null, false, out string error, out string target);

            Assert.IsFalse(ok);
            Assert.IsNotEmpty(error);
            Assert.AreEqual(string.Empty, target);
        }

        [Test]
        public void CheckEmptyPreconditions_BlankPageId_IsRejected()
        {
            FakePage page = new FakePage("  ", string.Empty);

            Assert.IsFalse(UiSandboxSceneBuilder.CheckEmptyPreconditions(page, false, out string error, out _));
            StringAssert.Contains("Id", error);
        }

        [Test]
        public void CheckEmptyPreconditions_ContractOnlyPage_IsNotRejectedForMissingSource()
        {
            FakePage page = new FakePage("contract_only", string.Empty, "상점 화면이 필요하다");

            bool ok = UiSandboxSceneBuilder.CheckEmptyPreconditions(page, false, out string error, out string target);

            Assert.AreEqual(UiSandboxSafetyPolicy.BuildSandboxScenePath("contract_only"), target);
            Assert.IsTrue(UiSandboxSafetyPolicy.IsSandboxScenePath(target));
            if (!ok)
            {
                StringAssert.DoesNotContain("계약 전용", error);
            }
        }

        [Test]
        public void CheckEmptyPreconditions_IdWithPathCharacters_StaysInsideSandboxFolder()
        {
            FakePage page = new FakePage("../../Outside", string.Empty);

            UiSandboxSceneBuilder.CheckEmptyPreconditions(page, false, out _, out string target);

            Assert.IsTrue(UiSandboxSafetyPolicy.IsSandboxScenePath(target));
        }

        [Test]
        public void BuildEmptyAndOpen_NullPage_FailsWithoutChangingOpenScenes()
        {
            int sceneCount = SceneManager.sceneCount;
            string activePath = SceneManager.GetActiveScene().path;

            UiSandboxBuildResult result = UiSandboxSceneBuilder.BuildEmptyAndOpen(null);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, result.Roots.Length);
            Assert.AreEqual(string.Empty, result.ScenePath);
            Assert.AreEqual(sceneCount, SceneManager.sceneCount);
            Assert.AreEqual(activePath, SceneManager.GetActiveScene().path);
        }

        [Test]
        public void CheckPreconditions_DiscardOverload_StillRejectsContractOnlyPage()
        {
            FakePage page = new FakePage("contract_only", string.Empty, "상점 화면이 필요하다");

            bool ok = UiSandboxSceneBuilder.CheckPreconditions(page, true, out string error, out _, out _);

            Assert.IsFalse(ok);
            StringAssert.Contains("계약 전용", error);
        }

        [Test]
        public void FindDirtySandboxSceneNames_ReturnsOnlyOwnedSandboxPaths()
        {
            System.Collections.Generic.List<string> names = UiSandboxSceneBuilder.FindDirtySandboxSceneNames();

            Assert.IsNotNull(names);
            for (int i = 0; i < names.Count; ++i)
            {
                Assert.IsTrue(UiSandboxSceneBuilder.IsOwnedSandboxScenePath(names[i]));
            }
        }

        [Test]
        public void FindDirtySceneNames_NeverListsOwnedSandboxScenes()
        {
            System.Collections.Generic.List<string> names = UiSandboxSceneBuilder.FindDirtySceneNames();

            for (int i = 0; i < names.Count; ++i)
            {
                Assert.IsFalse(UiSandboxSceneBuilder.IsOwnedSandboxScenePath(names[i]));
            }
        }

        [Test]
        public void TryGetActiveOwnedSandboxScene_AgreesWithTryGetActiveSandboxScene()
        {
            bool owned = UiSandboxSceneBuilder.TryGetActiveOwnedSandboxScene(out Scene ownedScene);
            bool sandbox = UiSandboxSceneBuilder.TryGetActiveSandboxScene(out Scene sandboxScene);

            Assert.AreEqual(owned, sandbox);
            Assert.AreEqual(ownedScene.handle, sandboxScene.handle);
        }

        [Test]
        public void TryGetActiveSandboxScene_ActiveSceneWithoutOwnershipLabel_ReturnsFalse()
        {
            Scene active = SceneManager.GetActiveScene();
            if (UiSandboxSceneBuilder.IsOwnedSandboxScenePath(active.path))
            {
                Assert.Ignore("테스트 실행 중 활성 씬이 샌드박스가 만든 사본이라 건너뛴다");
            }

            Assert.IsFalse(UiSandboxSceneBuilder.TryGetActiveSandboxScene(out _));
        }

        [TestCase("")]
        [TestCase("Assets/00.Scenes/Release/ShinshiTown.unity")]
        [TestCase("Assets/00.Scenes/Dev_UiSandbox/UiSandbox___NoSuchFile__.unity")]
        [TestCase("Assets/../Outside.unity")]
        public void IsOwnedSandboxScenePath_NonExistingOrOutsideFolder_ReturnsFalse(string path)
        {
            Assert.IsFalse(UiSandboxSceneBuilder.IsOwnedSandboxScenePath(path));
        }

        [Test]
        public void UsesSourceCopy_OnlyForPagesWithViewAndSourcePath()
        {
            Assert.IsTrue(UiSandboxSceneBuilder.UsesSourceCopy(new FakePage("real", "Assets/00.Scenes/Release/Town.unity")));
            Assert.IsFalse(UiSandboxSceneBuilder.UsesSourceCopy(new FakePage("view_without_source", string.Empty, "", true)));
            Assert.IsFalse(UiSandboxSceneBuilder.UsesSourceCopy(new FakePage("contract", string.Empty, "", false)));
            Assert.IsFalse(UiSandboxSceneBuilder.UsesSourceCopy(new FakePage("blank_source", "   ", "", true)));
            Assert.IsFalse(UiSandboxSceneBuilder.UsesSourceCopy(null));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CanOpenSafeScene_ContractOnlyPage_IsReachableUnlessPlaying(bool playingOrWillPlay)
        {
            FakePage noView = new FakePage("contract", string.Empty, "상점 화면", false);
            FakePage viewOnly = new FakePage("view_only", string.Empty, "상점 화면", true);
            FakePage real = new FakePage("real", "Assets/00.Scenes/Release/Town.unity");

            Assert.AreEqual(!playingOrWillPlay, UiSandboxSceneBuilder.CanOpenSafeScene(noView, playingOrWillPlay));
            Assert.AreEqual(!playingOrWillPlay, UiSandboxSceneBuilder.CanOpenSafeScene(viewOnly, playingOrWillPlay));
            Assert.AreEqual(!playingOrWillPlay, UiSandboxSceneBuilder.CanOpenSafeScene(real, playingOrWillPlay));
            Assert.IsFalse(UiSandboxSceneBuilder.CanOpenSafeScene(null, false));
        }

        [Test]
        public void CheckEmptyPreconditions_NoViewContractPage_IsAcceptedAsEmptySceneTarget()
        {
            FakePage page = new FakePage("contract_no_view", string.Empty, "상점 화면", false);

            UiSandboxSceneBuilder.CheckEmptyPreconditions(page, false, out string error, out string target);

            Assert.AreEqual(UiSandboxSafetyPolicy.BuildSandboxScenePath("contract_no_view"), target);
            if (error != null)
            {
                StringAssert.DoesNotContain("계약 전용", error);
            }
        }

        [Test]
        public void BuildAndOpen_InvalidSourcePath_FailsWithoutWriting()
        {
            FakePage page = new FakePage("bad", "Assets/Prefabs/Panel.prefab");

            UiSandboxBuildResult result = UiSandboxSceneBuilder.BuildAndOpen(page);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(string.Empty, result.ScenePath);
        }

        [Test]
        public void GetRoots_InvalidScene_ReturnsEmptyArray()
        {
            Assert.AreEqual(0, UiSandboxSceneBuilder.GetRoots(default(Scene)).Length);
        }

        /****************************************
        *            Rollback Plan
        ****************************************/

        [Test]
        public void RollbackPlan_CopyIsOnlyOpenScene_NeedsPlaceholderBeforeClosingCopy()
        {
            UiSandboxRollbackPlan onlyCopy = UiSandboxRollbackPlan.Create(true, 1, true, true);
            UiSandboxRollbackPlan withOthers = UiSandboxRollbackPlan.Create(true, 2, true, true);
            UiSandboxRollbackPlan noCopy = UiSandboxRollbackPlan.Create(false, 1, true, true);

            Assert.IsTrue(onlyCopy.NeedsPlaceholder);
            Assert.IsFalse(withOthers.NeedsPlaceholder);
            Assert.IsFalse(noCopy.NeedsPlaceholder);
        }

        [Test]
        public void RollbackPlan_FailureBeforeFileWrite_TouchesNoFile()
        {
            UiSandboxRollbackPlan existing = UiSandboxRollbackPlan.Create(true, 2, true, false);
            UiSandboxRollbackPlan created = UiSandboxRollbackPlan.Create(true, 2, false, false);

            Assert.IsFalse(existing.RestoreTargetFile);
            Assert.IsFalse(existing.DeleteCreatedTarget);
            Assert.IsFalse(created.RestoreTargetFile);
            Assert.IsFalse(created.DeleteCreatedTarget);
        }

        [Test]
        public void RollbackPlan_FailureAfterFileWrite_RestoresExistingOrDeletesCreated()
        {
            UiSandboxRollbackPlan existing = UiSandboxRollbackPlan.Create(true, 2, true, true);
            UiSandboxRollbackPlan created = UiSandboxRollbackPlan.Create(true, 2, false, true);

            Assert.IsTrue(existing.RestoreTargetFile);
            Assert.IsFalse(existing.DeleteCreatedTarget);
            Assert.IsFalse(created.RestoreTargetFile);
            Assert.IsTrue(created.DeleteCreatedTarget);
        }

        [Test]
        public void RollbackPlan_IsOriginalActive_UsesSceneCapturedBeforeCopyWasActivated()
        {
            using (UiSandboxTestScene original = UiSandboxTestScene.CreatePreview())
            using (UiSandboxTestScene other = UiSandboxTestScene.CreatePreview())
            {
                Assert.IsTrue(UiSandboxRollbackPlan.IsOriginalActive(original.Scene, original.Scene));
                Assert.IsFalse(UiSandboxRollbackPlan.IsOriginalActive(other.Scene, original.Scene));
                Assert.IsFalse(UiSandboxRollbackPlan.IsOriginalActive(original.Scene, default(Scene)));
                Assert.IsFalse(UiSandboxRollbackPlan.IsOriginalActive(default(Scene), original.Scene));
            }
        }

        [Test]
        public void RollbackPlan_ActiveSceneClosedThenReopened_RestoresReopenedScene()
        {
            Assert.AreEqual(EUiSandboxActiveRestore.Original, UiSandboxRollbackPlan.ChooseActiveRestore(true, false));
            Assert.AreEqual(EUiSandboxActiveRestore.Original, UiSandboxRollbackPlan.ChooseActiveRestore(true, true));
            Assert.AreEqual(EUiSandboxActiveRestore.Reopened, UiSandboxRollbackPlan.ChooseActiveRestore(false, true));
            Assert.AreEqual(EUiSandboxActiveRestore.None, UiSandboxRollbackPlan.ChooseActiveRestore(false, false));
        }
    }
}
