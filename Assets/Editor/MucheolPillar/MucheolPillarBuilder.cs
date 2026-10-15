// System
using System.IO;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.Editor
{
    /// <summary> 무철 석주 FBX와 텍스처로 재질, LOD 프리팹을 만들고 무철 보스 씬에 3개를 배치한다 </summary>
    public static class MucheolPillarBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ROOT      = "Assets/01.Scripts/05.Art/Environment/MucheolPillar";
        private const string PREFAB    = "Assets/02.Prefab/Environment/MucheolPillar/PF_MucheolPillar.prefab";
        private const string SCENE     = "Assets/00.Scenes/Dev_HUD/Dev_BossMucheol.unity";
        private const string LIT_SHADER = "Universal Render Pipeline/Lit";
        private const string CLOTH_SHADER = "Shinshi/Environment/Lit Occlusion Fade Wind Cloth";

        private const string GROUP_NAME   = "MucheolPillars";
        private const string GROUND_NAME  = "Collider_Ground";

        private const int   PILLAR_COUNT   = 3;
        private const float RING_RADIUS    = 13.5f;
        private const float START_ANGLE    = 90f;
        private const float RAY_HEIGHT     = 60f;
        private const float RAY_DISTANCE   = 120f;
        private const float FULL_CIRCLE    = 360f;

        private const float LOD0_HEIGHT    = 0.45f;
        private const float LOD1_HEIGHT    = 0.15f;
        private const float LOD2_HEIGHT    = 0.04f;

        private const float COLLIDER_RADIUS = 1.2f;
        private const float COLLIDER_HEIGHT = 3.4f;

        private const float BANNER_SMOOTHNESS = 0.2f;
        private const float CLOTH_TOP_Y       = 2.8f;
        private const float CLOTH_LENGTH      = 1.68f;
        private const float WIND_AMPLITUDE    = 0.14f;
        private const float WIND_SPEED        = 1.4f;
        private const float WIND_FLUTTER      = 0.04f;
        private const float WIND_DIR_X        = -0.57f;
        private const float WIND_DIR_Z        = 0.82f;

        private const string BANNER_NODE      = "SM_MucheolPillar_RedBanner";
        private const float BANNER_OFFSET_X   = 0.24f;
        private const float BANNER_OFFSET_Y   = 0.16f;
        private const float BANNER_OFFSET_Z   = 0.71f;
        private const float BANNER_YAW        = 290.52f;

        private static readonly string[] LOD_NAMES = { "High", "Medium", "Low" };

        /****************************************
        *               Methods
        ****************************************/

        [MenuItem("Shinshi/Art/Build Mucheol Pillar And Place 3")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            ConfigureTextures();
            Material stone  = BuildStoneMaterial();
            Material banner = BuildBannerMaterial();
            GameObject prefab = BuildPrefab(stone, banner);
            MucheolPillarShatterBuilder.Apply();
            PlacePillars(prefab);

            AssetDatabase.SaveAssets();
            Debug.Log("[MucheolPillarBuilder] 석주 프리팹 생성과 3개 배치를 마쳤다");
        }

        /// <summary> 텍스처 임포트 설정을 용도별로 맞추고 거칠기를 매끄러움 알파로 바꾼 팩 텍스처를 만든다 </summary>
        private static void ConfigureTextures()
        {
            SetImporter(ROOT + "/Textures/T_MucheolPillar_Meshy_base_color.png", false, false);
            SetImporter(ROOT + "/Textures/T_MucheolPillar_Banner_BaseColor.png", false, false);
            SetImporter(ROOT + "/Textures/T_MucheolPillar_Meshy_normal.png", true, false);
            SetImporter(ROOT + "/Textures/T_MucheolPillar_Meshy_AO.png", false, true);
            SetImporter(ROOT + "/Textures/T_MucheolPillar_Meshy_metallic.png", false, true, true);
            SetImporter(ROOT + "/Textures/T_MucheolPillar_Meshy_roughness.png", false, true, true);

            PackMetallicSmoothness();
            SetImporter(ROOT + "/Textures/T_MucheolPillar_MetallicSmoothness.png", false, true);
        }

        private static void SetImporter(string path, bool normalMap, bool linear, bool readable = false)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }

            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normalMap && !linear;
            importer.isReadable = readable;
            importer.maxTextureSize = 2048;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }

        /// <summary> R은 금속성, A는 1 - 거칠기로 담아 URP Lit의 금속성·매끄러움 맵 규약에 맞춘다 </summary>
        private static void PackMetallicSmoothness()
        {
            Texture2D metallic  = AssetDatabase.LoadAssetAtPath<Texture2D>(ROOT + "/Textures/T_MucheolPillar_Meshy_metallic.png");
            Texture2D roughness = AssetDatabase.LoadAssetAtPath<Texture2D>(ROOT + "/Textures/T_MucheolPillar_Meshy_roughness.png");

            int width  = roughness.width;
            int height = roughness.height;
            Color32[] rough = roughness.GetPixels32();
            Color32[] metal = metallic.width == width && metallic.height == height ? metallic.GetPixels32() : null;
            Color32[] packed = new Color32[rough.Length];

            for (int i = 0; i < packed.Length; i++)
            {
                byte m = metal != null ? metal[i].r : (byte)0;
                packed[i] = new Color32(m, 0, 0, (byte)(255 - rough[i].r));
            }

            Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            result.SetPixels32(packed);
            result.Apply();
            File.WriteAllBytes(ROOT + "/Textures/T_MucheolPillar_MetallicSmoothness.png", result.EncodeToPNG());
            Object.DestroyImmediate(result);

            AssetDatabase.ImportAsset(ROOT + "/Textures/T_MucheolPillar_MetallicSmoothness.png", ImportAssetOptions.ForceSynchronousImport);
        }

        private static Material BuildStoneMaterial()
        {
            Material material = LoadOrCreateMaterial("M_MucheolPillar_Stone", LIT_SHADER);
            material.SetTexture("_BaseMap", Load("T_MucheolPillar_Meshy_base_color.png"));
            material.SetTexture("_BumpMap", Load("T_MucheolPillar_Meshy_normal.png"));
            material.SetTexture("_MetallicGlossMap", Load("T_MucheolPillar_MetallicSmoothness.png"));
            material.SetTexture("_OcclusionMap", Load("T_MucheolPillar_Meshy_AO.png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_Metallic", 1f);
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.EnableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material BuildBannerMaterial()
        {
            Material material = LoadOrCreateMaterial("M_MucheolPillar_Banner", CLOTH_SHADER);
            material.SetFloat("_ClothTopY", CLOTH_TOP_Y);
            material.SetFloat("_ClothLength", CLOTH_LENGTH);
            material.SetFloat("_WindAmplitude", WIND_AMPLITUDE);
            material.SetFloat("_WindSpeed", WIND_SPEED);
            material.SetFloat("_WindFlutter", WIND_FLUTTER);
            material.SetVector("_WindDir", new Vector4(WIND_DIR_X, WIND_DIR_Z, 0f, 0f));
            material.SetTexture("_BaseMap", Load("T_MucheolPillar_Banner_BaseColor.png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", BANNER_SMOOTHNESS);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D Load(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(ROOT + "/Textures/" + fileName);
        }

        private static Material LoadOrCreateMaterial(string materialName, string shaderName)
        {
            string path = ROOT + "/Materials/" + materialName + ".mat";
            Shader shader = Shader.Find(shaderName);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                material.shader = shader;
                return material;
            }

            material = new Material(shader) { name = materialName };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary> High, Medium, Low FBX를 LODGroup으로 묶고 충돌체를 붙인 프리팹을 만든다 </summary>
        private static GameObject BuildPrefab(Material stone, Material banner)
        {
            GameObject root = new GameObject("PF_MucheolPillar");
            LOD[] lods = new LOD[LOD_NAMES.Length];
            float[] heights = { LOD0_HEIGHT, LOD1_HEIGHT, LOD2_HEIGHT };

            for (int i = 0; i < LOD_NAMES.Length; i++)
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ROOT + "/Models/SM_MucheolPillar_" + LOD_NAMES[i] + ".fbx");
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                instance.name = "LOD" + i + "_" + LOD_NAMES[i];

                Transform bannerNode = instance.transform.Find(BANNER_NODE);
                if (bannerNode != null)
                {
                    bannerNode.localPosition = new Vector3(BANNER_OFFSET_X, BANNER_OFFSET_Y, BANNER_OFFSET_Z);
                    bannerNode.localRotation = Quaternion.Euler(0f, BANNER_YAW, 0f);
                }

                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
                for (int r = 0; r < renderers.Length; r++)
                {
                    bool isBanner = renderers[r].sharedMaterial != null && renderers[r].sharedMaterial.name.Contains("Banner");
                    renderers[r].sharedMaterial = isBanner ? banner : stone;
                }

                lods[i] = new LOD(heights[i], renderers);
            }

            LODGroup group = root.AddComponent<LODGroup>();
            group.SetLODs(lods);
            group.RecalculateBounds();

            GameObject colliderObject = new GameObject("Collider_Pillar");
            colliderObject.transform.SetParent(root.transform, false);
            CapsuleCollider capsule = colliderObject.AddComponent<CapsuleCollider>();
            capsule.radius = COLLIDER_RADIUS;
            capsule.height = COLLIDER_HEIGHT;
            capsule.center = new Vector3(0f, COLLIDER_HEIGHT * 0.5f, 0f);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PREFAB);
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary> 아레나 중심 반경 13.5 m에 120도 간격으로 석주를 놓고 바닥 높이에 밑면을 맞춘다 </summary>
        private static void PlacePillars(GameObject prefab)
        {
            Scene scene = EditorSceneManager.OpenScene(SCENE, OpenSceneMode.Single);

            GameObject old = GameObject.Find(GROUP_NAME);
            if (old != null)
                Object.DestroyImmediate(old);

            GameObject group = new GameObject(GROUP_NAME);
            Physics.SyncTransforms();

            for (int i = 0; i < PILLAR_COUNT; i++)
            {
                float angle = (START_ANGLE + FULL_CIRCLE / PILLAR_COUNT * i) * Mathf.Deg2Rad;
                float x = Mathf.Cos(angle) * RING_RADIUS;
                float z = Mathf.Sin(angle) * RING_RADIUS;

                GameObject pillar = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
                pillar.name = "Pillar_" + i;
                pillar.transform.position = new Vector3(x, FindGroundHeight(x, z), z);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static float FindGroundHeight(float x, float z)
        {
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, RAY_HEIGHT, z), Vector3.down, RAY_DISTANCE,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider.name == GROUND_NAME)
                    return hits[i].point.y;
            }

            return 0f;
        }
    }
}
