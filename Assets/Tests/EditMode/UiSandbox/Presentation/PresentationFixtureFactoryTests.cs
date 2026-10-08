using System;
using NUnit.Framework;
using Shinshi.Editor;
using Shinshi.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.Tests
{
    public sealed class PresentationFixtureFactoryTests
    {
        private const string LABEL = "시험";

        private PresentationTestHarness _harness;

        [TearDown]
        public void TearDown()
        {
            if (_harness != null)
            {
                _harness.Dispose();
                _harness = null;
            }
        }

        /****************************************
        *           Host scene refusal
        ****************************************/

        [Test]
        public void Create_NoScopeAndNoOwnedSandboxScene_ThrowsRefuseReason()
        {
            _harness = new PresentationTestHarness(false);
            Assume.That(PresentationFixtureFactory.HasTestScope, Is.False);
            Assume.That(PresentationFixtureFactory.CanCreate(out _), Is.False);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => PresentationFixtureFactory.Create<HudPartyView>(LABEL));

            Assert.AreEqual(PresentationFixtureFactory.REFUSE_REASON, error.Message);
            Assert.AreEqual(0, PresentationTestHarness.CountFixtureCanvases());
        }

        [Test]
        public void CanCreate_NoScopeAndNoOwnedSandboxScene_ReturnsFalseWithReason()
        {
            _harness = new PresentationTestHarness(false);
            Assume.That(PresentationFixtureFactory.HasTestScope, Is.False);

            bool canCreate = PresentationFixtureFactory.CanCreate(out string reason);
            Assume.That(canCreate, Is.False);

            Assert.AreEqual(PresentationFixtureFactory.REFUSE_REASON, reason);
        }

        [Test]
        public void CanCreate_OpenScope_ReturnsTrueWithEmptyReason()
        {
            _harness = new PresentationTestHarness();

            Assert.IsTrue(PresentationFixtureFactory.HasTestScope);
            Assert.IsTrue(PresentationFixtureFactory.CanCreate(out string reason));
            Assert.IsEmpty(reason);
        }

        [Test]
        public void IsHostScene_ScopeSceneAndInvalidScene_OnlyScopeSceneIsHost()
        {
            _harness = new PresentationTestHarness();

            Assert.IsTrue(PresentationFixtureFactory.IsHostScene(_harness.Scope.Scene));
            Assert.IsFalse(PresentationFixtureFactory.IsHostScene(default(Scene)));
        }

        [Test]
        public void Create_NullType_ThrowsArgumentNullException()
        {
            _harness = new PresentationTestHarness();

            Assert.Throws<ArgumentNullException>(() => PresentationFixtureFactory.Create(null, LABEL));
        }

        /****************************************
        *               Test scope
        ****************************************/

        [Test]
        public void OpenTestScope_AlreadyOpen_ThrowsInvalidOperation()
        {
            _harness = new PresentationTestHarness();

            Assert.Throws<InvalidOperationException>(() => PresentationFixtureFactory.OpenTestScope());
        }

        [Test]
        public void Dispose_Harness_ClosesScopeAndAllowsReopen()
        {
            _harness = new PresentationTestHarness();
            _harness.Dispose();
            _harness = null;

            Assert.IsFalse(PresentationFixtureFactory.HasTestScope);

            PresentationFixtureScope reopened = PresentationFixtureFactory.OpenTestScope();
            Assert.IsTrue(reopened.IsOpen);
            reopened.Dispose();
            Assert.IsFalse(reopened.IsOpen);
            Assert.IsFalse(PresentationFixtureFactory.HasTestScope);
        }

        /****************************************
        *            Fixture assembly
        ****************************************/

        [Test]
        public void Create_PartyViewWithLengthFour_WiresFourSlotsBeforeActivation()
        {
            _harness = new PresentationTestHarness();

            PresentationFixture fixture = PresentationFixtureFactory.Create<HudPartyView>(
                LABEL, new PresentationFixtureOptions().WithLength("_slots", 4));

            HudPartyView view = fixture.View as HudPartyView;
            Assert.IsNotNull(view);
            Assert.AreEqual(4, view.SlotCount);
            Assert.IsTrue(fixture.IsAlive);
            Assert.IsTrue(fixture.Root.activeSelf);
        }

        [Test]
        public void Create_PartyViewWithLengthTwo_UsesGivenLength()
        {
            _harness = new PresentationTestHarness();

            PresentationFixture fixture = PresentationFixtureFactory.Create<HudPartyView>(
                LABEL, new PresentationFixtureOptions().WithLength("_slots", 2));

            Assert.AreEqual(2, ((HudPartyView)fixture.View).SlotCount);
        }

        [Test]
        public void Create_SkippedField_LeavesFieldNull()
        {
            _harness = new PresentationTestHarness();

            PresentationFixture fixture = PresentationFixtureFactory.Create<HudPartyView>(
                LABEL, new PresentationFixtureOptions().Skip("_slots"));

            Assert.AreEqual(0, ((HudPartyView)fixture.View).SlotCount);
        }

        [Test]
        public void Create_Default_BuildsLabeledRootWithHeaderAndSeparateViewChild()
        {
            _harness = new PresentationTestHarness();

            PresentationFixture fixture = PresentationFixtureFactory.Create<HudPartyView>(LABEL);

            Assert.AreEqual(PresentationFixtureFactory.FIXTURE_PREFIX + LABEL, fixture.Root.name);
            Assert.AreEqual(LABEL, fixture.Label);

            Transform header = fixture.Root.transform.Find("header");
            Transform host   = fixture.Root.transform.Find("View");
            Assert.IsNotNull(header);
            Assert.IsNotNull(host);
            Assert.AreSame(fixture.View.gameObject, host.gameObject);
            Assert.AreEqual(string.Format(PresentationFixtureFactory.HEADER_FORMAT, LABEL), header.GetComponent<TMP_Text>().text);
            StringAssert.Contains("실제 배치 아님", header.GetComponent<TMP_Text>().text);
        }

        [Test]
        public void Create_ChatView_ReportsInputFieldAsUnwired()
        {
            _harness = new PresentationTestHarness();

            PresentationFixture fixture = PresentationFixtureFactory.Create<HudChatView>(LABEL);

            bool foundInput = false;
            for (int i = 0; i < fixture.UnwiredFields.Count; ++i)
            {
                if (fixture.UnwiredFields[i].StartsWith("_input", StringComparison.Ordinal))
                {
                    foundInput = true;
                }
            }

            Assert.IsTrue(foundInput, "입력 필드는 조립하지 않고 미연결로 남긴다");
        }

        [Test]
        public void Create_TopLeftAnchorWithOffset_AppliesAnchorPivotAndPosition()
        {
            _harness = new PresentationTestHarness();

            PresentationFixture fixture = PresentationFixtureFactory.Create<HudPartyView>(
                LABEL, new PresentationFixtureOptions(EFixtureAnchor.TopLeft).WithOffset(10.0f, -20.0f));

            RectTransform rect = fixture.Root.GetComponent<RectTransform>();
            Assert.AreEqual(new Vector2(0.0f, 1.0f), rect.anchorMin);
            Assert.AreEqual(new Vector2(0.0f, 1.0f), rect.anchorMax);
            Assert.AreEqual(new Vector2(0.0f, 1.0f), rect.pivot);
            Assert.AreEqual(new Vector2(10.0f, -20.0f), rect.anchoredPosition);
        }

        /****************************************
        *        Canvas lifetime / isolation
        ****************************************/

        [Test]
        public void ActiveFixtureCount_CreateThenDestroy_TracksFixtures()
        {
            _harness = new PresentationTestHarness();
            Assert.AreEqual(0, PresentationFixtureFactory.ActiveFixtureCount);

            PresentationFixture first  = PresentationFixtureFactory.Create<HudPartyView>("A");
            PresentationFixture second = PresentationFixtureFactory.Create<HudPartyView>("B");
            Assert.AreEqual(2, PresentationFixtureFactory.ActiveFixtureCount);

            first.Destroy();
            Assert.IsFalse(first.IsAlive);
            Assert.AreEqual(1, PresentationFixtureFactory.ActiveFixtureCount);

            second.Destroy();
            Assert.AreEqual(0, PresentationFixtureFactory.ActiveFixtureCount);
        }

        [Test]
        public void ReleaseCanvasIfUnused_AfterLastDestroy_RemovesCanvas()
        {
            _harness = new PresentationTestHarness();
            PresentationFixture fixture = PresentationFixtureFactory.Create<HudPartyView>(LABEL);
            Assert.AreEqual(1, PresentationTestHarness.CountFixtureCanvases());

            fixture.Destroy();
            PresentationFixtureFactory.ReleaseCanvasIfUnused();

            Assert.AreEqual(0, PresentationTestHarness.CountFixtureCanvases());
        }

        [Test]
        public void ReleaseCanvasIfUnused_FixtureStillAlive_KeepsCanvas()
        {
            _harness = new PresentationTestHarness();
            PresentationFixture fixture = PresentationFixtureFactory.Create<HudPartyView>(LABEL);

            PresentationFixtureFactory.ReleaseCanvasIfUnused();

            Assert.IsTrue(fixture.IsAlive);
            Assert.AreEqual(1, PresentationTestHarness.CountFixtureCanvases());
        }

        [Test]
        public void DestroyOrphans_FlaggedCanvas_IsRemovedWithItsFixtures()
        {
            _harness = new PresentationTestHarness();
            PresentationFixture fixture = PresentationFixtureFactory.Create<HudPartyView>(LABEL);

            PresentationFixtureFactory.DestroyOrphans();

            Assert.IsFalse(fixture.IsAlive);
            Assert.AreEqual(0, PresentationTestHarness.CountFixtureCanvases());
        }

        [Test]
        public void DestroyOrphans_SameNameWithoutFixtureFlag_LeavesUserObjectAlone()
        {
            _harness = new PresentationTestHarness();
            GameObject userCanvas = _harness.Spawn(PresentationFixtureFactory.CANVAS_NAME);
            userCanvas.AddComponent<Canvas>();

            PresentationFixtureFactory.DestroyOrphans();

            Assert.IsTrue(userCanvas != null, "숨김 플래그가 없는 같은 이름 객체는 지우지 않는다");
        }

        [Test]
        public void Create_OpenScope_DoesNotTouchActiveSceneDirtyStateOrRoots()
        {
            _harness = new PresentationTestHarness();
            Scene active = SceneManager.GetActiveScene();
            bool dirtyBefore     = active.isDirty;
            int  rootCountBefore = active.rootCount;

            PresentationFixture fixture = PresentationFixtureFactory.Create<HudPartyView>(LABEL);

            Assert.AreEqual(dirtyBefore, active.isDirty);
            Assert.AreEqual(rootCountBefore, active.rootCount);
            Assert.AreEqual(_harness.Scope.Scene, fixture.Root.scene);
        }
    }
}
