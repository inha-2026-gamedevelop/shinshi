// System
using System.Collections;

// Unity
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;
using Shinshi.Combat;
using Shinshi.Contracts;

namespace Shinshi.Tests
{
    public sealed class ReleaseCharacterAssetTests
    {
        private const string PROFILE = "Assets/09.Data/Avatar/PlayerCombatAnimationProfile_Classes.asset";
        private const string CATALOG = "Assets/09.Data/Avatar/PlayerCharacterLookCatalog_Classes.asset";

        [TestCase(EPlayerClass.Swordsman, 3)]
        [TestCase(EPlayerClass.Mageknight, 3)]
        [TestCase(EPlayerClass.Archer, 3)]
        public void ClassProfile_AllActions_HaveAssignedHumanoidClips(EPlayerClass playerClass, int steps)
        {
            var profile = AssetDatabase.LoadAssetAtPath<PlayerCombatAnimationProfileSO>(PROFILE);
            Assert.That(profile.GetAttackStepCount(playerClass), Is.EqualTo(steps));
            for (int i = 0; i < steps; ++i)
            {
                Assert.That(profile.TryGetAttackIndex(playerClass, i, out int index), Is.True);
                Assert.That(profile.GetAttackClipLength(index), Is.GreaterThan(0f));
            }
            for (int slot = 0; slot < 4; ++slot)
            {
                Assert.That(profile.TryGetSkillIndex(playerClass, ESkillRequestKind.Regular, slot, out int index), Is.True,
                    playerClass + "/" + slot);
                Assert.That(profile.GetSkillClipLength(index), Is.GreaterThan(0f));
            }
        }

        [UnityTest]
        public IEnumerator ClassController_AllActions_ActuallyMoveHumanoidBones()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var catalog = AssetDatabase.LoadAssetAtPath<PlayerCharacterLookCatalogSO>(CATALOG);
            var profile = AssetDatabase.LoadAssetAtPath<PlayerCombatAnimationProfileSO>(PROFILE);
            GameObject actor = Object.Instantiate(catalog.Prefab);
            CharacterP09Look.TryFrom(CharacterAppearanceCatalog.CreateDefault(3), out CharacterP09Look look);
            Assert.That(PlayerCharacterLookApplier.TryApply(actor, catalog, look, out string error), Is.True, error);
            Animator animator = actor.GetComponent<Animator>();
            animator.runtimeAnimatorController = catalog.Controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.SetBool("Alive", true);
            yield return null;
            foreach (EPlayerClass playerClass in new[] { EPlayerClass.Swordsman, EPlayerClass.Mageknight, EPlayerClass.Archer })
            {
                for (int step = 0; step < profile.GetAttackStepCount(playerClass); ++step)
                {
                    Assert.That(profile.TryGetAttackIndex(playerClass, step, out int index), Is.True);
                    AssertMoving(animator, "Attack" + index);
                }
                for (int slot = 0; slot < 4; ++slot)
                {
                    Assert.That(profile.TryGetSkillIndex(playerClass, ESkillRequestKind.Regular, slot, out int index), Is.True);
                    AssertMoving(animator, "Skill" + index);
                }
            }
            Object.Destroy(actor);
            yield return null;
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator ReleaseEffects_Played_ProduceVisiblePixels()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var camera = new GameObject("VfxCamera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 5f, -5f);
            camera.transform.LookAt(Vector3.up);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.orthographicSize = 2.5f;
            var target = new RenderTexture(256, 256, 24);
            camera.targetTexture = target;
            string[] paths = {
                "SwordsmanMagicSlash/PF_SwordsmanMagicSlash_Basic", "SwordsmanMagicSlash/PF_SwordsmanMagicSlash_Wide",
                "SwordsmanMagicSlash/PF_SwordsmanMagicSlash_Dash", "SwordsmanMagicSlash/PF_SwordsmanMagicSlash_Focus",
                "SwordsmanMagicSlash/PF_SwordsmanMagicSlash_ChainFirst", "SwordsmanMagicSlash/PF_SwordsmanMagicSlash_ChainSecond",
                "Archer/PF_ArcherVfx_Arrow", "Archer/PF_ArcherVfx_Muzzle", "Archer/PF_ArcherVfx_Wind",
                "Archer/PF_ArcherVfx_WindMuzzle", "Archer/PF_ArcherVfx_Impact", "Mageknight/PF_MageknightVfx_Basic",
                "Mageknight/PF_MageknightVfx_Shield", "Mageknight/PF_MageknightVfx_Wave",
                "Mageknight/PF_MageknightVfx_Field", "Mageknight/PF_MageknightVfx_Jump"
            };
            foreach (string path in paths)
            {
                var effect = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/02.Prefab/VFX/" + path + ".prefab"));
                foreach (ParticleSystem particle in effect.GetComponentsInChildren<ParticleSystem>())
                {
                    particle.Simulate(0.1f, false, true, false);
                    particle.Pause(false);
                }
                yield return null;
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(256, 256, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
                image.Apply();
                int visible = 0;
                foreach (Color32 pixel in image.GetPixels32())
                {
                    Assert.That(pixel.r > 240 && pixel.b > 240 && pixel.g < 10, Is.False,
                        path + " contains error-shader pixels");
                    if (pixel.r > 10 || pixel.g > 10 || pixel.b > 10)
                    {
                        ++visible;
                    }
                }
                string directory = System.Environment.GetEnvironmentVariable("SHINSHI_UI_CAPTURE_DIR");
                if (!string.IsNullOrEmpty(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, System.IO.Path.GetFileName(path) + ".png"), image.EncodeToPNG());
                }
                Assert.That(visible, Is.GreaterThan(8), path);
                Object.Destroy(image);
                Object.Destroy(effect);
                yield return null;
            }
            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.Destroy(target);
            Object.Destroy(camera.gameObject);
            yield return new ExitPlayMode();
        }

        private static void AssertMoving(Animator animator, string state)
        {
            Transform arm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            animator.Play(state, 0, 0.1f);
            animator.Update(0f);
            Quaternion before = arm.localRotation;
            animator.Play(state, 0, 0.7f);
            animator.Update(0f);
            Assert.That(Quaternion.Angle(before, arm.localRotation), Is.GreaterThan(0.1f), state);
            AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
            Assert.That(clips, Is.Not.Empty, state);
            Assert.That(clips[0].clip.humanMotion, Is.True, state);
        }
    }
}
