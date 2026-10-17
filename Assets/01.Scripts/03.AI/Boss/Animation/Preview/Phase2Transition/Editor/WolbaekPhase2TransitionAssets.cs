// System
using System.IO;

// Unity
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    // 월백 2페이즈 전환 컷신 시안이 참조하는 기존 에셋의 GUID와 결과 경로를 한곳에 둔다
    // 정식 프리팹(PF_WolbaekBoss)은 네트워크와 패턴 표현 컴포넌트를 가지므로 쓰지 않고, 그 안에 들어 있는 모델 프리팹과 아바타만 같은 GUID로 가져온다
    /// <summary> 월백 2페이즈 전환 시안의 기존 에셋 참조와 출력 경로 </summary>
    public static class WolbaekPhase2TransitionAssets
    {
        /****************************************
        *              Constants
        ****************************************/

        public const string MENU_ROOT = "Shinshi/Boss Preview/Wolbaek Phase2 Transition/";

        public const string SCENE_PATH    = "Assets/00.Scenes/Dev_AI/Dev_WolbaekPhase2TransitionPreview.unity";
        public const string TIMELINE_DIR  = "Assets/01.Scripts/03.AI/Boss/Animation/Preview/Phase2Transition";
        public const string TIMELINE_PATH = TIMELINE_DIR + "/TL_WolbaekPhase2Transition.playable";

        // 기록 폴더는 .gitignore의 .claude/docs/ 아래라 커밋되지 않는다
        public const string RECORD_DIR = ".claude/docs/2026-10-17/wolbaek-phase2-transition";

        public const string URP_MATERIAL_DIR = "Assets/01.Scripts/03.AI/Boss/Animation/Preview/Mats/";

        public const string SUCCUBUS_INPLACE_DIR = "Assets/99.ThirdParty/Animation/Succubus_Anim_Set/Animation/Humanoid/Inplace/";

        // PF_WolbaekBoss 안의 모델 프리팹과 아바타
        public const string MODEL_PREFAB_GUID = "3af5b5dbb9b23a64eaf87518a829ffcb";
        public const string AVATAR_GUID       = "1157835604b9b0b459f4b310fdefb54a";
        public const string IDLE_CLIP_GUID    = "71411382272f0894c95a88e426572b3f";

        // PF_WolbaekBoss의 기믹 보호막(_shieldPrefab)과 모은 기운(_gatherPrefab)
        public const string SHIELD_PREFAB_GUID = "8dc31ec6489595f4db14088a66b8bdfe";
        public const string GATHER_PREFAB_GUID = "0a3357f39d15c7a409ecc42c799cb5ef";

        public const string PLAYER_PREFAB_GUID_PATH = "Assets/02.Prefab/Characters/PlayerP09/PF_PlayerP09.prefab";

        /****************************************
        *                Methods
        ****************************************/

        public static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        public static string RecordPath(string relative)
        {
            string path = Path.Combine(ProjectRoot(), RECORD_DIR, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return path;
        }

        public static T LoadByGuid<T>(string guid) where T : Object
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

        /// <summary> FBX 안의 첫 애니메이션 클립, 미리보기용 내부 클립은 건너뛴다 </summary>
        public static AnimationClip LoadClip(string assetPath)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < assets.Length; ++i)
            {
                AnimationClip clip = assets[i] as AnimationClip;
                if ((clip != null) && (!clip.name.StartsWith("__preview__")))
                {
                    return clip;
                }
            }

            return null;
        }

        public static AnimationClip LoadSuccubusClip(string name)
        {
            return LoadClip(SUCCUBUS_INPLACE_DIR + name + "_inplace.fbx");
        }
    }
}
