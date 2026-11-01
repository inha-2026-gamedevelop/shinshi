// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary>쿼터뷰 카메라 구도 보정(각도, 거리)의 저장값과 미리보기를 함께 처리한다</summary>
    public static class CameraComposition
    {
        // 슬라이더 값은 0에서 1이며 0.5가 줌 단계 그대로다. 보정은 줌 단계 값에 겹쳐 쓰므로 휠 줌과 충돌하지 않는다

        /****************************************
        *                Fields
        ****************************************/

        public const string PITCH_PREFS_KEY    = "Game.CameraPitch";
        public const string DISTANCE_PREFS_KEY = "Game.CameraDistance";
        public const float  DEFAULT_RATE       = 0.5f;

        public const float MAX_PITCH_OFFSET   = 10f;   // 도, 슬라이더 양끝에서 줌 단계 각도에 더하고 빼는 값
        public const float MIN_DISTANCE_SCALE = 0.75f; // 슬라이더 왼쪽 끝에서 줌 단계 거리에 곱하는 값
        public const float MAX_DISTANCE_SCALE = 1.25f; // 슬라이더 오른쪽 끝에서 줌 단계 거리에 곱하는 값

        public static event Action Changed;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 줌 단계 각도에 더할 값, 도. 양수면 더 위에서 내려다본다 </summary>
        public static float PitchOffset { get; private set; } = 0f;

        /// <summary> 줌 단계 거리에 곱할 값, 1이면 줌 단계 그대로다 </summary>
        public static float DistanceScale { get; private set; } = 1f;

        /****************************************
        *                Methods
        ****************************************/

        public static float ToPitchOffset(float rate)
        {
            return Mathf.Lerp(-MAX_PITCH_OFFSET, MAX_PITCH_OFFSET, Valid(rate));
        }

        public static float ToDistanceScale(float rate)
        {
            return Mathf.Lerp(MIN_DISTANCE_SCALE, MAX_DISTANCE_SCALE, Valid(rate));
        }

        /// <summary>저장된 각도 슬라이더 값을 읽고 손상된 값은 기본값으로 복구한다</summary>
        public static float LoadPitch()
        {
            return Valid(PlayerPrefs.GetFloat(PITCH_PREFS_KEY, DEFAULT_RATE));
        }

        /// <summary>저장된 거리 슬라이더 값을 읽고 손상된 값은 기본값으로 복구한다</summary>
        public static float LoadDistance()
        {
            return Valid(PlayerPrefs.GetFloat(DISTANCE_PREFS_KEY, DEFAULT_RATE));
        }

        /// <summary>값을 저장하며 디스크 반영은 설정 창의 적용 경로에서 수행한다</summary>
        public static void Save(float pitchRate, float distanceRate)
        {
            PlayerPrefs.SetFloat(PITCH_PREFS_KEY, Valid(pitchRate));
            PlayerPrefs.SetFloat(DISTANCE_PREFS_KEY, Valid(distanceRate));
        }

        /// <summary>저장 없이 구도를 바꿔 본다. 취소 시 마지막 적용값을 다시 전달한다</summary>
        public static void Preview(float pitchRate, float distanceRate)
        {
            float pitch    = ToPitchOffset(pitchRate);
            float distance = ToDistanceScale(distanceRate);
            if ((Mathf.Approximately(PitchOffset, pitch)) && (Mathf.Approximately(DistanceScale, distance)))
            {
                return;
            }

            PitchOffset   = pitch;
            DistanceScale = distance;
            Changed?.Invoke();
        }

        private static float Valid(float value)
        {
            return float.IsNaN(value) ? DEFAULT_RATE : Mathf.Clamp01(value);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            Changed       = null;
            PitchOffset   = 0f;
            DistanceScale = 1f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplySavedPreference()
        {
#if !UNITY_SERVER
            Preview(LoadPitch(), LoadDistance());
#endif
        }
    }
}
