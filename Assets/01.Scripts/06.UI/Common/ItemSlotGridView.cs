// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary>아이템 칸 여러 개를 필요한 만큼 만들어 재사용하며 값 목록을 한 번에 표시한다</summary>
    public sealed class ItemSlotGridView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("참조")]
        [SerializeField] private ItemSlotView _slotPrefab = null;
        [SerializeField] private TooltipView  _tooltip    = null;

        [Header("표시")]
        [SerializeField] private int _minSlotCount = 0;

        private readonly List<ItemSlotView> _slots = new List<ItemSlotView>();

        /****************************************
        *              Properties
        ****************************************/

        public int SlotCount => _slots.Count;

        /// <summary>칸이 눌렸을 때 알린다. 빈 칸도 알린다</summary>
        public event Action<ItemSlotView> SlotClicked;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnDestroy()
        {
            for (int i = 0; i < _slots.Count; ++i)
            {
                if (_slots[i] != null)
                {
                    _slots[i].Clicked -= OnSlotClicked;
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>목록보다 최소 칸 수가 크면 남는 칸은 빈 칸으로 둔다</summary>
        public void SetItems(IReadOnlyList<ItemSlotData> items) { SetItems(items, _minSlotCount); }

        /// <summary>인벤토리 칸 수처럼 실행 중에 정해지는 최소 칸 수로 표시한다</summary>
        public void SetItems(IReadOnlyList<ItemSlotData> items, int minSlotCount)
        {
            int count = Mathf.Max(minSlotCount, (items != null) ? items.Count : 0);
            EnsureSlots(count);

            for (int i = 0; i < _slots.Count; ++i)
            {
                bool used = (i < count);
                _slots[i].gameObject.SetActive(used);

                if (used)
                {
                    bool hasItem = (items != null) && (i < items.Count);
                    _slots[i].SetData(hasItem ? items[i] : default(ItemSlotData));
                }
            }
        }

        public void SetSelectedIndex(int index)
        {
            for (int i = 0; i < _slots.Count; ++i)
            {
                _slots[i].SetSelected(i == index);
            }
        }

        public int IndexOf(ItemSlotView slot) { return _slots.IndexOf(slot); }

        private void EnsureSlots(int count)
        {
            if (_slotPrefab == null)
            {
                return;
            }

            while (_slots.Count < count)
            {
                ItemSlotView slot = Instantiate(_slotPrefab, transform);
                slot.Bind(_tooltip);
                slot.Clicked += OnSlotClicked;
                _slots.Add(slot);
            }
        }

        private void OnSlotClicked(ItemSlotView slot)
        {
            Action<ItemSlotView> handler = SlotClicked;

            if (handler != null)
            {
                handler(slot);
            }
        }
    }
}
