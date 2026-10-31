using Shinshi.Common;
using Shinshi.UI;
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 파티 현황 HUD. Dev_HUD 씬에는 HudPartyView가 없어 시험 전용 배치를 만들어 값을 적용한다 </summary>
    public sealed class HudPartyPage : PresentationPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "hud.party";
        public const string SCENE_PATH = "Assets/00.Scenes/Release/ShinshiTown.unity";

        public const int MEMBER_COUNT = 4;

        public const string OP_JOIN  = "party.join";
        public const string OP_LEAVE = "party.leave";

        private const int    MAX_HP       = 999999;
        private const string LABEL_PARTY  = "파티 현황";
        private const string JOINED_NAME  = "모의 파티원";

        private static readonly string[]       G_DefaultNames   = { "나", "동료A", "동료B", "동료C" };
        private static readonly EPlayerClass[] G_DefaultClasses =
        {
            EPlayerClass.Swordsman, EPlayerClass.Mageknight, EPlayerClass.Archer, EPlayerClass.Warrior
        };

        private HudPartyView _party;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "HUD 파티 현황"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get
            {
                return "HudPartyView는 Dev_HUD 씬에 없다. 시험 전용 배치로 슬롯 " + MEMBER_COUNT + "개에 이름 · 직업 · 체력을 적용한다. " +
                       PresentationNotes.CLASS_NOTE + " " + PresentationNotes.FIXTURE_NOTE;
            }
        }

        public HudPartyView PartyView
        {
            get { return _party; }
        }

        /****************************************
        *                Methods
        ****************************************/

        protected override void Seed(UiSandboxScenarioSO scenario)
        {
            for (int i = 0; i < MEMBER_COUNT; ++i)
            {
                scenario.SeedBool(Key(i, "present"), i < 3);
                scenario.SeedString(Key(i, "name"), G_DefaultNames[i]);
                scenario.SeedInt(Key(i, "class"), (int)G_DefaultClasses[i]);
                scenario.SeedBool(Key(i, "hasHp"), true);
                scenario.SeedInt(Key(i, "hp"), 700 - (i * 150));
                scenario.SeedInt(Key(i, "maxHp"), 1000);
            }
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions(EFixtureAnchor.MiddleLeft)
                .WithOffset(40.0f, 0.0f)
                .WithLength("_slots", MEMBER_COUNT);

            _party = BindView<HudPartyView>(context, LABEL_PARTY, options);
        }

        protected override void OnApply(UiSandboxContext context)
        {
            if (_party == null)
            {
                return;
            }

            UiSandboxScenarioSO scenario = context.Scenario;
            int count = Mathf.Min(MEMBER_COUNT, _party.SlotCount);
            for (int i = 0; i < count; ++i)
            {
                if (!scenario.GetBool(Key(i, "present")))
                {
                    _party.ClearMember(i);
                    continue;
                }

                _party.SetMember(i,
                    scenario.GetString(Key(i, "name")),
                    (EPlayerClass)scenario.GetInt(Key(i, "class")),
                    scenario.GetBool(Key(i, "hasHp")),
                    scenario.GetInt(Key(i, "hp")),
                    scenario.GetInt(Key(i, "maxHp")));
            }
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;

            for (int i = 0; i < MEMBER_COUNT; ++i)
            {
                DrawHeader("파티원 " + (i + 1));
                DrawBool(scenario, Key(i, "present"), "표시");
                DrawString(scenario, Key(i, "name"), "이름");
                DrawEnum(scenario, Key(i, "class"), "직업", EPlayerClass.Swordsman);
                DrawBool(scenario, Key(i, "hasHp"), "체력 정보 있음");
                DrawInt(scenario, Key(i, "hp"), "HP", 0, MAX_HP);
                DrawInt(scenario, Key(i, "maxHp"), "HP 최대", 0, MAX_HP);
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("합류 (모의 요청)"))
            {
                RunRequest(context, OP_JOIN, () => JoinNext(context));
            }

            if (GUILayout.Button("이탈 (모의 요청)"))
            {
                RunRequest(context, OP_LEAVE, () => LeaveLast(context));
            }

            EditorGUILayout.EndHorizontal();
        }

        protected override void OnDetach()
        {
            _party = null;
        }

        private void JoinNext(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;
            for (int i = 0; i < MEMBER_COUNT; ++i)
            {
                if (!scenario.GetBool(Key(i, "present")))
                {
                    scenario.SetBool(Key(i, "present"), true);
                    scenario.SetString(Key(i, "name"), JOINED_NAME + (i + 1));
                    OnApply(context);
                    return;
                }
            }
        }

        private void LeaveLast(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;
            for (int i = MEMBER_COUNT - 1; i >= 0; --i)
            {
                if (scenario.GetBool(Key(i, "present")))
                {
                    scenario.SetBool(Key(i, "present"), false);
                    OnApply(context);
                    return;
                }
            }
        }

        public static string Key(int index, string field)
        {
            return "hud.party." + index + "." + field;
        }
    }
}
