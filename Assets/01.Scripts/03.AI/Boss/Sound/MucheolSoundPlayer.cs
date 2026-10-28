// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// Project
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.AI
{
    /// <summary> 무철 효과음을 정해진 수의 AudioSource로 재생한다. 서버 신호를 쓰지 않고 표현 코드가 부른다 </summary>
    public sealed class MucheolSoundPlayer : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    VOICE_COUNT        = 16;
        private const float  VOICE_MAX_DISTANCE = 60f;
        private const double LOOP_FOREVER       = double.MaxValue;

        [SerializeField] private MucheolSoundCatalogSO _catalog = null;
        [SerializeField, Range(0f, 1f)] private float _spatialBlend = 0.5f; // 0이면 화면 소리, 1이면 위치 소리

        private AudioSource[]        _voices    = null;
        private EMucheolSound[]      _voiceIds  = null;
        private float[]              _voiceBase = null; // 설정창 볼륨을 곱하기 전의 클립 볼륨
        private double[]             _voiceEnd  = null; // 칸이 비는 dsp 시각, 예약된 소리도 이 시각까지 차지한다
        private Dictionary<int, int> _loops     = null; // 호출자 키 -> 소리 칸 번호
        private float                _sfxVolume = 1f;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _voices    = new AudioSource[VOICE_COUNT];
            _voiceIds  = new EMucheolSound[VOICE_COUNT];
            _voiceBase = new float[VOICE_COUNT];
            _voiceEnd  = new double[VOICE_COUNT];
            _loops     = new Dictionary<int, int>();

            for (int i = 0; i < VOICE_COUNT; ++i)
            {
                GameObject voiceObject = new GameObject("Voice" + i);
                voiceObject.transform.SetParent(transform, false);

                AudioSource source = voiceObject.AddComponent<AudioSource>();
                source.playOnAwake  = false;
                source.spatialBlend = _spatialBlend;
                source.rolloffMode  = AudioRolloffMode.Linear;
                source.maxDistance  = VOICE_MAX_DISTANCE;
                _voices[i] = source;
            }
        }

        private void OnEnable()
        {
            SoundVolume.PreviewChanged += ApplyVolume;
            ApplyVolume(SoundVolume.Load());
        }

        private void OnDisable()
        {
            SoundVolume.PreviewChanged -= ApplyVolume;
            StopAll();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 한 번 재생한다. delaySeconds 뒤에 울리고, 같은 소리가 한도만큼 이미 울리면 건너뛴다 </summary>
        public void Play(EMucheolSound id, Vector3 position, float delaySeconds = 0f)
        {
            if (!TryStart(id, position, false, delaySeconds, out int index))
            {
                return;
            }

            ScheduleVoice(index, delaySeconds);
        }

        /// <summary> 반복 소리를 key로 시작한다, 같은 key가 이미 있으면 무시한다 </summary>
        public void StartLoop(int key, EMucheolSound id, Vector3 position, float delaySeconds = 0f)
        {
            if ((_loops == null) || (_loops.ContainsKey(key)))
            {
                return;
            }

            if (!TryStart(id, position, true, delaySeconds, out int index))
            {
                return;
            }

            _loops[key] = index;
            ScheduleVoice(index, delaySeconds);
        }

        public void StopLoop(int key)
        {
            if ((_loops == null) || (!_loops.TryGetValue(key, out int index)))
            {
                return;
            }

            _loops.Remove(key);
            ReleaseVoice(index);
        }

        /// <summary> 전투 종료, 취소, 사망 때 예약된 소리와 반복 소리까지 모두 멈춘다 </summary>
        public void StopAll()
        {
            if (_voices == null)
            {
                return;
            }

            _loops.Clear();
            for (int i = 0; i < _voices.Length; ++i)
            {
                ReleaseVoice(i);
            }
        }

        private bool TryStart(EMucheolSound id, Vector3 position, bool loop, float delaySeconds, out int index)
        {
            index = -1;

            if ((_voices == null) || (_catalog == null) || (!_catalog.TryGet(id, out MucheolSoundEntry entry)))
            {
                return false;
            }

            double now = AudioSettings.dspTime;
            int sameCount = 0;
            for (int i = 0; i < _voices.Length; ++i)
            {
                if (_voiceEnd[i] <= now)
                {
                    if (index < 0)
                    {
                        index = i;
                    }
                }
                else if (_voiceIds[i] == id)
                {
                    ++sameCount;
                }
            }

            if ((index < 0) || (sameCount >= entry.MaxVoices))
            {
                index = -1;
                return false;
            }

            AudioSource source = _voices[index];
            source.Stop();
            source.transform.position = position;
            source.clip   = entry.Clip;
            source.loop   = loop;
            source.volume = entry.Volume * _sfxVolume;

            _voiceIds[index]  = id;
            _voiceBase[index] = entry.Volume;
            _voiceEnd[index]  = loop ? LOOP_FOREVER : (now + Mathf.Max(0f, delaySeconds) + entry.Clip.length);
            return true;
        }

        private void ScheduleVoice(int index, float delaySeconds)
        {
            if (delaySeconds > 0f)
            {
                _voices[index].PlayScheduled(AudioSettings.dspTime + delaySeconds);
                return;
            }

            _voices[index].Play();
        }

        private void ReleaseVoice(int index)
        {
            AudioSource source = _voices[index];
            source.Stop();
            source.loop = false;
            source.clip = null;
            _voiceIds[index] = EMucheolSound.None;
            _voiceEnd[index] = 0d;
        }

        private void ApplyVolume(SoundVolume volume)
        {
            _sfxVolume = volume.Sfx;

            if (_voices == null)
            {
                return;
            }

            double now = AudioSettings.dspTime;
            for (int i = 0; i < _voices.Length; ++i)
            {
                if (_voiceEnd[i] > now)
                {
                    _voices[i].volume = _voiceBase[i] * _sfxVolume;
                }
            }
        }
    }
}
