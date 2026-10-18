// System
using System;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 테스트
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class InventoryModernLayoutSceneTests
    {
        // 출시 장면은 읽기만 한다. 이미 열려 있으면 그대로 두고, 이 시험이 연 경우에만 닫는다. 저장하지 않는다.
        // 인벤토리 창은 빈 임시 장면에 복제해 시험하고, 바인더는 지워 네트워크 요청이 없게 한다

        private const string SCENE_PATH = "Assets/00.Scenes/Release/ShinshiTown.unity";
        private const string DETAIL     = "MD_Detail";

        private Scene      _source;
        private bool       _openedSource;
        private Scene      _preview;
        private GameObject _clone;

        [SetUp]
        public void SetUp()
        {
            _source = SceneManager.GetSceneByPath(SCENE_PATH);
            if (!_source.isLoaded)
            {
                _source       = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Additive);
                _openedSource = true;
            }

            _preview = EditorSceneManager.NewPreviewScene();
        }

        [TearDown]
        public void TearDown()
        {
            if (_clone != null)
            {
                UnityEngine.Object.DestroyImmediate(_clone);
            }

            EditorSceneManager.ClosePreviewScene(_preview);
            if (_openedSource)
            {
                EditorSceneManager.CloseScene(_source, true);
            }
        }

        [Test]
        public void MainInventory_DetailFollowsSelectionFilterAndSort()
        {
            InventoryView view = CloneInventory(false);
            Awake(view);

            Transform detail = view.transform.Find(DETAIL);
            Assert.IsNotNull(detail, "넓은 배치의 상세 패널이 붙어야 한다");
            Transform content = detail.Find("Content");
            Transform empty   = detail.Find("Empty");
            Assert.IsFalse(content.gameObject.activeSelf);
            Assert.IsTrue(empty.gameObject.activeSelf);

            view.SetItems(BuildItems(), 24);
            Assert.IsFalse(content.gameObject.activeSelf, "선택 전에는 안내 문구만 보인다");

            // 선택: 상세가 그 칸의 값을 보인다
            view.SelectItem(3);
            Assert.IsTrue(content.gameObject.activeSelf);
            Assert.AreEqual("소모 3", Label(content, "Title").text);
            StringAssert.Contains("영웅", Label(content, "Meta").text);
            StringAssert.Contains("4개", Label(content, "Meta").text);

            // 필터: 선택한 아이템이 걸러지면 상세가 비고, 보이는 아이템을 고르면 다시 채워진다
            var tabs = (Toggle[])typeof(InventoryView).GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            tabs[(int)EInventoryCategory.Material].isOn = true;
            Assert.IsFalse(content.gameObject.activeSelf, "걸러진 아이템의 상세가 남으면 안 된다");
            Assert.IsTrue(empty.gameObject.activeSelf);
            view.SelectItem(2);
            Assert.IsTrue(content.gameObject.activeSelf);
            Assert.AreEqual("재료 2", Label(content, "Title").text);

            // 정렬: 정렬을 바꾸면 선택이 풀려 상세가 비고, 정렬 뒤 첫 칸을 고르면 그 칸의 값과 같다
            tabs[(int)EInventoryCategory.All].isOn = true;
            var sort = (TMP_Dropdown)typeof(InventoryView).GetField("_sortDropdown", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            sort.value = 3;
            Assert.IsFalse(content.gameObject.activeSelf, "정렬 뒤에는 선택이 풀린다");

            ItemSlotGridView grid = view.GetComponentInChildren<ItemSlotGridView>(true);
            ItemSlotData first = grid.GetSlot(0).Data;
            Assert.IsFalse(first.IsEmpty);
            view.SelectItem(0);
            Assert.IsTrue(content.gameObject.activeSelf);
            Assert.AreEqual(first.Title, Label(content, "Title").text);
        }

        [Test]
        public void MainInventory_HidesEquippedInEveryFilterAndSort_AndShowsAgainWhenUnequipped()
        {
            InventoryView view = CloneInventory(false);
            Awake(view);

            view.SetItems(BuildEquippedItems(true), 24);
            Assert.AreEqual(0, CountVisibleTitles(view, "착용 1", out int used), "착용 중 장비가 전체 탭에 보인다");
            Assert.AreEqual(2, used, "칸 사용량은 착용 장비를 세지 않는다");

            var tabs = (Toggle[])typeof(InventoryView).GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            tabs[(int)EInventoryCategory.Equipment].isOn = true;
            Assert.AreEqual(0, CountVisibleTitles(view, "착용 1", out _), "장비 탭에도 착용 중 장비가 없어야 한다");

            var sort = (TMP_Dropdown)typeof(InventoryView).GetField("_sortDropdown", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            for (int value = 0; value < 4; ++value)
            {
                sort.value = value;
                Assert.AreEqual(0, CountVisibleTitles(view, "착용 1", out _), "정렬 " + value + "에서 착용 장비가 보인다");
            }

            tabs[(int)EInventoryCategory.All].isOn = true;
            view.SetItems(BuildEquippedItems(false), 24);
            Assert.AreEqual(1, CountVisibleTitles(view, "착용 1", out used), "착용을 풀면 다시 보인다");
            Assert.AreEqual(3, used);
        }

        [Test]
        public void MainInventory_SelectionAndDetailDoNotKeepHiddenEquipped()
        {
            InventoryView view = CloneInventory(false);
            Awake(view);

            view.SetItems(BuildEquippedItems(false), 24);
            view.SelectItem(1);
            Transform content = view.transform.Find(DETAIL).Find("Content");
            Assert.IsTrue(content.gameObject.activeSelf);
            Assert.AreEqual("착용 1", Label(content, "Title").text);

            view.SetItems(BuildEquippedItems(true), 24);
            Assert.IsFalse(content.gameObject.activeSelf, "새 목록을 받으면 이전 선택 상세가 남으면 안 된다");
        }

        [Test]
        public void TradeSideInventory_StillShowsEquipped()
        {
            if (FindInventory(true) == null)
            {
                Assert.Inconclusive("출시 장면에 거래 창 옆 인벤토리가 없다");
            }

            InventoryView view = CloneInventory(true);
            Awake(view);

            view.SetItems(BuildEquippedItems(true), 24);
            Assert.AreEqual(1, CountVisibleTitles(view, "착용 1", out int used), "거래 보조 인벤토리는 옛 동작을 유지한다");
            Assert.AreEqual(3, used);
        }

        [Test]
        public void TradeSideInventory_KeepsLegacyLayout()
        {
            InventoryView original = FindInventory(true);
            if (original == null)
            {
                Assert.Inconclusive("출시 장면에 거래 창 옆 인벤토리가 없다");
            }

            Vector2 size = ((RectTransform)original.transform).sizeDelta;

            InventoryView view = CloneInventory(true);
            Awake(view);

            Assert.IsNull(view.transform.Find(DETAIL), "거래 보조 인벤토리에는 상세 패널이 붙으면 안 된다");
            Assert.AreEqual(size, ((RectTransform)view.transform).sizeDelta, "거래 보조 인벤토리 크기가 바뀌면 안 된다");
            foreach (Transform child in view.GetComponentsInChildren<Transform>(true))
            {
                Assert.IsFalse(child.name.StartsWith(UiModernDark.DECOR_PREFIX, StringComparison.Ordinal), "새 장식이 붙었다: " + child.name);
            }
        }

        private InventoryView CloneInventory(bool tradeSide)
        {
            InventoryView original = FindInventory(tradeSide);
            Assert.IsNotNull(original, "출시 장면에서 인벤토리 창을 찾지 못했다");

            var path = new List<int>();
            Transform canvasTransform = original.transform.GetComponentInParent<Canvas>(true).rootCanvas.transform;
            for (Transform walk = original.transform; walk != canvasTransform; walk = walk.parent)
            {
                path.Add(walk.GetSiblingIndex());
            }

            _clone = UnityEngine.Object.Instantiate(canvasTransform.gameObject);
            SceneManager.MoveGameObjectToScene(_clone, _preview);
            _clone.SetActive(true);

            foreach (MonoBehaviour behaviour in _clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if ((behaviour != null) && behaviour.GetType().Name.EndsWith("Binder", StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(behaviour);
                }
            }

            Transform walkCopy = _clone.transform;
            for (int i = path.Count - 1; i >= 0; --i)
            {
                walkCopy = walkCopy.GetChild(path[i]);
                walkCopy.gameObject.SetActive(true);
            }

            return walkCopy.GetComponent<InventoryView>();
        }

        private InventoryView FindInventory(bool tradeSide)
        {
            foreach (GameObject root in _source.GetRootGameObjects())
            {
                foreach (InventoryView view in root.GetComponentsInChildren<InventoryView>(true))
                {
                    bool hasTrade = view.transform.root.GetComponentInChildren<TradeView>(true) != null;
                    if (hasTrade == tradeSide)
                    {
                        return view;
                    }
                }
            }

            return null;
        }

        // 편집 모드에서는 Awake가 돌지 않으므로 같은 초기화를 한 번 부른다
        private static void Awake(InventoryView view)
        {
            typeof(InventoryView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
        }

        private static TMP_Text Label(Transform content, string name)
        {
            return content.Find(name).GetComponent<TMP_Text>();
        }

        // 장비 셋: 일반 0, 착용 1(equipped 값에 따라 착용), 일반 2
        private static List<InventoryItemData> BuildEquippedItems(bool equipped)
        {
            string[] names = { "일반 0", "착용 1", "일반 2" };
            var items = new List<InventoryItemData>();
            for (int i = 0; i < names.Length; ++i)
            {
                var slot = new ItemSlotData("equip_test_" + i, EItemGrade.Rare, 1, names[i], "시험용 설명", false, equipped && (i == 1));
                items.Add(new InventoryItemData(slot, EInventoryCategory.Equipment, i));
            }

            return items;
        }

        // 보이는 칸 중 제목이 같은 칸 수를 세고, 칸 사용량 표시에 쓰이는 비어 있지 않은 항목 수를 함께 돌려준다
        private static int CountVisibleTitles(InventoryView view, string title, out int used)
        {
            ItemSlotGridView grid = view.GetComponentInChildren<ItemSlotGridView>(true);
            int matches = 0;
            used = 0;
            for (int i = 0; i < grid.SlotCount; ++i)
            {
                ItemSlotData data = grid.GetSlot(i).Data;
                if (data.IsEmpty)
                {
                    continue;
                }

                ++used;
                if (data.Title == title)
                {
                    ++matches;
                }
            }

            return matches;
        }

        // 칸 0~5: 장비 둘, 재료, 소모품, 부적, 기타 순서. 이름의 숫자는 칸 번호이고 개수는 번호+1이다
        private static List<InventoryItemData> BuildItems()
        {
            string[]             names      = { "장비 0", "장비 1", "재료 2", "소모 3", "부적 4", "기타 5" };
            EInventoryCategory[] categories = { EInventoryCategory.Equipment, EInventoryCategory.Equipment, EInventoryCategory.Material,
                EInventoryCategory.Consumable, EInventoryCategory.Talisman, EInventoryCategory.Etc };
            EItemGrade[]         grades     = { EItemGrade.Legendary, EItemGrade.Rare, EItemGrade.Common, EItemGrade.Epic, EItemGrade.Rare, EItemGrade.Common };

            var items = new List<InventoryItemData>();
            for (int i = 0; i < names.Length; ++i)
            {
                var slot = new ItemSlotData("test_item_" + i, grades[i], i + 1, names[i], "시험용 설명 " + i);
                items.Add(new InventoryItemData(slot, categories[i], i));
            }

            return items;
        }
    }
}
