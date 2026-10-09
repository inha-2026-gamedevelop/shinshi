using Shinshi.UI;
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 체력 · 마나 · 레벨, 스킬 슬롯, 퀘스트 추적기 화면. Dev_HUD 씬의 실제 View에 값을 적용한다 </summary>
    public sealed class HudStatusPage : PresentationPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "hud.status";
        public const string SCENE_PATH = "Assets/00.Scenes/Dev_HUD/Dev_HUD.unity";

        public const string KEY_LEVEL          = "hud.status.level";
        public const string KEY_HP             = "hud.status.hp";
        public const string KEY_HP_MAX         = "hud.status.hpMax";
        public const string KEY_MP             = "hud.status.mp";
        public const string KEY_MP_MAX         = "hud.status.mpMax";
        public const string KEY_QUEST_CURRENT  = "hud.quest.current";
        public const string KEY_QUEST_TOTAL    = "hud.quest.total";
        public const string KEY_QUEST_COLLAPSE = "hud.quest.collapsed";
        public const string KEY_SLOT_PREFIX    = "hud.skill.";

        public const int MAX_LEVEL      = 30;
        public const int MAX_SLOTS      = 4;
        public const int DEFAULT_LEVEL  = 12;
        public const int DEFAULT_HP     = 820;
        public const int DEFAULT_HP_MAX = 1000;
        public const int DEFAULT_MP     = 60;
        public const int DEFAULT_MP_MAX = 100;

        private const float DEFAULT_COOLDOWN = 8.0f;
        private const float MAX_COOLDOWN     = 600.0f;
        private const int   MAX_VALUE        = 999999;
        private const int   MAX_QUEST        = 99;
        private const string LABEL_STATUS    = "체력 · 마나 · 레벨";
        private const string LABEL_SKILL     = "스킬 슬롯";
        private const string LABEL_QUEST     = "퀘스트 추적기";

        private static readonly string[] G_DefaultKeys = { "Q", "W", "E", "R" };

        private HudPlayerStatusView   _status;
        private HudQuestTrackerView   _quest;
        private HudSkillSlotView[]    _slots = new HudSkillSlotView[0];

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "HUD 상태 (체력 · 마나 · 레벨 · 스킬 · 퀘스트)"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get
            {
                return "Dev_HUD 씬의 HudPlayerStatusView, HudSkillSlotView, HudQuestTrackerView에 값을 적용한다. " +
                       PresentationNotes.LEVEL_NOTE + " " + PresentationNotes.CLASS_NOTE + " " + PresentationNotes.FIXTURE_NOTE;
            }
        }

        public HudPlayerStatusView StatusView
        {
            get { return _status; }
        }

        public HudQuestTrackerView QuestView
        {
            get { return _quest; }
        }

        public HudSkillSlotView[] SlotViews
        {
            get { return _slots; }
        }

        /****************************************
        *                Methods
        ****************************************/

        protected override void Seed(UiSandboxScenarioSO scenario)
        {
            scenario.SeedInt(KEY_LEVEL, DEFAULT_LEVEL);
            scenario.SeedInt(KEY_HP, DEFAULT_HP);
            scenario.SeedInt(KEY_HP_MAX, DEFAULT_HP_MAX);
            scenario.SeedInt(KEY_MP, DEFAULT_MP);
            scenario.SeedInt(KEY_MP_MAX, DEFAULT_MP_MAX);
            scenario.SeedInt(KEY_QUEST_CURRENT, 1);
            scenario.SeedInt(KEY_QUEST_TOTAL, 3);
            scenario.SeedBool(KEY_QUEST_COLLAPSE, false);

            for (int i = 0; i < MAX_SLOTS; ++i)
            {
                scenario.SeedString(SlotKey(i, "key"), G_DefaultKeys[i]);
                scenario.SeedFloat(SlotKey(i, "remaining"), 0.0f);
                scenario.SeedFloat(SlotKey(i, "duration"), DEFAULT_COOLDOWN);
            }
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            _status = BindView<HudPlayerStatusView>(context, LABEL_STATUS,
                new PresentationFixtureOptions(EFixtureAnchor.TopLeft).WithOffset(40.0f, -40.0f));
            _quest = BindView<HudQuestTrackerView>(context, LABEL_QUEST,
                new PresentationFixtureOptions(EFixtureAnchor.TopRight).WithOffset(-40.0f, -40.0f));

            HudSkillSlotView[] found = context.FindAll<HudSkillSlotView>();
            if (found.Length > 0)
            {
                _slots = found;
            }
            else
            {
                _slots = new[] { BindView<HudSkillSlotView>(context, LABEL_SKILL,
                    new PresentationFixtureOptions(EFixtureAnchor.BottomCenter).WithOffset(0.0f, 60.0f)) };
            }
        }

        protected override void OnApply(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;

            if (_status != null)
            {
                _status.SetLevel(scenario.GetInt(KEY_LEVEL));
                _status.SetHp(scenario.GetInt(KEY_HP), scenario.GetInt(KEY_HP_MAX));
                _status.SetMp(scenario.GetInt(KEY_MP), scenario.GetInt(KEY_MP_MAX));
            }

            if (_quest != null)
            {
                _quest.SetCount(scenario.GetInt(KEY_QUEST_CURRENT), scenario.GetInt(KEY_QUEST_TOTAL));
                _quest.SetCollapsed(scenario.GetBool(KEY_QUEST_COLLAPSE));
            }

            int count = Mathf.Min(_slots.Length, MAX_SLOTS);
            for (int i = 0; i < count; ++i)
            {
                if (_slots[i] == null)
                {
                    continue;
                }

                _slots[i].SetKeyLabel(scenario.GetString(SlotKey(i, "key")));
                _slots[i].SetCooldown(scenario.GetFloat(SlotKey(i, "remaining")), scenario.GetFloat(SlotKey(i, "duration")));
            }
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;

            DrawHeader(LABEL_STATUS);
            DrawInt(scenario, KEY_LEVEL, "레벨", 0, MAX_LEVEL);
            DrawInt(scenario, KEY_HP, "HP", 0, MAX_VALUE);
            DrawInt(scenario, KEY_HP_MAX, "HP 최대", 0, MAX_VALUE);
            DrawInt(scenario, KEY_MP, "MP", 0, MAX_VALUE);
            DrawInt(scenario, KEY_MP_MAX, "MP 최대", 0, MAX_VALUE);

            DrawHeader(LABEL_SKILL);
            int count = Mathf.Min(_slots.Length, MAX_SLOTS);
            for (int i = 0; i < count; ++i)
            {
                EditorGUILayout.LabelField("슬롯 " + (i + 1), EditorStyles.miniBoldLabel);
                DrawString(scenario, SlotKey(i, "key"), "키 표시");
                DrawFloat(scenario, SlotKey(i, "remaining"), "남은 쿨다운(초)", 0.0f, MAX_COOLDOWN);
                DrawFloat(scenario, SlotKey(i, "duration"), "전체 쿨다운(초)", 0.0f, MAX_COOLDOWN);
            }

            DrawHeader(LABEL_QUEST);
            DrawInt(scenario, KEY_QUEST_CURRENT, "현재 개수", 0, MAX_QUEST);
            DrawInt(scenario, KEY_QUEST_TOTAL, "전체 개수", 0, MAX_QUEST);
            DrawBool(scenario, KEY_QUEST_COLLAPSE, "접힘");
        }

        protected override void OnDetach()
        {
            _status = null;
            _quest  = null;
            _slots  = new HudSkillSlotView[0];
        }

        public static string SlotKey(int index, string field)
        {
            return KEY_SLOT_PREFIX + index + "." + field;
        }
    }
}
