// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> 곡 목록에서 무작위로 골라 한 곡씩 이어 재생한다. 같은 곡이 연달아 나오지 않는다 </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class BgmPlaylist : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private AudioClip[] _clips = null;

        private AudioSource _source    = null;
        private int         _lastIndex = -1;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _source             = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop        = false;
        }

        private void OnEnable()
        {
            PlayNext();
        }

        // 곡이 끝나면 다음 곡을 고른다. 일시 정지 중에는 isPlaying이 거짓이어도 넘기지 않는다
        private void Update()
        {
            if ((!_source.isPlaying) && (!AudioListener.pause))
            {
                PlayNext();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void PlayNext()
        {
            if ((_clips == null) || (_clips.Length == 0))
            {
                return;
            }

            int index = Random.Range(0, _clips.Length);
            if ((_clips.Length > 1) && (index == _lastIndex))
            {
                index = (index + 1 + Random.Range(0, _clips.Length - 1)) % _clips.Length;
            }

            _lastIndex   = index;
            _source.clip = _clips[index];
            _source.Play();
        }
    }
}
