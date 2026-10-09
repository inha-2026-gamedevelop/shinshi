using Shinshi.UI;
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 얼러트 · 컨펌 · 토스트 팝업. Dev_ToastMessage 씬의 UiPopupHub를 쓰고 확인 결과는 모의 요청으로 이어 보낸다 </summary>
    public sealed class PopupPage : PresentationPageBase
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PAGE_ID    = "hud.popup";
        public const string SCENE_PATH = "Assets/00.Scenes/Dev_HUD/Dev_ToastMessage.unity";

        public const string KEY_SIZE    = "popup.size";
        public const string KEY_ALERT   = "popup.alertText";
        public const string KEY_CONFIRM = "popup.confirmText";
        public const string KEY_TOAST   = "popup.toastText";

        public const string OP_CONFIRM = "popup.confirm.accept";

        private const int    SIZE_COUNT   = 3;
        private const string LABEL_HUB    = "팝업 허브";
        private const string PLAY_ONLY    = "팝업 연출(DOTween)은 Play Mode에서만 동작한다";
        private const string NO_RESULT    = "없음";

        private UiPopupHub _hub;
        private string     _confirmResult = NO_RESULT;

        /****************************************
        *              Properties
        ****************************************/

        public override string Id
        {
            get { return PAGE_ID; }
        }

        public override string Title
        {
            get { return "알림 · 확인 · 토스트 팝업"; }
        }

        public override string SourceScenePath
        {
            get { return SCENE_PATH; }
        }

        public override string Requirement
        {
            get
            {
                return "Dev_ToastMessage 씬의 UiPopupHub로 소형/중형/대형 얼러트 · 컨펌과 토스트를 연다. 확인 버튼 결과(onResult)를 받으면 모의 요청을 보내 응답을 표시한다. " +
                       PresentationNotes.COMMON_UI_NOTE + " " + PresentationNotes.FIXTURE_NOTE;
            }
        }

        public UiPopupHub Hub
        {
            get { return _hub; }
        }

        public string ConfirmResult
        {
            get { return _confirmResult; }
        }

        /****************************************
        *                Methods
        ****************************************/

        protected override void Seed(UiSandboxScenarioSO scenario)
        {
            scenario.SeedInt(KEY_SIZE, (int)EPopupSize.Medium);
            scenario.SeedString(KEY_ALERT, "저장되지 않은 변경 사항이 있습니다");
            scenario.SeedString(KEY_CONFIRM, "정말 진행하시겠습니까?");
            scenario.SeedString(KEY_TOAST, "아이템을 획득했습니다");
        }

        protected override void OnAttach(UiSandboxContext context)
        {
            _confirmResult = NO_RESULT;

            PresentationFixtureOptions options = new PresentationFixtureOptions(EFixtureAnchor.Center)
                .WithLength("_alerts", SIZE_COUNT)
                .WithLength("_confirms", SIZE_COUNT);
            _hub = BindView<UiPopupHub>(context, LABEL_HUB, options);
        }

        protected override void OnApply(UiSandboxContext context)
        {
        }

        protected override void OnDrawControls(UiSandboxContext context)
        {
            UiSandboxScenarioSO scenario = context.Scenario;

            if (!context.IsPlaying)
            {
                EditorGUILayout.HelpBox(PLAY_ONLY, MessageType.Info);
            }

            DrawEnum(scenario, KEY_SIZE, "크기", EPopupSize.Medium);
            DrawString(scenario, KEY_ALERT, "얼러트 문구");
            DrawString(scenario, KEY_CONFIRM, "컨펌 문구");
            DrawString(scenario, KEY_TOAST, "토스트 문구");

            EditorGUILayout.LabelField("마지막 컨펌 결과", _confirmResult);

            using (new EditorGUI.DisabledScope(!context.IsPlaying || _hub == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("얼러트 열기"))
                {
                    _hub.ShowAlert(Size(scenario), scenario.GetString(KEY_ALERT));
                }

                if (GUILayout.Button("컨펌 열기"))
                {
                    _hub.ShowConfirm(Size(scenario), scenario.GetString(KEY_CONFIRM), OnConfirmResult);
                }

                if (GUILayout.Button("토스트 열기"))
                {
                    _hub.ShowToast(scenario.GetString(KEY_TOAST));
                }

                if (GUILayout.Button("모두 닫기"))
                {
                    _hub.CloseAll();
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        protected override void OnDetach()
        {
            if (_hub != null)
            {
                _hub.CloseAll();
            }

            _hub           = null;
            _confirmResult = NO_RESULT;
        }

        private void OnConfirmResult(bool accepted)
        {
            if (Context == null)
            {
                return;
            }

            _confirmResult = accepted ? "확인" : "취소";
            if (accepted)
            {
                RunRequest(Context, OP_CONFIRM, () => _hub.ShowToast("확인 요청 성공 (모의)"),
                    message => _hub.ShowToast(message));
            }
        }

        private static EPopupSize Size(UiSandboxScenarioSO scenario)
        {
            int size = Mathf.Clamp(scenario.GetInt(KEY_SIZE), 0, SIZE_COUNT - 1);
            return (EPopupSize)size;
        }
    }
}
