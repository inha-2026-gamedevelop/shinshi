// System
using System;

// Unity
using UnityEngine;
using UnityEditor;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    public class PlayerFemaleCharacterSecondaryMotionBindingTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string REFERENCE_PREFAB_PATH = "Assets/02.Prefab/Characters/PlayerFemaleTripo/PF_FemaleCommonBody.prefab";
        private const float  BONE_LENGTH           = 0.08f;

        private GameObject _root;
        private Animator _animator;
        private Transform _attachment;
        private Transform _extraBone;
        private Mesh _mesh;
        private SkinnedMeshRenderer _renderer;
        private CharacterSecondaryMotionView _simulation;
        private PlayerFemaleCharacterSecondaryMotionBinding _binding;

        /****************************************
        *              Unity Event
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(REFERENCE_PREFAB_PATH);
            Assert.IsNotNull(prefab, "공통 52본 참조 프리팹이 필요하다");
            _root = UnityEngine.Object.Instantiate(prefab);
            Assert.IsTrue(_root.TryGetComponent(out _animator));
            Assert.IsTrue(_animator.isHuman);
            _attachment = _animator.GetBoneTransform(HumanBodyBones.Head);
            var boneObject = new GameObject("TestHairSecondary");
            _extraBone = boneObject.transform;
            _extraBone.SetParent(_attachment, false);
            var rendererObject = new GameObject("TestHairSkin");
            rendererObject.transform.SetParent(_root.transform, false);
            _renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            _mesh = new Mesh { name = "SecondaryBindingFixture" };
            _mesh.vertices = new[] { Vector3.zero, Vector3.right * BONE_LENGTH, Vector3.up * BONE_LENGTH };
            _mesh.triangles = new[] { 0, 1, 2 };
            _mesh.bindposes = new[] { _extraBone.worldToLocalMatrix * _renderer.transform.localToWorldMatrix };
            var weight = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            _mesh.boneWeights = new[] { weight, weight, weight };
            _renderer.sharedMesh = _mesh;
            _renderer.bones = new[] { _extraBone };
            _renderer.rootBone = _attachment;
            _simulation = _root.AddComponent<CharacterSecondaryMotionView>();
            _binding = _root.AddComponent<PlayerFemaleCharacterSecondaryMotionBinding>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
            UnityEngine.Object.DestroyImmediate(_mesh);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Configure_WeightedExtraBoneOnReferenceHumanoid_InitializesExistingSolver()
        {
            Assert.IsTrue(Configure(new[] { CreateChain(_extraBone) }), _binding.ValidationError);
            Assert.IsTrue(_binding.IsBound);
            Assert.IsTrue(_simulation.IsInitialized);
            Assert.AreEqual(1, _binding.SimulatedBoneCount);
        }

        [Test]
        public void Configure_HumanoidBoneAsSecondary_RejectsBinding()
        {
            Transform head = _animator.GetBoneTransform(HumanBodyBones.Head);
            _renderer.bones = new[] { head };
            var chain = new SecondaryMotionChain(head.parent, new[] { head }, BONE_LENGTH, Vector3.up);
            Assert.IsFalse(Configure(new[] { chain }));
            Assert.IsFalse(_simulation.enabled);
        }

        [Test]
        public void Configure_ExtraBoneNotInSkinReferences_RejectsBinding()
        {
            _renderer.bones = new[] { _attachment };
            Assert.IsFalse(Configure(new[] { CreateChain(_extraBone) }));
        }

        [Test]
        public void Configure_EmptyChainsAfterSuccessfulBinding_DisablesOldSimulation()
        {
            Assert.IsTrue(Configure(new[] { CreateChain(_extraBone) }), _binding.ValidationError);
            Assert.IsFalse(Configure(Array.Empty<SecondaryMotionChain>()));
            Assert.IsFalse(_binding.IsBound);
            Assert.IsFalse(_simulation.enabled);
        }

        [Test]
        public void Configure_AnimatePhysicsMode_RejectsUnsupportedEvaluationOrder()
        {
            _animator.updateMode = AnimatorUpdateMode.Fixed;
            Assert.IsFalse(Configure(new[] { CreateChain(_extraBone) }));
        }

        [Test]
        public void Configure_BrokenDirectParent_PropagatesSolverValidationError()
        {
            _extraBone.SetParent(_root.transform, true);
            Assert.IsFalse(Configure(new[] { CreateChain(_extraBone) }));
            Assert.IsNotEmpty(_binding.ValidationError);
            Assert.IsFalse(_simulation.enabled);
        }

        /****************************************
        *                Methods
        ****************************************/

        private bool Configure(SecondaryMotionChain[] chains)
        {
            return _binding.Configure(_animator, _root.transform, _simulation, new[] { _renderer },
                chains, Array.Empty<SecondaryMotionCapsule>());
        }

        private SecondaryMotionChain CreateChain(Transform bone)
        {
            return new SecondaryMotionChain(_attachment, new[] { bone }, BONE_LENGTH, Vector3.up);
        }
    }
}
