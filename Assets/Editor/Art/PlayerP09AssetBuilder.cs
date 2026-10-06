#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Unity
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Editor
{
    // P09 모듈 캐릭터를 게임에 쓰려고 원본을 건드리지 않고 프로젝트 쪽 에셋을 만든다.
    // 프리팹은 원본 P09_Human의 변형이라 재질만 URP 사본으로 덮어쓴다. 애니메이션은 CombatGirls 검 팩 FBX 클립을 .anim으로 복사해
    // 이벤트를 지우고 루트 이동, 회전을 루트 모션으로 분리한다(애니메이터 루트 모션을 꺼서 버려지므로 제자리 재생이 된다)
    /// <summary> P09 캐릭터 프리팹, 클립, 컨트롤러, 외형 카탈로그, 전투 애니메이션 매핑을 만든다 </summary>
    public static class PlayerP09AssetBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PREFAB_PATH     = "Assets/02.Prefab/Characters/PlayerP09/PF_PlayerP09.prefab";
        public const string CONTROLLER_PATH = "Assets/01.Scripts/05.Art/Characters/PlayerP09/Animations/AC_PlayerP09.controller";
        public const string CATALOG_PATH    = "Assets/09.Data/Avatar/PlayerCharacterLookCatalog.asset";
        public const string PROFILE_PATH    = "Assets/09.Data/Avatar/PlayerCombatAnimationProfile.asset";

        public const string PARAM_SPEED        = "Speed";
        public const string PARAM_ALIVE        = "Alive";
        public const string PARAM_ATTACK       = "Attack";
        public const string PARAM_SKILL        = "Skill";
        public const string PARAM_HIT          = "Hit";
        public const string PARAM_DODGE        = "Dodge";
        public const string PARAM_ACTION_INDEX = "ActionIndex";
        public const string PARAM_ACTION_SPEED = "ActionSpeed";
        public const string PARAM_FACE_EMOTION = "FaceEmotion";

        private const string MENU_PATH        = "Shinshi/Art/P09/Build Assets";
        private const string SOURCE_PREFAB    = "Assets/99.ThirdParty/Model/P09_Modular_Humanoid/Model_DATA/Prefab/P09_Human.prefab";
        private const string SOURCE_FACE_ROOT = "Assets/99.ThirdParty/Model/P09_Modular_Humanoid/Scenes/DemoScene_Data/Animation/Facial_Emotion";
        private const string SOURCE_CLIP_ROOT = "Assets/99.ThirdParty/Animation/CombatGirlsCharacterPack/Sword_Girl/Animations";
        private const string ANIMATION_FOLDER = "Assets/01.Scripts/05.Art/Characters/PlayerP09/Animations";
        private const string CLIP_FOLDER      = ANIMATION_FOLDER + "/Clips";
        private const string MASK_PATH        = ANIMATION_FOLDER + "/AM_PlayerP09_FaceOnly.mask";
        private const string REPORT_PATH      = ANIMATION_FOLDER + "/README.md";
        private const string SKILL_CATALOG    = "Assets/09.Data/Combat/SkillCatalog.asset";
        private const string CLIP_PREFIX      = "AN_P09_";
        private const string WEAPON_NODE      = "Weapon";
        private const string PREVIEW_PREFIX   = "__preview__";
        private const string DODGE_CLIP       = "Special/S_QuickShift_F";
        private const string DIE_CLIP         = "Normal/S_Die";

        private const float WALK_SPEED          = 2.5f;
        private const float RUN_SPEED           = 5f;
        private const float ACTION_BLEND        = 0.08f;
        private const float ACTION_EXIT_TIME    = 0.95f;
        private const float ACTION_EXIT_BLEND   = 0.15f;
        private const float DIE_BLEND           = 0.1f;
        private const float REVIVE_BLEND        = 0.3f;
        private const float FACE_BLEND          = 0.15f;
        private const float MIN_SPEED           = 0.5f;
        private const float MAX_SPEED           = 3f;
        private const float MAX_OVERRUN_SECONDS = 0.5f;
        private const int   BUST_SIZE_COUNT     = 3;

        // 원본 FBX(Animations 아래 경로)
        private static readonly string[] G_LocomotionClips = { "Normal/S_Idle", "Normal/S_Walk", "Normal/S_Run" };
        private static readonly string[] G_AttackClips     = { "Normal/S_Attack_1", "Normal/S_Attack_2", "Normal/S_Attack_3" };
        private static readonly string[] G_SkillClips      = { "Special/S_Sp_Skill_1", "Special/S_Sp_Skill_2", "Special/S_Sp_Skill_3",
                                                               "Normal/S_Attack_3", "Special/S_QuickShift_F", "Normal/S_Attack_1",
                                                               "Normal/S_Attack_2" };
        private static readonly string[] G_HitClips        = { "Normal/S_Hit_L", "Normal/S_Hit_R" };

        private static readonly string[] G_FaceEmotions = { "P09_Facial_Neutoral", "P09_Facial_Damage", "P09_Facial_Failed",
            "P09_Facial_Fierce", "P09_Facial_Fun", "P09_Facial_Jito", "P09_Facial_Sad", "P09_Facial_Sharp", "P09_Facial_Smile",
            "P09_Facial_Surprised" };

        // 기본 배정(직업, 종류 0 일반 1 강림, 슬롯, 기술 인덱스). 지연 기준을 넘으면 BuildProfile이 더 짧은 클립으로 바꾼다
        private static readonly int[][] G_DefaultSkillMap = {
            new[] { (int)EPlayerClass.Warrior,  0, 0, 3 }, new[] { (int)EPlayerClass.Warrior,  0, 1, 4 },
            new[] { (int)EPlayerClass.Warrior,  0, 2, 2 }, new[] { (int)EPlayerClass.Warrior,  0, 3, 0 },
            new[] { (int)EPlayerClass.Sorcerer, 0, 0, 0 }, new[] { (int)EPlayerClass.Sorcerer, 0, 1, 1 },
            new[] { (int)EPlayerClass.Sorcerer, 0, 2, 2 }, new[] { (int)EPlayerClass.Sorcerer, 0, 3, 0 },
            new[] { (int)EPlayerClass.Summoner, 0, 0, 0 }, new[] { (int)EPlayerClass.Summoner, 0, 1, 1 },
            new[] { (int)EPlayerClass.Summoner, 0, 2, 2 }, new[] { (int)EPlayerClass.Summoner, 0, 3, 0 },
            new[] { (int)EPlayerClass.Warrior,  1, 0, 0 }, new[] { (int)EPlayerClass.Warrior,  1, 1, 1 },
            new[] { (int)EPlayerClass.Warrior,  1, 2, 2 },
            new[] { (int)EPlayerClass.Sorcerer, 1, 0, 0 }, new[] { (int)EPlayerClass.Sorcerer, 1, 1, 1 },
            new[] { (int)EPlayerClass.Sorcerer, 1, 2, 2 },
            new[] { (int)EPlayerClass.Summoner, 1, 0, 0 }, new[] { (int)EPlayerClass.Summoner, 1, 1, 1 },
            new[] { (int)EPlayerClass.Summoner, 1, 2, 2 } };

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem(MENU_PATH)]
        private static void BuildMenu()
        {
            Debug.Log(BuildAll());
        }

        /// <summary> 재질 변환부터 카탈로그까지 순서대로 만들고 결과 요약을 돌려준다 </summary>
        public static string BuildAll()
        {
            var log = new StringBuilder();
            log.AppendLine(P09MaterialConverter.Convert());
            log.AppendLine(BuildPrefab());

            var clips = new Dictionary<string, AnimationClip>();
            log.AppendLine(BuildClips(clips));
            if (clips.Count == 0)
            {
                return log.ToString();
            }

            AvatarMask         mask       = BuildFaceMask();
            AnimatorController controller = BuildController(clips, mask);
            log.AppendLine("[P09] 컨트롤러 " + CONTROLLER_PATH);
            log.AppendLine(BuildProfile(clips));
            log.AppendLine(BuildCatalog(controller));
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        /// <summary> 원본 프리팹의 변형을 만들고 재질을 URP 사본으로 바꾼다 </summary>
        public static string BuildPrefab()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SOURCE_PREFAB);
            if (source == null)
            {
                return "[P09] 원본 프리팹이 없다: " + SOURCE_PREFAB;
            }

            EnsureFolder(Path.GetDirectoryName(PREFAB_PATH).Replace('\\', '/'));
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            int replaced = 0;
            var missing  = new List<string>();
            try
            {
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; ++i)
                {
                    Material[] materials = renderers[i].sharedMaterials;
                    for (int m = 0; m < materials.Length; ++m)
                    {
                        if (materials[m] == null)
                        {
                            continue;
                        }

                        var converted = AssetDatabase.LoadAssetAtPath<Material>(P09MaterialConverter.GetConvertedPath(materials[m].name));
                        if (converted != null)
                        {
                            materials[m] = converted;
                            ++replaced;
                        }
                        else if (!missing.Contains(materials[m].name))
                        {
                            missing.Add(materials[m].name);
                        }
                    }

                    renderers[i].sharedMaterials = materials;
                }

                Transform weapon = instance.transform.Find(WEAPON_NODE);
                if (weapon != null)
                {
                    weapon.gameObject.SetActive(false);
                }

                Animator animator = instance.GetComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.applyRootMotion           = false;

                PrefabUtility.SaveAsPrefabAsset(instance, PREFAB_PATH);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            // 무기는 꺼 두므로 무기 재질의 사본이 없는 것은 정상이다
            return $"[P09] 프리팹 {PREFAB_PATH}, 재질 슬롯 {replaced}개 교체, 사본 없음: {string.Join(", ", missing)}";
        }

        private static string BuildClips(Dictionary<string, AnimationClip> clips)
        {
            EnsureFolder(CLIP_FOLDER);
            var sources = new List<string>();
            sources.AddRange(G_LocomotionClips);
            sources.AddRange(G_AttackClips);
            sources.AddRange(G_SkillClips);
            sources.AddRange(G_HitClips);
            sources.Add(DODGE_CLIP);
            sources.Add(DIE_CLIP);

            var failures = new List<string>();
            for (int i = 0; i < sources.Count; ++i)
            {
                string key = sources[i];
                if (clips.ContainsKey(key))
                {
                    continue;
                }

                AnimationClip clip = CopyClip(key, Array.IndexOf(G_LocomotionClips, key) >= 0);
                if (clip == null)
                {
                    failures.Add(key);
                    continue;
                }

                clips.Add(key, clip);
            }

            if (failures.Count > 0)
            {
                clips.Clear();
                return "[P09] 클립을 복사하지 못했다: " + string.Join(", ", failures);
            }

            return $"[P09] 클립 {clips.Count}개 복사";
        }

        private static AnimationClip CopyClip(string sourceKey, bool loop)
        {
            string fbxPath = SOURCE_CLIP_ROOT + "/" + sourceKey + ".fbx";
            AnimationClip source = null;
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            for (int i = 0; i < assets.Length; ++i)
            {
                if ((assets[i] is AnimationClip candidate) && (!candidate.name.StartsWith(PREVIEW_PREFIX)))
                {
                    source = candidate;
                    break;
                }
            }

            if (source == null)
            {
                return null;
            }

            string path = CLIP_FOLDER + "/" + CLIP_PREFIX + Path.GetFileName(sourceKey) + ".anim";
            var    copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (copy == null)
            {
                copy = new AnimationClip();
                EditorUtility.CopySerialized(source, copy);
                AssetDatabase.CreateAsset(copy, path);
            }
            else
            {
                EditorUtility.CopySerialized(source, copy);
            }

            copy.name = Path.GetFileNameWithoutExtension(path);
            AnimationUtility.SetAnimationEvents(copy, Array.Empty<AnimationEvent>());

            // XZ 이동과 회전은 굽지 않아 루트 모션으로 분리되고, 루트 모션을 끈 애니메이터가 버리므로 제자리에서 재생된다.
            // Y는 원래 높이를 기준으로 굽어 발 높이를 지킨다
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime              = loop;
            settings.loopBlendOrientation  = false;
            settings.loopBlendPositionXZ   = false;
            settings.loopBlendPositionY    = true;
            settings.keepOriginalPositionY = true;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            EditorUtility.SetDirty(copy);
            return copy;
        }

        // 표정 레이어는 블렌드셰이프만 움직인다. 휴머노이드 부위를 모두 꺼서 몸 포즈에 끼어들지 않게 한다
        private static AvatarMask BuildFaceMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MASK_PATH);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, MASK_PATH);
            }

            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; ++i)
            {
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            }

            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static AnimatorController BuildController(Dictionary<string, AnimationClip> clips, AvatarMask mask)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(CONTROLLER_PATH) != null)
            {
                AssetDatabase.DeleteAsset(CONTROLLER_PATH);
            }

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(CONTROLLER_PATH);
            controller.AddParameter(PARAM_SPEED, AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = PARAM_ALIVE, type = AnimatorControllerParameterType.Bool, defaultBool = true });
            controller.AddParameter(PARAM_ATTACK, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(PARAM_SKILL, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(PARAM_HIT, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(PARAM_DODGE, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(PARAM_ACTION_INDEX, AnimatorControllerParameterType.Int);
            controller.AddParameter(new AnimatorControllerParameter { name = PARAM_ACTION_SPEED, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            controller.AddParameter(PARAM_FACE_EMOTION, AnimatorControllerParameterType.Int);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType              = BlendTreeType.Simple1D;
            tree.blendParameter         = PARAM_SPEED;
            tree.useAutomaticThresholds = false;
            tree.AddChild(clips[G_LocomotionClips[0]], 0f);
            tree.AddChild(clips[G_LocomotionClips[1]], WALK_SPEED);
            tree.AddChild(clips[G_LocomotionClips[2]], RUN_SPEED);
            machine.defaultState = locomotion;

            for (int i = 0; i < G_AttackClips.Length; ++i)
            {
                AddAction(machine, locomotion, "Attack" + i, clips[G_AttackClips[i]], PARAM_ATTACK, i);
            }

            for (int i = 0; i < G_SkillClips.Length; ++i)
            {
                AddAction(machine, locomotion, "Skill" + i, clips[G_SkillClips[i]], PARAM_SKILL, i);
            }

            for (int i = 0; i < G_HitClips.Length; ++i)
            {
                AddAction(machine, locomotion, "Hit" + i, clips[G_HitClips[i]], PARAM_HIT, i);
            }

            AddAction(machine, locomotion, "Dodge", clips[DODGE_CLIP], PARAM_DODGE, -1);

            AnimatorState die = machine.AddState("Die");
            die.motion = clips[DIE_CLIP];
            AnimatorStateTransition toDie = machine.AddAnyStateTransition(die);
            toDie.AddCondition(AnimatorConditionMode.IfNot, 0f, PARAM_ALIVE);
            toDie.duration            = DIE_BLEND;
            toDie.canTransitionToSelf = false;
            toDie.hasExitTime         = false;
            AnimatorStateTransition revive = die.AddTransition(locomotion);
            revive.AddCondition(AnimatorConditionMode.If, 0f, PARAM_ALIVE);
            revive.duration    = REVIVE_BLEND;
            revive.hasExitTime = false;

            BuildFaceLayer(controller, mask);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        // 행동 상태는 ActionSpeed를 재생 속도로 쓰고, 같은 행동을 연속으로 받으면 처음부터 다시 재생한다
        private static void AddAction(AnimatorStateMachine machine, AnimatorState locomotion, string name, AnimationClip clip,
            string trigger, int index)
        {
            AnimatorState state = machine.AddState(name);
            state.motion               = clip;
            state.speedParameter       = PARAM_ACTION_SPEED;
            state.speedParameterActive = true;

            AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
            enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            enter.AddCondition(AnimatorConditionMode.If, 0f, PARAM_ALIVE);
            if (index >= 0)
            {
                enter.AddCondition(AnimatorConditionMode.Equals, index, PARAM_ACTION_INDEX);
            }

            enter.duration            = ACTION_BLEND;
            enter.canTransitionToSelf = true;
            enter.hasExitTime         = false;

            AnimatorStateTransition exit = state.AddTransition(locomotion);
            exit.hasExitTime = true;
            exit.exitTime    = ACTION_EXIT_TIME;
            exit.duration    = ACTION_EXIT_BLEND;
        }

        private static void BuildFaceLayer(AnimatorController controller, AvatarMask mask)
        {
            var machine = new AnimatorStateMachine { name = "Face", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(machine, controller);

            AnimatorState none = machine.AddState("None");
            machine.defaultState = none;
            AnimatorStateTransition toNone = machine.AddAnyStateTransition(none);
            toNone.AddCondition(AnimatorConditionMode.Equals, 0, PARAM_FACE_EMOTION);
            toNone.duration            = FACE_BLEND;
            toNone.canTransitionToSelf = false;

            for (int i = 0; i < G_FaceEmotions.Length; ++i)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(SOURCE_FACE_ROOT + "/" + G_FaceEmotions[i] + ".anim");
                AnimatorState state = machine.AddState(G_FaceEmotions[i]);
                state.motion = clip;
                AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
                enter.AddCondition(AnimatorConditionMode.Equals, i + 1, PARAM_FACE_EMOTION);
                enter.duration            = FACE_BLEND;
                enter.canTransitionToSelf = false;
            }

            controller.AddLayer(new AnimatorControllerLayer
            {
                name          = "Face",
                defaultWeight = 1f,
                blendingMode  = AnimatorLayerBlendingMode.Override,
                avatarMask    = mask,
                stateMachine  = machine
            });
        }

        // 기술 목표 시간에 3배속으로도 0.5초 넘게 늦는 배정은 지연이 가장 작은 기술 클립으로 바꾸고 표로 남긴다
        private static string BuildProfile(Dictionary<string, AnimationClip> clips)
        {
            var skillCatalog = AssetDatabase.LoadAssetAtPath<SkillCatalogSO>(SKILL_CATALOG);
            if (skillCatalog == null)
            {
                return "[P09] 스킬 카탈로그가 없다: " + SKILL_CATALOG;
            }

            float[] attackLengths = LengthsOf(clips, G_AttackClips);
            float[] skillLengths  = LengthsOf(clips, G_SkillClips);
            float[] hitLengths    = LengthsOf(clips, G_HitClips);
            float   dodgeLength   = clips[DODGE_CLIP].length;

            var report  = new StringBuilder();
            var entries = new List<PlayerCombatAnimationProfileSO.SkillAnimation>();
            report.AppendLine("# AC_PlayerP09 기술 배정과 시각 지연");
            report.AppendLine();
            report.AppendLine("`Assets/Editor/Art/PlayerP09AssetBuilder.cs`가 만든다. 속도 = 클립 길이 / 목표 시간을 0.5 ~ 3으로 자른 값, 지연 = 클립 길이 / 속도 - 목표 시간.");
            report.AppendLine("지연이 0.5초를 넘으면 지연이 가장 작은 기술 클립으로 바꾼다. 도술사, 신령사는 검 모션 임시 배정이다.");
            report.AppendLine();
            report.AppendLine("| 직업 | 종류 | 슬롯 | 목표(초) | 기본 클립 | 최종 클립 | 속도 | 지연(초) |");
            report.AppendLine("|---|---|---|---|---|---|---|---|");

            int overLimit = 0;
            for (int i = 0; i < G_DefaultSkillMap.Length; ++i)
            {
                var playerClass = (EPlayerClass)G_DefaultSkillMap[i][0];
                var kind        = (ESkillRequestKind)G_DefaultSkillMap[i][1];
                int slot        = G_DefaultSkillMap[i][2];
                int index       = G_DefaultSkillMap[i][3];
                if (!TryGetSkillSeconds(skillCatalog, playerClass, kind, slot, out float target))
                {
                    continue;
                }

                int chosen = index;
                if (Overrun(skillLengths[index], target) > MAX_OVERRUN_SECONDS)
                {
                    for (int c = 0; c < skillLengths.Length; ++c)
                    {
                        if (Overrun(skillLengths[c], target) < Overrun(skillLengths[chosen], target))
                        {
                            chosen = c;
                        }
                    }
                }

                float overrun = Overrun(skillLengths[chosen], target);
                if (overrun > MAX_OVERRUN_SECONDS)
                {
                    ++overLimit;
                }

                entries.Add(new PlayerCombatAnimationProfileSO.SkillAnimation(playerClass, kind, slot, chosen));
                report.Append("| ").Append(playerClass).Append(" | ").Append(kind).Append(" | ").Append(slot).Append(" | ")
                    .Append(target.ToString("F2")).Append(" | ").Append(Path.GetFileName(G_SkillClips[index])).Append(" | ")
                    .Append(Path.GetFileName(G_SkillClips[chosen])).Append(" | ")
                    .Append(Mathf.Clamp(skillLengths[chosen] / target, MIN_SPEED, MAX_SPEED).ToString("F2")).Append(" | ")
                    .Append(overrun.ToString("F2")).AppendLine(" |");
            }

            var profile = AssetDatabase.LoadAssetAtPath<PlayerCombatAnimationProfileSO>(PROFILE_PATH);
            if (profile == null)
            {
                EnsureFolder(Path.GetDirectoryName(PROFILE_PATH).Replace('\\', '/'));
                profile = ScriptableObject.CreateInstance<PlayerCombatAnimationProfileSO>();
                AssetDatabase.CreateAsset(profile, PROFILE_PATH);
            }

            profile.EditorSetup(entries.ToArray(), attackLengths, skillLengths, hitLengths, dodgeLength);
            EditorUtility.SetDirty(profile);
            File.WriteAllText(REPORT_PATH, report.ToString());

            string summary = $"[P09] 기술 배정 {entries.Count}개, 지연 기준 초과 {overLimit}개";
            if (overLimit > 0)
            {
                Debug.LogError(summary);
            }

            return summary;
        }

        private static bool TryGetSkillSeconds(SkillCatalogSO catalog, EPlayerClass playerClass, ESkillRequestKind kind, int slot,
            out float seconds)
        {
            seconds = 0f;
            if (kind == ESkillRequestKind.Regular)
            {
                if (!catalog.TryGetSkillBySlot(playerClass, slot, out SkillCatalogSO.SkillDefinition skill))
                {
                    return false;
                }

                seconds = skill.Timeline.Windup + skill.Timeline.Active + skill.Timeline.Recovery;
                return seconds > 0f;
            }

            IReadOnlyList<SkillCatalogSO.AscensionDefinition> ascensions = catalog.Ascensions;
            for (int i = 0; i < ascensions.Count; ++i)
            {
                if (ascensions[i].Slot == slot)
                {
                    seconds = ascensions[i].Windup + ascensions[i].Recovery;
                    return seconds > 0f;
                }
            }

            return false;
        }

        private static float Overrun(float clipLength, float target)
        {
            float speed = Mathf.Clamp(clipLength / target, MIN_SPEED, MAX_SPEED);
            return Mathf.Max(0f, (clipLength / speed) - target);
        }

        private static float[] LengthsOf(Dictionary<string, AnimationClip> clips, string[] keys)
        {
            var lengths = new float[keys.Length];
            for (int i = 0; i < keys.Length; ++i)
            {
                lengths[i] = clips[keys[i]].length;
            }

            return lengths;
        }

        private static string BuildCatalog(RuntimeAnimatorController controller)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlayerCharacterLookCatalogSO>(CATALOG_PATH);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<PlayerCharacterLookCatalogSO>();
                AssetDatabase.CreateAsset(catalog, CATALOG_PATH);
            }

            var so = new SerializedObject(catalog);
            so.FindProperty("_prefab").objectReferenceValue     = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            so.FindProperty("_controller").objectReferenceValue = controller;
            SetMaterials(so.FindProperty("_femaleSkins"), "P09_Female_Skin", "P09_Female_Skin_Bright", "P09_Female_Skin_Dark");
            SetMaterials(so.FindProperty("_maleSkins"), "P09_Male_Skin", "P09_Male_Skin_Bright", "P09_Male_Skin_Dark");
            SetMaterials(so.FindProperty("_eyes"), "P09_Eye_Yellow", "P09_Eye_Bright", "P09_Eye_Purple", "P09_Eye_Cyan", "P09_Eye_Red");
            SetMaterials(so.FindProperty("_hairColors"), "P09_Hair_Brown", "P09_Hair_Blue", "P09_Hair_Gold", "P09_Hair_Green",
                "P09_Hair_Ivory", "P09_Hair_Orange", "P09_Hair_Pink", "P09_Hair_Purple", "P09_Hair_Red");

            // 데모 BustSize 데이터(S, M, L)와 같은 값
            SerializedProperty bust = so.FindProperty("_bustScales");
            bust.arraySize = BUST_SIZE_COUNT;
            bust.GetArrayElementAtIndex(0).vector3Value = new Vector3(0.85f, 0.8f, 0.85f);
            bust.GetArrayElementAtIndex(1).vector3Value = Vector3.one;
            bust.GetArrayElementAtIndex(2).vector3Value = new Vector3(1.2f, 1.2f, 1.2f);

            SerializedProperty emotions = so.FindProperty("_faceEmotions");
            emotions.arraySize = G_FaceEmotions.Length;
            for (int i = 0; i < G_FaceEmotions.Length; ++i)
            {
                emotions.GetArrayElementAtIndex(i).stringValue = G_FaceEmotions[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);

            var errors = new List<string>();
            catalog.Validate(errors);
            return (errors.Count == 0) ? "[P09] 카탈로그 " + CATALOG_PATH : "[P09] 카탈로그 오류: " + string.Join(", ", errors);
        }

        private static void SetMaterials(SerializedProperty property, params string[] sourceNames)
        {
            property.arraySize = sourceNames.Length;
            for (int i = 0; i < sourceNames.Length; ++i)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Material>(P09MaterialConverter.GetConvertedPath(sourceNames[i]));
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
