// Unity
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 마우스 오른쪽으로 캐릭터를 짧게 누르면 그 자리에 상호작용 패널을 연다. 내 캐릭터면 내 패널, 다른 유저면 타유저 패널이다 </summary>
    public sealed class PlayerInteractionPanelOpener : ClientView
    {
        // 오른쪽은 이동 키이기도 해서, 누른 채 이동하다 캐릭터 위를 스치는 경우에는 열지 않아야 한다.
        // 누른 자리의 캐릭터와 뗀 자리의 캐릭터가 같고 짧게 눌렀을 때만 연다.
        // 대상은 카메라 광선이 맞힌 콜라이더 부모의 PlayerCombatState로 찾는다(내 캐릭터는 isLocalPlayer). 콜라이더를 직접 연결해 두면(개발 씬) 그것이 우선이다.
        // 닉네임은 서버가 내려준 캐릭터 이름(PlayerCombatState)을 쓰고, 그것이 없는 개체만 맞은 오브젝트의 이름을 쓴다.
        // 다른 유저 패널의 거래하기, 파티초대 버튼은 이 컴포넌트가 클라이언트 창구로 보낸다. 귓속말과 캐릭터 정보는 아직 준비 중이라 끈다

        /****************************************
        *                Fields
        ****************************************/

        // Input.GetMouseButton 번호. 0 왼쪽, 1 오른쪽
        private const int   MOUSE_BUTTON_OPEN  = 1;
        private const int   MOUSE_BUTTON_CLOSE = 0;
        private const float CLICK_MAX_SECONDS  = 0.3f;

        [Header("월드")]
        [SerializeField] private Camera   _camera        = null;

        // 개발 씬용 고정 연결, 비워 두면 광선이 맞힌 플레이어를 찾는다
        [SerializeField] private Collider _selfCollider  = null;
        [SerializeField] private Collider _otherCollider = null;

        [Header("패널")]
        [SerializeField] private RectTransform _selfPanel  = null;
        [SerializeField] private RectTransform _otherPanel = null;
        [SerializeField] private TMP_Text      _selfName   = null;
        [SerializeField] private TMP_Text      _otherName  = null;

        [Header("다른 유저 패널 버튼")]
        [SerializeField] private Button _tradeButton       = null;
        [SerializeField] private Button _partyInviteButton = null;
        [SerializeField] private Button _whisperButton     = null;
        [SerializeField] private Button _infoButton        = null;

        private RectTransform _parentRect      = null;
        private RectTransform _openedPanel     = null;
        private Component     _pressedTarget   = null;
        private PlayerInteractionRules.ETarget _pressedKind = PlayerInteractionRules.ETarget.None;
        private float         _pressedTime     = 0f;
        private string        _targetName      = string.Empty;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_selfPanel == null) || (_otherPanel == null) || (_selfName == null) || (_otherName == null))
            {
                Debug.LogError("[UI] 상호작용 패널 열기에 패널과 닉네임 칸이 모두 연결되지 않았다", this);
                enabled = false;
                return;
            }

            _parentRect = (RectTransform)_selfPanel.parent;
            _selfPanel.gameObject.SetActive(false);
            _otherPanel.gameObject.SetActive(false);

            // 귓속말 채널과 다른 플레이어 정보는 아직 없다
            SetInteractable(_whisperButton, false);
            SetInteractable(_infoButton, false);
        }

        private void OnEnable()
        {
            AddListener(_tradeButton, OnTradeClicked);
            AddListener(_partyInviteButton, OnPartyInviteClicked);
        }

        private void OnDisable()
        {
            RemoveListener(_tradeButton, OnTradeClicked);
            RemoveListener(_partyInviteButton, OnPartyInviteClicked);
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
            _pressedTarget = null;
            _pressedKind   = PlayerInteractionRules.ETarget.None;

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

            _pressedTarget = RaycastTarget(out _pressedKind);
            _pressedTime   = Time.unscaledTime;
        }

        private void OnOpenButtonUp()
        {
            Component pressed = _pressedTarget;
            PlayerInteractionRules.ETarget kind = _pressedKind;
            _pressedTarget = null;
            _pressedKind   = PlayerInteractionRules.ETarget.None;

            if (pressed == null)
            {
                return;
            }

            if ((Time.unscaledTime - _pressedTime) > CLICK_MAX_SECONDS)
            {
                return;
            }

            if (RaycastTarget(out _) != pressed)
            {
                return;
            }

            Open(pressed, kind);
        }

        private void Open(Component target, PlayerInteractionRules.ETarget kind)
        {
            RectTransform panel = null;
            TMP_Text      label = null;
            if (kind == PlayerInteractionRules.ETarget.Self)
            {
                panel = _selfPanel;
                label = _selfName;
            }
            else if (kind == PlayerInteractionRules.ETarget.Other)
            {
                panel = _otherPanel;
                label = _otherName;
            }

            if (panel == null)
            {
                return;
            }

            _targetName = ResolveName(target);
            label.text  = _targetName;

            // 패널 왼쪽 위를 마우스 자리에 맞춘다. 부모는 가운데 기준이라 변환한 값이 그대로 위치가 된다
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRect, Input.mousePosition, null, out Vector2 local);
            panel.pivot            = new Vector2(0f, 1f);
            panel.anchoredPosition = local;
            panel.gameObject.SetActive(true);
            _openedPanel = panel;
        }

        // 서버가 내려준 이름을 먼저 쓴다. 네트워크 개체가 아니면 오브젝트 이름이다
        private static string ResolveName(Component target)
        {
            PlayerCombatState combat = target.GetComponentInParent<PlayerCombatState>();
            if ((combat != null) && (!string.IsNullOrEmpty(combat.DisplayName)))
            {
                return combat.DisplayName;
            }

            return target.name;
        }

        private void OnTradeClicked()
        {
            if (!string.IsNullOrEmpty(_targetName))
            {
                TownTradeClient.Request(_targetName);
            }

            Close();
        }

        private void OnPartyInviteClicked()
        {
            if (!string.IsNullOrEmpty(_targetName))
            {
                TownPartyClient.Invite(_targetName);
            }

            Close();
        }

        private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private static void RemoveListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
            }
        }

        private static void SetInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        // 광선이 맞힌 대상과 종류를 찾는다. 비교용 키는 맞은 플레이어의 PlayerCombatState, 없으면 고정 콜라이더다
        private Component RaycastTarget(out PlayerInteractionRules.ETarget kind)
        {
            kind = PlayerInteractionRules.ETarget.None;
            Camera camera = (_camera != null) ? _camera : Camera.main;
            if (camera == null)
            {
                return null;
            }

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit))
            {
                return null;
            }

            Collider collider = hit.collider;
            PlayerCombatState player = collider.GetComponentInParent<PlayerCombatState>();
            kind = PlayerInteractionRules.Classify((_selfCollider != null) && (collider == _selfCollider),
                (_otherCollider != null) && (collider == _otherCollider), player != null, (player != null) && (player.isLocalPlayer));
            if (kind == PlayerInteractionRules.ETarget.None)
            {
                return null;
            }

            return (player != null) ? player : collider;
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