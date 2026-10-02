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
    public class DungeonSessionHostTests
    {
        // 연결이 끊긴 캐릭터를 서버에 남기는 Mirror 부분은 EditMode에서 돌릴 수 없어, 그 경로가 부르는 명단 전이를 본다

        /****************************************
        *                Fields
        ****************************************/

        private const int    REGISTRY_CAPACITY = 16;
        private const string SORTIE_ID         = "6f1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string OTHER_SORTIE_ID   = "6f1c2d3e-4a5b-4c6d-8e7f-001122334466";
        private const string KEY_ONE           = "p:0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string KEY_TWO           = "p:0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc02";
        private const string KEY_THREE         = "p:0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc03";

        private SessionConfigSO       _config;
        private ServerSessionRegistry _registry;
        private RecordingSink         _sink;
        private DungeonSessionHost    _host;
        private List<string>          _warnings;
        private List<int>             _closed;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config   = ScriptableObject.CreateInstance<SessionConfigSO>();
            _registry = new ServerSessionRegistry(REGISTRY_CAPACITY);
            _sink     = new RecordingSink();
            _warnings = new List<string>();
            _closed   = new List<int>();
            _host     = new DungeonSessionHost(_config, _registry, null, true, _warnings.Add, _sink);
            _host.Roster.ParticipantClosed += OnParticipantClosed;
        }

        [TearDown]
        public void TearDown()
        {
            _host.Roster.ParticipantClosed -= OnParticipantClosed;
            _host.Dispose();
            Object.DestroyImmediate(_config);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void BeginTick_AfterReadyCountdown_StartsWithFixedStartingCount()
        {
            Admit(KEY_ONE);
            Admit(KEY_TWO);

            Assert.IsFalse(_host.BeginTick(0.0), "준비 시간 전에는 세션을 열지 않는다");
            Assert.IsFalse(_host.HasStarted);
            Assert.IsTrue(_host.BeginTick(_config.ReadyCountdownSeconds));
            _host.CompleteTick();

            Assert.IsTrue(_host.IsSessionRunning);
            Assert.AreEqual(2, _host.Session.StartingCount);
            Assert.AreEqual(_config.GetEnemyHealthScale(2), _host.Session.EnemyHealthScale);
            Assert.AreEqual(_config.GetDamageScale(2), _host.Session.DamageScale);
            Assert.AreEqual(ERejectReason.SessionMismatch, AdmitReason(KEY_THREE), "시작 뒤 새 참가자는 받지 않는다");

            // 저장은 1초 단위 배치라 다음 주기에 나간다
            Tick(_config.ReadyCountdownSeconds + Constants.Session.EVENT_BATCH_INTERVAL_SECONDS);
            Assert.AreEqual(2, _sink.Count(ESessionEventType.ParticipantJoined));
            Assert.AreEqual(2, _host.Session.StartingCount);
        }

        [Test]
        public void Admit_DifferentSortie_RejectsSecondSession()
        {
            Assert.AreEqual(ERejectReason.None, AdmitReason(KEY_ONE, SORTIE_ID));

            Assert.AreEqual(ERejectReason.SessionMismatch, AdmitReason(KEY_TWO, OTHER_SORTIE_ID));
            Assert.AreEqual(SORTIE_ID, _host.SessionId);
            Assert.AreEqual(1, _host.Roster.Count);
        }

        [Test]
        public void GraceHit_DetachedParticipantDies_KeepsGraceAndRewardEligibility()
        {
            int one = Admit(KEY_ONE);
            int two = Admit(KEY_TWO);
            StartSession();
            double detachTime = _config.ReadyCountdownSeconds + 1.0;
            Tick(detachTime);

            Assert.IsTrue(_host.CanRetain(one));
            Assert.IsTrue(_host.OnParticipantDetached(one, detachTime));
            Tick(detachTime + 1.0);
            _host.ReportLifeState(one, EParticipantLifeState.Dead);
            Tick(detachTime + 2.0);

            Assert.IsTrue(_host.Roster.TryGet(one, out ParticipantSnapshot snapshot));
            Assert.AreEqual(EParticipantConnectionState.ReconnectGrace, snapshot.ConnectionState);
            Assert.AreEqual(EParticipantLifeState.Dead, snapshot.LifeState);
            Assert.AreEqual(detachTime + _config.ReconnectGraceSeconds, snapshot.GraceUntil);
            Assert.IsTrue(_host.Roster.IsRewardEligible(one, detachTime + 2.0), "사망해도 유예 안이면 보상 자격이 있다");
            Assert.IsTrue(_host.CanRetain(one), "유예 중에는 캐릭터를 계속 남긴다");
            Assert.IsTrue(_host.IsSessionRunning);
            Assert.AreEqual(0, _closed.Count);
            Assert.AreEqual(1, _sink.Count(ESessionEventType.ParticipantLeft));
            Assert.IsFalse(_registry.IsClosed(one));
            Assert.IsFalse(_registry.IsClosed(two));
        }

        [Test]
        public void GraceHit_ReattachInsideGrace_KeepsLifeStateWithoutRevive()
        {
            int one = Admit(KEY_ONE);
            Admit(KEY_TWO);
            StartSession();
            double detachTime = _config.ReadyCountdownSeconds + 1.0;
            Tick(detachTime);
            _host.OnParticipantDetached(one, detachTime);
            _host.ReportLifeState(one, EParticipantLifeState.Dead);
            Tick(detachTime + 1.0);

            Assert.IsTrue(_host.OnParticipantAttached(one, detachTime + 1.0));
            Tick(detachTime + 2.0);

            Assert.IsTrue(_host.Roster.TryGet(one, out ParticipantSnapshot snapshot));
            Assert.AreEqual(EParticipantConnectionState.Connected, snapshot.ConnectionState);
            Assert.AreEqual(EParticipantLifeState.Dead, snapshot.LifeState, "복귀는 부활이 아니다");
            Assert.AreEqual(3, _sink.Count(ESessionEventType.ParticipantJoined), "시작 참가 두 건 뒤 복귀 한 건이 더 저장된다");
        }

        [Test]
        public void BeginTick_GraceExpired_ClosesOnceSavesSettledAndClosesRegistry()
        {
            int one = Admit(KEY_ONE);
            Admit(KEY_TWO);
            StartSession();
            double detachTime = _config.ReadyCountdownSeconds + 1.0;
            Tick(detachTime);
            _host.OnParticipantDetached(one, detachTime);
            _host.ReportLifeState(one, EParticipantLifeState.Dead);

            double expiry = detachTime + _config.ReconnectGraceSeconds;
            Tick(expiry - 1.0);
            Assert.AreEqual(0, _closed.Count);
            Tick(expiry);
            Tick(expiry + 1.0);

            Assert.AreEqual(1, _closed.Count);
            Assert.AreEqual(one, _closed[0]);
            Assert.IsTrue(_host.Roster.TryGet(one, out ParticipantSnapshot snapshot));
            Assert.AreEqual(EParticipantConnectionState.Departed, snapshot.ConnectionState);
            Assert.AreEqual(ESessionEndReason.DisconnectExpired, snapshot.CloseReason);
            Assert.IsFalse(_host.CanRetain(one), "만료된 캐릭터는 남기지 않는다");
            Assert.IsFalse(_host.OnParticipantAttached(one, expiry + 1.0));
            Assert.IsTrue(_registry.IsClosed(one), "인증 명단도 닫혀 재합류를 막는다");
            Assert.AreEqual(1, _sink.Count(ESessionEventType.ParticipantSettled));
            Assert.AreEqual(ERejectReason.AlreadySettled, AdmitAgain(one, KEY_ONE));
            Assert.IsTrue(_host.IsSessionRunning, "남은 파티는 계속 진행한다");
        }

        [Test]
        public void BeginTick_LastParticipantExpires_EndsSessionAndSettlesOnce()
        {
            int one = Admit(KEY_ONE);
            StartSession();
            double detachTime = _config.ReadyCountdownSeconds + 1.0;
            Tick(detachTime);
            _host.OnParticipantDetached(one, detachTime);

            Tick(detachTime + _config.ReconnectGraceSeconds);
            Tick(detachTime + _config.ReconnectGraceSeconds + 1.0);

            Assert.IsTrue(_host.Session.IsEnded);
            Assert.AreEqual(ESessionEndReason.DisconnectExpired, _host.Session.EndReason);
            Assert.AreEqual(1, _sink.Count(ESessionEventType.SessionEnded));
            Assert.IsTrue(_host.Session.CanReturn);
            Assert.AreEqual(ESessionSettlementState.Settled, _host.Settlement.State);
            Assert.AreEqual(1, _sink.LastGrantCount, "종료 정산안은 원래 참가자 전원을 덮는다");
        }

        [Test]
        public void Tick_SettledSortie_ReportsTerminalForRecycleWithReturnGrace()
        {
            int one = Admit(KEY_ONE);
            Assert.IsFalse(_host.ClosesParticipantOnRetentionExpiry, "시작 전에는 보존 만료로 명단을 닫지 않는다");
            StartSession();
            Assert.IsTrue(_host.ClosesParticipantOnRetentionExpiry);
            Assert.AreEqual(ESortieTerminalReason.None, _host.TerminalReason);
            double detachTime = _config.ReadyCountdownSeconds + 1.0;
            Tick(detachTime);
            _host.OnParticipantDetached(one, detachTime);

            Tick(detachTime + _config.ReconnectGraceSeconds);
            Tick(detachTime + _config.ReconnectGraceSeconds + 1.0);

            Assert.AreEqual(ESortieTerminalReason.Settled, _host.TerminalReason);
            Assert.AreEqual(Constants.Network.RECYCLE_RETURN_GRACE_SECONDS, ServerShutdownController.GraceFor(_host.TerminalReason));
            Assert.AreEqual(ERejectReason.SessionMismatch, AdmitReason(KEY_TWO), "끝난 출정은 새 입장을 받지 않는다");
        }

        [Test]
        public void LobbyDetach_BeforeStart_MarksAbsentAndStartPutsThemInGrace()
        {
            int one = Admit(KEY_ONE);
            Admit(KEY_TWO);

            Assert.IsTrue(_host.OnParticipantDetached(one, 0.0));
            Assert.AreEqual(1, _host.LobbyAbsentCount);
            StartSession();

            Assert.AreEqual(0, _host.LobbyAbsentCount);
            Assert.IsTrue(_host.Roster.TryGet(one, out ParticipantSnapshot snapshot));
            Assert.AreEqual(EParticipantConnectionState.ReconnectGrace, snapshot.ConnectionState,
                "개발 출정은 기다리지 않고 시작하되, 끊긴 사람은 시작 시각부터 유예를 센다");
            Assert.AreEqual(_config.ReadyCountdownSeconds + _config.ReconnectGraceSeconds, snapshot.GraceUntil);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private int Admit(string key)
        {
            Assert.AreEqual(ERejectReason.None, AdmitReason(key));
            return _host.Roster.GetAt(_host.Roster.Count - 1).ParticipantId;
        }

        private ERejectReason AdmitReason(string key, string sortieId = null)
        {
            var           claims = new JoinTokenClaims(key, null, null, sortieId);
            ERejectReason reason = _registry.TryAdmit(in claims, out SessionBinding binding);
            if (reason != ERejectReason.None)
            {
                return reason;
            }

            reason = _host.Admit(in claims, binding);
            if (reason != ERejectReason.None)
            {
                _registry.Release(binding);
            }

            return reason;
        }

        // 연결을 끊은 뒤 같은 키로 다시 들어오는 순서를 흉내 낸다. 닫힌 참가자는 인증 명단에서 먼저 거절된다
        private ERejectReason AdmitAgain(int participantId, string key)
        {
            var           claims = new JoinTokenClaims(key);
            ERejectReason reason = _registry.TryAdmit(in claims, out SessionBinding binding);
            if (reason != ERejectReason.None)
            {
                return reason;
            }

            return _host.Admit(in claims, binding);
        }

        private void StartSession()
        {
            Assert.IsFalse(_host.BeginTick(0.0));
            Assert.IsTrue(_host.BeginTick(_config.ReadyCountdownSeconds));
            _host.CompleteTick();
        }

        private void Tick(double serverTime)
        {
            if (_host.BeginTick(serverTime))
            {
                _host.CompleteTick();
            }
        }

        private void OnParticipantClosed(int participantId, ESessionEndReason reason, double serverTime)
        {
            _closed.Add(participantId);
        }

        private sealed class RecordingSink : IBackendEventSink
        {
            private readonly List<SessionEvent> _received = new List<SessionEvent>();

            public int LastGrantCount { get; private set; } = -1;

            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
                IBackendEventSinkListener listener)
            {
                for (int i = 0; i < count; ++i)
                {
                    _received.Add(events[i]);
                    if (events[i].Data.Type == ESessionEventType.SessionEnded)
                    {
                        LastGrantCount = (events[i].Data.Grants != null) ? events[i].Data.Grants.Length : 0;
                    }
                }

                listener.OnBatchCompleted(attemptId, EEventBatchResult.Committed);
                return true;
            }

            public int Count(ESessionEventType type)
            {
                int count = 0;
                for (int i = 0; i < _received.Count; ++i)
                {
                    if (_received[i].Data.Type == type)
                    {
                        ++count;
                    }
                }

                return count;
            }
        }
    }
}
