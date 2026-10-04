// System
using System;

// Unity
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary>등급 틀, 아이콘, 수량, 잠금·착용 표시를 갖춘 아이템 칸이며 마우스를 올리면 툴팁을 연다</summary>
    public sealed class ItemSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string COUNT_FORMAT = "{0:N0}";

        [Header("표시")]
        [SerializeField] private Image      _frame        = null;
        [SerializeField] private Image      _icon         = null;
        [SerializeField] private TMP_Text   _countLabel   = null;
        [SerializeField] private GameObject _lockMark     = null;
        [SerializeField] private GameObject _equippedMark = null;
        [SerializeField] private GameObject _selectedMark = null;

        [Header("참조")]
        [SerializeField] private UiIconLibrarySO _library = null;
        [SerializeField] private TooltipView     _tooltip = null;

        private ItemSlotData _data;

        /****************************************
        *              Properties
        ****************************************/

        public ItemSlotData Data => _data;

        /// <summary>칸을 눌렀을 때 알린다. 빈 칸도 알린다</summary>
        public event Action<ItemSlotView> Clicked;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnDisable()
        {
            if (_tooltip != null)
            {
                _tooltip.Hide();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if ((_tooltip == null) || _data.IsEmpty)
            {
                return;
            }

            _tooltip.Show(_data.Title, _data.Description, eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_tooltip != null)
            {
                _tooltip.Hide();
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Action<ItemSlotView> handler = Clicked;

            if (handler != null)
            {
                handler(this);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetData(ItemSlotData data)
        {
            _data = data;
            bool empty = data.IsEmpty;

            if (_icon != null)
            {
                Sprite sprite = ((_library != null) && !empty) ? _library.FindIcon(data.Id) : null;
                _icon.sprite  = sprite;
                _icon.enabled = (sprite != null);
            }

            if (_frame != null)
            {
                Sprite frame = ((_library != null) && !empty) ? _library.FindGradeFrame(data.Grade) : null;
                _frame.sprite  = frame;
                _frame.enabled = (frame != null);
            }

            if (_countLabel != null)
            {
                bool showCount = !empty && (data.Count > 1);
                _countLabel.gameObject.SetActive(showCount);

                if (showCount)
                {
                    _countLabel.text = string.Format(COUNT_FORMAT, data.Count);
                }
            }

            SetActive(_lockMark, !empty && data.IsLocked);
            SetActive(_equippedMark, !empty && data.IsEquipped);
        }

        /// <summary>코드로 칸을 만들 때 툴팁을 연결한다</summary>
        public void Bind(TooltipView tooltip) { _tooltip = tooltip; }

        public void SetSelected(bool selected) { SetActive(_selectedMark, selected); }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
            {
                target.SetActive(active);
            }
        }
    }
}
