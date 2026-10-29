// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 무철 효과음 한 종류의 클립과 재생 설정 </summary>
    [Serializable]
    public sealed class MucheolSoundEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private EMucheolSound _id        = EMucheolSound.None;
        [SerializeField] private AudioClip     _clip      = null;
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;
        [SerializeField] private int           _maxVoices = 3; // 같은 소리가 동시에 울릴 수 있는 최대 수

        /****************************************
        *              Properties
        ****************************************/

        public EMucheolSound Id        => _id;
        public AudioClip     Clip      => _clip;
        public float         Volume    => _volume;
        public int           MaxVoices => _maxVoices;
    }
}
