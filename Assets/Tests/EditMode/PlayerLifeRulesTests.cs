// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Data;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class PlayerLifeRulesTests
    {
        /****************************************
        *                Fields
        ****************************************/

        // 기획이 정한 일반전 부활 규칙, SessionConfigSO 초안 값이 이와 같아야 한다
        private const float  DOCUMENTED_NORMAL_DELAY       = 5f;
        private const float  DOCUMENTED_NORMAL_HEALTH_RATE = 0.5f;
        private const float  MAX_HEALTH                    = 1500f;
        private const double DEATH_TIME                    = 100.0;
        private const double JUST_BEFORE                   = 0.001;
        private const float  TOLERANCE                     = 0.0001f;

        private SessionConfigSO _session;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _session = ScriptableObject.CreateInstance<SessionConfigSO>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_session);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void SessionConfig_NormalRevive_MatchesDocumentedRule()
        {
            Assert.AreEqual(DOCUMENTED_NORMAL_DELAY, _session.NormalReviveDelay);
            Assert.AreEqual(DOCUMENTED_NORMAL_HEALTH_RATE, _session.NormalReviveHealthRate);
        }

        [Test]
        public void IsReviveReady_BeforeNormalDelay_False()
        {
            double now = DEATH_TIME + _session.NormalReviveDelay - JUST_BEFORE;

            Assert.IsFalse(PlayerLifeRules.IsReviveReady(DEATH_TIME, now, _session.NormalReviveDelay));
        }

        [Test]
        public void IsReviveReady_AtNormalDelay_True()
        {
            double now = DEATH_TIME + _session.NormalReviveDelay;

            Assert.IsTrue(PlayerLifeRules.IsReviveReady(DEATH_TIME, now, _session.NormalReviveDelay));
        }

        [Test]
        public void ReviveHealth_Normal_IsHalfOfMax()
        {
            float health = PlayerLifeRules.ReviveHealth(MAX_HEALTH, _session.NormalReviveHealthRate);

            Assert.AreEqual(MAX_HEALTH * DOCUMENTED_NORMAL_HEALTH_RATE, health, TOLERANCE);
        }

        [Test]
        public void ReviveHealth_RateAboveOne_ClampsToMax()
        {
            const float OVER_RATE = 2f;

            Assert.AreEqual(MAX_HEALTH, PlayerLifeRules.ReviveHealth(MAX_HEALTH, OVER_RATE), TOLERANCE);
        }

        [Test]
        public void ReviveReadyTime_DeathTime_AddsDelay()
        {
            Assert.AreEqual(DEATH_TIME + DOCUMENTED_NORMAL_DELAY, PlayerLifeRules.ReviveReadyTime(DEATH_TIME, _session.NormalReviveDelay), TOLERANCE);
        }
    }
}
