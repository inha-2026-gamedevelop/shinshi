// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class EnemyHitReactionTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float FRAME_SECONDS = 1f / 60f;
        private const float TOLERANCE     = 0.0001f;

        private static readonly Vector3 s_away = new Vector3(0f, 0f, 1f);

        /****************************************
        *                Methods
        ****************************************/

        [Test]
        public void Tick_WithoutHit_StaysAtRest()
        {
            var reaction = new EnemyHitReaction();

            reaction.Tick(FRAME_SECONDS);

            Assert.IsFalse(reaction.IsFrozen);
            Assert.IsFalse(reaction.IsActive);
            Assert.AreEqual(Vector3.zero, reaction.VibrateOffset);
            Assert.AreEqual(Vector3.one, reaction.PunchScale);
        }

        [Test]
        public void Tick_FreezeFrames_FreezesExactlyThatManyFrames()
        {
            var reaction = new EnemyHitReaction();
            reaction.Begin(new EnemyHitCue(s_away, 3, 0.05f, 0f, false));

            int frozen = 0;
            for (int i = 0; i < 6; ++i)
            {
                reaction.Tick(FRAME_SECONDS);
                if (reaction.IsFrozen)
                {
                    ++frozen;
                }
            }

            Assert.AreEqual(3, frozen);
            Assert.IsFalse(reaction.IsFrozen);
        }

        [Test]
        public void Tick_Vibrate_AlternatesAlongHitAxisAndDecays()
        {
            var reaction = new EnemyHitReaction();
            reaction.Begin(new EnemyHitCue(new Vector3(0f, 5f, 2f), 4, 0.1f, 0f, false));

            reaction.Tick(FRAME_SECONDS);
            Vector3 first = reaction.VibrateOffset;
            reaction.Tick(FRAME_SECONDS);
            Vector3 second = reaction.VibrateOffset;

            Assert.AreEqual(0f, first.x, TOLERANCE);
            Assert.AreEqual(0f, first.y, TOLERANCE, "수직 성분은 축에서 뺀다");
            Assert.Greater(Mathf.Abs(first.z), Mathf.Abs(second.z), "멈춤이 끝나 갈수록 줄어든다");
            Assert.Less(first.z * second.z, 0f, "앞뒤를 번갈아 떤다");
        }

        [Test]
        public void Begin_LongerFreeze_ReplacesShorter()
        {
            var reaction = new EnemyHitReaction();
            reaction.Begin(new EnemyHitCue(s_away, 2, 0.05f, 0f, false));
            reaction.Begin(new EnemyHitCue(s_away, 5, 0.05f, 0f, false));

            int frozen = 0;
            for (int i = 0; i < 8; ++i)
            {
                reaction.Tick(FRAME_SECONDS);
                frozen += reaction.IsFrozen ? 1 : 0;
            }

            Assert.AreEqual(5, frozen);
        }

        [Test]
        public void Begin_ShorterFreeze_DoesNotCutLonger()
        {
            var reaction = new EnemyHitReaction();
            reaction.Begin(new EnemyHitCue(s_away, 5, 0.05f, 0f, false));
            reaction.Begin(new EnemyHitCue(s_away, 1, 0.05f, 0f, false));

            int frozen = 0;
            for (int i = 0; i < 8; ++i)
            {
                reaction.Tick(FRAME_SECONDS);
                frozen += reaction.IsFrozen ? 1 : 0;
            }

            Assert.AreEqual(5, frozen);
        }

        [Test]
        public void Tick_Punch_SquashesHeightAndSpreadsSidewaysThenSettles()
        {
            var reaction = new EnemyHitReaction();
            reaction.Begin(new EnemyHitCue(s_away, 0, 0f, 0.3f, false));

            reaction.Tick(FRAME_SECONDS);
            Vector3 early = reaction.PunchScale;

            Assert.Less(early.y, 1f);
            Assert.Greater(early.x, 1f);
            Assert.AreEqual(early.x, early.z, TOLERANCE);

            for (int i = 0; i < 60; ++i)
            {
                reaction.Tick(FRAME_SECONDS);
            }

            Assert.AreEqual(Vector3.one, reaction.PunchScale);
            Assert.IsFalse(reaction.IsActive);
        }

        [Test]
        public void Tick_PunchAmountZero_NeverChangesScale()
        {
            var reaction = new EnemyHitReaction();
            reaction.Begin(new EnemyHitCue(s_away, 2, 0.05f, 0f, false));

            for (int i = 0; i < 4; ++i)
            {
                reaction.Tick(FRAME_SECONDS);
                Assert.AreEqual(Vector3.one, reaction.PunchScale);
            }
        }

        [Test]
        public void Reset_ClearsEveryEffect()
        {
            var reaction = new EnemyHitReaction();
            reaction.Begin(new EnemyHitCue(s_away, 6, 0.1f, 0.3f, true));
            reaction.Tick(FRAME_SECONDS);

            reaction.Reset();

            Assert.IsFalse(reaction.IsFrozen);
            Assert.IsFalse(reaction.IsActive);
            Assert.AreEqual(Vector3.zero, reaction.VibrateOffset);
            Assert.AreEqual(Vector3.one, reaction.PunchScale);
        }

        [Test]
        public void Begin_ZeroAway_KeepsPreviousAxis()
        {
            var reaction = new EnemyHitReaction();
            reaction.Begin(new EnemyHitCue(s_away, 2, 0.1f, 0f, false));
            reaction.Begin(new EnemyHitCue(Vector3.zero, 2, 0.1f, 0f, false));

            reaction.Tick(FRAME_SECONDS);

            Assert.AreEqual(0f, reaction.VibrateOffset.x, TOLERANCE);
            Assert.AreNotEqual(0f, reaction.VibrateOffset.z);
        }

        [Test]
        public void Cue_StoresValues()
        {
            var cue = new EnemyHitCue(s_away, 7, 0.2f, 0.4f, true);

            Assert.AreEqual(s_away, cue.Away);
            Assert.AreEqual(7, cue.FreezeFrames);
            Assert.AreEqual(0.2f, cue.VibrateMeters, TOLERANCE);
            Assert.AreEqual(0.4f, cue.PunchAmount, TOLERANCE);
            Assert.IsTrue(cue.IsCritical);
        }
    }
}
