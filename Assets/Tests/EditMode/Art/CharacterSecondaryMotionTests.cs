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
    public class CharacterSecondaryMotionTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float TOLERANCE       = 0.0001f;
        private const float BONE_LENGTH     = 0.25f;
        private const float SINGLE_LENGTH   = 1f;
        private const float FRAME_SECONDS   = 0.016666668f;
        private const float MAX_ANGLE       = 45f;
        private const float CAPSULE_RADIUS  = 0.3f;
        private const float TELEPORT_LENGTH = 10f;
        private const int SETTLE_FRAMES     = 300;

        private static readonly MethodInfo _updateMethod = typeof(CharacterSecondaryMotionView)
            .GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo _tickMethod = typeof(CharacterSecondaryMotionView)
            .GetMethod("UpdateVisual", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _object;
        private Transform _attachment;
        private Transform _first;
        private Transform _second;
        private CharacterSecondaryMotionView _view;

        /****************************************
        *              Unity Event
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _object = new GameObject("SecondaryMotionTests");
            _attachment = CreateChild("Attachment", _object.transform, Vector3.up);
            _first = CreateChild("First", _attachment, Vector3.zero);
            _second = CreateChild("Second", _first, Vector3.right * BONE_LENGTH);
            _view = _object.AddComponent<CharacterSecondaryMotionView>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_object);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void ConstrainDirection_OppositeCandidate_PreservesLengthAndMaximumAngle()
        {
            Vector3 rest = Vector3.right * BONE_LENGTH;
            Vector3 result = SecondaryMotionMath.ConstrainDirection(-rest * TELEPORT_LENGTH, rest, MAX_ANGLE);

            Assert.AreEqual(BONE_LENGTH, result.magnitude, TOLERANCE);
            Assert.LessOrEqual(Vector3.Angle(rest, result), MAX_ANGLE + TOLERANCE);
            Assert.IsTrue(SecondaryMotionMath.IsFinite(result));
        }

        [Test]
        public void ConstrainDirection_NonFiniteCandidate_ReturnsRestDirection()
        {
            Vector3 rest = Vector3.right * BONE_LENGTH;
            Vector3 result = SecondaryMotionMath.ConstrainDirection(new Vector3(float.NaN, 0f, 0f), rest, MAX_ANGLE);

            Assert.AreEqual(rest, result);
        }

        [Test]
        public void ConstrainDirection_ZeroRest_ReturnsFiniteZero()
        {
            Vector3 result = SecondaryMotionMath.ConstrainDirection(Vector3.up, Vector3.zero, MAX_ANGLE);

            Assert.AreEqual(Vector3.zero, result);
        }

        [Test]
        public void ConstrainDirection_SmallGravityDisplacement_PreservesDirectionChange()
        {
            Vector3 rest = Vector3.right * BONE_LENGTH;
            Vector3 candidate = rest + (Vector3.down * TOLERANCE);
            Vector3 result = SecondaryMotionMath.ConstrainDirection(candidate, rest, MAX_ANGLE);

            Assert.Less(result.y, 0f);
            Assert.AreEqual(BONE_LENGTH, result.magnitude, TOLERANCE);
            Assert.AreEqual(0f, (result.normalized - candidate.normalized).magnitude, TOLERANCE);
        }

        [Test]
        public void GetSwingRotation_SmallScaledDirection_RotatesToDesiredDirection()
        {
            Vector3 rest = Vector3.right * BONE_LENGTH;
            Vector3 desired = rest + (Vector3.down * TOLERANCE);
            Quaternion rotation = SecondaryMotionMath.GetSwingRotation(rest, desired);
            Vector3 rotated = rotation * rest.normalized;

            Assert.Less(rotated.y, 0f);
            Assert.AreEqual(0f, (rotated - desired.normalized).magnitude, TOLERANCE);
            Assert.IsTrue(SecondaryMotionMath.IsFinite(rotation));
        }

        [Test]
        public void ResolveCapsule_InsideSide_LeavesCombinedRadius()
        {
            Vector3 point = Vector3.right * (CAPSULE_RADIUS / TELEPORT_LENGTH);
            Vector3 result = SecondaryMotionMath.ResolveCapsule(point, Vector3.down, Vector3.up,
                CAPSULE_RADIUS, Vector3.right);

            Assert.AreEqual(CAPSULE_RADIUS, result.x, TOLERANCE);
            Assert.AreEqual(0f, result.y, TOLERANCE);
            Assert.IsTrue(SecondaryMotionMath.IsFinite(result));
        }

        [Test]
        public void ResolveCapsule_OnAxisWithParallelFallback_ProducesFiniteSurfacePoint()
        {
            Vector3 result = SecondaryMotionMath.ResolveCapsule(Vector3.zero, Vector3.down, Vector3.up,
                CAPSULE_RADIUS, Vector3.up);

            Assert.AreEqual(CAPSULE_RADIUS, new Vector2(result.x, result.z).magnitude, TOLERANCE);
            Assert.IsTrue(SecondaryMotionMath.IsFinite(result));
        }

        [Test]
        public void ResolveCapsule_ZeroLengthCapsule_ActsAsSphere()
        {
            Vector3 result = SecondaryMotionMath.ResolveCapsule(Vector3.zero, Vector3.zero, Vector3.zero,
                CAPSULE_RADIUS, Vector3.zero);

            Assert.AreEqual(CAPSULE_RADIUS, result.magnitude, TOLERANCE);
            Assert.IsTrue(SecondaryMotionMath.IsFinite(result));
        }

        [Test]
        public void ResolveCapsule_OutsideCapsule_PreservesPoint()
        {
            Vector3 point = Vector3.right;
            Vector3 result = SecondaryMotionMath.ResolveCapsule(point, Vector3.down, Vector3.up,
                CAPSULE_RADIUS, Vector3.up);

            Assert.AreEqual(point, result);
        }

        [Test]
        public void HasTeleported_PositionJumpOrRotationJump_RequestsReset()
        {
            Assert.IsTrue(SecondaryMotionMath.HasTeleported(Vector3.zero, Quaternion.identity,
                Vector3.right * TELEPORT_LENGTH, Quaternion.identity, BONE_LENGTH, MAX_ANGLE));
            Assert.IsTrue(SecondaryMotionMath.HasTeleported(Vector3.zero, Quaternion.identity,
                Vector3.zero, Quaternion.Euler(0f, SecondaryMotionMath.MAX_ANGLE, 0f), BONE_LENGTH, MAX_ANGLE));
            Assert.IsFalse(SecondaryMotionMath.HasTeleported(Vector3.zero, Quaternion.identity,
                Vector3.zero, Quaternion.identity, BONE_LENGTH, MAX_ANGLE));
        }

        [Test]
        public void HasTeleported_InvalidRotation_RequestsReset()
        {
            Assert.IsTrue(SecondaryMotionMath.HasTeleported(Vector3.zero, Quaternion.identity,
                Vector3.zero, new Quaternion(0f, 0f, 0f, 0f), BONE_LENGTH, MAX_ANGLE));
        }

        [Test]
        public void Configure_NullNextBone_ReturnsFalseWithoutThrowing()
        {
            var chain = new SecondaryMotionChain(_attachment, new[] { _first, null }, BONE_LENGTH, Vector3.right);

            Assert.DoesNotThrow(() => Assert.IsFalse(Configure(chain)));
            Assert.IsFalse(_view.IsInitialized);
            Assert.IsNotEmpty(_view.ValidationError);
        }

        [Test]
        public void Configure_DuplicateDrivenBone_RejectsConfiguration()
        {
            SecondaryMotionChain chain = CreateChain();

            Assert.IsFalse(Configure(chain, chain));
            Assert.AreEqual(0, _view.SimulatedBoneCount);
        }

        [Test]
        public void Configure_NestedChainAttachment_RejectsAncestorOverlap()
        {
            Transform nestedAttachment = CreateChild("NestedAttachment", _second, Vector3.up);
            Transform nestedBone = CreateChild("NestedBone", nestedAttachment, Vector3.up * BONE_LENGTH);
            var nested = new SecondaryMotionChain(nestedAttachment, new[] { nestedBone }, BONE_LENGTH, Vector3.up);

            Assert.IsFalse(Configure(CreateChain(), nested));
            Assert.IsNotEmpty(_view.ValidationError);
        }

        [Test]
        public void Configure_CapsuleUnderDrivenBone_RejectsFeedbackLoop()
        {
            var capsule = new SecondaryMotionCapsule(_second, Vector3.zero, Vector3.up, CAPSULE_RADIUS);

            Assert.IsFalse(_view.Configure(_object.transform, new[] { CreateChain() }, new[] { capsule }));
        }

        [Test]
        public void Configure_NonFiniteParameter_RejectsConfiguration()
        {
            var chain = new SecondaryMotionChain(_attachment, new[] { _first, _second },
                BONE_LENGTH, Vector3.right, damping: float.NaN);

            Assert.IsFalse(Configure(chain));
        }

        [Test]
        public void UpdateVisual_ReparentedBone_StopsUsingCachedParent()
        {
            Assert.IsTrue(Configure(CreateChain()));
            Transform newParent = CreateChild("NewParent", _object.transform, Vector3.zero);
            newParent.localRotation = Quaternion.Euler(0f, MAX_ANGLE, 0f);
            _first.SetParent(newParent, false);
            Tick(FRAME_SECONDS);

            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, _first.localRotation), TOLERANCE);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, _second.localRotation), TOLERANCE);
        }

        [Test]
        public void UpdateVisual_TipAtCapsuleCenter_EscapesReachableCollisionWithinAngleLimit()
        {
            Vector3 center = _second.position;
            var capsule = new SecondaryMotionCapsule(_object.transform, center, center, BONE_LENGTH / 2f);
            Assert.IsTrue(_view.Configure(_object.transform, new[] { CreateChain() }, new[] { capsule }));
            Tick(FRAME_SECONDS);

            Assert.GreaterOrEqual((_second.position - center).magnitude, (BONE_LENGTH / 2f) - TOLERANCE);
            Assert.AreEqual(BONE_LENGTH, (_second.position - _first.position).magnitude, TOLERANCE);
            Assert.LessOrEqual(Quaternion.Angle(Quaternion.identity, _first.localRotation), MAX_ANGLE + TOLERANCE);
            Assert.AreEqual(0, _view.UnresolvedCollisionCount);
        }

        [Test]
        public void UpdateVisual_GravityParallelToCenteredCapsule_EscapesAlongTangent()
        {
            _attachment.localPosition = Vector3.zero;
            var chain = new SecondaryMotionChain(_attachment, new[] { _first }, SINGLE_LENGTH,
                Vector3.up, maxAngle: MAX_ANGLE, radius: 0f);
            var capsule = new SecondaryMotionCapsule(_object.transform, Vector3.up, Vector3.up, SINGLE_LENGTH / 2f);
            Assert.IsTrue(_view.Configure(_object.transform, new[] { chain }, new[] { capsule }));
            Tick(FRAME_SECONDS);
            Vector3 tip = _first.TransformPoint(Vector3.up * SINGLE_LENGTH);

            Assert.GreaterOrEqual((tip - Vector3.up).magnitude, (SINGLE_LENGTH / 2f) - TOLERANCE);
            Assert.AreEqual(SINGLE_LENGTH, (tip - _first.position).magnitude, TOLERANCE);
            Assert.LessOrEqual(Quaternion.Angle(Quaternion.identity, _first.localRotation), MAX_ANGLE + TOLERANCE);
            Assert.AreEqual(0, _view.UnresolvedCollisionCount);
        }

        [Test]
        public void UpdateVisual_GravityParallelToOffsetCapsule_EscapesStalledLengthProjection()
        {
            _attachment.localPosition = Vector3.zero;
            var chain = new SecondaryMotionChain(_attachment, new[] { _first }, SINGLE_LENGTH,
                Vector3.up, maxAngle: MAX_ANGLE, radius: 0f);
            Vector3 center = Vector3.up * (SINGLE_LENGTH - (BONE_LENGTH / 5f));
            var capsule = new SecondaryMotionCapsule(_object.transform, center, center, SINGLE_LENGTH / 2f);
            Assert.IsTrue(_view.Configure(_object.transform, new[] { chain }, new[] { capsule }));
            Tick(FRAME_SECONDS);
            Vector3 tip = _first.TransformPoint(Vector3.up * SINGLE_LENGTH);

            Assert.GreaterOrEqual((tip - center).magnitude, (SINGLE_LENGTH / 2f) - TOLERANCE);
            Assert.AreEqual(SINGLE_LENGTH, (tip - _first.position).magnitude, TOLERANCE);
            Assert.LessOrEqual(Quaternion.Angle(Quaternion.identity, _first.localRotation), MAX_ANGLE + TOLERANCE);
            Assert.AreEqual(0, _view.UnresolvedCollisionCount);
        }

        [Test]
        public void UpdateVisual_CapsuleAxisAcrossBone_EscapesAlongBothAxesTangent()
        {
            _attachment.localPosition = Vector3.zero;
            var chain = new SecondaryMotionChain(_attachment, new[] { _first }, SINGLE_LENGTH,
                Vector3.up, maxAngle: MAX_ANGLE, radius: 0f);
            var capsule = new SecondaryMotionCapsule(_object.transform, Vector3.up - Vector3.forward,
                Vector3.up + Vector3.forward, SINGLE_LENGTH / 2f);
            Assert.IsTrue(_view.Configure(_object.transform, new[] { chain }, new[] { capsule }));
            Tick(FRAME_SECONDS);
            Vector3 tip = _first.TransformPoint(Vector3.up * SINGLE_LENGTH);

            Assert.IsFalse(SecondaryMotionMath.IsInsideCapsule(tip, capsule.Start, capsule.End, capsule.Radius, TOLERANCE));
            Assert.AreEqual(SINGLE_LENGTH, (tip - _first.position).magnitude, TOLERANCE);
            Assert.LessOrEqual(Quaternion.Angle(Quaternion.identity, _first.localRotation), MAX_ANGLE + TOLERANCE);
            Assert.AreEqual(0, _view.UnresolvedCollisionCount);
        }

        [Test]
        public void UpdateVisual_UnreachableCollision_ReportsPenetrationAndPreservesRig()
        {
            var capsule = new SecondaryMotionCapsule(_object.transform, _first.position, _first.position, CAPSULE_RADIUS);
            Assert.IsTrue(_view.Configure(_object.transform, new[] { CreateChain() }, new[] { capsule }));
            Tick(FRAME_SECONDS);

            Assert.Greater(_view.UnresolvedCollisionCount, 0);
            Assert.AreEqual(BONE_LENGTH, (_second.position - _first.position).magnitude, TOLERANCE);
            Assert.IsTrue(SecondaryMotionMath.IsFinite(_first.localRotation));
        }

        [Test]
        public void UpdateVisual_MovementAndGravity_PreservesLocalLengthsPositionsAndScales()
        {
            Vector3 firstPosition = _first.localPosition;
            Vector3 secondPosition = _second.localPosition;
            Vector3 firstScale = _first.localScale;
            Vector3 secondScale = _second.localScale;
            Assert.IsTrue(Configure(CreateChain()));

            for (int i = 0; i < SETTLE_FRAMES; ++i)
            {
                _object.transform.position += Vector3.forward * TOLERANCE;
                Tick(FRAME_SECONDS);
            }

            Assert.AreEqual(firstPosition, _first.localPosition);
            Assert.AreEqual(secondPosition, _second.localPosition);
            Assert.AreEqual(firstScale, _first.localScale);
            Assert.AreEqual(secondScale, _second.localScale);
            Assert.AreEqual(BONE_LENGTH, (_second.position - _first.position).magnitude, TOLERANCE);
            Assert.Greater(Quaternion.Angle(Quaternion.identity, _first.localRotation), TOLERANCE, DescribeSimulation());
            Assert.LessOrEqual(Quaternion.Angle(Quaternion.identity, _first.localRotation), MAX_ANGLE + TOLERANCE);
            Assert.IsTrue(SecondaryMotionMath.IsFinite(_second.rotation));
        }

        [Test]
        public void UpdateVisual_NonUniformRootScale_PreservesRigAndAngleLimit()
        {
            _object.transform.localScale = new Vector3(2f, 0.75f, 1.25f);
            Assert.IsTrue(Configure(CreateChain()));
            for (int i = 0; i < SETTLE_FRAMES; ++i)
            {
                Tick(FRAME_SECONDS);
            }

            Assert.AreEqual(Vector3.right * BONE_LENGTH, _second.localPosition);
            Assert.AreEqual(Vector3.one, _first.localScale);
            Assert.AreEqual(Vector3.one, _second.localScale);
            Assert.LessOrEqual(Quaternion.Angle(Quaternion.identity, _first.localRotation), MAX_ANGLE + TOLERANCE);
            Assert.IsTrue(SecondaryMotionMath.IsFinite(_second.rotation));
        }

        [Test]
        public void UpdateVisual_Teleport_DiscardsOldInertia()
        {
            Assert.IsTrue(Configure(CreateChain()));
            for (int i = 0; i < SETTLE_FRAMES; ++i)
            {
                Tick(FRAME_SECONDS);
            }

            Assert.Greater(Quaternion.Angle(Quaternion.identity, _first.localRotation), TOLERANCE, DescribeSimulation());
            _object.transform.position += Vector3.forward * TELEPORT_LENGTH;
            Tick(FRAME_SECONDS);

            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, _first.localRotation), TOLERANCE);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, _second.localRotation), TOLERANCE);
        }

        [Test]
        public void UpdateVisual_InvalidDeltaTime_RestoresBasePoseWithoutPoisoningNextFrame()
        {
            Assert.IsTrue(Configure(CreateChain()));
            Tick(FRAME_SECONDS);
            Tick(float.NaN);

            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, _first.localRotation), TOLERANCE);
            Tick(FRAME_SECONDS);
            Assert.IsTrue(SecondaryMotionMath.IsFinite(_first.localRotation));
            Assert.IsTrue(SecondaryMotionMath.IsFinite(_second.localRotation));
        }

        [Test]
        public void ResetSimulation_AfterSwing_RestoresCachedPoseWithoutRecapturingSwing()
        {
            Assert.IsTrue(Configure(CreateChain()));
            for (int i = 0; i < SETTLE_FRAMES; ++i)
            {
                Tick(FRAME_SECONDS);
            }

            _view.ResetSimulation();
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, _first.localRotation), TOLERANCE);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, _second.localRotation), TOLERANCE);
        }

        [Test]
        public void OnDisableAndOnEnable_AfterSwing_RestoresAndResetsCachedPose()
        {
            Assert.IsTrue(Configure(CreateChain()));
            for (int i = 0; i < SETTLE_FRAMES; ++i)
            {
                Tick(FRAME_SECONDS);
            }

            InvokeLifecycle("OnDisable");
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, _first.localRotation), TOLERANCE);
            _object.transform.position += Vector3.forward * TELEPORT_LENGTH;
            InvokeLifecycle("OnEnable");
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, _first.localRotation), TOLERANCE);
            Tick(FRAME_SECONDS);
            Assert.Less(Quaternion.Angle(Quaternion.identity, _first.localRotation), MAX_ANGLE);
        }

        /****************************************
        *                Methods
        ****************************************/

        private bool Configure(params SecondaryMotionChain[] chains)
        {
            return _view.Configure(_object.transform, chains, Array.Empty<SecondaryMotionCapsule>());
        }

        private SecondaryMotionChain CreateChain()
        {
            return new SecondaryMotionChain(_attachment, new[] { _first, _second }, BONE_LENGTH,
                Vector3.right, maxAngle: MAX_ANGLE, radius: 0f);
        }

        private void Tick(float deltaTime)
        {
            // Unity 루프를 직접 조작하지 않고 비공개 이벤트를 같은 순서로 호출한다
            _updateMethod.Invoke(_view, null);
            _tickMethod.Invoke(_view, new object[] { deltaTime });
        }

        private void InvokeLifecycle(string method)
        {
            typeof(CharacterSecondaryMotionView).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_view, null);
        }

        private string DescribeSimulation()
        {
            const BindingFlags FLAGS = BindingFlags.NonPublic | BindingFlags.Instance;
            Type type = typeof(CharacterSecondaryMotionView);
            var states = (Array)type.GetField("_boneStates", FLAGS).GetValue(_view);
            object first = states.GetValue(0);
            Type stateType = first.GetType();
            return $"initialized={_view.IsInitialized}, error={_view.ValidationError}, rotation={_first.localRotation}, "
                + $"directionAngle={Vector3.Angle(Vector3.right, _second.position - _first.position)}, "
                + $"tip={stateType.GetField("Tip").GetValue(first)}, previousTip={stateType.GetField("PreviousTip").GetValue(first)}, "
                + $"needsReset={type.GetField("_needsReset", FLAGS).GetValue(_view)}, accumulator={type.GetField("_accumulator", FLAGS).GetValue(_view)}, "
                + $"gravity={type.GetField("_gravity", FLAGS).GetValue(_view)}, step={type.GetField("_stepSeconds", FLAGS).GetValue(_view)}";
        }

        private static Transform CreateChild(string name, Transform parent, Vector3 localPosition)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
            return child.transform;
        }
    }
}
