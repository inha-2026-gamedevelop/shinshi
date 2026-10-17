// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 기믹 성공 때 보호막은 새 파티클을 멈춘 뒤 정해진 시간 끝에 지워진다. 일부 파티클 수명이 그 시간보다 길어 지울 때 한 번에 끊겨 보였다
    // 보호막 재질(Piloto Studio/UberFXSG)은 알파 혼합과 가산 모두 정점 색 알파가 0이면 화면에 남지 않는 것을 렌더 실측으로 확인했다
    // 그래서 이 인스턴스에 살아 있는 파티클의 정점 색 알파만 시간에 따라 낮춘다. 공유 재질과 원본 프리팹은 바꾸지 않는다
    /// <summary> 성공한 보호막 인스턴스의 남은 파티클을 지정한 시간 끝에 보이지 않게 서서히 낮춘다 </summary>
    public sealed class WolbaekShieldFade : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private ParticleSystem[] _systems;
        private ParticleSystem.Particle[][] _buffers;
        private Dictionary<uint, byte>[] _alphas;
        private float _startTime;
        private float _duration;

        public bool IsFading => _systems != null;
        public float Duration => _duration;

        /****************************************
        *              Unity Event
        ****************************************/

        private void LateUpdate()
        {
            if (_systems == null)
            {
                return;
            }

            ApplyAt(Time.time - _startTime);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 새 파티클을 멈추고 남은 파티클의 처음 알파를 기억한 뒤 지금부터 seconds 동안 낮춘다. 이미 소멸 중이면 다시 시작하지 않는다 </summary>
        public static WolbaekShieldFade Begin(GameObject shield, float seconds)
        {
            if (shield == null)
            {
                return null;
            }

            WolbaekShieldFade fade = shield.GetComponent<WolbaekShieldFade>();
            if (fade == null)
            {
                fade = shield.AddComponent<WolbaekShieldFade>();
            }

            if (!fade.IsFading)
            {
                fade.Capture(seconds);
            }

            return fade;
        }

        /// <summary> 시작 후 elapsed초 시점의 투명도를 남은 파티클에 적용한다. 지정 시간 이후에는 완전히 투명하다 </summary>
        public void ApplyAt(float elapsed)
        {
            if (_systems == null)
            {
                return;
            }

            float visibility = Visibility(elapsed / _duration);
            for (int i = 0; i < _systems.Length; ++i)
            {
                ParticleSystem system = _systems[i];
                if ((system == null) || (_buffers[i] == null))
                {
                    continue;
                }

                ParticleSystem.Particle[] buffer = _buffers[i];
                Dictionary<uint, byte> alphas = _alphas[i];
                int count = system.GetParticles(buffer);
                for (int j = 0; j < count; ++j)
                {
                    Color32 color = buffer[j].startColor;
                    byte original;
                    if (!alphas.TryGetValue(buffer[j].randomSeed, out original))
                    {
                        original = color.a;
                    }

                    color.a = (byte)Mathf.RoundToInt(original * visibility);
                    buffer[j].startColor = color;
                }

                system.SetParticles(buffer, count);
            }
        }

        /// <summary> 진행도 0에서 1, 1에서 0이 되고 시작과 끝이 부드러운 투명도 곡선 </summary>
        public static float Visibility(float progress)
        {
            return 1f - Mathf.SmoothStep(0f, 1f, progress);
        }

        private void Capture(float seconds)
        {
            _duration = Mathf.Max(seconds, Mathf.Epsilon);
            _startTime = Time.time;
            _systems = GetComponentsInChildren<ParticleSystem>(true);
            _buffers = new ParticleSystem.Particle[_systems.Length][];
            _alphas = new Dictionary<uint, byte>[_systems.Length];
            for (int i = 0; i < _systems.Length; ++i)
            {
                ParticleSystem system = _systems[i];
                system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                int alive = system.particleCount;
                _alphas[i] = new Dictionary<uint, byte>(alive);
                if (alive <= 0)
                {
                    continue;
                }

                // 방출을 멈췄으므로 이후 파티클은 줄기만 한다. 지금 수만큼만 담는다
                ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[alive];
                int count = system.GetParticles(buffer);
                for (int j = 0; j < count; ++j)
                {
                    _alphas[i][buffer[j].randomSeed] = buffer[j].startColor.a;
                }

                _buffers[i] = buffer;
            }
        }
    }
}
