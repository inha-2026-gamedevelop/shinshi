using System.Collections.Generic;
using Shinshi.Common;
using Shinshi.UI;
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 출정 결과 화면. Dev_Result 씬의 ResultScreenView에 결과와 저장 상태를 적용하고 귀환 버튼 이벤트를 모의 요청으로 이어 준다 </summary>
    public sealed class SortieResultPage : PresentationPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "sortie.result";
        public const string SCENE_PATH = "Assets/00.Scenes/Dev_HUD/Dev_Result.unity";

        public const int ITEM_SLOTS = 4;

        public const string KEY_REASON       = "result.reason";
        public const string KEY_REGION       = "result.region";
        public const string KEY_BASE         = "result.baseEssence";
        public const string KEY_EXTRA        = "result.extraEssence";
        public const string KEY_LOST         = "result.lostEssence";
        public const string KEY_CONFIRMED    = "result.confirmed.count";
        public const string KEY_CHANCE       = "result.chance.count";
        public const string KEY_SAVE_STATUS  = "result.saveStatus";
        public const string KEY_RETURN_NOTE  = "result.returnNote";

        public const string OP_SAVE   = "result.save";
        public const string OP_RETURN = "result.return";

        private const int    MAX_ESSENCE   = 99999999;
        private const int    MAX_ITEM_COUNT = 999;
        private const string LABEL_RESULT  = "출정 결과";

        public const string ITEM_SLOT_PREFAB_PATH = "Assets/02.Prefab/UI/PF_ItemSlot.prefab";

        private ResultScreenView _view;
        private UiSandboxContext _subscribed;
        private bool             _savePending;
        private int              _savePrevious;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "출정 결과"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get
            {
                return "Dev_Result 씬의 ResultScreenView에 종료 사유 · 지역 · 정수 · 아이템과 저장 상태를 적용한다. 귀환 버튼은 저장 완료일 때만 눌리며 모의 요청을 보낸다. " +
                       "시험 전용 배치는 확정 · 확률 보상 격자(ItemSlotGridView)에 PF_ItemSlot 프리팹을 칸으로 연결하고 등장 연출용 그룹 · 창도 연결한다. " +
                       "격자의 툴팁은 연결하지 않아 보상 칸 호버 설명은 시험하지 않으며, DOTween 등장 연출은 Play에서만 움직이고 에디터 모드에서는 최종 상태로 보인다. " +
                       "빛(_glow) · 터짐 · 분위기 입자는 연결하지 않는다. " +
                       PresentationNotes.WAVE_NOTE + " " + PresentationNotes.FIXTURE_NOTE;
            }
        }

        public ResultScreenView View
        {
            get { return _view; }
        }

        /****************************************
        *                Methods
        ****************************************/

        protected override void Seed(UiSandboxScenarioSO scenario)
        {
            scenario.SeedInt(KEY_REASON, (int)ESessionEndReason.Success);
            scenario.SeedString(KEY_REGION, "숲 일반전");
            scenario.SeedInt(KEY_BASE, 1200);
            scenario.SeedInt(KEY_EXTRA, 300);
            scenario.SeedInt(KEY_LOST, 0);
            scenario.SeedInt(KEY_CONFIRMED, 2);
            scenario.SeedInt(KEY_CHANCE, 1);
            scenario.SeedInt(KEY_SAVE_STATUS, (int)ESaveStatus.Saved);
            scenario.SeedString(KEY_RETURN_NOTE, "마을로 귀환했습니다 (모의)");

            for (int i = 0; i < ITEM_SLOTS; ++i)
            {
                scenario.SeedInt(ItemKey("confirmed", i, "grade"), i % 4);
                scenario.SeedInt(ItemKey("confirmed", i, "count"), 1 + i);
                scenario.SeedString(ItemKey("confirmed", i, "title"), "확정 아이템 " + (i + 1));
                scenario.SeedInt(ItemKey("chance", i, "grade"), (i + 1) % 4);
                scenario.SeedInt(ItemKey("chance", i, "count"), 1);
                scenario.SeedString(ItemKey("chance", i, "title"), "확률 아이템 " + (i + 1));
            }
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            PresentationFixtureOptions options = new PresentationFixtureOptions(EFixtureAnchor.Center)
                .Skip("_glow")
                .WithAsset("_slotPrefab", AssetDatabase.LoadAssetAtPath<ItemSlotView>(ITEM_SLOT_PREFAB_PATH));
            _view = BindView<ResultScreenView>(context, LABEL_RESULT, options);

            _subscribed = context;
            if (_view != null)
            {
                _view.ReturnClicked += OnReturnClicked;
            }
        }

        protected override void OnApply(UiSandboxContext context)
        {
            if (_view == null)
            {
                return;
            }

            UiSandboxScenarioSO scenario = context.Scenario;
            RestoreCanceledSave(context);
            _view.SetResult(BuildResult(scenario));
            _view.SetSaveStatus((ESaveStatus)scenario.GetInt(KEY_SAVE_STATUS));
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;

            if (RestoreCanceledSave(context))
            {
                MarkDirty();
            }

            DrawHeader("결과");
            DrawEnum(scenario, KEY_REASON, "종료 사유", ESessionEndReason.Success);
            DrawString(scenario, KEY_REGION, "지역 이름");
            DrawInt(scenario, KEY_BASE, "기본 정수", 0, MAX_ESSENCE);
            DrawInt(scenario, KEY_EXTRA, "추가 정수", 0, MAX_ESSENCE);
            DrawInt(scenario, KEY_LOST, "잃은 정수 (0이면 숨김)", 0, MAX_ESSENCE);

            DrawHeader("아이템");
            DrawItems(scenario, "confirmed", KEY_CONFIRMED, "확정");
            DrawItems(scenario, "chance", KEY_CHANCE, "확률");

            DrawHeader("저장");
            DrawEnum(scenario, KEY_SAVE_STATUS, "저장 상태", ESaveStatus.Saved);
            DrawString(scenario, KEY_RETURN_NOTE, "귀환 응답 문구");

            if (GUILayout.Button("저장 시작 (모의 요청)"))
            {
                StartSave(context);
            }
        }

        protected override void OnDetach()
        {
            if (_view != null)
            {
                _view.ReturnClicked -= OnReturnClicked;
            }

            if ((_subscribed != null) && (!_subscribed.IsDisposed))
            {
                RestoreCanceledSave(_subscribed);
            }

            _view        = null;
            _subscribed  = null;
            _savePending = false;
        }

        private void DrawItems(UiSandboxScenarioSO scenario, string group, string countKey, string label)
        {
            int count = DrawInt(scenario, countKey, label + " 개수", 0, ITEM_SLOTS);
            for (int i = 0; i < count; ++i)
            {
                EditorGUILayout.LabelField(label + " " + (i + 1), EditorStyles.miniBoldLabel);
                DrawEnum(scenario, ItemKey(group, i, "grade"), "등급", EItemGrade.Common);
                DrawInt(scenario, ItemKey(group, i, "count"), "수량", 0, MAX_ITEM_COUNT);
                DrawString(scenario, ItemKey(group, i, "title"), "이름");
            }
        }

        private static ResultScreenData BuildResult(UiSandboxScenarioSO scenario)
        {
            return new ResultScreenData(
                (ESessionEndReason)scenario.GetInt(KEY_REASON),
                scenario.GetString(KEY_REGION),
                scenario.GetInt(KEY_BASE),
                scenario.GetInt(KEY_EXTRA),
                scenario.GetInt(KEY_LOST),
                BuildItems(scenario, "confirmed", KEY_CONFIRMED),
                BuildItems(scenario, "chance", KEY_CHANCE));
        }

        private static List<ItemSlotData> BuildItems(UiSandboxScenarioSO scenario, string group, string countKey)
        {
            int count = Mathf.Clamp(scenario.GetInt(countKey), 0, ITEM_SLOTS);
            List<ItemSlotData> items = new List<ItemSlotData>(count);
            for (int i = 0; i < count; ++i)
            {
                items.Add(new ItemSlotData(
                    group + "_" + i,
                    (EItemGrade)scenario.GetInt(ItemKey(group, i, "grade")),
                    scenario.GetInt(ItemKey(group, i, "count")),
                    scenario.GetString(ItemKey(group, i, "title")),
                    string.Empty));
            }

            return items;
        }

        /// <summary> 요청이 실제로 받아들여졌을 때만 Saving을 적용한다. 시작하지 못하면(Play 밖, 다른 요청 진행 중) 저장 상태를 건드리지 않고 false </summary>
        public bool StartSave(UiSandboxContext context)
        {
            if ((context == null) || (!IsAttached))
            {
                return false;
            }

            int previous = context.Scenario.GetInt(KEY_SAVE_STATUS);

            bool started = RunRequest(context, OP_SAVE,
                () =>
                {
                    _savePending = false;
                    context.Scenario.SetInt(KEY_SAVE_STATUS, (int)ESaveStatus.Saved);
                    OnApply(context);
                },
                message =>
                {
                    _savePending = false;
                    context.Scenario.SetInt(KEY_SAVE_STATUS, (int)ESaveStatus.Retrying);
                    OnApply(context);
                });

            if (!started)
            {
                return false;
            }

            _savePending  = true;
            _savePrevious = previous;
            context.Scenario.SetInt(KEY_SAVE_STATUS, (int)ESaveStatus.Saving);
            OnApply(context);
            return true;
        }

        // 취소에는 콜백이 없다. 저장 요청이 더는 진행 중이 아니고 화면이 아직 Saving이면 요청 전 상태로 되돌린다
        private bool RestoreCanceledSave(UiSandboxContext context)
        {
            if ((!_savePending) || ((context.IsBusy) && (context.PendingOperation == OP_SAVE)))
            {
                return false;
            }

            _savePending = false;
            if (context.Scenario.GetInt(KEY_SAVE_STATUS) != (int)ESaveStatus.Saving)
            {
                return false;
            }

            context.Scenario.SetInt(KEY_SAVE_STATUS, _savePrevious);
            return true;
        }

        private void OnReturnClicked()
        {
            UiSandboxContext context = _subscribed;
            if (context == null)
            {
                return;
            }

            RunRequest(context, OP_RETURN, () => context.SetStatus(context.Scenario.GetString(KEY_RETURN_NOTE)));
        }

        public static string ItemKey(string group, int index, string field)
        {
            return "result." + group + "." + index + "." + field;
        }
    }
}
