// System
using System;

// Unity
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 장비 옵션 재설정 창. 착용 부위 목록과 선택한 장비의 옵션 두 줄, 비용, 재설정 버튼을 보여 준다 </summary>
    public sealed class EquipmentOptionView : MonoBehaviour
    {
        // 네트워크는 모르고 이벤트로 의도만 알린다. 글자는 EquipmentOptionBinder가 만들어 넣는다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Button _closeButton = null;

        [Header("부위 목록 (부위 순서)")]
        [SerializeField] private Button[]     _slotButtons       = null;
        [SerializeField] private TMP_Text[]   _slotLabels        = null;
        [SerializeField] private GameObject[] _slotSelectedMarks = null;

        [Header("선택한 장비")]
        [SerializeField] private TMP_Text   _itemLabel   = null;
        [SerializeField] private TMP_Text[] _lineLabels  = null;
        [SerializeField] private TMP_Text[] _rangeLabels = null;
        [SerializeField] private Color      _normalLineColor   = new Color(0.9f, 0.92f, 0.94f, 1f);
        [SerializeField] private Color      _highTierLineColor = new Color(0.94f, 0.62f, 0.15f, 1f);

        [Header("재설정")]
        [SerializeField] private TMP_Text _costLabel    = null;
        [SerializeField] private TMP_Text _pityLabel    = null;
        [SerializeField] private TMP_Text _statusLabel  = null;
        [SerializeField] private Button   _rerollButton = null;
        [SerializeField] private TMP_Text _rerollLabel  = null;

        private UnityAction[] _slotHandlers;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen => gameObject.activeSelf;

        public event Action      Closed;
        public event Action<int> SlotSelected;
        public event Action      RerollRequested;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(OnCloseClicked);
            }

            if (_rerollButton != null)
            {
                _rerollButton.onClick.AddListener(OnRerollClicked);
            }

            if (_slotButtons == null)
            {
                return;
            }

            // 줄마다 자기 번호를 넘겨야 해서 처리기를 따로 만들어 두고 해제할 때 같은 것을 쓴다
            _slotHandlers = new UnityAction[_slotButtons.Length];
            for (int i = 0; i < _slotButtons.Length; ++i)
            {
                if (_slotButtons[i] == null)
                {
                    continue;
                }

                int slot = i;
                _slotHandlers[i] = () => SlotSelected?.Invoke(slot);
                _slotButtons[i].onClick.AddListener(_slotHandlers[i]);
            }
        }

        private void OnDestroy()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(OnCloseClicked);
            }

            if (_rerollButton != null)
            {
                _rerollButton.onClick.RemoveListener(OnRerollClicked);
            }

            if ((_slotButtons == null) || (_slotHandlers == null))
            {
                return;
            }

            for (int i = 0; i < _slotButtons.Length; ++i)
            {
                if ((_slotButtons[i] != null) && (_slotHandlers[i] != null))
                {
                    _slotButtons[i].onClick.RemoveListener(_slotHandlers[i]);
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetOpen(bool open)
        {
            gameObject.SetActive(open);
        }

        public void SetSlot(int slot, string text)
        {
            if ((_slotLabels != null) && (slot >= 0) && (slot < _slotLabels.Length))
            {
                SetText(_slotLabels[slot], text);
            }
        }

        public void SetSelectedSlot(int selected)
        {
            if (_slotSelectedMarks == null)
            {
                return;
            }

            for (int i = 0; i < _slotSelectedMarks.Length; ++i)
            {
                if (_slotSelectedMarks[i] != null)
                {
                    _slotSelectedMarks[i].SetActive(i == selected);
                }
            }
        }

        public void SetItem(string itemName)
        {
            SetText(_itemLabel, itemName);
        }

        /// <summary> 옵션 한 줄, 상급 줄이면 강조색으로 칠한다 </summary>
        public void SetLine(int line, string text, string rangeText, bool isHighTier)
        {
            if ((_lineLabels != null) && (line >= 0) && (line < _lineLabels.Length) && (_lineLabels[line] != null))
            {
                _lineLabels[line].text  = text ?? string.Empty;
                _lineLabels[line].color = isHighTier ? _highTierLineColor : _normalLineColor;
            }

            if ((_rangeLabels != null) && (line >= 0) && (line < _rangeLabels.Length))
            {
                SetText(_rangeLabels[line], rangeText);
            }
        }

        public void SetCost(string cost)
        {
            SetText(_costLabel, cost);
        }

        public void SetPity(string pity)
        {
            SetText(_pityLabel, pity);
        }

        public void SetStatus(string status)
        {
            SetText(_statusLabel, status);
        }

        /// <summary> 재설정 버튼의 글자와 누를 수 있는지 </summary>
        public void SetReroll(string label, bool interactable)
        {
            SetText(_rerollLabel, label);
            if (_rerollButton != null)
            {
                _rerollButton.interactable = interactable;
            }
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text ?? string.Empty;
            }
        }

        private void OnCloseClicked()
        {
            SetOpen(false);
            Closed?.Invoke();
        }

        private void OnRerollClicked()
        {
            RerollRequested?.Invoke();
        }
    }
}
