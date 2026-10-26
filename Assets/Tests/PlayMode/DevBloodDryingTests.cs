// System
using System;
using System.Collections;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// Third Party
using NUnit.Framework;

namespace Shinshi.Tests
{
    public sealed class DevBloodDryingTests
    {
        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int CAPACITY = 160;
        private static readonly Vector3 s_point = new Vector3(10000f, 0f, 10000f);
        private GameObject _floor;
        private GameObject _root;
        private Component _blood;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _floor = new GameObject("BloodDryingTestFloor");
            _floor.transform.position = s_point + (Vector3.down * 0.5f);
            _floor.AddComponent<BoxCollider>().size = new Vector3(100f, 1f, 100f);
            _root = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("DevHitBlood/PF_DevHitBlood"));
            _blood = _root.GetComponent(Type.GetType("Shinshi.Dev.DevHitFeedbackBlood, Shinshi.Dev"));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            UnityEngine.Object.Destroy(_root);
            UnityEngine.Object.Destroy(_floor);
            yield return null;
        }

        [Test]
        public void Stain_DriesThenFadesContinuouslyBeforeHiding()
        {
            Stamp(s_point);
            object stain = Stains().GetValue(0);
            MeshRenderer renderer = Renderer(stain);
            Color wet = Tint(renderer);
            float wetGloss = Gloss(renderer);
            Seek(stain, "_fade", 12f);
            Color dry = Tint(renderer);
            Assert.That(dry.r, Is.LessThan(wet.r));
            Assert.That(Gloss(renderer), Is.LessThan(wetGloss));
            Assert.IsTrue(renderer.enabled);
            Seek(stain, "_fade", 36f);
            float middle = Tint(renderer).a;
            Assert.That(middle, Is.GreaterThan(0f).And.LessThan(dry.a));
            Seek(stain, "_fade", 44f);
            Assert.That(Tint(renderer).a, Is.GreaterThan(0f).And.LessThan(middle));
            Assert.IsTrue(renderer.enabled);
            Seek(stain, "_fade", 45f);
            Assert.IsFalse(renderer.enabled);
        }

        [Test]
        public void Stain_FullPoolFadesOldSurfaceBeforeReusingMesh()
        {
            for (int i = 0; i < CAPACITY; ++i)
            {
                Stamp(s_point);
            }
            object stain = Stains().GetValue(0);
            MeshRenderer renderer = Renderer(stain);
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] original = mesh.vertices;
            int rendererCount = _root.GetComponentsInChildren<Renderer>(true).Length;
            float alpha = Tint(renderer).a;
            Stamp(s_point + Vector3.right * 5f);
            Assert.That(Visible(), Is.EqualTo(CAPACITY));
            CollectionAssert.AreEqual(original, mesh.vertices);
            Assert.That(Tint(renderer).a, Is.EqualTo(alpha).Within(0.001f));
            Seek(stain, "_retire", 0.325f);
            Assert.IsTrue(renderer.enabled);
            Assert.That(Tint(renderer).a, Is.GreaterThan(0f).And.LessThan(alpha));
            float fading = Tint(renderer).a;
            stain.GetType().GetMethod("Retire").Invoke(stain, null);
            Assert.That(Tint(renderer).a, Is.EqualTo(fading).Within(0.001f));
            Seek(stain, "_retire", 0.65f);
            Assert.IsFalse(renderer.enabled);
            Stamp(s_point + Vector3.right * 5f);
            Assert.IsTrue(renderer.enabled);
            Assert.That(Tint(renderer).a, Is.EqualTo(alpha).Within(0.001f));
            Assert.That(Gloss(renderer), Is.EqualTo(0.45f).Within(0.001f));
            Assert.That(mesh.vertices[0], Is.Not.EqualTo(original[0]));
            Assert.That(_root.GetComponentsInChildren<Renderer>(true).Length, Is.EqualTo(rendererCount));
            Assert.That(Visible(), Is.EqualTo(CAPACITY));
        }

        [Test]
        public void Stain_DisableClearsAndNextUseStartsFresh()
        {
            Stamp(s_point);
            Seek(Stains().GetValue(0), "_fade", 35f);
            _root.SetActive(false);
            Assert.That(Visible(), Is.Zero);
            _root.SetActive(true);
            Stamp(s_point);
            object fresh = Stains().GetValue(1);
            Assert.That(Tint(Renderer(fresh)).a, Is.EqualTo(0.95f).Within(0.001f));
            Assert.That(Gloss(Renderer(fresh)), Is.EqualTo(0.45f).Within(0.001f));
        }

        private Array Stains() { return (Array)_blood.GetType().GetField("_stains", PRIVATE_INSTANCE).GetValue(_blood); }
        private int Visible() { return (int)_blood.GetType().GetProperty("VisibleStains").GetValue(_blood); }
        private void Stamp(Vector3 point) { _blood.GetType().GetMethod("StampLandingBlood").Invoke(_blood, new object[] { point, 1f }); }
        private static MeshRenderer Renderer(object stain) { return (MeshRenderer)stain.GetType().GetField("_renderer", PRIVATE_INSTANCE).GetValue(stain); }

        private static Color Tint(MeshRenderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetColor("_BaseColor");
        }

        private static float Gloss(MeshRenderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetFloat("_Smoothness");
        }

        private static void Seek(object stain, string field, float seconds)
        {
            object tween = stain.GetType().GetField(field, PRIVATE_INSTANCE).GetValue(stain);
            tween.GetType().Assembly.GetType("DG.Tweening.TweenExtensions").GetMethod("Goto", BindingFlags.Static | BindingFlags.Public)
                .Invoke(null, new object[] { tween, seconds, false });
        }
    }
}
