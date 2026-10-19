// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 길드 창을 공통 어두운 스타일로 한 번 다시 배치한다. 길드 없음 · 있음 묶음, 탭, 줄, 확인창은 GuildView 직렬화 참조 그대로다 </summary>
    public static class GuildModernLayout
    {
        private const float WIDTH     = 1500f;
        private const float HEIGHT    = 820f;
        private const float PANEL_Y   = 84f;
        private const float PANEL_H   = 640f;
        private const float LEFT_X    = 32f;
        private const float LEFT_W    = 380f;
        private const float RIGHT_X   = 432f;
        private const float RIGHT_W   = 1036f;
        private const float TAB_W     = 180f;
        private const float TAB_H     = 48f;
        private const float ROW_GAP   = 6f;

        public static void Apply(GuildView view)
        {
            var window = view.transform as RectTransform;
            TMP_Text status = ModernDarkKit.Field<TMP_Text>(view, "_statusLabel");
            if (!ModernDarkKit.TryBegin(view, window, status, out ModernDarkKit kit))
            {
                return;
            }

            foreach (EquipmentGrowthSurface surface in window.GetComponentsInChildren<EquipmentGrowthSurface>(true))
            {
                surface.enabled = false;
            }

            kit.Frame(new Vector2(WIDTH, HEIGHT), "길드");
            Image windowImage = window.GetComponent<Image>();
            if (windowImage != null)
            {
                windowImage.enabled    = true;
                windowImage.fillCenter = true;
                windowImage.color      = Color.white;
            }

            GameObject noGuild = ModernDarkKit.Field<GameObject>(view, "_noGuildGroup");
            GameObject guild   = ModernDarkKit.Field<GameObject>(view, "_guildGroup");
            GameObject leader  = ModernDarkKit.Field<GameObject>(view, "_leaderGroup");
            GameObject confirm = ModernDarkKit.Field<GameObject>(view, "_confirmPanel");
            kit.PlaceGroup(noGuild);
            kit.PlaceGroup(guild);
            kit.PlaceGroup(leader);

            Button close = ModernDarkKit.Field<Button>(view, "_closeButton");
            kit.Place(close, WIDTH - 72f, 8f, 44f, 44f);
            kit.StyleClose(close);

            LayoutNoGuild(view, kit, noGuild);
            LayoutGuild(view, kit, guild, leader);

            kit.Place(status, LEFT_X, HEIGHT - 56f, WIDTH - 64f, 32f);
            ModernDarkKit.StyleText(status, 20f, UiModernDark.TextGold);

            LayoutConfirm(view, kit, confirm);
            kit.HideLegacy();
        }

        private static void LayoutNoGuild(GuildView view, ModernDarkKit kit, GameObject group)
        {
            if (group == null)
            {
                return;
            }

            // 만들기 · 가입 카드는 왼쪽 열에 세로로 두고, 오른쪽 열(받은 초대 제목 · 줄 · 빈 안내)은 카드와 겹치지 않게 비워 둔다
            const float CARD_H   = 240f;
            const float CARD_GAP = 20f;
            float applyY = PANEL_Y + CARD_H + CARD_GAP;
            float inner  = LEFT_W - 48f;
            kit.InsetIn(group, "CreateCard", LEFT_X, PANEL_Y, LEFT_W, CARD_H);
            kit.InsetIn(group, "ApplyCard", LEFT_X, applyY, LEFT_W, CARD_H);

            Place(kit, ModernDarkKit.Field<TMP_InputField>(view, "_createInput"), LEFT_X + 24f, PANEL_Y + 44f, inner, 56f);
            Place(kit, ModernDarkKit.Field<Button>(view, "_createButton"), LEFT_X + 24f, PANEL_Y + 124f, inner, 64f, true);
            Place(kit, ModernDarkKit.Field<TMP_InputField>(view, "_applyInput"), LEFT_X + 24f, applyY + 44f, inner, 56f);
            Place(kit, ModernDarkKit.Field<Button>(view, "_applyButton"), LEFT_X + 24f, applyY + 124f, inner, 64f, true);
        }

        private static void LayoutGuild(GuildView view, ModernDarkKit kit, GameObject group, GameObject leader)
        {
            if (group == null)
            {
                return;
            }

            kit.InsetIn(group, "InfoPanel", LEFT_X, PANEL_Y, LEFT_W, PANEL_H);
            kit.InsetIn(group, "ListPanel", RIGHT_X, PANEL_Y, RIGHT_W, PANEL_H);

            TMP_Text title = ModernDarkKit.Field<TMP_Text>(view, "_titleLabel");
            TMP_Text count = ModernDarkKit.Field<TMP_Text>(view, "_countLabel");
            // 루트 아래 앞쪽 형제로 있으면 길드 묶음 안의 InfoPanel 장식에 가려지므로 묶음 안 맨 위 형제로 옮긴다
            AboveInfoPanel(title, group);
            AboveInfoPanel(count, group);
            kit.Place(title, LEFT_X + 24f, PANEL_Y + 24f, LEFT_W - 48f, 44f);
            kit.Place(count, LEFT_X + 24f, PANEL_Y + 74f, LEFT_W - 48f, 30f);
            ModernDarkKit.StyleText(title, 30f, UiModernDark.TextGold);
            ModernDarkKit.StyleText(count, 20f, UiModernDark.Cyan);

            if (leader != null)
            {
                kit.InsetIn(leader, "InvitePanel", LEFT_X + 16f, PANEL_Y + 320f, LEFT_W - 32f, 190f);
            }

            Place(kit, ModernDarkKit.Field<TMP_InputField>(view, "_inviteInput"), LEFT_X + 32f, PANEL_Y + 340f, LEFT_W - 64f, 52f);
            Place(kit, ModernDarkKit.Field<Button>(view, "_inviteButton"), LEFT_X + 32f, PANEL_Y + 412f, LEFT_W - 64f, 56f, true);
            Place(kit, ModernDarkKit.Field<Button>(view, "_leaveButton"), LEFT_X + 32f, PANEL_Y + PANEL_H - 80f, LEFT_W - 64f, 56f, false);

            var tabs = ModernDarkKit.Field<Button[]>(view, "_tabButtons");
            int tabCount = (tabs != null) ? tabs.Length : 0;
            for (int i = 0; i < tabCount; ++i)
            {
                kit.Place(tabs[i], RIGHT_X + 20f + (i * (TAB_W + 8f)), PANEL_Y + 16f, TAB_W, TAB_H);
                StyleTab(tabs[i]);
            }

            TMP_Text listTitle = ModernDarkKit.Field<TMP_Text>(view, "_listTitle");
            kit.Place(listTitle, RIGHT_X + 24f, PANEL_Y + 78f, RIGHT_W - 48f, 36f);
            ModernDarkKit.StyleText(listTitle, 22f, UiModernDark.TextGold);

            var rows     = ModernDarkKit.Field<SocialRowView[]>(view, "_rows");
            int rowCount = (rows != null) ? rows.Length : 0;
            float top    = PANEL_Y + 124f;
            float avail  = PANEL_H - 124f - 72f;
            float rowH   = (rowCount > 0) ? Mathf.Min(64f, (avail / rowCount) - ROW_GAP) : 64f;
            for (int i = 0; i < rowCount; ++i)
            {
                if (rows[i] == null)
                {
                    continue;
                }

                kit.Place(rows[i], RIGHT_X + 20f, top + (i * (rowH + ROW_GAP)), RIGHT_W - 40f, rowH);
                SocialRowModernLayout.Style(rows[i], RIGHT_W - 40f, rowH);
            }

            TMP_Text empty = ModernDarkKit.Field<TMP_Text>(view, "_emptyLabel");
            kit.Place(empty, RIGHT_X + 24f, top + 40f, RIGHT_W - 48f, 48f);
            if (empty != null)
            {
                ModernDarkKit.StyleText(empty, 22f, UiModernDark.TextMuted);
                empty.alignment = TextAlignmentOptions.Center;
            }

            float footY = PANEL_Y + PANEL_H - 56f;
            Button prev = ModernDarkKit.Field<Button>(view, "_prevPage");
            Button next = ModernDarkKit.Field<Button>(view, "_nextPage");
            TMP_Text page = ModernDarkKit.Field<TMP_Text>(view, "_pageLabel");
            kit.Place(prev, RIGHT_X + (RIGHT_W * 0.5f) - 150f, footY, 90f, 44f);
            kit.Place(page, RIGHT_X + (RIGHT_W * 0.5f) - 50f, footY, 100f, 44f);
            kit.Place(next, RIGHT_X + (RIGHT_W * 0.5f) + 60f, footY, 90f, 44f);
            ModernDarkKit.StyleButton(prev, false);
            ModernDarkKit.StyleButton(next, false);
            ModernDarkKit.StyleText(page, 20f, UiModernDark.TextMain);
        }

        private static void AboveInfoPanel(TMP_Text label, GameObject group)
        {
            if (label == null)
            {
                return;
            }

            if (label.transform.parent != group.transform)
            {
                label.transform.SetParent(group.transform, false);
            }

            label.transform.SetAsLastSibling();
        }

        private static void LayoutConfirm(GuildView view, ModernDarkKit kit, GameObject confirm)
        {
            if (confirm == null)
            {
                return;
            }

            // 확인창 루트 배경은 뒤 창의 클릭을 막는 모달 차단막이라 켜 둔 채 raycastTarget을 지키고, 안쪽 옛 장식만 끈다
            Graphic backdrop       = confirm.GetComponent<Graphic>();
            bool    backdropActive = (backdrop != null) && backdrop.enabled;
            bool    backdropBlocks = (backdrop != null) && backdrop.raycastTarget;

            kit.PlaceGroup(confirm, false);
            ModernDarkKit.Quiet(confirm);
            if (backdrop != null)
            {
                backdrop.enabled       = backdropActive;
                backdrop.raycastTarget = backdropBlocks;
            }

            kit.InsetIn(confirm, "ConfirmCard", 480f, 290f, 540f, 240f, UiModernDark.SPRITE_PANEL);

            TMP_Text label = ModernDarkKit.Field<TMP_Text>(view, "_confirmLabel");
            kit.Place(label, 512f, 322f, 476f, 100f);
            if (label != null)
            {
                ModernDarkKit.StyleText(label, 22f, UiModernDark.TextMain);
                label.alignment        = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.Normal;
            }

            Place(kit, ModernDarkKit.Field<Button>(view, "_confirmAccept"), 512f, 446f, 230f, 56f, true);
            Place(kit, ModernDarkKit.Field<Button>(view, "_confirmCancel"), 758f, 446f, 230f, 56f, false);
        }

        private static void StyleTab(Button tab)
        {
            if (tab == null)
            {
                return;
            }

            if (tab.targetGraphic is Image image)
            {
                ModernDarkKit.SetSliced(image, UiModernDark.SPRITE_TAB, Color.white);
            }

            TMP_Text label = tab.GetComponentInChildren<TMP_Text>(true);
            ModernDarkKit.StyleText(label, 22f, UiModernDark.TextMain);
        }

        private static void Place(ModernDarkKit kit, Component widget, float x, float y, float w, float h, bool primary = false)
        {
            kit.Place(widget, x, y, w, h);
            if (widget is Button button)
            {
                ModernDarkKit.StyleButton(button, primary);
            }
            else if (widget is TMP_InputField input)
            {
                ModernDarkKit.StyleInset(input.targetGraphic);
                ModernDarkKit.StyleText(input.textComponent, 22f, UiModernDark.TextMain);
            }
        }
    }
}
