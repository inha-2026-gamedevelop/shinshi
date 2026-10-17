// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.Playables;

namespace Shinshi.AI
{
    // 정식 WolbaekShieldFade처럼 살아 있는 파티클의 정점 색 알파만 낮춘다. 공유 재질과 원본 프리팹은 바꾸지 않는다
    // 다른 점은 시작 시각을 기억해 실제 시간으로 흐르게 하지 않고, 매 평가마다 클립 안 경과 시간으로 투명도를 정한다는 것이다
    // 그래서 재생, 스크러빙, 되감기, 반복 재생 어디서든 같은 시각이면 같은 투명도가 나온다
    //
    // 알파를 누적해서 곱하지 않도록 파티클마다(randomSeed) 처음 본 알파를 원래 값으로 기억하고, 매번 원래 값 x 투명도로 덮어쓴다
    // 처음 보는 파티클은 아직 한 번도 바꾸지 않았으므로 지금 알파가 곧 원래 값이다
    // 되감으면 ControlTrack이 파티클을 처음부터 다시 시뮬레이션하고 같은 seed의 파티클이 원래 색으로 다시 생기므로 기억한 값과 같다
    /// <summary> 바인딩한 루트 아래 파티클의 정점 알파를 클립 경과 시간에 따라 원래 값에서 0까지 낮춘다 </summary>
    public sealed class CutsceneParticleFadeBehaviour : PlayableBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        public float FadeStart;
        public float FadeEnd;

        private readonly Dictionary<ParticleSystem, Dictionary<uint, byte>> _originals = new Dictionary<ParticleSystem, Dictionary<uint, byte>>();
        private readonly List<ParticleSystem> _systems = new List<ParticleSystem>();
        private ParticleSystem.Particle[] _buffer = new ParticleSystem.Particle[256];

        /****************************************
        *            Playable Events
        ****************************************/

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            Transform root = playerData as Transform;
            if (root == null)
            {
                return;
            }

            Apply(root, Visibility((float)playable.GetTime(), FadeStart, FadeEnd));
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            _originals.Clear();
            _systems.Clear();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 클립 안 시각 time의 투명도. FadeStart까지 1, FadeEnd부터 0이고 사이는 정식 보호막 소멸과 같은 SmoothStep 곡선이다 </summary>
        public static float Visibility(float time, float fadeStart, float fadeEnd)
        {
            if (fadeEnd <= fadeStart)
            {
                return time < fadeEnd ? 1f : 0f;
            }

            float progress = Mathf.Clamp01((time - fadeStart) / (fadeEnd - fadeStart));
            return 1f - Mathf.SmoothStep(0f, 1f, progress);
        }

        // ControlTrack은 그래프를 만들 때 VFX 인스턴스를 만들고 그래프가 사라질 때 지우므로 한 그래프 안에서 목록이 바뀌지 않는다.
        // 그래서 처음 찾은 목록을 그래프 수명 동안 다시 쓰고, 매 프레임 계층을 다시 훑지 않는다
        private void Apply(Transform root, float visibility)
        {
            if (_systems.Count == 0)
            {
                root.GetComponentsInChildren(true, _systems);
            }

            for (int i = 0; i < _systems.Count; ++i)
            {
                ParticleSystem system = _systems[i];
                if (system == null)
                {
                    continue;
                }

                int alive = system.particleCount;
                if (alive <= 0)
                {
                    continue;
                }

                if (_buffer.Length < alive)
                {
                    _buffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(alive)];
                }

                Dictionary<uint, byte> originals;
                if (!_originals.TryGetValue(system, out originals))
                {
                    originals = new Dictionary<uint, byte>(alive);
                    _originals[system] = originals;
                }

                int count = system.GetParticles(_buffer, alive);
                for (int j = 0; j < count; ++j)
                {
                    Color32 color = _buffer[j].startColor;
                    uint seed = _buffer[j].randomSeed;
                    byte original;
                    if (!originals.TryGetValue(seed, out original))
                    {
                        original = color.a;
                        originals[seed] = original;
                    }

                    color.a = (byte)Mathf.RoundToInt(original * visibility);
                    _buffer[j].startColor = color;
                }

                system.SetParticles(_buffer, count);
            }
        }
    }
}
