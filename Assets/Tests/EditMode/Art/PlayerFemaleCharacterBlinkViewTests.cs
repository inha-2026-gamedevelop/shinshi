// System
using System;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    public class PlayerFemaleCharacterBlinkViewTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   LOD_COUNT      = 3;
        private const float MAX_WEIGHT     = 100f;
        private const float INITIAL_WEIGHT = 15f;
        private const float HALF_CLOSED    = 0.5f;

        private GameObject _root;
        private Mesh[] _meshes;
        private SkinnedMeshRenderer[] _renderers;
        private PlayerFemaleCharacterBlinkView _view;

        /****************************************
        *              Unity Event
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("FemaleBlinkTests");
            _meshes = new Mesh[LOD_COUNT];
            _renderers = new SkinnedMeshRenderer[LOD_COUNT];
            for (int i = 0; i < LOD_COUNT; ++i)
            {
                var child = new GameObject("FaceLOD" + i);
                child.transform.SetParent(_root.transform, false);
                _meshes[i] = CreateFaceMesh();
                _renderers[i] = child.AddComponent<SkinnedMeshRenderer>();
                _renderers[i].sharedMesh = _meshes[i];
            }

            _view = _root.AddComponent<PlayerFemaleCharacterBlinkView>();
            Assert.IsTrue(_view.Configure(_renderers, "Blink", new[] { "Smile" }), _view.ValidationError);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
            for (int i = 0; i < _meshes.Length; ++i)
            {
                UnityEngine.Object.DestroyImmediate(_meshes[i]);
            }
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TrySetBlink_ThreeLods_UpdatesEveryRendererIncludingHiddenLod()
        {
            _renderers[LOD_COUNT - 1].enabled = false;
            Assert.IsTrue(_view.TrySetBlink(HALF_CLOSED));
            for (int i = 0; i < LOD_COUNT; ++i)
            {
                Assert.AreEqual(MAX_WEIGHT * HALF_CLOSED, _renderers[i].GetBlendShapeWeight(0));
            }

            Assert.AreEqual(LOD_COUNT, _view.BlinkRendererCount);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void TrySetBlink_NonFiniteWeight_PreservesPreviousPose(float weight)
        {
            Assert.IsTrue(_view.TrySetBlink(HALF_CLOSED));
            Assert.IsFalse(_view.TrySetBlink(weight));
            Assert.AreEqual(MAX_WEIGHT * HALF_CLOSED, _renderers[0].GetBlendShapeWeight(0));
        }

        [TestCase(-1f, 0f)]
        [TestCase(2f, MAX_WEIGHT)]
        public void TrySetBlink_OutsideNormalizedRange_ClampsWeight(float normalizedWeight, float expectedWeight)
        {
            Assert.IsTrue(_view.TrySetBlink(normalizedWeight));
            Assert.AreEqual(expectedWeight, _renderers[0].GetBlendShapeWeight(0));
        }

        [Test]
        public void TrySetExpression_Smile_DoesNotChangeBlink()
        {
            Assert.IsTrue(_view.TrySetBlink(HALF_CLOSED));
            Assert.IsTrue(_view.TrySetExpression("Smile", 1f));
            for (int i = 0; i < LOD_COUNT; ++i)
            {
                Assert.AreEqual(MAX_WEIGHT * HALF_CLOSED, _renderers[i].GetBlendShapeWeight(0));
                Assert.AreEqual(MAX_WEIGHT, _renderers[i].GetBlendShapeWeight(1));
            }

            Assert.IsFalse(_view.TrySetExpression("Missing", 1f));
        }

        [Test]
        public void TrySetBlink_OneLodMeshReplaced_RejectsEntireUpdate()
        {
            Assert.IsTrue(_view.TrySetBlink(HALF_CLOSED));
            _renderers[LOD_COUNT - 1].sharedMesh = null;
            Assert.IsFalse(_view.TrySetBlink(1f));
            Assert.AreEqual(MAX_WEIGHT * HALF_CLOSED, _renderers[0].GetBlendShapeWeight(0));
        }

        [Test]
        public void Configure_MissingMorph_RejectsAndRestoresPreviousBinding()
        {
            Assert.IsTrue(_view.TrySetBlink(1f));
            Assert.IsFalse(_view.Configure(_renderers, "MissingBlink", Array.Empty<string>()));
            Assert.IsFalse(_view.IsBound);
            Assert.AreEqual(0f, _renderers[0].GetBlendShapeWeight(0));
        }

        [Test]
        public void Configure_DuplicateRenderer_RejectsBinding()
        {
            Assert.IsFalse(_view.Configure(new[] { _renderers[0], _renderers[0] }, "Blink", Array.Empty<string>()));
            Assert.IsFalse(_view.IsBound);
        }

        [Test]
        public void Configure_ExpressionDuplicatesBlink_RejectsBinding()
        {
            Assert.IsFalse(_view.Configure(_renderers, "Blink", new[] { "Blink" }));
            Assert.IsFalse(_view.IsBound);
        }

        [Test]
        public void OnDisable_PreexistingWeights_RestoresBlinkAndExpression()
        {
            _view.Configure(Array.Empty<SkinnedMeshRenderer>(), "Blink", Array.Empty<string>());
            for (int i = 0; i < LOD_COUNT; ++i)
            {
                _renderers[i].SetBlendShapeWeight(0, INITIAL_WEIGHT);
                _renderers[i].SetBlendShapeWeight(1, INITIAL_WEIGHT);
            }

            Assert.IsTrue(_view.Configure(_renderers, "Blink", new[] { "Smile" }));
            Assert.IsTrue(_view.TrySetBlink(1f));
            Assert.IsTrue(_view.TrySetExpression("Smile", 1f));
            typeof(PlayerFemaleCharacterBlinkView).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_view, null);

            for (int i = 0; i < LOD_COUNT; ++i)
            {
                Assert.AreEqual(INITIAL_WEIGHT, _renderers[i].GetBlendShapeWeight(0));
                Assert.AreEqual(INITIAL_WEIGHT, _renderers[i].GetBlendShapeWeight(1));
            }
        }

        [Test]
        public void TryBind_AfterManualPose_RestoresOriginalWeightsBeforeRecaching()
        {
            Assert.IsTrue(_view.TrySetBlink(1f));
            Assert.IsTrue(_view.TrySetExpression("Smile", 1f));
            Assert.IsTrue(_view.TryBind(out string error), error);
            Assert.AreEqual(0f, _renderers[0].GetBlendShapeWeight(0));
            Assert.AreEqual(0f, _renderers[0].GetBlendShapeWeight(1));
        }

        /****************************************
        *                Methods
        ****************************************/

        private static Mesh CreateFaceMesh()
        {
            var mesh = new Mesh { name = "BlinkFixture" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            var delta = new[] { Vector3.up, Vector3.zero, Vector3.zero };
            mesh.AddBlendShapeFrame("Blink", MAX_WEIGHT, delta, null, null);
            mesh.AddBlendShapeFrame("Smile", MAX_WEIGHT, delta, null, null);
            return mesh;
        }
    }
}
