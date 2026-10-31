// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.Editor
{
    /// <summary> 실제 InventoryView에 편집 가능한 아이템 목록을 넣고, 정리 단추와 칸 클릭을 가짜 서버 응답에 묶는다 </summary>
    public sealed class InventorySandboxPage : EconomyPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID         = "economy_inventory";
        public const string SCENE_PATH      = "Assets/00.Scenes/Release/ShinshiTown.unity";
        public const string KEY_CAPACITY    = "inventory.capacity";
        public const string KEY_YEOPJEON    = "inventory.yeopjeon";
        public const string KEY_ESSENCE     = "inventory.essence";
        public const string KEY_INTERACTION = "inventory.interaction";

        public const string OP_ARRANGE = "인벤토리 정리";
        public const string OP_LOCK    = "아이템 잠금 전환";
        public const string OP_REFRESH = "인벤토리 갱신";

        private const int  DEFAULT_CAPACITY = 24;
        private const int  MAX_CAPACITY     = 200;
        private const int  MAX_ITEMS        = 200;
        private const long DEFAULT_YEOPJEON = 12500L;
        private const long DEFAULT_ESSENCE  = 30L;
        private const long MAX_CURRENCY     = 999999999L;
        private const int  MAX_STACK        = 9999;

        private const string INTERACTION_FIELD = "_enableSlotInteraction";

        private static readonly string[] G_GradeOptions    = { "일반", "희귀", "영웅", "전설" };
        private static readonly string[] G_CategoryOptions = { "장비", "재료", "소비", "부적", "기타" };

        private static readonly EconomyListSchema G_ItemSchema = new EconomyListSchema(
            "inventory.item", "아이템 목록", MAX_ITEMS,
            new[]
            {
                new EconomyField("id", "ID", EEconomyFieldKind.Text, "item.{n}"),
                new EconomyField("title", "이름", EEconomyFieldKind.Text, "아이템 {n}"),
                new EconomyField("description", "설명", EEconomyFieldKind.Text, "시험용 아이템 {n}"),
                new EconomyField("grade", "등급", EEconomyFieldKind.Text, "일반", 0, 0, G_GradeOptions),
                new EconomyField("category", "분류", EEconomyFieldKind.Text, "기타", 0, 0, G_CategoryOptions),
                new EconomyField("count", "수량", EEconomyFieldKind.Int, "1", 1, MAX_STACK),
                new EconomyField("locked", "잠금", EEconomyFieldKind.Bool, "false"),
                new EconomyField("equipped", "장착", EEconomyFieldKind.Bool, "false"),
                new EconomyField("order", "획득 순서", EEconomyFieldKind.Long, "{n}", 0, MAX_CURRENCY)
            },
            new[]
            {
                new[] { "item.sword", "낡은 검", "시험용 장비", "일반", "장비", "1", "false", "true", "1" },
                new[] { "item.herb", "약초", "시험용 재료", "희귀", "재료", "12", "false", "false", "2" },
                new[] { "item.potion", "회복약", "시험용 소비", "영웅", "소비", "5", "true", "false", "3" },
                new[] { "item.charm", "부적", "시험용 부적", "전설", "부적", "1", "false", "false", "4" }
            });

        private static Func<bool> _altHeldProvider;

        private InventoryView    _view;
        private ItemSlotGridView _grid;
        private Button           _arrangeButton;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "인벤토리"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get { return string.Empty; }
        }

        public InventoryView View
        {
            get { return _view; }
        }

        public static EconomyListSchema ItemSchema
        {
            get { return G_ItemSchema; }
        }

        /// <summary> Alt 키 판정을 바꾸는 시험용 자리. null이면 실제 Input을 읽는다 </summary>
        public static Func<bool> AltHeldProvider
        {
            get { return _altHeldProvider; }
            set { _altHeldProvider = value; }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 시나리오의 아이템 목록을 View가 받는 형태로 바꾼다 </summary>
        public static List<InventoryItemData> BuildItems(UiSandboxScenarioSO scenario)
        {
            int count = EconomyList.Count(scenario, G_ItemSchema);
            List<InventoryItemData> items = new List<InventoryItemData>(count);
            for (int i = 0; i < count; ++i)
            {
                int gradeIndex = Array.IndexOf(G_GradeOptions, EconomyList.GetText(scenario, G_ItemSchema, i, "grade"));
                EItemGrade grade = (EItemGrade)Mathf.Clamp(gradeIndex, 0, G_GradeOptions.Length - 1);
                int categoryIndex = Array.IndexOf(G_CategoryOptions, EconomyList.GetText(scenario, G_ItemSchema, i, "category"));
                EInventoryCategory category = (categoryIndex < 0) ? EInventoryCategory.Etc : (EInventoryCategory)(categoryIndex + 1);

                ItemSlotData slot = new ItemSlotData(
                    EconomyList.GetText(scenario, G_ItemSchema, i, "id"),
                    grade,
                    Mathf.Max(1, EconomyList.GetInt(scenario, G_ItemSchema, i, "count")),
                    EconomyList.GetText(scenario, G_ItemSchema, i, "title"),
                    EconomyList.GetText(scenario, G_ItemSchema, i, "description"),
                    EconomyList.GetBool(scenario, G_ItemSchema, i, "locked"),
                    EconomyList.GetBool(scenario, G_ItemSchema, i, "equipped"));
                items.Add(new InventoryItemData(slot, category, EconomyList.GetLong(scenario, G_ItemSchema, i, "order")));
            }

            return items;
        }

        protected override void SeedScenario(UiSandboxScenarioSO scenario)
        {
            EconomyScenarioValues.SeedInt(scenario, KEY_CAPACITY, DEFAULT_CAPACITY);
            EconomyScenarioValues.SeedLong(scenario, KEY_YEOPJEON, DEFAULT_YEOPJEON);
            EconomyScenarioValues.SeedLong(scenario, KEY_ESSENCE, DEFAULT_ESSENCE);
            EconomyScenarioValues.SeedBool(scenario, KEY_INTERACTION, true);
            EconomyList.Seed(scenario, G_ItemSchema);
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            _view = context.Find<InventoryView>();
            if (_view == null)
            {
                context.SetStatus("InventoryView를 찾지 못했다. 원본 씬 사본을 연 뒤 다시 선택한다");
                return;
            }

            _grid          = EconomyUiBuilder.GetRef<ItemSlotGridView>(_view, "_grid");
            _arrangeButton = EconomyUiBuilder.GetRef<Button>(_view, "_arrangeButton");
            if (_arrangeButton != null)
            {
                _arrangeButton.onClick.AddListener(OnArrangeClicked);
            }

            if (_grid != null)
            {
                _grid.SlotClicked += OnSlotClicked;
            }
        }

        protected override void OnApply(UiSandboxContext context)
        {
            ApplyView(context.Scenario);
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;
            EconomyScenarioValues.DrawInt(scenario, KEY_CAPACITY, "칸 수", 0, MAX_CAPACITY);
            EconomyScenarioValues.DrawLong(scenario, KEY_YEOPJEON, "엽전", 0L, MAX_CURRENCY);
            EconomyScenarioValues.DrawLong(scenario, KEY_ESSENCE, "정수", 0L, MAX_CURRENCY);
            EconomyScenarioValues.DrawBool(scenario, KEY_INTERACTION, "칸 이동 · 잠금 상호작용 켜기 (시험용)");
            EconomyList.Draw(scenario, G_ItemSchema);

            EditorGUILayout.Space();
            if (GUILayout.Button("편집 값을 화면에 다시 적용"))
            {
                Apply(context);
            }

            if ((_view != null) && (!_view.IsOpen) && (GUILayout.Button("창 다시 열기")))
            {
                ReopenView();
            }

            if (DrawRequestButton(context, "서버 갱신 요청 (가짜)"))
            {
                Send(OP_REFRESH, () => ApplyView(context.Scenario), reason => ReportFailure(OP_REFRESH, reason));
            }
        }

        /// <summary> 닫기 단추로 닫힌 창을 실제 SetOpen(true)로 다시 열고 편집 값을 다시 적용한다. 열 수 없으면 거짓 </summary>
        public bool ReopenView()
        {
            if ((_view == null) || (Context == null))
            {
                return false;
            }

            _view.SetOpen(true);
            ApplyView(Context.Scenario);
            return true;
        }

        protected override void OnDetach()
        {
            if (_arrangeButton != null)
            {
                _arrangeButton.onClick.RemoveListener(OnArrangeClicked);
            }

            if (_grid != null)
            {
                _grid.SlotClicked -= OnSlotClicked;
            }

            _view          = null;
            _grid          = null;
            _arrangeButton = null;
        }

        private void ApplyView(UiSandboxScenarioSO scenario)
        {
            if (_view == null)
            {
                return;
            }

            SetInteraction(scenario.GetBool(KEY_INTERACTION, true));
            _view.SetItems(BuildItems(scenario), scenario.GetInt(KEY_CAPACITY, DEFAULT_CAPACITY));
            _view.SetCurrency(scenario.GetLong(KEY_YEOPJEON, DEFAULT_YEOPJEON), scenario.GetLong(KEY_ESSENCE, DEFAULT_ESSENCE));
        }

        // 사본 View의 직렬화 값만 바꾼다. 원본 씬과 프리팹은 건드리지 않는다
        private void SetInteraction(bool enabled)
        {
            SerializedObject serialized = new SerializedObject(_view);
            SerializedProperty property = serialized.FindProperty(INTERACTION_FIELD);
            if ((property == null) || (property.boolValue == enabled))
            {
                return;
            }

            property.boolValue = enabled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // 실제 정리 단추는 화면에서 바로 정리한다. 서버 확정을 받지 못하면 시나리오 값으로 되돌린다.
        // View의 Arrange가 이 구독보다 나중에 걸렸을 수 있어(창이 꺼진 채 시작해 Awake가 늦은 경우) 막힘은 한 번 더 늦춰 되돌린다
        private void OnArrangeClicked()
        {
            UiSandboxContext context = Context;
            if (context == null)
            {
                return;
            }

            bool started = Send(OP_ARRANGE, () => ReportSuccess(OP_ARRANGE), reason =>
            {
                ReportFailure(OP_ARRANGE, reason);
                ApplyView(Context.Scenario);
            });
            if (!started)
            {
                RevertView(context);
            }
        }

        // 일반 클릭은 실제 View가 선택만 처리한다. Alt+클릭은 실제 InventorySlotInteraction이 화면의 잠금을 바로 바꾸므로,
        // 여기서는 다시 뒤집지 않고 같은 결과를 서버가 확정하는 가짜 요청만 보낸다. 거절 · 취소 · 막힘이면 시나리오 값으로 되돌린다
        private void OnSlotClicked(ItemSlotView slot)
        {
            UiSandboxContext context = Context;
            if ((context == null) || (slot == null) || (!AltHeld()) || (!context.Scenario.GetBool(KEY_INTERACTION, true)) || (slot.Data.IsEmpty))
            {
                return;
            }

            string itemId = slot.Data.Id;
            int row = IndexOfItemId(context.Scenario, itemId);
            if (row < 0)
            {
                return;
            }

            bool wantLocked = !EconomyList.GetBool(context.Scenario, G_ItemSchema, row, "locked");
            bool started = Send(OP_LOCK, () => CompleteLock(itemId, wantLocked), reason =>
            {
                ReportFailure(OP_LOCK, reason);
                ApplyView(Context.Scenario);
            });
            if (!started)
            {
                RevertView(context);
            }
        }

        private void CompleteLock(string itemId, bool wantLocked)
        {
            UiSandboxContext context = Context;
            if (context == null)
            {
                return;
            }

            int row = IndexOfItemId(context.Scenario, itemId);
            if (row < 0)
            {
                ReportFailure(OP_LOCK, "아이템이 이미 바뀌었다");
                ApplyView(context.Scenario);
                return;
            }

            EconomyList.SetBool(context.Scenario, G_ItemSchema, row, "locked", wantLocked);
            ApplyView(context.Scenario);
            ReportSuccess(OP_LOCK);
        }

        // 실제 상호작용 컴포넌트가 이 핸들러보다 늦게 도는 순서일 수 있어 한 번 더 미룬 뒤에도 되돌린다
        private void RevertView(UiSandboxContext context)
        {
            ApplyView(context.Scenario);
            int session = Session;
            EditorApplication.delayCall += () =>
            {
                if ((IsAttached) && (Session == session))
                {
                    ApplyView(Context.Scenario);
                }
            };
        }

        private static int IndexOfItemId(UiSandboxScenarioSO scenario, string itemId)
        {
            int count = EconomyList.Count(scenario, G_ItemSchema);
            for (int i = 0; i < count; ++i)
            {
                if (string.Equals(EconomyList.GetText(scenario, G_ItemSchema, i, "id"), itemId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool AltHeld()
        {
            Func<bool> provider = _altHeldProvider;
            return (provider != null) ? provider() : (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
        }
    }
}
