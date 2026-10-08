using Shinshi.Common;
using Shinshi.UI;
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 물약 · 보스 · 웨이브 · 부활 상태 · 거절 피드백 화면. Dev_CombatHud 씬의 실제 View에 값을 적용한다 </summary>
    public sealed class HudCombatPage : PresentationPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "hud.combat";
        public const string SCENE_PATH = "Assets/00.Scenes/Dev_HUD/Dev_CombatHud.unity";

        public const string KEY_POTION_SMALL    = "hud.combat.potion.small";
        public const string KEY_POTION_LARGE    = "hud.combat.potion.large";
        public const string KEY_POTION_CD       = "hud.combat.potion.cooldown";
        public const string KEY_POTION_CD_TOTAL = "hud.combat.potion.cooldownTotal";
        public const string KEY_BOSS_NAME       = "hud.combat.boss.name";
        public const string KEY_BOSS_HP         = "hud.combat.boss.hpRatio";
        public const string KEY_BOSS_PHASE      = "hud.combat.boss.phase";
        public const string KEY_BOSS_PHASE_MAX  = "hud.combat.boss.phaseTotal";
        public const string KEY_BOSS_REVIVES    = "hud.combat.boss.revives";
        public const string KEY_BOSS_REVIVE_MAX = "hud.combat.boss.revivesMax";
        public const string KEY_BOSS_POTION     = "hud.combat.boss.potionUsed";
        public const string KEY_BOSS_POTION_MAX = "hud.combat.boss.potionMax";
        public const string KEY_WAVE            = "hud.combat.wave.current";
        public const string KEY_WAVE_TOTAL      = "hud.combat.wave.total";
        public const string KEY_WAVE_TIME       = "hud.combat.wave.timeSeconds";
        public const string KEY_WAVE_ENEMIES    = "hud.combat.wave.enemies";
        public const string KEY_WAVE_INTERMIT   = "hud.combat.wave.intermission";
        public const string KEY_LIFE_STATE      = "hud.combat.life.state";
        public const string KEY_LIFE_WAIT       = "hud.combat.life.waitSeconds";
        public const string KEY_LIFE_REVIVES    = "hud.combat.life.sharedRevives";
        public const string KEY_REJECT_REASON   = "hud.combat.reject.reason";
        public const string KEY_REJECT_RES      = "hud.combat.reject.usesResonance";

        public const string OP_POTION = "hud.combat.usePotion";

        private const int   DEFAULT_WAVE_TOTAL  = 6;
        private const float DEFAULT_TIME_LIMIT  = 420.0f;
        private const int   DEFAULT_ENEMIES     = 20;
        private const int   DEFAULT_REVIVES_MAX = 3;
        private const int   DEFAULT_POTION_MAX  = 9;
        private const float DEFAULT_POTION_CD   = 3.0f;
        private const int   POTION_VIEW_SLOTS   = 2;
        private const int   MAX_COUNT           = 9999;
        private const float MAX_SECONDS         = 3600.0f;
        private const string LABEL_POTION       = "물약";
        private const string LABEL_BOSS         = "보스";
        private const string LABEL_WAVE         = "웨이브";
        private const string LABEL_REVIVE       = "부활 상태";
        private const string LABEL_REJECT       = "거절 피드백";

        private HudPotionView          _potion;
        private HudBossView            _boss;
        private HudWaveView            _wave;
        private HudReviveStatusView    _revive;
        private HudRejectFeedbackView  _reject;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "HUD 전투 (물약 · 보스 · 웨이브 · 부활 · 거절)"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get
            {
                return "Dev_CombatHud 씬의 물약 · 보스 · 웨이브 · 부활 · 거절 View에 값을 적용한다. " +
                       PresentationNotes.WAVE_NOTE + " " + PresentationNotes.BOSS_NOTE + " " +
                       "거절 피드백의 토스트 연출은 Play Mode에서만 보인다. " + PresentationNotes.FIXTURE_NOTE;
            }
        }

        public HudPotionView PotionView
        {
            get { return _potion; }
        }

        public HudBossView BossView
        {
            get { return _boss; }
        }

        public HudWaveView WaveView
        {
            get { return _wave; }
        }

        public HudReviveStatusView ReviveView
        {
            get { return _revive; }
        }

        public HudRejectFeedbackView RejectView
        {
            get { return _reject; }
        }

        /****************************************
        *                Methods
        ****************************************/

        protected override void Seed(UiSandboxScenarioSO scenario)
        {
            scenario.SeedInt(KEY_POTION_SMALL, 5);
            scenario.SeedInt(KEY_POTION_LARGE, 2);
            scenario.SeedFloat(KEY_POTION_CD, 0.0f);
            scenario.SeedFloat(KEY_POTION_CD_TOTAL, DEFAULT_POTION_CD);

            scenario.SeedString(KEY_BOSS_NAME, "보스");
            scenario.SeedFloat(KEY_BOSS_HP, 0.75f);
            scenario.SeedInt(KEY_BOSS_PHASE, 1);
            scenario.SeedInt(KEY_BOSS_PHASE_MAX, 2);
            scenario.SeedInt(KEY_BOSS_REVIVES, DEFAULT_REVIVES_MAX);
            scenario.SeedInt(KEY_BOSS_REVIVE_MAX, DEFAULT_REVIVES_MAX);
            scenario.SeedInt(KEY_BOSS_POTION, 0);
            scenario.SeedInt(KEY_BOSS_POTION_MAX, DEFAULT_POTION_MAX);

            scenario.SeedInt(KEY_WAVE, 1);
            scenario.SeedInt(KEY_WAVE_TOTAL, DEFAULT_WAVE_TOTAL);
            scenario.SeedFloat(KEY_WAVE_TIME, DEFAULT_TIME_LIMIT);
            scenario.SeedInt(KEY_WAVE_ENEMIES, DEFAULT_ENEMIES);
            scenario.SeedFloat(KEY_WAVE_INTERMIT, 0.0f);

            scenario.SeedInt(KEY_LIFE_STATE, (int)EParticipantLifeState.Alive);
            scenario.SeedFloat(KEY_LIFE_WAIT, 0.0f);
            scenario.SeedInt(KEY_LIFE_REVIVES, DEFAULT_REVIVES_MAX);

            scenario.SeedInt(KEY_REJECT_REASON, (int)ERejectReason.Cooldown);
            scenario.SeedBool(KEY_REJECT_RES, false);
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            PresentationFixtureOptions potionOptions = new PresentationFixtureOptions(EFixtureAnchor.BottomRight)
                .WithOffset(-40.0f, 40.0f)
                .WithLength("_countLabels", POTION_VIEW_SLOTS)
                .WithLength("_groups", POTION_VIEW_SLOTS)
                .WithLength("_slots", POTION_VIEW_SLOTS);

            _potion = BindView<HudPotionView>(context, LABEL_POTION, potionOptions);
            _boss   = BindView<HudBossView>(context, LABEL_BOSS,
                new PresentationFixtureOptions(EFixtureAnchor.TopCenter).WithOffset(0.0f, -120.0f));
            _wave   = BindView<HudWaveView>(context, LABEL_WAVE,
                new PresentationFixtureOptions(EFixtureAnchor.TopCenter).WithOffset(0.0f, -40.0f));
            _revive = BindView<HudReviveStatusView>(context, LABEL_REVIVE,
                new PresentationFixtureOptions(EFixtureAnchor.Center));
            _reject = BindView<HudRejectFeedbackView>(context, LABEL_REJECT,
                new PresentationFixtureOptions(EFixtureAnchor.BottomCenter).WithOffset(0.0f, 200.0f));
        }

        protected override void OnApply(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;

            if (_potion != null)
            {
                _potion.SetCount(EPotionSize.Small, scenario.GetInt(KEY_POTION_SMALL));
                _potion.SetCount(EPotionSize.Large, scenario.GetInt(KEY_POTION_LARGE));
                _potion.SetSharedCooldown(scenario.GetFloat(KEY_POTION_CD), scenario.GetFloat(KEY_POTION_CD_TOTAL));
            }

            if (_boss != null)
            {
                _boss.SetBossName(scenario.GetString(KEY_BOSS_NAME));
                _boss.SetHealth(scenario.GetFloat(KEY_BOSS_HP));
                _boss.SetPhase(scenario.GetInt(KEY_BOSS_PHASE), scenario.GetInt(KEY_BOSS_PHASE_MAX));
                _boss.SetSharedRevives(scenario.GetInt(KEY_BOSS_REVIVES), scenario.GetInt(KEY_BOSS_REVIVE_MAX));
                _boss.SetPotionUse(scenario.GetInt(KEY_BOSS_POTION), scenario.GetInt(KEY_BOSS_POTION_MAX));
            }

            if (_wave != null)
            {
                _wave.SetWave(scenario.GetInt(KEY_WAVE), scenario.GetInt(KEY_WAVE_TOTAL));
                _wave.SetTimeRemaining(scenario.GetFloat(KEY_WAVE_TIME));
                _wave.SetEnemiesRemaining(scenario.GetInt(KEY_WAVE_ENEMIES));
                _wave.SetIntermission(scenario.GetFloat(KEY_WAVE_INTERMIT));
            }

            if (_revive != null)
            {
                EParticipantLifeState state = (EParticipantLifeState)scenario.GetInt(KEY_LIFE_STATE);
                _revive.SetState(state, scenario.GetFloat(KEY_LIFE_WAIT), scenario.GetInt(KEY_LIFE_REVIVES));
            }
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;

            DrawHeader(LABEL_POTION);
            DrawInt(scenario, KEY_POTION_SMALL, "소형 개수", 0, MAX_COUNT);
            DrawInt(scenario, KEY_POTION_LARGE, "대형 개수", 0, MAX_COUNT);
            DrawFloat(scenario, KEY_POTION_CD, "공용 쿨다운 남은(초)", 0.0f, MAX_SECONDS);
            DrawFloat(scenario, KEY_POTION_CD_TOTAL, "공용 쿨다운 전체(초)", 0.0f, MAX_SECONDS);

            if (GUILayout.Button("물약 사용 (모의 요청)"))
            {
                RequestPotion(context);
            }

            DrawHeader(LABEL_BOSS);
            DrawString(scenario, KEY_BOSS_NAME, "이름");
            DrawFloat(scenario, KEY_BOSS_HP, "체력 비율 0~1", 0.0f, 1.0f);
            DrawInt(scenario, KEY_BOSS_PHASE, "현재 페이즈", 0, MAX_COUNT);
            DrawInt(scenario, KEY_BOSS_PHASE_MAX, "전체 페이즈", 0, MAX_COUNT);
            DrawInt(scenario, KEY_BOSS_REVIVES, "공유 부활 남은", 0, MAX_COUNT);
            DrawInt(scenario, KEY_BOSS_REVIVE_MAX, "공유 부활 최대", 0, MAX_COUNT);
            DrawInt(scenario, KEY_BOSS_POTION, "물약 사용", 0, MAX_COUNT);
            DrawInt(scenario, KEY_BOSS_POTION_MAX, "물약 최대", 0, MAX_COUNT);

            DrawHeader(LABEL_WAVE);
            DrawInt(scenario, KEY_WAVE, "현재 웨이브", 0, MAX_COUNT);
            DrawInt(scenario, KEY_WAVE_TOTAL, "전체 웨이브", 0, MAX_COUNT);
            DrawFloat(scenario, KEY_WAVE_TIME, "남은 시간(초)", 0.0f, MAX_SECONDS);
            DrawInt(scenario, KEY_WAVE_ENEMIES, "남은 적", 0, MAX_COUNT);
            DrawFloat(scenario, KEY_WAVE_INTERMIT, "휴식(초, 0이면 숨김)", 0.0f, MAX_SECONDS);

            DrawHeader(LABEL_REVIVE);
            DrawEnum(scenario, KEY_LIFE_STATE, "상태", EParticipantLifeState.Alive);
            DrawFloat(scenario, KEY_LIFE_WAIT, "부활 대기(초)", 0.0f, MAX_SECONDS);
            DrawInt(scenario, KEY_LIFE_REVIVES, "공유 부활 남은", 0, MAX_COUNT);

            DrawHeader(LABEL_REJECT);
            DrawEnum(scenario, KEY_REJECT_REASON, "사유", ERejectReason.Cooldown);
            DrawBool(scenario, KEY_REJECT_RES, "공명 사용 중 표시");
            if (GUILayout.Button("거절 피드백 표시"))
            {
                ShowReject(scenario);
            }
        }

        protected override void OnDetach()
        {
            _potion = null;
            _boss   = null;
            _wave   = null;
            _revive = null;
            _reject = null;
        }

        private void RequestPotion(UiSandboxContext context)
        {
            RunRequest(context, OP_POTION,
                () =>
                {
                    UiSandboxScenarioSO scenario = context.Scenario;
                    int small = scenario.GetInt(KEY_POTION_SMALL);
                    scenario.SetInt(KEY_POTION_SMALL, Mathf.Max(0, small - 1));
                    scenario.SetFloat(KEY_POTION_CD, scenario.GetFloat(KEY_POTION_CD_TOTAL));
                    scenario.SetInt(KEY_BOSS_POTION, scenario.GetInt(KEY_BOSS_POTION) + 1);
                    OnApply(context);
                },
                message =>
                {
                    ShowReject(context.Scenario);
                });
        }

        private void ShowReject(UiSandboxScenarioSO scenario)
        {
            if (_reject == null)
            {
                return;
            }

            ERejectReason reason = (ERejectReason)scenario.GetInt(KEY_REJECT_REASON);
            _reject.Show(reason, scenario.GetBool(KEY_REJECT_RES));
        }
    }
}
