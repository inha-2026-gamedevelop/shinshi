#if UNITY_EDITOR
// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.UI.Editor
{
    /// <summary> 친구 창 프리팹을 공통 창 스킨과 글꼴로 만든다 </summary>
    public static class FriendsWindowBuilder
    {
        // 다시 실행하면 프리팹을 통째로 새로 쓰므로 프리팹을 손으로 고친 내용은 사라진다

        /****************************************
        *                Fields
        ****************************************/

        public const string PREFAB_PATH = "Assets/02.Prefab/UI/Windows/PF_FriendWindow.prefab";

        private const float WINDOW_WIDTH  = 760f;
        private const float WINDOW_HEIGHT = 640f;
        private const float INPUT_TOP     = 88f;
        private const float REQUEST_WIDTH = 180f;
        private const float TAB_TOP       = 148f;
        private const float TAB_WIDTH     = 160f;
        private const float TAB_GAP       = 168f;
        private const float LIST_TOP      = 204f;
        private const float FOOTER_TOP    = 592f;
        private const float PAGE_BUTTON   = 44f;
        private const float FOOTER_HEIGHT = 36f;

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/친구창 만들기")]
        public static void Build()
        {
            SocialWindowKit.SavePrefab("PF_FriendWindow", PREFAB_PATH, BuildWindow);
            Debug.Log("[UI] 친구창 프리팹을 만들었다: " + PREFAB_PATH);
        }

        private static void BuildWindow(RectTransform root)
        {
            float listWidth = WINDOW_WIDTH - (SocialWindowKit.MARGIN * 2f);
            Button close = SocialWindowKit.Frame(root, WINDOW_WIDTH, WINDOW_HEIGHT, "친구", out _, out TMP_Text count);
            var view = root.gameObject.AddComponent<FriendsView>();

            TMP_InputField input = SocialWindowKit.Input(root, "이름 입력", "캐릭터 이름", SocialWindowKit.MARGIN, INPUT_TOP,
                listWidth - REQUEST_WIDTH - 12f, SocialWindowKit.BUTTON_HEIGHT, SocialLimits.MAX_NAME_INPUT_LENGTH);
            Button request = SocialWindowKit.TextButton(root, "친구 요청 버튼", "친구 요청", WINDOW_WIDTH - SocialWindowKit.MARGIN - REQUEST_WIDTH,
                INPUT_TOP, REQUEST_WIDTH, SocialWindowKit.BUTTON_HEIGHT, true);

            var tabButtons  = new Object[FriendsViewRules.TAB_COUNT];
            var tabLabels   = new Object[FriendsViewRules.TAB_COUNT];
            var tabSelected = new Object[FriendsViewRules.TAB_COUNT];
            for (int i = 0; i < FriendsViewRules.TAB_COUNT; ++i)
            {
                tabButtons[i] = SocialWindowKit.Tab(root, FriendsViewRules.TabLabel(i, 0), SocialWindowKit.MARGIN + (TAB_GAP * i), TAB_TOP,
                    TAB_WIDTH, SocialWindowKit.BUTTON_HEIGHT, out TMP_Text label, out GameObject selected);
                tabLabels[i]   = label;
                tabSelected[i] = selected;
            }

            var rows = new Object[FriendsViewRules.ROWS_PER_PAGE];
            for (int i = 0; i < FriendsViewRules.ROWS_PER_PAGE; ++i)
            {
                rows[i] = SocialWindowKit.Row(root, i, SocialWindowKit.MARGIN, LIST_TOP + (SocialWindowKit.ROW_GAP * i), listWidth);
            }

            TMP_Text empty = SocialWindowKit.Label(root, "빈 목록", string.Empty, SocialWindowKit.MARGIN, LIST_TOP + 120f, listWidth, 60f,
                17f, SocialWindowKit.G_Muted, false, TextAlignmentOptions.Center);
            empty.gameObject.SetActive(false);

            Button prev = SocialWindowKit.TextButton(root, "이전 쪽", "<", SocialWindowKit.MARGIN, FOOTER_TOP, PAGE_BUTTON, FOOTER_HEIGHT, false);
            TMP_Text page = SocialWindowKit.Label(root, "쪽", "1 / 1", SocialWindowKit.MARGIN + PAGE_BUTTON + 4f, FOOTER_TOP, 92f,
                FOOTER_HEIGHT, 15f, SocialWindowKit.G_Text, false, TextAlignmentOptions.Center);
            Button next = SocialWindowKit.TextButton(root, "다음 쪽", ">", SocialWindowKit.MARGIN + PAGE_BUTTON + 100f, FOOTER_TOP,
                PAGE_BUTTON, FOOTER_HEIGHT, false);
            float statusLeft = SocialWindowKit.MARGIN + (PAGE_BUTTON * 2f) + 116f;
            TMP_Text status = SocialWindowKit.Label(root, "상태", string.Empty, statusLeft, FOOTER_TOP, WINDOW_WIDTH - statusLeft
                - SocialWindowKit.MARGIN, FOOTER_HEIGHT, 15f, EquipmentGrowthStyle.Soul, false, TextAlignmentOptions.MidlineLeft);

            GameObject confirm = SocialWindowKit.Confirm(root, WINDOW_WIDTH, WINDOW_HEIGHT, out TMP_Text confirmText, out Button accept,
                out Button cancel);

            SocialWindowKit.SetReference(view, "_closeButton", close);
            SocialWindowKit.SetReference(view, "_countLabel", count);
            SocialWindowKit.SetReference(view, "_nameInput", input);
            SocialWindowKit.SetReference(view, "_requestButton", request);
            SocialWindowKit.SetArray(view, "_tabButtons", tabButtons);
            SocialWindowKit.SetArray(view, "_tabLabels", tabLabels);
            SocialWindowKit.SetArray(view, "_tabSelected", tabSelected);
            SocialWindowKit.SetArray(view, "_rows", rows);
            SocialWindowKit.SetReference(view, "_emptyLabel", empty);
            SocialWindowKit.SetReference(view, "_prevPage", prev);
            SocialWindowKit.SetReference(view, "_nextPage", next);
            SocialWindowKit.SetReference(view, "_pageLabel", page);
            SocialWindowKit.SetReference(view, "_statusLabel", status);
            SocialWindowKit.SetReference(view, "_confirmPanel", confirm);
            SocialWindowKit.SetReference(view, "_confirmLabel", confirmText);
            SocialWindowKit.SetReference(view, "_confirmAccept", accept);
            SocialWindowKit.SetReference(view, "_confirmCancel", cancel);

            // 확인 상자가 맨 위에 그려지도록 마지막 자식으로 둔다
            confirm.transform.SetAsLastSibling();
            EquipmentGrowthStyle.Finish(root, EquipmentGrowthStyle.Mint);
        }
    }
}
#endif
