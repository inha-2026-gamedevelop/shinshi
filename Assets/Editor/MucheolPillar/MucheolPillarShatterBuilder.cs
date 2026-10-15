// System
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Editor
{
    /// <summary> 석주 프리팹에 붕괴 파편, 파편 전용 레이어, 파괴 컴포넌트를 붙인다 </summary>
    public static class MucheolPillarShatterBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string ROOT       = "Assets/01.Scripts/05.Art/Environment/MucheolPillar";
        private const string PREFAB     = "Assets/02.Prefab/Environment/MucheolPillar/PF_MucheolPillar.prefab";
        private const string COLLAPSE   = ROOT + "/Models/SM_MucheolPillarCollapse.fbx";
        private const string LIT_SHADER = "Universal Render Pipeline/Lit";

        public const string DEBRIS_LAYER = "Debris";

        private const string FRAGMENTS_NAME = "Fragments";
        private const string FLOOR_NAME     = "DebrisFloor";
        private const string BANNER_MARK    = "RedBanner";
        private const string DYNAMICS_PATH  = "ProjectSettings/DynamicsManager.asset";

        private const int FIRST_USER_LAYER = 9;
        private const int LAYER_COUNT      = 32;

        private const float COLLIDER_SHRINK = 0.85f;
        private const float DENSITY         = 800f;
        private const float MIN_MASS        = 0.5f;
        private const float FLOOR_SIZE      = 16f;
        private const float FLOOR_THICKNESS = 0.5f;

        private const float BANNER_OFFSET_X = 0.24f;
        private const float BANNER_OFFSET_Y = 0.16f;
        private const float BANNER_OFFSET_Z = 0.71f;
        private const float BANNER_YAW      = 290.52f;

        /****************************************
        *               Methods
        ****************************************/

        /// <summary> 프리팹에 파편과 파괴 컴포넌트를 다시 만든다. 여러 번 실행해도 같은 결과가 된다 </summary>
        public static void Apply()
        {
            int debrisLayer = EnsureDebrisLayer();
            ConfigureCollapseModel();
            PackFractureSmoothness();

            Material fracture = BuildFractureMaterial();
            Material banner   = BuildBannerFragmentMaterial();

            GameObject root = PrefabUtility.LoadPrefabContents(PREFAB);
            RemoveOld(root);

            Rigidbody[] fragments = BuildFragments(root, fracture, banner, debrisLayer);
            BuildFloor(root, debrisLayer);
            AttachShatter(root, fragments);

            PrefabUtility.SaveAsPrefabAsset(root, PREFAB);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.SaveAssets();
        }

        /// <summary> 파편 전용 레이어를 만들고 이 레이어끼리만 충돌하도록 충돌 행렬을 맞춘다 </summary>
        private static int EnsureDebrisLayer()
        {
            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            int index = -1;
            for (int i = FIRST_USER_LAYER; i < LAYER_COUNT; i++)
            {
                string layerName = layers.GetArrayElementAtIndex(i).stringValue;
                if (layerName == DEBRIS_LAYER)
                {
                    index = i;
                    break;
                }

                if (index < 0 && string.IsNullOrEmpty(layerName))
                {
                    index = i;
                }
            }

            layers.GetArrayElementAtIndex(index).stringValue = DEBRIS_LAYER;
            tagManager.ApplyModifiedProperties();

            SerializedObject dynamics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(DYNAMICS_PATH)[0]);
            SerializedProperty matrix = dynamics.FindProperty("m_LayerCollisionMatrix");
            uint debrisBit = 1u << index;

            for (int i = 0; i < LAYER_COUNT; i++)
            {
                SerializedProperty row = matrix.GetArrayElementAtIndex(i);
                uint bits = row.uintValue;
                bits = i == index ? debrisBit : bits & ~debrisBit;
                row.uintValue = bits;
            }

            dynamics.ApplyModifiedProperties();
            return index;
        }

        private static void ConfigureCollapseModel()
        {
            ModelImporter importer = AssetImporter.GetAtPath(COLLAPSE) as ModelImporter;
            if (importer == null || !importer.importAnimation)
            {
                return;
            }

            importer.importAnimation = false;
            importer.SaveAndReimport();
        }

        /// <summary> 파편 거칠기를 URP Lit의 매끄러움 알파로 바꾼 맵을 만든다 </summary>
        private static void PackFractureSmoothness()
        {
            string roughPath = ROOT + "/Textures/T_MucheolPillar_Fracture_Roughness.png";
            string outPath   = ROOT + "/Textures/T_MucheolPillar_Fracture_MetallicSmoothness.png";

            TextureImporter importer = AssetImporter.GetAtPath(roughPath) as TextureImporter;
            importer.isReadable = true;
            importer.sRGBTexture = false;
            importer.SaveAndReimport();

            Texture2D roughness = AssetDatabase.LoadAssetAtPath<Texture2D>(roughPath);
            Color32[] rough = roughness.GetPixels32();
            Color32[] packed = new Color32[rough.Length];

            for (int i = 0; i < packed.Length; i++)
            {
                packed[i] = new Color32(0, 0, 0, (byte)(255 - rough[i].r));
            }

            Texture2D result = new Texture2D(roughness.width, roughness.height, TextureFormat.RGBA32, false, true);
            result.SetPixels32(packed);
            result.Apply();
            File.WriteAllBytes(outPath, result.EncodeToPNG());
            Object.DestroyImmediate(result);

            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceSynchronousImport);
            SetTexture(outPath, false, true);
            SetTexture(ROOT + "/Textures/T_MucheolPillar_Fracture_BaseColor_Warm.png", false, false);
            SetTexture(ROOT + "/Textures/T_MucheolPillar_Fracture_Normal.png", true, true);
        }

        private static void SetTexture(string path, bool normalMap, bool linear)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }

            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normalMap && !linear;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }

        private static Material BuildFractureMaterial()
        {
            Material material = LoadOrCreate("M_MucheolPillar_Fracture");
            material.SetTexture("_BaseMap", LoadTexture("T_MucheolPillar_Fracture_BaseColor_Warm.png"));
            material.SetTexture("_BumpMap", LoadTexture("T_MucheolPillar_Fracture_Normal.png"));
            material.SetTexture("_MetallicGlossMap", LoadTexture("T_MucheolPillar_Fracture_MetallicSmoothness.png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_Metallic", 0f);
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary> 떨어진 천은 걸린 곳이 없으므로 바람 셰이더 대신 기본 Lit을 쓴다 </summary>
        private static Material BuildBannerFragmentMaterial()
        {
            Material material = LoadOrCreate("M_MucheolPillar_BannerFragment");
            material.SetTexture("_BaseMap", LoadTexture("T_MucheolPillar_Banner_BaseColor.png"));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.2f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D LoadTexture(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(ROOT + "/Textures/" + fileName);
        }

        private static Material LoadOrCreate(string materialName)
        {
            string path = ROOT + "/Materials/" + materialName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            material = new Material(Shader.Find(LIT_SHADER)) { name = materialName };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void RemoveOld(GameObject root)
        {
            Transform fragments = root.transform.Find(FRAGMENTS_NAME);
            if (fragments != null)
            {
                Object.DestroyImmediate(fragments.gameObject);
            }

            Transform floor = root.transform.Find(FLOOR_NAME);
            if (floor != null)
            {
                Object.DestroyImmediate(floor.gameObject);
            }

            MucheolPillarShatter old = root.GetComponent<MucheolPillarShatter>();
            if (old != null)
            {
                Object.DestroyImmediate(old);
            }
        }

        /// <summary> 붕괴 FBX의 조각마다 메시, 상자 충돌체, Rigidbody를 가진 오브젝트를 만든다 </summary>
        private static Rigidbody[] BuildFragments(GameObject root, Material fracture, Material banner, int layer)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(COLLAPSE);
            GameObject group = new GameObject(FRAGMENTS_NAME) { layer = layer };
            group.transform.SetParent(root.transform, false);

            Quaternion yaw = Quaternion.Euler(0f, BANNER_YAW, 0f);
            Vector3 bannerOffset = new Vector3(BANNER_OFFSET_X, BANNER_OFFSET_Y, BANNER_OFFSET_Z);
            List<Rigidbody> bodies = new List<Rigidbody>();

            foreach (MeshFilter source in model.GetComponentsInChildren<MeshFilter>())
            {
                bool isBanner = source.name.Contains(BANNER_MARK);
                GameObject piece = new GameObject(source.name) { layer = layer };
                piece.transform.SetParent(group.transform, false);

                Vector3 position = source.transform.localPosition;
                Quaternion rotation = source.transform.localRotation;
                if (isBanner)
                {
                    // 온전한 천이 석주 쪽으로 옮겨 놓은 자리와 같게 맞춘다
                    position = yaw * position + bannerOffset;
                    rotation = yaw * rotation;
                }

                piece.transform.SetLocalPositionAndRotation(position, rotation);
                // FBX 단위 환산이 노드 크기에 담겨 있으므로 같이 가져와야 원래 크기가 된다
                piece.transform.localScale = source.transform.localScale;
                piece.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                piece.AddComponent<MeshRenderer>().sharedMaterial = isBanner ? banner : fracture;

                Bounds bounds = source.sharedMesh.bounds;
                BoxCollider box = piece.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size * COLLIDER_SHRINK;
                box.enabled = false;

                Rigidbody body = piece.AddComponent<Rigidbody>();
                Vector3 worldSize = Vector3.Scale(box.size, piece.transform.lossyScale);
                body.mass = Mathf.Max(MIN_MASS, worldSize.x * worldSize.y * worldSize.z * DENSITY);
                body.isKinematic = true;
                body.collisionDetectionMode = CollisionDetectionMode.Continuous;
                bodies.Add(body);
            }

            group.SetActive(false);
            return bodies.ToArray();
        }

        /// <summary> 파편 레이어 전용 바닥. 캐릭터와 겹치지 않고 파편만 받아 준다 </summary>
        private static void BuildFloor(GameObject root, int layer)
        {
            GameObject floor = new GameObject(FLOOR_NAME) { layer = layer };
            floor.transform.SetParent(root.transform, false);

            BoxCollider box = floor.AddComponent<BoxCollider>();
            box.size = new Vector3(FLOOR_SIZE, FLOOR_THICKNESS, FLOOR_SIZE);
            box.center = new Vector3(0f, -FLOOR_THICKNESS * 0.5f, 0f);
        }

        private static void AttachShatter(GameObject root, Rigidbody[] fragments)
        {
            MucheolPillarShatter shatter = root.AddComponent<MucheolPillarShatter>();
            SerializedObject so = new SerializedObject(shatter);

            SerializedProperty intact = so.FindProperty("_intactObjects");
            intact.arraySize = 3;
            string[] lodNames = { "LOD0_High", "LOD1_Medium", "LOD2_Low" };
            for (int i = 0; i < lodNames.Length; i++)
            {
                intact.GetArrayElementAtIndex(i).objectReferenceValue = root.transform.Find(lodNames[i]).gameObject;
            }

            so.FindProperty("_intactCollider").objectReferenceValue = root.transform.Find("Collider_Pillar").GetComponent<Collider>();
            so.FindProperty("_fragmentRoot").objectReferenceValue = root.transform.Find(FRAGMENTS_NAME).gameObject;

            SerializedProperty list = so.FindProperty("_fragments");
            list.arraySize = fragments.Length;
            for (int i = 0; i < fragments.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = fragments[i];
            }

            so.ApplyModifiedProperties();
        }
    }
}
