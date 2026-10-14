// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 프리워밍 카탈로그의 항목 하나, 어떤 프리팹을 어떤 방식으로 몇 프레임 그릴지 정한다 </summary>
    [Serializable]
    public sealed class PrewarmCatalogEntry
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int DEFAULT_VISIBLE_FRAMES = 3;

        [SerializeField] private GameObject   _prefab        = null;
        [SerializeField] private EPrewarmKind _kind          = EPrewarmKind.Particle;
        [Tooltip("켠 채로 그릴 프레임 수")]
        [SerializeField] private int          _visibleFrames = DEFAULT_VISIBLE_FRAMES;
        [Tooltip("지연 방출처럼 특정 시점에만 그려지는 부분의 시점(초), 비면 재생 직후만 그린다")]
        [SerializeField] private float[]      _sampleSeconds = Array.Empty<float>();
        [Tooltip("실패 시 경고 대신 오류로 기록한다, 진입을 막지는 않는다")]
        [SerializeField] private bool         _required      = false;
        [Tooltip("트레일을 켜고 스테이지 안에서 이동시킨다")]
        [SerializeField] private bool         _moveTrail     = false;

        /****************************************
        *              Properties
        ****************************************/

        public GameObject   Prefab        => _prefab;
        public EPrewarmKind Kind          => _kind;
        public int          VisibleFrames => _visibleFrames;
        public float[]      SampleSeconds => _sampleSeconds;
        public bool         Required      => _required;
        public bool         MoveTrail     => _moveTrail;

        /****************************************
        *                Methods
        ****************************************/

        public PrewarmCatalogEntry() { }

        public PrewarmCatalogEntry(GameObject prefab, EPrewarmKind kind, int visibleFrames, float[] sampleSeconds,
            bool required, bool moveTrail)
        {
            _prefab        = prefab;
            _kind          = kind;
            _visibleFrames = visibleFrames;
            _sampleSeconds = sampleSeconds ?? Array.Empty<float>();
            _required      = required;
            _moveTrail     = moveTrail;
        }
    }
}
