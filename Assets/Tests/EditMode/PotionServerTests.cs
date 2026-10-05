// System
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;

namespace Shinshi.Tests
{
    public class PotionServerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    PARTICIPANT     = 3;
        private const string PARTICIPANT_KEY = "dungeon:participant-3";
        private const string BINDING         = "Account";
        private const int    CAPACITY        = 4;
        private const float  MAX_HEALTH      = 1500f;
        private const float  LOW_HEALTH      = 500f;
        private const double START_TIME      = 100.0;
        private const float  TOLERANCE       = 0.001f;
        private const int    MANY_POTIONS    = 20;
        private const uint   INPUT_SEQUENCE  = 41;

        private PlayerConfigSO  _player;
        private LifeConfigSO    _life;
        private FakeLedger      _ledger;
        private FakeHost        _host;
        private FakeUser        _user;
        private PotionServer    _potions;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _player  = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _life    = ScriptableObject.CreateInstance<LifeConfigSO>();
            _ledger  = new FakeLedger();
            _user    = new FakeUser { Health = LOW_HEALTH };
            _host    = new FakeHost(_user);
            _potions = new PotionServer(_player, _life, _ledger, _host, CAPACITY);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_player);
            Object.DestroyImmediate(_life);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryUse_NotRunning_RejectsWrongPhaseWithoutEvent()
        {
            Register(2, 0);
            _ledger.IsSessionRunning = false;

            AssertRejected(EPotionSize.Small, ERejectReason.WrongPhase);
        }

        [Test]
        public void TryUse_DeadWithEveryOtherBlock_RejectsDeadFirst()
        {
            Register(0, 0);
            _user.LifeState       = EParticipantLifeState.Dead;
            _user.IsControlLocked = true;
            _user.Health          = MAX_HEALTH;

            AssertRejected(EPotionSize.Small, ERejectReason.Dead);
        }

        [Test]
        public void TryUse_ControlLockedAndFullHealth_RejectsDisabledFirst()
        {
            Register(0, 0);
            _user.IsControlLocked = true;
            _user.Health          = MAX_HEALTH;

            AssertRejected(EPotionSize.Small, ERejectReason.Disabled);
        }

        [Test]
        public void TryUse_FullHealthDuringCooldown_RejectsFullHealthFirst()
        {
            Register(2, 0);
            UseAndCommit(EPotionSize.Small, START_TIME);
            _user.Health = MAX_HEALTH;

            AssertRejected(EPotionSize.Small, ERejectReason.FullHealth, START_TIME + 1.0);
        }

        [Test]
        public void TryUse_CooldownWithoutStock_RejectsCooldownFirst()
        {
            Register(1, 0);
            UseAndCommit(EPotionSize.Small, START_TIME);

            AssertRejected(EPotionSize.Small, ERejectReason.Cooldown, START_TIME + _player.PotionSharedCooldown - 0.1);
        }

        [Test]
        public void TryUse_NoStock_RejectsPotionLimitBeforeApi()
        {
            Register(0, 3);

            AssertRejected(EPotionSize.Small, ERejectReason.PotionLimit);
        }

        [Test]
        public void TryUse_Pending_RejectsPendingCommit()
        {
            Register(2, 2);
            Assert.AreEqual(ERejectReason.None, _potions.TryUse(_user, EPotionSize.Small, INPUT_SEQUENCE, START_TIME));

            AssertRejected(EPotionSize.Large, ERejectReason.PendingCommit, START_TIME, 1);
        }

        [Test]
        public void TryUse_BossNinthUseCommitted_BlocksTenthBeforeApi()
        {
            _ledger.IsBossSession = true;
            Register(MANY_POTIONS, 0);
            double now = START_TIME;
            for (int i = 0; i < GameSessionCodes.Potion.BOSS_USE_LIMIT; ++i)
            {
                _user.Health = LOW_HEALTH;
                UseAndCommit(EPotionSize.Small, now);
                now += _player.PotionSharedCooldown;
            }

            _user.Health = LOW_HEALTH;
            AssertRejected(EPotionSize.Small, ERejectReason.PotionLimit, now, GameSessionCodes.Potion.BOSS_USE_LIMIT);
            Assert.AreEqual(GameSessionCodes.Potion.BOSS_USE_LIMIT, _potions.GetBossUses(PARTICIPANT));
        }

        [Test]
        public void TryUse_NormalSession_AllowsMoreThanBossLimit()
        {
            Register(MANY_POTIONS, 0);
            double now = START_TIME;
            for (int i = 0; i <= GameSessionCodes.Potion.BOSS_USE_LIMIT; ++i)
            {
                _user.Health = LOW_HEALTH;
                UseAndCommit(EPotionSize.Small, now);
                now += _player.PotionSharedCooldown;
            }

            Assert.AreEqual(MANY_POTIONS - GameSessionCodes.Potion.BOSS_USE_LIMIT - 1, _potions.GetAvailable(PARTICIPANT, EPotionSize.Small));
        }

        [Test]
        public void Tick_Committed_HealsOnceAndStartsCooldown()
        {
            Register(2, 0);
            Assert.AreEqual(ERejectReason.None, _potions.TryUse(_user, EPotionSize.Small, INPUT_SEQUENCE, START_TIME));
            _potions.Tick(START_TIME + 0.1);
            Assert.AreEqual(LOW_HEALTH, _user.Health, "확정 전에는 회복하지 않는다");

            _ledger.CommitAll();
            _potions.Tick(START_TIME + 0.2);
            _potions.Tick(START_TIME + 0.3);

            Assert.AreEqual(LOW_HEALTH + (MAX_HEALTH * _life.GetPotion(EPotionSize.Small).HealRate), _user.Health, TOLERANCE);
            Assert.AreEqual(1, _user.HealCalls);
            Assert.AreEqual(START_TIME + 0.2 + _player.PotionSharedCooldown, _potions.GetCooldownEndsAt(PARTICIPANT), TOLERANCE);
            Assert.AreEqual(1, _potions.GetAvailable(PARTICIPANT, EPotionSize.Small));
            Assert.AreEqual(INPUT_SEQUENCE, _host.LastSequence);
            Assert.AreEqual(ERejectReason.None, _host.LastReason);
        }

        [Test]
        public void Tick_LargePotion_HealsHalfOfMaxHealthWithoutOverflow()
        {
            Register(0, 1);
            _user.Health = MAX_HEALTH - 100f;
            UseAndCommit(EPotionSize.Large, START_TIME);

            Assert.AreEqual(MAX_HEALTH, _user.Health, TOLERANCE);
        }

        [Test]
        public void Tick_DiedBeforeCommit_RevertsAndHoldsStockUntilRevertCommits()
        {
            Register(1, 0);
            Assert.AreEqual(ERejectReason.None, _potions.TryUse(_user, EPotionSize.Small, INPUT_SEQUENCE, START_TIME));
            long useSequence = _ledger.LastSequence;
            _user.LifeState = EParticipantLifeState.Dead;

            _ledger.CommitAll();
            _potions.Tick(START_TIME + 0.2);

            Assert.AreEqual(0, _user.HealCalls);
            Assert.AreEqual(1, _ledger.Reverts.Count);
            Assert.AreEqual(useSequence, _ledger.Reverts[0]);
            Assert.AreEqual(0, _potions.GetAvailable(PARTICIPANT, EPotionSize.Small), "되돌림 확정 전에는 다시 쓰지 못한다");
            Assert.AreEqual(0.0, _potions.GetCooldownEndsAt(PARTICIPANT));

            _ledger.CommitAll();
            _potions.Tick(START_TIME + 0.3);

            Assert.AreEqual(1, _potions.GetAvailable(PARTICIPANT, EPotionSize.Small));
            Assert.AreEqual(0, _potions.GetBossUses(PARTICIPANT));
        }

        [Test]
        public void Tick_SessionEndedBeforeCommit_KeepsUseWithoutRevert()
        {
            Register(1, 0);
            Assert.AreEqual(ERejectReason.None, _potions.TryUse(_user, EPotionSize.Small, INPUT_SEQUENCE, START_TIME));
            _ledger.IsSessionRunning = false;
            _ledger.CommitAll();

            LogAssert.Expect(LogType.Warning, new Regex("회복도 되돌림도"));
            _potions.Tick(START_TIME + 0.2);

            Assert.AreEqual(0, _ledger.Reverts.Count);
            Assert.AreEqual(0, _user.HealCalls);
            Assert.AreEqual(0, _potions.GetAvailable(PARTICIPANT, EPotionSize.Small));
        }

        [Test]
        public void RevertUnapplied_PendingUse_QueuesRevertAndClearsPending()
        {
            Register(1, 0);
            Assert.AreEqual(ERejectReason.None, _potions.TryUse(_user, EPotionSize.Small, INPUT_SEQUENCE, START_TIME));
            long useSequence = _ledger.LastSequence;

            _potions.RevertUnapplied(START_TIME + 0.1);

            Assert.AreEqual(1, _ledger.Reverts.Count);
            Assert.AreEqual(useSequence, _ledger.Reverts[0]);
            Assert.Greater(_ledger.LastSequence, useSequence);
            Assert.IsFalse(_potions.HasPending(PARTICIPANT));
            Assert.AreEqual(ERejectReason.SessionTerminal, _host.LastReason);

            _ledger.IsSessionRunning = false;
            _ledger.CommitAll();
            _potions.Tick(START_TIME + 0.2);

            Assert.AreEqual(0, _user.HealCalls);
            Assert.AreEqual(1, _ledger.Reverts.Count, "종료 뒤 틱은 다시 되돌리지 않는다");
            Assert.AreEqual(1, _potions.GetAvailable(PARTICIPANT, EPotionSize.Small));
        }

        [Test]
        public void RevertUnapplied_QueueRejects_KeepsPendingWithWarning()
        {
            Register(1, 0);
            Assert.AreEqual(ERejectReason.None, _potions.TryUse(_user, EPotionSize.Small, INPUT_SEQUENCE, START_TIME));
            _ledger.AcceptsEvents = false;

            LogAssert.Expect(LogType.Warning, new Regex("되돌리지 못했다"));
            _potions.RevertUnapplied(START_TIME + 0.1);

            Assert.AreEqual(0, _ledger.Reverts.Count);
            Assert.IsTrue(_potions.HasPending(PARTICIPANT));
        }

        [Test]
        public void RevertUnapplied_NoPending_QueuesNothing()
        {
            Register(1, 0);

            _potions.RevertUnapplied(START_TIME);

            Assert.AreEqual(0, _ledger.Reverts.Count);
            Assert.AreEqual(0, _ledger.Uses.Count);
        }

        [Test]
        public void TryUse_QueueRejects_ReportsUnavailableWithoutPending()
        {
            Register(1, 0);
            _ledger.AcceptsEvents = false;

            Assert.AreEqual(ERejectReason.BackendUnavailable, _potions.TryUse(_user, EPotionSize.Small, INPUT_SEQUENCE, START_TIME));
            Assert.IsFalse(_potions.HasPending(PARTICIPANT));
        }

        [Test]
        public void TryUse_Accepted_RecordsReservationBinding()
        {
            Register(1, 0);

            Assert.AreEqual(ERejectReason.None, _potions.TryUse(_user, EPotionSize.Small, INPUT_SEQUENCE, START_TIME));

            Assert.AreEqual(PARTICIPANT_KEY, _ledger.LastKey);
            Assert.AreEqual(_life.GetPotion(EPotionSize.Small).DefinitionId, _ledger.LastDefinition);
            Assert.AreEqual(BINDING, _ledger.LastBinding);
        }

        [Test]
        public void TryRegister_SecondEntry_KeepsFirstStock()
        {
            Register(3, 0);

            Assert.IsTrue(_potions.TryRegister(PARTICIPANT, PARTICIPANT_KEY, Reserved(9, 9)));

            Assert.AreEqual(3, _potions.GetAvailable(PARTICIPANT, EPotionSize.Small));
            Assert.AreEqual(0, _potions.GetAvailable(PARTICIPANT, EPotionSize.Large));
        }

        [Test]
        public void TryRegister_UnknownPotion_IgnoredWithWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("쓰지 않는 지참 예약"));

            Assert.IsTrue(_potions.TryRegister(PARTICIPANT, PARTICIPANT_KEY,
                new[] { new ReservationPotion("item_unknown", BINDING, 5) }));

            Assert.AreEqual(0, _potions.GetAvailable(PARTICIPANT, EPotionSize.Small));
        }

        [Test]
        public void CreateState_BossSession_CarriesRemainingUsesAndCooldown()
        {
            _ledger.IsBossSession = true;
            Register(4, 2);
            UseAndCommit(EPotionSize.Small, START_TIME);

            PotionStateMessage state = _potions.CreateState(PARTICIPANT, INPUT_SEQUENCE, ERejectReason.None);

            Assert.AreEqual(3, state.SmallRemaining);
            Assert.AreEqual(2, state.LargeRemaining);
            Assert.AreEqual(1, state.BossUsed);
            Assert.AreEqual(GameSessionCodes.Potion.BOSS_USE_LIMIT, state.BossLimit);
            Assert.IsFalse(state.IsPending);
            Assert.Greater(state.CooldownEndsAt, START_TIME);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void Register(int small, int large)
        {
            Assert.IsTrue(_potions.TryRegister(PARTICIPANT, PARTICIPANT_KEY, Reserved(small, large)));
        }

        private ReservationPotion[] Reserved(int small, int large)
        {
            var list = new List<ReservationPotion>();
            if (small > 0)
            {
                list.Add(new ReservationPotion(_life.GetPotion(EPotionSize.Small).DefinitionId, BINDING, small));
            }

            if (large > 0)
            {
                list.Add(new ReservationPotion(_life.GetPotion(EPotionSize.Large).DefinitionId, BINDING, large));
            }

            return list.ToArray();
        }

        private void UseAndCommit(EPotionSize size, double now)
        {
            Assert.AreEqual(ERejectReason.None, _potions.TryUse(_user, size, INPUT_SEQUENCE, now));
            _ledger.CommitAll();
            _potions.Tick(now);
        }

        private void AssertRejected(EPotionSize size, ERejectReason expected, double now = START_TIME, int expectedUses = -1)
        {
            int events = _ledger.Uses.Count;
            Assert.AreEqual(expected, _potions.TryUse(_user, size, INPUT_SEQUENCE, now));
            Assert.AreEqual(events, _ledger.Uses.Count, "거절은 사용 이벤트를 넣지 않는다");
            if (expectedUses >= 0)
            {
                Assert.AreEqual(expectedUses, _ledger.Uses.Count);
            }
        }

        private sealed class FakeLedger : IPotionEventLedger
        {
            private long _committed = 0;

            public bool       IsSessionRunning { get; set; } = true;
            public bool       IsBossSession    { get; set; }
            public bool       IsStorageStopped { get; set; }
            public bool       AcceptsEvents    { get; set; } = true;
            public long       LastSequence     { get; private set; }
            public string     LastKey          { get; private set; }
            public string     LastDefinition   { get; private set; }
            public string     LastBinding      { get; private set; }
            public List<long> Uses             { get; } = new List<long>();
            public List<long> Reverts          { get; } = new List<long>();

            public bool TryRecordUse(string participantKey, string definitionId, string bindingKind, double serverTime,
                out long sequence)
            {
                sequence = 0;
                if (!AcceptsEvents)
                {
                    return false;
                }

                sequence       = ++LastSequence;
                LastKey        = participantKey;
                LastDefinition = definitionId;
                LastBinding    = bindingKind;
                Uses.Add(sequence);
                return true;
            }

            public bool TryRecordRevert(string participantKey, long useSequence, double serverTime, out long sequence)
            {
                sequence = 0;
                if (!AcceptsEvents)
                {
                    return false;
                }

                sequence = ++LastSequence;
                Reverts.Add(useSequence);
                return true;
            }

            public bool IsCommitted(long sequence) { return (sequence > 0) && (sequence <= _committed); }

            public void CommitAll() { _committed = LastSequence; }
        }

        private sealed class FakeHost : IPotionHost
        {
            private readonly FakeUser _user;

            public uint          LastSequence { get; private set; }
            public ERejectReason LastReason   { get; private set; }

            public FakeHost(FakeUser user) { _user = user; }

            public bool TryGetPotionUser(int participantId, out IPotionUser user)
            {
                user = (participantId == PARTICIPANT) ? _user : null;
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
            public int                   ParticipantId   => PARTICIPANT;
            public EParticipantLifeState LifeState       { get; set; } = EParticipantLifeState.Alive;
            public float                 Health          { get; set; }
            public float                 MaxHealth       => MAX_HEALTH;
            public bool                  IsControlLocked { get; set; }
            public int                   HealCalls       { get; private set; }

            public void Heal(float amount)
            {
                ++HealCalls;
                Health = Mathf.Min(MaxHealth, Health + amount);
            }
        }
    }
}
