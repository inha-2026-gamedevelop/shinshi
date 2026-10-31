// System
using System.Collections;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using Mirror;
using TMPro;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 실제 성장 창의 첫 진입과 도움말을 기존 안내 카탈로그와 서버 완료 기록에 연결한다 </summary>
    public sealed class MenuGuideWindowBinder : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string TRAINING_MENU_ID = "menu_training";
        public const string TALISMAN_MENU_ID = "menu_talisman";

        private const string HELP_NAME   = "MenuGuideHelp";
        private const string PANEL_KEY   = "panel";
        private const float  HELP_SIZE   = 42f;
        private const float  HELP_X      = -100f;
        private const float  HELP_Y      = -30f;
        private const float  HELP_FONT_SIZE = 28f;

        private UiWindowView        _window;
        private string              _menuId;
        private bool                _available;
        private MenuGuideView       _view;
        private MenuGuideController _controller;
        private Button              _help;
        private Coroutine           _openRoutine;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 성장 창의 현재 표시 모드가 안내 대상인지 갱신한다. 에디터 미리보기와 서버에는 붙이지 않는다 </summary>
        public static void Configure(UiWindowView window, string menuId, bool available)
        {
            if ((!Application.isPlaying) || (IsServerBuild))
            {
                return;
            }

            if (!window.TryGetComponent(out MenuGuideWindowBinder binder))
            {
                binder = window.gameObject.AddComponent<MenuGuideWindowBinder>();
            }

            binder._window    = window;
            binder._menuId    = menuId;
            binder._available = available;
            if (binder._help != null)
            {
                binder._help.gameObject.SetActive(available);
            }
            if (!available)
            {
                binder._controller?.Abort();
            }
            if (binder.isActiveAndEnabled)
            {
                binder.ScheduleOpen();
            }
        }

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            ServerMenuGuideRecord.Instance.Changed += TryShowFirstTime;
            ScheduleOpen();
        }

        private void OnDisable()
        {
            ServerMenuGuideRecord.Instance.Changed -= TryShowFirstTime;
            if (_openRoutine != null)
            {
                StopCoroutine(_openRoutine);
                _openRoutine = null;
            }
            _controller?.Abort();
        }

        private void OnDestroy()
        {
            _controller?.Dispose();
            if (_view != null)
            {
                Destroy(_view.gameObject);
            }
        }

        /****************************************
        *             Presentation
        ****************************************/

        private void ScheduleOpen()
        {
            if (_openRoutine != null)
            {
                StopCoroutine(_openRoutine);
            }
            _openRoutine = StartCoroutine(CoOpen());
        }

        private IEnumerator CoOpen()
        {
            // 창 모드와 레이아웃을 바꾸는 기존 열기 처리가 끝난 뒤에 화면 위치를 읽는다.
            yield return null;
            _openRoutine = null;
            TryShowFirstTime();
        }

        private void TryShowFirstTime()
        {
            if ((!_available) || (_window == null) || (!isActiveAndEnabled) || (!NetworkClient.isConnected))
            {
                return;
            }

            if (!EnsureController())
            {
                return;
            }
            if (!ServerMenuGuideRecord.Instance.IsLoaded)
            {
                if (_view != null)
                {
                    _controller.Abort();
                }
                return;
            }
            if (!_controller.IsShowing)
            {
                _controller.TryShowFirstTime(_menuId, ResolveTarget);
            }
        }

        private bool EnsureController()
        {
            if (_controller != null)
            {
                return true;
            }

            PrologueAssetsSO assets = PrologueAssetsSO.Load();
            if ((assets == null) || (assets.MenuGuides == null) || (assets.Illustrations == null))
            {
                return false;
            }

            _view = MenuGuideView.Create();
            _view.SetFont(assets.Illustrations.Font);
            _controller = new MenuGuideController(assets.MenuGuides, ServerMenuGuideRecord.Instance, _view);
            CreateHelpButton(assets.Illustrations.Font);
            return true;
        }

        private RectTransform ResolveTarget(string key)
        {
            return (key == PANEL_KEY) ? (_window.transform as RectTransform) : null;
        }

        private void Replay()
        {
            if ((_available) && (NetworkClient.isConnected) && (EnsureController()))
            {
                _controller.Replay(_menuId, ResolveTarget);
            }
        }

        private void CreateHelpButton(TMP_FontAsset font)
        {
            var root = new GameObject(HELP_NAME, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)root.transform;
            rect.SetParent(_window.transform, false);
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = new Vector2(HELP_SIZE, HELP_SIZE);
            rect.anchoredPosition = new Vector2(HELP_X, HELP_Y);
            root.GetComponent<Image>().color = UiModernDark.ButtonFill;
            _help = root.GetComponent<Button>();
            _help.onClick.AddListener(Replay);

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(root.transform, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelObject.GetComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = HELP_FONT_SIZE;
            label.color = UiModernDark.TextGold;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.SetText("?");
        }
    }
}
