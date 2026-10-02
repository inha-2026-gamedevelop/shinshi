// Shinshi.Runtime 어셈블리 안의 에디터 전용 창이라 에디터에서만 컴파일되게 통째로 감싼다
#if UNITY_EDITOR
// System
using System;

// Unity
using UnityEditor;
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary>배치 편집기(웹) JSON을 붙여 넣거나 골라 씬의 RectTransform 아래에 적용하는 창</summary>
    public sealed class UiLayoutImportWindow : EditorWindow
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_PATH   = "Shinshi/UI/배치 JSON 적용";
        private const string WINDOW_NAME = "UI 배치 적용";
        private const string CANVAS_NAME = "UiLayoutCanvas";
        private const float  TEXT_HEIGHT = 150f;
        private const float  MIN_WIDTH   = 420f;
        private const float  MIN_HEIGHT  = 560f;
        private const float  APPLY_LINES = 2f;

        private static readonly string[] G_PreviewLabels = { "전체 켜기", "필드 기준", "보스전 기준" };

        private TextAsset                       _jsonAsset;
        private string                          _json            = string.Empty;
        private RectTransform                   _parent;
        private bool                            _addPlaceholders = true;
        private bool                            _addNotes        = true;
        private UiLayoutImporter.EPreviewFilter _preview         = UiLayoutImporter.EPreviewFilter.All;
        private Vector2                         _scroll;
        private string                          _message;
        private MessageType                     _messageType;
        private UiLayoutImporter.Summary        _summary;
        private string                          _summaryError;
        private string                          _summarySource;

        /****************************************
        *                 Menu
        ****************************************/

        [MenuItem(MENU_PATH)]
        public static void Open()
        {
            UiLayoutImportWindow window = GetWindow<UiLayoutImportWindow>(WINDOW_NAME);
            window.minSize = new Vector2(MIN_WIDTH, MIN_HEIGHT);
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if (Selection.activeTransform is RectTransform selected)
            {
                _parent = selected;
            }
        }

        private void OnSelectionChange()
        {
            if ((_parent != null) || (!(Selection.activeTransform is RectTransform selected)))
            {
                return;
            }
            _parent = selected;
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("UI 배치 JSON 적용", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("웹 배치 편집기에서 'JSON 복사'한 내용을 붙여 넣거나 JSON 파일을 고르세요. "
                + "같은 id는 위치, 크기, 순서만 갱신합니다. 그래픽을 바꿔 둔 오브젝트와 JSON에 없는 오브젝트는 건드리지 않습니다.", MessageType.Info);

            DrawSource();
            DrawSummary();
            EditorGUILayout.Space();
            DrawTarget();
            EditorGUILayout.Space();
            DrawOptions();
            EditorGUILayout.Space();
            DrawApply();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void DrawSource()
        {
            EditorGUILayout.LabelField("1. JSON", EditorStyles.miniBoldLabel);
            _jsonAsset = (TextAsset)EditorGUILayout.ObjectField("JSON 파일", _jsonAsset, typeof(TextAsset), false);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("클립보드에서 붙여넣기"))
            {
                _json = EditorGUIUtility.systemCopyBuffer ?? string.Empty;
                _jsonAsset = null;
                GUI.FocusControl(null);
            }
            if (GUILayout.Button("지우기"))
            {
                _json = string.Empty;
                _jsonAsset = null;
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
            if (_jsonAsset != null)
            {
                return;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(TEXT_HEIGHT));
            _json = EditorGUILayout.TextArea(_json, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private string CurrentJson()
        {
            return (_jsonAsset != null) ? _jsonAsset.text : _json;
        }

        private void DrawSummary()
        {
            RefreshSummary(CurrentJson());
            if (_summaryError != null)
            {
                EditorGUILayout.HelpBox(_summaryError, MessageType.Error);
                return;
            }
            if (_summary == null)
            {
                return;
            }
            string text = $"레이아웃 '{_summary.LayoutName}' - 요소 {_summary.NodeCount}개";
            if (_summary.ReferenceCount > 0)
            {
                text += $" (참고용 {_summary.ReferenceCount}개는 만들지 않음)";
            }
            if (!string.IsNullOrEmpty(_summary.LayoutDescription))
            {
                text += "\n" + _summary.LayoutDescription;
            }
            EditorGUILayout.HelpBox(text, MessageType.None);
        }

        // 매 OnGUI마다 파싱하지 않게 원문이 바뀌었을 때만 다시 읽는다
        private void RefreshSummary(string source)
        {
            if (source == _summarySource)
            {
                return;
            }
            _summarySource = source;
            _summary = null;
            _summaryError = null;
            if (string.IsNullOrWhiteSpace(source))
            {
                return;
            }
            try
            {
                _summary = UiLayoutImporter.Inspect(source);
            }
            catch (Exception e)
            {
                _summaryError = e.Message;
            }
        }

        private void DrawTarget()
        {
            EditorGUILayout.LabelField("2. 넣을 곳", EditorStyles.miniBoldLabel);
            _parent = (RectTransform)EditorGUILayout.ObjectField("부모 RectTransform", _parent, typeof(RectTransform), true);
            if (_parent == null)
            {
                EditorGUILayout.HelpBox("Hierarchy에서 Canvas나 패널을 고르거나 새 Canvas를 만드세요. 부모 영역이 1920x1080 기준 화면 역할을 합니다.", MessageType.Warning);
                if (GUILayout.Button("새 Canvas 만들기 (1920x1080)"))
                {
                    _parent = UiLayoutImporter.CreateCanvas(CANVAS_NAME);
                    Selection.activeObject = _parent.gameObject;
                }
                return;
            }
            if (EditorUtility.IsPersistent(_parent))
            {
                EditorGUILayout.HelpBox("프로젝트의 프리팹 에셋에는 바로 적용하지 않습니다. 프리팹을 열고(Prefab Mode) 그 안에서 고르세요.", MessageType.Error);
                return;
            }
            Rect rect = _parent.rect;
            EditorGUILayout.LabelField("대상 씬", _parent.gameObject.scene.name);
            EditorGUILayout.LabelField("부모 크기", $"{rect.width:0} x {rect.height:0}");
        }

        private void DrawOptions()
        {
            EditorGUILayout.LabelField("3. 옵션", EditorStyles.miniBoldLabel);
            _addPlaceholders = EditorGUILayout.Toggle(new GUIContent("자리표시 그래픽", "새로 만드는 박스와 원형에만 옅은 Image를 붙입니다"), _addPlaceholders);
            _addNotes = EditorGUILayout.Toggle(new GUIContent("설명 컴포넌트", "UiLayoutNote에 id, 설명, 표시 조건을 남깁니다. 다시 적용할 때 같은 오브젝트를 찾는 열쇠입니다"), _addNotes);
            _preview = (UiLayoutImporter.EPreviewFilter)EditorGUILayout.Popup("조건부 요소", (int)_preview, G_PreviewLabels);
        }

        private void DrawApply()
        {
            bool canApply = (_summary != null) && (_parent != null) && (!EditorUtility.IsPersistent(_parent));
            using (new EditorGUI.DisabledScope(!canApply))
            {
                if (GUILayout.Button("적용", GUILayout.Height(EditorGUIUtility.singleLineHeight * APPLY_LINES)))
                {
                    Apply();
                }
            }
            if (!string.IsNullOrEmpty(_message))
            {
                EditorGUILayout.HelpBox(_message, _messageType);
            }
        }

        private void Apply()
        {
            UiLayoutImporter.Options options = new UiLayoutImporter.Options
            {
                AddPlaceholderGraphics = _addPlaceholders,
                AddNotes = _addNotes,
                Preview = _preview,
            };
            UiLayoutImporter.Report report;
            try
            {
                report = UiLayoutImporter.Apply(CurrentJson(), _parent, options);
            }
            catch (Exception e)
            {
                _message = e.Message;
                _messageType = MessageType.Error;
                Debug.LogException(e);
                return;
            }

            _message = report.ToString();
            _messageType = (report.Untouched.Count > 0) ? MessageType.Warning : MessageType.Info;
            Selection.activeObject = report.Root;
            EditorGUIUtility.PingObject(report.Root);
            Debug.Log($"[UiLayoutImporter] {report.Root.name}: {_message}", report.Root);
        }
    }
}
#endif
