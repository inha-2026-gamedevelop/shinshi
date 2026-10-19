// System
using System.Reflection;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using NUnit.Framework;
using TMPro;

// 프로젝트 내부
using Shinshi.UI;

namespace Shinshi.Tests
{
    /// <summary>
    /// 실제 시스템 메뉴처럼 버튼이 패널 바로 아래가 아니라 Body 안에 있을 때 AddEntry가 항목을 만들고 화면 안에 두는지 확인한다.
    /// 에디터 모드라 Awake가 불리지 않으므로 패널은 꺼 둔 채 버튼은 onClick.Invoke로만 누른다
    /// </summary>
    public sealed class HudMenuNestedEntryTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PREFAB_PATH     = "Assets/02.Prefab/UI/Windows/PF_SystemMenu.prefab";
        private const float  SCREEN_WIDTH    = 1920f;
        private const float  SCREEN_HEIGHT   = 1080f;
        private const float  SHORT_HEIGHT    = 500f;
        private const float  BUTTON_WIDTH    = 244f;
        private const float  BUTTON_HEIGHT   = 44f;
        private const float  ROW_STEP        = 52f;
        private const float  FIRST_ROW_Y     = -244f;
        private const float  COLUMN_LEFT     = 24f;
        private const float  COLUMN_RIGHT    = 276f;
        private const int    ROW_COUNT       = 8;
        private const int    COLUMN_COUNT    = 2;
        private const int    CORNER_COUNT    = 4;
        private const int    EXISTING_COUNT  = 15;
        private const int    FIXTURE_BUTTONS = EXISTING_COUNT + 1;

        private static readonly string[] LABELS = { "신체강화", "수련", "부적", "파티모집" };

        private readonly Vector3[] _corners = new Vector3[CORNER_COUNT];

        private GameObject      _screen;
        private HudMenuDropdown _menu;
        private RectTransform   _panel;
        private RectTransform   _body;

        /****************************************
        *               Lifecycle
        ****************************************/

        [TearDown]
        public void TearDown()
        {
            if (_screen != null)
            {
                Object.DestroyImmediate(_screen);
            }

            _screen = null;
            _menu   = null;
            _panel  = null;
            _body   = null;
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void AddEntry_ButtonsUnderBody_CreatesEntryInsideBody()
        {
            BuildFixture(SCREEN_HEIGHT);

            Button entry = _menu.AddEntry(LABELS[0], null);

            Assert.IsNotNull(entry);
            Assert.AreSame(_body, entry.transform.parent);
        }

        [Test]
        public void AddEntry_SameLabelTwice_ReturnsSameButtonWithoutDuplicate()
        {
            BuildFixture(SCREEN_HEIGHT);

            Button first  = _menu.AddEntry(LABELS[1], null);
            Button second = _menu.AddEntry(LABELS[1], null);

            Assert.AreSame(first, second);
            Assert.AreEqual(FIXTURE_BUTTONS + 1, _panel.GetComponentsInChildren<Button>(true).Length);
        }

        [Test]
        public void AddEntry_Clicked_InvokesOriginalActionOnce()
        {
            BuildFixture(SCREEN_HEIGHT);
            int calls = 0;

            Button entry = _menu.AddEntry(LABELS[2], () => ++calls);
            entry.onClick.Invoke();

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void AddEntry_TemplateSearch_SkipsCloseEntry()
        {
            BuildFixture(SCREEN_HEIGHT);

            Button entry = _menu.AddEntry(LABELS[3], null);

            Assert.IsFalse(entry.name.ToLowerInvariant().Contains("close"));
        }

        [Test]
        public void AddEntry_FourEntries_FitVisibleBodyWithoutCoveringControls()
        {
            BuildFixture(SCREEN_HEIGHT);

            Button[] added = AddAll();

            Assert.AreEqual(FIXTURE_BUTTONS + LABELS.Length, _panel.GetComponentsInChildren<Button>(true).Length);
            Assert.IsNull(_body.GetComponent<ScrollRect>());
            Rect viewport = WorldRect(_body);
            for (int i = 0; i < added.Length; ++i)
            {
                Assert.IsTrue(Contains(viewport, WorldRect((RectTransform)added[i].transform)), added[i].name + " is outside the visible body");
                AssertNoOverlap(added[i]);
            }
        }

        [Test]
        public void AddEntry_ShortScreen_ScrollsContentInsteadOfClipping()
        {
            BuildFixture(SHORT_HEIGHT);

            Button[] added = AddAll();

            var scroll = _body.GetComponent<ScrollRect>();
            Assert.IsNotNull(scroll);
            Assert.IsNotNull(scroll.content);
            Assert.IsNotNull(_body.GetComponent<RectMask2D>());
            Rect content = WorldRect(scroll.content);
            for (int i = 0; i < added.Length; ++i)
            {
                Assert.AreSame(scroll.content, added[i].transform.parent);
                Assert.IsTrue(Contains(content, WorldRect((RectTransform)added[i].transform)), added[i].name + " is outside scroll content");
                AssertNoOverlap(added[i]);
            }

            Assert.AreEqual(FIXTURE_BUTTONS + LABELS.Length, scroll.content.GetComponentsInChildren<Button>(true).Length);
        }

        [Test]
        public void AddEntry_AfterScrollCreated_ReturnsSameEntriesWithoutDuplicates()
        {
            BuildFixture(SHORT_HEIGHT);
            Button[] first = AddAll();

            Button[] second = AddAll();

            for (int i = 0; i < first.Length; ++i)
            {
                Assert.AreSame(first[i], second[i]);
            }

            Assert.AreEqual(FIXTURE_BUTTONS + LABELS.Length, _panel.GetComponentsInChildren<Button>(true).Length);
        }

        [Test]
        public void AddEntry_CatalogFontAvailable_AppliesItToPanelTexts()
        {
            ModernDarkUiCatalog catalog = ModernDarkUiCatalog.Load();
            if ((catalog == null) || (catalog.Font == null))
            {
                Assert.Ignore("ModernDark catalog font not available");
            }

            BuildFixture(SCREEN_HEIGHT);
            TextMeshProUGUI original = null;
            foreach (Button item in _body.GetComponentsInChildren<Button>(true))
            {
                var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                label.transform.SetParent(item.transform, false);
                if (item.name == "Item0")
                {
                    original = label.GetComponent<TextMeshProUGUI>();
                }
            }

            Button entry = _menu.AddEntry(LABELS[0], null);

            Assert.AreSame(catalog.Font, original.font);
            Assert.AreSame(catalog.Font, entry.GetComponentInChildren<TMP_Text>(true).font);
        }

        [Test]
        public void AddEntry_SystemMenuPrefab_AddsAllFourEntriesWithoutOverlap()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            if (prefab == null)
            {
                Assert.Ignore("PF_SystemMenu prefab not found");
            }

            BuildScreen(SCREEN_HEIGHT);
            GameObject instance = Object.Instantiate(prefab, _screen.transform, false);
            _menu = instance.GetComponentInChildren<HudMenuDropdown>(true);
            if (_menu == null)
            {
                Assert.Ignore("PF_SystemMenu has no HudMenuDropdown");
            }

            _panel = (RectTransform)typeof(HudMenuDropdown).GetField("_panel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_menu);
            int before = _panel.GetComponentsInChildren<Button>(true).Length;

            Button[] added = AddAll();

            Assert.AreEqual(before + LABELS.Length, _panel.GetComponentsInChildren<Button>(true).Length);
            for (int i = 0; i < added.Length; ++i)
            {
                AssertNoOverlap(added[i]);
            }
        }

        /****************************************
        *                Helpers
        ****************************************/

        private Button[] AddAll()
        {
            var added = new Button[LABELS.Length];
            for (int i = 0; i < LABELS.Length; ++i)
            {
                added[i] = _menu.AddEntry(LABELS[i], null);
                Assert.IsNotNull(added[i], LABELS[i]);
            }

            return added;
        }

        private void BuildScreen(float height)
        {
            _screen = new GameObject("Screen", typeof(RectTransform));
            var rect = (RectTransform)_screen.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(SCREEN_WIDTH, height);
        }

        private void BuildFixture(float height)
        {
            BuildScreen(height);
            _panel = NewRect("Panel", _screen.transform, new Vector2(640f, 0f));
            _body  = NewRect("Body", _panel, new Vector2(544f, 0f));
            _body.gameObject.AddComponent<Image>();

            for (int i = 0; i < EXISTING_COUNT; ++i)
            {
                float x = ((i % COLUMN_COUNT) == 0) ? COLUMN_LEFT : COLUMN_RIGHT;
                NewButton("Item" + i, x, FIRST_ROW_Y - ((i / COLUMN_COUNT) * ROW_STEP));
            }

            NewButton("CloseButton", COLUMN_RIGHT, FIRST_ROW_Y - ((ROW_COUNT - 1) * ROW_STEP));

            var go = new GameObject("Menu");
            go.transform.SetParent(_screen.transform, false);
            _menu = go.AddComponent<HudMenuDropdown>();
            typeof(HudMenuDropdown).GetField("_panel", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_menu, _panel);
            _panel.gameObject.SetActive(false);
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot     = new Vector2(1f, 0.5f);
            rect.sizeDelta = sizeDelta;
            return rect;
        }

        private void NewButton(string name, float x, float y)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_body, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin        = new Vector2(0f, 1f);
            rect.anchorMax        = new Vector2(0f, 1f);
            rect.pivot            = new Vector2(0f, 1f);
            rect.sizeDelta        = new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT);
            rect.anchoredPosition = new Vector2(x, y);
        }

        private Rect WorldRect(RectTransform target)
        {
            target.GetWorldCorners(_corners);
            return Rect.MinMaxRect(_corners[0].x, _corners[0].y, _corners[2].x, _corners[2].y);
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return (inner.xMin >= outer.xMin) && (inner.xMax <= outer.xMax) && (inner.yMin >= outer.yMin) && (inner.yMax <= outer.yMax);
        }

        private void AssertNoOverlap(Button entry)
        {
            Rect rect = WorldRect((RectTransform)entry.transform);
            foreach (Button other in _panel.GetComponentsInChildren<Button>(true))
            {
                if ((other == entry) || (!other.gameObject.activeSelf))
                {
                    continue;
                }

                Assert.IsFalse(rect.Overlaps(WorldRect((RectTransform)other.transform)), entry.name + " overlaps " + other.name);
            }
        }
    }
}
