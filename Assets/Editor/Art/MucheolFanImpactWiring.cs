// Unity
using UnityEditor;
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Art;

// 부채꼴 착탄 VFX를 보스 프리팹에 연결하고, 같은 자리를 그리던 기존 지면 강타 타격 레이어를 비운다
// 여러 번 실행해도 같은 결과가 나온다. 예고 레이어(Warning)와 재생기 타이밍은 그대로 둔다
public static class MucheolFanImpactWiring
{
    /****************************************
    *                Fields
    ****************************************/

    private const string BOSS_PREFAB_PATH    = "Assets/02.Prefab/Boss/PF_MucheolBoss.prefab";
    private const string FAN_SLAM_PREFAB_PATH = "Assets/02.Prefab/VFX/Mucheol/PF_Mucheol_FanSlam70.prefab";
    private const string SMALL_WAVE_PREFAB_PATH = "Assets/02.Prefab/VFX/Mucheol/PF_Mucheol_04_Slam.prefab";
    private const string LARGE_WAVE_PREFAB_PATH = "Assets/02.Prefab/VFX/Mucheol/PF_Mucheol_05.prefab";
    private const string CIRCLE_PREFAB_PATH    = "Assets/02.Prefab/VFX/Mucheol/PF_Mucheol_02_Slam.prefab";
    private const string BATTLEFIELD_PREFAB_PATH = "Assets/02.Prefab/VFX/Mucheol/PF_Mucheol_07_Slam.prefab";
    private const string WAVE_PREFAB_PATH    = "Assets/02.Prefab/VFX/Mucheol/FanImpact/Prefabs/PF_VFX_MucheolWaveImpact.prefab";
    private const string IMPACT_PREFAB_PATH  = "Assets/02.Prefab/VFX/Mucheol/FanImpact/Prefabs/PF_VFX_MucheolFanImpact.prefab";

    /****************************************
    *                Methods
    ****************************************/

    [MenuItem("Giwoong/VFX/Wire Fan Impact")]
    public static void Wire()
    {
        WireBoss();
        StripTemplate(FAN_SLAM_PREFAB_PATH);
        StripTemplate(SMALL_WAVE_PREFAB_PATH);
        StripTemplate(LARGE_WAVE_PREFAB_PATH);
        StripTemplate(CIRCLE_PREFAB_PATH);
        StripTemplate(BATTLEFIELD_PREFAB_PATH);
        AssetDatabase.SaveAssets();
        Debug.Log("FanImpact 연결 완료: 보스 프리팹 필드, 지면 강타와 두 충격파의 기존 타격 레이어 비움");
    }

    private static void WireBoss()
    {
        GameObject impact = AssetDatabase.LoadAssetAtPath<GameObject>(IMPACT_PREFAB_PATH);
        MucheolFanImpactView view = (impact != null) ? impact.GetComponent<MucheolFanImpactView>() : null;
        if (view == null)
        {
            Debug.LogError("부채꼴 착탄 프리팹이 없다: " + IMPACT_PREFAB_PATH);
            return;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(BOSS_PREFAB_PATH);
        try
        {
            MucheolPatternVfxView vfx = contents.GetComponentInChildren<MucheolPatternVfxView>(true);
            if (vfx == null)
            {
                Debug.LogError("보스 프리팹에 MucheolPatternVfxView가 없다");
                return;
            }

            SerializedObject so = new SerializedObject(vfx);
            so.FindProperty("_fanImpactPrefab").objectReferenceValue = view;
            GameObject wave = AssetDatabase.LoadAssetAtPath<GameObject>(WAVE_PREFAB_PATH);
            so.FindProperty("_waveImpactPrefab").objectReferenceValue = (wave != null) ? wave.GetComponent<MucheolWaveImpactView>() : null;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(contents, BOSS_PREFAB_PATH);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // 기존 타격 연출(바닥, 균열, 충격파, 번쩍임, 파편, 지면 강타 애니메이션)을 재생기에서 떼고 숨긴다. 새 착탄 VFX가 대신 그린다
    private static void StripTemplate(string prefabPath)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            MucheolImpactVfxPlayer impact = contents.GetComponentInChildren<MucheolImpactVfxPlayer>(true);
            MucheolBatchVfxPlayer batch   = contents.GetComponentInChildren<MucheolBatchVfxPlayer>(true);
            if (impact == null)
            {
                Debug.LogError("MucheolImpactVfxPlayer가 없다: " + prefabPath);
                return;
            }

            SerializedObject impactObject = new SerializedObject(impact);
            Transform warning = null;
            SerializedProperty warningProperty = impactObject.FindProperty("_warning");
            if ((warningProperty != null) && (warningProperty.objectReferenceValue is Component warningComponent))
            {
                warning = warningComponent.transform;
            }

            foreach (string field in new[] { "_ground", "_cracks", "_shock", "_flash", "_root" })
            {
                impactObject.FindProperty(field).objectReferenceValue = null;
            }

            impactObject.FindProperty("_rocks").arraySize     = 0;
            impactObject.FindProperty("_particles").arraySize = 0;
            impactObject.ApplyModifiedPropertiesWithoutUndo();

            if (batch != null)
            {
                SerializedObject batchObject = new SerializedObject(batch);
                foreach (string field in new[] { "_groundSlam", "_chargeHead", "_shockTransform" })
                {
                    SerializedProperty property = batchObject.FindProperty(field);
                    if (property != null)
                    {
                        property.objectReferenceValue = null;
                    }
                }

                batchObject.ApplyModifiedPropertiesWithoutUndo();
            }

            // 예고 레이어와 그 부모 말고는 모두 끈다
            Transform root = contents.transform;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if ((child == root) || IsWarningBranch(child, warning))
                {
                    continue;
                }

                child.gameObject.SetActive(false);
            }

            PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // 예고 자신이거나 예고의 부모(예고가 켜지려면 부모가 켜져 있어야 한다)이거나 예고의 자식이면 남긴다
    private static bool IsWarningBranch(Transform node, Transform warning)
    {
        if (warning == null)
        {
            return false;
        }

        return (node == warning) || warning.IsChildOf(node) || node.IsChildOf(warning);
    }
}
