// System
using System;

// Unity
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Shinshi.AI.Preview
{
    /// <summary> 클립 안 FadeStart~FadeEnd초 동안 바인딩한 루트 아래 파티클을 원래 알파에서 0까지 낮추는 시안 전용 클립 </summary>
    [Serializable]
    public sealed class PreviewParticleFadeClip : PlayableAsset, ITimelineClipAsset
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private float _fadeStart = 0.1f;
        [SerializeField] private float _fadeEnd   = 0.7f;

        public float FadeStart { get => _fadeStart; set => _fadeStart = value; }
        public float FadeEnd   { get => _fadeEnd; set => _fadeEnd = value; }

        public ClipCaps clipCaps => ClipCaps.None;

        /****************************************
        *                Methods
        ****************************************/

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<PreviewParticleFadeBehaviour> playable = ScriptPlayable<PreviewParticleFadeBehaviour>.Create(graph);
            PreviewParticleFadeBehaviour behaviour = playable.GetBehaviour();
            behaviour.FadeStart = _fadeStart;
            behaviour.FadeEnd = _fadeEnd;
            return playable;
        }
    }
}
