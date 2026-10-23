// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> 상시 HUD 묶음의 그림 없는 자리 표시 칸을 숨기는 도구. HUD 뷰는 런타임에 배치나 모양을 바꾸지 않는다 </summary>
    public static class HudModernLayout
    {
        // 플레이 전 편집 화면과 플레이 화면이 같아야 하므로 HUD의 위치와 모양은 씬이 기준이다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 그림이 없는 상태 효과 자리 표시 칸(DebuffIcon, BuffIcon)의 Image만 끈다. 묶음과 그림이 있는 칸은 그대로 둔다 </summary>
        public static void HidePlaceholderIcons(Transform root)
        {
            HidePlaceholder(root, "StatusEffectGroup/DebuffGroup/DebuffIcon");
            HidePlaceholder(root, "StatusEffectGroup/BuffGroup/BuffIcon");
        }

        private static void HidePlaceholder(Transform root, string path)
        {
            Transform node = (root != null) ? root.Find(path) : null;
            var image = (node != null) ? node.GetComponent<UnityEngine.UI.Image>() : null;
            if ((image != null) && (image.sprite == null))
            {
                image.enabled = false;
            }
        }
    }
}
