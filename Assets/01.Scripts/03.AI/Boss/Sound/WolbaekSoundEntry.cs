// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 월백 효과음 한 종류의 클립과 재생 설정 </summary>
    [Serializable]
    public sealed class WolbaekSoundEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private EWolbaekSound _id        = EWolbaekSound.None;
        [SerializeField] private AudioClip     _clip      = null;
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;
        [SerializeField] private int           _maxVoices = 3; // 같은 소리가 동시에 울릴 수 있는 최대 수

        /****************************************
        *              Properties
        ****************************************/

        public EWolbaekSound Id        => _id;
        public AudioClip     Clip      => _clip;
        public float         Volume    => _volume;
        public int           MaxVoices => _maxVoices;
    }
}
