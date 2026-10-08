// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary>지정된 AudioSource에 BGM 또는 SFX 설정값을 곱해 적용한다</summary>
    public sealed class SettingsAudioChannel : MonoBehaviour
    {
        public enum EChannel
        {
            Bgm,
            Sfx,
        }

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private AudioSource _source  = null;
        [SerializeField] private EChannel    _channel = EChannel.Sfx;

        private float _sourceVolume = 1f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_source != null)
            {
                _sourceVolume = _source.volume;
            }
        }

        private void OnEnable()
        {
            SoundVolume.PreviewChanged += Apply;
            Apply(SoundVolume.Load());
        }

        private void OnDisable()
        {
            SoundVolume.PreviewChanged -= Apply;
            if (_source != null)
            {
                _source.volume = _sourceVolume;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void Apply(SoundVolume volume)
        {
            if (_source == null)
            {
                return;
            }

            _source.volume = _sourceVolume * ((_channel == EChannel.Bgm) ? volume.Bgm : volume.Sfx);
        }
    }
}
