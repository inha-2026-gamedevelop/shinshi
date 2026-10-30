// Unity
using UnityEngine;

// 서드파티
using Mirror;

// Project
using Shinshi.UI;

namespace Shinshi.AI
{
    /// <summary> 월백 전투의 페이즈에 맞춰 BGM을 바꾼다. 서버가 복제한 BossState를 읽기만 하고 서버 신호를 보내지 않는다 </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class WolbaekBgmPlayer : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float FIND_INTERVAL_SECONDS = 0.5f;
        private const ushort PHASE2_GENERATION    = 1;

        [SerializeField] private AudioClip _phase1Clip = null;
        [SerializeField] private AudioClip _phase2Clip = null;
        [SerializeField, Range(0f, 1f)] private float _volume = 0.5f;
        [SerializeField] private float _fadeSeconds = 1.5f;
        [SerializeField] private bool  _loop        = true;

        private AudioSource _source       = null;
        private BossState   _state        = null;
        private AudioClip   _playing      = null;
        private float       _fade         = 0f;
        private float       _bgmVolume    = 1f;
        private float       _nextFindTime = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _source              = GetComponent<AudioSource>();
            _source.playOnAwake  = false;
            _source.spatialBlend = 0f;
        }

        private void OnEnable()
        {
            SoundVolume.PreviewChanged += ApplyBgmVolume;
            ApplyBgmVolume(SoundVolume.Load());
        }

        private void OnDisable()
        {
            SoundVolume.PreviewChanged -= ApplyBgmVolume;
            _source.Stop();
            _playing = null;
            _fade    = 0f;
        }

        private void Update()
        {
            AudioClip desired = ResolveClip();
            float step = (_fadeSeconds > 0f) ? (Time.unscaledDeltaTime / _fadeSeconds) : 1f;

            if (desired != _playing)
            {
                _fade = Mathf.Max(0f, _fade - step);
                if (_fade <= 0f)
                {
                    SwapClip(desired);
                }
            }
            else if (_playing != null)
            {
                _fade = Mathf.Min(1f, _fade + step);
            }

            _source.volume = _volume * _fade * _bgmVolume;
        }

        /****************************************
        *                Methods
        ****************************************/

        // 전투 중이 아니거나 보스가 없으면 비운다. 페이즈 세대가 오르면 2페이즈 곡이다
        private AudioClip ResolveClip()
        {
            if (_state == null)
            {
                if (Time.unscaledTime >= _nextFindTime)
                {
                    _nextFindTime = Time.unscaledTime + FIND_INTERVAL_SECONDS;
                    _state        = FindSpawnedBossState();
                }

                return null;
            }

            if (_state.FlowState != EBossFlowState.Combat)
            {
                return null;
            }

            return (_state.PhaseGeneration >= PHASE2_GENERATION) ? _phase2Clip : _phase1Clip;
        }

        // 보스는 전투가 열릴 때 네트워크로 스폰돼서 씬을 순회하지 않고 스폰된 개체 목록에서 찾는다. 0.5초마다만 부른다
        private static BossState FindSpawnedBossState()
        {
            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                if ((identity != null) && (identity.TryGetComponent(out BossState state)))
                {
                    return state;
                }
            }

            return null;
        }

        private void SwapClip(AudioClip clip)
        {
            _source.Stop();
            _source.clip = clip;
            _source.loop = _loop;
            _playing     = clip;

            if (clip != null)
            {
                _source.Play();
            }
        }

        private void ApplyBgmVolume(SoundVolume volume)
        {
            _bgmVolume = volume.Bgm;
        }
    }
}
