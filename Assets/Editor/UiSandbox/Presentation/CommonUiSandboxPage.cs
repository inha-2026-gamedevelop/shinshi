using System;
using System.Reflection;
using Shinshi.Common;
using Shinshi.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Shinshi.Editor
{
    /// <summary>
    /// 공통 UI 중 단독 배치가 없던 TooltipView · SaveStatusView · UiButtonFeedback · UiWindowDragHandle · UiCursor를 한 화면에서 시험한다.
    /// 마우스 올림 · 누름 · 끌기 · 실제 커서 그림은 물리 입력이라 샌드박스가 대신하지 않고, 같은 공개 메서드를 합성 이벤트로 부르는 보조 버튼만 둔다
    /// </summary>
    public sealed class CommonUiSandboxPage : PresentationPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "common.ui";
        public const string SCENE_PATH = "Assets/00.Scenes/Release/ShinshiTown.unity";

        public const string ITEM_SLOT_PREFAB_PATH = "Assets/02.Prefab/UI/PF_ItemSlot.prefab";
        public const string CURSOR_PREFAB_PATH    = "Assets/02.Prefab/UI/PF_UiCursor.prefab";

        public const string KEY_TIP_TITLE   = "common.tooltip.title";
        public const string KEY_TIP_BODY    = "common.tooltip.body";
        public const string KEY_TIP_X       = "common.tooltip.x";
        public const string KEY_TIP_Y       = "common.tooltip.y";
        public const string KEY_SAVE_STATUS = "common.save.status";
        public const string KEY_DRAG_X      = "common.drag.x";
        public const string KEY_DRAG_Y      = "common.drag.y";
        public const string KEY_CURSOR_ON   = "common.cursor.on";

        public const string OP_SAVE = "common.save";

        public const string LONG_BODY =
            "긴 설명 시험: 이 문장은 말풍선 폭을 넘어 여러 줄로 접히는지, 그리고 늘어난 높이만큼 영역 가장자리에서 안쪽으로 밀려 보정되는지 확인하기 위한 것이다. " +
            "실제 아이템 설명 문구가 아니며 임의로 지어낸 시험용 글이다.";

        private const string SHORT_BODY = "짧은 설명 한 줄 (시험)";

        private const string LABEL_TOOLTIP = "툴팁";
        private const string LABEL_SAVE    = "저장 상태";
        private const string LABEL_BUTTON  = "버튼 피드백";
        private const string LABEL_DRAG    = "끌 수 있는 창";

        private const string FIELD_PANEL  = "_panel";
        private const string FIELD_TITLE  = "_titleLabel";
        private const string FIELD_BODY   = "_bodyLabel";
        private const string FIELD_WINDOW = "_window";
        private const string FIELD_AREA   = "_area";
        private const string FIELD_TWEEN  = "_tween";
        private const string METHOD_AWAKE = "Awake";
        private const string SLOT_ITEM_ID = "sandbox_tooltip_item";

        private const float AREA_WIDTH     = 360.0f;
        private const float AREA_HEIGHT    = 220.0f;
        private const float PANEL_PADDING  = 8.0f;
        private const float PANEL_WIDTH    = 240.0f;
        private const float SLOT_MARGIN    = 12.0f;
        private const float BUTTON_AREA_W  = 220.0f;
        private const float BUTTON_AREA_H  = 120.0f;
        private const float BUTTON_WIDTH   = 150.0f;
        private const float BUTTON_HEIGHT  = 32.0f;
        private const float WINDOW_WIDTH   = 160.0f;
        private const float WINDOW_HEIGHT  = 90.0f;
        private const float TITLE_BAR_H    = 24.0f;
        private const float EDGE_MARGIN    = 8.0f;
        private const float INSIDE_EPSILON = 0.01f;
        private const float DRAG_RANGE_MIN = -0.25f;
        private const float DRAG_RANGE_MAX = 1.25f;
        private const float TIP_RANGE_MIN  = 0.0f;
        private const float TIP_RANGE_MAX  = 1.0f;
        private const float FONT_TITLE     = 16.0f;
        private const float FONT_SMALL     = 12.0f;

        private static readonly Color G_AreaColor   = new Color(0.1f, 0.12f, 0.16f, 0.9f);
        private static readonly Color G_PanelColor  = new Color(0.04f, 0.05f, 0.08f, 0.96f);
        private static readonly Color G_WindowColor = new Color(0.2f, 0.28f, 0.4f, 0.96f);
        private static readonly Color G_BarColor    = new Color(0.32f, 0.44f, 0.62f, 1.0f);
        private static readonly Color G_TitleColor  = new Color(1.0f, 0.82f, 0.4f, 1.0f);

        private enum EPointerStep
        {
            Enter,
            Down,
            Up,
            Exit
        }

        private TooltipView        _tooltip;
        private RectTransform      _tooltipArea;
        private ItemSlotView       _tooltipSlot;
        private SaveStatusView     _saveView;
        private UiButtonFeedback   _feedbackNormal;
        private UiButtonFeedback   _feedbackDisabled;
        private UiWindowDragHandle _dragHandle;
        private RectTransform      _dragArea;
        private RectTransform      _dragWindow;
        private GameObject         _cursorObject;
        private UiCursor           _cursor;
        private bool               _attachedPlaying;
        private bool               _savePending;
        private int                _savePrevious;
        private UiSandboxContext   _saveContext;
        private bool               _feedbackTargetDisabled;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "공통 UI 샌드박스"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get
            {
                return "TooltipView · SaveStatusView · UiButtonFeedback · UiWindowDragHandle · UiCursor(Assets/01.Scripts/06.UI/Common)는 실제 View이며 Dev_ItemSlot 씬은 툴팁과 아이템 칸만 담는다. " +
                       "각 View를 시험 배치에 붙여 툴팁 표시 · 숨김 · 긴 글 보정, 저장 상태 4종, 버튼 반응 표본, 끌 수 있는 창, Play 전용 게임 커서를 조작한다. " +
                       "마우스 올림 · 누름 · 끌기와 커서 그림은 물리 입력이라 직접 해 보아야 하며, 에디터 모드에서는 View의 Awake가 불리지 않아 말풍선과 끌기 창만 같은 Awake를 리플렉션으로 불러 준비시킨다. " +
                       "버튼 반응은 DOTween이라 Play에서만 움직이고, 커서는 PF_UiCursor 프리팹이 있을 때만 Play에서 켠다. " +
                       PresentationNotes.FIXTURE_NOTE;
            }
        }

        public TooltipView Tooltip
        {
            get { return _tooltip; }
        }

        public ItemSlotView TooltipSlot
        {
            get { return _tooltipSlot; }
        }

        public SaveStatusView SaveView
        {
            get { return _saveView; }
        }

        public UiButtonFeedback FeedbackNormal
        {
            get { return _feedbackNormal; }
        }

        public UiButtonFeedback FeedbackDisabled
        {
            get { return _feedbackDisabled; }
        }

        public UiWindowDragHandle DragHandle
        {
            get { return _dragHandle; }
        }

        public RectTransform DragWindow
        {
            get { return _dragWindow; }
        }

        public RectTransform DragArea
        {
            get { return _dragArea; }
        }

        public GameObject CursorObject
        {
            get { return _cursorObject; }
        }

        public bool IsCursorPrefabAvailable
        {
            get { return LoadCursorPrefab() != null; }
        }

        public bool IsTooltipVisible
        {
            get
            {
                RectTransform panel = GetPrivate(_tooltip, FIELD_PANEL) as RectTransform;
                return (panel != null) && (panel.gameObject.activeSelf);
            }
        }

        /// <summary> UiButtonFeedback의 Awake가 이미 불려 DOTween 트윈이 만들어졌는지. 에디터 모드에서는 불리지 않는다 </summary>
        public bool IsFeedbackReady
        {
            get { return (_feedbackNormal != null) && (GetPrivate(_feedbackNormal, FIELD_TWEEN) != null); }
        }

        /****************************************
        *                Methods
        ****************************************/

        protected override bool NeedsReattach(UiSandboxContext context)
        {
            return (!IsRefused) && (context.IsPlaying != _attachedPlaying);
        }

        protected override void Seed(UiSandboxScenarioSO scenario)
        {
            scenario.SeedString(KEY_TIP_TITLE, "시험 아이템");
            scenario.SeedString(KEY_TIP_BODY, SHORT_BODY);
            scenario.SeedFloat(KEY_TIP_X, 0.5f);
            scenario.SeedFloat(KEY_TIP_Y, 0.5f);
            scenario.SeedInt(KEY_SAVE_STATUS, (int)ESaveStatus.Saved);
            scenario.SeedFloat(KEY_DRAG_X, 0.5f);
            scenario.SeedFloat(KEY_DRAG_Y, 0.5f);
            scenario.SeedBool(KEY_CURSOR_ON, false);
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            _attachedPlaying = context.IsPlaying;

            BuildTooltip();
            BuildSaveStatus();
            BuildButtonFeedback();
            BuildDragWindow();

            for (int i = 0; i < Fixtures.Count; ++i)
            {
                if (Fixtures[i].IsAlive)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)Fixtures[i].Root.transform);
                }
            }

            EnsureAwake(_tooltip, FIELD_AREA);
            EnsureAwake(_dragHandle, FIELD_AREA);

            ApplyAll(context);
        }

        protected override void OnApply(UiSandboxContext context)
        {
            ApplyAll(context);
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;

            if (RestoreCanceledSave(context))
            {
                MarkDirty();
            }

            DrawTooltipControls(scenario);
            DrawSaveControls(context);
            DrawFeedbackControls();
            DrawDragControls(scenario);
            DrawCursorControls(context);
        }

        protected override void OnDetach()
        {
            if ((_saveContext != null) && (!_saveContext.IsDisposed))
            {
                RestoreCanceledSave(_saveContext);
            }

            _saveContext = null;
            DestroyCursor();

            _tooltip                = null;
            _tooltipArea            = null;
            _tooltipSlot            = null;
            _saveView               = null;
            _feedbackNormal         = null;
            _feedbackDisabled       = null;
            _dragHandle             = null;
            _dragArea               = null;
            _dragWindow             = null;
            _savePending            = false;
            _attachedPlaying        = false;
            _feedbackTargetDisabled = false;
        }

        /****************************************
        *              Tooltip
        ****************************************/

        private void BuildTooltip()
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions(EFixtureAnchor.TopLeft).WithOffset(24.0f, -24.0f);
            ItemSlotView slotPrefab = AssetDatabase.LoadAssetAtPath<ItemSlotView>(ITEM_SLOT_PREFAB_PATH);

            _tooltip = CreateScaffold(LABEL_TOOLTIP, options, host =>
            {
                RectTransform area = NewArea(host, "area", AREA_WIDTH, AREA_HEIGHT);
                _tooltipArea = area;

                if (slotPrefab != null)
                {
                    ItemSlotView slot = UnityEngine.Object.Instantiate(slotPrefab, area);
                    RectTransform slotRect = (RectTransform)slot.transform;
                    slotRect.anchorMin        = Vector2.up;
                    slotRect.anchorMax        = Vector2.up;
                    slotRect.pivot            = Vector2.up;
                    slotRect.anchoredPosition = new Vector2(SLOT_MARGIN, -SLOT_MARGIN);
                    _tooltipSlot = slot;
                }

                GameObject panelObject = PresentationFixtureFactory.NewUi("panel", area);
                RectTransform panel = (RectTransform)panelObject.transform;
                panel.anchorMin = new Vector2(0.5f, 0.5f);
                panel.anchorMax = new Vector2(0.5f, 0.5f);
                panel.pivot     = Vector2.up;

                Image back = panelObject.AddComponent<Image>();
                back.color         = G_PanelColor;
                back.raycastTarget = false;

                PresentationFixtureFactory.AddVerticalLayout(panelObject, (int)PANEL_PADDING);
                ContentSizeFitter fitter = panelObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

                TMP_Text title = PresentationFixtureFactory.CreateText(panel, "title", string.Empty, FONT_TITLE);
                title.color = G_TitleColor;
                TMP_Text body = PresentationFixtureFactory.CreateText(panel, "body", string.Empty, FONT_SMALL + 2.0f);
                PresentationFixtureFactory.SetPreferred(title.gameObject, PANEL_WIDTH, -1.0f, -1.0f);
                PresentationFixtureFactory.SetPreferred(body.gameObject, PANEL_WIDTH, -1.0f, -1.0f);

                TooltipView view = area.gameObject.AddComponent<TooltipView>();
                SetPrivate(view, FIELD_PANEL, panel);
                SetPrivate(view, FIELD_TITLE, title);
                SetPrivate(view, FIELD_BODY, body);
                return view;
            });
        }

        /// <summary> 툴팁을 현재 시나리오의 제목 · 본문 · 위치로 띄운다. 말풍선을 만들지 못했으면 false </summary>
        public bool ShowTooltip()
        {
            if ((_tooltip == null) || (_tooltipArea == null) || (Context == null))
            {
                return false;
            }

            UiSandboxScenarioSO scenario = Context.Scenario;
            Vector2 screen = AreaToScreen(_tooltipArea, scenario.GetFloat(KEY_TIP_X), scenario.GetFloat(KEY_TIP_Y));
            _tooltip.Show(scenario.GetString(KEY_TIP_TITLE), scenario.GetString(KEY_TIP_BODY), screen);
            return true;
        }

        public bool HideTooltip()
        {
            if (_tooltip == null)
            {
                return false;
            }

            _tooltip.Hide();
            return true;
        }

        /// <summary> 실제 ItemSlotView.OnPointerEnter · Exit를 합성 이벤트로 부른다. 물리 호버가 아니라 같은 공개 메서드 호출이다 </summary>
        public bool HoverTooltipSlot(bool enter)
        {
            if ((_tooltipSlot == null) || (_tooltip == null))
            {
                return false;
            }

            PointerEventData data = new PointerEventData(EventSystem.current);
            if (enter)
            {
                data.position = AreaToScreen(_tooltipArea, 0.5f, 0.5f);
                _tooltipSlot.OnPointerEnter(data);
            }
            else
            {
                _tooltipSlot.OnPointerExit(data);
            }

            return true;
        }

        /// <summary> 보이는 말풍선이 영역 가장자리 여백 안에 있는지. 보이지 않거나 영역이 없으면 false </summary>
        public bool IsTooltipInsideArea()
        {
            RectTransform panel = GetPrivate(_tooltip, FIELD_PANEL) as RectTransform;
            if ((panel == null) || (_tooltipArea == null) || (!panel.gameObject.activeSelf))
            {
                return false;
            }

            Rect inner = _tooltipArea.rect;
            inner.xMin += EDGE_MARGIN - INSIDE_EPSILON;
            inner.yMin += EDGE_MARGIN - INSIDE_EPSILON;
            inner.xMax -= EDGE_MARGIN - INSIDE_EPSILON;
            inner.yMax -= EDGE_MARGIN - INSIDE_EPSILON;

            Rect panelRect = new Rect((Vector2)panel.localPosition - Vector2.Scale(panel.pivot, panel.rect.size), panel.rect.size);
            return (panelRect.xMin >= inner.xMin) && (panelRect.xMax <= inner.xMax)
                && (panelRect.yMin >= inner.yMin) && (panelRect.yMax <= inner.yMax);
        }

        private void DrawTooltipControls(UiSandboxScenarioSO scenario)
        {
            DrawHeader("툴팁 (TooltipView)");
            DrawString(scenario, KEY_TIP_TITLE, "제목");
            DrawString(scenario, KEY_TIP_BODY, "본문");
            DrawFloat(scenario, KEY_TIP_X, "가로 위치 (0 왼쪽 ~ 1 오른쪽)", TIP_RANGE_MIN, TIP_RANGE_MAX);
            DrawFloat(scenario, KEY_TIP_Y, "세로 위치 (0 아래 ~ 1 위)", TIP_RANGE_MIN, TIP_RANGE_MAX);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("짧은 글"))
                {
                    scenario.SetString(KEY_TIP_BODY, SHORT_BODY);
                    MarkDirty();
                }

                if (GUILayout.Button("긴 글"))
                {
                    scenario.SetString(KEY_TIP_BODY, LONG_BODY);
                    MarkDirty();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("툴팁 표시"))
                {
                    ShowTooltip();
                }

                if (GUILayout.Button("툴팁 숨김"))
                {
                    HideTooltip();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginDisabledGroup(_tooltipSlot == null);
                if (GUILayout.Button("아이템 칸 올림 호출"))
                {
                    HoverTooltipSlot(true);
                }

                if (GUILayout.Button("아이템 칸 벗어남 호출"))
                {
                    HoverTooltipSlot(false);
                }

                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.LabelField("말풍선", IsTooltipVisible ? (IsTooltipInsideArea() ? "보임 (영역 안)" : "보임 (영역 밖)") : "숨김");
            if (_tooltipSlot == null)
            {
                EditorGUILayout.HelpBox("PF_ItemSlot 프리팹을 찾지 못해 아이템 칸 연결 시험은 건너뛴다: " + ITEM_SLOT_PREFAB_PATH, MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("실제 마우스 호버(칸 위에 올리기)는 Play에서 직접 확인한다. 위 호출 버튼은 같은 OnPointerEnter · Exit를 합성 이벤트로 부를 뿐이다.", MessageType.None);
            }
        }

        /****************************************
        *             Save Status
        ****************************************/

        private void BuildSaveStatus()
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions(EFixtureAnchor.TopRight).WithOffset(-24.0f, -24.0f);
            _saveView = CreateFixture<SaveStatusView>(LABEL_SAVE, options);
        }

        /// <summary> 저장 요청을 모의로 보낸다. 시작되면 Saving, 성공이면 Saved, 실패면 Retrying으로 View에 표시한다 </summary>
        public bool StartSave(UiSandboxContext context)
        {
            if ((context == null) || (!IsAttached) || (_saveView == null))
            {
                return false;
            }

            int previous = context.Scenario.GetInt(KEY_SAVE_STATUS);
            bool started = RunRequest(context, OP_SAVE,
                () =>
                {
                    _savePending = false;
                    SetSaveStatus(context, ESaveStatus.Saved);
                },
                message =>
                {
                    _savePending = false;
                    SetSaveStatus(context, ESaveStatus.Retrying);
                });

            if (!started)
            {
                return false;
            }

            _savePending  = true;
            _savePrevious = previous;
            _saveContext  = context;
            SetSaveStatus(context, ESaveStatus.Saving);
            return true;
        }

        private void SetSaveStatus(UiSandboxContext context, ESaveStatus status)
        {
            context.Scenario.SetInt(KEY_SAVE_STATUS, (int)status);
            if (_saveView != null)
            {
                _saveView.SetStatus(status);
            }
        }

        // 취소에는 콜백이 없다. 저장 요청이 더는 진행 중이 아닌데 아직 Saving이면 요청 전 상태로 되돌린다
        private bool RestoreCanceledSave(UiSandboxContext context)
        {
            if ((!_savePending) || ((context.IsBusy) && (context.PendingOperation == OP_SAVE)))
            {
                return false;
            }

            _savePending = false;
            if (context.Scenario.GetInt(KEY_SAVE_STATUS) != (int)ESaveStatus.Saving)
            {
                return false;
            }

            context.Scenario.SetInt(KEY_SAVE_STATUS, _savePrevious);
            return true;
        }

        private void DrawSaveControls(UiSandboxContext context)
        {
            DrawHeader("저장 상태 (SaveStatusView)");
            DrawEnum(context.Scenario, KEY_SAVE_STATUS, "상태", ESaveStatus.Saved);
            EditorGUILayout.LabelField("View 상태", (_saveView == null) ? "없음" : _saveView.Status.ToString());

            if (GUILayout.Button("저장 시작 (모의 요청: 성공하면 저장 완료, 실패하면 재시도 중)"))
            {
                StartSave(context);
            }
        }

        /****************************************
        *           Button Feedback
        ****************************************/

        private void BuildButtonFeedback()
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions(EFixtureAnchor.BottomLeft).WithOffset(24.0f, 24.0f);

            CreateScaffold<UiButtonFeedback>(LABEL_BUTTON, options, host =>
            {
                RectTransform area = NewArea(host, "area", BUTTON_AREA_W, BUTTON_AREA_H);
                _feedbackNormal   = AddFeedbackButton(area, "feedback_normal", "반응 버튼", new Vector2(BUTTON_AREA_W * 0.5f, -32.0f), true);
                _feedbackDisabled = AddFeedbackButton(area, "feedback_disabled", "비활성 버튼 (반응 없음)", new Vector2(BUTTON_AREA_W * 0.5f, -84.0f), false);
                return _feedbackNormal;
            });
        }

        // 레이아웃 그룹 밖에 절대 위치로 둔다. UiButtonFeedback이 Awake에서 기준 위치를 기억하므로 레이아웃이 나중에 옮기면 안 된다
        private static UiButtonFeedback AddFeedbackButton(RectTransform area, string objectName, string label, Vector2 position, bool interactable)
        {
            Button button = PresentationFixtureFactory.CreateButton(area, objectName);
            button.transition   = Selectable.Transition.None;
            button.interactable = interactable;

            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text  = label;
                text.color = Color.black;
            }

            RectTransform rect = (RectTransform)button.transform;
            rect.anchorMin        = Vector2.up;
            rect.anchorMax        = Vector2.up;
            rect.pivot            = new Vector2(0.5f, 0.5f);
            rect.sizeDelta        = new Vector2(BUTTON_WIDTH, BUTTON_HEIGHT);
            rect.anchoredPosition = position;

            return button.gameObject.AddComponent<UiButtonFeedback>();
        }

        /// <summary> 포인터 단계를 합성 이벤트로 부른다. 트윈이 없으면(에디터 모드) 호출은 해도 아무 효과가 없다 </summary>
        private bool SimulatePointer(EPointerStep step, bool disabledTarget)
        {
            UiButtonFeedback target = disabledTarget ? _feedbackDisabled : _feedbackNormal;
            if (target == null)
            {
                return false;
            }

            PointerEventData data = new PointerEventData(EventSystem.current);
            switch (step)
            {
                case EPointerStep.Enter: target.OnPointerEnter(data); break;
                case EPointerStep.Down:  target.OnPointerDown(data);  break;
                case EPointerStep.Up:    target.OnPointerUp(data);    break;
                case EPointerStep.Exit:  target.OnPointerExit(data);  break;
            }

            return true;
        }

        private void DrawFeedbackControls()
        {
            DrawHeader("버튼 반응 (UiButtonFeedback)");

            if (!IsFeedbackReady)
            {
                EditorGUILayout.HelpBox("Awake가 아직 불리지 않아 트윈이 없다 (에디터 모드). 아래 호출은 아무 효과가 없고, 올림 · 누름 반응은 Play에서만 움직인다. 실제 마우스로 직접 확인한다.", MessageType.Warning);
            }

            _feedbackTargetDisabled = EditorGUILayout.Toggle("비활성 버튼에 보내기", _feedbackTargetDisabled);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("올림"))
                {
                    SimulatePointer(EPointerStep.Enter, _feedbackTargetDisabled);
                }

                if (GUILayout.Button("누름"))
                {
                    SimulatePointer(EPointerStep.Down, _feedbackTargetDisabled);
                }

                if (GUILayout.Button("뗌"))
                {
                    SimulatePointer(EPointerStep.Up, _feedbackTargetDisabled);
                }

                if (GUILayout.Button("벗어남"))
                {
                    SimulatePointer(EPointerStep.Exit, _feedbackTargetDisabled);
                }
            }

            UiButtonFeedback shown = _feedbackTargetDisabled ? _feedbackDisabled : _feedbackNormal;
            if (shown != null)
            {
                Graphic graphic = shown.GetComponent<Graphic>();
                EditorGUILayout.LabelField("크기", shown.transform.localScale.x.ToString("0.000"));
                EditorGUILayout.LabelField("색", (graphic == null) ? "없음" : ColorUtility.ToHtmlStringRGBA(graphic.color));
            }
        }

        /****************************************
        *            Drag Window
        ****************************************/

        private void BuildDragWindow()
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions(EFixtureAnchor.BottomRight).WithOffset(-24.0f, 24.0f);

            _dragHandle = CreateScaffold(LABEL_DRAG, options, host =>
            {
                RectTransform area = NewArea(host, "area", AREA_WIDTH, AREA_HEIGHT);
                _dragArea = area;

                GameObject windowObject = PresentationFixtureFactory.NewUi("window", area);
                RectTransform window = (RectTransform)windowObject.transform;
                window.anchorMin = new Vector2(0.5f, 0.5f);
                window.anchorMax = new Vector2(0.5f, 0.5f);
                window.pivot     = new Vector2(0.5f, 0.5f);
                window.sizeDelta = new Vector2(WINDOW_WIDTH, WINDOW_HEIGHT);
                Image windowImage = windowObject.AddComponent<Image>();
                windowImage.color         = G_WindowColor;
                windowImage.raycastTarget = false;
                _dragWindow = window;

                GameObject barObject = PresentationFixtureFactory.NewUi("titleBar", window);
                RectTransform bar = (RectTransform)barObject.transform;
                bar.anchorMin = Vector2.up;
                bar.anchorMax = Vector2.one;
                bar.pivot     = new Vector2(0.5f, 1.0f);
                bar.sizeDelta = new Vector2(0.0f, TITLE_BAR_H);
                Image barImage = barObject.AddComponent<Image>();
                barImage.color = G_BarColor;

                TMP_Text barText = PresentationFixtureFactory.CreateText(bar, "label", "제목줄 (끌기)", FONT_SMALL);
                barText.raycastTarget = false;
                RectTransform barTextRect = (RectTransform)barText.transform;
                barTextRect.anchorMin = Vector2.zero;
                barTextRect.anchorMax = Vector2.one;
                barTextRect.offsetMin = Vector2.zero;
                barTextRect.offsetMax = Vector2.zero;

                UiWindowDragHandle handle = barObject.AddComponent<UiWindowDragHandle>();
                SetPrivate(handle, FIELD_WINDOW, window);
                return handle;
            });
        }

        /// <summary> 창 중심을 영역의 정규 좌표(0~1, 범위 밖도 허용)로 옮기는 드래그를 합성 이벤트로 부른다. 물리 드래그가 아니다 </summary>
        public bool SimulateDrag(float normalizedX, float normalizedY)
        {
            if ((_dragHandle == null) || (_dragArea == null) || (_dragWindow == null))
            {
                return false;
            }

            PointerEventData begin = new PointerEventData(EventSystem.current);
            begin.position = RectTransformUtility.WorldToScreenPoint(null, _dragWindow.position);
            _dragHandle.OnBeginDrag(begin);

            PointerEventData drag = new PointerEventData(EventSystem.current);
            drag.position = AreaToScreen(_dragArea, normalizedX, normalizedY);
            _dragHandle.OnDrag(drag);
            return true;
        }

        public bool IsDragWindowInsideArea()
        {
            if ((_dragArea == null) || (_dragWindow == null))
            {
                return false;
            }

            Rect area      = _dragArea.rect;
            Vector2 size   = _dragWindow.rect.size;
            Vector2 center = _dragWindow.localPosition;
            return (center.x - (size.x * 0.5f) >= area.xMin - INSIDE_EPSILON) && (center.x + (size.x * 0.5f) <= area.xMax + INSIDE_EPSILON)
                && (center.y - (size.y * 0.5f) >= area.yMin - INSIDE_EPSILON) && (center.y + (size.y * 0.5f) <= area.yMax + INSIDE_EPSILON);
        }

        private void DrawDragControls(UiSandboxScenarioSO scenario)
        {
            DrawHeader("끌 수 있는 창 (UiWindowDragHandle)");
            DrawFloat(scenario, KEY_DRAG_X, "목표 가로 (0~1, 범위 밖도 가능)", DRAG_RANGE_MIN, DRAG_RANGE_MAX);
            DrawFloat(scenario, KEY_DRAG_Y, "목표 세로 (0~1, 범위 밖도 가능)", DRAG_RANGE_MIN, DRAG_RANGE_MAX);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("목표로 드래그 호출"))
                {
                    SimulateDrag(scenario.GetFloat(KEY_DRAG_X), scenario.GetFloat(KEY_DRAG_Y));
                }

                if (GUILayout.Button("창 가운데로"))
                {
                    if (_dragWindow != null)
                    {
                        _dragWindow.localPosition = Vector3.zero;
                    }
                }
            }

            if (_dragWindow != null)
            {
                EditorGUILayout.LabelField("창 위치 (영역 기준)", ((Vector2)_dragWindow.localPosition).ToString("0.0"));
                EditorGUILayout.LabelField("창이 영역 안", IsDragWindowInsideArea() ? "예" : "아니오");
            }

            EditorGUILayout.HelpBox("실제 마우스로 제목줄을 끌어 이동하는 것은 Play에서 직접 확인한다. 위 버튼은 OnBeginDrag · OnDrag를 합성 이벤트로 부르는 보조 호출이다.", MessageType.None);
        }

        /****************************************
        *                Cursor
        ****************************************/

        private static GameObject LoadCursorPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CURSOR_PREFAB_PATH);
            return ((prefab != null) && (prefab.GetComponent<UiCursor>() != null)) ? prefab : null;
        }

        private void SyncCursor(UiSandboxContext context)
        {
            GameObject prefab = LoadCursorPrefab();
            bool wanted = (context.IsPlaying) && (context.Scenario.GetBool(KEY_CURSOR_ON)) && (prefab != null);

            if ((wanted) && (_cursorObject == null))
            {
                _cursorObject           = UnityEngine.Object.Instantiate(prefab);
                _cursorObject.name      = "UiSandbox_Cursor";
                _cursorObject.hideFlags = HideFlags.DontSaveInEditor;
                if ((PresentationFixtureFactory.TryGetHostScene(out Scene host, out _)) && (_cursorObject.scene != host))
                {
                    SceneManager.MoveGameObjectToScene(_cursorObject, host);
                }

                _cursor = _cursorObject.GetComponent<UiCursor>();
            }
            else if ((!wanted) && (_cursorObject != null))
            {
                DestroyCursor();
            }
        }

        private void DestroyCursor()
        {
            if (_cursorObject == null)
            {
                _cursor = null;
                return;
            }

            UnityEngine.Object.DestroyImmediate(_cursorObject);
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            _cursorObject = null;
            _cursor       = null;
        }

        private void DrawCursorControls(UiSandboxContext context)
        {
            DrawHeader("게임 커서 (UiCursor, Play 전용)");

            if (LoadCursorPrefab() == null)
            {
                EditorGUILayout.HelpBox("의존 프리팹이 없다: " + CURSOR_PREFAB_PATH + " (UiCursor 포함). 대용 그림을 만들지 않으므로 커서 시험은 쓸 수 없다.", MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("의존 프리팹", CURSOR_PREFAB_PATH);

            EditorGUI.BeginDisabledGroup(!context.IsPlaying);
            DrawBool(context.Scenario, KEY_CURSOR_ON, "게임 커서 켜기");
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(_cursor == null);
            if (GUILayout.Button("불가 커서 깜빡임 (FlashBlocked)"))
            {
                _cursor.FlashBlocked();
            }

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.LabelField("커서 인스턴스", (_cursorObject == null) ? "없음" : "있음 (끄거나 화면을 나가면 OS 기본 커서로 복원)");
            EditorGUILayout.HelpBox(
                "커서 그림 자체는 Game 뷰에서 Play 중 직접 확인한다. 마우스 버튼 누름 그림과 UiCursorBlockedTarget 위의 불가 그림은 View가 실제 입력과 레이캐스트를 읽으므로 샌드박스가 흉내 내지 않는다.",
                MessageType.None);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void ApplyAll(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;

            if (_saveView != null)
            {
                _saveView.SetStatus((ESaveStatus)scenario.GetInt(KEY_SAVE_STATUS));
            }

            if (_tooltipSlot != null)
            {
                _tooltipSlot.Bind(_tooltip);
                _tooltipSlot.SetData(new ItemSlotData(
                    SLOT_ITEM_ID,
                    EItemGrade.Common,
                    1,
                    scenario.GetString(KEY_TIP_TITLE),
                    scenario.GetString(KEY_TIP_BODY)));
            }

            SyncCursor(context);
        }

        private static RectTransform NewArea(Transform parent, string objectName, float width, float height)
        {
            GameObject go = PresentationFixtureFactory.NewUi(objectName, parent);
            Image image = go.AddComponent<Image>();
            image.color         = G_AreaColor;
            image.raycastTarget = false;
            PresentationFixtureFactory.SetPreferred(go, width, height, height);
            return (RectTransform)go.transform;
        }

        private static Vector2 AreaToScreen(RectTransform area, float normalizedX, float normalizedY)
        {
            Rect rect = area.rect;
            Vector3 local = new Vector3(
                Mathf.LerpUnclamped(rect.xMin, rect.xMax, normalizedX),
                Mathf.LerpUnclamped(rect.yMin, rect.yMax, normalizedY),
                0.0f);
            return RectTransformUtility.WorldToScreenPoint(null, area.TransformPoint(local));
        }

        // 에디터 모드에서는 유니티가 Awake를 부르지 않는다. 준비 필드가 아직 비어 있을 때만 같은 Awake를 직접 호출한다 (Play에서는 이미 불려 건너뜀)
        private static void EnsureAwake(Component component, string readyField)
        {
            if ((component == null) || (GetPrivate(component, readyField) != null))
            {
                return;
            }

            MethodInfo awake = component.GetType().GetMethod(METHOD_AWAKE, BindingFlags.Instance | BindingFlags.NonPublic);
            if (awake == null)
            {
                throw new MissingMethodException(component.GetType().Name, METHOD_AWAKE);
            }

            awake.Invoke(component, null);
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
            {
                throw new MissingFieldException(target.GetType().Name, fieldName);
            }

            field.SetValue(target, value);
        }

        private static object GetPrivate(object target, string fieldName)
        {
            if (target == null)
            {
                return null;
            }

            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return (field == null) ? null : field.GetValue(target);
        }
    }
}
