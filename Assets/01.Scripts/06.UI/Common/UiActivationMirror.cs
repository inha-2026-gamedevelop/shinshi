// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> 이 오브젝트가 켜지고 꺼질 때 연결된 오브젝트도 함께 켜고 끈다. UI 계층에 둘 수 없는 월드 쪽 미리보기 무대를 창과 함께 여닫는 데 쓴다 </summary>
    public sealed class UiActivationMirror : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private GameObject _target = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if (_target != null)
            {
                _target.SetActive(true);
            }
        }

        private void OnDisable()
        {
            if (_target != null)
            {
                _target.SetActive(false);
            }
        }
    }
}
