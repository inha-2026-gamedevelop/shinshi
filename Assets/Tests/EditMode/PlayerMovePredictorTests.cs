// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class PlayerMovePredictorTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    CORNERS          = 4;
        private const int    HISTORY_CAPACITY = 64;
        private const double HISTORY_INTERVAL = 0.001;
        private const float  SPEED            = 5f;
        private const float  FRAME_SECONDS    = 0.1f;
        private const float  TOLERANCE        = 0.0001f;
        private const uint   MOVE_SEQUENCE    = 3;

        private static readonly Vector3 _destination = new Vector3(10f, 0f, 0f);

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Reconcile_ServerOnSameTimeline_KeepsPrediction()
        {
            PlayerMovePredictor predictor = CreateMovingPredictor(out Vector3 position);

            // 서버는 지연 때문에 0.1초 지점에 있다. 클라이언트도 0.1초에 같은 곳을 지났으므로 보정하지 않는다
            var     authority = new PlayerMoveAuthority(new Vector3(0.5f, 0f, 0f), MOVE_SEQUENCE, 0.1f, true);
            Vector3 corrected = predictor.Reconcile(in authority, position, 0.3, FRAME_SECONDS);

            Assert.AreEqual(1.5f, position.x, TOLERANCE);
            Assert.AreEqual(position, corrected);
        }

        [Test]
        public void Reconcile_SmallOffset_BlendsTowardServer()
        {
            PlayerMovePredictor predictor = CreateMovingPredictor(out Vector3 position);

            var     authority = new PlayerMoveAuthority(new Vector3(0.5f, 0f, 0.5f), MOVE_SEQUENCE, 0.1f, true);
            Vector3 corrected = predictor.Reconcile(in authority, position, 0.3, FRAME_SECONDS);

            float expected = 0.5f * (1f - Mathf.Exp(-Constants.Player.PREDICTION_CORRECTION_RATE * FRAME_SECONDS));
            Assert.AreEqual(expected, corrected.z, TOLERANCE);
            Assert.AreEqual(position.x, corrected.x, TOLERANCE);
        }

        [Test]
        public void Reconcile_AfterPartialCorrection_MeasuresOnlyRemainder()
        {
            PlayerMovePredictor predictor = CreateMovingPredictor(out Vector3 position);
            var                 authority = new PlayerMoveAuthority(new Vector3(0.5f, 0f, 0.5f), MOVE_SEQUENCE, 0.1f, true);

            Vector3 first  = predictor.Reconcile(in authority, position, 0.3, FRAME_SECONDS);
            Vector3 second = predictor.Reconcile(in authority, first, 0.3, FRAME_SECONDS);

            // 같은 확정 상태를 두 번 받아도 남은 차이만 줄이므로 0.5를 넘지 않는다
            Assert.Greater(second.z, first.z);
            Assert.Less(second.z, 0.5f);
        }

        [Test]
        public void Reconcile_ErrorBeyondSnapDistance_SnapsWholeError()
        {
            PlayerMovePredictor predictor = CreateMovingPredictor(out Vector3 position);
            float               far       = Constants.Player.PREDICTION_SNAP_DISTANCE + 1f;

            var     authority = new PlayerMoveAuthority(new Vector3(0.5f, 0f, far), MOVE_SEQUENCE, 0.1f, true);
            Vector3 corrected = predictor.Reconcile(in authority, position, 0.3, FRAME_SECONDS);

            Assert.AreEqual(far, corrected.z, TOLERANCE);
        }

        [Test]
        public void Reconcile_NewerMoveNotYetOnServer_KeepsPrediction()
        {
            PlayerMovePredictor predictor = CreateMovingPredictor(out Vector3 position);

            var     authority = new PlayerMoveAuthority(new Vector3(0f, 0f, 2f), MOVE_SEQUENCE - 1, 1f, true);
            Vector3 corrected = predictor.Reconcile(in authority, position, 0.3, FRAME_SECONDS);

            Assert.AreEqual(position, corrected);
        }

        // 지연이 1초 넘게 튀어도 응답을 기다린다. 기다리지 않으면 옛 서버 위치로 스냅했다가 응답이 오면 다시 앞으로 스냅한다
        [Test]
        public void Reconcile_UnackedMoveDuringLatencySpike_KeepsPredictionEvenWhenFar()
        {
            PlayerMovePredictor predictor = CreateMovingPredictor(out Vector3 position);
            Vector3             server    = new Vector3(0f, 0f, Constants.Player.PREDICTION_SNAP_DISTANCE + 1f);

            var     authority = new PlayerMoveAuthority(server, MOVE_SEQUENCE - 1, 1f, true);
            Vector3 corrected = predictor.Reconcile(in authority, position, 2.0, FRAME_SECONDS);

            Assert.AreEqual(position, corrected);
        }

        [Test]
        public void Reconcile_UnackedMovePastTimeout_SnapsWhenFar()
        {
            PlayerMovePredictor predictor = CreateMovingPredictor(out Vector3 position);
            Vector3             server    = new Vector3(0f, 0f, Constants.Player.PREDICTION_SNAP_DISTANCE + 1f);
            double              late      = Constants.Player.PREDICTION_MOVE_ACK_TIMEOUT_SECONDS + 0.5;

            var     authority = new PlayerMoveAuthority(server, MOVE_SEQUENCE - 1, 1f, true);
            Vector3 corrected = predictor.Reconcile(in authority, position, late, FRAME_SECONDS);

            Assert.AreEqual(server, corrected);
        }

        [Test]
        public void SetRoundTripTime_LongRoundTrip_ExtendsAckTimeout()
        {
            PlayerMovePredictor predictor = CreateMovingPredictor(out Vector3 position);
            Vector3             server    = new Vector3(0f, 0f, Constants.Player.PREDICTION_SNAP_DISTANCE + 1f);
            double              late      = Constants.Player.PREDICTION_MOVE_ACK_TIMEOUT_SECONDS + 0.5;

            // 왕복 1초면 배수만큼 기다리므로 기본 시간이 지나도 스냅하지 않는다
            predictor.SetRoundTripTime(1.0);
            var     authority = new PlayerMoveAuthority(server, MOVE_SEQUENCE - 1, 1f, true);
            Vector3 corrected = predictor.Reconcile(in authority, position, late, FRAME_SECONDS);

            Assert.AreEqual(position, corrected);
        }

        [Test]
        public void Reconcile_BothStoppedWithoutRecord_CorrectsTowardServer()
        {
            var predictor = new PlayerMovePredictor(CORNERS, HISTORY_CAPACITY, HISTORY_INTERVAL);
            predictor.Reset(0.0);
            Vector3 position = predictor.Advance(Vector3.zero, SPEED, FRAME_SECONDS);

            var     authority = new PlayerMoveAuthority(new Vector3(0f, 0f, 1f), 0, 0f, false);
            Vector3 corrected = predictor.Reconcile(in authority, position, FRAME_SECONDS, FRAME_SECONDS);

            Assert.Greater(corrected.z, 0f);
            Assert.Less(corrected.z, 1f);
        }

        [Test]
        public void Reconcile_InvalidAuthority_KeepsPrediction()
        {
            PlayerMovePredictor predictor = CreateMovingPredictor(out Vector3 position);

            Vector3 corrected = predictor.Reconcile(default, position, 0.3, FRAME_SECONDS);

            Assert.AreEqual(position, corrected);
        }

        [Test]
        public void Reconcile_ResolvedRecordWhileServerMoves_WaitsUnlessFar()
        {
            PlayerMovePredictor predictor = CreateMovingPredictor(out Vector3 position);
            predictor.ResolveRecord();

            var     authority = new PlayerMoveAuthority(new Vector3(0.5f, 0f, 1f), MOVE_SEQUENCE, 0.1f, true);
            Vector3 corrected = predictor.Reconcile(in authority, position, 0.3, FRAME_SECONDS);

            Assert.AreEqual(position, corrected);
            Assert.IsFalse(predictor.IsMoving);
        }

        [Test]
        public void InterruptMove_WhileMoving_StopsAndRemembers()
        {
            PlayerMovePredictor predictor = CreateMovingPredictor(out _);

            predictor.InterruptMove();

            Assert.IsFalse(predictor.IsMoving);
            Assert.IsTrue(predictor.WasInterrupted);
        }

        [Test]
        public void IsSameActiveDestination_MoveStops_ReturnsTrueOnlyWhileMoving()
        {
            PlayerMovePredictor predictor = CreateMovingPredictor(out _);

            Assert.IsTrue(predictor.IsSameActiveDestination(_destination));
            Assert.IsFalse(predictor.IsSameActiveDestination(Vector3.zero));

            predictor.InterruptMove();
            Assert.IsFalse(predictor.IsSameActiveDestination(_destination));
        }

        // 클라이언트가 먼저 도착한 뒤 같은 곳을 다시 눌러도 서버는 같은 이동으로 보므로 새 기록을 시작하지 않는다
        [Test]
        public void IsSameActiveDestination_ArrivedBeforeServer_ReturnsTrueUntilResolved()
        {
            PlayerMovePredictor predictor = CreateMovingPredictor(out Vector3 position);
            for (double now = 0.4; now <= 3.0; now += 0.1)
            {
                position = predictor.Advance(position, SPEED, now);
            }

            Assert.IsFalse(predictor.IsMoving);
            Assert.IsTrue(predictor.IsSameActiveDestination(_destination));

            predictor.ResolveRecord();
            Assert.IsFalse(predictor.IsSameActiveDestination(_destination));
        }

        [Test]
        public void Follow_FarFromServer_SnapsToServer()
        {
            var     predictor = new PlayerMovePredictor(CORNERS, HISTORY_CAPACITY, HISTORY_INTERVAL);
            Vector3 server    = new Vector3(Constants.Player.PREDICTION_SNAP_DISTANCE + 1f, 0f, 0f);

            var     authority = new PlayerMoveAuthority(server, 0, 0f, false);
            Vector3 followed  = predictor.Follow(in authority, Vector3.zero, FRAME_SECONDS);

            Assert.AreEqual(server, followed);
        }

        /****************************************
        *                Helpers
        ****************************************/

        // 원점에서 +x로 가는 경로를 0초에 시작해 0.1초 간격으로 세 번 진행한다. 0.3초에 x = 1.5
        private static PlayerMovePredictor CreateMovingPredictor(out Vector3 position)
        {
            var predictor = new PlayerMovePredictor(CORNERS, HISTORY_CAPACITY, HISTORY_INTERVAL);
            predictor.Reset(0.0);
            predictor.Path.CornerBuffer[0] = Vector3.zero;
            predictor.Path.CornerBuffer[1] = _destination;
            predictor.Path.Begin(2);
            predictor.BeginMove(MOVE_SEQUENCE, _destination, 0.0);

            position = Vector3.zero;
            position = predictor.Advance(position, SPEED, 0.1);
            position = predictor.Advance(position, SPEED, 0.2);
            position = predictor.Advance(position, SPEED, 0.3);
            return predictor;
        }
    }
}
