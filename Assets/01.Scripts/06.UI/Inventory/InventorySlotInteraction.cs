// Unity
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Shinshi.UI
{
    /// <summary>인벤토리에서만 슬롯 이동과 정렬 잠금 입력을 처리한다</summary>
    public sealed class InventorySlotInteraction : MonoBehaviour, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        /****************************************
        *                Fields
        ****************************************/

        private InventoryView _owner    = null;
        private ItemSlotView  _slot     = null;
        private Canvas        _canvas   = null;
        private Image         _ghost    = null;
        private bool          _dragging = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnDisable() { CancelDrag(); }

        private void OnDestroy() { CancelDrag(); }

        public void OnPointerClick(PointerEventData eventData)
        {
            if ((_owner == null) || (eventData.dragging) || (eventData.button != PointerEventData.InputButton.Left))
            {
                return;
            }

            if ((Input.GetKey(KeyCode.LeftAlt)) || (Input.GetKey(KeyCode.RightAlt)))
            {
                _owner.TryToggleSlotLock(_slot);
            }
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if ((_owner == null) || (_slot == null) || (_canvas == null)
                || (_slot.Data.IsEmpty) || (eventData.button != PointerEventData.InputButton.Left)
                || (Input.GetKey(KeyCode.LeftAlt)) || (Input.GetKey(KeyCode.RightAlt)))
            {
                return;
            }

            _dragging = true;
            GameObject ghost = new GameObject("InventoryDragIcon", typeof(RectTransform), typeof(Image));
            ghost.transform.SetParent(_canvas.transform, false);
            _ghost = ghost.GetComponent<Image>();
            _ghost.sprite = _slot.IconSprite;
            _ghost.preserveAspect = true;
            _ghost.raycastTarget = false;
            RectTransform sourceRect = _slot.transform as RectTransform;
            _ghost.rectTransform.sizeDelta = (sourceRect != null) ? sourceRect.rect.size : Vector2.one;
            MoveGhost(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_dragging)
            {
                MoveGhost(eventData);
            }
        }

        public void OnEndDrag(PointerEventData eventData) { CancelDrag(); }

        public void OnDrop(PointerEventData eventData)
        {
            if ((_owner == null) || (eventData.pointerDrag == null)
                || (eventData.button != PointerEventData.InputButton.Left))
            {
                return;
            }

            if (!eventData.pointerDrag.TryGetComponent(out InventorySlotInteraction source))
            {
                return;
            }

            if ((!source._dragging) || (source._owner != _owner))
            {
                return;
            }

            _owner.TryMoveSlot(source._slot, _slot);
            source.CancelDrag();
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Bind(InventoryView owner, ItemSlotView slot)
        {
            // 탭 변경, 정렬, 데이터 갱신 시 이전 화면을 기준으로 진행하던 드래그를 취소한다.
            CancelDrag();
            if ((_owner == owner) && (_slot == slot))
            {
                return;
            }

            _owner = owner;
            _slot = slot;
            Canvas canvas = GetComponentInParent<Canvas>();
            _canvas = (canvas != null) ? canvas.rootCanvas : null;
        }

        private void MoveGhost(PointerEventData eventData)
        {
            if ((_ghost == null) || (_canvas == null))
            {
                return;
            }

            RectTransform canvasRect = _canvas.transform as RectTransform;
            Camera camera = (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                ? null : _canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,
                eventData.position, camera, out Vector2 position))
            {
                _ghost.rectTransform.anchoredPosition = position;
            }
        }

        private void CancelDrag()
        {
            _dragging = false;
            if (_ghost != null)
            {
                _ghost.gameObject.SetActive(false);
                Destroy(_ghost.gameObject);
                _ghost = null;
            }
        }
    }
}
