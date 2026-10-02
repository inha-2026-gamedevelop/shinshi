// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 쿼터뷰 카메라 시점 한 벌, 줌 단계 사이를 옮길 때 네 값을 함께 보간한다 </summary>
    public readonly struct QuarterViewPose
    {
        /****************************************
        *                Fields
        ****************************************/

        public float PitchAngle  { get; } // 도
        public float FieldOfView { get; } // 도
        public float Distance    { get; } // m
        public float FocusHeight { get; } // m

        /****************************************
        *                Methods
        ****************************************/

        public QuarterViewPose(float pitchAngle, float fieldOfView, float distance, float focusHeight)
        {
            PitchAngle  = pitchAngle;
            FieldOfView = fieldOfView;
            Distance    = distance;
            FocusHeight = focusHeight;
        }

        public static QuarterViewPose Lerp(in QuarterViewPose from, in QuarterViewPose to, float t)
        {
            return new QuarterViewPose(Mathf.Lerp(from.PitchAngle, to.PitchAngle, t), Mathf.Lerp(from.FieldOfView, to.FieldOfView, t),
                Mathf.Lerp(from.Distance, to.Distance, t), Mathf.Lerp(from.FocusHeight, to.FocusHeight, t));
        }
    }
}
