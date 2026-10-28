// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> 창의 활성 상태가 바뀔 때 공통 열기, 닫기 효과음을 재생한다 </summary>
    public abstract class UiWindowView : MonoBehaviour
    {
        /****************************************
        *              Unity Event
        ****************************************/

        protected virtual void OnEnable()
        {
            UiSound.PlayWindowTransition(gameObject, true);
        }

        protected virtual void OnDisable()
        {
            UiSound.PlayWindowTransition(gameObject, false);
        }
    }
}
