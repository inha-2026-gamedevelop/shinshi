// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Systems;

namespace Shinshi.UI
{
    /// <summary> 메뉴 버튼이나 Esc로 오른쪽 메뉴를 펼치고, 펼치는 동안 그 자리를 가리는 HUD를 숨긴다. 닫기 버튼, 항목 선택, 바깥 클릭, Esc로 접는다 </summary>
    public sealed class HudMenuDropdown : ClientView
    {
        // 항목이 실제로 하는 일은 이 컴포넌트가 정하지 않는다. 항목 버튼의 onClick에 창 열기를 연결하면 되고,
        // 이 컴포넌트는 항목을 누른 뒤 메뉴를 접는 일만 맡는다.
        // 이 컴포넌트가 붙은 오브젝트 자체를 숨김 대상에 넣으면 Update가 멈춰 Esc가 먹지 않으므로 넣지 않는다

        /****************************************
        *                Fields
        ****************************************/

        // Input.GetMouseButton 번호. 0 왼쪽
        private const int    MOUSE_BUTTON = 0;
        private const string ENTRY_PREFIX = "MD_Entry_";
        private const string CLOSE_TEXT   = "닫기";
        private const float  ENTRY_GAP    = 8f;

        private const string CONTENT_NAME        = "MD_MenuContent";
        private const float  SECTION_GAP         = 24f;
        private const float  BOTTOM_MARGIN       = 16f;
        private const float  SCROLL_SENSITIVITY  = 40f;
        private const float  COLUMN_EPSILON      = 1f;
        private const int    MAX_COLUMNS         = 2;
        private const int    CORNER_BOTTOM_LEFT  = 0;
        private const int    CORNER_TOP_LEFT     = 1;
        private const int    CORNER_COUNT        = 4;

        [SerializeField] private Button        _menuButton = null;
        [SerializeField] private RectTransform _panel      = null;

        [Header("펼치는 동안 숨길 HUD")]
        [SerializeField] private GameObject[] _hideWhileOpen = null;

        // Esc를 창 닫기와 나눠 쓴다. 나중에 등록한 처리기가 먼저 받으므로 창이 열려 있으면 창이 먼저 닫히고, 열린 창이 없을 때만 메뉴가 받는다
        [Header("단축키")]
        [SerializeField] private HotkeyRouter _router = null;

        private Func<bool> _onEscape = null;

        // 숨기기 전에 켜져 있던 것만 닫을 때 되살린다. Awake 전에는 비어 있고 EnsureHideState가 만든다
        private bool[] _wasActive = null;

        private readonly Vector3[] _corners = new Vector3[CORNER_COUNT];

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 참이면 Esc로 메뉴를 열고 닫지 않는다. Esc를 다른 화면이 쓰는 동안만 그 화면이 켜고 끈다, 평소는 거짓이다 </summary>
        public static bool IsEscapeSuppressed { get; set; } = false;

        /****************************************
        *              Unity Event
        ****************************************/

        // 패널이 없으면 메뉴 자체가 없으므로 끈다. 메뉴 버튼만 빠졌으면 오류를 남기되 Esc로는 계속 열 수 있게 켜 둔다
        protected override void OnViewAwake()
        {
            if (_panel == null)
            {
                Debug.LogError("[UI] 메뉴에 패널이 연결되지 않았다", this);
                enabled = false;
                return;
            }

            if (_menuButton == null)
            {
                Debug.LogError("[UI] 메뉴에 메뉴 버튼이 연결되지 않았다. Esc로만 열린다", this);
            }

            EnsureHideState();
            ApplyKoreanFont();
            _panel.gameObject.SetActive(false);
        }

        // Awake에서 꺼진 경우에도 유니티가 OnDisable을 부를 수 있어 OnEnable, OnDisable 모두 연결이 빠진 상태를 견딘다
        private void OnEnable()
        {
            if (_panel == null)
            {
                return;
            }

            _onEscape = OnEscape;
            if (_router != null)
            {
                _router.Register(EHotkeyAction.CloseWindow, _onEscape);
            }

            if (_menuButton != null)
            {
                _menuButton.onClick.AddListener(Toggle);
            }

            foreach (Button item in _panel.GetComponentsInChildren<Button>(true))
            {
                item.onClick.AddListener(Close);
            }
        }

        private void OnDisable()
        {
            if ((_router != null) && (_onEscape != null))
            {
                _router.Unregister(EHotkeyAction.CloseWindow, _onEscape);
            }

            if (_menuButton != null)
            {
                _menuButton.onClick.RemoveListener(Toggle);
            }

            if (_panel == null)
            {
                return;
            }

            foreach (Button item in _panel.GetComponentsInChildren<Button>(true))
            {
                item.onClick.RemoveListener(Close);
            }
        }

        private void Update()
        {
            if (_panel == null)
            {
                return;
            }

            // 라우터가 있으면 Esc는 라우터의 창 닫기 처리기로 받는다. 없을 때만 직접 본다
            if ((_router == null) && (!IsEscapeSuppressed) && (Input.GetKeyDown(KeyCode.Escape)))
            {
                Toggle();
                return;
            }

            if (!_panel.gameObject.activeSelf)
            {
                return;
            }

            // 메뉴 버튼은 자기 클릭에서 접으므로 여기서는 패널 밖이면서 메뉴 버튼도 아닌 곳만 본다
            if ((Input.GetMouseButtonDown(MOUSE_BUTTON)) && (!IsPointerOnMenu()))
            {
                Close();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 메뉴 항목을 하나 더한다. 같은 이름 항목이 있으면 새로 만들지 않고 돌려준다. 눌리면 onClick 뒤에 메뉴를 접는다 </summary>
        public Button AddEntry(string label, UnityAction onClick)
        {
            if ((_panel == null) || (string.IsNullOrEmpty(label)))
            {
                return null;
            }

            string entryName = ENTRY_PREFIX + label;
            Button existing = FindEntry(entryName);
            if (existing != null)
            {
                return existing;
            }

            RectTransform container = FindEntryContainer();
            if (container == null)
            {
                return null;
            }

            Button template = null;
            int insertIndex = 0;
            for (int i = 0; i < container.childCount; ++i)
            {
                Button item = container.GetChild(i).GetComponent<Button>();
                if ((item == null) || (IsCloseEntry(item)))
                {
                    continue;
                }

                if (!IsAddedEntry(item.transform))
                {
                    template = item;
                }

                insertIndex = i + 1;
            }

            if (template == null)
            {
                return null;
            }

            Button entry = Instantiate(template, container);
            entry.name = entryName;
            entry.onClick.RemoveAllListeners();
            for (int i = 0; i < entry.onClick.GetPersistentEventCount(); ++i)
            {
                entry.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
            }

            TMP_Text text = entry.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
            }

            entry.transform.SetSiblingIndex(insertIndex);
            if (onClick != null)
            {
                entry.onClick.AddListener(onClick);
            }

            entry.onClick.AddListener(Close);
            PlaceEntry(entry, template, container);
            ApplyKoreanFont();
            return entry;
        }

        // 씬에 저장된 메뉴 폰트에는 한글 글리프가 빠져 있어, 한국어 검증을 마친 공통 폰트(ModernDark 카탈로그)를 실행 중에만 씌운다. 원본 폰트 에셋은 건드리지 않는다
        private void ApplyKoreanFont()
        {
            ModernDarkUiCatalog catalog = ModernDarkUiCatalog.Load();
            TMP_FontAsset font = (catalog != null) ? catalog.Font : null;
            if ((font == null) || (_panel == null))
            {
                return;
            }

            foreach (TMP_Text text in _panel.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.font != font)
                {
                    text.font = font;
                }
            }
        }

        private Button FindEntry(string entryName)
        {
            foreach (Button item in _panel.GetComponentsInChildren<Button>(true))
            {
                if (item.name == entryName)
                {
                    return item;
                }
            }

            return null;
        }

        // 항목 버튼이 가장 많이 직접 달린 부모가 실제 본문이다. 패널 바로 아래가 아니어도 찾는다
        private RectTransform FindEntryContainer()
        {
            RectTransform best      = null;
            int           bestCount = 0;
            var           counts    = new Dictionary<Transform, int>();
            foreach (Button item in _panel.GetComponentsInChildren<Button>(true))
            {
                Transform parent = item.transform.parent;
                if ((IsCloseEntry(item)) || (!(parent is RectTransform rect)))
                {
                    continue;
                }

                counts.TryGetValue(parent, out int count);
                ++count;
                counts[parent] = count;
                if (count > bestCount)
                {
                    best      = rect;
                    bestCount = count;
                }
            }

            return best;
        }

        private static bool IsAddedEntry(Transform item)
        {
            return item.name.StartsWith(ENTRY_PREFIX, StringComparison.Ordinal);
        }

        private static bool IsCloseEntry(Button item)
        {
            TMP_Text text = item.GetComponentInChildren<TMP_Text>(true);
            return (item.name.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0)
                || ((text != null) && (!string.IsNullOrEmpty(text.text)) && (text.text.Contains(CLOSE_TEXT)));
        }

        // 배치 그룹이 없는 본문이면 기존 내용 맨 아래에 열 격자로 이어 붙인다. 기존 항목은 옮기지 않는다
        private void PlaceEntry(Button entry, Button template, RectTransform container)
        {
            if (container.GetComponent<LayoutGroup>() != null)
            {
                LayoutRebuilder.MarkLayoutForRebuild(container);
                return;
            }

            var rect         = (RectTransform)entry.transform;
            var templateRect = (RectTransform)template.transform;
            float step       = templateRect.rect.height + ENTRY_GAP;

            var columns = new List<float>();
            float bottom = GetCorner(container, templateRect, CORNER_BOTTOM_LEFT).y;
            int slot     = 0;
            for (int i = 0; i < container.childCount; ++i)
            {
                var child = container.GetChild(i) as RectTransform;
                if ((child == null) || (child == rect) || (!child.gameObject.activeSelf))
                {
                    continue;
                }

                if (IsAddedEntry(child))
                {
                    ++slot;
                    continue;
                }

                if ((!Mathf.Approximately(child.anchorMin.y, 1f)) || (!Mathf.Approximately(child.anchorMax.y, 1f)))
                {
                    continue;
                }

                bottom = Mathf.Min(bottom, GetCorner(container, child, CORNER_BOTTOM_LEFT).y);
                Button button = child.GetComponent<Button>();
                if ((button != null) && (!IsCloseEntry(button)))
                {
                    AddColumn(columns, GetCorner(container, child, CORNER_BOTTOM_LEFT).x);
                }
            }

            Vector2 templateTopLeft = GetCorner(container, templateRect, CORNER_TOP_LEFT);
            if (columns.Count == 0)
            {
                columns.Add(templateTopLeft.x);
            }

            int columnCount = Mathf.Min(columns.Count, MAX_COLUMNS);
            float left      = columns[slot % columnCount];
            float top       = bottom - SECTION_GAP - ((slot / columnCount) * step);
            rect.anchoredPosition = templateRect.anchoredPosition + new Vector2(left - templateTopLeft.x, top - templateTopLeft.y);

            float depth = container.rect.yMax - (top - templateRect.rect.height) + BOTTOM_MARGIN;
            RectTransform viewport = (container.name == CONTENT_NAME) ? (RectTransform)container.parent : container;
            if (depth > viewport.rect.height)
            {
                EnsureScroll(container, depth);
            }
        }

        private static void AddColumn(List<float> columns, float left)
        {
            for (int i = 0; i < columns.Count; ++i)
            {
                if (Mathf.Abs(columns[i] - left) < COLUMN_EPSILON)
                {
                    return;
                }
            }

            columns.Add(left);
            columns.Sort();
        }

        private Vector2 GetCorner(RectTransform container, RectTransform target, int corner)
        {
            target.GetWorldCorners(_corners);
            return container.InverseTransformPoint(_corners[corner]);
        }

        // 화면보다 길어지면 본문을 세로 스크롤 안에 넣는다. 기존 자식은 같은 좌표로 내용 상자로 옮긴다
        private static void EnsureScroll(RectTransform container, float depth)
        {
            RectTransform content = container;
            if (container.name != CONTENT_NAME)
            {
                var go = new GameObject(CONTENT_NAME, typeof(RectTransform));
                go.layer = container.gameObject.layer;
                content  = (RectTransform)go.transform;
                content.SetParent(container, false);
                content.anchorMin        = new Vector2(0f, 1f);
                content.anchorMax        = new Vector2(1f, 1f);
                content.pivot            = new Vector2(0f, 1f);
                content.anchoredPosition = Vector2.zero;
                content.sizeDelta        = new Vector2(0f, container.rect.height);

                var children = new List<Transform>();
                for (int i = 0; i < container.childCount; ++i)
                {
                    Transform child = container.GetChild(i);
                    if (child != content)
                    {
                        children.Add(child);
                    }
                }

                for (int i = 0; i < children.Count; ++i)
                {
                    children[i].SetParent(content, false);
                }

                if (container.GetComponent<RectMask2D>() == null)
                {
                    container.gameObject.AddComponent<RectMask2D>();
                }

                if (container.GetComponent<Graphic>() == null)
                {
                    var hit = container.gameObject.AddComponent<Image>();
                    hit.color = Color.clear;
                }

                ScrollRect scroll = container.GetComponent<ScrollRect>();
                if (scroll == null)
                {
                    scroll = container.gameObject.AddComponent<ScrollRect>();
                }

                scroll.viewport           = container;
                scroll.content            = content;
                scroll.horizontal         = false;
                scroll.vertical           = true;
                scroll.movementType       = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity  = SCROLL_SENSITIVITY;
            }

            if (depth > content.rect.height)
            {
                content.sizeDelta = new Vector2(content.sizeDelta.x, depth);
            }
        }

        private bool OnEscape()
        {
            if ((IsEscapeSuppressed) || (_panel == null))
            {
                return false;
            }

            Toggle();
            return true;
        }

        private void Toggle()
        {
            if (_panel == null)
            {
                return;
            }

            if (_panel.gameObject.activeSelf)
            {
                Close();
                return;
            }

            Open();
        }

        // 숨김 목록 길이와 기억 배열 길이가 어긋나면 다시 만든다. Awake를 거치지 않고 열린 경우도 여기서 맞춘다
        private void EnsureHideState()
        {
            int count = (_hideWhileOpen != null) ? _hideWhileOpen.Length : 0;
            if ((_wasActive == null) || (_wasActive.Length != count))
            {
                _wasActive = new bool[count];
            }
        }

        private void Open()
        {
            if (_panel == null)
            {
                return;
            }

            EnsureHideState();
            for (int i = 0; i < _wasActive.Length; ++i)
            {
                GameObject target = _hideWhileOpen[i];
                _wasActive[i] = (target != null) && (target.activeSelf);
                if (_wasActive[i])
                {
                    target.SetActive(false);
                }
            }

            _panel.gameObject.SetActive(true);
            UiSound.Play(EUiSoundGroup.Hud, EUiSoundAction.Open);
        }

        private void Close()
        {
            if ((_panel == null) || (!_panel.gameObject.activeSelf))
            {
                return;
            }

            _panel.gameObject.SetActive(false);
            UiSound.Play(EUiSoundGroup.Common, EUiSoundAction.Close);
            EnsureHideState();
            for (int i = 0; i < _wasActive.Length; ++i)
            {
                if ((_wasActive[i]) && (_hideWhileOpen[i] != null))
                {
                    _hideWhileOpen[i].SetActive(true);
                }
            }
        }

        private bool IsPointerOnMenu()
        {
            Vector2 point = Input.mousePosition;
            if (RectTransformUtility.RectangleContainsScreenPoint(_panel, point, null))
            {
                return true;
            }

            return (_menuButton != null)
                && (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_menuButton.transform, point, null));
        }
    }
}
