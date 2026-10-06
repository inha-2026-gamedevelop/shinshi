// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class PotionSessionEndingTests
    {
        // 출정 종료 직전 미적용 물약 되돌림의 저장 순서: 던전 호스트, 실제 물약 이벤트 경로(DungeonPotionEventLedger), 물약 판정을
        // 묶어 종료 이벤트(SessionEnded)보다 되돌림이 앞 순번으로 나가고 종료 뒤에는 물약 이벤트가 나가지 않는지 본다

        /****************************************
        *                Fields
        ****************************************/

        private const int    REGISTRY_CAPACITY = 16;
        private const string KEY_ONE           = "p:0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string BINDING           = "Account";
        private const int    SMALL_RESERVED    = 2;
        private const float  MAX_HEALTH        = 1500f;
        private const float  LOW_HEALTH        = 500f;
        private const uint   INPUT_SEQUENCE    = 7;
        private const double FLUSH_SECONDS     = 5.0;

        private SessionConfigSO       _config;
        private PlayerConfigSO        _player;
        private LifeConfigSO          _life;
        private ServerSessionRegistry _registry;
        private RecordingSink         _sink;
        private DungeonSessionHost    _host;
        private FakePotionHost        _potionHost;
        private PotionServer          _potions;
        private int                   _participantId;
        private double                _now;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config     = ScriptableObject.CreateInstance<SessionConfigSO>();
            _player     = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _life       = ScriptableObject.CreateInstance<LifeConfigSO>();
            _registry   = new ServerSessionRegistry(REGISTRY_CAPACITY);
            _sink       = new RecordingSink();
            _host       = new DungeonSessionHost(_config, _registry, null, true, null, _sink);
            _potionHost = new FakePotionHost();
            _potions    = new PotionServer(_player, _life, new DungeonPotionEventLedger(_host), _potionHost,
                Constants.Session.MAX_PARTY_SIZE);
            _host.SessionEndingListener = new PotionEndingListener(_potions);

            _participantId          = Admit(KEY_ONE);
            _potionHost.Participant = _participantId;
            _potionHost.User.ParticipantId = _participantId;
            Assert.IsTrue(_potions.TryRegister(_participantId, KEY_ONE, new[]
            {
                new ReservationPotion(_life.GetPotion(EPotionSize.Small).DefinitionId, BINDING, SMALL_RESERVED)
            }));

            // 첫 틱이 준비 카운트다운을 시작하고, 카운트다운이 끝난 틱에서 세션을 연다(DungeonSessionHostTests와 같은 순서)
            Assert.IsFalse(_host.BeginTick(0.0));
            _now = _config.ReadyCountdownSeconds;
            Assert.IsTrue(_host.BeginTick(_now));
            _host.CompleteTick();
            Assert.IsTrue(_host.IsSessionRunning);
        }

        [TearDown]
        public void TearDown()
        {
            _host.Dispose();
            Object.DestroyImmediate(_config);
            Object.DestroyImmediate(_player);
            Object.DestroyImmediate(_life);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void SessionEnd_UnsentPendingUse_RevertIsQueuedBeforeSessionEnded()
        {
            Assert.AreEqual(ERejectReason.None, _potions.TryUse(_potionHost.User, EPotionSize.Small, INPUT_SEQUENCE, _now));

            EndSessionAndFlush();

            AssertRevertBeforeEnd();
            Assert.IsFalse(_potions.HasPending(_participantId));
            Assert.AreEqual(ERejectReason.SessionTerminal, _potionHost.LastReason);
            Assert.AreEqual(INPUT_SEQUENCE, _potionHost.LastSequence);
        }

        [Test]
        public void SessionEnd_CommittedButUnappliedUse_RevertsInsteadOfConsuming()
        {
            Assert.AreEqual(ERejectReason.None, _potions.TryUse(_potionHost.User, EPotionSize.Small, INPUT_SEQUENCE, _now));
            Tick(_now + FLUSH_SECONDS);
            Assert.AreEqual(1, _sink.Count(ESessionEventType.PotionUsed), "사용은 종료 전에 이미 확정됐다");

            EndSessionAndFlush();
            _potions.Tick(_now);

            AssertRevertBeforeEnd();
            Assert.AreEqual(0, _potionHost.User.HealCalls, "종료된 출정에서는 회복하지 않는다");
            Assert.AreEqual(0, _potions.GetBossUses(_participantId), "되돌린 사용은 세지 않는다");
            Assert.AreEqual(SMALL_RESERVED, _potions.GetAvailable(_participantId, EPotionSize.Small), "소비로 남기지 않는다");
        }

        [Test]
        public void SessionEnd_NoPendingUse_QueuesNoPotionEvent()
        {
            EndSessionAndFlush();

            Assert.AreEqual(0, _sink.Count(ESessionEventType.PotionUseReverted));
            Assert.AreEqual(1, _sink.Count(ESessionEventType.SessionEnded));
        }

        /****************************************
        *               Helpers
        ****************************************/

        private void EndSessionAndFlush()
        {
            _now += 1.0;
            Assert.IsTrue(_host.Session.TryCompleteBossForDevelopment(_now));
            Assert.IsFalse(_host.IsSessionRunning);
            Tick(_now + FLUSH_SECONDS);
            Tick(_now + (FLUSH_SECONDS * 2.0));
        }

        private void AssertRevertBeforeEnd()
        {
            int used     = _sink.IndexOf(ESessionEventType.PotionUsed);
            int reverted = _sink.IndexOf(ESessionEventType.PotionUseReverted);
            int ended    = _sink.IndexOf(ESessionEventType.SessionEnded);
            Assert.GreaterOrEqual(used, 0);
            Assert.Greater(reverted, used);
            Assert.Greater(ended, reverted, "되돌림이 종료 이벤트보다 앞 순번이어야 API가 받는다");
            Assert.AreEqual(_sink.At(used).Seq, _sink.At(reverted).Data.RevertsSequence);
            Assert.AreEqual(ended, _sink.Total - 1, "종료 이벤트 뒤에는 아무것도 보내지 않는다");
        }

        private int Admit(string key)
        {
            var           claims = new JoinTokenClaims(key, null, null, null);
            ERejectReason reason = _registry.TryAdmit(in claims, out SessionBinding binding);
            Assert.AreEqual(ERejectReason.None, reason);
            Assert.AreEqual(ERejectReason.None, _host.Admit(in claims, binding));
            return _host.Roster.GetAt(_host.Roster.Count - 1).ParticipantId;
        }

        private void Tick(double serverTime)
        {
            _now = serverTime;
            if (_host.BeginTick(serverTime))
            {
                _host.CompleteTick();
            }
        }

        private sealed class PotionEndingListener : ISessionEndingListener
        {
            private readonly PotionServer _potions;

            public PotionEndingListener(PotionServer potions) { _potions = potions; }

            public void OnSessionEnding(double serverTime) { _potions.RevertUnapplied(serverTime); }
        }

        private sealed class FakePotionHost : IPotionHost
        {
            public FakeUser      User         { get; } = new FakeUser();
            public int           Participant  { get; set; }
            public uint          LastSequence { get; private set; }
            public ERejectReason LastReason   { get; private set; }

            public bool TryGetPotionUser(int participantId, out IPotionUser user)
            {
                user = (participantId == Participant) ? User : null;
                return user != null;
            }

            public void OnPotionState(int participantId, uint sequence, ERejectReason reason)
            {
                LastSequence = sequence;
                LastReason   = reason;
            }
        }

        private sealed class FakeUser : IPotionUser
        {
            public int                   ParticipantId   { get; set; }
            public EParticipantLifeState LifeState       => EParticipantLifeState.Alive;
            public float                 Health          { get; private set; } = LOW_HEALTH;
            public float                 MaxHealth       => MAX_HEALTH;
            public bool                  IsControlLocked => false;
            public int                   HealCalls       { get; private set; }

            public void Heal(float amount)
            {
                ++HealCalls;
                Health = Mathf.Min(MaxHealth, Health + amount);
            }
        }

        private sealed class RecordingSink : IBackendEventSink
        {
            private readonly List<SessionEvent> _received = new List<SessionEvent>();

            public int Total => _received.Count;

            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count,
                IBackendEventSinkListener listener)
            {
                for (int i = 0; i < count; ++i)
                {
                    _received.Add(events[i]);
                }

                listener.OnBatchCompleted(attemptId, EEventBatchResult.Committed);
                return true;
            }

            public SessionEvent At(int index) { return _received[index]; }

            public int IndexOf(ESessionEventType type)
            {
                for (int i = 0; i < _received.Count; ++i)
                {
                    if (_received[i].Data.Type == type)
                    {
                        return i;
                    }
                }

                return -1;
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
