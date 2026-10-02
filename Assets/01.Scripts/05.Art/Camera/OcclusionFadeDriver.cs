// Unity
using UnityEngine;
using UnityEngine.Rendering;

// 서드파티
using Unity.Cinemachine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Art
{
    /// <summary> 카메라와 따라가는 플레이어 사이의 가림 범위를 셰이더 전역값과 값 채널에 넘긴다 </summary>
    public sealed class OcclusionFadeDriver : ClientView
    {
        // 물체마다 재질을 바꾸지 않고 전역값 몇 개로 비운다. 가림 대상을 충돌체로 찾으면 충돌체가 없는 나뭇잎이 빠지고,
        // 재질을 투명으로 바꾸면 SRP Batcher가 깨지고 한 나무 안에서 정렬이 엉킨다.
        // 그래서 이 값을 읽는 셰이더(Lit Occlusion Fade, 폴리지 그래프)를 쓰는 재질만 비워진다

        /****************************************
        *                Fields
        ****************************************/

        private static readonly int _cameraId = Shader.PropertyToID("_ShinshiOcclusionCamera");
        private static readonly int _focusId  = Shader.PropertyToID("_ShinshiOcclusionFocus");
        private static readonly int _paramsId = Shader.PropertyToID("_ShinshiOcclusionParams");

        // 화면을 그리는 실제 카메라. 에디터 씬 뷰 같은 다른 카메라가 그릴 때는 값을 바꾸지 않는다
        [SerializeField] private Camera               _outputCamera;
        [SerializeField] private CinemachineCamera    _followCamera;
        [SerializeField] private QuarterViewCameraRig _rig;

        private CameraConfigSO _config = null;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            if ((_outputCamera == null) || (_followCamera == null) || (_rig == null))
            {
                Debug.LogError("[Camera] 가림 투명화에 출력 카메라, Cinemachine 카메라, 쿼터뷰 리그 중 연결되지 않은 것이 있다", this);
                enabled = false;
                return;
            }

            _config = GameDB.Camera;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            ClearFade();
        }

        /****************************************
        *                Methods
        ****************************************/

        // Cinemachine Brain이 LateUpdate에서 카메라를 옮긴 뒤 그리기 직전에 불리므로 이번 프레임의 최종 위치를 쓴다
        private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if ((camera != _outputCamera) || (_config == null))
            {
                return;
            }

            Transform target = _followCamera.Target.TrackingTarget;
            if (target == null)
            {
                ClearFade();
                return;
            }

            // 줌 단계마다 초점 높이가 달라 리그가 지금 보간 중인 값을 쓴다
            Vector3 feet  = target.position;
            Vector3 focus = feet + (Vector3.up * _rig.Pose.FocusHeight);
            var shape = new OcclusionFadeShape(camera.transform.position, focus, _config.OcclusionRadius,
                _config.OcclusionEdgeSoftness, _config.OcclusionTargetMargin, feet.y + _config.OcclusionFloorClearance,
                _config.OcclusionMinVisibility);

            OcclusionFadeChannel.Publish(shape);

            // 카메라 벡터의 w가 켜짐 표시다. 값을 쓰는 쪽이 없으면 전역값이 0이라 셰이더는 아무것도 비우지 않는다
            Shader.SetGlobalVector(_cameraId, new Vector4(shape.CameraPosition.x, shape.CameraPosition.y, shape.CameraPosition.z, 1f));
            Shader.SetGlobalVector(_focusId, new Vector4(shape.FocusPoint.x, shape.FocusPoint.y, shape.FocusPoint.z, shape.Radius));
            Shader.SetGlobalVector(_paramsId, new Vector4(shape.EdgeSoftness, shape.TargetMargin, shape.FloorHeight, shape.MinVisibility));
        }

        private static void ClearFade()
        {
            OcclusionFadeChannel.Clear();
            Shader.SetGlobalVector(_cameraId, Vector4.zero);
        }
    }
}
