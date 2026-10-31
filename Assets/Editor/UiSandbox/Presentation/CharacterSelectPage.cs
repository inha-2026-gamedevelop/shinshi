using System;
using System.Reflection;
using Shinshi.Common;
using Shinshi.UI;
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary>
    /// 캐릭터 선택 화면. 네트워크와 카메라에 묶이지 않은 CharacterSelectSelection(고른 칸 · 올린 칸)만 실제로 구동하고,
    /// 칸 목록(이름 · 직업 · 레벨)은 계약 입력으로 편집해 보여 준다. 나머지 CharacterSelect* View는 쓰지 않는다
    /// </summary>
    public sealed class CharacterSelectPage : PresentationPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "lobby.charselect";
        public const string SCENE_PATH = "Assets/00.Scenes/Release/CharacterSelect.unity";

        public const int SLOT_COUNT = 4;

        public const string KEY_SELECTED = "lobby.charselect.selected";
        public const string KEY_HOVERED  = "lobby.charselect.hovered";

        public const string OP_ENTER  = "lobby.charselect.enter";
        public const string OP_DELETE = "lobby.charselect.delete";

        private const string FIELD_SELECTED         = "_selectedSlot";
        private const string FIELD_HOVERED          = "_hoveredSlot";
        private const string EVENT_SELECTION_CHANGED = "SelectionChanged";
        private const string EVENT_HOVER_CHANGED     = "HoverChanged";

        private const int    MAX_LEVEL       = 99;
        private const string LABEL_SELECTION = "캐릭터 선택";
        private const string EMPTY_NAME      = "";
        private const string NOT_SELECTED    = "선택 없음";

        private CharacterSelectSelection _selection;
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
            get { return "캐릭터 선택 (선택 칸만 실제 View)"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get
            {
                return "CharacterSelectSelection의 고른 칸 · 올린 칸과 SelectionChanged · HoverChanged 이벤트만 실제 View로 구동한다. " +
                       "CharacterSelectSlotClick(포인터 클릭 · 진입 · 이탈)은 Selection과 EventSystem 레이캐스트를 거쳐야 하고, CharacterSelectListHighlight(선택 테두리)는 Image 배열 · DOTween 페이드 · 입자 방출기 연결이 필요하며, " +
                       "CharacterSelectCameraFocus는 Cinemachine 카메라 연결이 필요해 이 페이지에서 따로 만들지 않았다(서버 호출 때문이 아니다). 따라서 슬롯 클릭 · 테두리 강조 · 카메라 전환은 이 페이지에서 검증하지 않는다. " +
                       "칸 이름 · 직업 · 레벨은 View에 반영되지 않는 계약 입력(화면 미반영)이고, 입장 · 삭제는 모의 요청이다. " +
                       "선택 해제(NO_SELECTION)는 View의 Select가 무시하므로, 시나리오 값이 -1일 때 이 페이지가 View의 고른 칸 · 올린 칸을 직접 비우고 SelectionChanged · HoverChanged를 다시 발생시켜 Reset과 이어지게 한다. " +
                       PresentationNotes.CLASS_NOTE + " " + PresentationNotes.NETWORK_NOTE + " " + PresentationNotes.FIXTURE_NOTE;
            }
        }

        public CharacterSelectSelection Selection
        {
            get { return _selection; }
        }

        /****************************************
        *                Methods
        ****************************************/

        protected override void Seed(UiSandboxScenarioSO scenario)
        {
            scenario.SeedInt(KEY_SELECTED, CharacterSelectSelection.NO_SELECTION);
            scenario.SeedInt(KEY_HOVERED, CharacterSelectSelection.NO_SELECTION);

            string[] names = { "검사", "마검사", "궁수", EMPTY_NAME };
            EPlayerClass[] classes = { EPlayerClass.Swordsman, EPlayerClass.Mageknight, EPlayerClass.Archer, EPlayerClass.Swordsman };
            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                scenario.SeedString(SlotKey(i, "name"), names[i]);
                scenario.SeedInt(SlotKey(i, "class"), (int)classes[i]);
                scenario.SeedInt(SlotKey(i, "level"), 1 + (i * 5));
            }
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            _selection  = BindView<CharacterSelectSelection>(context, LABEL_SELECTION, new PresentationFixtureOptions());
            _subscribed = context;

            if (_selection != null)
            {
                _selection.SelectionChanged += OnSelectionChanged;
                _selection.HoverChanged     += OnHoverChanged;
            }
        }

        protected override void OnApply(UiSandboxContext context)
        {
            if (_selection == null)
            {
                return;
            }

            UiSandboxScenarioSO scenario = context.Scenario;
            int selected = scenario.GetInt(KEY_SELECTED);
            if (selected < 0)
            {
                if (!ClearSelection(context))
                {
                    return;
                }
            }
            else
            {
                _selection.Select(selected);
            }

            int hovered = scenario.GetInt(KEY_HOVERED);
            if (hovered >= 0)
            {
                _selection.Hover(hovered);
            }
            else if (_selection.HoveredSlot >= 0)
            {
                _selection.Unhover(_selection.HoveredSlot);
            }
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;

            DrawHeader("View 상태");
            EditorGUILayout.LabelField("고른 칸", FormatSlot((_selection != null) ? _selection.SelectedSlot : CharacterSelectSelection.NO_SELECTION));
            EditorGUILayout.LabelField("올린 칸", FormatSlot((_selection != null) ? _selection.HoveredSlot : CharacterSelectSelection.NO_SELECTION));
            DrawInt(scenario, KEY_SELECTED, "고를 칸 (-1 무시)", CharacterSelectSelection.NO_SELECTION, SLOT_COUNT - 1);
            DrawInt(scenario, KEY_HOVERED, "올릴 칸 (-1 벗어남)", CharacterSelectSelection.NO_SELECTION, SLOT_COUNT - 1);

            DrawHeader("칸 데이터 (계약 입력, 화면 미반영)");
            for (int i = 0; i < SLOT_COUNT; ++i)
            {
                EditorGUILayout.LabelField("칸 " + i, EditorStyles.miniBoldLabel);
                DrawString(scenario, SlotKey(i, "name"), "이름 (비면 빈 칸)");
                DrawEnum(scenario, SlotKey(i, "class"), "직업", EPlayerClass.Swordsman);
                DrawInt(scenario, SlotKey(i, "level"), "레벨", 1, MAX_LEVEL);
            }

            DrawHeader("모의 요청");
            using (new EditorGUI.DisabledScope(!context.IsPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("입장 (모의 요청)"))
                {
                    RunSlotRequest(context, OP_ENTER);
                }

                if (GUILayout.Button("삭제 (모의 요청)"))
                {
                    RunSlotRequest(context, OP_DELETE);
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        protected override void OnDetach()
        {
            if (_selection != null)
            {
                _selection.SelectionChanged -= OnSelectionChanged;
                _selection.HoverChanged     -= OnHoverChanged;
            }

            _selection  = null;
            _subscribed = null;
        }

        /// <summary>
        /// View의 Select는 음수를 무시하므로 비공개 필드를 직접 비우고 이벤트를 다시 발생시킨다. 복제 씬의 하이라이트나 카메라 구독자도 같이 갱신된다.
        /// 필드를 찾지 못하면(이름이 바뀐 경우) 상태 줄에 알리고 false를 돌려준다
        /// </summary>
        private bool ClearSelection(UiSandboxContext context)
        {
            bool selectedChanged = (_selection.SelectedSlot != CharacterSelectSelection.NO_SELECTION);
            bool hoveredChanged  = (_selection.HoveredSlot != CharacterSelectSelection.NO_SELECTION) &&
                                   (context.Scenario.GetInt(KEY_HOVERED) < 0);
            if ((!selectedChanged) && (!hoveredChanged))
            {
                return true;
            }

            FieldInfo selectedField = typeof(CharacterSelectSelection).GetField(FIELD_SELECTED, BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo hoveredField  = typeof(CharacterSelectSelection).GetField(FIELD_HOVERED, BindingFlags.Instance | BindingFlags.NonPublic);
            if ((selectedField == null) || (hoveredField == null))
            {
                context.SetStatus(Title + ": 선택 초기화 실패 (CharacterSelectSelection 필드 이름이 바뀌었다)");
                return false;
            }

            if (selectedChanged)
            {
                selectedField.SetValue(_selection, CharacterSelectSelection.NO_SELECTION);
                InvokeEvent(EVENT_SELECTION_CHANGED);
            }

            if (hoveredChanged)
            {
                hoveredField.SetValue(_selection, CharacterSelectSelection.NO_SELECTION);
                InvokeEvent(EVENT_HOVER_CHANGED);
            }

            return true;
        }

        private void InvokeEvent(string eventName)
        {
            FieldInfo field = typeof(CharacterSelectSelection).GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
            Action<int> handler = (field == null) ? null : field.GetValue(_selection) as Action<int>;
            if (handler != null)
            {
                handler(CharacterSelectSelection.NO_SELECTION);
            }
        }

        private void RunSlotRequest(UiSandboxContext context, string operation)
        {
            int slot = context.Scenario.GetInt(KEY_SELECTED);
            if ((slot < 0) || (slot >= SLOT_COUNT))
            {
                context.SetStatus("먼저 칸을 고른다");
                return;
            }

            string name = context.Scenario.GetString(SlotKey(slot, "name"));
            if (string.IsNullOrEmpty(name))
            {
                context.SetStatus("빈 칸에는 요청할 수 없다");
                return;
            }

            RunRequest(context, operation, () => context.SetStatus(operation + " 성공: " + name));
        }

        private void OnSelectionChanged(int slot)
        {
            if (_subscribed != null)
            {
                _subscribed.Scenario.SetInt(KEY_SELECTED, slot);
            }
        }

        private void OnHoverChanged(int slot)
        {
            if (_subscribed != null)
            {
                _subscribed.Scenario.SetInt(KEY_HOVERED, slot);
            }
        }

        private static string FormatSlot(int slot)
        {
            return (slot < 0) ? NOT_SELECTED : slot.ToString();
        }

        public static string SlotKey(int index, string field)
        {
            return "lobby.charselect.slot." + index + "." + field;
        }
    }
}
