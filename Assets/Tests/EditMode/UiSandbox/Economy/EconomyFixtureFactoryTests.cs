// System
using System;

// Unity
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Editor;
using Shinshi.UI;

namespace Shinshi.Tests
{
    /// <summary> 시험 배치 공장이 미리보기 씬 안에서만 화면을 조립하고, 참조를 모두 채우고, 맥락 루트로 추적하고, 해제로 지우는지 확인한다 </summary>
    public sealed class EconomyFixtureFactoryTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private EconomyTestHarness _harness;

        /****************************************
        *                Methods
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _harness = new EconomyTestHarness();
        }

        [TearDown]
        public void TearDown()
        {
            _harness?.Dispose();
            _harness = null;
        }

        [Test]
        public void IsSafeScene_OutsideScope_IsFalseWithReasonAndTrueInsideScope()
        {
            _harness.Dispose();
            _harness = null;

            using (UiSandboxTestScene preview = UiSandboxTestScene.CreatePreview())
            {
                Assume.That(UiSandboxSceneBuilder.TryGetActiveOwnedSandboxScene(out Scene _), Is.False);

                Assert.IsFalse(EconomyFixtureFactory.IsSafeScene(out string reason));
                Assert.IsNotEmpty(reason);

                using (EconomyFixtureFactory.BeginTestScope(preview.Scene))
                {
                    Assert.IsTrue(EconomyFixtureFactory.IsSafeScene(out string insideReason));
                    Assert.IsNull(insideReason);
                }

                Assert.IsFalse(EconomyFixtureFactory.IsSafeScene(out string afterReason));
                Assert.IsNotEmpty(afterReason);
            }
        }

        [Test]
        public void BeginTestScope_WhileAnotherScopeIsOpen_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => EconomyFixtureFactory.BeginTestScope(_harness.Scene));
        }

        [Test]
        public void BeginTestScope_InvalidScene_Throws()
        {
            Assert.Throws<ArgumentException>(() => EconomyFixtureFactory.BeginTestScope(default(Scene)));
        }

        [Test]
        public void Ensure_OutsideScope_ReturnsNullWithNoteAndLeavesNoRoot()
        {
            _harness.Dispose();
            _harness = null;

            UiSandboxScenarioSO template = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
            UiSandboxContext context = new UiSandboxContext(template, null, () => 0.0, () => true, () => null);
            try
            {
                Assume.That(UiSandboxSceneBuilder.TryGetActiveOwnedSandboxScene(out Scene _), Is.False);

                CharDetailView view = EconomyFixtureFactory.EnsureCharDetail(context, out string note);

                Assert.IsNull(view);
                Assert.IsNotEmpty(note);
                Assert.AreEqual(0, context.Roots.Count);
            }
            finally
            {
                context.Dispose();
                UnityEngine.Object.DestroyImmediate(template);
            }
        }

        [TestCase("MailBox")]
        [TestCase("CharDetail")]
        [TestCase("Auction")]
        [TestCase("Trade")]
        public void Ensure_EachFixture_IsWiredInPreviewSceneAndTrackedAsRoot(string name)
        {
            Component view = Ensure(name, out string note);

            Assert.NotNull(view, note);
            Assert.AreEqual(EconomyFixtureFactory.FIXTURE_NOTICE, note);
            CollectionAssert.IsEmpty(EconomyFixtureFactory.FindUnwired(view), "연결 안 된 참조");
            Assert.IsTrue(EconomyFixtureFactory.IsFixture(view));
            Assert.IsTrue(view.gameObject.scene == _harness.Scene, "미리보기 씬 안에 있어야 한다");

            GameObject root = view.transform.root.gameObject;
            CollectionAssert.Contains(_harness.Context.Roots, root);
            Assert.IsNotNull(root.GetComponentInChildren<EventSystem>(true), "시험 배치 안에 EventSystem이 있어야 한다");
        }

        [Test]
        public void Ensure_CalledTwice_ReturnsTheSameViewWithoutAnotherRoot()
        {
            CharDetailView first = EconomyFixtureFactory.EnsureCharDetail(_harness.Context, out string _);
            int rootCount = _harness.Context.Roots.Count;

            CharDetailView second = EconomyFixtureFactory.EnsureCharDetail(_harness.Context, out string _);

            Assert.NotNull(first);
            Assert.AreSame(first, second);
            Assert.AreEqual(rootCount, _harness.Context.Roots.Count);
        }

        [Test]
        public void Ensure_PlaceholderCanvasWithHighSortingOrder_FixtureDrawsAboveIt()
        {
            GameObject placeholder = _harness.Spawn("PlaceholderCanvas");
            Canvas placeholderCanvas = placeholder.AddComponent<Canvas>();
            placeholderCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            placeholderCanvas.sortingOrder = 50;
            _harness.AddRoot(placeholder);

            MailBoxView view = EconomyFixtureFactory.EnsureMailBox(_harness.Context, out string note);

            Assert.NotNull(view, note);
            Assert.Greater(view.transform.root.GetComponent<Canvas>().sortingOrder, placeholderCanvas.sortingOrder);
        }

        [Test]
        public void Release_Fixture_DestroysRootAndRemovesItFromContext()
        {
            TradeView view = EconomyFixtureFactory.EnsureTrade(_harness.Context, out string note);
            Assert.NotNull(view, note);
            GameObject root = view.transform.root.gameObject;

            EconomyFixtureFactory.Release(_harness.Context, view);

            Assert.IsTrue(root == null);
            CollectionAssert.DoesNotContain(_harness.Context.Roots, root);
        }

        [Test]
        public void Release_NonFixtureView_LeavesItAlone()
        {
            GameObject own = _harness.Spawn("OwnView");
            CharDetailView view = own.AddComponent<CharDetailView>();

            EconomyFixtureFactory.Release(_harness.Context, view);

            Assert.IsTrue(own != null);
        }

        private Component Ensure(string name, out string note)
        {
            switch (name)
            {
                case "MailBox":
                    return EconomyFixtureFactory.EnsureMailBox(_harness.Context, out note);
                case "CharDetail":
                    return EconomyFixtureFactory.EnsureCharDetail(_harness.Context, out note);
                case "Auction":
                    return EconomyFixtureFactory.EnsureAuction(_harness.Context, out note);
                default:
                    return EconomyFixtureFactory.EnsureTrade(_harness.Context, out note);
            }
        }
    }
}
