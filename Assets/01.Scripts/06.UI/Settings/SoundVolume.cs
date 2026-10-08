// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary>SoundVolume 저장값과 오디오 미리보기를 함께 처리한다</summary>
    [Serializable]
    public struct SoundVolume : IEquatable<SoundVolume>
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PREFS_KEY = "SoundVolume";

        [SerializeField] private float _master;
        [SerializeField] private float _bgm;
        [SerializeField] private float _sfx;
        [SerializeField] private bool  _muted;

        public static event Action<SoundVolume> PreviewChanged;

        /****************************************
        *              Properties
        ****************************************/

        public float Master => _master;
        public float Bgm    => _bgm;
        public float Sfx    => _sfx;
        public bool  Muted  => _muted;

        public static SoundVolume Default => new SoundVolume(1f, 1f, 1f, false);

        /****************************************
        *                Methods
        ****************************************/

        public SoundVolume(float master, float bgm, float sfx, bool muted)
        {
            _master = Mathf.Clamp01(master);
            _bgm    = Mathf.Clamp01(bgm);
            _sfx    = Mathf.Clamp01(sfx);
            _muted  = muted;
        }

        /// <summary>저장된 볼륨을 읽고 손상된 값은 기본값으로 복구한다</summary>
        public static SoundVolume Load()
        {
            if (!PlayerPrefs.HasKey(PREFS_KEY))
            {
                return Default;
            }

            try
            {
                SoundVolume value = JsonUtility.FromJson<SoundVolume>(PlayerPrefs.GetString(PREFS_KEY));
                return new SoundVolume(value.Master, value.Bgm, value.Sfx, value.Muted);
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning($"[Settings] 오디오 저장값을 읽을 수 없어 기본값을 사용합니다: {exception.Message}");
                return Default;
            }
        }

        /// <summary>값을 저장하며 디스크 반영은 설정 창의 적용 경로에서 수행한다</summary>
        public void Save()
        {
            PlayerPrefs.SetString(PREFS_KEY, JsonUtility.ToJson(this));
        }

        /// <summary>저장 없이 볼륨을 미리 듣는다. 취소 시 마지막 적용값을 다시 전달한다</summary>
        public void Preview()
        {
            AudioListener.volume = _muted ? 0f : _master;
            PreviewChanged?.Invoke(this);
        }

        public bool Equals(SoundVolume other)
        {
            return (Mathf.Approximately(_master, other._master))
                && (Mathf.Approximately(_bgm, other._bgm))
                && (Mathf.Approximately(_sfx, other._sfx))
                && (_muted == other._muted);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetEvents() { PreviewChanged = null; }
    }
}
