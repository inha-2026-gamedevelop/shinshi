#if UNITY_EDITOR
// System
using System;
using System.IO;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Systems;

namespace Shinshi.UI.Editor
{
    /// <summary> 마을 중앙 벚꽃나무 아래의 균열 안내인 NPC 프리팹을 만들고, 입장 창과 함께 마을 출시 씬에 놓는다 </summary>
    // 모델은 새로 만들지 않고 기존 도술사 캐릭터 프리팹과 이동 애니메이터를 쓴다. 다시 실행하면 프리팹과 씬 오브젝트를 새로 쓰므로
    // 손으로 고친 내용은 사라진다. 열린 씬에 저장하지 않은 변경이 있으면 아무것도 하지 않고 멈춘다
    public static class RiftGuideNpcBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string NPC_PREFAB_PATH = "Assets/02.Prefab/Npc/PF_RiftGuideNpc.prefab";

        private const string MODEL_PATH      = "Assets/02.Prefab/Characters/PlayerFemaleTripo/PF_FemaleTaoist_Cute.prefab";
        private const string CONTROLLER_PATH = "Assets/01.Scripts/05.Art/Characters/PlayerFemaleTripo/Animations/AC_FemaleTripo_Locomotion.controller";
        private const string TOWN_SCENE_PATH = "Assets/00.Scenes/Release/ShinshiTown.unity";
        private const string NPC_FOLDER      = "Assets/02.Prefab/Npc";

        private const string TREE_NAME    = "SacredCherryTree";
        private const string NPC_NAME     = "NPC_RiftGuide";
        private const string CANVAS_NAME  = "DungeonEntryCanvas";
        private const string WINDOW_NAME  = "DungeonEntryWindow";
        private const string BINDER_NAME  = "DungeonEntryBinding";
        private const string UI_LAYER     = "UI";

        private const string NPC_TITLE    = "균열 안내인";
        private const string NPC_SUBTITLE = "카오스 던전 / 보스 맵";
        private const string PROMPT_TEXT  = "[F] 말을 건다";

        // 벚꽃나무 중심에서 남쪽(플레이어 출발 쪽, 카메라를 보는 방향)으로 벌린 자리. 제단 상과 화로 사이 빈 포석이고 나무 몸통에서 떨어져 있다
        private static readonly Vector3 G_OffsetFromTree = new Vector3(2.5f, 0f, -9f);
        private const float NPC_YAW         = 180f;
        private const float SNAP_RADIUS     = 3f;
        private const float INTERACT_RANGE  = 3.5f;

        // 이름표 두 줄이 머리와 겹치지 않게 머리 위로 띄우고, 안내는 이름표 위에 둔다
        private const float LABEL_ABOVE_HEAD = 0.55f;
        private const float NAME_FONT_SIZE   = 3f;
        private const float PROMPT_FONT_SIZE = 2.2f;
        private const float PROMPT_OFFSET    = 0.95f;
        private const float FALLBACK_HEIGHT  = 1.8f;
        private const float LABEL_WIDTH      = 8f;
        private const float LABEL_HEIGHT     = 2f;

        private static readonly Color G_Gold = new Color(0.91f, 0.74f, 0.41f, 1f);
        private static readonly Color G_Mint = new Color(0.46f, 0.9f, 0.74f, 1f);

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/UI/균열 안내인 NPC 만들기")]
        public static string BuildPrefab()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(MODEL_PATH);
            var controller   = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CONTROLLER_PATH);
            if ((model == null) || (controller == null))
            {
                throw new FileNotFoundException("NPC 모델 프리팹이나 이동 애니메이터를 찾을 수 없습니다");
            }

            TMP_FontAsset regular = DungeonEntryFonts.Ensure("Regular");
            TMP_FontAsset bold    = DungeonEntryFonts.Ensure("Bold");
            DungeonEntryFonts.Preload(regular, bold, new[] { NPC_TITLE, NPC_SUBTITLE, PROMPT_TEXT });

            if (!AssetDatabase.IsValidFolder(NPC_FOLDER))
            {
                AssetDatabase.CreateFolder("Assets/02.Prefab", "Npc");
            }

            GameObject root = new GameObject("PF_RiftGuideNpc");
            try
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                instance.name = "Model";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                Animator animator = instance.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    throw new InvalidOperationException("NPC 모델에 Animator가 없습니다");
                }

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion           = false;
                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = false;
                }

                float top = MeasureHeight(instance);

                // 이름표와 안내는 카메라를 향해 돈다. 안내는 가까이 갔을 때만 켠다
                GameObject labels = new GameObject("Labels");
                labels.transform.SetParent(root.transform, false);
                labels.transform.localPosition = new Vector3(0f, top + LABEL_ABOVE_HEAD, 0f);
                labels.AddComponent<UiWorldBillboard>();

                MakeWorldLabel(labels.transform, "Nameplate", Vector3.zero,
                    NPC_TITLE + "\n<size=55%><color=#A6BBCB>" + NPC_SUBTITLE + "</color></size>", NAME_FONT_SIZE, G_Gold, bold);
                GameObject prompt = MakeWorldLabel(labels.transform, "Prompt", new Vector3(0f, PROMPT_OFFSET, 0f), PROMPT_TEXT,
                    PROMPT_FONT_SIZE, G_Mint, bold).gameObject;

                var trigger = root.AddComponent<NpcInteractionTrigger>();
                SetReference(trigger, "_promptRoot", prompt);
                var serialized = new SerializedObject(trigger);
                serialized.FindProperty("_interactDistance").floatValue = INTERACT_RANGE;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, NPC_PREFAB_PATH);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            AssetDatabase.SaveAssets();
            string result = "[UI] 균열 안내인 NPC 프리팹을 만들었다: " + NPC_PREFAB_PATH;
            Debug.Log(result);
            return result;
        }

        /// <summary> 마을 출시 씬의 벚꽃나무 아래에 NPC를 놓고 입장 창과 바인더를 연결한다. 끝나면 원래 열려 있던 씬으로 돌아간다 </summary>
        [MenuItem("Shinshi/UI/균열 안내인과 입장 창을 마을 씬에 배치")]
        public static string WireTownScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Play Mode에서는 출시 씬을 변경하지 않습니다");
            }

            for (int i = 0; i < SceneManager.sceneCount; ++i)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    throw new InvalidOperationException("열린 씬에 저장하지 않은 변경이 있습니다: " + SceneManager.GetSceneAt(i).path);
                }
            }

            GameObject npcPrefab    = AssetDatabase.LoadAssetAtPath<GameObject>(NPC_PREFAB_PATH);
            GameObject windowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DungeonEntryWindowBuilder.PREFAB_PATH);
            if ((npcPrefab == null) || (windowPrefab == null))
            {
                throw new InvalidOperationException("NPC나 입장 창 프리팹이 없습니다. 먼저 두 프리팹을 만드세요");
            }

            string previousPath = SceneManager.GetActiveScene().path;
            Scene scene = EditorSceneManager.OpenScene(TOWN_SCENE_PATH, OpenSceneMode.Single);
            try
            {
                RemoveExisting(scene);
                Transform tree = FindByName(scene, TREE_NAME);
                if (tree == null)
                {
                    throw new InvalidOperationException("마을 씬에서 " + TREE_NAME + "을 찾지 못했습니다");
                }

                Vector3 position = ResolveGround(tree.position + G_OffsetFromTree);
                GameObject npc = (GameObject)PrefabUtility.InstantiatePrefab(npcPrefab, scene);
                npc.name = NPC_NAME;
                npc.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, NPC_YAW, 0f));

                WireWindow(scene, windowPrefab, npc.GetComponent<NpcInteractionTrigger>());

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                string result = $"[UI] 균열 안내인을 마을 씬 {position}에 놓고 입장 창을 연결했다";
                Debug.Log(result);
                return result;
            }
            finally
            {
                if ((!string.IsNullOrEmpty(previousPath)) && (previousPath != TOWN_SCENE_PATH))
                {
                    EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
                }
            }
        }

        [MenuItem("Shinshi/UI/NPC_Dungeon에 던전 입장 연결")]
        public static string WireNpcDungeon()
        {
            return WireExistingNpc("NPC_Dungeon", "MinimapDevTarget");
        }

        /// <summary> 열린 씬에 이미 놓인 NPC 오브젝트에 상호작용 트리거와 이름표를 달고 입장 창과 바인더를 연결한다. 씬은 저장하지 않는다 </summary>
        // 개발 씬에는 네트워크 플레이어가 없어 가까움 판정은 fixedPlayerName의 오브젝트를 대상으로 한다. 접속이 없으면 입장 요청은
        // 창이 "마을 서버에 접속한 상태에서만"이라고 안내하고 보내지 않는다
        public static string WireExistingNpc(string npcObjectName, string fixedPlayerName)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Play Mode에서는 씬을 변경하지 않습니다");
            }

            Scene scene = SceneManager.GetActiveScene();
            Transform npc = FindByName(scene, npcObjectName);
            if (npc == null)
            {
                throw new InvalidOperationException("열린 씬에서 " + npcObjectName + "을 찾지 못했습니다");
            }

            var windowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DungeonEntryWindowBuilder.PREFAB_PATH);
            if (windowPrefab == null)
            {
                throw new InvalidOperationException("입장 창 프리팹이 없습니다: " + DungeonEntryWindowBuilder.PREFAB_PATH);
            }

            TMP_FontAsset regular = DungeonEntryFonts.Ensure("Regular");
            TMP_FontAsset bold    = DungeonEntryFonts.Ensure("Bold");
            DungeonEntryFonts.Preload(regular, bold, new[] { NPC_TITLE, NPC_SUBTITLE, PROMPT_TEXT });

            RemoveWindowObjects(scene);
            Transform oldLabels = npc.Find("Labels");
            if (oldLabels != null)
            {
                UnityEngine.Object.DestroyImmediate(oldLabels.gameObject);
            }

            GameObject labels = new GameObject("Labels");
            labels.transform.SetParent(npc, false);
            labels.transform.localPosition = new Vector3(0f, MeasureHeight(npc.gameObject) + LABEL_ABOVE_HEAD, 0f);
            labels.AddComponent<UiWorldBillboard>();
            MakeWorldLabel(labels.transform, "Nameplate", Vector3.zero,
                NPC_TITLE + "\n<size=55%><color=#A6BBCB>" + NPC_SUBTITLE + "</color></size>", NAME_FONT_SIZE, G_Gold, bold);
            GameObject prompt = MakeWorldLabel(labels.transform, "Prompt", new Vector3(0f, PROMPT_OFFSET, 0f), PROMPT_TEXT,
                PROMPT_FONT_SIZE, G_Mint, bold).gameObject;

            if (!npc.TryGetComponent(out NpcInteractionTrigger trigger))
            {
                trigger = npc.gameObject.AddComponent<NpcInteractionTrigger>();
            }

            var serialized = new SerializedObject(trigger);
            serialized.FindProperty("_interactDistance").floatValue = INTERACT_RANGE;
            serialized.FindProperty("_promptRoot").objectReferenceValue = prompt;
            Transform fixedPlayer = string.IsNullOrEmpty(fixedPlayerName) ? null : FindByName(scene, fixedPlayerName);
            serialized.FindProperty("_fixedPlayer").objectReferenceValue = fixedPlayer;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            WireWindow(scene, windowPrefab, trigger);
            EditorSceneManager.MarkSceneDirty(scene);
            string result = "[UI] " + npcObjectName + "에 던전 입장을 연결했다. 가까움 대상: " + ((fixedPlayer != null) ? fixedPlayer.name : "내 플레이어");
            Debug.Log(result);
            return result;
        }

        // NPC는 남기고 입장 창 캔버스와 바인더만 지운다
        private static void RemoveWindowObjects(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if ((root.name == CANVAS_NAME) || (root.name == BINDER_NAME))
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static void WireWindow(Scene scene, GameObject windowPrefab, NpcInteractionTrigger trigger)
        {
            // 캐릭터 상세창의 캔버스 설정을 그대로 따른다. 같은 씬의 다른 창과 배율, 입력 처리가 달라지지 않게 한다
            CharDetailView view = FindComponent<CharDetailView>(scene);
            HotkeyRouter router = FindComponent<HotkeyRouter>(scene);
            SortiePrepBinder sortie = FindComponent<SortiePrepBinder>(scene);
            if ((view == null) || (router == null) || (sortie == null))
            {
                throw new InvalidOperationException("마을 씬에서 캐릭터 상세창, 단축키 라우터, 출정 준비 바인더를 찾지 못했습니다");
            }

            Canvas referenceCanvas       = view.GetComponentInParent<Canvas>(true);
            CanvasScaler referenceScaler = (referenceCanvas != null) ? referenceCanvas.GetComponent<CanvasScaler>() : null;
            if ((referenceCanvas == null) || (referenceScaler == null))
            {
                throw new InvalidOperationException("캐릭터 상세창의 캔버스 설정을 찾지 못했습니다");
            }

            GameObject canvasObject = new GameObject(CANVAS_NAME, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            canvasObject.layer = LayerMask.NameToLayer(UI_LAYER);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode   = referenceCanvas.renderMode;
            canvas.sortingOrder = referenceCanvas.sortingOrder + 1;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = referenceScaler.uiScaleMode;
            scaler.referenceResolution = referenceScaler.referenceResolution;
            scaler.screenMatchMode     = referenceScaler.screenMatchMode;
            scaler.matchWidthOrHeight  = referenceScaler.matchWidthOrHeight;
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject window = (GameObject)PrefabUtility.InstantiatePrefab(windowPrefab, canvasObject.transform);
            window.name = WINDOW_NAME;

            GameObject bindingObject = new GameObject(BINDER_NAME);
            SceneManager.MoveGameObjectToScene(bindingObject, scene);
            DungeonEntryBinder binder = bindingObject.AddComponent<DungeonEntryBinder>();
            SetReference(binder, "_router", router);
            SetReference(binder, "_view", window.GetComponent<DungeonEntryView>());
            SetReference(binder, "_npc", trigger);
            SetReference(binder, "_sortiePrep", sortie);
        }

        private static void RemoveExisting(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if ((root.name == NPC_NAME) || (root.name == CANVAS_NAME) || (root.name == BINDER_NAME))
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        // 길찾기 지면에 붙인다. 근처에 지면이 없으면 후보 위치를 그대로 쓴다
        private static Vector3 ResolveGround(Vector3 candidate)
        {
            return NavMesh.SamplePosition(candidate, out NavMeshHit hit, SNAP_RADIUS, NavMesh.AllAreas) ? hit.position : candidate;
        }

        private static float MeasureHeight(GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return FALLBACK_HEIGHT;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; ++i)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return Mathf.Max(bounds.max.y - instance.transform.position.y, FALLBACK_HEIGHT);
        }

        private static TextMeshPro MakeWorldLabel(Transform parent, string name, Vector3 localPosition, string value, float size,
            Color color, TMP_FontAsset font)
        {
            GameObject labelObject = new GameObject(name);
            labelObject.transform.SetParent(parent, false);
            labelObject.transform.localPosition = localPosition;
            TextMeshPro text = labelObject.AddComponent<TextMeshPro>();
            text.font                    = font;
            text.text                    = value;
            text.fontSize                = size;
            text.color                   = color;
            text.alignment               = TextAlignmentOptions.Center;
            text.textWrappingMode        = TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta = new Vector2(LABEL_WIDTH, LABEL_HEIGHT);
            return text;
        }

        private static Transform FindByName(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate.name == name)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static T FindComponent<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        private static void SetReference(UnityEngine.Object target, string name, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException("연결할 필드가 없습니다: " + name);
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
