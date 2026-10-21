// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 월백 기믹 보호막·예고 이펙트를 지속 표시용으로 반복시킨다. 수명이 긴 메시 방출기는 개수를 제한해 유지한다. </summary>
    internal static class WolbaekParticleLoop
    {
        private const int LOOP_MAX_PARTICLES = 200; // 프리팹이 바뀌어도 한 방출기가 무한히 쌓이지 않게 하는 상한

        private const int MESH_MAX_PARTICLES = 2;       // 원본 보호막 구체 방출기가 한 번에 만드는 개수

        public static void Apply(GameObject root)
        {
            foreach (ParticleSystem particle in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                // 메시 방출기는 주기가 짧고 수명이 길어 그대로 반복시키면 수백 겹으로 쌓이므로, 원본 개수까지만 두어 수명이 끝나면 다시 채운다.
                ParticleSystemRenderer renderer = particle.GetComponent<ParticleSystemRenderer>();
                int cap = (renderer != null && renderer.renderMode == ParticleSystemRenderMode.Mesh)
                    ? MESH_MAX_PARTICLES : LOOP_MAX_PARTICLES;

                ParticleSystem.MainModule main = particle.main;
                main.loop = true;
                if (main.maxParticles > cap) main.maxParticles = cap;
            }
        }
    }
}
