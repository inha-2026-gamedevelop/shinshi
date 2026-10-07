// Unity
using UnityEngine;
using UnityEngine.EventSystems;

// 서드파티
using TMPro;

namespace Shinshi.Common
{
    // 채팅이나 이름 입력 중에 친 글자가 단축키, 기술 키로 새지 않게 키 입력을 읽는 쪽이 먼저 묻는다
    /// <summary> 지금 글자 입력 칸이 포커스를 갖고 있는지 </summary>
    public static class TextInputFocus
    {
        /****************************************
        *                Methods
        ****************************************/

        public static bool IsTyping()
        {
            EventSystem system = EventSystem.current;
            GameObject  focus  = (system != null) ? system.currentSelectedGameObject : null;
            if (focus == null)
            {
                return false;
            }

            if ((focus.TryGetComponent(out TMP_InputField tmpField)) && (tmpField.isFocused))
            {
                return true;
            }

            return (focus.TryGetComponent(out UnityEngine.UI.InputField field)) && (field.isFocused);
        }
    }
}
