// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public sealed class BossParticipantSourceTests
    {
        // 보스 기믹의 참가자 창구가 전투 런타임의 참가자 목록과 출정 명단으로 답하는지 본다.
        // 쓰러진 사람과 재접속 유예 참가자는 명단에 남고 이탈이 확정된 참가자와 명단에 없는 번호는 빠진다

        /****************************************
        *                Fields
        ****************************************/

        private const int    PARTICIPANT_A = 1;
        private const int    PARTICIPANT_B = 2;
        private const int    UNLISTED      = 3;
        private const string KEY_A         = "participant-a";
        private const string KEY_B         = "participant-b";
        private const int    CAST_ID       = 77;
        private const double NOW           = 12.5;
        private const double REVIVE_TIME   = 20.0;
        private const float  REVIVE_RATE   = 0.5f;

        private static readonly Vector3 POSITION_A = new Vector3(1f, 0f, 2f);
        private static readonly Vector3 POSITION_B = new Vector3(-3f, 0f, 4f);

        private PlayerServerTestRig   _rig;
        private SessionConfigSO       _config;
        private ParticipantRoster     _roster;
        private PlayerServer          _playerA;
        private PlayerServer          _playerB;
        private BossParticipantSource _source;
        private List<int>             _ids;
        private int                   _remainingRevives;
        private double                _clock;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _rig     = new PlayerServerTestRig();
            _config  = ScriptableObject.CreateInstance<SessionConfigSO>();
            _roster  = new ParticipantRoster(_config);
            _playerA = _rig.Add(PARTICIPANT_A, POSITION_A);
            _playerB = _rig.Add(PARTICIPANT_B, POSITION_B);
            Assert.IsTrue(_roster.TryAdd(PARTICIPANT_A, KEY_A));
            Assert.IsTrue(_roster.TryAdd(PARTICIPANT_B, KEY_B));
            _roster.Seal();

            _remainingRevives = 1;
            _clock            = NOW;
            _ids              = new List<int>();
            _source           = new BossParticipantSource(_rig.Players, _roster, () => _remainingRevives, () => _clock);
        }

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
            UnityEngine.Object.DestroyImmediate(_config);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Constructor_NullArgument_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new BossParticipantSource(null, _roster, () => 0, () => 0.0));
            Assert.Throws<ArgumentNullException>(() => new BossParticipantSource(_rig.Players, null, () => 0, () => 0.0));
            Assert.Throws<ArgumentNullException>(() => new BossParticipantSource(_rig.Players, _roster, null, () => 0.0));
            Assert.Throws<ArgumentNullException>(() => new BossParticipantSource(_rig.Players, _roster, () => 0, null));
        }

        [Test]
        public void CollectParticipants_ConnectedPlayers_AppendsCombatIds()
        {
            int count = _source.CollectParticipants(_ids);

            Assert.AreEqual(2, count);
            CollectionAssert.AreEqual(new[] { _playerA.CombatId, _playerB.CombatId }, _ids);
        }

        [Test]
        public void CollectParticipants_DeadAndReconnectGrace_AreIncluded()
        {
            _playerA.ApplyDamage(_playerA.MaxHealth, true, NOW);
            Assert.IsTrue(_roster.TryDisconnect(PARTICIPANT_B, NOW));

            Assert.AreEqual(2, _source.CollectParticipants(_ids));

            Assert.IsTrue(_source.TryGetParticipant(_playerA.CombatId, out BossParticipantInfo dead));
            Assert.AreEqual(EParticipantLifeState.Dead, dead.LifeState);
            Assert.IsTrue(dead.IsConnected);

            Assert.IsTrue(_source.TryGetParticipant(_playerB.CombatId, out BossParticipantInfo grace));
            Assert.AreEqual(EParticipantLifeState.Alive, grace.LifeState);
            Assert.IsFalse(grace.IsConnected, "재접속 유예 중인 생존자는 접속 중이 아니다");
            Assert.AreEqual(POSITION_B, grace.Position);
        }

        [Test]
        public void CollectParticipants_Departed_IsExcluded()
        {
            Assert.IsTrue(_roster.TryClose(PARTICIPANT_B, ESessionEndReason.Abandon, NOW));

            Assert.AreEqual(1, _source.CollectParticipants(_ids));

            CollectionAssert.AreEqual(new[] { _playerA.CombatId }, _ids);
            Assert.IsFalse(_source.TryGetParticipant(_playerB.CombatId, out _));
            Assert.IsFalse(_source.TryGetMaxHealth(_playerB.CombatId, out _));
            Assert.IsFalse(_source.TryKill(_playerB.CombatId, CAST_ID, EWolbaekFinalDeath.Fall));
            Assert.AreEqual(EParticipantLifeState.Alive, _playerB.LifeState);
        }

        [Test]
        public void CollectParticipants_PlayerNotOnRoster_IsExcluded()
        {
            PlayerServer stranger = _rig.Add(UNLISTED, Vector3.zero);

            Assert.AreEqual(2, _source.CollectParticipants(_ids));

            Assert.IsFalse(_source.TryGetParticipant(stranger.CombatId, out _));
        }

        [Test]
        public void CollectParticipants_PlayerLeftList_IsReflectedWithoutCopy()
        {
            _rig.Players.Remove(_playerB);

            Assert.AreEqual(1, _source.CollectParticipants(_ids));
            Assert.IsFalse(_source.TryGetParticipant(_playerB.CombatId, out _));

            _rig.Players.Add(_playerB);
        }

        [Test]
        public void TryGetParticipant_UnknownOrNone_ReturnsFalse()
        {
            Assert.IsFalse(_source.TryGetParticipant(CombatIds.NONE, out _));
            Assert.IsFalse(_source.TryGetParticipant(_playerB.CombatId + 100, out _));
        }

        [Test]
        public void TryGetParticipant_IsRevivable_FollowsRemainingRevives()
        {
            Assert.IsTrue(_source.TryGetParticipant(_playerA.CombatId, out BossParticipantInfo withRevives));
            Assert.IsTrue(withRevives.IsRevivable);

            _remainingRevives = 0;

            Assert.IsTrue(_source.TryGetParticipant(_playerA.CombatId, out BossParticipantInfo withoutRevives));
            Assert.IsFalse(withoutRevives.IsRevivable);
            Assert.AreEqual(POSITION_A, withoutRevives.Position);
        }

        [Test]
        public void TryGetMaxHealth_KnownParticipant_ReturnsServerMaxHealth()
        {
            Assert.IsTrue(_source.TryGetMaxHealth(_playerA.CombatId, out float maxHealth));

            Assert.AreEqual(_playerA.MaxHealth, maxHealth);
            Assert.IsFalse(_source.TryGetMaxHealth(CombatIds.NONE, out _));
        }

        [Test]
        public void TryGetLastReviveServerTime_NeverRevived_ReturnsFalse()
        {
            Assert.IsFalse(_source.TryGetLastReviveServerTime(_playerA.CombatId, out _));
        }

        [Test]
        public void TryGetLastReviveServerTime_AfterRevive_ReturnsApprovedTime()
        {
            _playerA.ApplyDamage(_playerA.MaxHealth, true, NOW);
            _playerA.Revive(POSITION_A, REVIVE_RATE, REVIVE_TIME);

            Assert.IsTrue(_source.TryGetLastReviveServerTime(_playerA.CombatId, out double revivedAt));

            Assert.AreEqual(REVIVE_TIME, revivedAt);
        }

        [Test]
        public void TryKill_Alive_KillsAtClockTimeOnce()
        {
            Assert.IsTrue(_source.TryKill(_playerA.CombatId, CAST_ID, EWolbaekFinalDeath.Fall));

            Assert.AreEqual(EParticipantLifeState.Dead, _playerA.LifeState);
            Assert.AreEqual(NOW, _playerA.DeathTime, "사망 시각은 시계가 준 이번 틱의 시각이다");
            Assert.IsFalse(_source.TryKill(_playerA.CombatId, CAST_ID, EWolbaekFinalDeath.DoubleBlast), "이미 쓰러졌으면 거짓이다");
            Assert.AreEqual(EParticipantLifeState.Alive, _playerB.LifeState);
        }

        [Test]
        public void TryKill_ReconnectGrace_StillKills()
        {
            Assert.IsTrue(_roster.TryDisconnect(PARTICIPANT_B, NOW));

            Assert.IsTrue(_source.TryKill(_playerB.CombatId, CAST_ID, EWolbaekFinalDeath.Fall));

            Assert.AreEqual(EParticipantLifeState.Dead, _playerB.LifeState);
        }

        [Test]
        public void TryKill_Protected_StillKills()
        {
            _source.GrantVictoryProtection(CAST_ID, NOW);

            Assert.IsTrue(_source.TryKill(_playerA.CombatId, CAST_ID, EWolbaekFinalDeath.Fall));
        }

        [Test]
        public void GrantVictoryProtection_ProtectsEveryPlayerInList()
        {
            _playerA.ApplyDamage(_playerA.MaxHealth, true, NOW);

            _source.GrantVictoryProtection(CAST_ID, NOW);

            Assert.IsTrue(_playerA.IsInvulnerable);
            Assert.IsTrue(_playerB.IsInvulnerable);
        }

        [Test]
        public void Queries_PerTick_DoNotAllocate()
        {
            _source.CollectParticipants(_ids);
            _source.TryGetParticipant(_playerA.CombatId, out _);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 64; ++i)
            {
                _ids.Clear();
                _source.CollectParticipants(_ids);
                _source.TryGetParticipant(_playerB.CombatId, out _);
                _source.TryGetMaxHealth(_playerB.CombatId, out _);
                _source.TryGetLastReviveServerTime(_playerB.CombatId, out _);
            }

            Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }
    }
}
