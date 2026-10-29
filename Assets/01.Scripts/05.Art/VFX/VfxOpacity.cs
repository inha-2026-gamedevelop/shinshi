// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 파티클과 트레일 묶음에 투명도를 곱한다. 처음 건드릴 때 원본 색을 기억해 두고 값이 바뀔 때만 다시 계산한다 </summary>
    public static class VfxOpacity
    {
        /****************************************
        *                Fields
        ****************************************/

        private struct ParticleBase
        {
            public ParticleSystem.MinMaxGradient Color;
            public float                         Alpha;
        }

        private struct TrailBase
        {
            public Gradient Color;
            public float    Alpha;
        }

        private static readonly Dictionary<ParticleSystem, ParticleBase> G_Particles = new Dictionary<ParticleSystem, ParticleBase>();
        private static readonly Dictionary<TrailRenderer, TrailBase>     G_Trails    = new Dictionary<TrailRenderer, TrailBase>();

        /****************************************
        *                Methods
        ****************************************/

        public static void Apply(ParticleSystem[] systems, float alpha)
        {
            for (int i = 0; i < systems.Length; ++i)
            {
                Apply(systems[i], alpha);
            }
        }

        public static void Apply(TrailRenderer[] trails, float alpha)
        {
            for (int i = 0; i < trails.Length; ++i)
            {
                Apply(trails[i], alpha);
            }
        }

        public static void Apply(ParticleSystem system, float alpha)
        {
            if (system == null)
            {
                return;
            }

            alpha = Mathf.Clamp01(alpha);
            if (!G_Particles.TryGetValue(system, out ParticleBase origin))
            {
                if (alpha >= 0.999f)
                {
                    return;
                }

                origin = new ParticleBase { Color = system.main.startColor, Alpha = 1f };
            }

            if (Mathf.Approximately(origin.Alpha, alpha) && G_Particles.ContainsKey(system))
            {
                return;
            }

            ParticleSystem.MainModule main = system.main;
            main.startColor = Scale(origin.Color, alpha);
            origin.Alpha = alpha;
            G_Particles[system] = origin;
        }

        public static void Apply(TrailRenderer trail, float alpha)
        {
            if (trail == null)
            {
                return;
            }

            alpha = Mathf.Clamp01(alpha);
            if (!G_Trails.TryGetValue(trail, out TrailBase origin))
            {
                if (alpha >= 0.999f)
                {
                    return;
                }

                origin = new TrailBase { Color = trail.colorGradient, Alpha = 1f };
            }

            if (Mathf.Approximately(origin.Alpha, alpha) && G_Trails.ContainsKey(trail))
            {
                return;
            }

            trail.colorGradient = Scale(origin.Color, alpha);
            origin.Alpha = alpha;
            G_Trails[trail] = origin;
        }

        private static Color Scale(Color color, float alpha)
        {
            color.a *= alpha;
            return color;
        }

        private static Gradient Scale(Gradient source, float alpha)
        {
            GradientAlphaKey[] keys = source.alphaKeys;
            for (int i = 0; i < keys.Length; ++i)
            {
                keys[i].alpha *= alpha;
            }

            var result = new Gradient { mode = source.mode, colorSpace = source.colorSpace };
            result.SetKeys(source.colorKeys, keys);
            return result;
        }

        private static ParticleSystem.MinMaxGradient Scale(ParticleSystem.MinMaxGradient source, float alpha)
        {
            switch (source.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(Scale(source.color, alpha));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(Scale(source.colorMin, alpha), Scale(source.colorMax, alpha));
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(Scale(source.gradient, alpha));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(Scale(source.gradientMin, alpha), Scale(source.gradientMax, alpha));
                case ParticleSystemGradientMode.RandomColor:
                    return new ParticleSystem.MinMaxGradient(Scale(source.gradient, alpha))
                    {
                        mode = ParticleSystemGradientMode.RandomColor
                    };
                default:
                    return source;
            }
        }
    }
}
