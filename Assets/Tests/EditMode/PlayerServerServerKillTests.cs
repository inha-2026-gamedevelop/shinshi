// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public sealed class PlayerServerServerKillTests
    {
        // 보스 기믹이 쓰는 PlayerServer의 창구를 본다: 마지막 부활 시각과 그 보존, 판정기를 거치지 않는 사망 확정, 서버 보호

        /****************************************
        *                Fields
        ****************************************/

        private const int    PARTICIPANT_ID = 1;
        private const double DEATH_TIME     = 3.0;
        private const double REVIVE_TIME    = 7.5;
        private const double RESTORED_TIME  = 42.0;
        private const float  REVIVE_RATE    = 0.5f;

        private static readonly Vector3 REVIVE_POINT = new Vector3(2f, 0f, -1f);

        private PlayerServerTestRig _rig;
        private PlayerServer        _player;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _rig    = new PlayerServerTestRig();
            _player = _rig.Add(PARTICIPANT_ID, Vector3.zero);
        }

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void LastReviveTime_NeverRevived_IsNoReviveMarker()
        {
            Assert.AreEqual(PlayerServer.NO_REVIVE_TIME, _player.LastReviveTime);
            Assert.AreEqual(PlayerServer.NO_REVIVE_TIME, _player.CaptureSnapshot().LastReviveTime);
        }

        [Test]
        public void Revive_Dead_RecordsApprovedServerTimeAndMoves()
        {
            _player.ApplyDamage(_player.MaxHealth, true, DEATH_TIME);

            _player.Revive(REVIVE_POINT, REVIVE_RATE, REVIVE_TIME);

            Assert.AreEqual(EParticipantLifeState.Alive, _player.LifeState);
            Assert.AreEqual(REVIVE_TIME, _player.LastReviveTime);
            Assert.AreEqual(REVIVE_POINT, _player.Position);
            Assert.Greater(_player.Health, 0f);
        }

        [Test]
        public void Revive_Alive_KeepsPreviousReviveTime()
        {
            _player.Revive(REVIVE_POINT, REVIVE_RATE, REVIVE_TIME);

            Assert.AreEqual(PlayerServer.NO_REVIVE_TIME, _player.LastReviveTime, "살아 있는 참가자의 부활은 아무것도 바꾸지 않는다");
            Assert.AreEqual(Vector3.zero, _player.Position);
        }

        [Test]
        public void Snapshot_AfterRevive_PreservesLastReviveTime()
        {
            _player.ApplyDamage(_player.MaxHealth, true, DEATH_TIME);
            _player.Revive(REVIVE_POINT, REVIVE_RATE, REVIVE_TIME);

            ParticipantCombatSnapshot snapshot = _player.CaptureSnapshot();
            PlayerServer rejoined = _rig.Add(PARTICIPANT_ID + 1, Vector3.zero);
            rejoined.Restore(in snapshot);

            Assert.AreEqual(REVIVE_TIME, snapshot.LastReviveTime);
            Assert.AreEqual(REVIVE_TIME, rejoined.LastReviveTime, "개체가 다시 만들어져도 부활 시각이 남는다");
        }

        [Test]
        public void Restore_SnapshotWithReviveTime_OverwritesMarker()
        {
            var snapshot = new ParticipantCombatSnapshot(_player.MaxHealth, EParticipantLifeState.Alive, 0.0, 0.0, 0u, 0, 0.0,
                lastReviveTime: RESTORED_TIME);

            _player.Restore(in snapshot);

            Assert.AreEqual(RESTORED_TIME, _player.LastReviveTime);
        }

        [Test]
        public void Snapshot_DefaultLastReviveTime_IsNoReviveMarker()
        {
            var snapshot = new ParticipantCombatSnapshot(1f, EParticipantLifeState.Alive, 0.0, 0.0, 0u, 0, 0.0);

            Assert.AreEqual(PlayerServer.NO_REVIVE_TIME, snapshot.LastReviveTime);
        }

        [Test]
        public void KillByServer_Alive_ConfirmsDeathLikeLethalDamage()
        {
            var position = new Vector3(4f, 0f, 4f);
            _player.Displace(position, DEATH_TIME);

            Assert.IsTrue(_player.KillByServer(DEATH_TIME));

            Assert.AreEqual(EParticipantLifeState.Dead, _player.LifeState);
            Assert.AreEqual(0f, _player.Health);
            Assert.AreEqual(DEATH_TIME, _player.DeathTime);
            Assert.IsFalse(_player.CanBeDisplaced);
            Assert.AreEqual(EPlayerControlKind.None, _player.ControlKind);
            Assert.IsTrue(_player.TryFindNormalRevivePoint(out Vector3 revivePoint), "사망 위치가 기록돼 일반전 부활 지점이 된다");
            Assert.AreEqual(position, revivePoint);
        }

        [Test]
        public void KillByServer_AlreadyDead_ReturnsFalseAndKeepsDeathTime()
        {
            Assert.IsTrue(_player.KillByServer(DEATH_TIME));

            Assert.IsFalse(_player.KillByServer(DEATH_TIME + 1.0));

            Assert.AreEqual(DEATH_TIME, _player.DeathTime);
        }

        [Test]
        public void KillByServer_ServerProtected_StillKills()
        {
            _player.SetServerProtection(true);

            Assert.IsTrue(_player.KillByServer(DEATH_TIME), "이 창구는 부르면 죽인다");

            Assert.AreEqual(EParticipantLifeState.Dead, _player.LifeState);
        }

        [Test]
        public void SetServerProtection_TogglesInvulnerable()
        {
            Assert.IsFalse(_player.IsInvulnerable);

            _player.SetServerProtection(true);
            Assert.IsTrue(_player.IsInvulnerable);

            _player.SetServerProtection(false);
            Assert.IsFalse(_player.IsInvulnerable);
        }

        [Test]
        public void ApplyDamage_NonLethal_ReducesHealthAndStaysAlive()
        {
            float before = _player.Health;

            _player.ApplyDamage(1f, false, DEATH_TIME);

            Assert.AreEqual(before - 1f, _player.Health);
            Assert.AreEqual(EParticipantLifeState.Alive, _player.LifeState);
        }
    }
}
