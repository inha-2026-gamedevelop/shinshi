// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> UI 효과음을 정해진 수의 2D AudioSource로 재생한다. UiSound가 만들고 부른다 </summary>
    public sealed class UiSoundPlayer : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int VOICE_COUNT = 8;

        private UiSoundCatalogSO                _catalog      = null;
        private AudioSource[]                   _voices       = null;
        private UiSoundEntry[]                  _voiceEntries = null;
        private Dictionary<UiSoundEntry, float> _lastPlayed   = null; // 항목 -> 마지막 재생 시각(unscaled 초)
        private float                           _sfxVolume    = 1f;
        private readonly System.Random           _random       = new System.Random();

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _voices       = new AudioSource[VOICE_COUNT];
            _voiceEntries = new UiSoundEntry[VOICE_COUNT];
            _lastPlayed   = new Dictionary<UiSoundEntry, float>();

            for (int i = 0; i < VOICE_COUNT; ++i)
            {
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake         = false;
                source.spatialBlend        = 0f;
                source.ignoreListenerPause = true; // 일시 정지 화면의 단추도 소리를 내야 한다
                _voices[i] = source;
            }
        }

        // 설정에 UI 볼륨이 따로 없어 효과음 볼륨을 따른다
        private void OnEnable()
        {
            SoundVolume.PreviewChanged += ApplyVolume;
            ApplyVolume(SoundVolume.Load());
        }

        private void OnDisable()
        {
            SoundVolume.PreviewChanged -= ApplyVolume;
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Initialize(UiSoundCatalogSO catalog) { _catalog = catalog; }

        /// <summary> 칸이 비었거나, 최소 간격 안이거나, 같은 칸이 한도만큼 울리고 있으면 건너뛴다 </summary>
        public void Play(EUiSoundGroup group, EUiSoundAction action)
        {
            if ((_catalog == null) || (_voices == null) || (!_catalog.TryGet(group, action, out UiSoundEntry entry)))
            {
                return;
            }

            float now = Time.unscaledTime;
            if ((_lastPlayed.TryGetValue(entry, out float last)) && ((now - last) < entry.MinInterval))
            {
                return;
            }

            int index     = -1;
            int sameCount = 0;
            for (int i = 0; i < _voices.Length; ++i)
            {
                if (!_voices[i].isPlaying)
                {
                    if (index < 0)
                    {
                        index = i;
                    }
                }
                else if (_voiceEntries[i] == entry)
                {
                    ++sameCount;
                }
            }

            if ((index < 0) || (sameCount >= entry.MaxVoices))
            {
                return;
            }

            AudioSource source = _voices[index];
            source.clip   = PickClip(entry);
            source.volume = entry.Volume * _sfxVolume;
            source.Play();

            _voiceEntries[index] = entry;
            _lastPlayed[entry]   = now;
        }

        private AudioClip PickClip(UiSoundEntry entry)
        {
            AudioClip[] variants = entry.Variants;
            int count = 1;
            if (variants != null)
            {
                foreach (AudioClip variant in variants)
                {
                    if (variant != null)
                    {
                        ++count;
                    }
                }
            }

            int selected = _random.Next(count);
            if ((selected == 0) || (variants == null))
            {
                return entry.Clip;
            }

            foreach (AudioClip variant in variants)
            {
                if ((variant != null) && (--selected == 0))
                {
                    return variant;
                }
            }

            return entry.Clip;
        }

        private void ApplyVolume(SoundVolume volume)
        {
            _sfxVolume = volume.Sfx;

            if (_voices == null)
            {
                return;
            }

            for (int i = 0; i < _voices.Length; ++i)
            {
                if (_voiceEntries[i] != null)
                {
                    _voices[i].volume = _voiceEntries[i].Volume * _sfxVolume;
                }
            }
        }
    }
}
