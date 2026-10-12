// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public sealed class CharacterExperienceTrackerTests
    {
        private CharacterLevelConfigSO _config;
        private CharacterLevelSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<CharacterLevelConfigSO>();
            _settings = _config.ToSettings();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void Commit_RejoinAlreadyContainsDelayedAck_DoesNotAwardTwice()
        {
            var tracker = new CharacterExperienceTracker(_settings,
                new CharacterExperienceSnapshot(2850, "session-a", 0));
            tracker.Merge(new CharacterExperienceSnapshot(2880, "session-a", 7));
            tracker.Commit("session-a", 7, 30);
            Assert.That(tracker.TotalExperience, Is.EqualTo(2880));
            tracker.Commit("session-a", 9, 5);
            tracker.Commit("session-a", 9, 5);
            Assert.That(tracker.TotalExperience, Is.EqualTo(2885));
            Assert.That(tracker.LastSequence, Is.EqualTo(9));
        }

        [Test]
        public void Merge_StaleSnapshot_DoesNotRewindTotalOrCursor()
        {
            var tracker = new CharacterExperienceTracker(_settings,
                new CharacterExperienceSnapshot(2880, "session-a", 7));
            tracker.Merge(new CharacterExperienceSnapshot(2850, "session-a", 0));
            tracker.Merge(new CharacterExperienceSnapshot(2880, "session-a", 6));
            Assert.That(tracker.TotalExperience, Is.EqualTo(2880));
            Assert.That(tracker.LastSequence, Is.EqualTo(7));
        }

        [Test]
        public void Commit_DifferentSession_IgnoresOldAck()
        {
            var tracker = new CharacterExperienceTracker(_settings,
                new CharacterExperienceSnapshot(2880, "session-a", 7));
            tracker.Merge(new CharacterExperienceSnapshot(2880, "session-b", 0));
            tracker.Commit("session-a", 8, 30);
            tracker.Commit("session-b", 1, 5);
            Assert.That(tracker.TotalExperience, Is.EqualTo(2885));
            Assert.That(tracker.LastSequence, Is.EqualTo(1));
        }

        [Test]
        public void Commit_AtCap_ConsumesSequenceWithoutOverflow()
        {
            var tracker = new CharacterExperienceTracker(_settings,
                new CharacterExperienceSnapshot(120350, "session-a", 7));
            tracker.Commit("session-a", 8, 30);
            Assert.That(tracker.TotalExperience, Is.EqualTo(120350));
            Assert.That(tracker.LastSequence, Is.EqualTo(8));
        }
    }
}
