// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> "그룹/동작" 한 칸의 클립과 재생 설정 </summary>
    [Serializable]
    public sealed class UiSoundEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private EUiSoundGroup  _group       = EUiSoundGroup.Common;
        [SerializeField] private EUiSoundAction _action      = EUiSoundAction.Click;
        [SerializeField] private AudioClip      _clip        = null;
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;
        [SerializeField] private int            _maxVoices   = 2;     // 같은 칸이 동시에 울릴 수 있는 최대 수
        [SerializeField] private float          _minInterval = 0.05f; // 같은 칸을 다시 울리기까지 최소 간격(초), 버튼 위를 훑을 때 겹침 방지

        /****************************************
        *              Properties
        ****************************************/

        public EUiSoundGroup  Group       => _group;
        public EUiSoundAction Action      => _action;
        public AudioClip      Clip        => _clip;
        public float          Volume      => _volume;
        public int            MaxVoices   => _maxVoices;
        public float          MinInterval => _minInterval;
    }
}
