// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 일반 적 효과음 한 종류의 클립과 재생 설정 </summary>
    [Serializable]
    public sealed class EnemySoundEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private EEnemySound _id        = EEnemySound.None;
        [SerializeField] private AudioClip[] _clips     = Array.Empty<AudioClip>(); // 둘 이상이면 울릴 때마다 무작위로 고른다
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;
        [SerializeField] private int         _maxVoices = 3; // 같은 소리가 동시에 울릴 수 있는 최대 수

        /****************************************
        *              Properties
        ****************************************/

        public EEnemySound Id        => _id;
        public float       Volume    => _volume;
        public int         MaxVoices => _maxVoices;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 울릴 클립을 고른다, 쓸 수 있는 클립이 없으면 null </summary>
        public AudioClip PickClip()
        {
            if ((_clips == null) || (_clips.Length == 0))
            {
                return null;
            }

            return _clips[(_clips.Length == 1) ? 0 : UnityEngine.Random.Range(0, _clips.Length)];
        }
    }
}
