#if UNITY_EDITOR
// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// 프로젝트 내부
using Shinshi.Data;

namespace Shinshi.AI
{
    /// <summary> Dev_PrologueMap 씬에서 월백 시험 구동기를 붙여 숲 지형 위에서 월백 패턴을 눈으로 확인하게 해 주는 에디터 전용 장치, 프롤로그 시험 장치가 월백 조우 때 부른다 </summary>
    public static class PrologueMapDevBoot
    {
        // 씬 파일에는 아무것도 직렬화하지 않는다. 전장 중심과 플레이어 시작은 씬에 아래 이름의 빈 오브젝트를 놓으면 그 위치를 쓰고,
        // 없으면 기본값을 쓴다. 전장 중심의 기본값은 월드 원점(구역 1 중심이고 반경 3m 안에 소품이 없다)이고,
        // 플레이어의 기본값은 이 씬에 이미 있는 던전 시작점이다(중심에서 약 13m). 월백 중심은 서버 코드가 원점에 고정하므로
        // 원점이 아닌 위치로 바꿔 보는 것은 시험 구동기에서만 의미가 있다.
        // 시험 구동기의 위치 처리는 서버 몸체의 충돌과 경로 검사를 대신하지 않으므로 이 결과는 서버 경로의 증거가 아니다

        /****************************************
        *                Fields
        ****************************************/

        private const string SCENE_NAME           = "Dev_PrologueMap";
        private const string RIG_OBJECT_NAME      = "PrologueMapWolbaekRig";
        private const string CENTER_OBJECT_NAME   = "PrologueWolbaekArenaCenter";
        private const string START_OBJECT_NAME    = "PrologueWolbaekPlayerStart";
        private const string DUNGEON_START_OBJECT_NAME = "Start_01";
        private const string PRESENTER_PATH       = "Assets/01.Scripts/03.AI/Boss/VFX/Generated/PF_BossTelegraphSample.prefab";
        private const string BOSS_VIEW_PATH       = "Assets/02.Prefab/Boss/PF_WolbaekBoss.prefab";
        private const string REGION_ID            = "region_forest";
        private const string PRESENTER_FIELD      = "_presenterPrefab";
        private const string BOSS_VIEW_FIELD      = "_bossViewPrefab";
        private const string REFERENCE_ATTACK_FIELD = "_referenceAttack";
        private const string REFERENCE_HEALTH_FIELD = "_referenceHealth";
        private const string PLAYER_START_FIELD   = "_playerStart";
        private const string CLAMP_PLAYER_FIELD   = "_clampPlayerToArena";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 지금 열린 씬이 숲 지형 개발 씬인지 알려 준다 </summary>
        public static bool IsMapScene()
        {
            return SceneManager.GetActiveScene().name == SCENE_NAME;
        }

        /// <summary> 월백 시험 구동기를 만들어 붙이고 돌려준다. 숲 구간 장치가 월백 조우 때 부른다 </summary>
        public static GameObject SpawnWolbaekRig()
        {
            var presenter = AssetDatabase.LoadAssetAtPath<BossTelegraphPresenter>(PRESENTER_PATH);
            var bossView  = AssetDatabase.LoadAssetAtPath<BossView>(BOSS_VIEW_PATH);
            if ((presenter == null) || (bossView == null))
            {
                Debug.LogError($"[PrologueMapDevBoot] 프리팹을 찾지 못했다, presenter={presenter != null}, bossView={bossView != null}");
                return null;
            }

            // 위치 표시용 오브젝트는 에디터 전용 코드라 이름으로 한 번만 찾는다
            Vector3 center = Vector3.zero;
            GameObject centerObject = GameObject.Find(CENTER_OBJECT_NAME);
            if (centerObject != null)
            {
                center = centerObject.transform.position;
            }

            var rigObject = new GameObject(RIG_OBJECT_NAME);
            rigObject.transform.position = center;
            var rig = rigObject.AddComponent<WolbaekBossTestRig>();

            var serialized = new SerializedObject(rig);
            serialized.FindProperty(PRESENTER_FIELD).objectReferenceValue = presenter;
            serialized.FindProperty(BOSS_VIEW_FIELD).objectReferenceValue = bossView;

            GameObject startObject = GameObject.Find(START_OBJECT_NAME);
            if (startObject == null)
            {
                startObject = GameObject.Find(DUNGEON_START_OBJECT_NAME);
            }

            if (startObject != null)
            {
                serialized.FindProperty(PLAYER_START_FIELD).vector3Value = startObject.transform.position - center;
            }

            // 프롤로그에서는 월백 전장 밖으로 나가지 못하게 한다
            serialized.FindProperty(CLAMP_PLAYER_FIELD).boolValue = true;

            ApplyRegionReference(serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return rigObject;
        }

        // 서버가 쓰는 숲 기준값을 그대로 넣는다. GameDB가 안 열리면 구동기 기본값을 그대로 둔다
        private static void ApplyRegionReference(SerializedObject serialized)
        {
            GameDBBootstrap.EnsureInitialized();
            if (!GameDB.IsInitialized)
            {
                Debug.LogWarning("[PrologueMapDevBoot] GameDB가 열리지 않아 구동기 기본 기준값을 쓴다");
                return;
            }

            if (!GameDB.Progression.TryGetRegion(REGION_ID, out ProgressionCatalogSO.RegionDefinition region))
            {
                return;
            }

            serialized.FindProperty(REFERENCE_ATTACK_FIELD).floatValue = GameDB.Progression.GetReferenceAttack(region);
            serialized.FindProperty(REFERENCE_HEALTH_FIELD).floatValue = GameDB.Progression.GetReferenceHealth(region);
        }
    }
}
#endif
