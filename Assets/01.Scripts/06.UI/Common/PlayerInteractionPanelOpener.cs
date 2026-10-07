// Unity
using UnityEngine;
using UnityEngine.EventSystems;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 마우스 오른쪽으로 캐릭터를 짧게 누르면 그 자리에 상호작용 패널을 연다. 내 캐릭터면 내 패널, 다른 유저면 타유저 패널이다 </summary>
    public sealed class PlayerInteractionPanelOpener : ClientView
    {
        // 오른쪽은 이동 키이기도 해서, 누른 채 이동하다 캐릭터 위를 스치는 경우에는 열지 않아야 한다.
        // 누른 자리의 캐릭터와 뗀 자리의 캐릭터가 같고 짧게 눌렀을 때만 연다.
        // 닉네임 칸은 지금 맞은 오브젝트의 이름을 넣는 임시 연결이며, 실제 구동에서는 서버가 준 닉네임으로 바꾼다

        /****************************************
        *                Fields
        ****************************************/

        // Input.GetMouseButton 번호. 0 왼쪽, 1 오른쪽
        private const int   MOUSE_BUTTON_OPEN  = 1;
        private const int   MOUSE_BUTTON_CLOSE = 0;
        private const float CLICK_MAX_SECONDS  = 0.3f;

        [Header("월드")]
        [SerializeField] private Camera   _camera        = null;
        [SerializeField] private Collider _selfCollider  = null;
        [SerializeField] private Collider _otherCollider = null;

        [Header("패널")]
        [SerializeField] private RectTransform _selfPanel  = null;
        [SerializeField] private RectTransform _otherPanel = null;
        [SerializeField] private TMP_Text      _selfName   = null;
        [SerializeField] private TMP_Text      _otherName  = null;

        private RectTransform _parentRect      = null;
        private RectTransform _openedPanel     = null;
        private Collider      _pressedCollider = null;
        private float         _pressedTime     = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_camera == null) || (_selfCollider == null) || (_otherCollider == null)
                || (_selfPanel == null) || (_otherPanel == null) || (_selfName == null) || (_otherName == null))
            {
                Debug.LogError("[UI] 상호작용 패널 열기에 카메라, 콜라이더, 패널, 닉네임 칸이 모두 연결되지 않았다", this);
                enabled = false;
                return;
            }

            _parentRect = (RectTransform)_selfPanel.parent;
            _selfPanel.gameObject.SetActive(false);
            _otherPanel.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }

            if (Input.GetMouseButtonDown(MOUSE_BUTTON_OPEN))
            {
                OnOpenButtonDown();
                return;
            }

            if (Input.GetMouseButtonUp(MOUSE_BUTTON_OPEN))
            {
                OnOpenButtonUp();
                return;
            }

            // 패널 밖을 왼쪽으로 누르면 닫는다. 패널 안의 버튼 클릭은 닫지 않는다
            if ((_openedPanel != null) && (Input.GetMouseButtonDown(MOUSE_BUTTON_CLOSE)) && (!IsInsideOpenedPanel()))
            {
                Close();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnOpenButtonDown()
        {
            _pressedCollider = null;

            // 열린 패널 안을 누른 것은 버튼에게 맡긴다
            if (IsInsideOpenedPanel())
            {
                return;
            }

            Close();

            // 다른 UI 위를 누른 경우는 뒤에 있는 캐릭터를 고르지 않는다
            if ((EventSystem.current != null) && (EventSystem.current.IsPointerOverGameObject()))
            {
                return;
            }

            _pressedCollider = RaycastCollider();
            _pressedTime     = Time.unscaledTime;
        }

        private void OnOpenButtonUp()
        {
            Collider pressed = _pressedCollider;
            _pressedCollider = null;

            if (pressed == null)
            {
                return;
            }

            if ((Time.unscaledTime - _pressedTime) > CLICK_MAX_SECONDS)
            {
                return;
            }

            if (RaycastCollider() != pressed)
            {
                return;
            }

            Open(pressed);
        }

        private void Open(Collider target)
        {
            RectTransform panel = null;
            TMP_Text      label = null;
            if (target == _selfCollider)
            {
                panel = _selfPanel;
                label = _selfName;
            }
            else if (target == _otherCollider)
            {
                panel = _otherPanel;
                label = _otherName;
            }

            if (panel == null)
            {
                return;
            }

            label.text = target.name;

            // 패널 왼쪽 위를 마우스 자리에 맞춘다. 부모는 가운데 기준이라 변환한 값이 그대로 위치가 된다
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRect, Input.mousePosition, null, out Vector2 local);
            panel.pivot            = new Vector2(0f, 1f);
            panel.anchoredPosition = local;
            panel.gameObject.SetActive(true);
            _openedPanel = panel;
        }

        private Collider RaycastCollider()
        {
            Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
            return Physics.Raycast(ray, out RaycastHit hit) ? hit.collider : null;
        }

        private bool IsInsideOpenedPanel()
        {
            return (_openedPanel != null) && RectTransformUtility.RectangleContainsScreenPoint(_openedPanel, Input.mousePosition, null);
        }

        private void Close()
        {
            if (_openedPanel == null)
            {
                return;
            }

            _openedPanel.gameObject.SetActive(false);
            _openedPanel = null;
        }
    }
}