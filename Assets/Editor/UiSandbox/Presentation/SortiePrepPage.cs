using System.Collections.Generic;
using Shinshi.Common;
using Shinshi.UI;
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 출정 준비 화면. Dev_SortiePrep 씬의 SortiePrepView에 값을 적용하고 View의 실제 이벤트를 모의 요청으로 이어 준다 </summary>
    public sealed class SortiePrepPage : PresentationPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "sortie.prep";
        public const string SCENE_PATH = "Assets/00.Scenes/Release/ShinshiTown.unity";

        public const int MEMBER_COUNT = 4;

        public const string KEY_DUNGEON_NAME    = "sortie.dungeon.name";
        public const string KEY_DUNGEON_SUMMARY = "sortie.dungeon.summary";
        public const string KEY_DIFFICULTY      = "sortie.difficulty";
        public const string KEY_LOCAL_LEADER    = "sortie.local.isLeader";
        public const string KEY_LOCAL_READY     = "sortie.local.isReady";
        public const string KEY_START_ENABLED   = "sortie.startEnabled";
        public const string KEY_COUNTDOWN       = "sortie.countdown";
        public const string KEY_NOTICE_TEXT     = "sortie.notice.text";
        public const string KEY_NOTICE_REASON   = "sortie.notice.reason";
        public const string KEY_NOTICE_USE_CODE = "sortie.notice.useReason";

        public const string OP_READY      = "sortie.ready";
        public const string OP_START      = "sortie.start";
        public const string OP_LEAVE      = "sortie.leave";
        public const string OP_DIFFICULTY = "sortie.difficulty";

        private const int    MAX_LEVEL     = 30;
        private const int    MAX_POWER     = 9999999;
        private const int    MAX_COUNTDOWN = 99;
        private const int    DIFFICULTY_DOTS = 2;
        private const string LABEL_SORTIE  = "출정 준비";

        private static readonly string[]       G_DefaultNames   = { "방장", "동료A", "동료B", "" };
        private static readonly EPlayerClass[] G_DefaultClasses =
        {
            EPlayerClass.Swordsman, EPlayerClass.Mageknight, EPlayerClass.Archer, EPlayerClass.Warrior
        };

        private SortiePrepView   _view;
        private UiSandboxContext _subscribed;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "출정 준비"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get
            {
                return "Dev_SortiePrep 씬의 SortiePrepView에 던전 · 난이도 · 파티원 4칸 · 방장/준비 상태 · 안내문을 적용한다. " +
                       "준비/출정/나가기/난이도 변경 버튼은 View의 실제 이벤트를 구독해 모의 요청으로 보내고, 실패 응답은 안내문에 표시한다(Play Mode). " +
                       "SortiePrepDemo의 레벨 32/30/28은 오래된 값이며 현행 레벨 상한은 30이다. " +
                       PresentationNotes.CLASS_NOTE + " " + PresentationNotes.LEVEL_NOTE + " " + PresentationNotes.FIXTURE_NOTE;
            }
        }

        public SortiePrepView View
        {
            get { return _view; }
        }

        /****************************************
        *                Methods
        ****************************************/

        protected override void Seed(UiSandboxScenarioSO scenario)
        {
            scenario.SeedString(KEY_DUNGEON_NAME, "숲 일반전");
            scenario.SeedString(KEY_DUNGEON_SUMMARY, "6웨이브 · 제한 420초");
            scenario.SeedInt(KEY_DIFFICULTY, (int)EBossDifficulty.Easy);
            scenario.SeedBool(KEY_LOCAL_LEADER, false);
            scenario.SeedBool(KEY_LOCAL_READY, false);
            scenario.SeedBool(KEY_START_ENABLED, true);
            scenario.SeedInt(KEY_COUNTDOWN, 0);
            scenario.SeedString(KEY_NOTICE_TEXT, "모든 파티원이 준비해야 출정할 수 있습니다");
            scenario.SeedInt(KEY_NOTICE_REASON, (int)ERejectReason.NotAllReady);
            scenario.SeedBool(KEY_NOTICE_USE_CODE, false);

            for (int i = 0; i < MEMBER_COUNT; ++i)
            {
                scenario.SeedString(Key(i, "name"), G_DefaultNames[i]);
                scenario.SeedInt(Key(i, "class"), (int)G_DefaultClasses[i]);
                scenario.SeedBool(Key(i, "leader"), i == 0);
                scenario.SeedBool(Key(i, "ready"), i == 1);
                scenario.SeedBool(Key(i, "connected"), true);
                scenario.SeedBool(Key(i, "meets"), true);
                scenario.SeedInt(Key(i, "level"), MAX_LEVEL - (i * 2));
                scenario.SeedInt(Key(i, "power"), 12000 - (i * 1500));
            }
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions(EFixtureAnchor.Center)
                .WithLength("_members", MEMBER_COUNT)
                .WithLength("_difficultyDots", DIFFICULTY_DOTS);
            _view = BindView<SortiePrepView>(context, LABEL_SORTIE, options);

            _subscribed = context;
            if (_view != null)
            {
                _view.ReadyClicked      += OnReadyClicked;
                _view.StartClicked      += OnStartClicked;
                _view.LeaveClicked      += OnLeaveClicked;
                _view.DifficultyChanged += OnDifficultyChanged;
            }
        }

        protected override void OnApply(UiSandboxContext context)
        {
            if (_view == null)
            {
                return;
            }

            UiSandboxScenarioSO scenario = context.Scenario;
            _view.SetDungeon(scenario.GetString(KEY_DUNGEON_NAME), scenario.GetString(KEY_DUNGEON_SUMMARY), null);
            _view.SetDifficulty((EBossDifficulty)scenario.GetInt(KEY_DIFFICULTY));
            _view.SetMembers(BuildMembers(scenario));
            _view.SetLocalState(scenario.GetBool(KEY_LOCAL_LEADER), scenario.GetBool(KEY_LOCAL_READY));
            _view.SetStartEnabled(scenario.GetBool(KEY_START_ENABLED));
            _view.SetCountdown(scenario.GetInt(KEY_COUNTDOWN));

            if (scenario.GetBool(KEY_NOTICE_USE_CODE))
            {
                _view.SetNotice((ERejectReason)scenario.GetInt(KEY_NOTICE_REASON));
            }
            else
            {
                _view.SetNotice(scenario.GetString(KEY_NOTICE_TEXT));
            }
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;

            DrawHeader("던전");
            DrawString(scenario, KEY_DUNGEON_NAME, "이름");
            DrawString(scenario, KEY_DUNGEON_SUMMARY, "요약");
            DrawEnum(scenario, KEY_DIFFICULTY, "난이도", EBossDifficulty.Easy);

            DrawHeader("내 상태");
            DrawBool(scenario, KEY_LOCAL_LEADER, "내가 방장");
            DrawBool(scenario, KEY_LOCAL_READY, "내가 준비 완료");
            DrawBool(scenario, KEY_START_ENABLED, "출정 버튼 활성");
            DrawInt(scenario, KEY_COUNTDOWN, "카운트다운(0이면 출정 글자)", 0, MAX_COUNTDOWN);

            DrawHeader("안내문");
            DrawBool(scenario, KEY_NOTICE_USE_CODE, "거절 사유 코드로 표시");
            DrawString(scenario, KEY_NOTICE_TEXT, "직접 문구");
            DrawEnum(scenario, KEY_NOTICE_REASON, "거절 사유", ERejectReason.NotAllReady);

            for (int i = 0; i < MEMBER_COUNT; ++i)
            {
                DrawHeader("파티원 " + (i + 1) + " (이름을 비우면 빈 칸)");
                DrawString(scenario, Key(i, "name"), "이름");
                DrawEnum(scenario, Key(i, "class"), "직업", EPlayerClass.Swordsman);
                DrawBool(scenario, Key(i, "leader"), "방장");
                DrawBool(scenario, Key(i, "ready"), "준비");
                DrawBool(scenario, Key(i, "connected"), "접속 중");
                DrawBool(scenario, Key(i, "meets"), "입장 조건 충족");
                DrawInt(scenario, Key(i, "level"), "레벨(0이면 ?)", 0, MAX_LEVEL);
                DrawInt(scenario, Key(i, "power"), "전투력(0이면 ?)", 0, MAX_POWER);
            }

            if (context.IsPlaying)
            {
                EditorGUILayout.HelpBox("화면의 준비/출정/나가기/난이도 버튼을 눌러 모의 요청을 보낸다", MessageType.None);
            }
        }

        protected override void OnDetach()
        {
            if (_view != null)
            {
                _view.ReadyClicked      -= OnReadyClicked;
                _view.StartClicked      -= OnStartClicked;
                _view.LeaveClicked      -= OnLeaveClicked;
                _view.DifficultyChanged -= OnDifficultyChanged;
            }

            _view       = null;
            _subscribed = null;
        }

        private static List<SortieMemberData> BuildMembers(UiSandboxScenarioSO scenario)
        {
            List<SortieMemberData> members = new List<SortieMemberData>(MEMBER_COUNT);
            for (int i = 0; i < MEMBER_COUNT; ++i)
            {
                members.Add(new SortieMemberData(
                    scenario.GetString(Key(i, "name")),
                    (EPlayerClass)scenario.GetInt(Key(i, "class")),
                    scenario.GetBool(Key(i, "leader")),
                    scenario.GetBool(Key(i, "ready")),
                    scenario.GetBool(Key(i, "connected")),
                    scenario.GetBool(Key(i, "meets")),
                    scenario.GetInt(Key(i, "level")),
                    scenario.GetInt(Key(i, "power"))));
            }

            return members;
        }

        private void OnReadyClicked()
        {
            Send(OP_READY, scenario =>
            {
                scenario.SetBool(KEY_LOCAL_READY, !scenario.GetBool(KEY_LOCAL_READY));
                scenario.SetString(KEY_NOTICE_TEXT, "준비 상태가 바뀌었습니다 (모의)");
                scenario.SetBool(KEY_NOTICE_USE_CODE, false);
            });
        }

        private void OnStartClicked()
        {
            Send(OP_START, scenario =>
            {
                scenario.SetInt(KEY_COUNTDOWN, 3);
                scenario.SetString(KEY_NOTICE_TEXT, "출정 카운트다운을 시작합니다 (모의)");
                scenario.SetBool(KEY_NOTICE_USE_CODE, false);
            });
        }

        private void OnLeaveClicked()
        {
            Send(OP_LEAVE, scenario =>
            {
                scenario.SetString(KEY_NOTICE_TEXT, "방에서 나갔습니다 (모의)");
                scenario.SetBool(KEY_NOTICE_USE_CODE, false);
            });
        }

        private void OnDifficultyChanged(EBossDifficulty difficulty)
        {
            Send(OP_DIFFICULTY, scenario =>
            {
                scenario.SetInt(KEY_DIFFICULTY, (int)difficulty);
                scenario.SetBool(KEY_LOCAL_READY, false);
                for (int i = 0; i < MEMBER_COUNT; ++i)
                {
                    if (!scenario.GetBool(Key(i, "leader")))
                    {
                        scenario.SetBool(Key(i, "ready"), false);
                    }
                }
            });
        }

        private void Send(string operation, System.Action<UiSandboxScenarioSO> onSuccess)
        {
            UiSandboxContext context = _subscribed;
            if (context == null)
            {
                return;
            }

            RunRequest(context, operation,
                () =>
                {
                    onSuccess(context.Scenario);
                    OnApply(context);
                },
                message =>
                {
                    context.Scenario.SetString(KEY_NOTICE_TEXT, message);
                    context.Scenario.SetBool(KEY_NOTICE_USE_CODE, false);
                    OnApply(context);
                });
        }

        public static string Key(int index, string field)
        {
            return "sortie.member." + index + "." + field;
        }
    }
}
