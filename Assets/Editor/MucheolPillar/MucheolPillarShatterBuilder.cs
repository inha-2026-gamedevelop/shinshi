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
        private const string EXTERIOR_MARK  = "Exterior";
        private const string DYNAMICS_PATH  = "ProjectSettings/DynamicsManager.asset";

        private const int FIRST_USER_LAYER = 9;
        private const int LAYER_COUNT      = 32;

        private const float COLLIDER_SHRINK = 0.85f;
        private const float DENSITY         = 800f;
        private const float MIN_MASS        = 0.5f;
        private const float FLOOR_SIZE      = 16f;
        private const float FLOOR_THICKNESS = 0.5f;

        private const string BANNER_MESH_PATH = ROOT + "/Models/SM_MucheolPillarBannerFragment.asset";
        private const string CLOTH_FLOOR_NAME = "ClothFloor";

        private const float CLOTH_FLOOR_RADIUS   = 100f;
        private const float CLOTH_PIN_RATIO      = 0.85f;
        private const float CLOTH_FREE_DISTANCE  = 1.5f;
        private const float CLOTH_PIN_DISTANCE   = 0.02f;
        private const float CLOTH_COLLISION_DIST = 0.05f;
        private const float CLOTH_MIN_BOX        = 0.15f;
        private const float CLOTH_CARRIER_MASS   = 2f;
        private const float CLOTH_DAMPING        = 0.35f;
        private const float CLOTH_STRETCH        = 1f;
        private const float CLOTH_BEND           = 0.6f;
        private const float CLOTH_FRICTION       = 0.1f;
        private const float CLOTH_VELOCITY_SCALE = 0.4f;

        private const float BANNER_OFFSET_X = 0.24f;
        private const float BANNER_OFFSET_Y = 0.16f;
        private const float BANNER_OFFSET_Z = 0.71f;
        private const float BANNER_YAW      = 290.52f;
        private const float BANNER_MOVE_X   = -0.98f;
        private const float BANNER_MOVE_Y   = -0.2f;
        private const float BANNER_TILT_Z   = -17.96f;

        private static readonly List<Cloth> s_cloths = new List<Cloth>();

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
            Material stone    = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "/Materials/M_MucheolPillar_Stone.mat");
            Material banner   = BuildBannerFragmentMaterial();

            GameObject root = PrefabUtility.LoadPrefabContents(PREFAB);
            RemoveOld(root);

            Rigidbody[] fragments = BuildFragments(root, fracture, stone, banner, debrisLayer);
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

            Transform clothFloor = root.transform.Find(CLOTH_FLOOR_NAME);
            if (clothFloor != null)
            {
                Object.DestroyImmediate(clothFloor.gameObject);
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
        private static Rigidbody[] BuildFragments(GameObject root, Material fracture, Material stone, Material banner, int layer)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(COLLAPSE);
            GameObject group = new GameObject(FRAGMENTS_NAME) { layer = layer };
            group.transform.SetParent(root.transform, false);

            Quaternion yaw = Quaternion.Euler(0f, BANNER_YAW, 0f);
            Vector3 bannerOffset = new Vector3(BANNER_OFFSET_X, BANNER_OFFSET_Y, BANNER_OFFSET_Z);
            List<Rigidbody> bodies = new List<Rigidbody>();
            s_cloths.Clear();

            foreach (MeshFilter source in model.GetComponentsInChildren<MeshFilter>())
            {
                if (source.name.Contains(BANNER_MARK))
                {
                    bodies.Add(BuildClothPiece(group.transform, source, banner, yaw, bannerOffset, layer));
                    continue;
                }

                GameObject piece = new GameObject(source.name) { layer = layer };
                piece.transform.SetParent(group.transform, false);

                piece.transform.SetLocalPositionAndRotation(source.transform.localPosition, source.transform.localRotation);
                // FBX 단위 환산이 노드 크기에 담겨 있으므로 같이 가져와야 원래 크기가 된다
                piece.transform.localScale = source.transform.localScale;
                piece.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                piece.AddComponent<MeshRenderer>().sharedMaterials = PickMaterials(source.GetComponent<Renderer>(), fracture, stone);

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

        /// <summary> 큰 파편은 외피와 새 절단면 두 슬롯을 가진다. 슬롯 이름으로 각각 석재와 절단면 재질을 잇는다 </summary>
        private static Material[] PickMaterials(Renderer source, Material fracture, Material stone)
        {
            Material[] slots = source.sharedMaterials;
            Material[] result = new Material[slots.Length];
            for (int i = 0; i < slots.Length; i++)
            {
                result[i] = slots[i].name.Contains(EXTERIOR_MARK) ? stone : fracture;
            }

            return result;
        }

        /// <summary>
        /// 떨어지는 천 조각. 단단한 몸체를 들고 다니는 얇은 상자 위에 Cloth를 얹어 펄럭이고 늘어지게 한다.
        /// Cloth는 크기가 있는 변환을 싫어하므로 FBX 노드의 회전과 크기를 메시에 미리 반영한다
        /// </summary>
        private static Rigidbody BuildClothPiece(Transform parent, MeshFilter source, Material banner, Quaternion yaw, Vector3 offset, int layer)
        {
            Mesh baked = BakeClothMesh(source);

            GameObject carrier = new GameObject(source.name) { layer = layer };
            carrier.transform.SetParent(parent, false);
            // 서 있는 천을 손으로 옮겨 기울인 만큼 같이 옮겨, 부서지는 순간 같은 자리에서 시작하게 한다
            Quaternion tilt = Quaternion.Euler(0f, 0f, BANNER_TILT_Z);
            Vector3 standing = yaw * source.transform.localPosition + offset;
            carrier.transform.SetLocalPositionAndRotation(tilt * standing + new Vector3(BANNER_MOVE_X, BANNER_MOVE_Y, 0f), tilt * yaw);

            Vector3 size = Vector3.Max(baked.bounds.size * COLLIDER_SHRINK * 0.5f, Vector3.one * CLOTH_MIN_BOX);
            BoxCollider box = carrier.AddComponent<BoxCollider>();
            box.center = baked.bounds.center;
            box.size = size;
            box.enabled = false;

            Rigidbody body = carrier.AddComponent<Rigidbody>();
            body.mass = CLOTH_CARRIER_MASS;
            body.isKinematic = true;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;

            GameObject skin = new GameObject("Cloth") { layer = layer };
            skin.transform.SetParent(carrier.transform, false);

            SkinnedMeshRenderer renderer = skin.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = baked;
            renderer.sharedMaterial = banner;
            renderer.updateWhenOffscreen = true;

            Cloth cloth = skin.AddComponent<Cloth>();
            cloth.useGravity = true;
            cloth.damping = CLOTH_DAMPING;
            cloth.stretchingStiffness = CLOTH_STRETCH;
            cloth.bendingStiffness = CLOTH_BEND;
            cloth.friction = CLOTH_FRICTION;
            cloth.worldVelocityScale = CLOTH_VELOCITY_SCALE;
            // Cloth는 겹친 꼭짓점을 하나로 합쳐 다루므로 계수 개수는 합친 뒤 꼭짓점 수에 맞아야 한다
            cloth.coefficients = BuildClothCoefficients(cloth.vertices);
            s_cloths.Add(cloth);

            return body;
        }

        /// <summary> FBX 노드의 회전과 크기를 꼭짓점에 곱해 크기 1, 회전 없는 메시 에셋으로 만든다 </summary>
        private static Mesh BakeClothMesh(MeshFilter source)
        {
            Mesh mesh = source.sharedMesh;
            Quaternion rotation = source.transform.localRotation;
            Vector3 scale = source.transform.localScale;

            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = rotation * Vector3.Scale(vertices[i], scale);
                normals[i] = rotation * normals[i];
            }

            List<Vector2> uvs = new List<Vector2>();
            mesh.GetUVs(0, uvs);

            Mesh baked = new Mesh { name = "SM_MucheolPillarBannerFragment" };
            baked.indexFormat = vertices.Length > ushort.MaxValue ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            baked.SetVertices(vertices);
            baked.SetNormals(normals);
            baked.SetUVs(0, uvs);
            baked.SetTriangles(mesh.triangles, 0);
            baked.RecalculateBounds();

            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(BANNER_MESH_PATH);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(baked, BANNER_MESH_PATH);
                return baked;
            }

            EditorUtility.CopySerialized(baked, existing);
            return existing;
        }

        /// <summary> 위쪽 끝은 몸체에 매달고 나머지는 느슨하게 풀어 아래 자락이 늘어지며 펄럭이게 한다 </summary>
        private static ClothSkinningCoefficient[] BuildClothCoefficients(Vector3[] vertices)
        {
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                minY = Mathf.Min(minY, vertices[i].y);
                maxY = Mathf.Max(maxY, vertices[i].y);
            }

            float pinHeight = minY + (maxY - minY) * CLOTH_PIN_RATIO;
            ClothSkinningCoefficient[] coefficients = new ClothSkinningCoefficient[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                coefficients[i].maxDistance = vertices[i].y >= pinHeight ? CLOTH_PIN_DISTANCE : CLOTH_FREE_DISTANCE;
                coefficients[i].collisionSphereDistance = CLOTH_COLLISION_DIST;
            }

            return coefficients;
        }

        /// <summary> 파편 레이어 전용 바닥. 캐릭터와 겹치지 않고 파편만 받아 준다 </summary>
        private static void BuildFloor(GameObject root, int layer)
        {
            GameObject floor = new GameObject(FLOOR_NAME) { layer = layer };
            floor.transform.SetParent(root.transform, false);

            BoxCollider box = floor.AddComponent<BoxCollider>();
            box.size = new Vector3(FLOOR_SIZE, FLOOR_THICKNESS, FLOOR_SIZE);
            box.center = new Vector3(0f, -FLOOR_THICKNESS * 0.5f, 0f);

            // Cloth는 상자 충돌체와 부딪히지 못하고 구와 캡슐만 안다. 아주 큰 구의 윗면을 바닥으로 쓴다
            GameObject sphere = new GameObject(CLOTH_FLOOR_NAME) { layer = layer };
            sphere.transform.SetParent(root.transform, false);
            sphere.transform.localPosition = new Vector3(0f, -CLOTH_FLOOR_RADIUS, 0f);

            SphereCollider ball = sphere.AddComponent<SphereCollider>();
            ball.radius = CLOTH_FLOOR_RADIUS;
            ball.isTrigger = true;
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

            SerializedProperty cloths = so.FindProperty("_cloths");
            cloths.arraySize = s_cloths.Count;
            for (int i = 0; i < s_cloths.Count; i++)
            {
                cloths.GetArrayElementAtIndex(i).objectReferenceValue = s_cloths[i];
            }

            so.FindProperty("_clothFloor").objectReferenceValue = root.transform.Find(CLOTH_FLOOR_NAME).GetComponent<SphereCollider>();
            so.ApplyModifiedProperties();
        }
    }
}
