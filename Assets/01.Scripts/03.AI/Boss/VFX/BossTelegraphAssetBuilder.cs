#if UNITY_EDITOR
// System
using System.IO;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Art;

namespace Shinshi.Editor
{
    // 임시 표현 프리팹이다. 직사각형 예고와, 표현 진입점에 풀과 샘플 입력을 묶은 확인용 프리팹을 만든다
    // 부채꼴 프리팹(PF_SectorGroundVFX)은 SectorVFXAssetBuilder가 먼저 만들어 둔 것을 쓴다
    /// <summary> 보스 예고 표현 프리팹과 공유 머티리얼을 만든다 </summary>
    public static class BossTelegraphAssetBuilder
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string MENU_PATH            = "Shinshi/VFX/Create Boss Telegraph Prefabs";
        private const string OUTPUT_ROOT          = "Assets/01.Scripts/03.AI/Boss/VFX/Generated";
        private const string RECT_SHADER_NAME     = "Shinshi/VFX/RectGround";
        private const string AREA_SHADER_NAME     = "Shinshi/VFX/AreaGround";
        private const string AREA_MATERIAL_NAME   = "M_AreaGround";
        private const string AREA_PREFAB_NAME     = "PF_AreaGroundVFX";
        private const string AREA_POOL_NAME       = "Area Pool";
        private const string RECT_MATERIAL_NAME   = "M_RectGround";
        private const string RECT_PREFAB_NAME     = "PF_RectTelegraphVFX";
        private const string SECTOR_PREFAB_NAME   = "PF_SectorGroundVFX";
        private const string SAMPLE_PREFAB_NAME   = "PF_BossTelegraphSample";
        private const string SECTOR_POOL_NAME     = "Sector Pool";
        private const string RECT_POOL_NAME       = "Rect Pool";
        private const int    POOL_CAPACITY        = 4;

        /****************************************
        *                 Menu
        ****************************************/

        [MenuItem(MENU_PATH)]
        public static void CreatePrefabs()
        {
            if (GraphicsSettings.defaultRenderPipeline == null)
            {
                Debug.LogError("[BossTelegraph] URP 렌더 파이프라인 에셋이 필요하다");
                return;
            }

            Shader shader = Shader.Find(RECT_SHADER_NAME);
            Shader areaShader = Shader.Find(AREA_SHADER_NAME);
            GameObject sectorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OUTPUT_ROOT + "/" + SECTOR_PREFAB_NAME + ".prefab");
            if ((shader == null) || (areaShader == null) || (sectorPrefab == null))
            {
                Debug.LogError("[BossTelegraph] 셰이더나 부채꼴 프리팹이 없다, 임포트가 끝났는지와 Create Sector Ground Prefab을 먼저 했는지 확인한다");
                return;
            }

            Directory.CreateDirectory(OUTPUT_ROOT);
            AssetDatabase.Refresh();

            Material material = GetMaterial(RECT_MATERIAL_NAME, shader);
            RectTelegraphVFXController rectPrefab = CreateRectPrefab(material);
            AreaGroundVFXController areaPrefab = CreateAreaPrefab(GetMaterial(AREA_MATERIAL_NAME, areaShader));
            CreateSamplePrefab(sectorPrefab.GetComponent<SectorVFXController>(), rectPrefab, areaPrefab);

            AssetDatabase.SaveAssets();
            Debug.Log("[BossTelegraph] 임시 표현 프리팹을 만들었다 - " + OUTPUT_ROOT);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 이미 있으면 그대로 돌려줘서 손으로 고친 머티리얼 설정을 보존한다
        private static Material GetMaterial(string name, Shader shader)
        {
            string path = OUTPUT_ROOT + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Material material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static RectTelegraphVFXController CreateRectPrefab(Material material)
        {
            string path = OUTPUT_ROOT + "/" + RECT_PREFAB_NAME + ".prefab";
            GameObject root = new GameObject(RECT_PREFAB_NAME);
            try
            {
                root.AddComponent<RectTelegraphVFXController>();
                MeshRenderer surface = root.GetComponent<MeshRenderer>();
                surface.sharedMaterial = material;
                surface.shadowCastingMode = ShadowCastingMode.Off;
                surface.receiveShadows = false;

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                return saved.GetComponent<RectTelegraphVFXController>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static AreaGroundVFXController CreateAreaPrefab(Material material)
        {
            string path = OUTPUT_ROOT + "/" + AREA_PREFAB_NAME + ".prefab";
            GameObject root = new GameObject(AREA_PREFAB_NAME);
            try
            {
                root.AddComponent<AreaGroundVFXController>();
                MeshRenderer surface = root.GetComponent<MeshRenderer>();
                surface.sharedMaterial = material;
                surface.shadowCastingMode = ShadowCastingMode.Off;
                surface.receiveShadows = false;

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
                return saved.GetComponent<AreaGroundVFXController>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void CreateSamplePrefab(SectorVFXController sectorPrefab, RectTelegraphVFXController rectPrefab,
            AreaGroundVFXController areaPrefab)
        {
            string path = OUTPUT_ROOT + "/" + SAMPLE_PREFAB_NAME + ".prefab";
            GameObject root = new GameObject(SAMPLE_PREFAB_NAME);
            try
            {
                BossTelegraphPresenter presenter = root.AddComponent<BossTelegraphPresenter>();
                BossTelegraphSampleInput sample = root.AddComponent<BossTelegraphSampleInput>();

                SectorVFXPool sectorPool = MakePool<SectorVFXPool>(root.transform, SECTOR_POOL_NAME);
                RectTelegraphVFXPool rectPool = MakePool<RectTelegraphVFXPool>(root.transform, RECT_POOL_NAME);
                AreaGroundVFXPool areaPool = MakePool<AreaGroundVFXPool>(root.transform, AREA_POOL_NAME);

                SetReference(sectorPool, "_prefab", sectorPrefab);
                SetInt(sectorPool, "_capacity", POOL_CAPACITY);
                SetReference(rectPool, "_prefab", rectPrefab);
                SetInt(rectPool, "_capacity", POOL_CAPACITY);
                SetReference(areaPool, "_prefab", areaPrefab);
                SetInt(areaPool, "_capacity", POOL_CAPACITY);
                SetReference(presenter, "_areaPool", areaPool);
                SetReference(presenter, "_sectorPool", sectorPool);
                SetReference(presenter, "_rectPool", rectPool);
                SetReference(sample, "_presenter", presenter);

                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static T MakePool<T>(Transform parent, string name) where T : Component
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }

        private static void SetReference(Object target, string field, Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Object target, string field, int value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(field).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
