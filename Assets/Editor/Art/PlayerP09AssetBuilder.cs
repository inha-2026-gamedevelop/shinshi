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

        // 검사, 마검사, 궁수용 독립 생성물, 위의 기존 경로는 건드리지 않는다
        public const string CLASS_CONTROLLER_PATH = "Assets/01.Scripts/05.Art/Characters/PlayerP09/Animations/AC_PlayerP09_Classes.controller";
        public const string CLASS_CATALOG_PATH    = "Assets/09.Data/Avatar/PlayerCharacterLookCatalog_Classes.asset";
        public const string CLASS_PROFILE_PATH    = "Assets/09.Data/Avatar/PlayerCombatAnimationProfile_Classes.asset";
        public const string CLASS_CLIP_FOLDER     = "Assets/01.Scripts/05.Art/Characters/PlayerP09/Animations/ClassClips";

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

        // 신규 직업 클립 키 접두부, CG는 기존 Clips 사본, MK와 AR은 ClassClips로 새로 복사한다
        private const string CLASS_MENU_PATH   = "Shinshi/Art/P09/Build Class Assets (Swordsman, Mageknight, Archer)";
        private const string CLASS_VERIFY_MENU_PATH = "Shinshi/Art/P09/Verify Class Assets Rerun Keeps GUIDs";
        private const string MK_CLIP_ROOT      = "Assets/99.ThirdParty/Animation/Magical-Knight_Set/Animation/Humanoid/Inplace";
        private const string ARCHER_CLIP_ROOT  = "Assets/99.ThirdParty/Animation/Grruzam Archer Animation/Animation/Archer/2_Attack";
        private const string KEY_BASE          = "CG/";
        private const string KEY_MK            = "MK/";
        private const string KEY_ARCHER        = "AR/";
        private const float  MAX_ASSIGN_SPEED  = 1.5f;

        private const string HOLD_KS01 = "문서 보류: Sp_Skill_1 사본 약 4.0초가 설계 예산을 크게 넘는다. 주 후보 재검토 필수";
        private const string HOLD_KS02 = "문서 보류: QuickShift_F+Attack_3 전체 순차 재생이 예산을 넘는다. 서버 이동/검격과 루트 중복 확인 필요";
        private const string HOLD_KS03 = "문서 보류: Sp_Skill_3 사본 약 4.4초가 설계 예산을 크게 넘는다. 주 후보 재검토 필수";
        private const string HOLD_KS04 = "문서 보류: Attack_2+Attack_3가 예산을 넘는다. 후속 간격과 연결 구간 재설계 필요";
        private const string HOLD_AR_E = "문서 보류: Shoot+End 약 3.33배 압축이 필요하다. 전체 순차 사용 보류, 구간/시간표 재설계 필수";

        private static readonly EPlayerClass[] G_ClassOrder           = { EPlayerClass.Swordsman, EPlayerClass.Mageknight, EPlayerClass.Archer };
        private static readonly float[]        G_ClassAttackIntervals = { 0.9f, 0.85f, 0.95f };
        private static readonly float[]        G_ClassComboResets     = { 1.2f, 1.2f, 1.2f };

        // 서버 콤보 직업의 단계별 주기(PlayerConfigSO 단계 명세와 같은 값). 없는 직업은 직업 주기 하나를 모든 단계에 쓴다
        private static readonly float[]        G_ArcherStepIntervals  = { 0.6f, 0.75f, 1.3f };

        // 후보 순서대로 시간 예산을 시험하고 처음 통과한 후보만 임시 배정한다. 어느 후보도 통과하지 못하거나 문서 보류면 배정하지 않는다
        private static readonly MotionPlan[] G_ClassPlans = {
            new MotionPlan(EPlayerClass.Swordsman, true, 0, "기본 1", null, new[] { "CUT/SwordAttack1" }),
            new MotionPlan(EPlayerClass.Swordsman, true, 1, "기본 2", null, new[] { "CUT/SwordAttack2" }),
            new MotionPlan(EPlayerClass.Swordsman, true, 2, "기본 3", null, new[] { "CUT/SwordAttack3" }),
            new MotionPlan(EPlayerClass.Swordsman, false, 0, "KS01 넓게 베기", null, new[] { "CUT/SwordWide" }),
            new MotionPlan(EPlayerClass.Swordsman, false, 1, "KS02 돌진 베기", null, new[] { "CUT/SwordDash" }),
            new MotionPlan(EPlayerClass.Swordsman, false, 2, "KS03 집중 베기", null, new[] { "CUT/SwordFocus" }),
            new MotionPlan(EPlayerClass.Swordsman, false, 3, "KS04 연환참", null,
                new[] { "CUT/SwordChain1", "CUT/SwordChain2" }),

            new MotionPlan(EPlayerClass.Mageknight, true, 0, "기본 1", null, new[] { "CUT/MageAttack1" }),
            new MotionPlan(EPlayerClass.Mageknight, true, 1, "기본 2", null, new[] { "CUT/MageAttack2" }),
            new MotionPlan(EPlayerClass.Mageknight, true, 2, "기본 3", null, new[] { "CUT/MageAttack3" }),
            new MotionPlan(EPlayerClass.Mageknight, false, 0, "Q 수류 방벽", null, new[] { "CUT/MageShield" }),
            new MotionPlan(EPlayerClass.Mageknight, false, 1, "W 검파 해방", null, new[] { "CUT/MageWave" }),
            new MotionPlan(EPlayerClass.Mageknight, false, 2, "E 공간 절단진", null, new[] { "CUT/MageField" }),
            new MotionPlan(EPlayerClass.Mageknight, false, 3, "R 섬광 일섬", null, new[] { "CUT/MageDash" }),

            new MotionPlan(EPlayerClass.Archer, true, 0, "기본 1", null, new[] { "AR/3__3Combo/Inplace/Archer@Attack_3Combo_1.FBX" }),
            new MotionPlan(EPlayerClass.Archer, true, 1, "기본 2", null, new[] { "AR/3__3Combo/Inplace/Archer@Attack_3Combo_2.FBX" }),
            new MotionPlan(EPlayerClass.Archer, true, 2, "기본 3 공중 사격", null, new[] { "AR/3__3Combo/Inplace/Archer@Attack_3Combo_3.FBX" }),
            // 속성 부여 Q/W/E는 시위에 화살을 거는 짧은 동작, R 속성 폭발은 차지 시작 전체 + 발사다
            new MotionPlan(EPlayerClass.Archer, false, 0, "Q 얼음 부여", null, new[] { "CUT/ArcherInfuse" }),
            new MotionPlan(EPlayerClass.Archer, false, 1, "W 바람 부여", null, new[] { "CUT/ArcherInfuse" }),
            new MotionPlan(EPlayerClass.Archer, false, 2, "E 불 부여", null, new[] { "CUT/ArcherInfuse" }),
            new MotionPlan(EPlayerClass.Archer, false, 3, "R 속성 폭발", null,
                new[] { "AR/4__ChargeShot/Inplace/Archer@Attack_ChargeShot_1_Start.FBX", "AR/4__ChargeShot/Inplace/Archer@Attack_ChargeShot_3_Shoot.FBX" }) };

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
            string path = CLIP_FOLDER + "/" + CLIP_PREFIX + Path.GetFileName(sourceKey) + ".anim";
            return CopyClipAt(SOURCE_CLIP_ROOT + "/" + sourceKey + ".fbx", path, loop);
        }

        // FBX 안의 첫 실제 클립, 미리보기 클립은 건너뛴다
        private static AnimationClip FindSourceClip(string fbxPath)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            for (int i = 0; i < assets.Length; ++i)
            {
                if ((assets[i] is AnimationClip candidate) && (!candidate.name.StartsWith(PREVIEW_PREFIX)))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static AnimationClip CopyClipAt(string fbxPath, string path, bool loop)
        {
            AnimationClip source = FindSourceClip(fbxPath);
            if (source == null)
            {
                return null;
            }

            var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
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
            return BuildControllerAt(CONTROLLER_PATH, clips, mask, null, false);
        }

        // extras는 신규 직업 행동 상태, 분절이 여럿이면 같은 속도로 이어 재생하는 상태 사슬이다.
        // keepAsset이 참이면 기존 컨트롤러 에셋을 지우지 않고 내용만 비워 다시 채워 GUID와 외부 참조를 보존한다
        private static AnimatorController BuildControllerAt(string controllerPath, Dictionary<string, AnimationClip> clips, AvatarMask mask,
            List<ExtraAction> extras, bool keepAsset)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if ((controller != null) && (keepAsset))
            {
                ClearControllerContents(controller);
            }
            else
            {
                if (controller != null)
                {
                    AssetDatabase.DeleteAsset(controllerPath);
                }

                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            }

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

            if (extras != null)
            {
                for (int i = 0; i < extras.Count; ++i)
                {
                    AddActionChain(machine, locomotion, extras[i]);
                }
            }

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

        // 컨트롤러 에셋 파일은 남기고 파라미터, 레이어, 상태, 전이, 하위 오브젝트를 모두 비운다. 첫 레이어와 그 루트 상태 머신은 유지한다
        private static void ClearControllerContents(AnimatorController controller)
        {
            for (int i = controller.layers.Length - 1; i > 0; --i)
            {
                controller.RemoveLayer(i);
            }

            for (int i = controller.parameters.Length - 1; i >= 0; --i)
            {
                controller.RemoveParameter(i);
            }

            AnimatorStateMachine root = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in root.states)
            {
                root.RemoveState(child.state);
            }

            foreach (ChildAnimatorStateMachine child in root.stateMachines)
            {
                root.RemoveStateMachine(child.stateMachine);
            }

            foreach (AnimatorStateTransition transition in root.anyStateTransitions)
            {
                root.RemoveAnyStateTransition(transition);
            }

            foreach (AnimatorTransition transition in root.entryTransitions)
            {
                root.RemoveEntryTransition(transition);
            }

            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller));
            for (int i = 0; i < subAssets.Length; ++i)
            {
                UnityEngine.Object sub = subAssets[i];
                if ((sub == null) || (sub == controller) || (sub == root))
                {
                    continue;
                }

                if ((sub is AnimatorStateMachine) || (sub is AnimatorState) || (sub is AnimatorTransitionBase) || (sub is BlendTree)
                    || (sub is StateMachineBehaviour))
                {
                    AssetDatabase.RemoveObjectFromAsset(sub);
                    UnityEngine.Object.DestroyImmediate(sub, true);
                }
            }
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

        /****************************************
        *       Class Assets (Swordsman, Mageknight, Archer)
        ****************************************/

        [MenuItem(CLASS_MENU_PATH)]
        private static void BuildClassMenu()
        {
            Debug.Log(BuildClassAssets(out _));
        }

        [MenuItem(CLASS_VERIFY_MENU_PATH)]
        private static void VerifyClassMenu()
        {
            Debug.Log(VerifyClassAssetsRerun());
        }

        /// <summary> 신규 직업 생성을 한 번 더 실행해 컨트롤러, 프로필, 카탈로그의 GUID와 카탈로그의 컨트롤러 참조, 컨트롤러 하위 오브젝트 수,
        /// 공유 마스크 파일이 그대로인지 확인한다. 처음 생성이 안 되어 있으면 먼저 한 번 생성한다. 결과는 줄 단위 문자열이다 </summary>
        public static string VerifyClassAssetsRerun()
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(CLASS_CONTROLLER_PATH) == null)
            {
                string first = BuildClassAssets(out _);
                if (AssetDatabase.LoadAssetAtPath<AnimatorController>(CLASS_CONTROLLER_PATH) == null)
                {
                    return "[P09 검증] 최초 생성에 실패해 확인하지 못했다: " + first;
                }
            }

            string[] paths = { CLASS_CONTROLLER_PATH, CLASS_PROFILE_PATH, CLASS_CATALOG_PATH };
            var before = new string[paths.Length];
            for (int i = 0; i < paths.Length; ++i)
            {
                before[i] = AssetDatabase.AssetPathToGUID(paths[i]);
            }

            string  catalogRefBefore = ReadCatalogControllerGuid();
            int     subCountBefore   = AssetDatabase.LoadAllAssetsAtPath(CLASS_CONTROLLER_PATH).Length;
            DateTime maskTimeBefore  = File.GetLastWriteTimeUtc(MASK_PATH);

            string rerun = BuildClassAssets(out _);

            var lines = new List<string>();
            bool isOk = true;
            for (int i = 0; i < paths.Length; ++i)
            {
                string after = AssetDatabase.AssetPathToGUID(paths[i]);
                bool   same  = (!string.IsNullOrEmpty(after)) && (after == before[i]);
                isOk &= same;
                lines.Add($"{(same ? "OK  " : "FAIL")} GUID {Path.GetFileName(paths[i])}: {before[i]} -> {after}");
            }

            string catalogRefAfter = ReadCatalogControllerGuid();
            bool   refSame         = (!string.IsNullOrEmpty(catalogRefAfter)) && (catalogRefAfter == catalogRefBefore) && (catalogRefAfter == before[0]);
            isOk &= refSame;
            lines.Add($"{(refSame ? "OK  " : "FAIL")} 카탈로그 _controller 참조 GUID: {catalogRefBefore} -> {catalogRefAfter} (컨트롤러 {before[0]})");

            int  subCountAfter = AssetDatabase.LoadAllAssetsAtPath(CLASS_CONTROLLER_PATH).Length;
            bool subSame       = subCountAfter == subCountBefore;
            isOk &= subSame;
            lines.Add($"{(subSame ? "OK  " : "FAIL")} 컨트롤러 하위 오브젝트 수(고아 누적 없음): {subCountBefore} -> {subCountAfter}");

            bool maskSame = File.GetLastWriteTimeUtc(MASK_PATH) == maskTimeBefore;
            isOk &= maskSame;
            lines.Add($"{(maskSame ? "OK  " : "FAIL")} 공유 마스크 파일 수정 시각 변화 없음");

            lines.Insert(0, $"[P09 검증] 신규 직업 생성 재실행 {(isOk ? "통과" : "실패")}. 재실행 결과: {rerun}");
            return string.Join("\n", lines);
        }

        private static string ReadCatalogControllerGuid()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlayerCharacterLookCatalogSO>(CLASS_CATALOG_PATH);
            if (catalog == null)
            {
                return string.Empty;
            }

            UnityEngine.Object controller = new SerializedObject(catalog).FindProperty("_controller").objectReferenceValue;
            return (controller == null) ? string.Empty : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(controller));
        }

        /// <summary> 요약 한 줄과 후보별 상세 측정 보고서(마크다운)를 이어 돌려준다. 보고서는 파일로 만들지 않으니 호출한 쪽이 저장한다 </summary>
        public static string BuildClassAssetsWithReport()
        {
            string summary = BuildClassAssets(out string detailReport);
            return (detailReport.Length == 0) ? summary : summary + "\n\n" + detailReport;
        }

        /// <summary> 검사, 마검사, 궁수용 컨트롤러, 프로필, 카탈로그를 기존 산출물과 별도 경로에 만든다.
        /// 먼저 Build Assets가 끝나 있어야 한다. 공유 마스크는 읽기만 하고 만든 에셋만 저장한다.
        /// 시간 예산을 통과하지 못한 모션은 배정하지 않으며, 그 이유를 포함한 상세 보고서는 detailReport로 돌려준다 </summary>
        public static string BuildClassAssets(out string detailReport)
        {
            detailReport = string.Empty;

            var baseProfile  = AssetDatabase.LoadAssetAtPath<PlayerCombatAnimationProfileSO>(PROFILE_PATH);
            var baseCatalog  = AssetDatabase.LoadAssetAtPath<PlayerCharacterLookCatalogSO>(CATALOG_PATH);
            var skillCatalog = AssetDatabase.LoadAssetAtPath<SkillCatalogSO>(SKILL_CATALOG);
            if ((baseProfile == null) || (baseCatalog == null) || (skillCatalog == null))
            {
                return "[P09] 신규 직업 생성 전에 Build Assets를 먼저 실행하고 스킬 카탈로그를 확인해야 한다";
            }

            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MASK_PATH);
            if (mask == null)
            {
                return "[P09] 표정 마스크가 없다. Build Assets를 먼저 실행한다: " + MASK_PATH;
            }

            var baseClips = new Dictionary<string, AnimationClip>();
            string loadError = LoadBaseClips(baseClips);
            if (loadError != null)
            {
                return loadError;
            }

            EnsureFolder(CLASS_CLIP_FOLDER);
            ReleaseMotionClips.Build();

            var results = new List<PlanResult>();
            for (int i = 0; i < G_ClassPlans.Length; ++i)
            {
                results.Add(EvaluatePlan(G_ClassPlans[i], skillCatalog));
            }

            ApplyAttackPrefixRule(results);

            // 슈퍼셋 컨트롤러, 기본 상태 뒤에 신규 행동 상태를 순서대로 붙인다
            var extras         = new List<ExtraAction>();
            var attackLengths  = new List<float>(LengthsOf(baseClips, G_AttackClips));
            var skillLengths   = new List<float>(LengthsOf(baseClips, G_SkillClips));
            var skills         = new List<PlayerCombatAnimationProfileSO.SkillAnimation>(baseProfile.EditorSkills);
            var attacks        = new List<PlayerCombatAnimationProfileSO.AttackAnimation>();
            var attackSegments = new List<PlayerCombatAnimationProfileSO.SegmentSet>();
            var skillSegments  = new List<PlayerCombatAnimationProfileSO.SegmentSet>();
            var generated      = new List<UnityEngine.Object>();

            for (int i = 0; i < results.Count; ++i)
            {
                PlanResult result = results[i];
                if (!result.IsAssigned)
                {
                    continue;
                }

                MotionPlan plan = result.Plan;
                if (!result.IsReused)
                {
                    int next  = plan.IsAttack ? attackLengths.Count : skillLengths.Count;
                    int limit = plan.IsAttack ? PlayerCombatAnimationProfileSO.MAX_ATTACK_ACTIONS : PlayerCombatAnimationProfileSO.MAX_SKILL_ACTIONS;
                    if (next >= limit)
                    {
                        Unassign(result, "배정 보류: 컨트롤러 행동 상태 한도 " + limit + "개를 넘는다");
                        continue;
                    }

                    AnimationClip[] clips = MaterializeClips(result.ChosenKeys);
                    if (clips == null)
                    {
                        Unassign(result, "배정 보류: 클립 복사에 실패했다");
                        continue;
                    }

                    for (int s = 0; s < clips.Length; ++s)
                    {
                        if (AssetDatabase.GetAssetPath(clips[s]).StartsWith(CLASS_CLIP_FOLDER + "/", StringComparison.Ordinal))
                        {
                            generated.Add(clips[s]);
                        }
                    }

                    result.ActionIndex = next;
                    extras.Add(new ExtraAction(((plan.IsAttack) ? "Attack" : "Skill") + next, plan.IsAttack ? PARAM_ATTACK : PARAM_SKILL, next, clips));
                    if (plan.IsAttack)
                    {
                        attackLengths.Add(result.Length);
                    }
                    else
                    {
                        skillLengths.Add(result.Length);
                    }

                    if (clips.Length > 1)
                    {
                        float[] lengths = new float[clips.Length];
                        for (int s = 0; s < clips.Length; ++s)
                        {
                            lengths[s] = clips[s].length;
                        }

                        (plan.IsAttack ? attackSegments : skillSegments).Add(new PlayerCombatAnimationProfileSO.SegmentSet(next, lengths));
                    }
                }

                if (plan.IsAttack)
                {
                    attacks.Add(new PlayerCombatAnimationProfileSO.AttackAnimation(plan.PlayerClass, plan.Slot, result.ActionIndex));
                }
                else
                {
                    skills.Add(new PlayerCombatAnimationProfileSO.SkillAnimation(plan.PlayerClass, ESkillRequestKind.Regular, plan.Slot, result.ActionIndex));
                }
            }

            AnimatorController controller = BuildControllerAt(CLASS_CONTROLLER_PATH, baseClips, mask, extras, true);

            var intervals = new PlayerCombatAnimationProfileSO.ClassAttackInterval[G_ClassOrder.Length];
            for (int i = 0; i < G_ClassOrder.Length; ++i)
            {
                intervals[i] = new PlayerCombatAnimationProfileSO.ClassAttackInterval(G_ClassOrder[i], G_ClassAttackIntervals[i], G_ClassComboResets[i]);
            }

            var profile = AssetDatabase.LoadAssetAtPath<PlayerCombatAnimationProfileSO>(CLASS_PROFILE_PATH);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PlayerCombatAnimationProfileSO>();
                AssetDatabase.CreateAsset(profile, CLASS_PROFILE_PATH);
            }

            EditorUtility.CopySerialized(baseProfile, profile);
            profile.name = Path.GetFileNameWithoutExtension(CLASS_PROFILE_PATH);
            profile.EditorSetup(skills.ToArray(), attackLengths.ToArray(), skillLengths.ToArray(), LengthsOf(baseClips, G_HitClips),
                baseClips[DODGE_CLIP].length);
            profile.EditorSetupClasses(attacks.ToArray(), intervals, attackSegments.ToArray(), skillSegments.ToArray());
            EditorUtility.SetDirty(profile);

            string catalogSummary = BuildClassCatalog(baseCatalog, controller);

            generated.Add(controller);
            generated.Add(profile);
            generated.Add(AssetDatabase.LoadAssetAtPath<PlayerCharacterLookCatalogSO>(CLASS_CATALOG_PATH));
            for (int i = 0; i < generated.Count; ++i)
            {
                AssetDatabase.SaveAssetIfDirty(generated[i]);
            }

            detailReport = BuildClassReport(results);

            int assigned = 0;
            for (int i = 0; i < results.Count; ++i)
            {
                if (results[i].IsAssigned)
                {
                    ++assigned;
                }
            }

            return $"[P09] 신규 직업 모션 {results.Count}개 중 임시 배정 {assigned}개, 보류 {results.Count - assigned}개. "
                + $"컨트롤러 {CLASS_CONTROLLER_PATH}, 프로필 {CLASS_PROFILE_PATH}, {catalogSummary}. 후보별 상세 측정은 반환된 보고서 문자열에 있다";
        }

        // 신규 직업이 쓰는 기본 클립은 모두 Build Assets가 만든 사본이다
        private static string LoadBaseClips(Dictionary<string, AnimationClip> clips)
        {
            var keys = new List<string>();
            keys.AddRange(G_LocomotionClips);
            keys.AddRange(G_AttackClips);
            keys.AddRange(G_SkillClips);
            keys.AddRange(G_HitClips);
            keys.Add(DODGE_CLIP);
            keys.Add(DIE_CLIP);

            for (int i = 0; i < keys.Count; ++i)
            {
                if (clips.ContainsKey(keys[i]))
                {
                    continue;
                }

                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CLIP_FOLDER + "/" + CLIP_PREFIX + Path.GetFileName(keys[i]) + ".anim");
                if (clip == null)
                {
                    clips.Clear();
                    return "[P09] 기본 클립 사본이 없다. Build Assets를 먼저 실행한다: " + keys[i];
                }

                clips.Add(keys[i], clip);
            }

            return null;
        }

        // 후보를 순서대로 시간 예산에 시험한다. 문서 보류, 목표 시간 없음, 모든 후보 초과는 배정하지 않는다. 속도는 억지로 맞추지 않는다
        private static PlanResult EvaluatePlan(MotionPlan plan, SkillCatalogSO skillCatalog)
        {
            var result = new PlanResult(plan);
            bool isHeld = plan.HoldReason != null;
            result.Verdict = isHeld ? plan.HoldReason : "배정 보류: 시간 예산을 통과한 후보가 없다";

            float target;
            bool  hasTarget;
            if (plan.IsAttack)
            {
                target    = ((plan.PlayerClass == EPlayerClass.Archer) && (plan.Slot < G_ArcherStepIntervals.Length))
                    ? G_ArcherStepIntervals[plan.Slot]
                    : G_ClassAttackIntervals[Array.IndexOf(G_ClassOrder, plan.PlayerClass)];
                hasTarget = true;
            }
            else
            {
                hasTarget = TryGetSkillSeconds(skillCatalog, plan.PlayerClass, ESkillRequestKind.Regular, plan.Slot, out target);
            }

            if (!hasTarget)
            {
                result.Verdict = isHeld ? plan.HoldReason : "배정 보류: 스킬 카탈로그에 이 기술의 목표 시간이 없다";
                return result;
            }

            result.Target = target;
            for (int c = 0; c < plan.Candidates.Length; ++c)
            {
                string[] keys  = plan.Candidates[c];
                string   label = DescribeKeys(keys);
                if (!TryMeasureKeys(keys, out float length, out float residue, out string error))
                {
                    result.Details.Add(label + ": " + error);
                    continue;
                }

                float speed = CombatAnimationTriggerRules.RequiredSpeed(length, target);
                result.Details.Add($"{label}: 길이 {length:F2}초, 필요 속도 {speed:F2}배, 루트 이동 추정 {residue:F2}m");
                if (isHeld)
                {
                    continue;
                }

                if (!CombatAnimationTriggerRules.IsWithinSpeedBudget(length, target, MIN_SPEED, MAX_ASSIGN_SPEED))
                {
                    continue;
                }

                result.IsAssigned   = true;
                result.ChosenKeys   = keys;
                result.ChosenLabel  = label;
                result.Length       = length;
                result.Speed        = speed;
                result.RootResidue  = residue;
                result.Verdict      = "임시 배정(실제 재생 검토 대기)";
                result.ActionIndex  = FindReusedIndex(plan, keys);
                result.IsReused     = result.ActionIndex >= 0;
                return result;
            }

            return result;
        }

        // 기본 공격 콤보는 앞 단계가 보류면 뒤 단계를 배정하지 않는다
        private static void ApplyAttackPrefixRule(List<PlanResult> results)
        {
            for (int c = 0; c < G_ClassOrder.Length; ++c)
            {
                bool isBroken = false;
                for (int i = 0; i < results.Count; ++i)
                {
                    PlanResult result = results[i];
                    if ((!result.Plan.IsAttack) || (result.Plan.PlayerClass != G_ClassOrder[c]))
                    {
                        continue;
                    }

                    if ((isBroken) && (result.IsAssigned))
                    {
                        Unassign(result, "배정 보류: 앞 단계가 보류라 콤보를 이을 수 없다");
                    }

                    if (!result.IsAssigned)
                    {
                        isBroken = true;
                    }
                }
            }
        }

        private static void Unassign(PlanResult result, string verdict)
        {
            result.IsAssigned = false;
            result.IsReused   = false;
            result.Verdict    = verdict;
        }

        // 기존 컨트롤러 상태와 같은 단일 CG 클립이면 새 상태를 만들지 않고 그 인덱스를 쓴다. 없으면 -1
        private static int FindReusedIndex(MotionPlan plan, string[] keys)
        {
            if ((keys.Length != 1) || (!keys[0].StartsWith(KEY_BASE, StringComparison.Ordinal)))
            {
                return -1;
            }

            return Array.IndexOf(plan.IsAttack ? G_AttackClips : G_SkillClips, keys[0].Substring(KEY_BASE.Length));
        }

        private static string DescribeKeys(string[] keys)
        {
            var names = new string[keys.Length];
            for (int i = 0; i < keys.Length; ++i)
            {
                names[i] = Path.GetFileNameWithoutExtension(keys[i]);
            }

            return string.Join(" + ", names);
        }

        // 키를 원본 FBX 경로와 사본 경로로 푼다. CG 키는 기존 사본이라 FBX 경로가 없다
        private static bool TryResolveKey(string key, out string fbxPath, out string clipPath)
        {
            fbxPath  = null;
            clipPath = null;
            if (key.StartsWith("CUT/", StringComparison.Ordinal))
            {
                clipPath = CLASS_CLIP_FOLDER + "/AN_Release_" + key.Substring(4) + ".anim";
                return true;
            }
            if (key.StartsWith(KEY_BASE, StringComparison.Ordinal))
            {
                clipPath = CLIP_FOLDER + "/" + CLIP_PREFIX + Path.GetFileName(key.Substring(KEY_BASE.Length)) + ".anim";
                return true;
            }

            if (key.StartsWith(KEY_MK, StringComparison.Ordinal))
            {
                string relative = key.Substring(KEY_MK.Length);
                fbxPath  = MK_CLIP_ROOT + "/" + relative;
                clipPath = CLASS_CLIP_FOLDER + "/" + CLIP_PREFIX + "MK_" + Path.GetFileNameWithoutExtension(relative) + ".anim";
                return true;
            }

            if (key.StartsWith(KEY_ARCHER, StringComparison.Ordinal))
            {
                string relative = key.Substring(KEY_ARCHER.Length);
                fbxPath  = ARCHER_CLIP_ROOT + "/" + relative;
                clipPath = CLASS_CLIP_FOLDER + "/" + CLIP_PREFIX + "AR_" + Path.GetFileNameWithoutExtension(relative).Replace('@', '_') + ".anim";
                return true;
            }

            return false;
        }

        // 길이 시험용 클립, 사본을 만들지 않고 원본 FBX를 직접 읽는다
        private static AnimationClip LoadMeasuredClip(string key)
        {
            if (!TryResolveKey(key, out string fbxPath, out string clipPath))
            {
                return null;
            }

            return (fbxPath == null) ? AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath)
                : FindSourceClip(fbxPath) ?? AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        }

        // 합계 길이와 루트 XZ 이동 추정(평균 속도 x 길이)을 잰다. 루트 이동은 보고용이며 판정에 쓰지 않는다
        private static bool TryMeasureKeys(string[] keys, out float length, out float residue, out string error)
        {
            length  = 0f;
            residue = 0f;
            error   = null;
            for (int i = 0; i < keys.Length; ++i)
            {
                AnimationClip clip = LoadMeasuredClip(keys[i]);
                if (clip == null)
                {
                    error = "클립을 찾지 못했다: " + keys[i];
                    return false;
                }

                length  += clip.length;
                residue += new Vector2(clip.averageSpeed.x, clip.averageSpeed.z).magnitude * clip.length;
            }

            return true;
        }

        // 통과한 후보만 ClassClips로 복사한다. 하나라도 못 만들면 null
        private static AnimationClip[] MaterializeClips(string[] keys)
        {
            var clips = new AnimationClip[keys.Length];
            for (int i = 0; i < keys.Length; ++i)
            {
                if (!TryResolveKey(keys[i], out string fbxPath, out string clipPath))
                {
                    return null;
                }

                clips[i] = (fbxPath == null) ? AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath)
                    : CopyClipAt(fbxPath, clipPath, false) ?? AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clips[i] == null)
                {
                    return null;
                }
            }

            return clips;
        }

        // 분절이 하나면 일반 행동 상태, 여럿이면 같은 속도로 이어지는 상태 사슬이다.
        // 앞 분절은 끝(1.0)에서 바로 다음으로 넘기고 마지막 분절만 Locomotion으로 나간다
        private static void AddActionChain(AnimatorStateMachine machine, AnimatorState locomotion, ExtraAction action)
        {
            if (action.Clips.Length == 1)
            {
                AddAction(machine, locomotion, action.Name, action.Clips[0], action.Trigger, action.Index);
                return;
            }

            AnimatorState previous = null;
            for (int s = 0; s < action.Clips.Length; ++s)
            {
                AnimatorState state = machine.AddState((s == 0) ? action.Name : (action.Name + "_" + s));
                state.motion               = action.Clips[s];
                state.speedParameter       = PARAM_ACTION_SPEED;
                state.speedParameterActive = true;

                if (s == 0)
                {
                    AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
                    enter.AddCondition(AnimatorConditionMode.If, 0f, action.Trigger);
                    enter.AddCondition(AnimatorConditionMode.If, 0f, PARAM_ALIVE);
                    enter.AddCondition(AnimatorConditionMode.Equals, action.Index, PARAM_ACTION_INDEX);
                    enter.duration            = ACTION_BLEND;
                    enter.canTransitionToSelf = true;
                    enter.hasExitTime         = false;
                }
                else
                {
                    AnimatorStateTransition chain = previous.AddTransition(state);
                    chain.hasExitTime = true;
                    chain.exitTime    = 1f;
                    chain.duration    = 0f;
                }

                previous = state;
            }

            AnimatorStateTransition exit = previous.AddTransition(locomotion);
            exit.hasExitTime = true;
            exit.exitTime    = ACTION_EXIT_TIME;
            exit.duration    = ACTION_EXIT_BLEND;
        }

        // 외형 카탈로그 사본, 컨트롤러만 신규 슈퍼셋으로 바꾼다
        private static string BuildClassCatalog(PlayerCharacterLookCatalogSO baseCatalog, RuntimeAnimatorController controller)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PlayerCharacterLookCatalogSO>(CLASS_CATALOG_PATH);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<PlayerCharacterLookCatalogSO>();
                AssetDatabase.CreateAsset(catalog, CLASS_CATALOG_PATH);
            }

            EditorUtility.CopySerialized(baseCatalog, catalog);
            catalog.name = Path.GetFileNameWithoutExtension(CLASS_CATALOG_PATH);
            var so = new SerializedObject(catalog);
            so.FindProperty("_controller").objectReferenceValue = controller;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);

            var errors = new List<string>();
            catalog.Validate(errors);
            return (errors.Count == 0) ? "카탈로그 " + CLASS_CATALOG_PATH : "카탈로그 오류: " + string.Join(", ", errors);
        }

        private static string BuildClassReport(List<PlanResult> results)
        {
            var report = new StringBuilder();
            report.AppendLine("# AC_PlayerP09_Classes 신규 직업 모션 배정");
            report.AppendLine();
            report.AppendLine("`Assets/Editor/Art/PlayerP09AssetBuilder.cs`의 `" + CLASS_MENU_PATH + "`가 만든다.");
            report.AppendLine();
            report.AppendLine("- 필요 속도 = 후보 합계 길이 / 목표 시간. 목표는 기본 공격이면 직업별 시각 간격, 기술이면 스킬 카탈로그의 Windup + Active + Recovery다.");
            report.AppendLine("- 필요 속도가 " + MIN_SPEED.ToString("F1") + " ~ " + MAX_ASSIGN_SPEED.ToString("F1") + "배 밖이면 속도를 억지로 맞추거나 다른 클립으로 바꾸지 않고 배정하지 않는다. 배정이 없으면 재생도 없다.");
            report.AppendLine("- 통과해도 정적 길이 기준의 임시 배정이다. 실제 재생, 리타겟, 발 미끄럼, 무기 쥠 검수는 하지 않았다.");
            report.AppendLine("- 기본 공격 콤보는 앞 단계가 보류면 뒤 단계도 배정하지 않는다.");
            report.AppendLine("- 이동, 회피, 피격, 사망은 기존 CG 모션을 공유한다. 구 직업(무사, 도술사, 신령사) 배정은 그대로다.");
            report.AppendLine("- 루트 이동 추정은 평균 속도 x 길이다. 제자리 재생이라 버려지는 이동량이며 판정에 쓰지 않는다.");
            report.AppendLine();
            report.AppendLine("| 직업 | 종류 | 슬롯 | 이름 | 목표(초) | 선택 후보 | 길이(초) | 필요 속도 | 행동 인덱스 | 루트 이동(m) | 판정 |");
            report.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            for (int i = 0; i < results.Count; ++i)
            {
                PlanResult r = results[i];
                report.Append("| ").Append(r.Plan.PlayerClass).Append(" | ").Append(r.Plan.IsAttack ? "기본 공격" : "기술").Append(" | ")
                    .Append(r.Plan.Slot).Append(" | ").Append(r.Plan.Label).Append(" | ")
                    .Append((r.Target > 0f) ? r.Target.ToString("F2") : "-").Append(" | ")
                    .Append(r.IsAssigned ? r.ChosenLabel : "-").Append(" | ")
                    .Append(r.IsAssigned ? r.Length.ToString("F2") : "-").Append(" | ")
                    .Append(r.IsAssigned ? r.Speed.ToString("F2") : "-").Append(" | ")
                    .Append(r.IsAssigned ? r.ActionIndex.ToString() : "-").Append(" | ")
                    .Append(r.IsAssigned ? r.RootResidue.ToString("F2") : "-").Append(" | ")
                    .Append(r.Verdict).AppendLine(" |");
            }

            report.AppendLine();
            report.AppendLine("## 후보 측정");
            report.AppendLine();
            for (int i = 0; i < results.Count; ++i)
            {
                PlanResult r = results[i];
                report.Append("- ").Append(r.Plan.PlayerClass).Append(' ').Append(r.Plan.IsAttack ? "기본" : "기술").Append(' ')
                    .Append(r.Plan.Slot).Append(' ').AppendLine(r.Plan.Label);
                for (int d = 0; d < r.Details.Count; ++d)
                {
                    report.Append("  - ").AppendLine(r.Details[d]);
                }
            }

            return report.ToString();
        }

        /****************************************
        *            Class Asset Types
        ****************************************/

        /// <summary> 한 기술이나 기본 공격 단계에 시험할 후보 목록, 후보 하나는 이어 재생할 클립 키 순서다 </summary>
        private sealed class MotionPlan
        {
            public readonly EPlayerClass PlayerClass;
            public readonly bool         IsAttack;
            public readonly int          Slot;
            public readonly string       Label;
            public readonly string       HoldReason;
            public readonly string[][]   Candidates;

            public MotionPlan(EPlayerClass playerClass, bool isAttack, int slot, string label, string holdReason, params string[][] candidates)
            {
                PlayerClass = playerClass;
                IsAttack    = isAttack;
                Slot        = slot;
                Label       = label;
                HoldReason  = holdReason;
                Candidates  = candidates;
            }
        }

        private sealed class PlanResult
        {
            public readonly MotionPlan    Plan;
            public readonly List<string>  Details = new List<string>();
            public bool                   IsAssigned;
            public bool                   IsReused;
            public int                    ActionIndex = -1;
            public string[]               ChosenKeys;
            public string                 ChosenLabel;
            public string                 Verdict;
            public float                  Target;
            public float                  Length;
            public float                  Speed;
            public float                  RootResidue;

            public PlanResult(MotionPlan plan)
            {
                Plan = plan;
            }
        }

        private sealed class ExtraAction
        {
            public readonly string          Name;
            public readonly string          Trigger;
            public readonly int             Index;
            public readonly AnimationClip[] Clips;

            public ExtraAction(string name, string trigger, int index, AnimationClip[] clips)
            {
                Name    = name;
                Trigger = trigger;
                Index   = index;
                Clips   = clips;
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
