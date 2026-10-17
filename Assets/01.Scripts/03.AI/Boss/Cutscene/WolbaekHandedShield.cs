// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 50% 정식 보호막 인스턴스를 넘겨받아 컷신 투명도 곡선으로 마저 소멸시킨다. 정식 소멸(WolbaekShieldFade)은 끄고 이 객체만 알파를 쓴다.
    // 넘겨받는 순간의 알파를 기준으로 삼아 factor 1에서 지금 모습 그대로이고, 0이면 완전히 투명하다. 그래서 인계 순간 밝기가 튀지 않는다.
    // 알파를 누적해서 곱하지 않도록 파티클마다(randomSeed) 처음 본 알파를 기준값으로 기억하고 매번 기준값 x factor로 덮어쓴다
    /// <summary> 컷신이 넘겨받은 정식 보호막의 남은 소멸과 정리 </summary>
    public sealed class WolbaekHandedShield
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int INITIAL_BUFFER = 256;

        private readonly List<ParticleSystem>          _systems = new List<ParticleSystem>();
        private readonly List<Dictionary<uint, byte>>  _bases   = new List<Dictionary<uint, byte>>();
        private ParticleSystem.Particle[]              _buffer  = new ParticleSystem.Particle[INITIAL_BUFFER];
        private GameObject                             _shield;

        /****************************************
        *              Properties
        ****************************************/

        public GameObject Shield => _shield;

        public bool IsHeld => _shield != null;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 넘겨받는다. 새 파티클 방출을 멈추고 정식 소멸을 끈 뒤 지금 알파를 기준으로 기억한다 </summary>
        public void Capture(GameObject shield)
        {
            Release();
            if (shield == null)
            {
                return;
            }

            _shield = shield;
            if (shield.TryGetComponent(out WolbaekShieldFade fade))
            {
                fade.enabled = false;
            }

            shield.GetComponentsInChildren(true, _systems);
            for (int i = 0; i < _systems.Count; ++i)
            {
                _systems[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
                _bases.Add(new Dictionary<uint, byte>());
            }

            Apply(1f);
        }

        public void Apply(float factor)
        {
            if (_shield == null)
            {
                return;
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

                Dictionary<uint, byte> bases = _bases[i];
                int count = system.GetParticles(_buffer, alive);
                for (int j = 0; j < count; ++j)
                {
                    Color32 color = _buffer[j].startColor;
                    if (!bases.TryGetValue(_buffer[j].randomSeed, out byte baseAlpha))
                    {
                        baseAlpha = color.a;
                        bases[_buffer[j].randomSeed] = baseAlpha;
                    }

                    color.a = (byte)Mathf.RoundToInt(baseAlpha * Mathf.Clamp01(factor));
                    _buffer[j].startColor = color;
                }

                system.SetParticles(_buffer, count);
            }
        }

        /// <summary> 넘겨받은 보호막을 지운다. 컷신 쪽만 이 보호막의 정리를 책임진다 </summary>
        public void Release()
        {
            if (_shield != null)
            {
                _shield.SetActive(false);
                if (Application.isPlaying)
                {
                    Object.Destroy(_shield);
                }
                else
                {
                    Object.DestroyImmediate(_shield);
                }
            }

            _shield = null;
            _systems.Clear();
            _bases.Clear();
        }
    }
}
