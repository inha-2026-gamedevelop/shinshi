// Unity
using UnityEngine;
using UnityEditor.Animations;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    public class PlayerFemaleCharacterPresentationViewTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float DELTA_SECONDS  = 0.1f;
        private const float TOLERANCE      = 0.00001f;
        private const float MOVE_DISTANCE  = 0.2f;
        private const float EXPECTED_SPEED = 2f;
        private const float LARGE_DISTANCE = 1.5f;

        private GameObject _root;
        private Animator _animator;
        private AnimatorController _controller;
        private PlayerFemaleCharacterPresentationView _view;

        /****************************************
        *              Unity Event
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("FemalePresentationTests");
            var visual = new GameObject("Visual");
            visual.transform.SetParent(_root.transform, false);
            _animator = visual.AddComponent<Animator>();
            _animator.applyRootMotion = false;
            _controller = new AnimatorController();
            _controller.AddLayer("Base Layer");
            _controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            _animator.runtimeAnimatorController = _controller;
            _view = visual.AddComponent<PlayerFemaleCharacterPresentationView>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_controller);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Configure_ValidVisualUnderMovementRoot_BindsWithoutChangingMovementRoot()
        {
            Vector3 initialPosition = _root.transform.position;
            Assert.IsTrue(_view.Configure(_animator, _root.transform), _view.ValidationError);
            Assert.IsTrue(_view.IsBound);
            Assert.AreEqual(initialPosition, _root.transform.position);
        }

        [Test]
        public void Configure_RootMotionEnabled_RejectsBinding()
        {
            _animator.applyRootMotion = true;
            Assert.IsFalse(_view.Configure(_animator, _root.transform));
        }

        [Test]
        public void Configure_MissingFloatSpeed_RejectsBinding()
        {
            _controller.RemoveParameter(0);
            _controller.AddParameter("Speed", AnimatorControllerParameterType.Bool);
            Assert.IsFalse(_view.Configure(_animator, _root.transform));
        }

        [Test]
        public void TryRead_FirstSample_EstablishesBaselineAndReturnsFalse()
        {
            var sampler = new PlayerFemaleCharacterMotionSampler();
            Assert.IsFalse(sampler.TryRead(true, 0f, 0.0, out float speed));
            Assert.AreEqual(0f, speed);
        }

        [Test]
        public void TryRead_RegularMovement_ReturnsMetersPerSecond()
        {
            var sampler = new PlayerFemaleCharacterMotionSampler();
            sampler.TryRead(true, 0f, 0.0, out _);
            Assert.IsTrue(sampler.TryRead(true, MOVE_DISTANCE, DELTA_SECONDS, out float speed));
            Assert.AreEqual(EXPECTED_SPEED, speed, TOLERANCE);
        }

        [Test]
        public void TryRead_DistanceDecreased_ResetsBaselineAndReturnsFalse()
        {
            var sampler = new PlayerFemaleCharacterMotionSampler();
            sampler.TryRead(true, LARGE_DISTANCE, DELTA_SECONDS, out _);
            Assert.IsFalse(sampler.TryRead(true, 0f, DELTA_SECONDS * 2f, out float speed));
            Assert.AreEqual(0f, speed);
        }

        [TestCase(0.0)]
        [TestCase(double.NaN)]
        public void TryRead_ElapsedTimeDidNotAdvance_ReturnsFalse(double unscaledTime)
        {
            var sampler = new PlayerFemaleCharacterMotionSampler();
            sampler.TryRead(true, 0f, 0.0, out _);
            Assert.IsFalse(sampler.TryRead(true, MOVE_DISTANCE, unscaledTime, out float speed));
            Assert.AreEqual(0f, speed);
        }

        [Test]
        public void TryRead_NotAvailable_ResetsBaselineAndReturnsFalse()
        {
            var sampler = new PlayerFemaleCharacterMotionSampler();
            sampler.TryRead(true, MOVE_DISTANCE, DELTA_SECONDS, out _);
            Assert.IsFalse(sampler.TryRead(false, MOVE_DISTANCE * 2f, DELTA_SECONDS * 2f, out float speed));
            Assert.AreEqual(0f, speed);
        }
    }
}
