// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 쿼터뷰 게임 카메라의 줌 단계별 시점, 따라가기, 가림 투명화 범위. 로컬 표현 전용이며 서버 판정에 쓰지 않는다 </summary>
    [CreateAssetMenu(fileName = "CameraConfigSO", menuName = "Shinshi/CameraConfigSO")]
    public class CameraConfigSO : GameDataSO
    {
        /// <summary> 줌 한 단계의 시점, 단계 사이는 네 값을 함께 보간한다 </summary>
        [Serializable]
        public class ZoomLevel
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private float _pitchAngle;  // 도, 수평에서 아래로 내려다보는 각
            [SerializeField] private float _fieldOfView; // 도, 세로 시야각
            [SerializeField] private float _distance;    // m, 초점에서 카메라까지
            [SerializeField] private float _focusHeight; // m, 플레이어 발밑에서 카메라가 바라보는 지점까지 높이

            public float PitchAngle  => _pitchAngle;
            public float FieldOfView => _fieldOfView;
            public float Distance    => _distance;
            public float FocusHeight => _focusHeight;

            /****************************************
            *                Methods
            ****************************************/

            public ZoomLevel(float pitchAngle, float fieldOfView, float distance, float focusHeight)
            {
                _pitchAngle  = pitchAngle;
                _fieldOfView = fieldOfView;
                _distance    = distance;
                _focusHeight = focusHeight;
            }
        }

        /****************************************
        *              Constants
        ****************************************/

        // 90도면 바로 위에서 내려다봐 화면 위쪽 방향이 정해지지 않는다
        private const float MAX_PITCH_ANGLE   = 89f;
        private const float MAX_YAW_ANGLE     = 360f;
        private const float MAX_FIELD_OF_VIEW = 179f;
        private const float MAX_RATE          = 1f;

        // 최소 보임이 1이면 투명화가 꺼진 것과 같아 설정 실수로 본다
        private const float MAX_MIN_VISIBILITY = 0.99f;

        /****************************************
        *                Fields
        ****************************************/

        // 사용자가 준 참고 영상(2026-10-02)의 세 단계를 화면에서 잰 근사값이다. 0번이 가장 먼 기본 시점이고
        // 휠을 위로 굴릴 때마다 한 단계씩 가까워진다. 가까울수록 낮게 내려와 마지막 단계는 수평선이 보인다
        [Header("줌 단계")]
        [SerializeField] private ZoomLevel[] _zoomLevels =
        {
            new ZoomLevel(55f, 30f, 14.5f, 0.3f),
            new ZoomLevel(40f, 30f, 4f, 0.9f),
            new ZoomLevel(13f, 30f, 2.6f, 1.2f)
        };

        [SerializeField] private int   _defaultZoomLevel = 0;    // 시작 단계 번호
        [SerializeField] private float _zoomDuration     = 0.7f; // 초, 한 단계를 옮기는 시간

        [Header("방위와 따라가기")]
        [SerializeField] private float _yawAngle      = 0f; // 도, 월드 기준 카메라가 바라보는 방위
        [SerializeField] private float _followDamping = 1f; // 초, Cinemachine 위치 감쇠, 0이면 바로 붙는다

        // 카메라에서 초점까지 이어진 원뿔 안의 가림만 비운다. 반지름은 초점 깊이에서 잰 값이라
        // 화면에서는 거리와 무관하게 같은 크기의 원이 된다
        [Header("가림 투명화")]
        [SerializeField] private float _occlusionRadius         = 2.2f;  // m, 초점 깊이에서 비우는 원의 반지름
        [SerializeField] private float _occlusionEdgeSoftness   = 0.4f;  // 0에서 1 사이 비율, 반지름 중 가장자리로 흐려지는 구간
        [SerializeField] private float _occlusionTargetMargin   = 0.6f;  // m, 초점 바로 앞 이 거리 안의 물체는 비우지 않는다
        [SerializeField] private float _occlusionFloorClearance = 0.35f; // m, 발밑에서 이 높이 아래는 비우지 않는다. 앞쪽 바닥이 뚫리지 않게 한다
        [SerializeField] private float _occlusionMinVisibility  = 0.2f;  // 0에서 1 사이 비율, 원 한가운데에서 남기는 픽셀 비율

        public IReadOnlyList<ZoomLevel> ZoomLevels              => _zoomLevels;
        public int                      DefaultZoomLevel        => _defaultZoomLevel;
        public float                    ZoomDuration            => _zoomDuration;
        public float                    YawAngle                => _yawAngle;
        public float                    FollowDamping           => _followDamping;
        public float                    OcclusionRadius         => _occlusionRadius;
        public float                    OcclusionEdgeSoftness   => _occlusionEdgeSoftness;
        public float                    OcclusionTargetMargin   => _occlusionTargetMargin;
        public float                    OcclusionFloorClearance => _occlusionFloorClearance;
        public float                    OcclusionMinVisibility  => _occlusionMinVisibility;

        /****************************************
        *                Methods
        ****************************************/

        protected override void ValidateValues(List<string> errors)
        {
            const string OWNER = nameof(CameraConfigSO);

            ValidateZoomLevels(errors, OWNER);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_zoomDuration), _zoomDuration);
            DataValidation.RequireRange(errors, OWNER, nameof(_yawAngle), _yawAngle, -MAX_YAW_ANGLE, MAX_YAW_ANGLE);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_followDamping), _followDamping);

            DataValidation.RequirePositive(errors, OWNER, nameof(_occlusionRadius), _occlusionRadius);
            DataValidation.RequireRange(errors, OWNER, nameof(_occlusionEdgeSoftness), _occlusionEdgeSoftness, 0f, MAX_RATE);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_occlusionTargetMargin), _occlusionTargetMargin);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_occlusionFloorClearance), _occlusionFloorClearance);
            DataValidation.RequireRange(errors, OWNER, nameof(_occlusionMinVisibility), _occlusionMinVisibility, 0f, MAX_MIN_VISIBILITY);
        }

        private void ValidateZoomLevels(List<string> errors, string owner)
        {
            if ((_zoomLevels == null) || (_zoomLevels.Length == 0))
            {
                errors.Add($"{owner}.{nameof(_zoomLevels)}: 단계가 없다");
                return;
            }

            for (int i = 0; i < _zoomLevels.Length; ++i)
            {
                ZoomLevel level = _zoomLevels[i];
                string    field = $"{nameof(_zoomLevels)}[{i}]";
                if (level == null)
                {
                    errors.Add($"{owner}.{field}: 비어 있다");
                    continue;
                }

                DataValidation.RequirePositiveAtMost(errors, owner, $"{field}.{nameof(ZoomLevel.PitchAngle)}", level.PitchAngle, MAX_PITCH_ANGLE);
                DataValidation.RequirePositiveAtMost(errors, owner, $"{field}.{nameof(ZoomLevel.FieldOfView)}", level.FieldOfView, MAX_FIELD_OF_VIEW);
                DataValidation.RequirePositive(errors, owner, $"{field}.{nameof(ZoomLevel.Distance)}", level.Distance);
                DataValidation.RequireNonNegative(errors, owner, $"{field}.{nameof(ZoomLevel.FocusHeight)}", level.FocusHeight);
            }

            DataValidation.RequireRange(errors, owner, nameof(_defaultZoomLevel), _defaultZoomLevel, 0, _zoomLevels.Length - 1);
        }
    }
}
