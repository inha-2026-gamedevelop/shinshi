#if UNITY_EDITOR
// Unity
using UnityEngine;

// 서드파티
using Unity.Cinemachine;

namespace Shinshi.AI
{
    /// <summary> 에디터 시험 장치가 씬의 쿼터뷰 카메라를 시험용 플레이어에게 묶는 도우미, 빌드에는 들어가지 않는다 </summary>
    public static class PrologueDevCamera
    {
        // 시험 장치가 있는 서버 어셈블리는 Cinemachine을 참조하지 않아서 카메라 타입을 이쪽에서 다룬다.
        // 실제 클라이언트는 LocalPlayerCameraBinder가 네트워크 로컬 플레이어에 묶으므로 이 도우미를 쓰지 않는다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 씬의 첫 시네머신 카메라가 target을 따라가게 하고 그 위치에서 바로 시작하게 한다 </summary>
        public static void Follow(Transform target)
        {
            var camera = Object.FindAnyObjectByType<CinemachineCamera>();
            if (camera == null)
            {
                Debug.LogWarning("[PrologueDevCamera] 시네머신 카메라를 찾지 못했다");
                return;
            }

            camera.Target.TrackingTarget = target;
            camera.PreviousStateIsValid  = false;
        }
    }
}
#endif
