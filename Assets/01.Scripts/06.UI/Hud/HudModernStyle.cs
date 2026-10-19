// Unity
using UnityEngine;
using UnityEngine.UI;

namespace Shinshi.UI
{
    /// <summary> Shared dark backdrop helper for HUD views, never moves existing widgets and never blocks raycasts </summary>
    public static class HudModernStyle
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string BACKDROP_NAME = UiModernDark.DECOR_PREFIX + "HudBackdrop";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> Adds one stretched backdrop behind the target's children, true only on the first call for that target </summary>
        public static bool AddBackdrop(Transform target, string spriteName, Color tint, float padding)
        {
            if ((target == null) || (target.Find(BACKDROP_NAME) != null))
            {
                return false;
            }

            var go = new GameObject(BACKDROP_NAME, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = target.gameObject.layer;
            go.transform.SetParent(target, false);
            go.transform.SetAsFirstSibling();

            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-padding, -padding);
            rect.offsetMax = new Vector2(padding, padding);

            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            ModernDarkKit.SetSliced(image, spriteName, tint);
            return true;
        }
    }
}
