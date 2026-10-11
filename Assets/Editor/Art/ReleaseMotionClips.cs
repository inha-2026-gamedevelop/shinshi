// System
using System;
using System.IO;
using System.Linq;

// Unity
using UnityEditor;
using UnityEngine;

namespace Shinshi.Editor
{
    /// <summary> 짧은 서버 행동 시간에 맞는 단일 공격 구간을 프로젝트 사본으로 만든다 </summary>
    public static class ReleaseMotionClips
    {
        private const string CG = "Assets/01.Scripts/05.Art/Characters/PlayerP09/Animations/Clips/AN_P09_";
        private const string MK = "Assets/99.ThirdParty/Animation/Magical-Knight_Set/Animation/Humanoid/Inplace/";

        public static void Build()
        {
            Copy("SwordAttack1", CG + "S_Attack_1.anim", 0f, 1.25f);
            Copy("SwordAttack2", CG + "S_Attack_2.anim", 0f, 1.2f);
            Copy("SwordAttack3", CG + "S_Attack_3.anim", 0f, 1.3f);
            Copy("SwordWide", CG + "S_Attack_1.anim", 0.1f, 0.7f);
            Copy("SwordDash", CG + "S_Attack_3.anim", 0.15f, 0.9f);
            Copy("SwordFocus", CG + "S_Attack_3.anim", 0f, 1.2f);
            Copy("SwordChain1", CG + "S_Attack_1.anim", 0f, 0.6f);
            Copy("SwordChain2", CG + "S_Attack_2.anim", 0.1f, 0.54f);
            Copy("MageAttack1", MK + "AttackSword01_inplace.fbx", 0f, 1.25f);
            Copy("MageAttack2", MK + "AttackSword02_inplace.fbx", 0f, 1.25f);
            Copy("MageAttack3", MK + "AttackSword03_inplace.fbx", 0f, 1f);
            Copy("MageArcane", MK + "AttackSword04_inplace.fbx", 0f, 0.6f);
            Copy("MageField", MK + "AttackEnergy03_inplace.fbx", 0.1f, 0.75f);
            Copy("MageSeal", MK + "AttackSword05_inplace.fbx", 0f, 0.65f);
            Copy("MageWave", MK + "AttackSword04_inplace.fbx", 0f, 1.2f);
            string shot = PlayerP09AssetBuilder.CLASS_CLIP_FOLDER + "/AN_P09_AR_Archer_Attack_1Shoot_ALL.anim";
            Copy("ArcherRetreat", shot, 0.1f, 1.0666667f);
            Copy("ArcherPiercing", shot, 0f, 1.0666667f);
        }

        private static void Copy(string name, string sourcePath, float start, float end)
        {
            AnimationClip source = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>()
                .FirstOrDefault(value => !value.name.StartsWith("__preview", StringComparison.Ordinal));
            if (source == null)
            {
                throw new InvalidOperationException("모션 원본이 없습니다: " + sourcePath);
            }
            end = Mathf.Min(end, source.length);
            string path = PlayerP09AssetBuilder.CLASS_CLIP_FOLDER + "/AN_Release_" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }
            EditorUtility.CopySerialized(source, clip);
            clip.name = Path.GetFileNameWithoutExtension(path);
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(source);
            var curves = new AnimationCurve[bindings.Length];
            int frames = Mathf.CeilToInt((end - start) * 30f);
            for (int b = 0; b < bindings.Length; ++b)
            {
                AnimationCurve sourceCurve = AnimationUtility.GetEditorCurve(source, bindings[b]);
                var keys = new Keyframe[frames + 1];
                for (int frame = 0; frame <= frames; ++frame)
                {
                    float time = (end - start) * frame / frames;
                    keys[frame] = new Keyframe(time, sourceCurve.Evaluate(start + time));
                }
                bool constant = true;
                for (int frame = 1; frame <= frames; ++frame)
                {
                    constant &= Mathf.Abs(keys[frame].value - keys[0].value) < 0.000001f;
                }
                curves[b] = new AnimationCurve(constant ? new[] { keys[0], keys[frames] } : keys);
                for (int frame = 0; frame < curves[b].length; ++frame)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curves[b], frame, AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyRightTangentMode(curves[b], frame, AnimationUtility.TangentMode.ClampedAuto);
                }
            }
            AnimationUtility.SetEditorCurves(clip, bindings, curves);
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.startTime = 0f;
            settings.stopTime = end - start;
            settings.loopTime = false;
            settings.loopBlendOrientation = false;
            settings.loopBlendPositionXZ = false;
            settings.loopBlendPositionY = true;
            settings.keepOriginalPositionY = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            if ((!clip.humanMotion) || (Mathf.Abs(clip.length - (end - start)) > 0.02f))
            {
                throw new InvalidOperationException("모션 사본 길이/휴머노이드 오류: " + path);
            }
        }
    }
}
