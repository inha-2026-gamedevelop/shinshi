// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary>마우스 커서 크기 단계의 저장값과 미리보기를 함께 처리한다</summary>
    public static class CursorScale
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PREFS_KEY     = "Game.CursorScale";
        public const int    DEFAULT_INDEX = 1;

        private static readonly float[]  G_Scales = { 0.75f, 1f, 1.5f };
        private static readonly string[] G_Labels = { "소 (75%)", "중 (100%)", "대 (150%)" };

        public static event Action<float> Changed;

        /****************************************
        *              Properties
        ****************************************/

        public static float Current { get; private set; } = 1f;

        public static int Count => G_Scales.Length;

        /****************************************
        *                Methods
        ****************************************/

        public static string Label(int index) { return G_Labels[Valid(index)]; }

        /// <summary>저장된 단계를 읽고 손상된 값은 중간 크기로 복구한다</summary>
        public static int Load()
        {
            return Valid(PlayerPrefs.GetInt(PREFS_KEY, DEFAULT_INDEX));
        }

        /// <summary>값을 저장하며 디스크 반영은 설정 창의 적용 경로에서 수행한다</summary>
        public static void Save(int index)
        {
            PlayerPrefs.SetInt(PREFS_KEY, Valid(index));
        }

        /// <summary>저장 없이 커서 크기를 미리 본다. 취소 시 마지막 적용값을 다시 전달한다</summary>
        public static void Preview(int index)
        {
            float scale = G_Scales[Valid(index)];
            if (Mathf.Approximately(Current, scale))
            {
                return;
            }

            Current = scale;
            Changed?.Invoke(scale);
        }

        private static int Valid(int index)
        {
            return ((index >= 0) && (index < G_Scales.Length)) ? index : DEFAULT_INDEX;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            Changed = null;
            Current = 1f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplySavedPreference()
        {
#if !UNITY_SERVER
            Preview(Load());
#endif
        }
    }
}
