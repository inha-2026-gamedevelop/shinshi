// Unity
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 메뉴 버튼이나 Esc로 오른쪽 메뉴를 펼치고, 펼치는 동안 그 자리를 가리는 HUD를 숨긴다. 닫기 버튼, 항목 선택, 바깥 클릭, Esc로 접는다 </summary>
    public sealed class HudMenuDropdown : ClientView
    {
        // 항목이 실제로 하는 일은 이 컴포넌트가 정하지 않는다. 항목 버튼의 onClick에 창 열기를 연결하면 되고,
        // 이 컴포넌트는 항목을 누른 뒤 메뉴를 접는 일만 맡는다.
        // 이 컴포넌트가 붙은 오브젝트 자체를 숨김 대상에 넣으면 Update가 멈춰 Esc가 먹지 않으므로 넣지 않는다

        /****************************************
        *                Fields
        ****************************************/

        // Input.GetMouseButton 번호. 0 왼쪽
        private const int MOUSE_BUTTON = 0;

        [SerializeField] private Button        _menuButton = null;
        [SerializeField] private RectTransform _panel      = null;

        [Header("펼치는 동안 숨길 HUD")]
        [SerializeField] private GameObject[] _hideWhileOpen = null;

        // 숨기기 전에 켜져 있던 것만 닫을 때 되살린다
        private bool[] _wasActive = null;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_menuButton == null) || (_panel == null))
            {
                Debug.LogError("[UI] 메뉴에 메뉴 버튼이나 패널이 연결되지 않았다", this);
                enabled = false;
                return;
            }

            _wasActive = new bool[(_hideWhileOpen != null) ? _hideWhileOpen.Length : 0];
            _panel.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            _menuButton.onClick.AddListener(Toggle);
            foreach (Button item in _panel.GetComponentsInChildren<Button>(true))
            {
                item.onClick.AddListener(Close);
            }
        }

        private void OnDisable()
        {
            _menuButton.onClick.RemoveListener(Toggle);
            foreach (Button item in _panel.GetComponentsInChildren<Button>(true))
            {
                item.onClick.RemoveListener(Close);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Toggle();
                return;
            }

            if (!_panel.gameObject.activeSelf)
            {
                return;
            }

            // 메뉴 버튼은 자기 클릭에서 접으므로 여기서는 패널 밖이면서 메뉴 버튼도 아닌 곳만 본다
            if ((Input.GetMouseButtonDown(MOUSE_BUTTON)) && (!IsPointerOnMenu()))
            {
                Close();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Toggle()
        {
            if (_panel.gameObject.activeSelf)
            {
                Close();
                return;
            }

            Open();
        }

        private void Open()
        {
            for (int i = 0; i < _wasActive.Length; ++i)
            {
                GameObject target = _hideWhileOpen[i];
                _wasActive[i] = (target != null) && (target.activeSelf);
                if (_wasActive[i])
                {
                    target.SetActive(false);
                }
            }

            _panel.gameObject.SetActive(true);
        }

        private void Close()
        {
            if (!_panel.gameObject.activeSelf)
            {
                return;
            }

            _panel.gameObject.SetActive(false);
            for (int i = 0; i < _wasActive.Length; ++i)
            {
                if ((_wasActive[i]) && (_hideWhileOpen[i] != null))
                {
                    _hideWhileOpen[i].SetActive(true);
                }
            }
        }

        private bool IsPointerOnMenu()
        {
            Vector2 point = Input.mousePosition;
            return RectTransformUtility.RectangleContainsScreenPoint(_panel, point, null)
                || RectTransformUtility.RectangleContainsScreenPoint((RectTransform)_menuButton.transform, point, null);
        }
    }
}
