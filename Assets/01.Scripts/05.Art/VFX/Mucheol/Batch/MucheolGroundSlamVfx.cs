// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 무철 지면 강타 연출 한 벌을 확정된 타격 시각부터 재생한다, 게임 판정은 하지 않는다 </summary>
    public sealed class MucheolGroundSlamVfx : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        [Header("지면 강타 프리팹 인스턴스, 켜 둔 채 저장하지 않는다")]
        [SerializeField] private Animation        _animation = null;
        [SerializeField] private AnimationClip    _clip      = null;
        [SerializeField] private ParticleSystem[] _particles = null;

        private bool _playing;

        /****************************************
        *              Properties
        ****************************************/

        public float Duration => (_clip != null) ? _clip.length : 0f;
        public bool  IsPlaying => _playing;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            Stop();
        }

        protected override void OnServerBuildAwake()
        {
            Stop();
        }

        private void OnDisable()
        {
            Stop();
        }

        private void Update()
        {
            if ((_playing) && (!_animation.isPlaying))
            {
                Stop();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 타격 후 경과한 시간부터 이어서 재생한다 </summary>
        public void Play(float elapsedSeconds)
        {
            if (!CanPlay(elapsedSeconds))
            {
                return;
            }

            float elapsed = Mathf.Max(0f, elapsedSeconds);
            if (elapsed >= _clip.length)
            {
                Stop();
                return;
            }

            _animation.gameObject.SetActive(true);
            _animation.Stop();
            _animation.Play(_clip.name);
            _animation[_clip.name].time = elapsed;
            _animation.Sample();
            PlayParticles(elapsed);
            _playing = true;
        }

        /// <summary> Inspector 스크러빙용, 타격 후 경과 시간 하나를 결정적으로 그린다 </summary>
        public void Sample(float elapsedSeconds)
        {
            if (!CanPlay(elapsedSeconds))
            {
                return;
            }

            if ((elapsedSeconds < 0f) || (elapsedSeconds >= _clip.length))
            {
                Stop();
                return;
            }

            _animation.gameObject.SetActive(true);
            _animation.Stop();
            _clip.SampleAnimation(_animation.gameObject, elapsedSeconds);
            for (int i = 0; i < _particles.Length; ++i)
            {
                if (_particles[i] != null)
                {
                    _particles[i].Simulate(elapsedSeconds, true, true, true);
                }
            }

            _playing = false;
        }

        /// <summary> 연출을 끄고 처음 상태로 되돌린다 </summary>
        public void Stop()
        {
            _playing = false;
            if (_animation == null)
            {
                return;
            }

            _animation.Stop();
            if (_particles != null)
            {
                for (int i = 0; i < _particles.Length; ++i)
                {
                    if (_particles[i] != null)
                    {
                        _particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }
            }

            _animation.gameObject.SetActive(false);
        }

        private bool CanPlay(float elapsedSeconds)
        {
            return (!IsServerBuild) && (isActiveAndEnabled) && (_animation != null) && (_clip != null)
                && (_particles != null) && (!float.IsNaN(elapsedSeconds)) && (!float.IsInfinity(elapsedSeconds));
        }

        private void PlayParticles(float elapsed)
        {
            for (int i = 0; i < _particles.Length; ++i)
            {
                if (_particles[i] == null)
                {
                    continue;
                }

                _particles[i].Simulate(elapsed, true, true, true);
                _particles[i].Play(true);
            }
        }
    }
}
