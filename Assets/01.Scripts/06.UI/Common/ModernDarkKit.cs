// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 장면에 놓인 창을 공통 어두운 스타일로 한 번 다시 배치하는 도구. 기존 위젯은 직렬화 참조 그대로 새 자리로 옮겨 처리기와 연결을 유지한다 </summary>
    public sealed class ModernDarkKit
    {
        // 장면과 프리팹 파일을 고치지 않고 창이 처음 깨어날 때 배치를 바꾼다.
        // 창마다 TryBegin이 표식 오브젝트를 남겨 한 번만 적용되고, 열고 닫아도 장식이 겹쳐 쌓이지 않는다.
        // 좌표는 창 왼쪽 위가 원점이고 아래로 갈수록 y가 커진다. 장식은 모두 raycastTarget이 꺼져 있다

        /****************************************
        *                Fields
        ****************************************/

        public const string APPLIED_NAME = UiModernDark.DECOR_PREFIX + "Applied";

        private const float CORNER_SIZE     = 24f;
        private const float CORNER_INSET    = 4f;
        private const float TITLE_HEIGHT    = 56f;
        private const int   REFERENCE_DEPTH = 4;

        private static readonly Color DIVIDER_COLOR = new Color(UiModernDark.GoldDim.r, UiModernDark.GoldDim.g, UiModernDark.GoldDim.b, 0.5f);

        private readonly RectTransform      _window;
        private readonly Component          _owner;
        private readonly TMP_FontAsset      _font;
        private readonly HashSet<Transform> _adopted = new HashSet<Transform>();
        private readonly HashSet<Transform> _groups  = new HashSet<Transform>();
        private HashSet<Transform>          _stateful = null;
        private int                         _decorCount = 0;

        /****************************************
        *              Properties
        ****************************************/

        public RectTransform Window => _window;
        public TMP_FontAsset Font   => _font;

        /****************************************
        *                Methods
        ****************************************/

        private ModernDarkKit(Component owner, RectTransform window, TMP_FontAsset font)
        {
            _owner  = owner;
            _window = window;
            _font   = font;
        }

        /// <summary> 이 창에 처음 적용할 때만 참이다. 표식을 남기므로 같은 창에 두 번째 호출은 거짓이다 </summary>
        public static bool TryBegin(Component owner, RectTransform window, TMP_Text fontSource, out ModernDarkKit kit)
        {
            kit = null;
            if ((owner == null) || (window == null) || (window.Find(APPLIED_NAME) != null))
            {
                return false;
            }

            var mark = new GameObject(APPLIED_NAME, typeof(RectTransform));
            mark.layer = window.gameObject.layer;
            mark.transform.SetParent(window, false);
            kit = new ModernDarkKit(owner, window, (fontSource != null) ? fontSource.font : null);
            return true;
        }

        /// <summary> 직렬화 필드 하나를 이름으로 읽는다. 없거나 형식이 다르면 null이다 </summary>
        public static T Field<T>(object owner, string name) where T : class
        {
            if (owner == null)
            {
                return null;
            }

            for (Type type = owner.GetType(); (type != null) && (type != typeof(MonoBehaviour)) && (type != typeof(object)); type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field.GetValue(owner) as T;
                }
            }

            return null;
        }

        /// <summary> 창 크기를 정하고 패널 바탕, 네 모서리, 제목 줄을 만든다 </summary>
        public void Frame(Vector2 size, string title)
        {
            _window.anchorMin        = new Vector2(0.5f, 0.5f);
            _window.anchorMax        = new Vector2(0.5f, 0.5f);
            _window.pivot            = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = Vector2.zero;
            _window.sizeDelta        = size;
            _window.localScale       = Vector3.one;

            foreach (LayoutGroup group in _window.GetComponents<LayoutGroup>())
            {
                group.enabled = false;
            }

            foreach (ContentSizeFitter fitter in _window.GetComponents<ContentSizeFitter>())
            {
                fitter.enabled = false;
            }

            Image image = _window.GetComponent<Image>();
            if (image == null)
            {
                image = _window.gameObject.AddComponent<Image>();
            }

            SetSliced(image, UiModernDark.SPRITE_PANEL, Color.white);
            image.raycastTarget = true;

            AddCorner("TL", new Vector2(0f, 1f), new Vector2(1f, 1f));
            AddCorner("TR", new Vector2(1f, 1f), new Vector2(-1f, 1f));
            AddCorner("BL", new Vector2(0f, 0f), new Vector2(1f, -1f));
            AddCorner("BR", new Vector2(1f, 0f), new Vector2(-1f, -1f));

            if (!string.IsNullOrEmpty(title))
            {
                TMP_Text label = Label("Title", title, 28f, UiModernDark.TextGold, TextAlignmentOptions.Center, true, 0f, 0f, size.x, TITLE_HEIGHT);
                float half = (label.preferredWidth * 0.5f) + 28f;
                AddDiamond("TitleL", (size.x * 0.5f) - half, TITLE_HEIGHT * 0.5f);
                AddDiamond("TitleR", (size.x * 0.5f) + half, TITLE_HEIGHT * 0.5f);
                Line("TitleLine", 24f, TITLE_HEIGHT + 4f, size.x - 48f);
            }
        }

        /// <summary> 위젯을 창 직속으로 옮기고 왼쪽 위 기준 좌표로 놓는다. 버튼 안의 글자처럼 이미 옮긴 위젯 안쪽이면 건드리지 않는다 </summary>
        public void Place(Component widget, float x, float y, float width, float height)
        {
            if (widget != null)
            {
                PlaceTransform(widget.transform, x, y, width, height, widget is TMP_Text);
            }
        }

        public void Place(GameObject widget, float x, float y, float width, float height)
        {
            if (widget != null)
            {
                PlaceTransform(widget.transform, x, y, width, height, false);
            }
        }

        /// <summary> 보이기 전환 대상 묶음을 통째로 창 전체 크기로 맞춘다. 묶음 안 위젯은 부모를 바꾸지 않고 창 좌표로 놓인다 </summary>
        public void PlaceGroup(GameObject group, bool quietOwnGraphic = true)
        {
            PlaceGroup(group, 0f, 0f, _window.rect.width, _window.rect.height, quietOwnGraphic);
        }

        /// <summary> 묶음을 지정한 상자로 맞춘다. 안쪽 위젯은 부모를 유지한 채 창 좌표로 놓이므로 묶음 켜기·끄기가 그대로 듣는다 </summary>
        public void PlaceGroup(GameObject group, float x, float y, float width, float height, bool quietOwnGraphic = true)
        {
            if (group == null)
            {
                return;
            }

            Transform target = group.transform;
            if (!HasStatefulAncestor(target) && (target.parent != _window))
            {
                target.SetParent(_window, false);
            }

            _groups.Add(target);
            if (target is RectTransform rect)
            {
                PlaceInPlace(rect, x, y, width, height);
            }

            DisableLayout(group.transform);
            if (quietOwnGraphic)
            {
                Quiet(group, false);
            }
        }

        /// <summary> 그림만 끄고 자식 위젯의 그림은 두는 쪽 </summary>
        public static void Quiet(GameObject group, bool includeChildren)
        {
            if (group == null)
            {
                return;
            }

            if (includeChildren)
            {
                Quiet(group);
                return;
            }

            Graphic own = group.GetComponent<Graphic>();
            if (own != null)
            {
                own.enabled = false;
            }
        }

        /// <summary> 뒤쪽에 깔리는 어두운 상자 장식. 위젯보다 항상 아래에 그려진다 </summary>
        public RectTransform Inset(string name, float x, float y, float width, float height, string spriteName = null)
        {
            GameObject go = NewImage(UiModernDark.DECOR_PREFIX + name, spriteName ?? UiModernDark.SPRITE_INSET, Color.white);
            go.transform.SetSiblingIndex(Mathf.Min(_decorCount++, _window.childCount - 1));
            var rect = (RectTransform)go.transform;
            PlaceRect(rect, x, y, width, height);
            return rect;
        }

        /// <summary> 묶음 안쪽 맨 뒤에 깔리는 어두운 상자. 묶음이 꺼지면 함께 사라진다 </summary>
        public RectTransform InsetIn(GameObject group, string name, float x, float y, float width, float height, string spriteName = null)
        {
            var go = new GameObject(UiModernDark.DECOR_PREFIX + name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = _window.gameObject.layer;
            go.transform.SetParent(group.transform, false);
            go.transform.SetAsFirstSibling();
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            SetSliced(image, spriteName ?? UiModernDark.SPRITE_INSET, Color.white);
            var rect = (RectTransform)go.transform;
            PlaceRect(rect, x, y, width, height);
            return rect;
        }

        public TMP_Text Label(string name, string text, float size, Color color, TextAlignmentOptions alignment, bool bold,
            float x, float y, float width, float height)
        {
            var go = new GameObject(UiModernDark.DECOR_PREFIX + name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = _window.gameObject.layer;
            go.transform.SetParent(_window, false);
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            if (_font != null)
            {
                label.font = _font;
            }

            label.text             = text;
            label.fontSize         = size;
            label.color            = color;
            label.alignment        = alignment;
            label.fontStyle        = bold ? FontStyles.Bold : FontStyles.Normal;
            label.raycastTarget    = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode     = TextOverflowModes.Ellipsis;
            PlaceRect((RectTransform)go.transform, x, y, width, height);
            return label;
        }

        public void Line(string name, float x, float y, float width)
        {
            GameObject go = NewImage(UiModernDark.DECOR_PREFIX + name, null, DIVIDER_COLOR);
            PlaceRect((RectTransform)go.transform, x, y, width, 1f);
        }

        /// <summary> 단추 바탕을 공통 스프라이트로 바꾸고 글자색을 맞춘다. 누름 색조는 흰색 기준이라 스프라이트 색이 그대로 보인다 </summary>
        public static void StyleButton(Button button, bool primary)
        {
            if (button == null)
            {
                return;
            }

            if (button.targetGraphic is Image image)
            {
                SetSliced(image, primary ? UiModernDark.SPRITE_BUTTON_PRIMARY : UiModernDark.SPRITE_BUTTON, Color.white);
            }

            ColorBlock colors = button.colors;
            colors.normalColor      = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor     = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor    = Color.white;
            colors.disabledColor    = UiModernDark.Disabled;
            button.colors           = colors;

            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.color = UiModernDark.TextMain;
            }
        }

        /// <summary> 이미 옮긴 단추나 칸 안쪽 위젯을 부모 기준 좌표로 직접 놓는다. Place와 달리 옮긴 위젯 안쪽이어도 건너뛰지 않고 부모도 바꾸지 않는다 </summary>
        public static void PlaceChild(Component widget, float x, float y, float width, float height)
        {
            if ((widget != null) && (widget.transform is RectTransform rect))
            {
                PlaceRect(rect, x, y, width, height);
            }
        }

        /// <summary> 닫기 단추 바탕을 맞추고, 아이콘만 있던 단추에는 × 글자를 얹는다. 이미 글자가 있으면 그대로 둔다 </summary>
        public void StyleClose(Button close)
        {
            if (close == null)
            {
                return;
            }

            StyleButton(close, false);
            TMP_Text existing = close.GetComponentInChildren<TMP_Text>(true);
            if ((existing != null) && !string.IsNullOrEmpty(existing.text))
            {
                return;
            }

            Transform glyph = close.transform.Find(UiModernDark.DECOR_PREFIX + "Glyph");
            if (glyph == null)
            {
                var go = new GameObject(UiModernDark.DECOR_PREFIX + "Glyph", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                go.layer = close.gameObject.layer;
                go.transform.SetParent(close.transform, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                var text = go.GetComponent<TextMeshProUGUI>();
                if (_font != null)
                {
                    text.font = _font;
                }

                text.text             = "×";
                text.fontSize         = 30f;
                text.color            = UiModernDark.TextMain;
                text.alignment        = TextAlignmentOptions.Center;
                text.raycastTarget    = false;
                text.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }

        /// <summary> 위젯을 모두 옮겨 간 묶음 오브젝트의 옛 바탕과 장식 그림을 끈다. 오브젝트 자체는 켜 둬서 보이기 전환 호출이 그대로 듣는다 </summary>
        public static void Quiet(GameObject group)
        {
            if (group == null)
            {
                return;
            }

            foreach (Graphic graphic in group.GetComponentsInChildren<Graphic>(true))
            {
                // 글자와 단추 바탕은 그대로 둔다. 옛 바탕·테두리 그림만 끈다
                if ((graphic is TMP_Text) || (graphic.GetComponentInParent<Selectable>(true) != null) || (graphic.GetComponent<TMP_InputField>() != null)
                    || (graphic.GetComponentInParent<TMP_InputField>(true) != null))
                {
                    continue;
                }

                graphic.enabled = false;
            }
        }

        /// <summary> 옛 목록·가로세로 배치와 크기 맞춤을 꺼 직접 지정한 좌표가 유지되게 한다 </summary>
        public static void DisableLayout(Component target)
        {
            if (target == null)
            {
                return;
            }

            foreach (LayoutGroup group in target.GetComponents<LayoutGroup>())
            {
                group.enabled = false;
            }

            foreach (ContentSizeFitter fitter in target.GetComponents<ContentSizeFitter>())
            {
                fitter.enabled = false;
            }
        }

        public static void StyleInset(Graphic graphic, string spriteName = null)
        {
            if (graphic is Image image)
            {
                SetSliced(image, spriteName ?? UiModernDark.SPRITE_INSET, Color.white);
            }
        }

        public static void StyleText(TMP_Text text, float size, Color color)
        {
            if (text == null)
            {
                return;
            }

            if (size > 0f)
            {
                text.fontSize = size;
            }

            text.color = color;
        }

        /// <summary> 직렬화 참조에 쓰이지 않은 옛 창 직속 가지를 끈다. 위젯을 품은 가지, 옮긴 위젯, 장식은 남긴다 </summary>
        public void HideLegacy(params object[] extraOwners)
        {
            var keep = new HashSet<Transform>();
            CollectReferences(_owner, keep);
            if (extraOwners != null)
            {
                foreach (object extra in extraOwners)
                {
                    CollectReferences(extra, keep);
                }
            }

            CollectBinderReferences(keep);

            for (int i = _window.childCount - 1; i >= 0; --i)
            {
                Transform branch = _window.GetChild(i);
                if (branch.name.StartsWith(UiModernDark.DECOR_PREFIX, StringComparison.Ordinal) || _adopted.Contains(branch) || _groups.Contains(branch))
                {
                    continue;
                }

                if (!ContainsAny(branch, keep))
                {
                    branch.gameObject.SetActive(false);
                }
            }
        }

        /// <summary> 가지 안에 보존할 참조가 하나라도 있는지 본다 </summary>
        public static bool ContainsAny(Transform branch, HashSet<Transform> keep)
        {
            foreach (Transform kept in keep)
            {
                if ((kept != null) && ((kept == branch) || (kept.IsChildOf(branch))))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary> 소유자가 직렬화로 들고 있는 모든 유니티 오브젝트의 Transform을 모은다. 배열과 중첩 구조는 얕게 따라간다 </summary>
        public static void CollectReferences(object owner, HashSet<Transform> into)
        {
            Collect(owner, into, 0);
        }

        private void CollectBinderReferences(HashSet<Transform> into)
        {
            Canvas canvas = _window.GetComponentInParent<Canvas>(true);
            if (canvas == null)
            {
                return;
            }

            var found = new HashSet<Transform>();
            foreach (MonoBehaviour behaviour in canvas.rootCanvas.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if ((behaviour == null) || (!behaviour.GetType().Name.EndsWith("Binder", StringComparison.Ordinal)))
                {
                    continue;
                }

                found.Clear();
                Collect(behaviour, found, 0);
                if (found.Contains(_owner.transform))
                {
                    into.UnionWith(found);
                }
            }
        }

        private static void Collect(object owner, HashSet<Transform> into, int depth)
        {
            if ((owner == null) || (depth > REFERENCE_DEPTH))
            {
                return;
            }

            for (Type type = owner.GetType(); (type != null) && (type != typeof(MonoBehaviour)) && (type != typeof(object)) && (type != typeof(ValueType)); type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    bool serialized = field.IsPublic || (field.GetCustomAttribute<SerializeField>() != null);
                    if (!serialized)
                    {
                        continue;
                    }

                    CollectValue(field.GetValue(owner), field.FieldType, into, depth);
                }
            }
        }

        // Color · Vector · Rect 같은 엔진 값 형식은 참조를 품지 않는다. 사용자 [Serializable] 구조체는 따라간다
        private static bool IsEngineValueType(Type type)
        {
            return type.IsValueType && (type.Namespace != null) && type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal);
        }

        private static void CollectValue(object value, Type type, HashSet<Transform> into, int depth)
        {
            if (value == null)
            {
                return;
            }

            if (value is Component component)
            {
                if (component != null)
                {
                    into.Add(component.transform);
                }

                return;
            }

            if (value is GameObject gameObject)
            {
                if (gameObject != null)
                {
                    into.Add(gameObject.transform);
                }

                return;
            }

            if (value is UnityEngine.Object || type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || IsEngineValueType(type))
            {
                return;
            }

            if (value is IEnumerable list)
            {
                foreach (object item in list)
                {
                    if (item != null)
                    {
                        CollectValue(item, item.GetType(), into, depth + 1);
                    }
                }

                return;
            }

            if (type.IsSerializable)
            {
                Collect(value, into, depth + 1);
            }
        }

        private void PlaceTransform(Transform target, float x, float y, float width, float height, bool isText)
        {
            if (target == null)
            {
                return;
            }

            // 이미 옮긴 위젯의 안쪽이거나, 단추 안의 글자이면 바깥 위젯이 자리를 정한다
            foreach (Transform adopted in _adopted)
            {
                if ((adopted != null) && (target != adopted) && (target.IsChildOf(adopted)))
                {
                    return;
                }
            }

            if (isText && IsInsideSelectable(target))
            {
                return;
            }

            if (!(target is RectTransform rect))
            {
                return;
            }

            _adopted.Add(target);

            // 보이기 전환 묶음, 알파·상호작용 묶음, 마스크 안의 위젯은 부모를 유지해 그 계약을 지키고 창 좌표만 환산한다.
            // 그런 조상이 없을 때만 창 직속으로 옮긴다
            if (HasStatefulAncestor(target))
            {
                PlaceInPlace(rect, x, y, width, height);
                return;
            }

            if (target.parent != _window)
            {
                target.SetParent(_window, false);
            }

            PlaceRect(rect, x, y, width, height);
        }

        private bool HasStatefulAncestor(Transform target)
        {
            if (_stateful == null)
            {
                _stateful = new HashSet<Transform>();
                CollectReferences(_owner, _stateful);
                CollectBinderReferences(_stateful);
            }

            for (Transform parent = target.parent; (parent != null) && (parent != _window); parent = parent.parent)
            {
                if (_stateful.Contains(parent) || _groups.Contains(parent)
                    || (parent.GetComponent<CanvasGroup>() != null)
                    || (parent.GetComponent<Mask>() != null)
                    || (parent.GetComponent<RectMask2D>() != null)
                    || (parent.GetComponent<ScrollRect>() != null))
                {
                    return true;
                }
            }

            return false;
        }

        // 부모를 바꾸지 않고 창 왼쪽 위 기준 좌표가 되도록 부모 기준 위치와 크기로 환산해 놓는다. 부모의 자동 배치는 좌표를 덮으므로 끈다
        private void PlaceInPlace(RectTransform rect, float x, float y, float width, float height)
        {
            var parent = rect.parent as RectTransform;
            if (parent == null)
            {
                PlaceRect(rect, x, y, width, height);
                return;
            }

            for (Transform up = parent; (up != null) && (up != _window); up = up.parent)
            {
                DisableLayout(up);
            }

            Vector3 topLeft = _window.TransformPoint(new Vector3(_window.rect.xMin + x, _window.rect.yMax - y, 0f));
            Vector3 local   = parent.InverseTransformPoint(topLeft);
            Vector3 ws      = _window.lossyScale;
            Vector3 ps      = parent.lossyScale;
            float scaleX    = (Mathf.Abs(ps.x) > 0.0001f) ? ws.x / ps.x : 1f;
            float scaleY    = (Mathf.Abs(ps.y) > 0.0001f) ? ws.y / ps.y : 1f;

            rect.anchorMin        = new Vector2(0f, 1f);
            rect.anchorMax        = new Vector2(0f, 1f);
            rect.pivot            = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(local.x - parent.rect.xMin, local.y - parent.rect.yMax);
            rect.sizeDelta        = new Vector2(width * scaleX, height * scaleY);
            rect.localScale       = Vector3.one;
        }

        private static bool IsInsideSelectable(Transform target)
        {
            Selectable selectable = target.GetComponentInParent<Selectable>(true);
            return (selectable != null) && (selectable.transform != target);
        }

        private GameObject NewImage(string name, string spriteName, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = _window.gameObject.layer;
            go.transform.SetParent(_window, false);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            if (spriteName != null)
            {
                SetSliced(image, spriteName, color);
            }
            else
            {
                image.color = color;
            }

            return go;
        }

        public static void SetSliced(Image image, string spriteName, Color color)
        {
            image.sprite = UiModernDarkSprites.Get(spriteName);
            image.type   = Image.Type.Sliced;
            image.color  = color;
        }

        public static void PlaceRect(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin        = new Vector2(0f, 1f);
            rect.anchorMax        = new Vector2(0f, 1f);
            rect.pivot            = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta        = new Vector2(width, height);
            rect.localScale       = Vector3.one;
        }

        private void AddCorner(string name, Vector2 anchor, Vector2 scale)
        {
            GameObject go = NewImage(UiModernDark.DECOR_PREFIX + "Corner" + name, null, Color.white);
            Image image = go.GetComponent<Image>();
            image.sprite = UiModernDarkSprites.Get(UiModernDark.SPRITE_CORNER);
            image.type   = Image.Type.Simple;
            var rect = (RectTransform)go.transform;
            rect.anchorMin        = anchor;
            rect.anchorMax        = anchor;
            rect.pivot            = new Vector2(0.5f, 0.5f);
            rect.sizeDelta        = new Vector2(CORNER_SIZE, CORNER_SIZE);
            float center = CORNER_INSET + (CORNER_SIZE * 0.5f);
            rect.anchoredPosition = new Vector2((anchor.x > 0.5f) ? -center : center, (anchor.y > 0.5f) ? -center : center);
            rect.localScale       = new Vector3(scale.x, scale.y, 1f);
        }

        private void AddDiamond(string name, float x, float y)
        {
            GameObject go = NewImage(UiModernDark.DECOR_PREFIX + "Diamond" + name, null, Color.white);
            Image image = go.GetComponent<Image>();
            image.sprite = UiModernDarkSprites.Get(UiModernDark.SPRITE_DIAMOND);
            image.type   = Image.Type.Simple;
            image.SetNativeSize();
            var rect = (RectTransform)go.transform;
            rect.anchorMin        = new Vector2(0f, 1f);
            rect.anchorMax        = new Vector2(0f, 1f);
            rect.pivot            = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, -y);
        }
    }
}
