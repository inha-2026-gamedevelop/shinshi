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
        [TestCase(EPlayerClass.Archer, 1)]
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
