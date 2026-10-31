using Shinshi.UI;
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 구 소환사 전용 공명 · 승천 HUD. 현행 3직업(검사/마검사/궁수) HUD와 분리한 별도 화면이다 </summary>
    public sealed class HudResonanceLegacyPage : PresentationPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "hud.resonance.legacy";
        public const string SCENE_PATH = "Assets/00.Scenes/Release/DungeonForest.unity";

        public const string KEY_CURRENT = "hud.resonance.current";
        public const string KEY_MAX     = "hud.resonance.max";
        public const int    SLOT_COUNT  = 3;

        private const int    DEFAULT_CURRENT = 40;
        private const int    DEFAULT_MAX     = 100;
        private const int    MAX_VALUE       = 9999;
        private const string LABEL_RESONANCE = "공명 (구 소환사)";

        private static readonly int[] G_DefaultCosts = { 60, 80, 40 };

        private HudResonanceView _resonance;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "HUD 공명 · 승천 (구 소환사 전용)"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get
            {
                return "구 소환사 전용 공명 · 승천 View다. 현행 3직업(검사/마검사/궁수)에는 공명 슬롯이 없으므로 현행 HUD 화면(hud.status, hud.combat)과 섞어 해석하지 않는다. " +
                       "공명 최대 100과 승천 비용 {60, 80, 40}은 데모 값이다. " + PresentationNotes.CLASS_NOTE + " " + PresentationNotes.FIXTURE_NOTE;
            }
        }

        public HudResonanceView ResonanceView
        {
            get { return _resonance; }
        }

        /****************************************
        *                Methods
        ****************************************/

        protected override void Seed(UiSandboxScenarioSO scenario)
        {
            scenario.SeedInt(KEY_CURRENT, DEFAULT_CURRENT);
            scenario.SeedInt(KEY_MAX, DEFAULT_MAX);

            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                scenario.SeedInt(CostKey(i), G_DefaultCosts[i]);
                scenario.SeedBool(CoolingKey(i), false);
            }
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions(EFixtureAnchor.BottomCenter)
                .WithOffset(0.0f, 140.0f)
                .WithLength("_slotGroups", SLOT_COUNT)
                .WithLength("_costLabels", SLOT_COUNT);

            _resonance = BindView<HudResonanceView>(context, LABEL_RESONANCE, options);
        }

        protected override void OnApply(UiSandboxContext context)
        {
            if (_resonance == null)
            {
                return;
            }

            UiSandboxScenarioSO scenario = context.Scenario;
            _resonance.SetResonance(scenario.GetInt(KEY_CURRENT), scenario.GetInt(KEY_MAX));

            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                _resonance.SetAscensionCost(i, scenario.GetInt(CostKey(i)));
                _resonance.SetAscensionCooling(i, scenario.GetBool(CoolingKey(i)));
            }
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;

            EditorGUILayout.HelpBox("구 소환사 전용 화면이다. 현행 3직업 HUD에는 적용되지 않는다", MessageType.Warning);
            DrawInt(scenario, KEY_CURRENT, "공명 현재", 0, MAX_VALUE);
            DrawInt(scenario, KEY_MAX, "공명 최대", 0, MAX_VALUE);

            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                DrawInt(scenario, CostKey(i), "승천 " + (i + 1) + " 비용", 0, MAX_VALUE);
                DrawBool(scenario, CoolingKey(i), "승천 " + (i + 1) + " 쿨다운 중");
            }
        }

        protected override void OnDetach()
        {
            _resonance = null;
        }

        public static string CostKey(int index)
        {
            return "hud.resonance.cost." + index;
        }

        public static string CoolingKey(int index)
        {
            return "hud.resonance.cooling." + index;
        }
    }
}
