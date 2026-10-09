// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Editor;
using Shinshi.UI;

namespace Shinshi.Tests
{
    /// <summary> 인벤토리 화면의 아이템 변환, 정리 단추, Alt+클릭 잠금 확정과 되돌림, 구독 정리를 실제 InventoryView로 확인한다 </summary>
    public sealed class InventorySandboxPageTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int SLOT_SWORD  = 0;
        private const int SLOT_POTION = 2;

        private EconomyTestHarness   _harness;
        private InventorySandboxPage _page;
        private InventoryView        _view;
        private ItemSlotGridView     _grid;
        private Button               _arrangeButton;
        private Button               _closeButton;

        /****************************************
        *                Methods
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _harness = new EconomyTestHarness();
            BuildView();
            _page = new InventorySandboxPage();
            _harness.AttachAndApply(_page);
        }

        [TearDown]
        public void TearDown()
        {
            InventorySandboxPage.AltHeldProvider = null;
            _harness?.Dispose();
            _harness = null;
            _page    = null;
        }

        [Test]
        public void BuildItems_DefaultScenario_MapsGradeCategoryAndFlags()
        {
            List<InventoryItemData> items = InventorySandboxPage.BuildItems(Scenario());

            Assert.AreEqual(4, items.Count);
            Assert.AreEqual("item.sword", items[0].Slot.Id);
            Assert.AreEqual(EItemGrade.Common, items[0].Slot.Grade);
            Assert.AreEqual(EInventoryCategory.Equipment, items[0].Category);
            Assert.IsTrue(items[0].Slot.IsEquipped);
            Assert.AreEqual(EItemGrade.Epic, items[2].Slot.Grade);
            Assert.AreEqual(EInventoryCategory.Consumable, items[2].Category);
            Assert.IsTrue(items[2].Slot.IsLocked);
            Assert.AreEqual(12, items[1].Slot.Count);
        }

        [Test]
        public void BuildItems_UnknownGradeAndCategory_FallBackToCommonAndEtc()
        {
            EconomyList.SetText(Scenario(), InventorySandboxPage.ItemSchema, 0, "grade", "없는 등급");
            EconomyList.SetText(Scenario(), InventorySandboxPage.ItemSchema, 0, "category", "없는 분류");

            List<InventoryItemData> items = InventorySandboxPage.BuildItems(Scenario());

            Assert.AreEqual(EItemGrade.Common, items[0].Slot.Grade);
            Assert.AreEqual(EInventoryCategory.Etc, items[0].Category);
        }

        [Test]
        public void Apply_DefaultScenario_FillsSlotsFromScenario()
        {
            Assert.AreEqual(24, _grid.SlotCount);
            Assert.AreEqual("item.sword", _grid.GetSlot(SLOT_SWORD).Data.Id);
            Assert.IsTrue(_grid.GetSlot(SLOT_POTION).Data.IsLocked);
            Assert.IsTrue(_grid.GetSlot(10).Data.IsEmpty);
        }

        [Test]
        public void Apply_AfterEditingItemRow_ShowsEditedValue()
        {
            EconomyList.SetText(Scenario(), InventorySandboxPage.ItemSchema, 0, "title", "고친 이름");
            EconomyList.SetInt(Scenario(), InventorySandboxPage.ItemSchema, 0, "count", 77);

            _page.Apply(_harness.Context);

            Assert.AreEqual("고친 이름", _grid.GetSlot(SLOT_SWORD).Data.Title);
            Assert.AreEqual(77, _grid.GetSlot(SLOT_SWORD).Data.Count);
        }

        [Test]
        public void Apply_AddedItemRow_ShowsNewItem()
        {
            Assert.IsTrue(EconomyList.Add(Scenario(), InventorySandboxPage.ItemSchema));

            _page.Apply(_harness.Context);

            Assert.IsFalse(_grid.GetSlot(4).Data.IsEmpty);
        }

        [Test]
        public void Apply_InteractionOff_ClearsSerializedFlag()
        {
            Scenario().SetBool(InventorySandboxPage.KEY_INTERACTION, false);

            _page.Apply(_harness.Context);

            Assert.IsFalse(new SerializedObject(_view).FindProperty("_enableSlotInteraction").boolValue);
        }

        [Test]
        public void ArrangeClick_Success_SendsOneRequestAndKeepsArrangedView()
        {
            EconomyTestHarness.Click(_arrangeButton);

            Assert.AreEqual(1, _harness.Context.History.Count);
            Assert.AreEqual(InventorySandboxPage.OP_ARRANGE, _harness.LastRecord.Operation);

            _harness.Tick();

            StringAssert.Contains("인벤토리 정리 성공", _harness.Context.Status);
            Assert.AreEqual("item.charm", _grid.GetSlot(SLOT_SWORD).Data.Id);
        }

        [Test]
        public void ArrangeClick_Rejected_RestoresScenarioOrder()
        {
            _harness.SetMode(EUiSandboxResponseMode.Rejected);

            EconomyTestHarness.Click(_arrangeButton);
            Assert.AreEqual("item.charm", _grid.GetSlot(SLOT_SWORD).Data.Id);
            _harness.Tick();

            StringAssert.Contains("인벤토리 정리 실패", _harness.Context.Status);
            Assert.AreEqual("item.sword", _grid.GetSlot(SLOT_SWORD).Data.Id);
        }

        [Test]
        public void ArrangeClick_NotPlaying_IsBlockedAndRestoresScenarioOrder()
        {
            _harness.Playing = false;

            EconomyTestHarness.Click(_arrangeButton);

            Assert.AreEqual(EUiSandboxRequestState.Blocked, _harness.LastRecord.State);
            Assert.AreEqual("item.sword", _grid.GetSlot(SLOT_SWORD).Data.Id);
        }

        [Test]
        public void SlotClick_PlainClick_SendsNoRequest()
        {
            InventorySandboxPage.AltHeldProvider = () => false;

            Click(_grid.GetSlot(SLOT_SWORD));

            Assert.AreEqual(0, _harness.Context.History.Count);
        }

        [Test]
        public void SlotClick_AltClickRealToggleFirst_ConfirmsAbsoluteLockWithoutDoubleFlip()
        {
            InventorySandboxPage.AltHeldProvider = () => true;
            ItemSlotView slot = _grid.GetSlot(SLOT_SWORD);

            Assert.IsTrue(_view.TryToggleSlotLock(slot));
            Click(slot);
            Assert.AreEqual(1, _harness.Context.History.Count);
            Assert.AreEqual(InventorySandboxPage.OP_LOCK, _harness.LastRecord.Operation);
            _harness.Tick();

            Assert.IsTrue(EconomyList.GetBool(Scenario(), InventorySandboxPage.ItemSchema, 0, "locked"));
            Assert.IsTrue(_grid.GetSlot(SLOT_SWORD).Data.IsLocked);
            StringAssert.Contains("아이템 잠금 전환 성공", _harness.Context.Status);
        }

        [Test]
        public void SlotClick_AltClickRealToggleAfter_ConfirmsAbsoluteLockWithoutDoubleFlip()
        {
            InventorySandboxPage.AltHeldProvider = () => true;
            ItemSlotView slot = _grid.GetSlot(SLOT_SWORD);

            Click(slot);
            Assert.IsTrue(_view.TryToggleSlotLock(slot));
            _harness.Tick();

            Assert.IsTrue(EconomyList.GetBool(Scenario(), InventorySandboxPage.ItemSchema, 0, "locked"));
            Assert.IsTrue(_grid.GetSlot(SLOT_SWORD).Data.IsLocked);
        }

        [Test]
        public void SlotClick_AltClickOnLockedItem_ConfirmsUnlock()
        {
            InventorySandboxPage.AltHeldProvider = () => true;
            ItemSlotView slot = _grid.GetSlot(SLOT_POTION);

            Assert.IsTrue(_view.TryToggleSlotLock(slot));
            Click(slot);
            _harness.Tick();

            Assert.IsFalse(EconomyList.GetBool(Scenario(), InventorySandboxPage.ItemSchema, 2, "locked"));
            Assert.IsFalse(_grid.GetSlot(SLOT_POTION).Data.IsLocked);
        }

        [Test]
        public void SlotClick_AltClickRejected_RevertsViewToScenario()
        {
            InventorySandboxPage.AltHeldProvider = () => true;
            _harness.SetMode(EUiSandboxResponseMode.Rejected);
            ItemSlotView slot = _grid.GetSlot(SLOT_SWORD);

            Assert.IsTrue(_view.TryToggleSlotLock(slot));
            Assert.IsTrue(_grid.GetSlot(SLOT_SWORD).Data.IsLocked);
            Click(slot);
            _harness.Tick();

            Assert.IsFalse(EconomyList.GetBool(Scenario(), InventorySandboxPage.ItemSchema, 0, "locked"));
            Assert.IsFalse(_grid.GetSlot(SLOT_SWORD).Data.IsLocked);
            StringAssert.Contains("아이템 잠금 전환 실패", _harness.Context.Status);
        }

        [Test]
        public void SlotClick_AltClickNotPlaying_IsBlockedAndRevertsView()
        {
            InventorySandboxPage.AltHeldProvider = () => true;
            _harness.Playing = false;
            ItemSlotView slot = _grid.GetSlot(SLOT_SWORD);

            Assert.IsTrue(_view.TryToggleSlotLock(slot));
            Click(slot);

            Assert.AreEqual(EUiSandboxRequestState.Blocked, _harness.LastRecord.State);
            Assert.IsFalse(_grid.GetSlot(SLOT_SWORD).Data.IsLocked);
        }

        [Test]
        public void SlotClick_ItemRemovedDuringDelay_ReportsStaleItem()
        {
            InventorySandboxPage.AltHeldProvider = () => true;
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);

            Click(_grid.GetSlot(SLOT_SWORD));
            Assert.IsTrue(EconomyList.RemoveAt(Scenario(), InventorySandboxPage.ItemSchema, 0));
            _harness.Advance(3.0);

            StringAssert.Contains("아이템이 이미 바뀌었다", _harness.Context.Status);
            Assert.AreEqual(3, EconomyList.Count(Scenario(), InventorySandboxPage.ItemSchema));
        }

        [Test]
        public void SlotClick_InteractionDisabled_AltClickIsIgnored()
        {
            InventorySandboxPage.AltHeldProvider = () => true;
            Scenario().SetBool(InventorySandboxPage.KEY_INTERACTION, false);

            Click(_grid.GetSlot(SLOT_SWORD));

            Assert.AreEqual(0, _harness.Context.History.Count);
        }

        [Test]
        public void SlotClick_AltClickOnEmptySlot_IsIgnored()
        {
            InventorySandboxPage.AltHeldProvider = () => true;

            Click(_grid.GetSlot(10));

            Assert.AreEqual(0, _harness.Context.History.Count);
        }

        [Test]
        public void Detach_AfterAttach_RemovesArrangeListenerAndSlotSubscriber()
        {
            int before = EconomyTestHarness.SubscriberCount(_grid, "SlotClicked");

            _page.Detach();

            Assert.AreEqual(before - 1, EconomyTestHarness.SubscriberCount(_grid, "SlotClicked"));
            EconomyTestHarness.Click(_arrangeButton);
            Assert.AreEqual(0, _harness.Context.History.Count);
        }

        [Test]
        public void Reattach_AfterDetach_DoesNotDuplicateHandlers()
        {
            int before = EconomyTestHarness.SubscriberCount(_grid, "SlotClicked");
            _page.Detach();
            _harness.AttachAndApply(_page);

            EconomyTestHarness.Click(_arrangeButton);

            Assert.AreEqual(before, EconomyTestHarness.SubscriberCount(_grid, "SlotClicked"));
            Assert.AreEqual(1, _harness.Context.History.Count);
        }

        [Test]
        public void Detach_WithPendingAltRequest_CancelsAndFiresNoCallback()
        {
            InventorySandboxPage.AltHeldProvider = () => true;
            _harness.SetMode(EUiSandboxResponseMode.Success, 2.0f);
            Click(_grid.GetSlot(SLOT_SWORD));

            _page.Detach();
            _harness.Advance(5.0);

            Assert.AreEqual(EUiSandboxRequestState.Canceled, _harness.LastRecord.State);
            Assert.IsFalse(EconomyList.GetBool(Scenario(), InventorySandboxPage.ItemSchema, 0, "locked"));
        }

        [Test]
        public void ReopenView_AfterCloseButtonClick_OpensWindowAndReappliesScenario()
        {
            EconomyTestHarness.Click(_closeButton);
            Assert.IsFalse(_view.IsOpen);
            EconomyList.SetText(Scenario(), InventorySandboxPage.ItemSchema, 0, "title", "닫힌 사이 고친 이름");

            bool reopened = _page.ReopenView();

            Assert.IsTrue(reopened);
            Assert.IsTrue(_view.IsOpen);
            Assert.AreEqual("닫힌 사이 고친 이름", _grid.GetSlot(SLOT_SWORD).Data.Title);
        }

        [Test]
        public void ReopenView_AfterDetach_ReturnsFalse()
        {
            _page.Detach();

            Assert.IsFalse(_page.ReopenView());
        }

        private UiSandboxScenarioSO Scenario()
        {
            return _harness.Context.Scenario;
        }

        private static void Click(ItemSlotView slot)
        {
            slot.OnPointerClick(new PointerEventData(null));
        }

        // 실제 InventoryView가 쓰는 참조(격자, 칸 견본, 정리 단추)를 Awake 전에 모두 연결한다
        private void BuildView()
        {
            GameObject root = _harness.Spawn("InventoryTestRoot");

            GameObject slotTemplate = new GameObject("SlotTemplate", typeof(RectTransform), typeof(ItemSlotView));
            slotTemplate.transform.SetParent(root.transform, false);
            slotTemplate.SetActive(false);

            GameObject gridObject = new GameObject("Grid", typeof(RectTransform), typeof(ItemSlotGridView));
            gridObject.transform.SetParent(root.transform, false);
            _grid = gridObject.GetComponent<ItemSlotGridView>();
            EconomyUiBuilder.SetRef(_grid, "_slotPrefab", slotTemplate.GetComponent<ItemSlotView>());

            GameObject buttonObject = new GameObject("ArrangeButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(root.transform, false);
            _arrangeButton = buttonObject.GetComponent<Button>();

            GameObject closeObject = new GameObject("CloseButton", typeof(RectTransform), typeof(Image), typeof(Button));
            closeObject.transform.SetParent(root.transform, false);
            _closeButton = closeObject.GetComponent<Button>();

            _view = root.AddComponent<InventoryView>();
            EconomyUiBuilder.SetRef(_view, "_grid", _grid);
            EconomyUiBuilder.SetRef(_view, "_arrangeButton", _arrangeButton);
            EconomyUiBuilder.SetRef(_view, "_closeButton", _closeButton);
            _harness.RunAwakes(root);
            _harness.AddRoot(root);
        }
    }
}
