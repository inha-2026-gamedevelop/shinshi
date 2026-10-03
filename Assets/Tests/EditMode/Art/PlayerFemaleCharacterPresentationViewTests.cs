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

        private const float DELTA_SECONDS       = 0.1f;
        private const float TELEPORT_DISTANCE   = 1.5f;
        private const float MAX_SPEED           = 12f;
        private const float TOLERANCE           = 0.00001f;
        private const float MOVE_DISTANCE       = 0.2f;
        private const float EXPECTED_SPEED      = 2f;
        private const float CORRECTION_DISTANCE = 1.3f;

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
        public void GetPlanarSpeed_RegularMovement_ReturnsMetersPerSecond()
        {
            Assert.AreEqual(EXPECTED_SPEED, GetSpeed(Vector3.zero, Vector3.forward * MOVE_DISTANCE), TOLERANCE);
        }

        [Test]
        public void GetPlanarSpeed_VerticalMotion_ReturnsZero()
        {
            Assert.AreEqual(0f, GetSpeed(Vector3.zero, Vector3.up));
        }

        [Test]
        public void GetPlanarSpeed_Teleport_ReturnsZero()
        {
            Assert.AreEqual(0f, GetSpeed(Vector3.zero, Vector3.forward * TELEPORT_DISTANCE));
        }

        [Test]
        public void GetPlanarSpeed_CorrectionBelowTeleportThreshold_ClampsVisualSpeed()
        {
            Assert.AreEqual(MAX_SPEED, GetSpeed(Vector3.zero, Vector3.forward * CORRECTION_DISTANCE));
        }

        [TestCase(0f)]
        [TestCase(float.NaN)]
        public void GetPlanarSpeed_InvalidDeltaTime_ReturnsZero(float deltaSeconds)
        {
            Assert.AreEqual(0f, PlayerFemaleCharacterPresentationView.GetPlanarSpeed(Vector3.zero, Vector3.forward,
                deltaSeconds, TELEPORT_DISTANCE, MAX_SPEED));
        }

        /****************************************
        *                Methods
        ****************************************/

        private static float GetSpeed(Vector3 previous, Vector3 current)
        {
            return PlayerFemaleCharacterPresentationView.GetPlanarSpeed(previous, current, DELTA_SECONDS,
                TELEPORT_DISTANCE, MAX_SPEED);
        }
    }
}
