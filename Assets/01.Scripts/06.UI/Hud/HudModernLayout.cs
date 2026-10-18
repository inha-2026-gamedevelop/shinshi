// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> 상시 HUD 묶음을 시안 위치로 옮긴다. 묶음 안 위젯과 바인더 연결은 그대로 두고 묶음의 앵커와 위치만 바꾼다 </summary>
    public static class HudModernLayout
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ROOT_NAME = "Layout_AlwaysVisible";

        private static readonly Vector2 TOP_LEFT     = new Vector2(0f, 1f);
        private static readonly Vector2 TOP_RIGHT    = new Vector2(1f, 1f);
        private static readonly Vector2 BOTTOM_LEFT  = new Vector2(0f, 0f);
        private static readonly Vector2 BOTTOM_RIGHT = new Vector2(1f, 0f);
        private static readonly Vector2 BOTTOM       = new Vector2(0.5f, 0f);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>뷰가 속한 캔버스의 상시 화면 묶음을 옮긴다. 여러 번 불러도 같다</summary>
        public static void Apply(Transform anyChild)
        {
            if (anyChild == null)
            {
                return;
            }

            Canvas canvas = anyChild.GetComponentInParent<Canvas>(true);
            Transform root = (canvas != null) ? canvas.rootCanvas.transform.Find(ROOT_NAME) : null;
            if (root == null)
            {
                return;
            }

            // 파티 왼쪽 위, 지도 오른쪽 위, 퀘스트는 지도 아래
            Place(root, "PartyGroup", TOP_LEFT, new Vector2(28f, -96f));
            Place(root, "MapInfoGroup", TOP_RIGHT, new Vector2(-24f, -24f));
            Place(root, "QuestGroup", TOP_RIGHT, new Vector2(-24f, -312f));

            // 채팅 왼쪽 아래, 메뉴 단추 오른쪽 아래, 체력과 기술 칸은 아래 가운데
            Place(root, "ChatGroup", BOTTOM_LEFT, new Vector2(28f, 32f));
            Place(root, "MenuGroup", BOTTOM_RIGHT, new Vector2(-32f, 32f));
            Place(root, "SkillGroup", BOTTOM, new Vector2(60f, 56f));
            Place(root, "PlayerStatusGroup", BOTTOM, new Vector2(60f, 164f));
            Place(root, "StatusEffectGroup", BOTTOM, new Vector2(60f, 236f));

            HidePlaceholderIcons(root);
        }

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

        private static void Place(Transform root, string childName, Vector2 anchor, Vector2 position)
        {
            var rect = root.Find(childName) as RectTransform;
            if (rect == null)
            {
                return;
            }

            rect.anchorMin        = anchor;
            rect.anchorMax        = anchor;
            rect.pivot            = anchor;
            rect.anchoredPosition = position;
        }
    }
}
