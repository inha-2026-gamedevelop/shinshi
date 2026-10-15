// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Systems;

namespace Shinshi.Tests
{
    public sealed class NpcInteractionTriggerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float DEFAULT_RANGE = 3.5f;
        private const float HEIGHT_GAP    = 20f;

        private GameObject _npc = null;

        /****************************************
        *                Methods
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _npc = new GameObject("NpcInteractionTriggerTests");
            _npc.transform.position = new Vector3(10f, 2f, -4f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_npc);
        }

        [Test]
        public void IsWithin_InsideHorizontalRange_ReturnsTrue()
        {
            var trigger = _npc.AddComponent<NpcInteractionTrigger>();

            Assert.That(trigger.IsWithin(new Vector3(10f + (DEFAULT_RANGE - 0.1f), 2f, -4f)), Is.True);
        }

        [Test]
        public void IsWithin_OutsideHorizontalRange_ReturnsFalse()
        {
            var trigger = _npc.AddComponent<NpcInteractionTrigger>();

            Assert.That(trigger.IsWithin(new Vector3(10f + DEFAULT_RANGE + 0.1f, 2f, -4f)), Is.False);
        }

        [Test]
        public void IsWithin_HeightDifferenceIsIgnored()
        {
            var trigger = _npc.AddComponent<NpcInteractionTrigger>();

            Assert.That(trigger.IsWithin(new Vector3(10f, 2f + HEIGHT_GAP, -4f)), Is.True);
        }
    }
}
