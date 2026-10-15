#if UNITY_EDITOR
// System
using System;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI.Editor
{
    /// <summary> 강화와 옵션 재설정 창의 색, 패널, 실행 버튼을 통일한다 </summary>
    public static class EquipmentGrowthStyle
    {
        /****************************************
        *                Fields
        ****************************************/

        public static readonly Color Canvas = Hex("#10151D");
        public static readonly Color Panel  = Hex("#202936");
        public static readonly Color Mint   = Hex("#76E6BD");
        public static readonly Color Soul   = Hex("#B99CFF");
        public static readonly Color Gold   = Hex("#E8BC68");
        public static readonly Color Text   = Hex("#EAF2F8");
        public static readonly Color Muted  = Hex("#A6BBCB");
        public static readonly Color Border = Hex("#3D596C");

        private const float BORDER_WIDTH = 1f;
        private const float FADE_DURATION = 0.15f;

        /****************************************
        *                Methods
        ****************************************/

        public static void Surface(Image image, Color color)
        {
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = color;
            if (!image.TryGetComponent(out EquipmentGrowthSurface surface))
            {
                image.gameObject.AddComponent<EquipmentGrowthSurface>();
            }
        }

        public static void Primary(Button button)
        {
            if (button.targetGraphic is Image background)
            {
                Surface(background, Color.white);
            }

            // 기존 피드백이 Button의 상태 색을 덮어쓰지 않도록 이 버튼만 기본 전환을 쓴다.
            if (button.TryGetComponent(out UiButtonFeedback feedback))
            {
                UnityEngine.Object.DestroyImmediate(feedback);
            }

            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Mint;
            colors.highlightedColor = Color.Lerp(Mint, Color.white, 0.3f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = Color.Lerp(Mint, Canvas, 0.2f);
            colors.disabledColor = Hex("#536776");
            colors.fadeDuration = FADE_DURATION;
            button.colors = colors;
            foreach (TMP_Text label in button.GetComponentsInChildren<TMP_Text>(true))
            {
                label.color = Canvas;
            }
        }

        public static RectTransform Card(RectTransform parent, string name, float x, float y, float w, float h, Color color)
        {
            RectTransform root = Rect(parent, name, x, y, w, h);
            Image panel = root.gameObject.AddComponent<Image>();
            Surface(panel, color);
            panel.raycastTarget = false;
            return root;
        }

        public static Sprite EquipmentIcon(int slot)
        {
            string[] names = { "weapon", "head", "body", "hands", "feet", "accessory" };
            if ((slot < 0) || (slot >= names.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ItemIcon64Builder.OUTPUT_FOLDER
                + "/T_Item64_equip_" + names[slot] + ".png");
            if (sprite != null)
            {
                return sprite;
            }
            throw new InvalidOperationException("성장 UI 장비 아이콘이 없습니다: " + names[slot]);
        }

        public static void Finish(RectTransform root, Color accent)
        {
            foreach (Image panel in root.GetComponentsInChildren<Image>(true))
            {
                Rect bounds = panel.rectTransform.rect;
                if ((panel.sprite == null) && (panel.color.a > 0.5f) && (bounds.width >= 40f) && (bounds.height >= 24f))
                {
                    Surface(panel, panel.color);
                }
            }
            Image background = root.GetComponent<Image>();
            if (background == null)
            {
                Transform existing = root.Find("Background");
                if (existing != null)
                {
                    background = existing.GetComponent<Image>();
                }
            }
            if (background != null)
            {
                Surface(background, Canvas);
                background.GetComponent<EquipmentGrowthSurface>().SetWindowFrame();
            }
            float width = root.rect.width;
            float height = root.rect.height;
            for (int i = 0; i < 4; ++i)
            {
                float x = ((i % 2) == 0) ? 15f : width - 15f;
                float y = (i < 2) ? 15f : height - 15f;
                RectTransform diamond = Rect(root, "FrameDiamond" + i, x, y, 5f, 5f);
                diamond.pivot = new Vector2(0.5f, 0.5f);
                diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
                Image graphic = diamond.gameObject.AddComponent<Image>();
                graphic.color = Color.Lerp(accent, Color.white, 0.25f);
                graphic.raycastTarget = false;
            }
        }

        public static void Orbit(RectTransform parent, string name, float x, float y, float size, Color color)
        {
            RectTransform root = Rect(parent, name, x, y, size, size);
            EquipmentGrowthOrbit orbit = root.gameObject.AddComponent<EquipmentGrowthOrbit>();
            orbit.color = color;
            orbit.raycastTarget = false;
        }

        public static Image Icon(RectTransform parent, string name, string assetPath, float x, float y, float size, Color color)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite == null)
            {
                throw new InvalidOperationException("성장 UI 이미지가 없습니다: " + assetPath);
            }

            Image image = Rect(parent, name, x, y, size, size).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform Rect(RectTransform parent, string name, float x, float y, float w, float h)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            obj.layer = LayerMask.NameToLayer("UI");
            RectTransform rect = (RectTransform)obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.up;
            rect.anchorMax = Vector2.up;
            rect.pivot = Vector2.up;
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
            return rect;
        }

        private static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString(value, out Color color);
            return color;
        }
    }
}
#endif
