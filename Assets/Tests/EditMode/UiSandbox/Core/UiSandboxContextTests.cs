using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Shinshi.Editor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Shinshi.Tests
{
    public sealed class UiSandboxContextTests
    {
        private const double START_TIME = 100.0;

        private double _now = START_TIME;
        private bool _playing = true;
        private UiSandboxScenarioSO _template = null;
        private UiSandboxContext _context = null;
        private string _unsafeReason = null;
        private UiSandboxTestScene _scene = null;

        [SetUp]
        public void SetUp()
        {
            _now = START_TIME;
            _playing = true;
            _unsafeReason = null;
            _scene = UiSandboxTestScene.CreatePreview();
            _template = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
            _context = Create(null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_context != null)
            {
                _context.Dispose();
                _context = null;
            }

            if (_scene != null)
            {
                _scene.Dispose();
                _scene = null;
            }

            UnityEngine.Object.DestroyImmediate(_template);
            _template = null;
        }

        // 실제 Mirror 상태에 기대지 않도록 안전 판정은 항상 주입한다
        private UiSandboxContext Create(GameObject[] roots)
        {
            return new UiSandboxContext(_template, roots, () => _now, () => _playing, () => _unsafeReason);
        }

        private GameObject Spawn(string name, GameObject parent = null)
        {
            return _scene.Spawn(name, parent);
        }

        private void SetMode(EUiSandboxResponseMode mode, float delay = 0.0f, string error = "")
        {
            _context.Scenario.SetString(UiSandboxScenarioKeys.RESPONSE_MODE, mode.ToString());
            _context.Scenario.SetFloat(UiSandboxScenarioKeys.RESPONSE_DELAY, delay);
            _context.Scenario.SetString(UiSandboxScenarioKeys.RESPONSE_ERROR, error);
        }

        private UiSandboxRequestRecord LastRecord()
        {
            return _context.History[_context.History.Count - 1];
        }

        /****************************************
        *               Snapshot
        ****************************************/

        [Test]
        public void RestoreSnapshot_NewContext_RecoversWorkingAndBaselineValues()
        {
            _template.SetInt("gold", 10);
            UiSandboxContext first = Create(null);
            first.Scenario.SetInt("gold", 999);
            first.Scenario.SetString("extra", "편집 중");
            string working = first.CaptureWorkingJson();
            string baseline = first.CaptureBaselineJson();
            first.Dispose();

            Assert.IsTrue(_context.RestoreSnapshot(working, baseline));

            Assert.AreEqual(999, _context.Scenario.GetInt("gold"));
            Assert.AreEqual("편집 중", _context.Scenario.GetString("extra"));

            _context.ResetScenario();
            Assert.AreEqual(10, _context.Scenario.GetInt("gold"));
            Assert.IsFalse(_context.Scenario.Contains("extra"));
        }

        [Test]
        public void RestoreSnapshot_BrokenWorkingJson_ReturnsFalseAndKeepsValues()
        {
            _context.Scenario.SetInt("keep", 3);

            Assert.IsFalse(_context.RestoreSnapshot("{ 깨짐", string.Empty));

            Assert.AreEqual(3, _context.Scenario.GetInt("keep"));
        }

        [Test]
        public void RestoreSnapshot_BrokenBaselineJson_UsesWorkingValuesAsBaseline()
        {
            _context.Scenario.SetInt("gold", 5);
            string working = _context.CaptureWorkingJson();
            _context.Scenario.SetInt("gold", 6);

            Assert.IsTrue(_context.RestoreSnapshot(working, "깨진 기준"));
            _context.Scenario.SetInt("gold", 7);
            _context.ResetScenario();

            Assert.AreEqual(5, _context.Scenario.GetInt("gold"));
        }

        [Test]
        public void RestoreSnapshot_PendingRequest_IsCancelled()
        {
            SetMode(EUiSandboxResponseMode.Success, 3.0f);
            int successCount = 0;
            _context.Request("구매", () => ++successCount);
            string working = _context.CaptureWorkingJson();

            Assert.IsTrue(_context.RestoreSnapshot(working, working));
            _context.Tick(_now + 10.0);

            Assert.AreEqual(0, successCount);
            Assert.IsFalse(_context.IsBusy);
        }

        [Test]
        public void Capture_AfterDispose_ReturnsEmptyAndRestoreFails()
        {
            _context.Dispose();

            Assert.AreEqual(string.Empty, _context.CaptureWorkingJson());
            Assert.AreEqual(string.Empty, _context.CaptureBaselineJson());
            Assert.IsFalse(_context.RestoreSnapshot("{\"Entries\":[]}", "{\"Entries\":[]}"));
        }

        /****************************************
        *               Request
        ****************************************/

        [Test]
        public void Request_NotPlaying_ReturnsFalseAndRecordsBlocked()
        {
            _playing = false;
            int successCount = 0;

            bool started = _context.Request("구매", () => ++successCount);

            Assert.IsFalse(started);
            Assert.IsFalse(_context.IsBusy);
            Assert.AreEqual(EUiSandboxRequestState.Blocked, LastRecord().State);
            StringAssert.Contains("Play", _context.Status);
            _context.Tick(_now + 10.0);
            Assert.AreEqual(0, successCount);
        }

        [Test]
        public void Request_EmptyOperation_ReturnsFalse()
        {
            Assert.IsFalse(_context.Request("  ", () => { }));
            Assert.IsFalse(_context.IsBusy);
        }

        [Test]
        public void Request_SuccessZeroDelay_CompletesOnTick()
        {
            int successCount = 0;
            SetMode(EUiSandboxResponseMode.Success);

            Assert.IsTrue(_context.Request("구매", () => ++successCount));
            Assert.IsTrue(_context.IsBusy);
            Assert.AreEqual("구매", _context.PendingOperation);
            Assert.AreEqual(0, successCount);

            _context.Tick(_now);

            Assert.AreEqual(1, successCount);
            Assert.IsFalse(_context.IsBusy);
            Assert.AreEqual(EUiSandboxRequestState.Succeeded, LastRecord().State);
        }

        [Test]
        public void Tick_BeforeDueTime_KeepsRequestPending()
        {
            int successCount = 0;
            SetMode(EUiSandboxResponseMode.Success, 2.0f);
            _context.Request("구매", () => ++successCount);

            _context.Tick(_now + 1.9);
            Assert.IsTrue(_context.IsBusy);
            Assert.AreEqual(0, successCount);

            _context.Tick(_now + 2.0);
            Assert.IsFalse(_context.IsBusy);
            Assert.AreEqual(1, successCount);
        }

        [TestCase(EUiSandboxResponseMode.Rejected, UiSandboxResponsePolicy.DEFAULT_REJECTED)]
        [TestCase(EUiSandboxResponseMode.Disconnected, UiSandboxResponsePolicy.DEFAULT_DISCONNECTED)]
        [TestCase(EUiSandboxResponseMode.Timeout, UiSandboxResponsePolicy.DEFAULT_TIMEOUT)]
        public void Tick_FailureMode_InvokesFailureWithDefaultMessage(EUiSandboxResponseMode mode, string expected)
        {
            int successCount = 0;
            string failure = null;
            SetMode(mode);
            _context.Request("구매", () => ++successCount, message => failure = message);

            _context.Tick(_now);

            Assert.AreEqual(0, successCount);
            Assert.AreEqual(expected, failure);
            Assert.AreEqual(EUiSandboxRequestState.Failed, LastRecord().State);
            Assert.AreEqual(mode, LastRecord().Mode);
        }

        [Test]
        public void Tick_FailureWithCustomError_UsesCustomMessage()
        {
            string failure = null;
            SetMode(EUiSandboxResponseMode.Rejected, 0.0f, "골드가 부족합니다");
            _context.Request("구매", () => { }, message => failure = message);

            _context.Tick(_now);

            Assert.AreEqual("골드가 부족합니다", failure);
        }

        [Test]
        public void Tick_FailureWithoutFailureCallback_DoesNotThrow()
        {
            SetMode(EUiSandboxResponseMode.Timeout);
            _context.Request("구매", () => { });

            Assert.DoesNotThrow(() => _context.Tick(_now));
            Assert.IsFalse(_context.IsBusy);
        }

        [Test]
        public void Request_WhileBusy_ReturnsFalseAndRecordsBlocked()
        {
            int firstCount = 0;
            int secondCount = 0;
            _context.Request("첫째", () => ++firstCount);

            bool second = _context.Request("둘째", () => ++secondCount);

            Assert.IsFalse(second);
            Assert.AreEqual(EUiSandboxRequestState.Blocked, LastRecord().State);
            Assert.AreEqual("둘째", LastRecord().Operation);

            _context.Tick(_now);
            Assert.AreEqual(1, firstCount);
            Assert.AreEqual(0, secondCount);
        }

        [Test]
        public void Request_ScenarioChangedAfterStart_UsesPolicySnapshot()
        {
            int successCount = 0;
            string failure = null;
            SetMode(EUiSandboxResponseMode.Success, 1.0f);
            _context.Request("구매", () => ++successCount, message => failure = message);

            SetMode(EUiSandboxResponseMode.Rejected, 100.0f, "나중에 바꾼 값");
            _context.Tick(_now + 1.0);

            Assert.AreEqual(1, successCount);
            Assert.IsNull(failure);
        }

        [Test]
        public void Request_DelayAboveLimit_IsCapped()
        {
            SetMode(EUiSandboxResponseMode.Success, 99999.0f);
            _context.Request("구매", () => { });

            Assert.AreEqual(UiSandboxScenarioKeys.MAX_DELAY_SECONDS, LastRecord().DelaySeconds);
        }

        [Test]
        public void Callback_StartsNewRequest_IsAllowed()
        {
            bool nestedStarted = false;
            int nestedCount = 0;
            _context.Request("첫째", () => nestedStarted = _context.Request("둘째", () => ++nestedCount));

            _context.Tick(_now);
            Assert.IsTrue(nestedStarted);
            Assert.IsTrue(_context.IsBusy);

            _context.Tick(_now);
            Assert.AreEqual(1, nestedCount);
            Assert.IsFalse(_context.IsBusy);
        }

        [Test]
        public void Callback_Throws_ContextStaysUsable()
        {
            LogAssert.Expect(LogType.Exception, new Regex("boom"));
            _context.Request("첫째", () => throw new System.InvalidOperationException("boom"));

            _context.Tick(_now);

            Assert.IsFalse(_context.IsBusy);
            int successCount = 0;
            Assert.IsTrue(_context.Request("둘째", () => ++successCount));
            _context.Tick(_now);
            Assert.AreEqual(1, successCount);
        }

        /****************************************
        *             Cancellation
        ****************************************/

        [Test]
        public void Dispose_WithPendingRequest_CancelsWithoutCallbacks()
        {
            int successCount = 0;
            int failureCount = 0;
            SetMode(EUiSandboxResponseMode.Rejected, 1.0f);
            _context.Request("구매", () => ++successCount, message => ++failureCount);
            UiSandboxRequestRecord record = LastRecord();

            _context.Dispose();

            Assert.AreEqual(EUiSandboxRequestState.Canceled, record.State);
            Assert.AreEqual(0, successCount);
            Assert.AreEqual(0, failureCount);
            Assert.IsFalse(_context.IsBusy);
        }

        [Test]
        public void ResetScenario_WithPendingRequest_CancelsAndRestoresBaseline()
        {
            int successCount = 0;
            _template.SetInt("gold", 10);
            _context.LoadScenario(_template);
            _context.Scenario.SetInt("gold", 99);
            _context.Request("구매", () => ++successCount);

            _context.ResetScenario();
            _context.Tick(_now + 5.0);

            Assert.AreEqual(0, successCount);
            Assert.AreEqual(EUiSandboxRequestState.Canceled, LastRecord().State);
            Assert.AreEqual(10, _context.Scenario.GetInt("gold"));
        }

        [Test]
        public void LoadScenario_WithPendingRequest_CancelsAndReplacesValues()
        {
            int successCount = 0;
            UiSandboxScenarioSO preset = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
            try
            {
                preset.SetInt("gold", 77);
                _context.Request("구매", () => ++successCount);

                _context.LoadScenario(preset);
                _context.Tick(_now + 5.0);

                Assert.AreEqual(0, successCount);
                Assert.AreEqual(77, _context.Scenario.GetInt("gold"));
                Assert.AreEqual(EUiSandboxRequestState.Canceled, LastRecord().State);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void Tick_PlayStoppedDuringRequest_CancelsInsteadOfCompleting()
        {
            int successCount = 0;
            _context.Request("구매", () => ++successCount);

            _playing = false;
            _context.Tick(_now + 5.0);

            Assert.AreEqual(0, successCount);
            Assert.AreEqual(EUiSandboxRequestState.Canceled, LastRecord().State);
            Assert.IsFalse(_context.IsBusy);
        }

        [Test]
        public void CancelPending_NothingPending_ReturnsFalse()
        {
            Assert.IsFalse(_context.CancelPending("없음"));
        }

        [Test]
        public void CancelPending_WithPending_ReturnsTrueAndSkipsCallbacks()
        {
            int successCount = 0;
            _context.Request("구매", () => ++successCount);

            Assert.IsTrue(_context.CancelPending("사용자"));
            _context.Tick(_now + 5.0);

            Assert.AreEqual(0, successCount);
        }

        [Test]
        public void Rebind_WithPendingRequest_CancelsIt()
        {
            int successCount = 0;
            _context.Request("구매", () => ++successCount);

            _context.Rebind(new GameObject[0]);
            _context.Tick(_now + 5.0);

            Assert.AreEqual(0, successCount);
            Assert.AreEqual(EUiSandboxRequestState.Canceled, LastRecord().State);
        }

        /****************************************
        *               Lifecycle
        ****************************************/

        [Test]
        public void Dispose_Twice_IsSafe()
        {
            _context.Dispose();

            Assert.DoesNotThrow(() => _context.Dispose());
            Assert.IsTrue(_context.IsDisposed);
        }

        [Test]
        public void Request_AfterDispose_ReturnsFalse()
        {
            _context.Dispose();

            Assert.IsFalse(_context.Request("구매", () => { }));
        }

        [Test]
        public void Dispose_DestroysOnlyOwnedCopyAndKeepsTemplate()
        {
            _template.SetInt("gold", 5);
            UiSandboxScenarioSO owned = _context.Scenario;

            _context.Dispose();

            Assert.IsTrue(_template != null);
            Assert.AreEqual(5, _template.GetInt("gold"));
            Assert.IsTrue(owned == null);
            Assert.IsNull(_context.Scenario);
        }

        [Test]
        public void Scenario_ModifyingContextCopy_DoesNotChangeTemplate()
        {
            _template.SetInt("gold", 5);
            UiSandboxContext context = Create(null);
            try
            {
                context.Scenario.SetInt("gold", 500);

                Assert.AreEqual(5, _template.GetInt("gold"));
            }
            finally
            {
                context.Dispose();
            }
        }

        [Test]
        public void Constructor_NullTemplate_ProvidesEmptyScenario()
        {
            UiSandboxContext context = new UiSandboxContext(null, null, () => _now, () => _playing, () => _unsafeReason);
            try
            {
                Assert.IsNotNull(context.Scenario);
                Assert.AreEqual(0, context.Scenario.Count);
                Assert.IsNull(context.Find<BoxCollider>());
            }
            finally
            {
                context.Dispose();
            }
        }

        [Test]
        public void History_MoreThanLimit_TrimsOldestRecords()
        {
            _playing = false;

            for (int i = 0; i < 60; ++i)
            {
                _context.Request("op" + i, () => { });
            }

            Assert.AreEqual(50, _context.History.Count);
            Assert.AreEqual(11, _context.History[0].Id);
            Assert.AreEqual(60, _context.History[49].Id);
        }

        [Test]
        public void ClearHistory_RemovesRecords()
        {
            _playing = false;
            _context.Request("op", () => { });

            _context.ClearHistory();

            Assert.AreEqual(0, _context.History.Count);
        }

        [Test]
        public void SetStatus_Message_IsExposed()
        {
            _context.SetStatus("준비 완료");

            Assert.AreEqual("준비 완료", _context.Status);
        }

        /****************************************
        *            Unsafe Context
        ****************************************/

        [Test]
        public void Request_UnsafeContext_IsBlockedWithReasonAndNeverCompletes()
        {
            _unsafeReason = "접속이 살아 있다";
            int successCount = 0;
            int failureCount = 0;

            bool started = _context.Request("구매", () => ++successCount, message => ++failureCount);
            _context.Tick(_now + 10.0);

            Assert.IsFalse(started);
            Assert.IsFalse(_context.IsBusy);
            Assert.AreEqual(EUiSandboxRequestState.Blocked, LastRecord().State);
            StringAssert.Contains("접속이 살아 있다", LastRecord().Message);
            Assert.AreEqual("접속이 살아 있다", _context.Status);
            Assert.AreEqual(0, successCount);
            Assert.AreEqual(0, failureCount);
        }

        [Test]
        public void Request_UnsafeContextWhileNotPlaying_ReportsSafetyReason()
        {
            _playing = false;
            _unsafeReason = "원본 씬이다";

            _context.Request("구매", () => { });

            StringAssert.Contains("원본 씬이다", LastRecord().Message);
        }

        [Test]
        public void Tick_ConnectionAppearsAfterRequestStarted_CancelsWithoutCallbacks()
        {
            SetMode(EUiSandboxResponseMode.Success, 1.0f);
            int successCount = 0;
            int failureCount = 0;
            Assert.IsTrue(_context.Request("구매", () => ++successCount, message => ++failureCount));

            _unsafeReason = "Play 진입 뒤 접속이 생겼다";
            _context.Tick(_now + 5.0);

            Assert.IsFalse(_context.IsBusy);
            Assert.AreEqual(EUiSandboxRequestState.Canceled, LastRecord().State);
            StringAssert.Contains("Play 진입 뒤 접속이 생겼다", LastRecord().Message);
            Assert.AreEqual(0, successCount);
            Assert.AreEqual(0, failureCount);
        }

        [Test]
        public void Tick_SafeContext_StillCompletesNormally()
        {
            int successCount = 0;
            Assert.IsTrue(_context.Request("구매", () => ++successCount));

            _context.Tick(_now);

            Assert.AreEqual(1, successCount);
        }

        [Test]
        public void Request_AfterUnsafeContextBecomesSafe_StartsAgain()
        {
            _unsafeReason = "접속";
            Assert.IsFalse(_context.Request("구매", () => { }));

            _unsafeReason = null;

            Assert.IsTrue(_context.Request("구매", () => { }));
            Assert.IsTrue(_context.IsBusy);
        }

        [Test]
        public void UnsafeReason_ReflectsProvider()
        {
            Assert.IsNull(_context.UnsafeReason);

            _unsafeReason = "막힘";

            Assert.AreEqual("막힘", _context.UnsafeReason);
        }

        /****************************************
        *             Scene Lookup
        ****************************************/

        [Test]
        public void Find_ComponentUnderRoot_ReturnsIncludingInactiveChildren()
        {
            GameObject root = Spawn("root");
            GameObject child = Spawn("child", root);
            BoxCollider collider = child.AddComponent<BoxCollider>();
            child.SetActive(false);
            _context.Rebind(new[] { root });

            Assert.AreSame(collider, _context.Find<BoxCollider>());
            Assert.AreEqual(1, _context.FindAll<BoxCollider>().Length);
        }

        [Test]
        public void Find_ComponentOutsideRoots_IsNotReturned()
        {
            GameObject inside = Spawn("inside");
            GameObject outside = Spawn("outside");
            outside.AddComponent<BoxCollider>();
            _context.Rebind(new[] { inside });

            Assert.IsNull(_context.Find<BoxCollider>());
            Assert.AreEqual(0, _context.FindAll<BoxCollider>().Length);
        }

        [Test]
        public void FindAll_MultipleRoots_ReturnsComponentsFromAll()
        {
            GameObject a = Spawn("a");
            GameObject b = Spawn("b");
            a.AddComponent<BoxCollider>();
            b.AddComponent<BoxCollider>();
            _context.Rebind(new[] { a, b });

            Assert.AreEqual(2, _context.FindAll<BoxCollider>().Length);
        }

        [Test]
        public void Find_CachedComponentDestroyed_NoLongerReturned()
        {
            GameObject root = Spawn("root");
            BoxCollider collider = root.AddComponent<BoxCollider>();
            _context.Rebind(new[] { root });
            Assert.AreSame(collider, _context.Find<BoxCollider>());

            UnityEngine.Object.DestroyImmediate(collider);

            Assert.IsNull(_context.Find<BoxCollider>());
        }

        [Test]
        public void Find_ComponentAddedAfterEmptyLookup_IsFoundOnNextCall()
        {
            GameObject root = Spawn("root");
            _context.Rebind(new[] { root });
            Assert.IsNull(_context.Find<BoxCollider>());

            BoxCollider added = root.AddComponent<BoxCollider>();

            Assert.AreSame(added, _context.Find<BoxCollider>());
        }

        [Test]
        public void Rebind_NewRoots_FindsOnlyNewComponents()
        {
            GameObject first = Spawn("first");
            GameObject second = Spawn("second");
            first.AddComponent<BoxCollider>();
            BoxCollider expected = second.AddComponent<BoxCollider>();
            _context.Rebind(new[] { first });
            Assert.IsNotNull(_context.Find<BoxCollider>());

            _context.Rebind(new[] { second });

            Assert.AreSame(expected, _context.Find<BoxCollider>());
        }

        [Test]
        public void Find_AfterDispose_ReturnsNull()
        {
            GameObject root = Spawn("root");
            root.AddComponent<BoxCollider>();
            _context.Rebind(new[] { root });

            _context.Dispose();

            Assert.IsNull(_context.Find<BoxCollider>());
            Assert.IsTrue(root != null);
        }
    }
}
