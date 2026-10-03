// Unity
using UnityEngine;
using UnityEngine.EventSystems;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> 캐릭터 선택 화면의 리스트 항목이나 캐릭터 모델을 누르면 그 칸을 고른다 </summary>
    public sealed class CharacterSelectSlotClick : ClientView, IPointerClickHandler
    {
        // UI는 Canvas의 GraphicRaycaster와 raycastTarget이 켜진 Graphic, 3D 모델은 카메라의 PhysicsRaycaster와
        // Collider가 있어야 클릭이 들어온다. 같은 EventSystem을 거치므로 UI가 덮은 자리는 뒤 모델까지 가지 않는다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private CharacterSelectSelection _selection = null;
        [SerializeField] private int                      _slotIndex = 0;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if (_selection == null)
            {
                Debug.LogError("[CharacterSelect] 칸 클릭 대상에 선택 상태가 연결되지 않았다", this);
                enabled = false;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            _selection.Select(_slotIndex);
        }
    }
}
