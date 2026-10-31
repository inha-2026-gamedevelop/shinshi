using System.Collections.Generic;
using System.Reflection;
using Shinshi.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Shinshi.Editor
{
    /// <summary>
    /// HUD 오른쪽 메뉴(HudMenuDropdown). 메뉴 버튼 열기 · 닫기, 항목 클릭 뒤 접기, 펼치는 동안 HUD 숨김과 복원을 실제 버튼 이벤트로 확인한다.
    /// 항목의 실제 동작은 View가 정하지 않으므로 항목 클릭은 샌드박스 모의 요청으로만 받는다
    /// </summary>
    public sealed class HudMenuPage : PresentationPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "hud.menu";
        public const string SCENE_PATH = "Assets/00.Scenes/Release/ShinshiTown.unity";

        public const string KEY_ITEM_PREFIX   = "hud.menu.item.";
        public const string KEY_LAST_ITEM     = "hud.menu.lastItem";
        public const string KEY_OPENED_WINDOW = "hud.menu.openedWindow";
        public const string KEY_NOTICE        = "hud.menu.notice";

        public const string OP_OPEN_WINDOW = "menu.openWindow";

        public const int ITEM_COUNT = 4;
        public const int HIDE_COUNT = 2;

        private const string LABEL_MENU         = "메뉴";
        private const string FIELD_MENU_BUTTON  = "_menuButton";
        private const string FIELD_PANEL        = "_panel";
        private const string FIELD_HIDE_TARGETS = "_hideWhileOpen";
        private const int    NO_ITEM            = -1;

        private HudMenuDropdown _menu;
        private Button          _menuButton;
        private RectTransform   _panel;
        private GameObject[]    _hideTargets = new GameObject[0];

        private readonly List<Button>      _items    = new List<Button>();
        private readonly List<UnityAction> _handlers = new List<UnityAction>();

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "HUD 메뉴 드롭다운"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get
            {
                return "HudMenuDropdown(Assets/01.Scripts/06.UI/Hud/HudMenuDropdown.cs)은 실제 View다. Dev_HUD 씬에는 없어 시험 배치를 만든다. " +
                       "메뉴 버튼 클릭으로 펼침 · 접기, 항목 클릭 뒤 접기, 펼치는 동안 숨김 대상 HUD를 끄고 접을 때 켜져 있던 것만 되살리는 동작을 확인한다. " +
                       "Esc와 바깥 클릭은 View가 Update에서 실제 입력을 읽으므로 Play에서 직접 눌러 확인하며 샌드박스가 흉내 내지 않는다. " +
                       "항목이 여는 창은 View가 정하지 않아 항목 클릭은 모의 요청 결과만 보여 준다. 숨김 대상은 운영 HUD가 아니라 시험용 표시물이다. " +
                       PresentationNotes.NETWORK_NOTE + " " + PresentationNotes.FIXTURE_NOTE;
            }
        }

        public HudMenuDropdown MenuView
        {
            get { return _menu; }
        }

        public int ItemCount
        {
            get { return _items.Count; }
        }

        public int HideTargetCount
        {
            get { return _hideTargets.Length; }
        }

        public bool IsPanelOpen
        {
            get { return (_panel != null) && (_panel.gameObject.activeSelf); }
        }

        /****************************************
        *                Methods
        ****************************************/

        public static string ItemKey(int index)
        {
            return KEY_ITEM_PREFIX + index;
        }

        protected override void Seed(UiSandboxScenarioSO scenario)
        {
            for (int i = 0; i < ITEM_COUNT; ++i)
            {
                scenario.SeedString(ItemKey(i), "메뉴 항목 " + (i + 1) + " (모의)");
            }

            scenario.SeedInt(KEY_LAST_ITEM, NO_ITEM);
            scenario.SeedString(KEY_OPENED_WINDOW, string.Empty);
            scenario.SeedString(KEY_NOTICE, string.Empty);
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions(EFixtureAnchor.TopRight)
                .WithOffset(-40.0f, -120.0f)
                .WithLength(FIELD_HIDE_TARGETS, HIDE_COUNT)
                .WithChildButtons(FIELD_PANEL, ITEM_COUNT);
            _menu = BindView<HudMenuDropdown>(context, LABEL_MENU, options);
            if (_menu == null)
            {
                return;
            }

            _menuButton  = ReadField<Button>(FIELD_MENU_BUTTON);
            _panel       = ReadField<RectTransform>(FIELD_PANEL);
            _hideTargets = ReadField<GameObject[]>(FIELD_HIDE_TARGETS) ?? new GameObject[0];

            if (_panel != null)
            {
                foreach (Button item in _panel.GetComponentsInChildren<Button>(true))
                {
                    int index = _items.Count;
                    UnityAction handler = () => OnItemClicked(index);
                    item.onClick.AddListener(handler);
                    _items.Add(item);
                    _handlers.Add(handler);
                }
            }

            ApplyLabels(context.Scenario);
        }

        protected override void OnApply(UiSandboxContext context)
        {
            ApplyLabels(context.Scenario);
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;

            DrawHeader("항목 이름 (모의)");
            for (int i = 0; i < ITEM_COUNT; ++i)
            {
                DrawString(scenario, ItemKey(i), "항목 " + (i + 1));
            }

            DrawHeader("숨김 대상 HUD (시험용 표시물)");
            for (int i = 0; i < _hideTargets.Length; ++i)
            {
                if (_hideTargets[i] == null)
                {
                    continue;
                }

                bool active = _hideTargets[i].activeSelf;
                bool next   = EditorGUILayout.Toggle("숨김 대상 " + (i + 1) + " 켜짐", active);
                if (next != active)
                {
                    _hideTargets[i].SetActive(next);
                }
            }

            DrawHeader("실제 버튼 이벤트 (Play 전용)");
            EditorGUILayout.LabelField("패널", IsPanelOpen ? "열림" : "닫힘");
            EditorGUILayout.LabelField("마지막 항목", scenario.GetInt(KEY_LAST_ITEM).ToString());
            EditorGUILayout.LabelField("열린 창 (모의)", scenario.GetString(KEY_OPENED_WINDOW));
            EditorGUILayout.LabelField("안내", scenario.GetString(KEY_NOTICE));

            EditorGUI.BeginDisabledGroup(!context.IsPlaying);
            if (GUILayout.Button("메뉴 버튼 클릭"))
            {
                ClickMenuButton();
            }

            for (int i = 0; i < _items.Count; ++i)
            {
                if (GUILayout.Button("항목 " + (i + 1) + " 클릭"))
                {
                    ClickItem(i);
                }
            }

            EditorGUI.EndDisabledGroup();
        }

        protected override void OnDetach()
        {
            for (int i = 0; i < _items.Count; ++i)
            {
                if (_items[i] != null)
                {
                    _items[i].onClick.RemoveListener(_handlers[i]);
                }
            }

            _items.Clear();
            _handlers.Clear();
            _hideTargets = new GameObject[0];
            _menuButton  = null;
            _panel       = null;
            _menu        = null;
        }

        /// <summary> 메뉴 버튼의 onClick을 실제로 호출한다. View의 Awake · OnEnable이 지난 뒤(Play)에만 효과가 있다 </summary>
        public bool ClickMenuButton()
        {
            if (_menuButton == null)
            {
                return false;
            }

            _menuButton.onClick.Invoke();
            return true;
        }

        public bool ClickItem(int index)
        {
            if ((index < 0) || (index >= _items.Count) || (_items[index] == null))
            {
                return false;
            }

            _items[index].onClick.Invoke();
            return true;
        }

        public bool IsHideTargetActive(int index)
        {
            return (index >= 0) && (index < _hideTargets.Length) && (_hideTargets[index] != null) && (_hideTargets[index].activeSelf);
        }

        public bool SetHideTargetActive(int index, bool active)
        {
            if ((index < 0) || (index >= _hideTargets.Length) || (_hideTargets[index] == null))
            {
                return false;
            }

            _hideTargets[index].SetActive(active);
            return true;
        }

        private void OnItemClicked(int index)
        {
            if (Context == null)
            {
                return;
            }

            UiSandboxScenarioSO scenario = Context.Scenario;
            scenario.SetInt(KEY_LAST_ITEM, index);
            string label = scenario.GetString(ItemKey(index));

            RunRequest(Context, OP_OPEN_WINDOW,
                () =>
                {
                    scenario.SetString(KEY_OPENED_WINDOW, label);
                    scenario.SetString(KEY_NOTICE, string.Empty);
                },
                message => scenario.SetString(KEY_NOTICE, message));
        }

        private void ApplyLabels(UiSandboxScenarioSO scenario)
        {
            for (int i = 0; i < _items.Count; ++i)
            {
                if (_items[i] == null)
                {
                    continue;
                }

                TMP_Text label = _items[i].GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    label.text = scenario.GetString(ItemKey(i));
                }
            }
        }

        // 연결 필드가 private이라 읽기 전용으로만 리플렉션을 쓴다. 값을 바꾸지 않는다
        private T ReadField<T>(string fieldName) where T : class
        {
            FieldInfo field = typeof(HudMenuDropdown).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            return (field == null) ? null : field.GetValue(_menu) as T;
        }
    }
}
