#if SHINSHI_DEV_TEST
// System
using System.Reflection;

// Unity
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Dev
{
    // 시험 빌드에서 -devPerfTown으로 실행할 때만 동작한다. 마을 씬을 서버 없이 열면 따라갈 플레이어가 없어 카메라가 한 자리에 서므로,
    // 게임 카메라 설정(GameDB.Camera)의 줌 단계마다 스폰 지점과 광장 중앙을 차례로 보며 DevPerfLogger가 시점별 비용을 남기게 한다
    /// <summary> 마을 성능 측정용 고정 시점 순환과 설정 비교 인자 </summary>
    public sealed class DevTownPerfViews : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PERF_TOWN_ARG       = "-devPerfTown";
        private const string PERF_VIEW_ARG       = "-devPerfView";
        private const string LOD_BIAS_ARG        = "-devLodBias";
        private const string CASCADES_ARG        = "-devShadowCascades";
        private const string CASCADE_SPLIT_ARG   = "-devCascade2Split";
        private const string SHADOW_DISTANCE_ARG = "-devShadowDistance";
        private const string SOFT_SHADOW_ARG     = "-devSoftShadowMedium";
        private const string NO_SSAO_ARG         = "-devNoSsao";
        private const string SSAO_FEATURE_NAME   = "ScreenSpaceAmbientOcclusion";
        private const string SOFT_QUALITY_NAME   = "softShadowQuality";
        private const string CHARACTERS_ARG      = "-devPerfCharacters";
        private const string CHARACTERS_ROOT     = "DevPerfCharacters";
        private const float  CHARACTER_SPACING   = 1.5f;
        private const int    CLASS_COUNT         = 3;
        private const int    MAX_CHARACTERS      = 200;
        private const float  VIEW_SECONDS        = 8f;
        private const float  GROUND_RAY_HEIGHT   = 200f;
        private const float  GROUND_RAY_DISTANCE = 400f;
        private const int    MIN_CASCADES        = 1;
        private const int    MAX_CASCADES        = 4;

        // 측정 초점은 스폰 지점 묶음의 가운데와 광장 중앙이다
        private static readonly Vector2 _spawnFocus = new Vector2(0f, -17f);
        private static readonly Vector2 _plazaFocus = new Vector2(0f, 0f);

        private Camera         _camera     = null;
        private CameraConfigSO _config     = null;
        private string[]       _viewNames  = null;
        private int            _viewIndex  = -1;
        private int            _pinnedView = -1;
        private float          _nextSwitch = 0f;

        /// <summary> 현재 시점 이름, 측정 중이 아니면 빈 문자열이다 </summary>
        public static string Label { get; private set; } = string.Empty;

        /// <summary> 시점이 바뀔 때마다 오른다, 기록기가 측정 구간을 다시 시작하는 데 쓴다 </summary>
        public static int Version { get; private set; } = 0;

        /****************************************
        *              Unity Event
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (!DevArgs.Has(PERF_TOWN_ARG))
            {
                return;
            }

            var host = new GameObject(nameof(DevTownPerfViews));
            DontDestroyOnLoad(host);
            host.AddComponent<DevTownPerfViews>();
        }

        private void Awake()
        {
            ApplySettingArgs();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void LateUpdate()
        {
            if ((_camera == null) || (_viewNames == null))
            {
                return;
            }

            if ((Time.unscaledTime >= _nextSwitch) && ((_pinnedView < 0) || (_viewIndex != _pinnedView)))
            {
                _viewIndex  = (_pinnedView >= 0) ? _pinnedView : ((_viewIndex + 1) % _viewNames.Length);
                _nextSwitch = Time.unscaledTime + VIEW_SECONDS;
                Label       = _viewNames[_viewIndex];
                ++Version;
            }

            ApplyView(_viewIndex);
        }

        /****************************************
        *                Methods
        ****************************************/

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _camera    = null;
            _viewIndex = -1;
            Label      = string.Empty;
            ++Version;

            if (scene.name != Constants.Scene.SHINSHI_TOWN)
            {
                return;
            }

            _config = GameDB.Camera;
            if ((_config == null) || (_config.ZoomLevels.Count == 0))
            {
                Debug.LogError("[Perf] 카메라 설정이 없어 마을 측정 시점을 만들지 못했다");
                return;
            }

            // 시점 번호는 스폰 지점의 줌 단계들, 그 뒤 광장 중앙의 줌 단계들 순서다
            int zoomCount = _config.ZoomLevels.Count;
            _viewNames = new string[zoomCount * 2];
            for (int i = 0; i < zoomCount; ++i)
            {
                _viewNames[i]             = "spawn_zoom" + i;
                _viewNames[zoomCount + i] = "plaza_zoom" + i;
            }

            _pinnedView = -1;
            if (DevArgs.TryReadFloat(PERF_VIEW_ARG, out float view))
            {
                _pinnedView = Mathf.Clamp((int)view, 0, _viewNames.Length - 1);
            }

            // 따라갈 대상이 없는 Cinemachine이 카메라 위치를 되돌리지 않게 끈다
            _camera = Camera.main;
            if ((_camera != null) && _camera.TryGetComponent(out CinemachineBrain brain))
            {
                brain.enabled = false;
            }

            _nextSwitch = 0f;

            if ((DevArgs.TryReadFloat(CHARACTERS_ARG, out float characterCount)) && (characterCount > 0f))
            {
                SpawnPerfCharacters(Mathf.Min((int)characterCount, MAX_CHARACTERS));
            }
        }

        // 서버 없는 마을에는 P09 카탈로그를 들고 있는 프리팹이 없어, 이미 메모리에 올라온 카탈로그 에셋만 찾아 쓴다
        private static void SpawnPerfCharacters(int count)
        {
            PlayerCharacterLookCatalogSO catalog = null;
            PlayerCharacterLookCatalogSO[] catalogs = Resources.FindObjectsOfTypeAll<PlayerCharacterLookCatalogSO>();
            if (catalogs.Length > 0)
            {
                catalog = catalogs[0];
            }

            if (catalog == null)
            {
                Debug.LogError("[Perf] P09 카탈로그를 찾지 못했다");
                return;
            }

            var root    = new GameObject(CHARACTERS_ROOT);
            int columns = Mathf.CeilToInt(Mathf.Sqrt(count));
            int placed  = 0;
            for (int i = 0; i < count; ++i)
            {
                int classId = i % CLASS_COUNT;
                CharacterAppearance appearance = CharacterAppearanceCatalog.CreateDefault(classId);
                if ((appearance == null) || (!CharacterP09Look.TryFrom(appearance, out CharacterP09Look baseLook)))
                {
                    continue;
                }

                int bodyType   = ((i % 2) == 0) ? CharacterAppearance.BODY_TYPE_FEMALE : CharacterAppearance.BODY_TYPE_MALE;
                int facialHair = baseLook.FacialHair;
                int bustSize   = baseLook.BustSize;
                CharacterAppearanceCatalog.NormalizeForBodyType(bodyType, ref facialHair, ref bustSize);

                var look = new CharacterP09Look(bodyType, baseLook.FaceId, baseLook.HairId, baseLook.HairColor, baseLook.EyeColor,
                    baseLook.SkinTone, facialHair, bustSize, baseLook.OutfitMask);

                float x = ((i % columns) - ((columns - 1) * 0.5f)) * CHARACTER_SPACING;
                float z = _spawnFocus.y + ((i / columns) * CHARACTER_SPACING);
                var   position = new Vector3(_spawnFocus.x + x, 0f, z);
                if (Physics.Raycast(position + (Vector3.up * GROUND_RAY_HEIGHT), Vector3.down, out RaycastHit hit, GROUND_RAY_DISTANCE))
                {
                    position.y = hit.point.y;
                }

                var slot = new GameObject("PerfCharacter" + i);
                slot.transform.SetParent(root.transform, false);
                slot.transform.SetPositionAndRotation(position, Quaternion.identity);

                if (!PlayerCharacterLookAssembler.TryAssemble(catalog, look, slot.transform, out GameObject result, out string error))
                {
                    Debug.LogError("[Perf] P09 조립 실패: " + error);
                    continue;
                }

                ++placed;
            }

            Debug.Log("[Perf] P09 캐릭터 " + placed + "명 배치");
        }

        private void ApplyView(int index)
        {
            int     zoomCount = _config.ZoomLevels.Count;
            Vector2 focusXZ   = (index < zoomCount) ? _spawnFocus : _plazaFocus;
            var     focus     = new Vector3(focusXZ.x, 0f, focusXZ.y);

            CameraConfigSO.ZoomLevel zoom = _config.ZoomLevels[index % zoomCount];

            if (Physics.Raycast(focus + (Vector3.up * GROUND_RAY_HEIGHT), Vector3.down, out RaycastHit hit, GROUND_RAY_DISTANCE))
            {
                focus.y = hit.point.y;
            }

            _camera.fieldOfView = zoom.FieldOfView;
            _camera.transform.SetPositionAndRotation(
                focus + QuarterViewCameraRules.GetFollowOffset(zoom.PitchAngle, _config.YawAngle, zoom.Distance, zoom.FocusHeight),
                QuarterViewCameraRules.GetRotation(zoom.PitchAngle, _config.YawAngle));
        }

        // 빌드를 다시 하지 않고 설정별 비용을 비교하려고 품질, URP 값을 실행 인자로 바꾼다. 에셋 원본은 바뀌지 않는다
        private static void ApplySettingArgs()
        {
            if (DevArgs.TryReadFloat(LOD_BIAS_ARG, out float lodBias) && (lodBias > 0f))
            {
                QualitySettings.lodBias = lodBias;
            }

            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline))
            {
                return;
            }

            if (DevArgs.TryReadFloat(CASCADES_ARG, out float cascades))
            {
                int count = Mathf.RoundToInt(cascades);
                if ((count >= MIN_CASCADES) && (count <= MAX_CASCADES))
                {
                    pipeline.shadowCascadeCount = count;
                }
                else
                {
                    Debug.LogError("[Perf] 그림자 캐스케이드 수는 1에서 4 사이여야 한다: " + cascades);
                }
            }

            if (DevArgs.TryReadFloat(CASCADE_SPLIT_ARG, out float split) && (split > 0f) && (split < 1f))
            {
                pipeline.cascade2Split = split;
            }

            if (DevArgs.TryReadFloat(SHADOW_DISTANCE_ARG, out float shadowDistance) && (shadowDistance > 0f))
            {
                pipeline.shadowDistance = shadowDistance;
            }

            // 부드러운 그림자 품질은 URP가 공개하지 않은 속성이라 시험 빌드에서만 반사로 바꾼다
            if (DevArgs.Has(SOFT_SHADOW_ARG))
            {
                PropertyInfo quality = typeof(UniversalRenderPipelineAsset).GetProperty(SOFT_QUALITY_NAME,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (quality != null)
                {
                    quality.SetValue(pipeline, SoftShadowQuality.Medium);
                }
                else
                {
                    Debug.LogError("[Perf] 부드러운 그림자 품질 속성을 찾지 못했다");
                }
            }

            if (DevArgs.Has(NO_SSAO_ARG))
            {
                DisableSsao(pipeline);
            }
        }

        private static void DisableSsao(UniversalRenderPipelineAsset pipeline)
        {
            foreach (ScriptableRendererData data in pipeline.rendererDataList)
            {
                if (data == null)
                {
                    continue;
                }

                foreach (ScriptableRendererFeature feature in data.rendererFeatures)
                {
                    if ((feature != null) && (feature.GetType().Name == SSAO_FEATURE_NAME))
                    {
                        feature.SetActive(false);
                    }
                }
            }
        }
    }
}
#endif
