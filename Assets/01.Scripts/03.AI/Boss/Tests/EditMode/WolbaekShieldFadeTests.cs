// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 성공한 보호막이 제거 시각에 투명해지는지, 원본 프리팹과 공유 재질을 바꾸지 않는지, 다시 만든 보호막은 처음 밝기로 시작하는지 본다
    public class WolbaekShieldFadeTests
    {
        private const string BOSS_PATH    = "Assets/02.Prefab/Boss/PF_WolbaekBoss.prefab";
        private const float  FADE_SECONDS = 2.5f;
        private const float  WARM_SECONDS = 4f;
        private const float  STEP_SECONDS = 1f / 60f;

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; ++i)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }
            _created.Clear();
        }

        [Test]
        public void Visibility_StartsFullEndsZeroAndNeverRises()
        {
            Assert.That(WolbaekShieldFade.Visibility(0f), Is.EqualTo(1f));
            Assert.That(WolbaekShieldFade.Visibility(1f), Is.EqualTo(0f));
            Assert.That(WolbaekShieldFade.Visibility(1.5f), Is.EqualTo(0f));
            float previous = 1f;
            for (int i = 1; i <= 100; ++i)
            {
                float current = WolbaekShieldFade.Visibility(i / 100f);
                Assert.That(current, Is.LessThanOrEqualTo(previous));
                previous = current;
            }
        }

        [Test]
        public void Begin_StopsEmissionAndReachesInvisibleAtFadeEnd()
        {
            GameObject shield = WarmShield();
            WolbaekShieldFade fade = WolbaekShieldFade.Begin(shield, FADE_SECONDS);
            foreach (ParticleSystem system in shield.GetComponentsInChildren<ParticleSystem>(true))
            {
                Assert.That(system.isEmitting, Is.False, system.name);
            }

            // 방출을 멈추는 순간 파티클 수가 바뀔 수 있어 멈춘 뒤의 값을 기준으로 삼는다
            List<byte> before = Alphas(shield);
            Assert.That(before.Count, Is.GreaterThan(0));

            fade.ApplyAt(0f);
            // 다시 쓸 때 막 수명이 끝난 파티클은 빠지고 순서가 바뀔 수 있어 값의 묶음으로 비교한다
            List<byte> atStart = Alphas(shield);
            Assert.That(atStart.Count, Is.GreaterThanOrEqualTo(before.Count - 2));
            Assert.That(before, Is.SupersetOf(atStart), "시작 순간에는 밝기가 그대로다");
            Assert.That(Max(atStart), Is.EqualTo(Max(before)));

            Step(shield, 1.25f);
            fade.ApplyAt(1.25f);
            int maxMid = Max(Alphas(shield));
            Assert.That(maxMid, Is.GreaterThan(0));
            Assert.That(maxMid, Is.LessThan(Max(before)));

            Step(shield, 1.25f);
            fade.ApplyAt(FADE_SECONDS);
            List<byte> end = Alphas(shield);
            Assert.That(end.Count, Is.GreaterThan(0), "수명이 긴 파티클이 남아 있는 상태에서 확인한다");
            Assert.That(Max(end), Is.Zero, "제거 시각에는 남은 파티클이 모두 투명하다");
        }

        [Test]
        public void Begin_DoesNotTouchSourcePrefabOrSharedMaterials()
        {
            GameObject source = ShieldPrefab();
            var prefabColors = new List<Color>();
            var prefabMaterials = new List<Material>();
            var materialStates = new List<string>();
            foreach (ParticleSystem system in source.GetComponentsInChildren<ParticleSystem>(true))
            {
                prefabColors.Add(system.main.startColor.color);
            }
            foreach (Renderer renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    prefabMaterials.Add(material);
                    materialStates.Add(Describe(material));
                }
            }

            GameObject shield = WarmShield();
            WolbaekShieldFade fade = WolbaekShieldFade.Begin(shield, FADE_SECONDS);
            fade.ApplyAt(FADE_SECONDS);

            int index = 0;
            foreach (ParticleSystem system in source.GetComponentsInChildren<ParticleSystem>(true))
            {
                Assert.That(system.main.startColor.color, Is.EqualTo(prefabColors[index++]));
            }
            index = 0;
            foreach (Renderer renderer in shield.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    Assert.That(material, Is.SameAs(prefabMaterials[index]), "인스턴스는 원본과 같은 공유 재질을 그대로 쓴다");
                    Assert.That(Describe(material), Is.EqualTo(materialStates[index]));
                    ++index;
                }
            }
        }

        [Test]
        public void SecondBegin_KeepsFirstFadeAndNewShieldStartsFull()
        {
            GameObject first = WarmShield();
            List<byte> fresh = Alphas(first);
            WolbaekShieldFade fade = WolbaekShieldFade.Begin(first, FADE_SECONDS);
            fade.ApplyAt(2f);
            Assert.That(WolbaekShieldFade.Begin(first, FADE_SECONDS), Is.SameAs(fade));
            Assert.That(first.GetComponents<WolbaekShieldFade>().Length, Is.EqualTo(1));
            fade.ApplyAt(FADE_SECONDS);
            Assert.That(Max(Alphas(first)), Is.Zero);

            GameObject second = WarmShield();
            Assert.That(second.GetComponent<WolbaekShieldFade>(), Is.Null);
            Assert.That(Max(Alphas(second)), Is.EqualTo(Max(fresh)), "다시 만든 보호막은 이전 소멸의 영향을 받지 않는다");
        }

        private GameObject WarmShield()
        {
            GameObject shield = Object.Instantiate(ShieldPrefab());
            _created.Add(shield);
            foreach (ParticleSystem system in shield.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = system.main;
                main.loop = true;
                system.Simulate(0f, false, true, true);
            }
            Step(shield, WARM_SECONDS);
            return shield;
        }

        private static GameObject ShieldPrefab()
        {
#if UNITY_EDITOR
            GameObject boss = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BOSS_PATH);
            Assert.IsNotNull(boss);
            var serialized = new UnityEditor.SerializedObject(boss.GetComponent<WolbaekFoxfireSealView>());
            var shield = serialized.FindProperty("_shieldPrefab").objectReferenceValue as GameObject;
            Assert.IsNotNull(shield);
            return shield;
#else
            Assert.Ignore("에디터에서만 원본 프리팹을 읽는다");
            return null;
#endif
        }

        private static void Step(GameObject shield, float seconds)
        {
            ParticleSystem[] systems = shield.GetComponentsInChildren<ParticleSystem>(true);
            for (float passed = 0f; passed < seconds - 0.0001f; passed += STEP_SECONDS)
            {
                float delta = Mathf.Min(STEP_SECONDS, seconds - passed);
                for (int i = 0; i < systems.Length; ++i)
                {
                    systems[i].Simulate(delta, false, false, true);
                }
            }
        }

        private static List<byte> Alphas(GameObject shield)
        {
            var result = new List<byte>();
            foreach (ParticleSystem system in shield.GetComponentsInChildren<ParticleSystem>(true))
            {
                var buffer = new ParticleSystem.Particle[system.particleCount];
                int count = system.GetParticles(buffer);
                for (int i = 0; i < count; ++i)
                {
                    result.Add(buffer[i].startColor.a);
                }
            }
            return result;
        }

        private static int Max(List<byte> values)
        {
            int max = 0;
            for (int i = 0; i < values.Count; ++i)
            {
                max = Mathf.Max(max, values[i]);
            }
            return max;
        }

        private static string Describe(Material material)
        {
            if (material == null)
            {
                return "null";
            }

            var text = new System.Text.StringBuilder(material.shader.name);
            Shader shader = material.shader;
            for (int i = 0; i < shader.GetPropertyCount(); ++i)
            {
                string name = shader.GetPropertyName(i);
                UnityEngine.Rendering.ShaderPropertyType type = shader.GetPropertyType(i);
                if ((type == UnityEngine.Rendering.ShaderPropertyType.Color) || (type == UnityEngine.Rendering.ShaderPropertyType.Vector))
                {
                    text.Append(name).Append(material.GetVector(name));
                }
                else if ((type == UnityEngine.Rendering.ShaderPropertyType.Float) || (type == UnityEngine.Rendering.ShaderPropertyType.Range))
                {
                    text.Append(name).Append(material.GetFloat(name));
                }
            }
            return text.ToString();
        }
    }
}
