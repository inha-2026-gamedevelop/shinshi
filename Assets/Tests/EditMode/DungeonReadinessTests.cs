// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class DungeonReadinessTests
    {
        // 인증된 연결이 서버 플레이어 개체(AddPlayer)를 만들었다는 신호는 OnParticipantAttached다. Mirror 없이 그 신호와 연결 끊김을 직접 넣는다

        /****************************************
        *                Fields
        ****************************************/

        private const string KEY_ONE = "p:0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string KEY_TWO = "p:0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc02";

        private SessionConfigSO       _config;
        private ServerSessionRegistry _registry;
        private DungeonSessionHost    _host;
        private List<string>          _warnings;
        private List<int>             _timedOut;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config   = ScriptableObject.CreateInstance<SessionConfigSO>();
            _registry = new ServerSessionRegistry(16);
            _warnings = new List<string>();
            _timedOut = new List<int>();
            _host     = new DungeonSessionHost(_config, _registry, null, true, _warnings.Add, new NullSink());
            _host.TracksPlayerReadiness  = true;
            _host.ParticipantLoadTimedOut += _timedOut.Add;
        }

        [TearDown]
        public void TearDown()
        {
            _host.ParticipantLoadTimedOut -= _timedOut.Add;
            _host.Dispose();
            Object.DestroyImmediate(_config);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Start_AuthenticatedButNoPlayer_WaitsUntilAttached()
        {
            SessionBinding one = Admit(KEY_ONE);
            SessionBinding two = Admit(KEY_TWO);
            Assert.AreEqual(2, _host.AwaitingPlayerCount);

            // 첫 틱에서 준비 시간이 시작된다
            Assert.IsFalse(_host.BeginTick(0.0));
            _host.OnParticipantAttached(one.ParticipantId, 0.0);
            Assert.IsFalse(_host.BeginTick(_config.ReadyCountdownSeconds), "한 명이 아직 로딩 중이면 시작하지 않는다");
            Assert.IsFalse(_host.HasStarted);

            _host.OnParticipantAttached(two.ParticipantId, _config.ReadyCountdownSeconds);
            Assert.AreEqual(0, _host.AwaitingPlayerCount);
            Assert.IsTrue(_host.BeginTick(_config.ReadyCountdownSeconds + 1.0), "전원이 준비되면 준비 시간이 지났으므로 시작한다");
            _host.CompleteTick();
            Assert.IsTrue(_host.IsSessionRunning);
            Assert.AreEqual(2, _host.Session.StartingCount);
        }

        [Test]
        public void Start_TrackingOff_StartsWithoutPlayerSignal()
        {
            _host.TracksPlayerReadiness = false;
            Admit(KEY_ONE);

            Assert.AreEqual(0, _host.AwaitingPlayerCount);
            Assert.IsFalse(_host.BeginTick(0.0));
            Assert.IsTrue(_host.BeginTick(_config.ReadyCountdownSeconds));
        }

        [Test]
        public void LoadingTimeout_NotReadyInTime_NotifiesOnceAfterDeadline()
        {
            SessionBinding one = Admit(KEY_ONE);

            Assert.IsFalse(_host.BeginTick(0.0));
            Assert.IsFalse(_host.BeginTick(Constants.Network.LOADING_TIMEOUT_SECONDS - 1.0));
            Assert.AreEqual(0, _timedOut.Count);

            Assert.IsFalse(_host.BeginTick(Constants.Network.LOADING_TIMEOUT_SECONDS));
            CollectionAssert.AreEqual(new[] { one.ParticipantId }, _timedOut);

            _host.BeginTick(Constants.Network.LOADING_TIMEOUT_SECONDS + 1.0);
            Assert.AreEqual(1, _timedOut.Count, "마감 통지는 한 번만 나간다");
        }

        [Test]
        public void LoadingTimeout_ReadyInTime_DoesNotNotify()
        {
            SessionBinding one = Admit(KEY_ONE);
            _host.BeginTick(0.0);
            _host.OnParticipantAttached(one.ParticipantId, 1.0);

            _host.BeginTick(Constants.Network.LOADING_TIMEOUT_SECONDS + 5.0);

            Assert.AreEqual(0, _timedOut.Count);
        }

        [Test]
        public void ConnectionLost_BeforePlayerExists_MarksLobbyAbsentAndBlocksStart()
        {
            SessionBinding one = Admit(KEY_ONE);
            SessionBinding two = Admit(KEY_TWO);
            _host.OnParticipantAttached(two.ParticipantId, 0.0);

            Assert.IsTrue(_host.OnConnectionLost(one.ParticipantId));
            Assert.AreEqual(1, _host.LobbyAbsentCount);
            Assert.AreEqual(0, _host.AwaitingPlayerCount);
            Assert.IsTrue(_host.Roster.TryGet(one.ParticipantId, out ParticipantSnapshot snapshot));
            Assert.AreEqual(EParticipantConnectionState.Connected, snapshot.ConnectionState, "시작 전에는 닫지 않고 돌아오기를 기다린다");
        }

        [Test]
        public void ConnectionLost_AfterPlayerExists_LeavesCleanupToCombat()
        {
            SessionBinding one = Admit(KEY_ONE);
            _host.OnParticipantAttached(one.ParticipantId, 0.0);

            Assert.IsFalse(_host.OnConnectionLost(one.ParticipantId), "개체가 있던 연결의 이탈은 전투 쪽 통지가 처리한다");
            Assert.AreEqual(0, _host.LobbyAbsentCount);
        }

        [Test]
        public void ConnectionLost_ThenRejoinAndReady_ClearsAbsentAndStarts()
        {
            SessionBinding one = Admit(KEY_ONE);
            Assert.IsTrue(_host.OnConnectionLost(one.ParticipantId));
            _registry.Release(one);

            SessionBinding again = Admit(KEY_ONE);
            Assert.AreEqual(one.ParticipantId, again.ParticipantId);
            Assert.AreEqual(1, _host.AwaitingPlayerCount);
            _host.OnParticipantAttached(again.ParticipantId, 1.0);

            Assert.AreEqual(0, _host.LobbyAbsentCount);
            Assert.AreEqual(0, _host.AwaitingPlayerCount);
            Assert.IsFalse(_host.BeginTick(2.0));
            Assert.IsTrue(_host.BeginTick(2.0 + _config.ReadyCountdownSeconds));
        }

        [Test]
        public void Reconnect_DuringGraceWithoutPlayer_KeepsGraceDeadlineWhenLost()
        {
            SessionBinding one = Admit(KEY_ONE);
            SessionBinding two = Admit(KEY_TWO);
            _host.OnParticipantAttached(one.ParticipantId, 0.0);
            _host.OnParticipantAttached(two.ParticipantId, 0.0);
            Assert.IsFalse(_host.BeginTick(0.0));
            double start = _config.ReadyCountdownSeconds;
            Assert.IsTrue(_host.BeginTick(start));
            _host.CompleteTick();

            // 끊김 시각은 서버가 마지막으로 돈 틱 시각이므로 호스트 시계도 그 시각까지 진행시킨다
            double detachTime = start + 1.0;
            Assert.IsTrue(_host.BeginTick(detachTime));
            _host.CompleteTick();
            Assert.IsTrue(_host.OnParticipantDetached(one.ParticipantId, detachTime));
            Assert.IsTrue(_host.Roster.TryGet(one.ParticipantId, out ParticipantSnapshot before));
            _registry.Release(one);

            // 재접속 연결이 인증만 되고 개체를 만들기 전에 끊긴다
            SessionBinding again = Admit(KEY_ONE);
            Assert.AreEqual(1, _host.AwaitingPlayerCount);
            Assert.IsTrue(_host.OnConnectionLost(again.ParticipantId));

            Assert.IsTrue(_host.Roster.TryGet(one.ParticipantId, out ParticipantSnapshot after));
            Assert.AreEqual(EParticipantConnectionState.ReconnectGrace, after.ConnectionState);
            Assert.AreEqual(before.GraceUntil, after.GraceUntil, "개체 없이 끊겨도 유예 만료 시각은 늘지 않는다");
        }

        /****************************************
        *                Helpers
        ****************************************/

        private SessionBinding Admit(string key)
        {
            var           claims = new JoinTokenClaims(key);
            ERejectReason reason = _registry.TryAdmit(in claims, out SessionBinding binding);
            Assert.AreEqual(ERejectReason.None, reason);
            Assert.AreEqual(ERejectReason.None, _host.Admit(in claims, binding));
            return binding;
        }

        private sealed class NullSink : IBackendEventSink
        {
            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count, IBackendEventSinkListener listener)
            {
                listener.OnBatchCompleted(attemptId, EEventBatchResult.Committed);
                return true;
            }
        }
    }
}
