using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Shinshi.Editor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Shinshi.Tests
{
    public sealed class UiSandboxWindowTests
    {
        private const string LOCK_KEY = "locked";
        private const double START_TIME = 100.0;

        private double _now = START_TIME;
        private UiSandboxContext _context = null;

        // 요청 전에 View를 먼저 바꾸고 성공 콜백에서만 시나리오를 바꾸는 화면. 취소하면 View만 달라진 채 남는 상황을 만든다
        private sealed class OptimisticPage : IUiSandboxPage
        {
            private readonly string _requirement;
            private readonly bool _hasView;
            private UiSandboxContext _attached = null;

            public bool ViewLocked = false;
            public bool SuccessCalled = false;
            public int AttachCount = 0;
            public int ApplyCount = 0;
            public int DetachCount = 0;
            public bool ThrowOnApply = false;

            public OptimisticPage(string requirement = "", bool hasView = true)
            {
                _requirement = requirement;
                _hasView = hasView;
            }

            public string Id { get { return "optimistic"; } }
            public string Title { get { return "낙관적 화면"; } }
            public string SourceScenePath { get { return _hasView ? "Assets/00.Scenes/Release/Town.unity" : string.Empty; } }
            public string Requirement { get { return _requirement; } }
            public bool HasView { get { return _hasView; } }

            public void Attach(UiSandboxContext context)
            {
                ++AttachCount;
                _attached = context;
            }

            public void Apply(UiSandboxContext context)
            {
                ++ApplyCount;
                if (ThrowOnApply)
                {
                    throw new InvalidOperationException("apply failed");
                }

                ViewLocked = context.Scenario.GetBool(LOCK_KEY, false);
            }

            public void DrawControls(UiSandboxContext context) { }

            public void Detach()
            {
                ++DetachCount;
                _attached = null;
            }

            public bool ToggleLockOptimistically()
            {
                bool next = !ViewLocked;
                ViewLocked = next;
                return _attached.Request("toggle_lock", () =>
                {
                    SuccessCalled = true;
                    _attached.Scenario.SetBool(LOCK_KEY, next);
                });
            }
        }

        [SetUp]
        public void SetUp()
        {
            _now = START_TIME;
            _context = new UiSandboxContext(null, null, () => _now, () => true, () => null);
            _context.Scenario.SetString(UiSandboxScenarioKeys.RESPONSE_MODE, EUiSandboxResponseMode.Success.ToString());
            _context.Scenario.SetFloat(UiSandboxScenarioKeys.RESPONSE_DELAY, 1.0f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_context != null)
            {
                _context.Dispose();
                _context = null;
            }
        }

        private OptimisticPage AttachedPage()
        {
            OptimisticPage page = new OptimisticPage();
            page.Attach(_context);
            page.Apply(_context);
            return page;
        }

        /****************************************
        *            Cancel And Restore
        ****************************************/

        [Test]
        public void CancelAndRestore_OptimisticViewChange_IsRevertedToScenarioWithoutCallbacks()
        {
            OptimisticPage page = AttachedPage();
            Assert.IsTrue(page.ToggleLockOptimistically());
            Assert.IsTrue(page.ViewLocked);
            Assert.IsTrue(_context.IsBusy);

            bool canceled = UiSandboxWindow.CancelAndRestore(_context, page, true, "사용자 취소");

            Assert.IsTrue(canceled);
            Assert.IsFalse(page.ViewLocked, "취소 뒤 View는 시나리오 값으로 돌아와야 한다");
            Assert.IsFalse(_context.Scenario.GetBool(LOCK_KEY, false), "시나리오 값은 바뀌지 않는다");
            Assert.IsFalse(_context.IsBusy);
            Assert.AreEqual(EUiSandboxRequestState.Canceled, _context.History[_context.History.Count - 1].State);
            StringAssert.Contains("되돌렸다", _context.Status);
        }

        [Test]
        public void CancelAndRestore_AfterCancel_LateTickNeverRunsCallbacks()
        {
            OptimisticPage page = AttachedPage();
            page.ToggleLockOptimistically();

            UiSandboxWindow.CancelAndRestore(_context, page, true, "사용자 취소");
            _now += 10.0;
            _context.Tick(_now);

            Assert.IsFalse(page.SuccessCalled);
            Assert.IsFalse(_context.Scenario.GetBool(LOCK_KEY, false));
            Assert.IsFalse(page.ViewLocked);
        }

        [Test]
        public void CancelAndRestore_ReattachesPageOnce()
        {
            OptimisticPage page = AttachedPage();
            page.ToggleLockOptimistically();
            int attach = page.AttachCount;
            int apply = page.ApplyCount;
            int detach = page.DetachCount;

            UiSandboxWindow.CancelAndRestore(_context, page, true, "사용자 취소");

            Assert.AreEqual(detach + 1, page.DetachCount);
            Assert.AreEqual(attach + 1, page.AttachCount);
            Assert.AreEqual(apply + 1, page.ApplyCount);
        }

        [Test]
        public void CancelAndRestore_NothingPending_ReturnsFalseAndLeavesPageAlone()
        {
            OptimisticPage page = AttachedPage();
            int attach = page.AttachCount;
            int detach = page.DetachCount;

            bool canceled = UiSandboxWindow.CancelAndRestore(_context, page, true, "사용자 취소");

            Assert.IsFalse(canceled);
            Assert.AreEqual(attach, page.AttachCount);
            Assert.AreEqual(detach, page.DetachCount);
        }

        [Test]
        public void CancelAndRestore_UnsafeHost_CancelsButDoesNotTouchPage()
        {
            OptimisticPage page = AttachedPage();
            page.ToggleLockOptimistically();
            int attach = page.AttachCount;
            int detach = page.DetachCount;

            bool canceled = UiSandboxWindow.CancelAndRestore(_context, page, false, "사용자 취소");

            Assert.IsTrue(canceled);
            Assert.IsFalse(_context.IsBusy);
            Assert.AreEqual(attach, page.AttachCount);
            Assert.AreEqual(detach, page.DetachCount);
        }

        [Test]
        public void CancelAndRestore_NullPage_StillCancelsPendingRequest()
        {
            OptimisticPage page = AttachedPage();
            page.ToggleLockOptimistically();

            Assert.IsTrue(UiSandboxWindow.CancelAndRestore(_context, null, true, "사용자 취소"));
            Assert.IsFalse(_context.IsBusy);
        }

        [Test]
        public void CancelAndRestore_NullOrDisposedContext_ReturnsFalse()
        {
            OptimisticPage page = AttachedPage();
            Assert.IsFalse(UiSandboxWindow.CancelAndRestore(null, page, true, "사용자 취소"));

            _context.Dispose();
            Assert.IsFalse(UiSandboxWindow.CancelAndRestore(_context, page, true, "사용자 취소"));
        }

        [Test]
        public void CancelAndRestore_PageThrowsWhileRestoring_ReportsInStatusAndKeepsCancel()
        {
            OptimisticPage page = AttachedPage();
            page.ToggleLockOptimistically();
            page.ThrowOnApply = true;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException"));

            bool canceled = UiSandboxWindow.CancelAndRestore(_context, page, true, "사용자 취소");

            Assert.IsTrue(canceled);
            Assert.IsFalse(_context.IsBusy);
            StringAssert.Contains("InvalidOperationException", _context.Status);
        }

        /****************************************
        *             Requirement Text
        ****************************************/

        [Test]
        public void DescribeRequirement_RealViewPage_ShowsLimitations()
        {
            OptimisticPage page = new OptimisticPage("레거시 직업 3종 · 웨이브 · 임시 배치 한계", true);

            string text = UiSandboxWindow.DescribeRequirement(page);

            StringAssert.Contains("레거시 직업 3종", text);
            StringAssert.Contains("제약과 한계", text);
        }

        [Test]
        public void DescribeRequirement_ContractOnlyPage_ShowsRequiredView()
        {
            OptimisticPage page = new OptimisticPage("상점 화면이 필요하다", false);

            string text = UiSandboxWindow.DescribeRequirement(page);

            StringAssert.Contains("상점 화면이 필요하다", text);
            StringAssert.Contains("필요한 화면", text);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void DescribeRequirement_BlankRequirement_ReturnsEmpty(string requirement)
        {
            Assert.AreEqual(string.Empty, UiSandboxWindow.DescribeRequirement(new OptimisticPage(requirement, true)));
            Assert.AreEqual(string.Empty, UiSandboxWindow.DescribeRequirement(new OptimisticPage(requirement, false)));
        }

        [Test]
        public void DescribeRequirement_NullPage_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, UiSandboxWindow.DescribeRequirement(null));
        }
    }
}
