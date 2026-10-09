// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Editor;

namespace Shinshi.Tests
{
    /// <summary> 계약 전용 경제 화면 8개의 라벨, 가짜 응답 처리, 요청 시점 값 고정, 분리와 리셋 때의 취소를 확인한다 </summary>
    public sealed class ContractOnlySandboxPageTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int OP_RECORD = 0;
        private const int OP_APPEND = 1;
        private const int OP_REMOVE = 2;

        private EconomyTestHarness      _harness;
        private ContractOnlySandboxPage _page;

        /****************************************
        *                Methods
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _harness = new EconomyTestHarness();
            _page    = CreatePage(0);
            _harness.AttachAndApply(_page);
        }

        [TearDown]
        public void TearDown()
        {
            _harness?.Dispose();
            _harness = null;
            _page    = null;
        }

        [Test]
        public void Attach_AllDefinitions_AreLabeledContractOnlyWithoutView()
        {
            ContractOnlyDefinition[] definitions = ContractOnlyDefinitions.All();
            _page.Detach();

            for (int i = 0; i < definitions.Length; ++i)
            {
                ContractOnlySandboxPage page = new ContractOnlySandboxPage(definitions[i]);
                _harness.AttachAndApply(page);

                Assert.IsFalse(page.HasView, definitions[i].Id);
                Assert.IsEmpty(page.SourceScenePath, definitions[i].Id);
                StringAssert.StartsWith("계약 전용 화면이다", page.FixtureNote, definitions[i].Id);
                StringAssert.Contains("계약 전용", _harness.Context.Status, definitions[i].Id);
                page.Detach();
            }
        }

        [Test]
        public void RunOperation_Success_CompletesOnlyAfterTick()
        {
            Assert.IsTrue(_page.RunOperation(OP_RECORD));

            Assert.AreEqual(ContractOnlySandboxPage.STATUS_IDLE, Scenario().GetString(ContractOnlySandboxPage.KEY_STATUS));
            Assert.AreEqual(0, Scenario().GetInt(ContractOnlySandboxPage.KEY_OK_COUNT));

            _harness.Tick();

            Assert.AreEqual(ContractOnlySandboxPage.STATUS_OK, Scenario().GetString(ContractOnlySandboxPage.KEY_STATUS));
            Assert.AreEqual(1, Scenario().GetInt(ContractOnlySandboxPage.KEY_OK_COUNT));
            Assert.AreEqual(_page.Definition.Operations[OP_RECORD].Label, Scenario().GetString(ContractOnlySandboxPage.KEY_LAST_OP));
            Assert.AreEqual(EUiSandboxRequestState.Succeeded, _harness.LastRecord.State);
        }

        [Test]
        public void RunOperation_Rejected_RecordsFailureWithDefaultMessage()
        {
            _harness.SetMode(EUiSandboxResponseMode.Rejected);

            Assert.IsTrue(_page.RunOperation(OP_RECORD));
            _harness.Tick();

            Assert.AreEqual(ContractOnlySandboxPage.STATUS_FAIL, Scenario().GetString(ContractOnlySandboxPage.KEY_STATUS));
            Assert.AreEqual(1, Scenario().GetInt(ContractOnlySandboxPage.KEY_FAIL_COUNT));
            Assert.AreEqual(0, Scenario().GetInt(ContractOnlySandboxPage.KEY_OK_COUNT));
            StringAssert.Contains("요청이 거절되었습니다", _harness.Context.Status);
            Assert.AreEqual(EUiSandboxRequestState.Failed, _harness.LastRecord.State);
        }

        [Test]
        public void RunOperation_Disconnected_RecordsFailureWithDefaultMessage()
        {
            _harness.SetMode(EUiSandboxResponseMode.Disconnected);

            Assert.IsTrue(_page.RunOperation(OP_RECORD));
            _harness.Tick();

            Assert.AreEqual(1, Scenario().GetInt(ContractOnlySandboxPage.KEY_FAIL_COUNT));
            StringAssert.Contains("서버와의 연결이 끊겼습니다", _harness.Context.Status);
        }

        [Test]
        public void RunOperation_Timeout_RecordsFailureAfterLongWait()
        {
            _harness.SetMode(EUiSandboxResponseMode.Timeout);

            Assert.IsTrue(_page.RunOperation(OP_RECORD));
            _harness.Advance(1000.0);

            Assert.AreEqual(1, Scenario().GetInt(ContractOnlySandboxPage.KEY_FAIL_COUNT));
            StringAssert.Contains("요청 시간이 초과되었습니다", _harness.Context.Status);
        }

        [Test]
        public void RunOperation_CustomErrorText_TakesPriorityOverDefaultMessage()
        {
            _harness.SetMode(EUiSandboxResponseMode.Rejected, 0.0f, "재료가 부족하다");

            Assert.IsTrue(_page.RunOperation(OP_RECORD));
            _harness.Tick();

            StringAssert.Contains("재료가 부족하다", _harness.Context.Status);
            StringAssert.DoesNotContain("요청이 거절되었습니다", _harness.Context.Status);
        }

        [Test]
        public void RunOperation_DelayedResponse_CompletesOnlyAfterDelayElapsed()
        {
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);

            Assert.IsTrue(_page.RunOperation(OP_RECORD));
            _harness.Advance(1.0);
            Assert.AreEqual(0, Scenario().GetInt(ContractOnlySandboxPage.KEY_OK_COUNT));

            _harness.Advance(1.5);

            Assert.AreEqual(1, Scenario().GetInt(ContractOnlySandboxPage.KEY_OK_COUNT));
        }

        [Test]
        public void RunOperation_NotPlaying_ReturnsFalseAndLeavesCountsUntouched()
        {
            _harness.Playing = false;

            Assert.IsFalse(_page.RunOperation(OP_RECORD));
            _harness.Tick();

            Assert.AreEqual(EUiSandboxRequestState.Blocked, _harness.LastRecord.State);
            Assert.AreEqual(0, Scenario().GetInt(ContractOnlySandboxPage.KEY_OK_COUNT));
            Assert.AreEqual(0, Scenario().GetInt(ContractOnlySandboxPage.KEY_FAIL_COUNT));
        }

        [Test]
        public void RunOperation_WhileAnotherPending_IsBlockedAndCompletesOnce()
        {
            Assert.IsTrue(_page.RunOperation(OP_RECORD));

            Assert.IsFalse(_page.RunOperation(OP_RECORD));
            _harness.Tick();

            Assert.AreEqual(1, Scenario().GetInt(ContractOnlySandboxPage.KEY_OK_COUNT));
            Assert.AreEqual(2, _harness.Context.History.Count);
            Assert.AreEqual(EUiSandboxRequestState.Blocked, _harness.Context.History[1].State);
        }

        [Test]
        public void RunOperation_OutOfRangeIndex_ReturnsFalseWithoutHistory()
        {
            Assert.IsFalse(_page.RunOperation(-1));
            Assert.IsFalse(_page.RunOperation(_page.Definition.Operations.Length));

            Assert.AreEqual(0, _harness.Context.History.Count);
        }

        [Test]
        public void RunOperation_AppendResult_AddsRowFromRequestTimeValues()
        {
            Scenario().SetString(ContractOnlySandboxPage.KEY_RESULT_NAME, "요청 때 이름");
            Scenario().SetInt(ContractOnlySandboxPage.KEY_RESULT_AMOUNT, 7);
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);

            Assert.IsTrue(_page.RunOperation(OP_APPEND));
            Scenario().SetString(ContractOnlySandboxPage.KEY_RESULT_NAME, "나중에 바꾼 이름");
            Scenario().SetInt(ContractOnlySandboxPage.KEY_RESULT_AMOUNT, 99);
            _harness.Advance(3.0);

            Assert.AreEqual(1, EconomyList.Count(Scenario(), ContractOnlySandboxPage.ResultSchema));
            Assert.AreEqual("요청 때 이름", EconomyList.GetText(Scenario(), ContractOnlySandboxPage.ResultSchema, 0, "name"));
            Assert.AreEqual(7, EconomyList.GetInt(Scenario(), ContractOnlySandboxPage.ResultSchema, 0, "amount"));
        }

        [Test]
        public void RunOperation_AppendResultWhenListFull_FailsWithoutAddingRow()
        {
            EconomyList.SetCount(Scenario(), ContractOnlySandboxPage.ResultSchema, ContractOnlySandboxPage.ResultSchema.MaxCount);

            Assert.IsTrue(_page.RunOperation(OP_APPEND));
            _harness.Tick();

            Assert.AreEqual(ContractOnlySandboxPage.ResultSchema.MaxCount, EconomyList.Count(Scenario(), ContractOnlySandboxPage.ResultSchema));
            Assert.AreEqual(1, Scenario().GetInt(ContractOnlySandboxPage.KEY_FAIL_COUNT));
            StringAssert.Contains("결과 기록이 가득 찼다", _harness.Context.Status);
        }

        [Test]
        public void RunOperation_RemoveInput_RemovesTargetRow()
        {
            Scenario().SetInt(ContractOnlySandboxPage.KEY_TARGET_INDEX, 0);
            int before = EconomyList.Count(Scenario(), _page.Definition.InputList);

            Assert.IsTrue(_page.RunOperation(OP_REMOVE));
            _harness.Tick();

            Assert.AreEqual(before - 1, EconomyList.Count(Scenario(), _page.Definition.InputList));
            Assert.AreEqual("퀘스트", EconomyList.GetText(Scenario(), _page.Definition.InputList, 0, "name"));
        }

        [Test]
        public void RunOperation_RemoveInputTargetIndexEditedDuringDelay_StillRemovesCapturedRow()
        {
            Scenario().SetInt(ContractOnlySandboxPage.KEY_TARGET_INDEX, 0);
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);

            Assert.IsTrue(_page.RunOperation(OP_REMOVE));
            Scenario().SetInt(ContractOnlySandboxPage.KEY_TARGET_INDEX, 1);
            _harness.Advance(3.0);

            Assert.AreEqual(1, EconomyList.Count(Scenario(), _page.Definition.InputList));
            Assert.AreEqual("퀘스트", EconomyList.GetText(Scenario(), _page.Definition.InputList, 0, "name"));
        }

        [Test]
        public void RunOperation_RemoveInputRowRenamedDuringDelay_FailsAsStale()
        {
            Scenario().SetInt(ContractOnlySandboxPage.KEY_TARGET_INDEX, 0);
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);
            int before = EconomyList.Count(Scenario(), _page.Definition.InputList);

            Assert.IsTrue(_page.RunOperation(OP_REMOVE));
            EconomyList.SetText(Scenario(), _page.Definition.InputList, 0, "name", "다른 줄");
            _harness.Advance(3.0);

            Assert.AreEqual(before, EconomyList.Count(Scenario(), _page.Definition.InputList));
            Assert.AreEqual(1, Scenario().GetInt(ContractOnlySandboxPage.KEY_FAIL_COUNT));
            StringAssert.Contains("대상 줄이 이미 바뀌었다", _harness.Context.Status);
        }

        [Test]
        public void RunOperation_RemoveInputTargetOutsideList_ReturnsFalseWithoutHistory()
        {
            Scenario().SetInt(ContractOnlySandboxPage.KEY_TARGET_INDEX, 5);

            Assert.IsFalse(_page.RunOperation(OP_REMOVE));

            Assert.AreEqual(0, _harness.Context.History.Count);
            StringAssert.Contains("입력 목록 밖", _harness.Context.Status);
        }

        [Test]
        public void Detach_WithPendingRequest_CancelsAndFiresNoCallback()
        {
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);
            Assert.IsTrue(_page.RunOperation(OP_RECORD));

            _page.Detach();
            _harness.Advance(5.0);

            Assert.AreEqual(EUiSandboxRequestState.Canceled, _harness.LastRecord.State);
            Assert.AreEqual(0, Scenario().GetInt(ContractOnlySandboxPage.KEY_OK_COUNT));
            Assert.AreEqual(0, Scenario().GetInt(ContractOnlySandboxPage.KEY_FAIL_COUNT));
        }

        [Test]
        public void ResetScenario_WithPendingRequest_CancelsAndFiresNoCallback()
        {
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);
            Assert.IsTrue(_page.RunOperation(OP_RECORD));

            _harness.Context.ResetScenario();
            _harness.Advance(5.0);

            Assert.AreEqual(EUiSandboxRequestState.Canceled, _harness.LastRecord.State);
            Assert.AreEqual(0, Scenario().GetInt(ContractOnlySandboxPage.KEY_OK_COUNT));
        }

        [Test]
        public void Reattach_AfterDetach_CompletesExactlyOneRequestOnce()
        {
            _page.Detach();
            _harness.AttachAndApply(_page);

            Assert.IsTrue(_page.RunOperation(OP_RECORD));
            _harness.Tick();
            _harness.Tick();

            Assert.AreEqual(1, Scenario().GetInt(ContractOnlySandboxPage.KEY_OK_COUNT));
            Assert.AreEqual(1, _harness.Context.History.Count);
        }

        [Test]
        public void RunOperation_EveryDefinitionAndOperation_SucceedsWithDefaultData()
        {
            ContractOnlyDefinition[] definitions = ContractOnlyDefinitions.All();
            _page.Detach();

            for (int d = 0; d < definitions.Length; ++d)
            {
                ContractOnlySandboxPage page = new ContractOnlySandboxPage(definitions[d]);
                _harness.AttachAndApply(page);

                for (int i = 0; i < definitions[d].Operations.Length; ++i)
                {
                    int before = Scenario().GetInt(ContractOnlySandboxPage.KEY_OK_COUNT);
                    Scenario().SetInt(ContractOnlySandboxPage.KEY_TARGET_INDEX, 0);

                    Assert.IsTrue(page.RunOperation(i), definitions[d].Id + " / " + definitions[d].Operations[i].Label);
                    _harness.Tick();

                    Assert.AreEqual(before + 1, Scenario().GetInt(ContractOnlySandboxPage.KEY_OK_COUNT), definitions[d].Id + " / " + definitions[d].Operations[i].Label);
                    Assert.AreEqual(EUiSandboxRequestState.Succeeded, _harness.LastRecord.State, definitions[d].Id);
                }

                page.Detach();
            }
        }

        private UiSandboxScenarioSO Scenario()
        {
            return _harness.Context.Scenario;
        }

        private static ContractOnlySandboxPage CreatePage(int definitionIndex)
        {
            return new ContractOnlySandboxPage(ContractOnlyDefinitions.All()[definitionIndex]);
        }
    }
}
