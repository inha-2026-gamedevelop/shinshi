// System
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class PlayerFemaleCharacterPredictionMotionSourceTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    CORNER_CAPACITY  = 4;
        private const int    HISTORY_CAPACITY = 16;
        private const int    PATH_CORNERS     = 2;
        private const double HISTORY_INTERVAL = 0.001;
        private const double FRAME_SECONDS    = 0.1;
        private const float  SPEED            = 5f;
        private const float  PATH_DISTANCE    = 10f;
        private const float  CORRECTION       = 1f;
        private const float  TOLERANCE        = 0.0001f;
        private const uint   MOVE_SEQUENCE    = 1;

        private GameObject _root;
        private NetworkIdentity _identity;
        private PlayerCombatState _combat;
        private PlayerPrediction _prediction;
        private PlayerMovePredictor _predictor;
        private PlayerFemaleCharacterPredictionMotionSource _source;

        /****************************************
        *              Unity Event
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("FemalePredictionMotionTests");
            _identity = _root.AddComponent<NetworkIdentity>();
            _combat = _root.AddComponent<PlayerCombatState>();
            _prediction = _root.AddComponent<PlayerPrediction>();
            _root.TryGetComponent(out PlayerInputState input);
            SetField(_prediction, "_input", input);
            SetField(_prediction, "_combat", _combat);
            _predictor = new PlayerMovePredictor(CORNER_CAPACITY, HISTORY_CAPACITY, HISTORY_INTERVAL);
            _predictor.Reset(0.0);
            SetField(_prediction, "_predictor", _predictor);
            SetField(_prediction, "_isActive", true);
            SetField(_prediction, "_isFollowing", false);
            SetNetworkRole(true, true, false);
            _source = _root.AddComponent<PlayerFemaleCharacterPredictionMotionSource>();
            Assert.IsTrue(_source.Configure(_prediction, _combat), _source.ValidationError);
        }

        [TearDown]
        public void TearDown()
        {
            SetField(_prediction, "_isActive", false);
            SetNetworkRole(false, false, false);
            Object.DestroyImmediate(_root);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryRead_ActiveOwnerPrediction_ReportsAdvancedDistance()
        {
            Assert.IsFalse(_source.TryRead(0.0, out _));
            Advance(FRAME_SECONDS);
            Assert.IsTrue(_source.TryRead(FRAME_SECONDS, out float speed));
            Assert.AreEqual(SPEED, speed, TOLERANCE);
        }

        [Test]
        public void TryRead_StoppedPredictionWithServerCorrection_ReportsZero()
        {
            Assert.IsFalse(_source.TryRead(0.0, out _));
            var authority = new PlayerMoveAuthority(Vector3.back * CORRECTION, MOVE_SEQUENCE, 0f, false);
            _root.transform.position = _predictor.Reconcile(in authority, Vector3.zero, FRAME_SECONDS, (float)FRAME_SECONDS);
            Assert.Greater(_predictor.CorrectedDistance, 0f);
            Assert.Less(_root.transform.position.z, 0f);
            Assert.IsTrue(_source.TryRead(FRAME_SECONDS, out float speed));
            Assert.AreEqual(0f, speed);
        }

        [Test]
        public void TryRead_PredictedMoveAndCorrection_UsesOnlyAdvancedDistance()
        {
            Assert.IsFalse(_source.TryRead(0.0, out _));
            Vector3 position = Advance(FRAME_SECONDS);
            var authority = new PlayerMoveAuthority(position + Vector3.back * CORRECTION,
                MOVE_SEQUENCE, (float)FRAME_SECONDS, true);
            _root.transform.position = _predictor.Reconcile(in authority, position, FRAME_SECONDS, (float)FRAME_SECONDS);
            Assert.Greater(_predictor.CorrectedDistance, 0f);
            Assert.IsTrue(_source.TryRead(FRAME_SECONDS, out float speed));
            Assert.AreEqual(SPEED, speed, TOLERANCE);
        }

        [TestCase(true, false, false)]
        [TestCase(true, true, true)]
        [TestCase(false, true, false)]
        public void TryRead_RemoteHostOrNoClient_ReturnsUnavailable(bool client, bool owned, bool server)
        {
            _source.TryRead(0.0, out _);
            Advance(FRAME_SECONDS);
            SetNetworkRole(client, owned, server);
            Assert.IsFalse(_source.TryRead(FRAME_SECONDS, out float speed));
            Assert.AreEqual(0f, speed);
        }

        [Test]
        public void TryRead_OwnershipLostAndReacquired_DoesNotAccumulateGap()
        {
            _source.TryRead(0.0, out _);
            SetNetworkRole(true, false, false);
            Assert.IsFalse(_source.TryRead(FRAME_SECONDS, out _));
            Advance(FRAME_SECONDS + FRAME_SECONDS);
            SetNetworkRole(true, true, false);
            Assert.IsFalse(_source.TryRead(FRAME_SECONDS + FRAME_SECONDS, out _));
            Assert.IsTrue(_source.TryRead(FRAME_SECONDS + FRAME_SECONDS + FRAME_SECONDS, out float speed));
            Assert.AreEqual(0f, speed);
        }

        [Test]
        public void TryRead_ServerFollowThenPrediction_Rebaselines()
        {
            _source.TryRead(0.0, out _);
            SetField(_prediction, "_isFollowing", true);
            Assert.IsFalse(_source.TryRead(FRAME_SECONDS, out _));
            SetField(_prediction, "_isFollowing", false);
            Assert.IsFalse(_source.TryRead(FRAME_SECONDS + FRAME_SECONDS, out _));
            Assert.IsTrue(_source.TryRead(FRAME_SECONDS + FRAME_SECONDS + FRAME_SECONDS, out float speed));
            Assert.AreEqual(0f, speed);
        }

        [Test]
        public void TryRead_PredictionDisabledThenEnabled_Rebaselines()
        {
            _source.TryRead(0.0, out _);
            _prediction.enabled = false;
            Assert.IsFalse(_source.TryRead(FRAME_SECONDS, out _));
            _prediction.enabled = true;
            SetField(_prediction, "_isActive", true);
            Assert.IsFalse(_source.TryRead(FRAME_SECONDS + FRAME_SECONDS, out _));
        }

        [Test]
        public void LifecycleCallbacks_SourceDisabledThenEnabled_Rebaselines()
        {
            _source.TryRead(0.0, out _);
            _source.enabled = false;
            // EditMode는 일반 MonoBehaviour의 활성화 메시지를 보내지 않으므로 콜백 본문을 직접 검증한다
            typeof(PlayerFemaleCharacterPredictionMotionSource)
                .GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_source, null);
            Advance(FRAME_SECONDS);
            _source.enabled = true;
            typeof(PlayerFemaleCharacterPredictionMotionSource)
                .GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_source, null);
            Assert.IsFalse(_source.TryRead(FRAME_SECONDS, out _));
        }

        [Test]
        public void TryRead_DeadThenAlive_Rebaselines()
        {
            _source.TryRead(0.0, out _);
            SetField(_combat, "_lifeState", EParticipantLifeState.Dead);
            Assert.IsFalse(_source.TryRead(FRAME_SECONDS, out _));
            SetField(_combat, "_lifeState", EParticipantLifeState.Alive);
            Assert.IsFalse(_source.TryRead(FRAME_SECONDS + FRAME_SECONDS, out _));
        }

        [Test]
        public void Configure_SourceReferenceRebound_DiscardsPriorBaseline()
        {
            _source.TryRead(0.0, out _);
            Advance(FRAME_SECONDS);
            Assert.IsTrue(_source.Configure(_prediction, _combat));
            Assert.IsFalse(_source.TryRead(FRAME_SECONDS, out _));
        }

        /****************************************
        *                Methods
        ****************************************/

        private Vector3 Advance(double now)
        {
            _predictor.Path.CornerBuffer[0] = Vector3.zero;
            _predictor.Path.CornerBuffer[1] = Vector3.forward * PATH_DISTANCE;
            _predictor.Path.Begin(PATH_CORNERS);
            _predictor.BeginMove(MOVE_SEQUENCE, Vector3.forward * PATH_DISTANCE, 0.0);
            return _predictor.Advance(Vector3.zero, SPEED, now);
        }

        private void SetNetworkRole(bool client, bool owned, bool server)
        {
            typeof(NetworkIdentity).GetProperty("isClient").SetValue(_identity, client);
            typeof(NetworkIdentity).GetProperty("isOwned").SetValue(_identity, owned);
            typeof(NetworkIdentity).GetProperty("isServer").SetValue(_identity, server);
            typeof(NetworkBehaviour).GetProperty("netIdentity").SetValue(_combat, _identity);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }
    }
}
