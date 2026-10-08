using Shinshi.Combat;
using Shinshi.UI;
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 전투 미터(딜미터)와 종료 결과 창. 두 View 모두 Dev_HUD 씬에 없어 시험 전용 배치를 만들고, 표시 문구는 CombatMeterViewRules로 만든다 </summary>
    public sealed class HudCombatMeterPage : PresentationPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "hud.combatmeter";
        public const string SCENE_PATH = "Assets/00.Scenes/Dev_HUD/Dev_HUD.unity";

        public const int MEMBER_COUNT = 4;
        public const int SLOT_COUNT   = (int)ECombatMeterSlot.Other + 1;
        public const int PHASE_COUNT  = 2;
        public const int SOURCE_COUNT = 3;

        public const string KEY_VISIBLE      = "hud.meter.visible";
        public const string KEY_EXPANDED     = "hud.meter.expanded";
        public const string KEY_BREAKDOWN    = "hud.meter.selfBreakdown";
        public const string KEY_PHASE_MODE   = "hud.meter.phaseMode";
        public const string KEY_SECONDS      = "hud.meter.seconds";
        public const string KEY_SELF_INDEX   = "hud.meter.selfIndex";
        public const string KEY_HAS_RECORD   = "hud.meter.result.hasRecord";
        public const string KEY_SELF_DETAIL  = "hud.meter.result.selfDetail";

        private const float  MAX_DAMAGE   = 1.0e9f;
        private const float  MAX_SECONDS  = 36000.0f;
        private const int    MAX_COUNT    = 9999;
        private const string LABEL_METER  = "전투 미터";
        private const string LABEL_RESULT = "전투 미터 결과";
        private const string TEXT_ALL     = "전체";
        private const string TEXT_PHASE   = "단계별";

        private static readonly string[] G_DefaultNames   = { "나", "동료A", "동료B", "동료C" };
        private static readonly float[]  G_DefaultDamages = { 125000.0f, 98000.0f, 61000.0f, 40000.0f };

        private HudCombatMeterView       _meter;
        private HudCombatMeterResultView _result;
        private UiSandboxContext         _subscribed;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "HUD 전투 미터 · 결과"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get
            {
                return "HudCombatMeterView와 HudCombatMeterResultView는 Dev_HUD 씬에 없다. 시험 전용 배치에 파티 " + MEMBER_COUNT +
                       "행 · 내 기술 " + SLOT_COUNT + "칸(ECombatMeterSlot 전체: 기본 공격 · 기술 1~4 · 소환물 · 추가 기술 · 기타)을 적용하고, 문구는 CombatMeterViewRules(피해 · DPS · 점유율 · 초)로 만든다. " +
                       "단계 전환 버튼과 내 줄 클릭은 View의 실제 이벤트를 구독해 시나리오를 바꾼다(Play Mode). " +
                       PresentationNotes.FIXTURE_NOTE;
            }
        }

        public HudCombatMeterView MeterView
        {
            get { return _meter; }
        }

        public HudCombatMeterResultView ResultView
        {
            get { return _result; }
        }

        /****************************************
        *                Methods
        ****************************************/

        protected override void Seed(UiSandboxScenarioSO scenario)
        {
            scenario.SeedBool(KEY_VISIBLE, true);
            scenario.SeedBool(KEY_EXPANDED, true);
            scenario.SeedBool(KEY_BREAKDOWN, true);
            scenario.SeedBool(KEY_PHASE_MODE, false);
            scenario.SeedFloat(KEY_SECONDS, 180.0f);
            scenario.SeedInt(KEY_SELF_INDEX, 0);
            scenario.SeedBool(KEY_HAS_RECORD, true);
            scenario.SeedBool(KEY_SELF_DETAIL, true);

            for (int i = 0; i < MEMBER_COUNT; ++i)
            {
                scenario.SeedBool(Key("member", i, "present"), true);
                scenario.SeedString(Key("member", i, "name"), G_DefaultNames[i]);
                scenario.SeedFloat(Key("member", i, "damage"), G_DefaultDamages[i]);
                scenario.SeedFloat(Key("member", i, "taken"), 20000.0f + (i * 5000.0f));
                scenario.SeedInt(Key("member", i, "deaths"), i % 2);
                scenario.SeedInt(Key("member", i, "revives"), i % 2);
            }

            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                scenario.SeedFloat(Key("slot", i, "damage"), 30000.0f - (i * 3000.0f));
                scenario.SeedInt(Key("slot", i, "hits"), 120 - (i * 10));
                scenario.SeedFloat(Key("slot", i, "maxHit"), 2400.0f - (i * 200.0f));
            }

            for (int i = 0; i < PHASE_COUNT; ++i)
            {
                scenario.SeedFloat(Key("phase", i, "seconds"), 90.0f);
                scenario.SeedFloat(Key("phase", i, "damage"), 60000.0f + (i * 20000.0f));
            }

            for (int i = 0; i < SOURCE_COUNT; ++i)
            {
                scenario.SeedString(Key("taken", i, "source"), "enemy_" + (i + 1));
                scenario.SeedFloat(Key("taken", i, "amount"), 9000.0f - (i * 2000.0f));
                scenario.SeedString(Key("death", i, "source"), "boss_attack_" + (i + 1));
                scenario.SeedFloat(Key("death", i, "amount"), 4000.0f - (i * 500.0f));
                scenario.SeedFloat(Key("death", i, "before"), 2.0f + i);
            }
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            PresentationFixtureOptions meterOptions = new PresentationFixtureOptions(EFixtureAnchor.MiddleRight)
                .WithOffset(-40.0f, 0.0f)
                .WithLength("_partyRows", MEMBER_COUNT)
                .WithLength("_selfSlotRows", SLOT_COUNT);

            PresentationFixtureOptions resultOptions = new PresentationFixtureOptions(EFixtureAnchor.Center)
                .WithLength("_rows", MEMBER_COUNT)
                .WithLength("_selfSlotRows", SLOT_COUNT)
                .WithLength("_phaseRows", PHASE_COUNT)
                .WithLength("_topDamageTakenRows", SOURCE_COUNT)
                .WithLength("_deathRows", SOURCE_COUNT);

            _meter  = BindView<HudCombatMeterView>(context, LABEL_METER, meterOptions);
            _result = BindView<HudCombatMeterResultView>(context, LABEL_RESULT, resultOptions);

            _subscribed = context;
            if (_meter != null)
            {
                _meter.PhaseToggleClicked += OnPhaseToggleClicked;
                _meter.SelfRowClicked     += OnMeterSelfRowClicked;
            }

            if (_result != null)
            {
                _result.SelfRowClicked += OnResultSelfRowClicked;
            }
        }

        protected override void OnApply(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;
            float seconds = scenario.GetFloat(KEY_SECONDS);

            float total = 0.0f;
            for (int i = 0; i < MEMBER_COUNT; ++i)
            {
                if (scenario.GetBool(Key("member", i, "present")))
                {
                    total += scenario.GetFloat(Key("member", i, "damage"));
                }
            }

            if (_meter != null)
            {
                ApplyMeter(scenario, seconds, total);
            }

            if (_result != null)
            {
                ApplyResult(scenario, seconds);
            }
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;

            DrawHeader("미터 창");
            DrawBool(scenario, KEY_VISIBLE, "창 표시");
            DrawBool(scenario, KEY_EXPANDED, "펼침");
            DrawBool(scenario, KEY_BREAKDOWN, "내 기술 내역 표시");
            DrawBool(scenario, KEY_PHASE_MODE, "단계별 보기");
            DrawFloat(scenario, KEY_SECONDS, "집계 구간(초)", 0.0f, MAX_SECONDS);
            DrawInt(scenario, KEY_SELF_INDEX, "내 줄 번호", 0, MEMBER_COUNT - 1);

            for (int i = 0; i < MEMBER_COUNT; ++i)
            {
                DrawHeader("파티원 " + (i + 1));
                DrawBool(scenario, Key("member", i, "present"), "표시");
                DrawString(scenario, Key("member", i, "name"), "이름");
                DrawFloat(scenario, Key("member", i, "damage"), "준 피해", 0.0f, MAX_DAMAGE);
                DrawFloat(scenario, Key("member", i, "taken"), "받은 피해", 0.0f, MAX_DAMAGE);
                DrawInt(scenario, Key("member", i, "deaths"), "사망", 0, MAX_COUNT);
                DrawInt(scenario, Key("member", i, "revives"), "부활", 0, MAX_COUNT);
            }

            DrawHeader("내 기술 내역");
            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                EditorGUILayout.LabelField(CombatMeterViewRules.SlotLabel((ECombatMeterSlot)i), EditorStyles.miniBoldLabel);
                DrawFloat(scenario, Key("slot", i, "damage"), "피해", 0.0f, MAX_DAMAGE);
                DrawInt(scenario, Key("slot", i, "hits"), "타수", 0, MAX_COUNT);
                DrawFloat(scenario, Key("slot", i, "maxHit"), "최대 타격", 0.0f, MAX_DAMAGE);
            }

            DrawHeader("결과 창");
            DrawBool(scenario, KEY_HAS_RECORD, "기록 있음");
            DrawBool(scenario, KEY_SELF_DETAIL, "내 기술 내역 표시");

            for (int i = 0; i < PHASE_COUNT; ++i)
            {
                DrawFloat(scenario, Key("phase", i, "seconds"), "단계 " + (i + 1) + " 시간(초)", 0.0f, MAX_SECONDS);
                DrawFloat(scenario, Key("phase", i, "damage"), "단계 " + (i + 1) + " 피해", 0.0f, MAX_DAMAGE);
            }

            for (int i = 0; i < SOURCE_COUNT; ++i)
            {
                DrawString(scenario, Key("taken", i, "source"), "받은 피해 원인 " + (i + 1));
                DrawFloat(scenario, Key("taken", i, "amount"), "받은 피해 양 " + (i + 1), 0.0f, MAX_DAMAGE);
                DrawString(scenario, Key("death", i, "source"), "사망 원인 " + (i + 1));
                DrawFloat(scenario, Key("death", i, "amount"), "사망 피해 " + (i + 1), 0.0f, MAX_DAMAGE);
                DrawFloat(scenario, Key("death", i, "before"), "사망 전 시간(초) " + (i + 1), 0.0f, MAX_SECONDS);
            }
        }

        protected override void OnDetach()
        {
            if (_meter != null)
            {
                _meter.PhaseToggleClicked -= OnPhaseToggleClicked;
                _meter.SelfRowClicked     -= OnMeterSelfRowClicked;
            }

            if (_result != null)
            {
                _result.SelfRowClicked -= OnResultSelfRowClicked;
            }

            _meter      = null;
            _result     = null;
            _subscribed = null;
        }

        private void ApplyMeter(UiSandboxScenarioSO scenario, float seconds, float total)
        {
            _meter.SetWindowVisible(scenario.GetBool(KEY_VISIBLE));
            _meter.SetExpanded(scenario.GetBool(KEY_EXPANDED));
            _meter.SetPhaseToggleLabel(scenario.GetBool(KEY_PHASE_MODE) ? TEXT_PHASE : TEXT_ALL);

            int selfIndex = scenario.GetInt(KEY_SELF_INDEX);
            float selfDamage = scenario.GetFloat(Key("member", Mathf.Clamp(selfIndex, 0, MEMBER_COUNT - 1), "damage"));
            _meter.SetCollapsed(CombatMeterViewRules.FormatDamage(selfDamage), CombatMeterViewRules.FormatDps(selfDamage, seconds));

            int rows = Mathf.Min(MEMBER_COUNT, _meter.PartyRowCount);
            for (int i = 0; i < rows; ++i)
            {
                if (!scenario.GetBool(Key("member", i, "present")))
                {
                    _meter.ClearPartyRow(i);
                    continue;
                }

                float damage = scenario.GetFloat(Key("member", i, "damage"));
                float share  = CombatMeterViewRules.ComputeShare(damage, total);
                _meter.SetPartyRow(i,
                    scenario.GetString(Key("member", i, "name")),
                    CombatMeterViewRules.FormatDamage(damage),
                    CombatMeterViewRules.FormatDps(damage, seconds),
                    CombatMeterViewRules.FormatShare(share),
                    share / 100.0f,
                    i == selfIndex);
            }

            _meter.SetSelfBreakdownVisible(scenario.GetBool(KEY_BREAKDOWN));
            int slots = Mathf.Min(SLOT_COUNT, _meter.SelfSlotCount);
            for (int i = 0; i < slots; ++i)
            {
                _meter.SetSelfSlot(i,
                    CombatMeterViewRules.SlotLabel((ECombatMeterSlot)i),
                    CombatMeterViewRules.FormatDamage(scenario.GetFloat(Key("slot", i, "damage"))),
                    scenario.GetInt(Key("slot", i, "hits")).ToString(),
                    CombatMeterViewRules.FormatDamage(scenario.GetFloat(Key("slot", i, "maxHit"))));
            }
        }

        private void ApplyResult(UiSandboxScenarioSO scenario, float seconds)
        {
            _result.SetHasRecord(scenario.GetBool(KEY_HAS_RECORD));
            int selfIndex = scenario.GetInt(KEY_SELF_INDEX);

            int rows = Mathf.Min(MEMBER_COUNT, _result.RowCount);
            for (int i = 0; i < rows; ++i)
            {
                if (!scenario.GetBool(Key("member", i, "present")))
                {
                    _result.ClearRow(i);
                    continue;
                }

                _result.SetRow(i,
                    scenario.GetString(Key("member", i, "name")),
                    CombatMeterViewRules.FormatDamage(scenario.GetFloat(Key("member", i, "damage"))),
                    CombatMeterViewRules.FormatDamage(scenario.GetFloat(Key("member", i, "taken"))),
                    scenario.GetInt(Key("member", i, "deaths")).ToString(),
                    scenario.GetInt(Key("member", i, "revives")).ToString(),
                    i == selfIndex);
            }

            _result.SetSelfDetailVisible(scenario.GetBool(KEY_SELF_DETAIL));
            int slots = Mathf.Min(SLOT_COUNT, _result.SelfSlotCount);
            for (int i = 0; i < slots; ++i)
            {
                _result.SetSelfSlot(i,
                    CombatMeterViewRules.SlotLabel((ECombatMeterSlot)i),
                    CombatMeterViewRules.FormatDamage(scenario.GetFloat(Key("slot", i, "damage"))),
                    scenario.GetInt(Key("slot", i, "hits")).ToString(),
                    CombatMeterViewRules.FormatDamage(scenario.GetFloat(Key("slot", i, "maxHit"))));
            }

            int phases = Mathf.Min(PHASE_COUNT, _result.PhaseRowCount);
            for (int i = 0; i < phases; ++i)
            {
                float phaseSeconds = scenario.GetFloat(Key("phase", i, "seconds"));
                float phaseDamage  = scenario.GetFloat(Key("phase", i, "damage"));
                _result.SetPhaseRow(i,
                    CombatMeterViewRules.PhaseLabel((byte)(i + 1)),
                    CombatMeterViewRules.FormatSeconds(phaseSeconds),
                    CombatMeterViewRules.FormatDamage(phaseDamage),
                    CombatMeterViewRules.FormatDps(phaseDamage, phaseSeconds));
            }

            int sources = Mathf.Min(SOURCE_COUNT, Mathf.Min(_result.TopDamageTakenCount, _result.DeathRowCount));
            for (int i = 0; i < sources; ++i)
            {
                _result.SetTopDamageTaken(i,
                    scenario.GetString(Key("taken", i, "source")),
                    CombatMeterViewRules.FormatDamage(scenario.GetFloat(Key("taken", i, "amount"))));
                _result.SetDeathRow(i,
                    scenario.GetString(Key("death", i, "source")),
                    CombatMeterViewRules.FormatDeathAmount(
                        scenario.GetFloat(Key("death", i, "amount")),
                        scenario.GetFloat(Key("death", i, "before"))));
            }
        }

        private void OnPhaseToggleClicked()
        {
            ToggleAndApply(KEY_PHASE_MODE);
        }

        private void OnMeterSelfRowClicked()
        {
            ToggleAndApply(KEY_BREAKDOWN);
        }

        private void OnResultSelfRowClicked()
        {
            ToggleAndApply(KEY_SELF_DETAIL);
        }

        private void ToggleAndApply(string key)
        {
            if (_subscribed == null)
            {
                return;
            }

            _subscribed.Scenario.SetBool(key, !_subscribed.Scenario.GetBool(key));
            ApplyIfAttached();
        }

        private void ApplyIfAttached()
        {
            if ((_subscribed != null) && (!_subscribed.IsDisposed))
            {
                OnApply(_subscribed);
            }
        }

        public static string Key(string group, int index, string field)
        {
            return "hud.meter." + group + "." + index + "." + field;
        }
    }
}
