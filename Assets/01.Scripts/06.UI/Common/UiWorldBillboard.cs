// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> 월드에 놓인 이름표가 늘 카메라 쪽을 보게 돌린다 </summary>
    // 쿼터뷰 카메라는 고정 각도라 카메라와 같은 방향으로 맞추면 글자가 찌그러지지 않는다
    public sealed class UiWorldBillboard : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private Camera _camera = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void LateUpdate()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    return;
                }
            }

            transform.rotation = _camera.transform.rotation;
        }
    }
}
