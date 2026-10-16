// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class PlayerHitImmunityTests
    {
        // 피격이상 면역(궁수 3타, 기술이 거는 면역) 동안 기절, 속박, 밀림을 받지 않는지 시험한다

        /****************************************
        *                Fields
        ****************************************/

        private const float  CONTROL_SECONDS = 1f;
        private const double IMMUNE_SECONDS  = 100.0;

        private PlayerServerTestRig _rig;
        private PlayerServer        _player;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _rig    = new PlayerServerTestRig();
            _player = _rig.Add(1, Vector3.zero);
        }

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
        }

        /****************************************
        *                 Tests
        ****************************************/

        [Test]
        public void ApplyControl_WhileImmune_ReturnsImmuneAndKeepsControlFree()
        {
            double now = NetworkTime.time;
            _player.GrantHitImmunity(now + IMMUNE_SECONDS);

            Assert.IsTrue(_player.IsHitImmune(now));
            Assert.AreEqual(EPlayerControlResult.Immune, _player.ApplyControl(EPlayerControlKind.Stun, CONTROL_SECONDS, now));
            Assert.AreEqual(EPlayerControlKind.None, _player.ControlKind);
            Assert.IsFalse(_player.CanBeDisplaced);
        }

        [Test]
        public void ApplyControl_AfterImmunityEnds_Applies()
        {
            double now = NetworkTime.time;
            _player.GrantHitImmunity(now + IMMUNE_SECONDS);

            double after = now + IMMUNE_SECONDS + 1.0;
            Assert.IsFalse(_player.IsHitImmune(after));
            Assert.AreEqual(EPlayerControlResult.Applied, _player.ApplyControl(EPlayerControlKind.Root, CONTROL_SECONDS, after));
        }
    }
}
