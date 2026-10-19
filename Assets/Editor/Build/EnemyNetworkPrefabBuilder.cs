// System
using System;
using System.Text;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Network;

namespace Shinshi.Editor
{
    // 일반 적 네트워크 프리팹 PF_NetEnemy와 효과 프리팹 PF_NetEnemyEffect를 만들고 Bootstrap 씬 매니저의 spawnPrefabs에 등록한다
    // 이미 있는 프리팹은 지우지 않고 제자리에서 갱신해 GUID를 지키며, 씬은 에디터 API로만 바꾸고 원래 열린 씬을 되돌린다
    // 외형은 Horror Creatures의 원본 Beast, Vendigo 프리팹을 Visual 아래에 함께 넣고 EnemyNetworkView가 종류에 맞는 하나만 켠다
    // 원본 에셋은 수정하지 않고 중첩 프리팹으로 쓰며, 원본이 하나라도 없을 때만 역할 색 캡슐을 대체 표현으로 둔다
    // 실행: 메뉴 Shinshi/Release/Build Enemy Network Prefabs, 또는 -executeMethod Shinshi.Editor.EnemyNetworkPrefabBuilder.BuildFromCommandLine
    public static class EnemyNetworkPrefabBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string MENU_PATH = "Shinshi/Release/Build Enemy Network Prefabs";

        public const string FOLDER_PATH        = "Assets/02.Prefab/Network/NetEnemy";
        public const string ENEMY_PREFAB_PATH  = FOLDER_PATH + "/PF_NetEnemy.prefab";
        public const string EFFECT_PREFAB_PATH = FOLDER_PATH + "/PF_NetEnemyEffect.prefab";

        private const string BODY_MATERIAL_PATH = FOLDER_PATH + "/M_NetEnemyFallback.mat";
        private const string LINE_MATERIAL_PATH = FOLDER_PATH + "/M_NetEnemyLine.mat";

        // 원본 모델은 GUID로 찾아 폴더가 옮겨져도 이어진다
        private const string BEAST_PREFAB_GUID   = "038dfae9efa158b41944836ffc625437";
        private const string VENDIGO_PREFAB_GUID = "c2837c1429a19804b8835aaa9269f4db";

        private const string BEAST_CONTROLLER_PATH   = "Assets/02.Prefab/Enemy/Animation/AC_BeastMelee.controller";
        private const string VENDIGO_CONTROLLER_PATH = "Assets/02.Prefab/Enemy/Animation/AC_VendigoCharger.controller";

        private const string BEAST_MODEL_NAME   = "Model_Beast";
        private const string VENDIGO_MODEL_NAME = "Model_Vendigo";
        private const string FALLBACK_NAME      = "Capsule";

        private const string VISUAL_NAME     = "Visual";
        private const string SEPARATION_NAME = "SeparationBody";
        private const string TELEGRAPH_NAME  = "Telegraph";
        private const string RING_NAME       = "Ring";
        private const string BOLT_NAME       = "Bolt";

        private const int   SEPARATION_LAYER  = 2;
        private const float BODY_HEIGHT       = 2f;
        private const float HIT_RADIUS        = 0.5f;
        private const float SEPARATION_RADIUS = 0.3f;
        private const float BOLT_SCALE        = 0.4f;
        private const float LINE_WIDTH        = 0.08f;

        /****************************************
        *               Entry Points
        ****************************************/

        /// <summary> 두 프리팹을 만들거나 갱신하고 Bootstrap 씬에 등록한다, 실패하면 예외를 던진다 </summary>
        [MenuItem(MENU_PATH)]
        public static void BuildAll()
        {
            Run();
        }

        public static void BuildFromCommandLine()
        {
            try
            {
                Run();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("플레이 모드에서는 적 프리팹을 만들 수 없다");
            }

            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    throw new InvalidOperationException("저장하지 않은 열린 씬이 있다. 저장하거나 닫은 뒤 다시 실행한다: " + SceneManager.GetSceneAt(i).path);
                }
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ShinshiBuild.BOOTSTRAP_SCENE_PATH) == null)
            {
                throw new InvalidOperationException("Bootstrap 씬이 없다: " + ShinshiBuild.BOOTSTRAP_SCENE_PATH);
            }

            SceneSetup[] initialSetup = EditorSceneManager.GetSceneManagerSetup();
            StringBuilder report = new StringBuilder("[EnemyNetworkPrefabBuilder] 결과\n");

            try
            {
                EnsureFolder();
                Material body = EnsureMaterial(BODY_MATERIAL_PATH, "Universal Render Pipeline/Lit");
                Material line = EnsureMaterial(LINE_MATERIAL_PATH, "Universal Render Pipeline/Unlit");

                GameObject enemy  = BuildPrefab(ENEMY_PREFAB_PATH, root => ConfigureEnemy(root, body, line, report));
                GameObject effect = BuildPrefab(EFFECT_PREFAB_PATH, root => ConfigureEffect(root, body, line));

                if (!EnemyNetworkSpawner.ValidatePrefab(enemy, out string enemyError))
                {
                    throw new InvalidOperationException("PF_NetEnemy 검증 실패: " + enemyError);
                }

                if (!EnemyNetworkSpawner.ValidateEffectPrefab(effect, out string effectError))
                {
                    throw new InvalidOperationException("PF_NetEnemyEffect 검증 실패: " + effectError);
                }

                report.AppendLine($"- {ENEMY_PREFAB_PATH}, {EFFECT_PREFAB_PATH} 갱신");
                RegisterSpawnPrefabs(enemy, effect, report);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(report.ToString());
            }
            finally
            {
                if (initialSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(initialSetup);
                }
                else
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
            }
        }

        private static void EnsureFolder()
        {
            string[] parts = FOLDER_PATH.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; ++i)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        // 이미 있는 재질은 그대로 둔다
        private static Material EnsureMaterial(string path, string shaderName)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // 프리팹이 있으면 내용을 열어 제자리에서 고쳐 저장하고(GUID 유지), 없으면 새로 만든다
        private static GameObject BuildPrefab(string path, Action<GameObject> configure)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                GameObject created = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
                try
                {
                    configure(created);
                    return PrefabUtility.SaveAsPrefabAsset(created, path);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(created);
                }
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                configure(contents);
                return PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            return target.TryGetComponent(out T existing) ? existing : target.AddComponent<T>();
        }

        private static Transform EnsureChild(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child != null)
            {
                return child;
            }

            GameObject created = new GameObject(childName);
            created.transform.SetParent(parent, false);
            return created.transform;
        }

        private static void ConfigureEnemy(GameObject root, Material body, Material line, StringBuilder report)
        {
            EnsureComponent<NetworkIdentity>(root);

            NetworkTransformReliable sync = EnsureComponent<NetworkTransformReliable>(root);
            sync.target        = root.transform;
            sync.syncDirection = SyncDirection.ServerToClient;
            sync.syncPosition  = true;
            sync.syncRotation  = true;
            sync.syncScale     = false;

            EnsureComponent<EnemyNetState>(root);
            EnemyNetworkView view = EnsureComponent<EnemyNetworkView>(root);

            // 감지와 피격을 받는 몸, 서버 몸체가 판정하므로 트리거로 둔다
            CapsuleCollider hit = EnsureComponent<CapsuleCollider>(root);
            hit.isTrigger = true;
            hit.center    = new Vector3(0f, BODY_HEIGHT * 0.5f, 0f);
            hit.radius    = HIT_RADIUS;
            hit.height    = BODY_HEIGHT;

            Transform separation = EnsureChild(root.transform, SEPARATION_NAME);
            separation.gameObject.layer = SEPARATION_LAYER;
            CapsuleCollider separationCollider = EnsureComponent<CapsuleCollider>(separation.gameObject);
            separationCollider.isTrigger = false;
            separationCollider.center    = new Vector3(0f, BODY_HEIGHT * 0.5f, 0f);
            separationCollider.radius    = SEPARATION_RADIUS;
            separationCollider.height    = BODY_HEIGHT;

            Transform visual = EnsureChild(root.transform, VISUAL_NAME);
            Renderer visualRenderer = null;
            bool hasModels = TryEnsureModels(visual, report, out GameObject[] modelRoots, out Animator[] modelAnimators);
            if (hasModels)
            {
                Transform stale = visual.Find(FALLBACK_NAME);
                if (stale != null)
                {
                    UnityEngine.Object.DestroyImmediate(stale.gameObject);
                }
            }
            else
            {
                visualRenderer = EnsureFallbackCapsule(visual, body);
            }

            LineRenderer telegraph = EnsureLine(EnsureChild(root.transform, TELEGRAPH_NAME).gameObject, line);

            SerializedObject serialized = new SerializedObject(view);
            serialized.FindProperty("_visual").objectReferenceValue         = visual;
            serialized.FindProperty("_visualRenderer").objectReferenceValue = visualRenderer;
            serialized.FindProperty("_telegraphLine").objectReferenceValue  = telegraph;
            SetObjectArray(serialized.FindProperty("_modelRoots"), modelRoots);
            SetObjectArray(serialized.FindProperty("_modelAnimators"), modelAnimators);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // 원본 Beast, Vendigo를 중첩 프리팹으로 Visual 아래에 둔다. 둘 중 하나라도 없으면 거짓을 돌려 대체 캡슐을 쓰게 한다
        private static bool TryEnsureModels(Transform visual, StringBuilder report, out GameObject[] roots, out Animator[] animators)
        {
            roots     = null;
            animators = null;

            GameObject beast   = LoadPrefabByGuid(BEAST_PREFAB_GUID);
            GameObject vendigo = LoadPrefabByGuid(VENDIGO_PREFAB_GUID);
            if ((beast == null) || (vendigo == null))
            {
                report.AppendLine("- 경고: Beast 또는 Vendigo 원본 프리팹을 찾지 못해 역할 색 캡슐을 대체 표현으로 둠");
                return false;
            }

            RuntimeAnimatorController beastController   = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(BEAST_CONTROLLER_PATH);
            RuntimeAnimatorController vendigoController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(VENDIGO_CONTROLLER_PATH);
            if ((beastController == null) || (vendigoController == null))
            {
                throw new InvalidOperationException("적 애니메이터 컨트롤러가 없다: " + BEAST_CONTROLLER_PATH + ", " + VENDIGO_CONTROLLER_PATH);
            }

            roots     = new GameObject[2];
            animators = new Animator[2];

            // 근접, 원거리, 정예가 Beast를 함께 쓰므로 처음에는 Beast만 켜 두고 뷰가 종류에 맞춰 바꾼다
            roots[EnemyNetworkView.MODEL_BEAST]   = EnsureModel(visual, BEAST_MODEL_NAME, beast, beastController, true, out animators[EnemyNetworkView.MODEL_BEAST]);
            roots[EnemyNetworkView.MODEL_VENDIGO] = EnsureModel(visual, VENDIGO_MODEL_NAME, vendigo, vendigoController, false, out animators[EnemyNetworkView.MODEL_VENDIGO]);

            report.AppendLine("- 원본 모델 Beast(근접, 원거리, 정예), Vendigo(돌진)를 Visual 아래 중첩 프리팹으로 배치");
            return true;
        }

        private static GameObject LoadPrefabByGuid(string guid)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        // 같은 원본의 중첩 인스턴스가 이미 있으면 그대로 써 다시 저장해도 프리팹 내부 ID가 흔들리지 않는다
        private static GameObject EnsureModel(Transform visual, string modelName, GameObject source, RuntimeAnimatorController controller,
            bool active, out Animator animator)
        {
            GameObject instance = null;
            Transform existing = visual.Find(modelName);
            if ((existing != null) && (PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject) == source))
            {
                instance = existing.gameObject;
            }
            else
            {
                if (existing != null)
                {
                    UnityEngine.Object.DestroyImmediate(existing.gameObject);
                }

                instance = (GameObject)PrefabUtility.InstantiatePrefab(source, visual);
                instance.name = modelName;
            }

            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            animator = instance.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                throw new InvalidOperationException("원본 모델에 Animator가 없다: " + source.name);
            }

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion           = false;
            instance.SetActive(active);
            return instance;
        }

        private static Renderer EnsureFallbackCapsule(Transform visual, Material body)
        {
            Transform existing = visual.Find(FALLBACK_NAME);
            if (existing != null)
            {
                return existing.GetComponent<Renderer>();
            }

            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = FALLBACK_NAME;
            UnityEngine.Object.DestroyImmediate(capsule.GetComponent<Collider>());
            capsule.transform.SetParent(visual, false);
            capsule.transform.localPosition = new Vector3(0f, BODY_HEIGHT * 0.5f, 0f);

            Renderer capsuleRenderer = capsule.GetComponent<Renderer>();
            capsuleRenderer.sharedMaterial = body;
            return capsuleRenderer;
        }

        private static void SetObjectArray(SerializedProperty property, UnityEngine.Object[] values)
        {
            int count = (values != null) ? values.Length : 0;
            property.arraySize = count;
            for (int i = 0; i < count; ++i)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static void ConfigureEffect(GameObject root, Material body, Material line)
        {
            EnsureComponent<NetworkIdentity>(root);
            EnsureComponent<EnemyEffectState>(root);
            EnemyEffectView view = EnsureComponent<EnemyEffectView>(root);

            LineRenderer ring = EnsureLine(EnsureChild(root.transform, RING_NAME).gameObject, line);

            Transform bolt = root.transform.Find(BOLT_NAME);
            if (bolt == null)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = BOLT_NAME;
                UnityEngine.Object.DestroyImmediate(sphere.GetComponent<Collider>());
                sphere.transform.SetParent(root.transform, false);
                sphere.transform.localScale = Vector3.one * BOLT_SCALE;
                sphere.GetComponent<Renderer>().sharedMaterial = body;
                sphere.SetActive(false);
                bolt = sphere.transform;
            }

            SerializedObject serialized = new SerializedObject(view);
            serialized.FindProperty("_ring").objectReferenceValue = ring;
            serialized.FindProperty("_bolt").objectReferenceValue = bolt;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static LineRenderer EnsureLine(GameObject target, Material material)
        {
            LineRenderer renderer = EnsureComponent<LineRenderer>(target);
            renderer.useWorldSpace     = true;
            renderer.loop              = false;
            renderer.alignment         = LineAlignment.View;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.startWidth        = LINE_WIDTH;
            renderer.endWidth          = LINE_WIDTH;
            renderer.sharedMaterial    = material;
            renderer.positionCount     = 0;
            renderer.enabled           = false;
            return renderer;
        }

        // Bootstrap 씬을 열어 매니저 spawnPrefabs에 없는 프리팹만 더하고, 바뀐 때만 저장한다
        private static void RegisterSpawnPrefabs(GameObject enemy, GameObject effect, StringBuilder report)
        {
            Scene scene = EditorSceneManager.OpenScene(ShinshiBuild.BOOTSTRAP_SCENE_PATH, OpenSceneMode.Single);
            ShinshiNetworkManager manager = UnityEngine.Object.FindFirstObjectByType<ShinshiNetworkManager>();
            if (manager == null)
            {
                throw new InvalidOperationException("Bootstrap 씬에 ShinshiNetworkManager가 없다");
            }

            SerializedObject serialized = new SerializedObject(manager);
            SerializedProperty list = serialized.FindProperty("spawnPrefabs");
            bool changed = false;

            changed |= AddIfMissing(list, enemy);
            changed |= AddIfMissing(list, effect);

            if (!changed)
            {
                report.AppendLine("- Bootstrap spawnPrefabs: 이미 등록되어 있어 씬을 저장하지 않음");
                return;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException("Bootstrap 씬 저장에 실패했다");
            }

            report.AppendLine("- Bootstrap spawnPrefabs에 적 프리팹을 등록하고 씬을 저장함");
        }

        private static bool AddIfMissing(SerializedProperty list, GameObject prefab)
        {
            for (int i = 0; i < list.arraySize; ++i)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == prefab)
                {
                    return false;
                }
            }

            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            list.GetArrayElementAtIndex(index).objectReferenceValue = prefab;
            return true;
        }
    }
}
