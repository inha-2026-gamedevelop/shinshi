// Unity
using UnityEngine;
using UnityEngine.EventSystems;

// 서드파티
using DG.Tweening;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Art
{
    /// <summary> 쿼터뷰 카메라를 줌 단계의 시점에 두고, 휠 한 칸마다 다음 단계로 각도와 거리를 함께 옮긴다 </summary>
    public sealed class QuarterViewCameraRig : ClientView
    {
        // 방위 회전은 넣지 않는다. 클릭 이동과 커서 조준이 월드 지점을 쓰므로 회전이 없어도 조작이 성립하고,
        // 회전을 넣으면 카메라 방위를 이동 쪽에 넘기는 값 채널과 화면 기준 예고 확인이 함께 필요하다.
        // 내려다보는 각은 줌 단계마다 다르며 가까울수록 낮아진다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private CinemachineCamera _camera;
        [SerializeField] private CinemachineFollow _follow;

        // 빠르게 출발해 끝에서 천천히 멈춘다
        [SerializeField] private Ease _zoomEase = Ease.OutCubic;

        private CameraConfigSO  _config    = null;
        private Tweener         _zoomTween = null;
        private QuarterViewPose _pose;
        private QuarterViewPose _fromPose;
        private QuarterViewPose _toPose;
        private float           _blend     = 0f;
        private int             _zoomLevel = 0;

        public QuarterViewPose Pose      => _pose;
        public int             ZoomLevel => _zoomLevel;

        /****************************************
        *              Unity Event
        ****************************************/

        // GameDB는 GameDBBootstrap이 초기화하므로 Awake가 아니라 Start에서 읽는다
        private void Start()
        {
            if ((_camera == null) || (_follow == null))
            {
                Debug.LogError("[Camera] 쿼터뷰 리그에 Cinemachine 카메라나 Follow가 연결되지 않았다", this);
                enabled = false;
                return;
            }

            _config    = GameDB.Camera;
            _zoomLevel = _config.DefaultZoomLevel;
            _pose      = GetLevelPose(_zoomLevel);
            _fromPose  = _pose;
            _toPose    = _pose;

            ApplyDamping();
            ApplyPose();

            // 단계를 옮길 때마다 새 트윈을 만들지 않고 시작, 끝 시점만 바꿔 0에서 1까지 다시 튼다
            _zoomTween = DOTween.To(GetBlend, SetBlend, 1f, _config.ZoomDuration)
                .SetEase(_zoomEase)
                .SetAutoKill(false)
                .Pause()
                .SetLink(gameObject);
        }

        private void Update()
        {
            if (_config == null)
            {
                return;
            }

            float scroll = Input.GetAxis(Constants.Player.AXIS_CAMERA_ZOOM);
            if (scroll == 0f)
            {
                return;
            }

            // 목록 같은 UI 위에서 굴린 휠은 화면 스크롤이다
            if ((EventSystem.current != null) && (EventSystem.current.IsPointerOverGameObject()))
            {
                return;
            }

            int next = QuarterViewCameraRules.StepZoomLevel(_zoomLevel, scroll, _config.ZoomLevels.Count);
            if (next == _zoomLevel)
            {
                return;
            }

            // 전환 도중에 다시 굴리면 지금 보이는 시점에서 새 단계로 이어 간다. 이전 시작점으로 튀지 않는다
            _zoomLevel = next;
            _fromPose  = _pose;
            _toPose    = GetLevelPose(_zoomLevel);
            _blend     = 0f;
            _zoomTween.Restart();
        }

        /****************************************
        *                Methods
        ****************************************/

        private QuarterViewPose GetLevelPose(int level)
        {
            CameraConfigSO.ZoomLevel data = _config.ZoomLevels[level];
            return new QuarterViewPose(data.PitchAngle, data.FieldOfView, data.Distance, data.FocusHeight);
        }

        private void ApplyDamping()
        {
            TrackerSettings tracker = _follow.TrackerSettings;
            tracker.PositionDamping = Vector3.one * _config.FollowDamping;
            _follow.TrackerSettings = tracker;
        }

        private void ApplyPose()
        {
            _camera.transform.rotation = QuarterViewCameraRules.GetRotation(_pose.PitchAngle, _config.YawAngle);

            LensSettings lens = _camera.Lens;
            lens.FieldOfView = _pose.FieldOfView;
            _camera.Lens     = lens;

            _follow.FollowOffset = QuarterViewCameraRules.GetFollowOffset(_pose.PitchAngle, _config.YawAngle, _pose.Distance,
                _pose.FocusHeight);
        }

        private float GetBlend()
        {
            return _blend;
        }

        private void SetBlend(float blend)
        {
            _blend = blend;
            _pose  = QuarterViewPose.Lerp(_fromPose, _toPose, blend);
            ApplyPose();
        }
    }
}
