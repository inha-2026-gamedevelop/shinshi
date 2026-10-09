using System.Reflection;
using NUnit.Framework;
using Shinshi.Common;
using Shinshi.Editor;
using Shinshi.UI;
using UnityEngine.UI;

namespace Shinshi.Tests
{
    public sealed class SortieResultPageTests
    {
        private PresentationTestHarness _harness;
        private SortieResultPage        _page;

        [SetUp]
        public void SetUp()
        {
            _harness = new PresentationTestHarness();
            _page    = _harness.Track(new SortieResultPage());
            _page.Attach(_harness.Context);
        }

        [TearDown]
        public void TearDown()
        {
            _harness.Dispose();
        }

        private int SaveStatus()
        {
            return _harness.Scenario.GetInt(SortieResultPage.KEY_SAVE_STATUS);
        }

        private static int CountOutcome(UiSandboxContext context, EUiSandboxRequestState state)
        {
            int count = 0;
            for (int i = 0; i < context.History.Count; ++i)
            {
                if (context.History[i].State == state)
                {
                    ++count;
                }
            }

            return count;
        }

        /****************************************
        *            Attach / Detach
        ****************************************/

        [Test]
        public void Attach_NoSceneView_BuildsFixtureAndSeedsSavedStatus()
        {
            Assert.IsNotNull(_page.View);
            Assert.IsTrue(_page.UsesFixture);
            Assert.AreEqual((int)ESaveStatus.Saved, SaveStatus());
            Assert.AreEqual("마을로 귀환했습니다 (모의)", _harness.Scenario.GetString(SortieResultPage.KEY_RETURN_NOTE));
        }

        [Test]
        public void Attach_UserValueExists_DoesNotOverwriteSeed()
        {
            _harness.Scenario.SetInt(SortieResultPage.KEY_SAVE_STATUS, (int)ESaveStatus.Retrying);
            _harness.Scenario.SetInt(SortieResultPage.KEY_BASE, 777);

            _page.Attach(_harness.Context);

            Assert.AreEqual((int)ESaveStatus.Retrying, SaveStatus());
            Assert.AreEqual(777, _harness.Scenario.GetInt(SortieResultPage.KEY_BASE));
        }

        [Test]
        public void Attach_Default_SubscribesReturnClickedOnce()
        {
            Assert.AreEqual(1, PresentationTestHarness.SubscriberCount(_page.View, "ReturnClicked"));
        }

        [Test]
        public void Apply_ItemCounts_FillsRewardGridsWithPrefabSlots()
        {
            ItemSlotView prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemSlotView>(SortieResultPage.ITEM_SLOT_PREFAB_PATH);
            Assume.That(prefab != null, "PF_ItemSlot 프리팹이 없어 건너뜀");

            FieldInfo confirmed = typeof(ResultScreenView).GetField("_confirmedGrid", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo chance    = typeof(ResultScreenView).GetField("_chanceGrid", BindingFlags.Instance | BindingFlags.NonPublic);
            ItemSlotGridView confirmedGrid = confirmed.GetValue(_page.View) as ItemSlotGridView;
            ItemSlotGridView chanceGrid    = chance.GetValue(_page.View) as ItemSlotGridView;
            Assume.That(confirmedGrid != null && chanceGrid != null, "보상 격자가 연결되지 않아 건너뜀");

            _harness.Scenario.SetInt(SortieResultPage.KEY_CONFIRMED, 3);
            _harness.Scenario.SetInt(SortieResultPage.KEY_CHANCE, 2);
            _page.Apply(_harness.Context);

            Assert.GreaterOrEqual(confirmedGrid.SlotCount, 3);
            Assert.GreaterOrEqual(chanceGrid.SlotCount, 2);
            Assert.AreEqual("확정 아이템 1", confirmedGrid.GetSlot(0).Data.Title);
            Assert.AreEqual("확률 아이템 1", chanceGrid.GetSlot(0).Data.Title);
        }

        [Test]
        public void Detach_AfterAttach_UnsubscribesAndDestroysFixture()
        {
            ResultScreenView view = _page.View;

            _page.Detach();

            Assert.AreEqual(0, PresentationTestHarness.SubscriberCount(view, "ReturnClicked"));
            Assert.IsNull(_page.View);
            Assert.IsFalse(_page.IsAttached);
            Assert.AreEqual(0, PresentationTestHarness.CountFixtureCanvases());
        }

        [Test]
        public void ItemKey_GroupIndexField_BuildsStableKey()
        {
            Assert.AreEqual("result.confirmed.2.grade", SortieResultPage.ItemKey("confirmed", 2, "grade"));
            Assert.IsTrue(_harness.Scenario.Contains(SortieResultPage.ItemKey("chance", 3, "title")));
        }

        /****************************************
        *              StartSave
        ****************************************/

        [Test]
        public void StartSave_Playing_AcceptsRequestAndShowsSaving()
        {
            bool started = _page.StartSave(_harness.Context);

            Assert.IsTrue(started);
            Assert.IsTrue(_harness.Context.IsBusy);
            Assert.AreEqual(SortieResultPage.OP_SAVE, _harness.Context.PendingOperation);
            Assert.AreEqual((int)ESaveStatus.Saving, SaveStatus());
        }

        [Test]
        public void StartSave_NotPlaying_ReturnsFalseAndDoesNotShowSaving()
        {
            _harness.Playing = false;

            bool started = _page.StartSave(_harness.Context);

            Assert.IsFalse(started);
            Assert.IsFalse(_harness.Context.IsBusy);
            Assert.AreEqual((int)ESaveStatus.Saved, SaveStatus());
            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Blocked));
            StringAssert.Contains("시작되지 않음", _page.LastResponse);
        }

        [Test]
        public void StartSave_AnotherRequestPending_ReturnsFalseAndKeepsPreviousStatus()
        {
            PresentationTestHarness.RaiseEvent(_page.View, "ReturnClicked");
            Assert.AreEqual(SortieResultPage.OP_RETURN, _harness.Context.PendingOperation);

            bool started = _page.StartSave(_harness.Context);

            Assert.IsFalse(started);
            Assert.AreEqual(SortieResultPage.OP_RETURN, _harness.Context.PendingOperation);
            Assert.AreEqual((int)ESaveStatus.Saved, SaveStatus());
        }

        [Test]
        public void StartSave_DuplicateWhilePending_ReturnsFalseAndStatusStaysSaving()
        {
            Assert.IsTrue(_page.StartSave(_harness.Context));

            bool second = _page.StartSave(_harness.Context);

            Assert.IsFalse(second);
            Assert.AreEqual((int)ESaveStatus.Saving, SaveStatus());
            Assert.AreEqual(SortieResultPage.OP_SAVE, _harness.Context.PendingOperation);
        }

        [Test]
        public void StartSave_NullContextOrDetached_ReturnsFalse()
        {
            Assert.IsFalse(_page.StartSave(null));

            _page.Detach();

            Assert.IsFalse(_page.StartSave(_harness.Context));
            Assert.IsFalse(_harness.Context.IsBusy);
        }

        [Test]
        public void StartSave_SuccessAfterTick_ShowsSaved()
        {
            _harness.Scenario.SetInt(SortieResultPage.KEY_SAVE_STATUS, (int)ESaveStatus.Retrying);
            _page.StartSave(_harness.Context);

            _harness.Tick();

            Assert.IsFalse(_harness.Context.IsBusy);
            Assert.AreEqual((int)ESaveStatus.Saved, SaveStatus());
            StringAssert.Contains("성공", _page.LastResponse);
        }

        [Test]
        public void StartSave_RejectedAfterTick_ShowsRetrying()
        {
            _harness.SetMode(EUiSandboxResponseMode.Rejected, "저장 실패 (모의)");
            _page.StartSave(_harness.Context);

            _harness.Tick();

            Assert.AreEqual((int)ESaveStatus.Retrying, SaveStatus());
            StringAssert.Contains("실패", _page.LastResponse);
        }

        /****************************************
        *             Cancellation
        ****************************************/

        [Test]
        public void Apply_AfterExternalCancel_RestoresPreviousStatus()
        {
            _page.StartSave(_harness.Context);

            _harness.Context.CancelPending("테스트 취소");
            _page.Apply(_harness.Context);
            _harness.Tick();

            Assert.AreEqual((int)ESaveStatus.Saved, SaveStatus());
            Assert.AreEqual(0, CountOutcome(_harness.Context, EUiSandboxRequestState.Succeeded));
        }

        [Test]
        public void Apply_AfterExternalCancelFromRetrying_RestoresRetrying()
        {
            _harness.Scenario.SetInt(SortieResultPage.KEY_SAVE_STATUS, (int)ESaveStatus.Retrying);
            _page.StartSave(_harness.Context);

            _harness.Context.CancelPending("테스트 취소");
            _page.Apply(_harness.Context);

            Assert.AreEqual((int)ESaveStatus.Retrying, SaveStatus());
        }

        [Test]
        public void Apply_WhileSavePending_KeepsSaving()
        {
            _page.StartSave(_harness.Context);

            _page.Apply(_harness.Context);

            Assert.AreEqual((int)ESaveStatus.Saving, SaveStatus());
            Assert.IsTrue(_harness.Context.IsBusy);
        }

        [Test]
        public void Apply_UserChangedStatusAfterCancel_DoesNotOverrideUserValue()
        {
            _page.StartSave(_harness.Context);
            _harness.Context.CancelPending("테스트 취소");
            _harness.Scenario.SetInt(SortieResultPage.KEY_SAVE_STATUS, (int)ESaveStatus.Retrying);

            _page.Apply(_harness.Context);

            Assert.AreEqual((int)ESaveStatus.Retrying, SaveStatus());
        }

        [Test]
        public void Detach_WhileSavePending_CancelsAndRestoresPreviousStatus()
        {
            _page.StartSave(_harness.Context);

            _page.Detach();
            _harness.Tick();

            Assert.IsFalse(_harness.Context.IsBusy);
            Assert.AreEqual((int)ESaveStatus.Saved, SaveStatus());
            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Canceled));
            Assert.AreEqual(0, CountOutcome(_harness.Context, EUiSandboxRequestState.Succeeded));
        }

        [Test]
        public void Tick_AfterCancelAndRestore_DoesNotApplyLateResponse()
        {
            _page.StartSave(_harness.Context);
            _harness.Context.CancelPending("테스트 취소");
            _page.Apply(_harness.Context);

            _harness.Tick();

            Assert.AreEqual((int)ESaveStatus.Saved, SaveStatus());
            StringAssert.DoesNotContain("성공", _page.LastResponse);
        }

        /****************************************
        *                Return
        ****************************************/

        [Test]
        public void ReturnClicked_Playing_RunsReturnRequestAndShowsNote()
        {
            _harness.Scenario.SetString(SortieResultPage.KEY_RETURN_NOTE, "사용자 귀환 문구");

            PresentationTestHarness.RaiseEvent(_page.View, "ReturnClicked");

            Assert.IsTrue(_harness.Context.IsBusy);
            Assert.AreEqual(SortieResultPage.OP_RETURN, _harness.Context.PendingOperation);

            _harness.Tick();

            Assert.AreEqual("사용자 귀환 문구", _harness.Context.Status);
            StringAssert.Contains("성공", _page.LastResponse);
        }

        [Test]
        public void ReturnClicked_NotPlaying_RecordsBlockedAndDoesNotShowNote()
        {
            _harness.Playing = false;

            PresentationTestHarness.RaiseEvent(_page.View, "ReturnClicked");

            Assert.IsFalse(_harness.Context.IsBusy);
            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Blocked));
            StringAssert.Contains("시작되지 않음", _page.LastResponse);
            Assert.AreNotEqual("마을로 귀환했습니다 (모의)", _harness.Context.Status);
        }

        [Test]
        public void SetSaveStatus_SavingThenSaved_TogglesReturnButtonInteractable()
        {
            FieldInfo field  = typeof(ResultScreenView).GetField("_returnButton", BindingFlags.Instance | BindingFlags.NonPublic);
            Button    button = (field == null) ? null : field.GetValue(_page.View) as Button;
            Assume.That(button != null, "시험 배치에 귀환 버튼이 연결되지 않아 건너뜀");

            _page.StartSave(_harness.Context);
            Assert.IsFalse(button.interactable);

            _harness.Tick();
            Assert.IsTrue(button.interactable);
        }
    }
}
