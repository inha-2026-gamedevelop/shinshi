// System
using System;

// Unity
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Shinshi.AI
{
    /// <summary> 클립 안 FadeStart~FadeEnd초 동안 바인딩한 루트 아래 파티클을 원래 알파에서 0까지 낮추는 컷신 클립 </summary>
    [Serializable]
    public sealed class CutsceneParticleFadeClip : PlayableAsset, ITimelineClipAsset
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
            ScriptPlayable<CutsceneParticleFadeBehaviour> playable = ScriptPlayable<CutsceneParticleFadeBehaviour>.Create(graph);
            CutsceneParticleFadeBehaviour behaviour = playable.GetBehaviour();
            behaviour.FadeStart = _fadeStart;
            behaviour.FadeEnd = _fadeEnd;
            return playable;
        }
    }
}
