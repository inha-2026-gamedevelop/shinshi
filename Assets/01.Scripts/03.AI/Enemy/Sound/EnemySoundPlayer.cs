// Unity
using UnityEngine;

// Project
using Shinshi.UI;

namespace Shinshi.AI
{
    /// <summary> 일반 적 효과음을 모든 적이 함께 쓰는 AudioSource 몇 개로 재생한다. 서버 신호를 쓰지 않고 표현 코드가 부른다 </summary>
    public sealed class EnemySoundPlayer : MonoBehaviour
    {
        // 적은 풀에서 많이 나오므로 적마다 재생기를 두지 않고, 처음 부를 때 만든 하나를 씬이 바뀌어도 이어서 쓴다.
        // 헤드리스 서버에서는 만들지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const string CATALOG_RESOURCE   = "EnemySoundCatalog";
        private const int    VOICE_COUNT        = 16;
        private const float  VOICE_MAX_DISTANCE = 40f;
        private const float  SPATIAL_BLEND      = 0.7f;

        private static EnemySoundPlayer s_instance;
        private static bool             s_missing;

        private EnemySoundCatalogSO _catalog;
        private AudioSource[]       _voices;
        private EEnemySound[]       _voiceIds;
        private float[]             _voiceBase; // 설정창 볼륨을 곱하기 전의 클립 볼륨
        private double[]            _voiceEnd;  // 칸이 비는 dsp 시각
        private float               _sfxVolume = 1f;

        /****************************************
        *              Unity Event
        ****************************************/

        // 도메인 리로드를 끈 플레이 모드 진입 때 지난 판의 참조가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            s_missing  = false;
        }

        private void OnEnable()
        {
            SoundVolume.PreviewChanged += ApplyVolume;
            ApplyVolume(SoundVolume.Load());
        }

        private void OnDisable()
        {
            SoundVolume.PreviewChanged -= ApplyVolume;
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> position에서 한 번 울린다. 같은 소리가 한도만큼 이미 울리면 건너뛴다 </summary>
        public static void Play(EEnemySound id, Vector3 position)
        {
#if !UNITY_SERVER
            if (Application.isBatchMode)
            {
                return;
            }

            if ((s_instance == null) && (!TryCreate()))
            {
                return;
            }

            s_instance.PlayInternal(id, position);
#endif
        }

        private static bool TryCreate()
        {
            if (s_missing)
            {
                return false;
            }

            EnemySoundCatalogSO catalog = Resources.Load<EnemySoundCatalogSO>(CATALOG_RESOURCE);
            if (catalog == null)
            {
                s_missing = true;
                return false;
            }

            GameObject host = new GameObject("EnemySoundPlayer");
            DontDestroyOnLoad(host);

            s_instance = host.AddComponent<EnemySoundPlayer>();
            s_instance.Init(catalog);
            return true;
        }

        private void Init(EnemySoundCatalogSO catalog)
        {
            _catalog   = catalog;
            _voices    = new AudioSource[VOICE_COUNT];
            _voiceIds  = new EEnemySound[VOICE_COUNT];
            _voiceBase = new float[VOICE_COUNT];
            _voiceEnd  = new double[VOICE_COUNT];

            for (int i = 0; i < VOICE_COUNT; ++i)
            {
                GameObject voiceObject = new GameObject("Voice" + i);
                voiceObject.transform.SetParent(transform, false);

                AudioSource source = voiceObject.AddComponent<AudioSource>();
                source.playOnAwake  = false;
                source.spatialBlend = SPATIAL_BLEND;
                source.rolloffMode  = AudioRolloffMode.Linear;
                source.maxDistance  = VOICE_MAX_DISTANCE;
                _voices[i] = source;
            }

            ApplyVolume(SoundVolume.Load());
        }

        private void PlayInternal(EEnemySound id, Vector3 position)
        {
            if (!_catalog.TryGet(id, out EnemySoundEntry entry))
            {
                return;
            }

            AudioClip clip = entry.PickClip();
            double now = AudioSettings.dspTime;
            int index = -1;
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
                return;
            }

            AudioSource source = _voices[index];
            source.Stop();
            source.transform.position = position;
            source.clip   = clip;
            source.volume = entry.Volume * _sfxVolume;
            source.Play();

            _voiceIds[index]  = id;
            _voiceBase[index] = entry.Volume;
            _voiceEnd[index]  = now + clip.length;
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
