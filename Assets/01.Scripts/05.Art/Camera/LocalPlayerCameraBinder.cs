// Unity
using UnityEngine;

// 서드파티
using Mirror;
using Unity.Cinemachine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 씬의 쿼터뷰 카메라가 이 클라이언트의 플레이어를 따라가게 묶는다 </summary>
    public sealed class LocalPlayerCameraBinder : ClientView
    {
        // 플레이어 프리팹은 네트워크로 생성되어 씬 카메라가 직접 참조할 수 없다. 로컬 플레이어는 스폰 뒤에 정해지고
        // 재접속하면 새 개체로 바뀌므로 묶인 대상이 사라졌을 때만 다시 찾는다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private CinemachineCamera _camera;

        private Transform _target = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void LateUpdate()
        {
            if ((_camera == null) || (_target != null))
            {
                return;
            }

            NetworkIdentity localPlayer = NetworkClient.localPlayer;
            if (localPlayer == null)
            {
                return;
            }

            _target                       = localPlayer.transform;
            _camera.Target.TrackingTarget = _target;

            // 묶기 전 위치에서 끌려오지 않고 바로 플레이어 위에서 시작한다
            _camera.PreviousStateIsValid = false;
        }
    }
}
