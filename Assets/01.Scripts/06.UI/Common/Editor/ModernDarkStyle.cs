#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI.Editor
{
    /// <summary> 기존 창 프리팹의 Image, Button, 글자에 공통 어두운 스타일을 제자리에서 입히는 도우미 </summary>
    public static class ModernDarkStyle
    {
        // 프리팹의 GUID와 연결 필드를 지키려고 새로 만들지 않고 LoadPrefabContents로 열어 값만 바꾼다.
        // 장식 오브젝트는 이름이 MD_로 시작하고, 다시 적용할 때 먼저 지운 뒤 새로 만든다

        /****************************************
        *                Fields
        ****************************************/

        public const float CORNER_SIZE      = 24f;
        public const float DIAMOND_SIZE     = 12f;
        public const float HIGHLIGHT_FACTOR = 1.2f;
        public const float PRESSED_FACTOR   = 0.8f;
        public const float FADE_DURATION    = 0.1f;
        public const float GLOW_HEIGHT_RATIO = 6f;

        private const string UI_LAYER = "UI";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 프리팹을 열어 edit로 고치고 같은 경로에 저장한다. 오류가 나도 열어 둔 내용은 반드시 닫는다 </summary>
        public static void EditPrefab(string path, Action<RectTransform> edit)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                edit((RectTransform)contents.transform);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        public static Transform FindByName(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        public static List<Transform> FindAllByPrefix(Transform root, string prefix)
        {
            var found = new List<Transform>();
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if ((child != root) && (child.name.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    found.Add(child);
                }
            }

            return found;
        }

        public static Image FindImage(Transform root, string name)
        {
            Transform found = FindByName(root, name);
            return (found == null) ? null : found.GetComponent<Image>();
        }

        public static TMP_Text FindText(Transform root, string name)
        {
            Transform found = FindByName(root, name);
            return (found == null) ? null : found.GetComponent<TMP_Text>();
        }

        /// <summary> 예전 Equipment 면 효과와 모서리 마름모는 새 스프라이트와 겹치므로 지운다 </summary>
        public static void StripLegacySurface(RectTransform root)
        {
            foreach (EquipmentGrowthSurface surface in root.GetComponentsInChildren<EquipmentGrowthSurface>(true))
            {
                UnityEngine.Object.DestroyImmediate(surface);
            }

            foreach (Transform child in FindAllByPrefix(root, "FrameDiamond"))
            {
                if (child != null)
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        public static void ClearDecor(RectTransform root)
        {
            foreach (Transform child in FindAllByPrefix(root, UiModernDark.DECOR_PREFIX))
            {
                if (child != null)
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        public static void ApplySprite(Image image, string spriteName, Color tint)
        {
            image.sprite = ModernDarkTextures.LoadSprite(spriteName);
            image.type   = Image.Type.Sliced;
            image.color  = tint;
        }

        public static void ApplyPanel(Image image)
        {
            ApplySprite(image, UiModernDark.SPRITE_PANEL, Color.white);
        }

        public static void ApplyInset(Image image)
        {
            ApplySprite(image, UiModernDark.SPRITE_INSET, Color.white);
        }

        public static void ApplyButton(Button button, bool primary)
        {
            if (button.targetGraphic is Image background)
            {
                ApplySprite(background, primary ? UiModernDark.SPRITE_BUTTON_PRIMARY : UiModernDark.SPRITE_BUTTON, Color.white);
            }

            ApplyStateColors(button);
            foreach (TMP_Text label in button.GetComponentsInChildren<TMP_Text>(true))
            {
                label.color = primary ? UiModernDark.Cyan : UiModernDark.TextMain;
            }
        }

        public static void ApplyTab(Button button)
        {
            if (button.targetGraphic is Image background)
            {
                ApplySprite(background, UiModernDark.SPRITE_TAB, Color.white);
            }

            ApplyStateColors(button);
        }

        public static void ApplyStateColors(Selectable selectable)
        {
            selectable.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = selectable.colors;
            colors.normalColor      = Color.white;
            colors.highlightedColor = new Color(HIGHLIGHT_FACTOR, HIGHLIGHT_FACTOR, HIGHLIGHT_FACTOR, 1f);
            colors.selectedColor    = colors.highlightedColor;
            colors.pressedColor     = new Color(PRESSED_FACTOR, PRESSED_FACTOR, PRESSED_FACTOR, 1f);
            colors.disabledColor    = UiModernDark.Disabled;
            colors.colorMultiplier  = 1f;
            colors.fadeDuration     = FADE_DURATION;
            selectable.colors = colors;
        }

        /// <summary> 선택된 탭 표시 막대를 금색으로 칠하고 위로 번지는 광택을 붙인다 </summary>
        public static void ApplyActiveMark(Image mark)
        {
            mark.sprite        = null;
            mark.type          = Image.Type.Simple;
            mark.color         = UiModernDark.Gold;
            mark.raycastTarget = false;

            RectTransform rect = mark.rectTransform;
            Image glow = AddDecorImage(rect, UiModernDark.DECOR_PREFIX + "Glow", UiModernDark.SPRITE_GLOW, Image.Type.Simple);
            glow.rectTransform.anchorMin        = new Vector2(0f, 1f);
            glow.rectTransform.anchorMax        = new Vector2(1f, 1f);
            glow.rectTransform.pivot            = new Vector2(0.5f, 0f);
            glow.rectTransform.anchoredPosition = Vector2.zero;
            glow.rectTransform.sizeDelta        = new Vector2(0f, rect.rect.height * GLOW_HEIGHT_RATIO);
        }

        /// <summary> 창 네 모서리에 금색 모서리 장식을 둔다. 배치 그룹이 있는 루트에서도 자리를 차지하지 않는다 </summary>
        public static void AddCornerDecor(RectTransform root, float inset)
        {
            for (int i = 0; i < 4; ++i)
            {
                bool right  = (i % 2) == 1;
                bool bottom = i >= 2;
                Image corner = AddDecorImage(root, UiModernDark.DECOR_PREFIX + "Corner" + i, UiModernDark.SPRITE_CORNER, Image.Type.Simple);
                RectTransform rect = corner.rectTransform;
                var anchor = new Vector2(right ? 1f : 0f, bottom ? 0f : 1f);
                rect.anchorMin        = anchor;
                rect.anchorMax        = anchor;
                rect.pivot            = new Vector2(0.5f, 0.5f);
                rect.sizeDelta        = new Vector2(CORNER_SIZE, CORNER_SIZE);
                float center = inset + (CORNER_SIZE * 0.5f);
                rect.anchoredPosition = new Vector2(right ? -center : center, bottom ? center : -center);
                rect.localScale       = new Vector3(right ? -1f : 1f, bottom ? -1f : 1f, 1f);
            }
        }

        /// <summary> 제목 글자 양옆에 금색 마름모를 둔다. 제목은 왼쪽 위 기준 좌표를 쓰는 창 빌더 배치를 따른다 </summary>
        public static void AddTitleDiamonds(RectTransform parent, RectTransform title, float gap)
        {
            float centerX = title.anchoredPosition.x + (title.sizeDelta.x * 0.5f);
            float centerY = title.anchoredPosition.y - (title.sizeDelta.y * 0.5f);
            for (int i = 0; i < 2; ++i)
            {
                bool right = i == 1;
                Image diamond = AddDecorImage(parent, UiModernDark.DECOR_PREFIX + "TitleDiamond" + i, UiModernDark.SPRITE_DIAMOND,
                    Image.Type.Simple);
                RectTransform rect = diamond.rectTransform;
                rect.anchorMin        = title.anchorMin;
                rect.anchorMax        = title.anchorMax;
                rect.pivot            = new Vector2(0.5f, 0.5f);
                rect.sizeDelta        = new Vector2(DIAMOND_SIZE, DIAMOND_SIZE);
                rect.anchoredPosition = new Vector2(centerX + (right ? gap : -gap), centerY);
            }
        }

        public static Image AddDecorImage(RectTransform parent, string name, string spriteName, Image.Type type)
        {
            var decor = new GameObject(name, typeof(RectTransform));
            decor.layer = LayerMask.NameToLayer(UI_LAYER);
            decor.transform.SetParent(parent, false);

            var layout = decor.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;

            var image = decor.AddComponent<Image>();
            image.sprite        = ModernDarkTextures.LoadSprite(spriteName);
            image.type          = type;
            image.color         = Color.white;
            image.raycastTarget = false;
            return image;
        }
    }
}
#endif
