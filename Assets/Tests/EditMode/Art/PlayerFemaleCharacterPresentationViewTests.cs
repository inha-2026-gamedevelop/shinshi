// System
using System.Reflection;

// Unity
using UnityEngine;
using UnityEditor.Animations;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class PlayerFemaleCharacterPresentationViewTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  FRAME_SECONDS   = 0.1f;
        private const float  MOVE_DISTANCE   = 0.5f;
        private const float  EXPECTED_SPEED  = 5f;
        private const float  MAX_SPEED       = 12f;
        private const float  EXCESS_SPEED    = 30f;
        private const float  CORRECTION      = 1.3f;
        private const float  OTHER_COUNTER   = 100f;
        private const float  TOLERANCE       = 0.0001f;
        private const float  ROOT_YAW        = 30f;
        private const float  SLOW_TIME_SCALE = 0.5f;
        private const float  TURN_SPEED      = 450f;
        private const float  TURN_PER_TICK   = 45f;
        private const float  RIGHT_ANGLE     = 90f;
        private const float  ANGLE_TOLERANCE = 0.5f;
        private const float  REFERENCE_SPEED = 4f;
        private const float  MAX_PLAYBACK    = 1.6f;

        private const string PLAYBACK_PARAMETER = "MoveAnimSpeed";

        private static readonly MethodInfo _updatePresentation = typeof(PlayerFemaleCharacterPresentationView)
            .GetMethod("UpdatePresentation", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _root;
        private Animator _animator;
        private AnimatorController _controller;
        private PlayerCombatState _combat;
        private PlayerFemaleCharacterPresentationView _view;
        private PlayerFemaleCharacterTestMotionSource _source;
        private float _previousTimeScale;

        /****************************************
        *              Unity Event
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _previousTimeScale = Time.timeScale;
            _root = new GameObject("FemalePresentationTests");
            _root.AddComponent<NetworkIdentity>();
            _combat = _root.AddComponent<PlayerCombatState>();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(_root.transform, false);
            _animator = visual.AddComponent<Animator>();
            _animator.applyRootMotion = false;
            _controller = new AnimatorController();
            _controller.AddLayer("Base Layer");
            _controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            _animator.runtimeAnimatorController = _controller;
            _view = visual.AddComponent<PlayerFemaleCharacterPresentationView>();
            _source = _root.AddComponent<PlayerFemaleCharacterTestMotionSource>();
            _source.MovementRoot = _root.transform;
            SetField(_view, "_speedDampSeconds", 0f);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _previousTimeScale;
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_controller);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Configure_ValidVisualAndSource_BindsWithoutChangingGameplayRoot()
        {
            Vector3 initialPosition = _root.transform.position;
            Quaternion initialRotation = _root.transform.rotation;
            Assert.IsTrue(Bind(), _view.ValidationError);
            Assert.IsTrue(_view.IsBound);
            Assert.AreEqual(initialPosition, _root.transform.position);
            Assert.AreEqual(initialRotation, _root.transform.rotation);
        }

        [Test]
        public void Configure_RootMotionEnabled_RejectsBinding()
        {
            _animator.applyRootMotion = true;
            Assert.IsFalse(Bind());
        }

        [Test]
        public void Configure_MissingFloatSpeed_RejectsBinding()
        {
            _controller.RemoveParameter(0);
            _controller.AddParameter("Speed", AnimatorControllerParameterType.Bool);
            Assert.IsFalse(Bind());
        }

        [Test]
        public void Configure_GameplayRootUsedAsVisualPivot_RejectsBinding()
        {
            Assert.IsFalse(_view.Configure(_animator, _root.transform, _combat, _source, _root.transform));
        }

        [Test]
        public void Configure_ComponentWithoutMotionContract_RejectsBinding()
        {
            Assert.IsFalse(_view.Configure(_animator, _root.transform, _combat, _combat));
        }

        [Test]
        public void Configure_MotionSourceForAnotherRoot_RejectsBinding()
        {
            var otherRoot = new GameObject("OtherPlayer");
            try
            {
                _source.MovementRoot = otherRoot.transform;
                Assert.IsFalse(Bind());
            }
            finally
            {
                Object.DestroyImmediate(otherRoot);
            }
        }

        [Test]
        public void UpdatePresentation_FirstThenMovingSample_PreservesBaselineAndReadsSpeed()
        {
            Assert.IsTrue(Bind());
            Tick(0.0);
            Assert.IsFalse(_view.HasMotionSample);
            _source.TravelledDistance = MOVE_DISTANCE;
            Tick(FRAME_SECONDS);
            Assert.IsTrue(_view.HasMotionSample);
            Assert.AreEqual(EXPECTED_SPEED, _view.LastVisualSpeed, TOLERANCE);
            Assert.AreEqual(EXPECTED_SPEED, _animator.GetFloat("Speed"), TOLERANCE);
        }

        [Test]
        public void UpdatePresentation_StoppedThenRootCorrected_DoesNotAnimateCorrection()
        {
            Assert.IsTrue(Bind());
            Tick(0.0);
            _root.transform.position = Vector3.back * CORRECTION;
            Vector3 correctedPosition = _root.transform.position;
            Tick(FRAME_SECONDS);
            Assert.IsTrue(_view.HasMotionSample);
            Assert.AreEqual(0f, _view.LastVisualSpeed);
            Assert.AreEqual(correctedPosition, _root.transform.position);
        }

        [Test]
        public void UpdatePresentation_UnavailableSource_DistinguishesFromValidStop()
        {
            Assert.IsTrue(Bind());
            Tick(0.0);
            Tick(FRAME_SECONDS);
            Assert.IsTrue(_view.HasMotionSample);
            _source.IsAvailable = false;
            Tick(FRAME_SECONDS + FRAME_SECONDS);
            Assert.IsFalse(_view.HasMotionSample);
            Assert.AreEqual(0f, _view.LastVisualSpeed);
        }

        [Test]
        public void UpdatePresentation_ExcessSpeed_ClampsPresentationOnly()
        {
            _source.UseSpeedOverride = true;
            _source.SpeedOverride = EXCESS_SPEED;
            Assert.IsTrue(Bind());
            Tick(FRAME_SECONDS);
            Assert.AreEqual(MAX_SPEED, _view.LastVisualSpeed);
            Assert.AreEqual(EXCESS_SPEED, _source.SpeedOverride);
        }

        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void UpdatePresentation_InvalidSpeed_ReturnsUnavailableAndClearsAnimator(float invalidSpeed)
        {
            Assert.IsTrue(Bind());
            DriveMovingSample();
            _source.UseSpeedOverride = true;
            _source.SpeedOverride = invalidSpeed;
            Tick(FRAME_SECONDS + FRAME_SECONDS);
            Assert.IsFalse(_view.HasMotionSample);
            Assert.AreEqual(0f, _animator.GetFloat("Speed"));
        }

        [Test]
        public void UpdatePresentation_UnscaledClockAtSlowTimeScale_KeepsPhysicalSpeed()
        {
            Time.timeScale = SLOW_TIME_SCALE;
            Assert.IsTrue(Bind());
            DriveMovingSample();
            Assert.AreEqual(EXPECTED_SPEED, _view.LastVisualSpeed, TOLERANCE);
        }

        [Test]
        public void UpdatePresentation_DeadThenAlive_ClearsSpeedAndRebaselines()
        {
            Assert.IsTrue(Bind());
            DriveMovingSample();
            SetField(_combat, "_lifeState", EParticipantLifeState.Dead);
            Tick(FRAME_SECONDS + FRAME_SECONDS);
            Assert.IsFalse(_view.HasMotionSample);
            Assert.AreEqual(0f, _animator.GetFloat("Speed"));
            SetField(_combat, "_lifeState", EParticipantLifeState.Alive);
            _source.TravelledDistance = OTHER_COUNTER;
            Tick(FRAME_SECONDS + FRAME_SECONDS + FRAME_SECONDS);
            Assert.IsFalse(_view.HasMotionSample);
        }

        [Test]
        public void UpdatePresentation_MissingSource_ClearsPreviousMovingValue()
        {
            Assert.IsTrue(Bind());
            DriveMovingSample();
            Object.DestroyImmediate(_source);
            Tick(FRAME_SECONDS + FRAME_SECONDS);
            Assert.IsFalse(_view.HasMotionSample);
            Assert.AreEqual(0f, _animator.GetFloat("Speed"));
        }

        [Test]
        public void UpdatePresentation_MissingAnimator_DoesNotThrowAndLosesBinding()
        {
            Assert.IsTrue(Bind());
            Object.DestroyImmediate(_animator);
            Assert.DoesNotThrow(() => Tick(FRAME_SECONDS));
            Assert.IsFalse(_view.IsBound);
        }

        [Test]
        public void UpdatePresentation_RemoteFacingWithUnavailableSpeed_RotatesOnlyVisualChild()
        {
            _root.transform.rotation = Quaternion.Euler(0f, ROOT_YAW, 0f);
            Quaternion gameplayRotation = _root.transform.rotation;
            Vector3 gameplayPosition = _root.transform.position;
            SetField(_combat, "_facingYaw", FacingRules.EncodeYaw(Vector3.right));
            _source.IsAvailable = false;
            Assert.IsTrue(Bind());
            Tick(FRAME_SECONDS);
            Assert.Less(Vector3.Distance(Vector3.right, _animator.transform.forward), TOLERANCE);
            Assert.AreEqual(gameplayRotation, _root.transform.rotation);
            Assert.AreEqual(gameplayPosition, _root.transform.position);
            Assert.IsFalse(_view.HasMotionSample);
        }

        [Test]
        public void Configure_SourceReplaced_ResetsBothSourcesAndRequiresFreshBaseline()
        {
            Assert.IsTrue(Bind());
            DriveMovingSample();
            int previousResets = _source.ResetCount;
            var replacement = _root.AddComponent<PlayerFemaleCharacterTestMotionSource>();
            replacement.MovementRoot = _root.transform;
            replacement.TravelledDistance = OTHER_COUNTER;
            Assert.IsTrue(_view.Configure(_animator, _root.transform, _combat, replacement));
            Assert.Greater(_source.ResetCount, previousResets);
            Tick(FRAME_SECONDS + FRAME_SECONDS);
            Assert.IsFalse(_view.HasMotionSample);
            replacement.TravelledDistance += MOVE_DISTANCE;
            Tick(FRAME_SECONDS + FRAME_SECONDS + FRAME_SECONDS);
            Assert.IsTrue(_view.HasMotionSample);
            Assert.AreEqual(EXPECTED_SPEED, _view.LastVisualSpeed, TOLERANCE);
        }

        [Test]
        public void LifecycleCallbacks_ViewReenabled_DoesNotUseDisabledGap()
        {
            Assert.IsTrue(Bind());
            DriveMovingSample();
            _view.enabled = false;
            // EditMode 콜백 직접 검사는 PlayMode에서 Unity가 메시지를 전달하는 검증과 구분한다
            typeof(PlayerFemaleCharacterPresentationView)
                .GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_view, null);
            _source.TravelledDistance = OTHER_COUNTER;
            _view.enabled = true;
            typeof(PlayerFemaleCharacterPresentationView)
                .GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_view, null);
            Tick(FRAME_SECONDS + FRAME_SECONDS);
            Assert.IsFalse(_view.HasMotionSample);
            Assert.AreEqual(0f, _animator.GetFloat("Speed"));
        }

        [Test]
        public void UpdatePresentation_FirstFacing_SnapsThenTurnsAtLimitedRate()
        {
            SetField(_view, "_turnDegreesPerSecond", TURN_SPEED);
            SetField(_combat, "_facingYaw", FacingRules.EncodeYaw(Vector3.right));
            Assert.IsTrue(Bind());
            Tick(FRAME_SECONDS);
            Assert.Less(Vector3.Distance(Vector3.right, _animator.transform.forward), TOLERANCE);

            SetField(_combat, "_facingYaw", FacingRules.EncodeYaw(Vector3.forward));
            Tick(FRAME_SECONDS + FRAME_SECONDS);
            Assert.AreEqual(TURN_PER_TICK, Vector3.Angle(Vector3.right, _animator.transform.forward), ANGLE_TOLERANCE);
            Assert.AreEqual(TURN_PER_TICK, Vector3.Angle(Vector3.forward, _animator.transform.forward), ANGLE_TOLERANCE);

            Tick(FRAME_SECONDS + FRAME_SECONDS + FRAME_SECONDS);
            Assert.AreEqual(RIGHT_ANGLE, Vector3.Angle(Vector3.right, _animator.transform.forward), ANGLE_TOLERANCE);
            Assert.Less(Vector3.Angle(Vector3.forward, _animator.transform.forward), ANGLE_TOLERANCE);
        }

        [Test]
        public void UpdatePresentation_NewAttack_SnapsToAimInsteadOfTurning()
        {
            SetField(_view, "_turnDegreesPerSecond", TURN_SPEED);
            SetField(_combat, "_facingYaw", FacingRules.EncodeYaw(Vector3.right));
            Assert.IsTrue(Bind());
            Tick(FRAME_SECONDS);

            // 서버는 조준 방향과 공격 순번을 같은 묶음으로 보낸다
            SetField(_combat, "_facingYaw", FacingRules.EncodeYaw(Vector3.left));
            SetField(_combat, "_attackSequence", (byte)1);
            Tick(FRAME_SECONDS + FRAME_SECONDS);
            Assert.Less(Vector3.Angle(Vector3.left, _animator.transform.forward), ANGLE_TOLERANCE);
        }

        [Test]
        public void UpdatePresentation_FirstSeenAttackSequence_DoesNotSnap()
        {
            SetField(_view, "_turnDegreesPerSecond", TURN_SPEED);
            SetField(_combat, "_facingYaw", FacingRules.EncodeYaw(Vector3.right));
            SetField(_combat, "_attackSequence", (byte)3);
            Assert.IsTrue(Bind());
            Tick(FRAME_SECONDS);

            SetField(_combat, "_facingYaw", FacingRules.EncodeYaw(Vector3.forward));
            Tick(FRAME_SECONDS + FRAME_SECONDS);
            Assert.AreEqual(TURN_PER_TICK, Vector3.Angle(Vector3.right, _animator.transform.forward), ANGLE_TOLERANCE);
        }

        [Test]
        public void Configure_Rebind_SnapsFacingAgainOnFirstFrame()
        {
            SetField(_view, "_turnDegreesPerSecond", TURN_SPEED);
            SetField(_combat, "_facingYaw", FacingRules.EncodeYaw(Vector3.right));
            Assert.IsTrue(Bind());
            Tick(FRAME_SECONDS);
            SetField(_combat, "_facingYaw", FacingRules.EncodeYaw(Vector3.back));
            Assert.IsTrue(Bind());
            Tick(FRAME_SECONDS + FRAME_SECONDS);
            Assert.Less(Vector3.Angle(Vector3.back, _animator.transform.forward), ANGLE_TOLERANCE);
        }

        [Test]
        public void UpdatePresentation_FacingRotation_NeverTouchesGameplayRoot()
        {
            SetField(_view, "_turnDegreesPerSecond", TURN_SPEED);
            SetField(_combat, "_facingYaw", FacingRules.EncodeYaw(Vector3.left));
            Quaternion gameplayRotation = _root.transform.rotation;
            Assert.IsTrue(Bind());
            Tick(FRAME_SECONDS);
            SetField(_combat, "_facingYaw", FacingRules.EncodeYaw(Vector3.back));
            Tick(FRAME_SECONDS + FRAME_SECONDS);
            Assert.AreEqual(gameplayRotation, _root.transform.rotation);
        }

        [Test]
        public void UpdatePresentation_PlaybackParameterPresent_ScalesOnlyAboveReference()
        {
            AddControllerParameter(PLAYBACK_PARAMETER, AnimatorControllerParameterType.Float);
            SetField(_view, "_locomotionReferenceSpeed", REFERENCE_SPEED);
            SetField(_view, "_maximumPlaybackScale", MAX_PLAYBACK);
            Assert.IsTrue(Bind(), _view.ValidationError);
            DriveMovingSample();
            Assert.AreEqual(EXPECTED_SPEED / REFERENCE_SPEED, _animator.GetFloat(PLAYBACK_PARAMETER), TOLERANCE);

            _source.UseSpeedOverride = true;
            _source.SpeedOverride = REFERENCE_SPEED * 0.5f;
            Tick(FRAME_SECONDS + FRAME_SECONDS);
            Assert.AreEqual(1f, _animator.GetFloat(PLAYBACK_PARAMETER));

            _source.SpeedOverride = MAX_SPEED;
            Tick(FRAME_SECONDS + FRAME_SECONDS + FRAME_SECONDS);
            Assert.AreEqual(MAX_PLAYBACK, _animator.GetFloat(PLAYBACK_PARAMETER));
        }

        [Test]
        public void UpdatePresentation_PlaybackParameterPresentAndSourceLost_ResetsToNormalSpeed()
        {
            AddControllerParameter(PLAYBACK_PARAMETER, AnimatorControllerParameterType.Float);
            SetField(_view, "_locomotionReferenceSpeed", REFERENCE_SPEED);
            Assert.IsTrue(Bind(), _view.ValidationError);
            DriveMovingSample();
            _source.IsAvailable = false;
            Tick(FRAME_SECONDS + FRAME_SECONDS);
            Assert.AreEqual(1f, _animator.GetFloat(PLAYBACK_PARAMETER));
        }

        [Test]
        public void UpdatePresentation_PlaybackParameterMissing_BindsAndAnimatesSpeed()
        {
            SetField(_view, "_locomotionReferenceSpeed", REFERENCE_SPEED);
            Assert.IsTrue(Bind(), _view.ValidationError);
            Assert.DoesNotThrow(DriveMovingSample);
            Assert.AreEqual(EXPECTED_SPEED, _animator.GetFloat("Speed"), TOLERANCE);
        }

        [Test]
        public void UpdatePresentation_PlaybackParameterWrongType_LeavesItUntouched()
        {
            AddControllerParameter(PLAYBACK_PARAMETER, AnimatorControllerParameterType.Int);
            SetField(_view, "_locomotionReferenceSpeed", REFERENCE_SPEED);
            Assert.IsTrue(Bind(), _view.ValidationError);
            DriveMovingSample();
            Assert.AreEqual(0, _animator.GetInteger(PLAYBACK_PARAMETER));
        }

        /****************************************
        *                Methods
        ****************************************/

        private bool Bind()
        {
            return _view.Configure(_animator, _root.transform, _combat, _source);
        }

        // 이미 연결된 컨트롤러에 파라미터를 더하면 Animator가 다시 연결될 때까지 새 파라미터를 모른다
        private void AddControllerParameter(string name, AnimatorControllerParameterType type)
        {
            _controller.AddParameter(name, type);
            _animator.runtimeAnimatorController = null;
            _animator.runtimeAnimatorController = _controller;
        }

        private void DriveMovingSample()
        {
            Tick(0.0);
            _source.TravelledDistance = MOVE_DISTANCE;
            Tick(FRAME_SECONDS);
            Assert.IsTrue(_view.HasMotionSample);
        }

        private void Tick(double now)
        {
            _updatePresentation.Invoke(_view, new object[] { now, FRAME_SECONDS });
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }
    }

    public sealed class PlayerFemaleCharacterTestMotionSource : MonoBehaviour, IPlayerFemaleCharacterMotionSource
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly PlayerFemaleCharacterMotionSampler _sampler = new PlayerFemaleCharacterMotionSampler();

        /****************************************
        *              Properties
        ****************************************/

        public Transform MovementRoot { get; set; }
        public float TravelledDistance { get; set; }
        public bool IsAvailable { get; set; } = true;
        public bool UseSpeedOverride { get; set; }
        public float SpeedOverride { get; set; }
        public int ResetCount { get; private set; }

        /****************************************
        *                Methods
        ****************************************/

        public bool TryRead(double unscaledTime, out float speed)
        {
            if (UseSpeedOverride)
            {
                speed = SpeedOverride;
                return IsAvailable;
            }

            return _sampler.TryRead(IsAvailable, TravelledDistance, unscaledTime, out speed);
        }

        public void ResetMotionSample()
        {
            ++ResetCount;
            _sampler.ResetMotionSample();
        }
    }
}
