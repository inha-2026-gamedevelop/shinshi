#if UNITY_EDITOR
// System
using System;
using System.Collections.Generic;
using System.Linq;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Dev;

namespace Shinshi.Editor
{
    public static class SkillShowcaseBuilder
    {
        private const string SCENE_PATH = "Assets/00.Scenes/Dev_VFX/Dev_SkillShowcase.unity";
        private const string SWORD = "Assets/99.ThirdParty/Animation/CombatGirlsCharacterPack/Sword_Girl/Animations/";
        private const string MAGE = "Assets/99.ThirdParty/Animation/Magical-Knight_Set/Animation/Humanoid/Inplace/";
        private const string ARCHER = "Assets/99.ThirdParty/Animation/Grruzam Archer Animation/Animation/Archer/2_Attack/";
        private const int SHOW_LAYER = 31;

        // 마검사 시연 효과 시각과 수명, 슬롯 순서는 기본 공격, Q 방벽, W 검파, E 장판, R 일섬이다
        private const float MAGE_CUE_TIME     = 0.5f;
        private const int   MAGE_SHIELD_INDEX = 1;
        private const int   MAGE_WAVE_INDEX   = 2;
        private const int   MAGE_FIELD_INDEX  = 3;
        private const float MAGE_SHIELD_LIFE  = 3f;
        private const float MAGE_WAVE_SPEED   = 20f;
        private const float MAGE_WAVE_RANGE   = 9f;
        private const float MAGE_WAVE_LIFE    = 0.7f;
        private const float MAGE_FIELD_LIFE   = 4f;

        [MenuItem("Shinshi/Art/Skill Showcase/Create Scene")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SCENE_PATH) != null)
            {
                throw new InvalidOperationException("시연 씬이 이미 있다. 기존 씬을 열어 사용한다: " + SCENE_PATH);
            }

            DevSkillShowcase.Entry[] entries = CreateEntries();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.4f, 0.5f);
            GameObject root = new GameObject("Skill Showcase");
            DevSkillShowcase showcase = root.AddComponent<DevSkillShowcase>();
            Camera camera = new GameObject("Showcase Camera").AddComponent<Camera>();
            camera.transform.SetParent(root.transform);
            camera.transform.position = new Vector3(7f, 5f, -7f);
            camera.transform.LookAt(new Vector3(0f, 0.9f, 1.5f));
            camera.orthographic = true;
            camera.orthographicSize = 3.3f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.07f);
            camera.cullingMask = 1 << SHOW_LAYER;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 60f;
            Light light = new GameObject("Key Light").AddComponent<Light>();
            light.transform.SetParent(root.transform);
            light.type = LightType.Directional;
            light.intensity = 1.8f;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            light.cullingMask = 1 << SHOW_LAYER;

            Material ground = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            ground.color = new Color(0.06f, 0.085f, 0.12f);
            AssetDatabase.CreateAsset(ground, "Assets/00.Scenes/Dev_VFX/M_SkillShowcaseGround.mat");
            Material weaponMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            weaponMaterial.color = new Color(0.65f, 0.72f, 0.8f);
            weaponMaterial.SetFloat("_Metallic", 0.6f);
            AssetDatabase.CreateAsset(weaponMaterial, "Assets/00.Scenes/Dev_VFX/M_SkillShowcaseWeapon.mat");
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plane.name = "Stage";
            plane.transform.SetParent(root.transform);
            plane.transform.position = new Vector3(0f, -0.13f, 3f);
            plane.transform.localScale = new Vector3(24f, 0.2f, 28f);
            plane.GetComponent<Renderer>().sharedMaterial = ground;
            UnityEngine.Object.DestroyImmediate(plane.GetComponent<Collider>());

            Animator[] actors = new Animator[3];
            GameObject source = Load<GameObject>("Assets/02.Prefab/Characters/PlayerP09/PF_PlayerP09.prefab");
            for (int i = 0; i < actors.Length; ++i)
            {
                GameObject actor = UnityEngine.Object.Instantiate(source, root.transform);
                actor.name = new[] { "Swordsman", "Mageknight", "Archer" }[i];
                actors[i] = actor.GetComponent<Animator>();
                actors[i].runtimeAnimatorController = null;
                actors[i].applyRootMotion = false;
                actors[i].fireEvents = false;
                actors[i].cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (Animator nested in actor.GetComponentsInChildren<Animator>(true))
                {
                    if (nested != actors[i]) { UnityEngine.Object.DestroyImmediate(nested); }
                }
                foreach (SkinnedMeshRenderer mesh in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    mesh.updateWhenOffscreen = true;
                }
                Transform weapon = actor.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Weapon");
                if (weapon != null) { weapon.gameObject.SetActive(i != 2); }
                if ((weapon != null) && (i != 2))
                {
                    foreach (Renderer mesh in weapon.GetComponentsInChildren<Renderer>(true))
                    {
                        mesh.gameObject.SetActive(mesh.transform.parent.name == "Sword");
                        mesh.sharedMaterial = weaponMaterial;
                    }
                    foreach (UnityEngine.Animations.ParentConstraint constraint in weapon.GetComponentsInChildren<UnityEngine.Animations.ParentConstraint>(true))
                    {
                        Transform holder = constraint.transform;
                        UnityEngine.Object.DestroyImmediate(constraint);
                        if (holder.name != "Sword") { continue; }
                        Transform socket = actor.GetComponentsInChildren<Transform>(true).First(t => t.name == "Weapon_Target_Hand_R");
                        holder.SetParent(socket, false);
                        holder.localPosition = Vector3.zero;
                        holder.localRotation = Quaternion.identity;
                    }
                }
                if (i == 2)
                {
                    GameObject bow = UnityEngine.Object.Instantiate(Load<GameObject>("Assets/99.ThirdParty/Animation/Grruzam Archer Animation/Prefebs/Unity_Grruzam_BaseModeling_Bow.prefab"), actors[i].GetBoneTransform(HumanBodyBones.LeftHand));
                    bow.transform.localPosition = Vector3.zero;
                    bow.transform.localRotation = Quaternion.identity;
                    GameObject bowReference = Load<GameObject>("Assets/99.ThirdParty/Animation/Grruzam Archer Animation/Prefebs/Unity_Grruzam_BaseModeling_Archer_Include_WP.prefab");
                    Transform referenceBow = bowReference.GetComponentsInChildren<Transform>(true).First(t => t.name == "Grruzam_Modeling_Bow");
                    Transform referenceHand = bowReference.GetComponentsInChildren<Transform>(true).First(t => t.name == "hand_l");
                    bow.transform.rotation = referenceBow.rotation;
                    bow.transform.position += referenceBow.position - referenceHand.position;
                    foreach (Renderer mesh in bow.GetComponentsInChildren<Renderer>(true)) { mesh.sharedMaterial = weaponMaterial; }
                }
                actor.SetActive(i == 0);
            }

            Canvas canvas = new GameObject("Labels", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
            canvas.transform.SetParent(root.transform);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            TMP_Text title = Label(canvas.transform, "Title", 44, new Vector2(40f, -35f));
            TMP_Text subtitle = Label(canvas.transform, "Note", 22, new Vector2(42f, -96f));
            TMP_Text phase = Label(canvas.transform, "Phase", 24, new Vector2(42f, -620f));
            Label(canvas.transform, "Footer", 18, new Vector2(42f, -662f)).text = "연출 검토용 · 원속도 후보 모션 · 서버 전투 동기화 검증 아님";
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) { child.gameObject.layer = SHOW_LAYER; }
            showcase.Configure(camera, actors, entries, title, subtitle, phase);
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            Selection.activeGameObject = root;
        }

        private static TMP_Text Label(Transform parent, string name, int size, Vector2 position)
        {
            TextMeshProUGUI text = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false);
            text.font = Load<TMP_FontAsset>("Assets/01.Scripts/06.UI/Hud/Fonts/HudQuestKorean-Regular.asset");
            text.fontSize = size;
            text.color = Color.white;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(1200f, 60f);
            return text;
        }

        private static DevSkillShowcase.Entry[] CreateEntries()
        {
            List<DevSkillShowcase.Entry> entries = new List<DevSkillShowcase.Entry>();
            entries.Add(Entry(0, "기본 공격", new[] { SWORD + "Normal/S_Attack_1.fbx" }, Cue(0, "Basic", 0.45f)));
            entries.Add(Entry(0, "Q · 넓게 베기", new[] { SWORD + "Special/S_Sp_Skill_1.fbx" }, Cue(0, "Wide", 1.3f)));
            DevSkillShowcase.Entry dash = Entry(0, "W · 돌진 베기", new[] { SWORD + "Special/S_QuickShift_F.fbx", SWORD + "Normal/S_Attack_3.fbx" }, Cue(0, "Dash", 0.3f));
            dash.MoveDistance = 3f; dash.MoveStart = 0.15f; dash.MoveDuration = 0.7f;
            entries.Add(dash);
            entries.Add(Entry(0, "E · 집중 베기", new[] { SWORD + "Special/S_Sp_Skill_3.fbx" }, Cue(0, "Focus", 1.8f)));
            entries.Add(Entry(0, "R · 연환참", new[] { SWORD + "Normal/S_Attack_2.fbx", SWORD + "Normal/S_Attack_3.fbx" }, Cue(0, "ChainFirst", 0.45f), Cue(0, "ChainSecond", 2.05f)));
            string[] mageClips = { "AttackSword01", "AttackSword04", "AttackEnergy03", "AttackSword05", "AttackEnergy06" };
            string[] mageTitles = { "기본 공격", "Q 수류 방벽", "W 검파 해방", "E 공간 절단진", "R 섬광 일섬" };
            string[] mageEffects = { "Basic", "Shield", "Wave", "Field", "Jump" };
            for (int i = 0; i < mageClips.Length; ++i)
            {
                DevSkillShowcase.Cue cue = Cue(1, mageEffects[i], MAGE_CUE_TIME);
                if (i == MAGE_SHIELD_INDEX)
                {
                    cue.Life = MAGE_SHIELD_LIFE;
                }

                if (i == MAGE_WAVE_INDEX)
                {
                    cue.Speed = MAGE_WAVE_SPEED;
                    cue.Range = MAGE_WAVE_RANGE;
                    cue.Life  = MAGE_WAVE_LIFE;
                }

                if (i == MAGE_FIELD_INDEX)
                {
                    cue.Life = MAGE_FIELD_LIFE;
                }

                entries.Add(Entry(1, mageTitles[i], new[] { MAGE + mageClips[i] + "_inplace.fbx" }, cue));
            }
            string shoot = ARCHER + "1__1Shoot/Inplace/Archer@Attack_1Shoot_";
            entries.Add(Entry(2, "기본 공격", new[] { shoot + "Start.FBX", shoot + "Loop.FBX", shoot + "End.FBX" }, Arrow(0.75f), Cue(2, "Muzzle", 0.75f)));
            string rapid = ARCHER + "5__RapidShot/Archer@Attack_RapidShot_";
            List<DevSkillShowcase.Cue> rapidCues = new List<DevSkillShowcase.Cue>();
            for (int i = 0; i < 3; ++i) { float t = 0.393f + (i * 0.333f); rapidCues.Add(Arrow(t)); rapidCues.Add(Cue(2, "Muzzle", t)); }
            entries.Add(Entry(2, "Q · 속사", new[] { rapid + "Start.FBX", rapid + "Shoot_Loop.FBX", rapid + "Shoot_Loop.FBX", rapid + "Shoot_Loop.FBX", rapid + "End.FBX" }, rapidCues.ToArray()));
            DevSkillShowcase.Entry dodge = Entry(2, "W · 후퇴사격", new[] { ARCHER + "9__Dodge_Shot/Archer@Attack_Dodge_Shot.FBX" }, Arrow(0.45f), Cue(2, "Muzzle", 0.45f));
            dodge.MoveDistance = -3f; dodge.MoveStart = 0.45f; dodge.MoveDuration = 0.5f;
            entries.Add(dodge);
            string charge = ARCHER + "4__ChargeShot/Inplace/Archer@Attack_ChargeShot_";
            DevSkillShowcase.Cue wind = Cue(2, "Wind", 1.65f); wind.Speed = 28f; wind.Range = 14f; wind.Life = 0.7f;
            entries.Add(Entry(2, "E · 관통시위", new[] { charge + "1_Start.FBX", charge + "2_A_Charging_Loop.FBX", charge + "3_Shoot.FBX", charge + "4_End.FBX" }, wind, Cue(2, "WindMuzzle", 1.65f)));
            List<DevSkillShowcase.Cue> fan = new List<DevSkillShowcase.Cue> { Cue(2, "Muzzle", 0.55f) };
            for (int i = 0; i < 5; ++i) { DevSkillShowcase.Cue arrow = Arrow(0.55f); arrow.Angle = -20f + (i * 10f); fan.Add(arrow); }
            entries.Add(Entry(2, "R · 산개사격", new[] { shoot + "ALL.FBX" }, fan.ToArray()));
            return entries.ToArray();
        }

        private static DevSkillShowcase.Entry Entry(int actor, string title, string[] clips, params DevSkillShowcase.Cue[] cues)
        {
            return new DevSkillShowcase.Entry { ActorIndex = actor, Title = new[] { "검사", "마검사", "궁수" }[actor] + "  /  " + title, Note = "실제 보유 애니메이션 + 게임용 VFX · 1× 재생 · 시연용 타이밍", Clips = clips.Select(Clip).ToArray(), Cues = cues };
        }

        private static DevSkillShowcase.Cue Arrow(float time)
        {
            DevSkillShowcase.Cue cue = Cue(2, "Arrow", time); cue.Speed = 24f; cue.Range = 10f; cue.Life = 0.62f; return cue;
        }

        private static DevSkillShowcase.Cue Cue(int actor, string effect, float time)
        {
            string[] folders = { "SwordsmanMagicSlash/PF_SwordsmanMagicSlash_", "Mageknight/PF_MageknightVfx_", "Archer/PF_ArcherVfx_" };
            return new DevSkillShowcase.Cue { Prefab = Load<GameObject>("Assets/02.Prefab/VFX/" + folders[actor] + effect + ".prefab"), Time = time, Life = 1f };
        }

        private static AnimationClip Clip(string path)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (clip == null) { throw new InvalidOperationException("클립 없음: " + path); }
            return clip;
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) { throw new InvalidOperationException("에셋 없음: " + path); }
            return asset;
        }
    }
}
#endif
