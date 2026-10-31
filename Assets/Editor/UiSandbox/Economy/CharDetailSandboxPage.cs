// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.UI;

namespace Shinshi.Editor
{
    /// <summary> 실제 CharDetailView에 이름 · 능력치 · 장비 · 수련 값을 넣고, 닫기 이벤트와 조회 요청 결과를 확인한다. 시험 배치 대상이다 </summary>
    public sealed class CharDetailSandboxPage : EconomyPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "economy_chardetail";
        public const string SCENE_PATH = "Assets/00.Scenes/Release/ShinshiTown.unity";

        public const string KEY_NAME     = "chardetail.name";
        public const string KEY_CLASS    = "chardetail.class";
        public const string KEY_COMPUTED = "chardetail.statsComputed";
        public const string KEY_ATTACK   = "chardetail.stat.attack";
        public const string KEY_HEALTH   = "chardetail.stat.health";
        public const string KEY_DEFENSE  = "chardetail.stat.defense";
        public const string KEY_CRIT     = "chardetail.stat.crit";
        public const string KEY_ENERGY   = "chardetail.stat.energy";
        public const string KEY_SPEED    = "chardetail.stat.speed";
        public const string KEY_TALISMAN = "chardetail.talisman";

        public const string OP_QUERY = "캐릭터 상세 조회";

        private const int SLOT_MAX     = 6;
        private const int TRAINING_MAX = 5;

        private static readonly EconomyListSchema G_SlotSchema = new EconomyListSchema(
            "chardetail.slot", "장비 슬롯 (줄 번호가 슬롯 번호)", SLOT_MAX,
            new[]
            {
                new EconomyField("name", "장비 이름", EEconomyFieldKind.Text, "장비 {n}"),
                new EconomyField("enhance", "강화 표기", EEconomyFieldKind.Text, "+0")
            },
            new[]
            {
                new[] { "낡은 검", "+3" },
                new[] { "가죽 모자", "+1" },
                new[] { "가죽 갑옷", "+0" }
            });

        private static readonly EconomyListSchema G_TrainingSchema = new EconomyListSchema(
            "chardetail.training", "수련 표기 (줄 번호가 능력치 번호)", TRAINING_MAX,
            new[]
            {
                new EconomyField("text", "표기", EEconomyFieldKind.Text, "Lv.{n}")
            },
            new[]
            {
                new[] { "Lv.3" },
                new[] { "Lv.2" },
                new[] { "Lv.1" }
            });

        private CharDetailView _view;
        private int            _closedCount;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "캐릭터 상세"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get { return string.Empty; }
        }

        public CharDetailView View
        {
            get { return _view; }
        }

        /// <summary> 닫기 단추로 Closed 이벤트가 온 횟수 </summary>
        public int ClosedCount
        {
            get { return _closedCount; }
        }

        public static EconomyListSchema SlotSchema
        {
            get { return G_SlotSchema; }
        }

        public static EconomyListSchema TrainingSchema
        {
            get { return G_TrainingSchema; }
        }

        /****************************************
        *                Methods
        ****************************************/

        protected override void SeedScenario(UiSandboxScenarioSO scenario)
        {
            EconomyScenarioValues.SeedString(scenario, KEY_NAME, "시험 캐릭터");
            EconomyScenarioValues.SeedString(scenario, KEY_CLASS, "직업 0");
            EconomyScenarioValues.SeedBool(scenario, KEY_COMPUTED, true);
            EconomyScenarioValues.SeedString(scenario, KEY_ATTACK, "120");
            EconomyScenarioValues.SeedString(scenario, KEY_HEALTH, "900");
            EconomyScenarioValues.SeedString(scenario, KEY_DEFENSE, "45");
            EconomyScenarioValues.SeedString(scenario, KEY_CRIT, "12%");
            EconomyScenarioValues.SeedString(scenario, KEY_ENERGY, "100");
            EconomyScenarioValues.SeedString(scenario, KEY_SPEED, "5.0");
            EconomyScenarioValues.SeedString(scenario, KEY_TALISMAN, "부적 3개 장착");
            EconomyList.Seed(scenario, G_SlotSchema);
            EconomyList.Seed(scenario, G_TrainingSchema);
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            _closedCount = 0;
            _view = EconomyFixtureFactory.EnsureCharDetail(context, out string note);
            SetFixtureNote(note);
            if (_view == null)
            {
                context.SetStatus(note);
                return;
            }

            _view.Closed += OnClosed;
        }

        protected override void OnApply(UiSandboxContext context)
        {
            ApplyView(context.Scenario, true);
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;
            EconomyScenarioValues.DrawString(scenario, KEY_NAME, "이름");
            EconomyScenarioValues.DrawString(scenario, KEY_CLASS, "직업");
            EconomyScenarioValues.DrawBool(scenario, KEY_COMPUTED, "능력치 계산됨 (끄면 빈 글자)");
            EconomyScenarioValues.DrawString(scenario, KEY_ATTACK, "공격력");
            EconomyScenarioValues.DrawString(scenario, KEY_HEALTH, "체력");
            EconomyScenarioValues.DrawString(scenario, KEY_DEFENSE, "방어력");
            EconomyScenarioValues.DrawString(scenario, KEY_CRIT, "치명");
            EconomyScenarioValues.DrawString(scenario, KEY_ENERGY, "기력");
            EconomyScenarioValues.DrawString(scenario, KEY_SPEED, "이동속도");
            EconomyScenarioValues.DrawString(scenario, KEY_TALISMAN, "부적 요약");
            EconomyList.Draw(scenario, G_SlotSchema);
            EconomyList.Draw(scenario, G_TrainingSchema);

            EditorGUILayout.Space();
            if (GUILayout.Button("편집 값을 화면에 다시 적용"))
            {
                Apply(context);
            }

            if ((_view != null) && (!_view.IsOpen) && (GUILayout.Button("창 다시 열기")))
            {
                _view.SetOpen(true);
            }

            if (DrawRequestButton(context, "상세 조회 요청 (가짜)"))
            {
                Send(OP_QUERY, () => ApplyView(context.Scenario, true), reason =>
                {
                    ReportFailure(OP_QUERY, reason);
                    ApplyView(context.Scenario, false);
                });
            }
        }

        protected override void OnDetach()
        {
            if (_view != null)
            {
                _view.Closed -= OnClosed;
                EconomyFixtureFactory.Release(Context, _view);
            }

            _view = null;
        }

        private void OnClosed()
        {
            ++_closedCount;
            if (Context != null)
            {
                Context.SetStatus("캐릭터 상세 창이 닫혔다");
            }
        }

        // computed가 거짓이면 능력치를 계산하지 못한 상태로 보고 빈 글자를 넣는다
        private void ApplyView(UiSandboxScenarioSO scenario, bool computed)
        {
            if (_view == null)
            {
                return;
            }

            bool showStats = computed && scenario.GetBool(KEY_COMPUTED, true);
            _view.SetIdentity(scenario.GetString(KEY_NAME, string.Empty), scenario.GetString(KEY_CLASS, string.Empty));
            if (showStats)
            {
                _view.SetStats(scenario.GetString(KEY_ATTACK, string.Empty), scenario.GetString(KEY_HEALTH, string.Empty),
                    scenario.GetString(KEY_DEFENSE, string.Empty), scenario.GetString(KEY_CRIT, string.Empty),
                    scenario.GetString(KEY_ENERGY, string.Empty), scenario.GetString(KEY_SPEED, string.Empty));
            }
            else
            {
                _view.SetStats(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
            }

            int slotCount = EconomyList.Count(scenario, G_SlotSchema);
            for (int i = 0; i < SLOT_MAX; ++i)
            {
                bool filled = i < slotCount;
                _view.SetSlot(i,
                    filled ? EconomyList.GetText(scenario, G_SlotSchema, i, "name") : string.Empty,
                    filled ? EconomyList.GetText(scenario, G_SlotSchema, i, "enhance") : string.Empty);
            }

            int trainingCount = EconomyList.Count(scenario, G_TrainingSchema);
            for (int i = 0; i < TRAINING_MAX; ++i)
            {
                _view.SetTraining(i, (i < trainingCount) ? EconomyList.GetText(scenario, G_TrainingSchema, i, "text") : string.Empty);
            }

            _view.SetTalismanSummary(scenario.GetString(KEY_TALISMAN, string.Empty));
        }
    }
}
