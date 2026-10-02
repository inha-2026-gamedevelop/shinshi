// Shinshi.Runtime 어셈블리 안의 에디터 전용 도구라 에디터에서만 컴파일되게 통째로 감싼다
#if UNITY_EDITOR
// System
using System.Collections.Generic;
using System.IO;
using System.Text;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Shinshi.UI
{
    /// <summary>
    /// 배치 편집기(웹)에서 내보낸 JSON으로 RectTransform 계층을 만들거나 갱신한다
    ///
    /// 다시 적용해도 안전해야 한다. 같은 id의 오브젝트는 위치, 크기, 부모, 순서만 맞추고
    /// 이미 붙어 있는 그래픽과 다른 컴포넌트는 건드리지 않는다. JSON에 없는 오브젝트도 지우지 않고 보고만 한다.
    /// 그래픽을 진짜 그림으로 바꾼 뒤 배치만 다시 옮기는 흐름을 막지 않기 위해서다
    /// </summary>
    public static class UiLayoutImporter
    {
        /****************************************
        *                Types
        ****************************************/

        public sealed class Options
        {
            public bool           AddPlaceholderGraphics = true;
            public bool           AddNotes               = true;
            public EPreviewFilter Preview                = EPreviewFilter.All;
        }

        /// <summary>조건부 요소를 어느 상태 기준으로 켜 둘지</summary>
        public enum EPreviewFilter
        {
            All   = 0,
            Field = 1,
            Boss  = 2,
        }

        public sealed class Report
        {
            public GameObject   Root;
            public int          Created;
            public int          Updated;
            public int          SkippedReference;
            public List<string> Untouched = new List<string>();

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                sb.Append($"새로 만듦 {Created}, 갱신 {Updated}, 참고용 건너뜀 {SkippedReference}");
                if (Untouched.Count > 0)
                {
                    sb.Append($"\nJSON에 없어 그대로 둔 오브젝트 {Untouched.Count}개: {string.Join(", ", Untouched)}");
                }
                return sb.ToString();
            }
        }

        /// <summary>JSON 머리 정보. 창에서 적용 전에 보여 준다</summary>
        public sealed class Summary
        {
            public string LayoutName;
            public string LayoutDescription;
            public int    NodeCount;
            public int    ReferenceCount;
        }

        /****************************************
        *                Fields
        ****************************************/

        // 웹 배치 편집기가 쓰는 형식 이름이다. 편집기와 같이 바꾸지 않으면 적용이 거부된다
        public const string FORMAT_NAME    = "gemeum-ui-layout";
        public const int    FORMAT_VERSION = 2;
        public const string ROOT_PREFIX    = "Layout_";
        public const string ROOT_NOTE_ID   = "__layout_root__";

        public const float REFERENCE_WIDTH  = 1920f;
        public const float REFERENCE_HEIGHT = 1080f;

        private const string UNDO_NAME          = "UI 배치 JSON 적용";
        private const string CIRCLE_SPRITE_PATH = "UI/Skin/Knob.psd";
        private const string UI_LAYER_NAME      = "UI";
        private const string TYPE_GROUP         = "group";
        private const string TYPE_CIRCLE        = "circle";
        private const int    VECTOR_COMPONENTS  = 2;

        // 자식 노드의 기본 앵커와 피벗. JSON 규칙상 부모의 왼쪽 위다
        private static readonly Vector2 G_TopLeft = new Vector2(0f, 1f);
        private static readonly Vector2 G_Center  = new Vector2(0.5f, 0.5f);

        // 자리표시 그래픽. 실제 그림이 들어오기 전까지 영역만 보이게 옅게 둔다
        private static readonly Color G_PlaceholderColor = new Color(1f, 1f, 1f, 0.12f);

        /****************************************
        *                Methods
        ****************************************/

        public static Summary Inspect(string json)
        {
            JObject doc = Parse(json);
            Summary summary = new Summary
            {
                LayoutName = (string)doc["layoutName"] ?? string.Empty,
                LayoutDescription = (string)doc["layoutDescription"] ?? string.Empty,
            };
            foreach (JToken node in (JArray)doc["root"])
            {
                CountNodes(node, summary);
            }
            return summary;
        }

        /// <summary>parent 아래에 Layout_{이름} 루트를 두고 JSON 계층을 맞춘다. parent 크기가 기준 화면 역할을 한다</summary>
        public static Report Apply(string json, RectTransform parent, Options options)
        {
            if (parent == null)
            {
                throw new System.ArgumentNullException(nameof(parent), "UI를 넣을 RectTransform이 필요합니다");
            }
            options ??= new Options();
            JObject doc = Parse(json);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UNDO_NAME);

            Report report = new Report();
            string layoutName = SafeName((string)doc["layoutName"], "Layout");
            RectTransform root = EnsureRoot(parent, ROOT_PREFIX + layoutName, report);
            report.Root = root.gameObject;
            if (options.AddNotes)
            {
                UiLayoutNote rootNote = GetOrAddNote(root.gameObject);
                Undo.RecordObject(rootNote, UNDO_NAME);
                rootNote.Apply(ROOT_NOTE_ID, (string)doc["layoutDescription"], UiLayoutNote.ECondition.Always);
            }

            Dictionary<string, UiLayoutNote> existing = new Dictionary<string, UiLayoutNote>();
            foreach (UiLayoutNote note in root.GetComponentsInChildren<UiLayoutNote>(true))
            {
                if ((!string.IsNullOrEmpty(note.LayoutId)) && (note.LayoutId != ROOT_NOTE_ID) && (!existing.ContainsKey(note.LayoutId)))
                {
                    existing.Add(note.LayoutId, note);
                }
            }

            HashSet<string> visited = new HashSet<string>();
            int order = 0;
            foreach (JToken node in (JArray)doc["root"])
            {
                ApplyNode((JObject)node, root, ref order, existing, visited, options, report);
            }

            foreach (KeyValuePair<string, UiLayoutNote> pair in existing)
            {
                if ((!visited.Contains(pair.Key)) && (pair.Value != null))
                {
                    report.Untouched.Add(pair.Value.name);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            return report;
        }

        /// <summary>새 Canvas를 기준 해상도 1920x1080으로 만든다. 고를 RectTransform이 없을 때 쓴다</summary>
        public static RectTransform CreateCanvas(string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = LayerMask.NameToLayer(UI_LAYER_NAME);
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
            // 화면비가 달라도 기준 영역 전체가 보이게 Expand로 둔다
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            Undo.RegisterCreatedObjectUndo(go, UNDO_NAME);
            return (RectTransform)go.transform;
        }

        private static JObject Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException("JSON이 비어 있습니다");
            }
            JObject doc;
            try
            {
                doc = JObject.Parse(json);
            }
            catch (JsonReaderException e)
            {
                throw new InvalidDataException($"JSON 문법 오류 - {e.Message}");
            }
            if (((string)doc["format"] != FORMAT_NAME) || ((int?)doc["version"] != FORMAT_VERSION))
            {
                throw new InvalidDataException($"배치 편집기 JSON이 아닙니다. format \"{FORMAT_NAME}\", version {FORMAT_VERSION}이어야 합니다");
            }
            if (!(doc["root"] is JArray))
            {
                throw new InvalidDataException("root 배열이 없습니다");
            }
            return doc;
        }

        private static void CountNodes(JToken node, Summary summary)
        {
            ++summary.NodeCount;
            if ((bool?)node["reference"] == true)
            {
                ++summary.ReferenceCount;
            }
            if (node["children"] is JArray children)
            {
                foreach (JToken child in children)
                {
                    CountNodes(child, summary);
                }
            }
        }

        private static RectTransform EnsureRoot(RectTransform parent, string rootName, Report report)
        {
            Transform found = parent.Find(rootName);
            RectTransform root = found as RectTransform;
            if (root == null)
            {
                root = CreateRect(rootName, parent);
                ++report.Created;
            }
            else
            {
                Undo.RecordObject(root, UNDO_NAME);
            }
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = G_Center;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            root.localScale = Vector3.one;
            root.localRotation = Quaternion.identity;
            return root;
        }

        private static void ApplyNode(JObject node, RectTransform parent, ref int order, Dictionary<string, UiLayoutNote> existing,
            HashSet<string> visited, Options options, Report report)
        {
            if ((bool?)node["reference"] == true)
            {
                ++report.SkippedReference;
                return;
            }

            string id = (string)node["id"] ?? string.Empty;
            string type = (string)node["type"] ?? string.Empty;
            string objectName = SafeName((string)node["name"], id);
            visited.Add(id);

            RectTransform rect = FindExisting(id, objectName, parent, existing);
            bool created = rect == null;
            if (created)
            {
                rect = CreateRect(objectName, parent);
                ++report.Created;
                if (options.AddPlaceholderGraphics && (type != TYPE_GROUP))
                {
                    AddPlaceholder(rect.gameObject, type == TYPE_CIRCLE);
                }
            }
            else
            {
                ++report.Updated;
                if (rect.parent != parent)
                {
                    Undo.SetTransformParent(rect, parent, false, UNDO_NAME);
                }
                if (rect.name != objectName)
                {
                    Undo.RecordObject(rect.gameObject, UNDO_NAME);
                    rect.name = objectName;
                }
                Undo.RecordObject(rect, UNDO_NAME);
            }

            rect.anchorMin = ReadVector(node["anchorMin"], G_TopLeft);
            rect.anchorMax = ReadVector(node["anchorMax"], G_TopLeft);
            rect.pivot = ReadVector(node["pivot"], G_TopLeft);
            rect.sizeDelta = ReadVector(node["sizeDelta"], Vector2.zero);
            rect.anchoredPosition = ReadVector(node["anchoredPosition"], Vector2.zero);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            if (rect.GetSiblingIndex() != order)
            {
                Undo.SetSiblingIndex(rect, order, UNDO_NAME);
            }
            ++order;

            UiLayoutNote.ECondition condition = ReadCondition((string)node["condition"]);
            if (options.AddNotes)
            {
                UiLayoutNote note = GetOrAddNote(rect.gameObject);
                Undo.RecordObject(note, UNDO_NAME);
                note.Apply(id, (string)node["description"], condition);
            }
            ApplyPreview(rect.gameObject, condition, options.Preview);

            if ((type == TYPE_GROUP) && (node["children"] is JArray children))
            {
                int childOrder = 0;
                foreach (JToken child in children)
                {
                    ApplyNode((JObject)child, rect, ref childOrder, existing, visited, options, report);
                }
            }
        }

        // id가 같은 오브젝트가 먼저다. 설명 컴포넌트 없이 만든 경우만 같은 부모의 같은 이름으로 찾는다
        private static RectTransform FindExisting(string id, string objectName, RectTransform parent, Dictionary<string, UiLayoutNote> existing)
        {
            if (existing.TryGetValue(id, out UiLayoutNote note) && (note != null))
            {
                return (RectTransform)note.transform;
            }
            for (int i = 0; i < parent.childCount; ++i)
            {
                Transform child = parent.GetChild(i);
                if (child.name != objectName)
                {
                    continue;
                }
                UiLayoutNote other = child.GetComponent<UiLayoutNote>();
                if ((other == null) || string.IsNullOrEmpty(other.LayoutId))
                {
                    return child as RectTransform;
                }
            }
            return null;
        }

        private static RectTransform CreateRect(string objectName, RectTransform parent)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, UNDO_NAME);
            return rect;
        }

        private static void AddPlaceholder(GameObject go, bool isCircle)
        {
            Image image = go.AddComponent<Image>();
            image.color = G_PlaceholderColor;
            image.raycastTarget = false;
            if (isCircle)
            {
                image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(CIRCLE_SPRITE_PATH);
            }
        }

        private static UiLayoutNote GetOrAddNote(GameObject go)
        {
            UiLayoutNote note = go.GetComponent<UiLayoutNote>();
            return (note != null) ? note : Undo.AddComponent<UiLayoutNote>(go);
        }

        private static void ApplyPreview(GameObject go, UiLayoutNote.ECondition condition, EPreviewFilter preview)
        {
            if (condition == UiLayoutNote.ECondition.Always)
            {
                return;
            }
            bool active = (preview == EPreviewFilter.All)
                || ((preview == EPreviewFilter.Field) && (condition == UiLayoutNote.ECondition.Field))
                || ((preview == EPreviewFilter.Boss) && (condition == UiLayoutNote.ECondition.Boss));
            if (go.activeSelf != active)
            {
                Undo.RecordObject(go, UNDO_NAME);
                go.SetActive(active);
            }
        }

        private static UiLayoutNote.ECondition ReadCondition(string value)
        {
            switch (value)
            {
                case "field":
                    return UiLayoutNote.ECondition.Field;
                case "boss":
                    return UiLayoutNote.ECondition.Boss;
                default:
                    return UiLayoutNote.ECondition.Always;
            }
        }

        private static Vector2 ReadVector(JToken token, Vector2 fallback)
        {
            if ((token is JArray array) && (array.Count >= VECTOR_COMPONENTS))
            {
                return new Vector2((float)array[0], (float)array[1]);
            }
            return fallback;
        }

        // Transform.Find 경로가 깨지지 않게 슬래시를 바꾼다
        private static string SafeName(string value, string fallback)
        {
            string name = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            return string.IsNullOrEmpty(name) ? "Node" : name.Replace('/', '-');
        }
    }
}
#endif
