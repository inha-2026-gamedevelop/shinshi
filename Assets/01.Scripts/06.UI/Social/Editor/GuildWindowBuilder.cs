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
    /// <summary> 길드 창 프리팹을 공통 창 스킨과 글꼴로 만든다. 길드 없음, 있음 두 묶음이 같은 목록 줄을 쓴다 </summary>
    public static class GuildWindowBuilder
    {
        // 다시 실행하면 프리팹을 통째로 새로 쓰므로 프리팹을 손으로 고친 내용은 사라진다

        /****************************************
        *                Fields
        ****************************************/

        public const string PREFAB_PATH = "Assets/02.Prefab/UI/Windows/PF_GuildWindow.prefab";

        private const float WINDOW_WIDTH  = 800f;
        private const float WINDOW_HEIGHT = 690f;
        private const float FIRST_TOP     = 88f;
        private const float SECOND_TOP    = 140f;
        private const float ACTION_WIDTH  = 200f;
        private const float LIST_TITLE    = 196f;
        private const float TAB_TOP       = 180f;
        private const float TAB_WIDTH     = 160f;
        private const float TAB_GAP       = 168f;
        private const float LIST_TOP      = 236f;
        private const float FOOTER_TOP    = 632f;
        private const float PAGE_BUTTON   = 44f;
        private const float FOOTER_HEIGHT = 36f;
        private const float LEAVE_WIDTH   = 136f;

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/길드창 만들기")]
        public static void Build()
        {
            SocialWindowKit.SavePrefab("PF_GuildWindow", PREFAB_PATH, BuildWindow);
            Debug.Log("[UI] 길드창 프리팹을 만들었다: " + PREFAB_PATH);
        }

        private static void BuildWindow(RectTransform root)
        {
            float innerWidth = WINDOW_WIDTH - (SocialWindowKit.MARGIN * 2f);
            float inputWidth = innerWidth - ACTION_WIDTH - 12f;
            float actionLeft = WINDOW_WIDTH - SocialWindowKit.MARGIN - ACTION_WIDTH;
            Button close = SocialWindowKit.Frame(root, WINDOW_WIDTH, WINDOW_HEIGHT, "길드", out TMP_Text title, out TMP_Text count);
            var view = root.gameObject.AddComponent<GuildView>();

            RectTransform noGuild = SocialWindowKit.Rect(root, "길드 없음", 0f, 0f, WINDOW_WIDTH, WINDOW_HEIGHT);
            TMP_InputField createInput = SocialWindowKit.Input(noGuild, "만들 이름", "새 길드 이름 (2~8자)", SocialWindowKit.MARGIN, FIRST_TOP,
                inputWidth, SocialWindowKit.BUTTON_HEIGHT, SocialLimits.MAX_NAME_INPUT_LENGTH);
            Button create = SocialWindowKit.TextButton(noGuild, "길드 만들기 버튼", "길드 만들기", actionLeft, FIRST_TOP, ACTION_WIDTH,
                SocialWindowKit.BUTTON_HEIGHT, true);
            TMP_InputField applyInput = SocialWindowKit.Input(noGuild, "신청 이름", "가입할 길드 이름", SocialWindowKit.MARGIN, SECOND_TOP,
                inputWidth, SocialWindowKit.BUTTON_HEIGHT, SocialLimits.MAX_NAME_INPUT_LENGTH);
            Button apply = SocialWindowKit.TextButton(noGuild, "가입 신청 버튼", "가입 신청", actionLeft, SECOND_TOP, ACTION_WIDTH,
                SocialWindowKit.BUTTON_HEIGHT, false);

            RectTransform guild = SocialWindowKit.Rect(root, "길드 있음", 0f, 0f, WINDOW_WIDTH, WINDOW_HEIGHT);
            RectTransform leader = SocialWindowKit.Rect(guild, "길드장 도구", 0f, 0f, WINDOW_WIDTH, WINDOW_HEIGHT);
            TMP_InputField inviteInput = SocialWindowKit.Input(leader, "초대 이름", "초대할 캐릭터 이름", SocialWindowKit.MARGIN, FIRST_TOP,
                inputWidth, SocialWindowKit.BUTTON_HEIGHT, SocialLimits.MAX_NAME_INPUT_LENGTH);
            Button invite = SocialWindowKit.TextButton(leader, "초대 버튼", "초대하기", actionLeft, FIRST_TOP, ACTION_WIDTH,
                SocialWindowKit.BUTTON_HEIGHT, true);
            var tabButtons  = new Object[GuildViewRules.TAB_COUNT];
            var tabLabels   = new Object[GuildViewRules.TAB_COUNT];
            var tabSelected = new Object[GuildViewRules.TAB_COUNT];
            for (int i = 0; i < GuildViewRules.TAB_COUNT; ++i)
            {
                tabButtons[i] = SocialWindowKit.Tab(guild, GuildViewRules.TabLabel(i, 0), SocialWindowKit.MARGIN + (TAB_GAP * i), TAB_TOP,
                    TAB_WIDTH, SocialWindowKit.BUTTON_HEIGHT, out TMP_Text label, out GameObject selected);
                tabLabels[i]   = label;
                tabSelected[i] = selected;
            }

            Button leave = SocialWindowKit.TextButton(guild, "탈퇴 버튼", "길드 탈퇴", WINDOW_WIDTH - SocialWindowKit.MARGIN - LEAVE_WIDTH,
                FOOTER_TOP, LEAVE_WIDTH, FOOTER_HEIGHT, false);

            TMP_Text listTitle = SocialWindowKit.Label(root, "목록 제목", string.Empty, SocialWindowKit.MARGIN, LIST_TITLE, innerWidth, 32f,
                18f, SocialWindowKit.G_Text, true, TextAlignmentOptions.MidlineLeft);
            var rows = new Object[GuildViewRules.ROWS_PER_PAGE];
            for (int i = 0; i < GuildViewRules.ROWS_PER_PAGE; ++i)
            {
                rows[i] = SocialWindowKit.Row(root, i, SocialWindowKit.MARGIN, LIST_TOP + (SocialWindowKit.ROW_GAP * i), innerWidth);
            }

            TMP_Text empty = SocialWindowKit.Label(root, "빈 목록", string.Empty, SocialWindowKit.MARGIN, LIST_TOP + 120f, innerWidth, 60f,
                17f, SocialWindowKit.G_Muted, false, TextAlignmentOptions.Center);
            empty.gameObject.SetActive(false);

            Button prev = SocialWindowKit.TextButton(root, "이전 쪽", "<", SocialWindowKit.MARGIN, FOOTER_TOP, PAGE_BUTTON, FOOTER_HEIGHT, false);
            TMP_Text page = SocialWindowKit.Label(root, "쪽", "1 / 1", SocialWindowKit.MARGIN + PAGE_BUTTON + 4f, FOOTER_TOP, 92f,
                FOOTER_HEIGHT, 15f, SocialWindowKit.G_Text, false, TextAlignmentOptions.Center);
            Button next = SocialWindowKit.TextButton(root, "다음 쪽", ">", SocialWindowKit.MARGIN + PAGE_BUTTON + 100f, FOOTER_TOP,
                PAGE_BUTTON, FOOTER_HEIGHT, false);
            float statusLeft = SocialWindowKit.MARGIN + (PAGE_BUTTON * 2f) + 116f;
            TMP_Text status = SocialWindowKit.Label(root, "상태", string.Empty, statusLeft, FOOTER_TOP, WINDOW_WIDTH - statusLeft
                - SocialWindowKit.MARGIN - LEAVE_WIDTH - 12f, FOOTER_HEIGHT, 15f, EquipmentGrowthStyle.Soul, false,
                TextAlignmentOptions.MidlineLeft);

            GameObject confirm = SocialWindowKit.Confirm(root, WINDOW_WIDTH, WINDOW_HEIGHT, out TMP_Text confirmText, out Button accept,
                out Button cancel);

            SocialWindowKit.SetReference(view, "_closeButton", close);
            SocialWindowKit.SetReference(view, "_titleLabel", title);
            SocialWindowKit.SetReference(view, "_countLabel", count);
            SocialWindowKit.SetReference(view, "_noGuildGroup", noGuild.gameObject);
            SocialWindowKit.SetReference(view, "_createInput", createInput);
            SocialWindowKit.SetReference(view, "_createButton", create);
            SocialWindowKit.SetReference(view, "_applyInput", applyInput);
            SocialWindowKit.SetReference(view, "_applyButton", apply);
            SocialWindowKit.SetReference(view, "_guildGroup", guild.gameObject);
            SocialWindowKit.SetArray(view, "_tabButtons", tabButtons);
            SocialWindowKit.SetArray(view, "_tabLabels", tabLabels);
            SocialWindowKit.SetArray(view, "_tabSelected", tabSelected);
            SocialWindowKit.SetReference(view, "_leaderGroup", leader.gameObject);
            SocialWindowKit.SetReference(view, "_inviteInput", inviteInput);
            SocialWindowKit.SetReference(view, "_inviteButton", invite);
            SocialWindowKit.SetReference(view, "_leaveButton", leave);
            SocialWindowKit.SetReference(view, "_listTitle", listTitle);
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

            guild.gameObject.SetActive(false);
            confirm.transform.SetAsLastSibling();
            EquipmentGrowthStyle.Finish(root, EquipmentGrowthStyle.Gold);
        }
    }
}
#endif
