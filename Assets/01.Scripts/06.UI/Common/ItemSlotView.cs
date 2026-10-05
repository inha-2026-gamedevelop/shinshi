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
    /// <summary>등급 틀, 아이콘, 수량, 잠금과 착용 표시를 갖춘 아이템 칸이며 마우스를 올리면 툴팁을 연다</summary>
    public sealed class ItemSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string COUNT_FORMAT = "{0:N0}";

        [Header("표시")]
        [SerializeField] private Image      _background   = null;
        [SerializeField] private Image      _frame        = null;
        [SerializeField] private Image      _icon         = null;
        [SerializeField] private TMP_Text   _countLabel   = null;
        [SerializeField] private GameObject _lockMark     = null;
        [SerializeField] private GameObject _equippedMark = null;
        [SerializeField] private GameObject _selectedMark = null;

        [Header("등급 줄 표시 (선택)")]
        [SerializeField] private Image      _gradeEdge      = null;
        [SerializeField] private GameObject _itemBackground = null;

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

            // 등급은 칸 바탕 색으로 먼저 알리고, 빈 칸은 움푹 들어간 바탕으로 구분한다
            if ((_background != null) && (_library != null))
            {
                Sprite background = empty ? _library.EmptyBackground : _library.FindGradeBackground(data.Grade);
                _background.sprite  = background;
                _background.enabled = (background != null);
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

            // 바탕 전체를 칠하지 않고 아래 줄 색으로만 등급을 알리는 칸에서 쓴다
            if (_gradeEdge != null)
            {
                bool showEdge = !empty && (_library != null);
                _gradeEdge.enabled = showEdge;

                if (showEdge)
                {
                    _gradeEdge.color = _library.FindGradeColor(data.Grade);
                }
            }

            SetActive(_itemBackground, !empty);
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
