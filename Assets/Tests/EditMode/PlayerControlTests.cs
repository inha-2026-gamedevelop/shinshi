// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class PlayerControlTests
    {
        // 기절과 속박이 막는 행동의 규칙표와, 종류별 종료 시각, 같은 종류 갱신, 제어 뒤 면역을 시험한다

        /****************************************
        *                Fields
        ****************************************/

        private const float  IMMUNITY      = 1f;
        private const double START         = 10.0;
        private const double TOLERANCE     = 0.0001;
        private const float  STUN_SECONDS  = 0.5f;
        private const float  ROOT_SECONDS  = 1f;

        private PlayerControlTracker _tracker;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _tracker = new PlayerControlTracker(IMMUNITY);
        }

        /****************************************
        *            Rules Tests
        ****************************************/

        [TestCase(EPlayerControlKind.None, false)]
        [TestCase(EPlayerControlKind.Root, true)]
        [TestCase(EPlayerControlKind.Stun, true)]
        public void BlocksMovementDodgeAndMovementSkill_ByKind_RootAndStunBlock(EPlayerControlKind kind, bool expected)
        {
            Assert.AreEqual(expected, PlayerControlRules.BlocksMovement(kind));
            Assert.AreEqual(expected, PlayerControlRules.BlocksDodge(kind));
            Assert.AreEqual(expected, PlayerControlRules.BlocksMovementSkill(kind));
        }

        [TestCase(EPlayerControlKind.None, false)]
        [TestCase(EPlayerControlKind.Root, false)]
        [TestCase(EPlayerControlKind.Stun, true)]
        public void BlocksActionAndPotion_ByKind_OnlyStunBlocks(EPlayerControlKind kind, bool expected)
        {
            Assert.AreEqual(expected, PlayerControlRules.BlocksAction(kind));
            Assert.AreEqual(expected, PlayerControlRules.BlocksPotion(kind));
        }

        [Test]
        public void Resolve_StunAndRootBothActive_StunWins()
        {
            Assert.AreEqual(EPlayerControlKind.Stun, PlayerControlRules.Resolve(11.0, 12.0, 10.5));
            Assert.AreEqual(EPlayerControlKind.Root, PlayerControlRules.Resolve(11.0, 12.0, 11.0));
            Assert.AreEqual(EPlayerControlKind.None, PlayerControlRules.Resolve(11.0, 12.0, 12.0));
        }

        [Test]
        public void MergeEndsAt_LongerRemainingWins_NeverStacks()
        {
            Assert.AreEqual(12.0, PlayerControlRules.MergeEndsAt(12.0, 10.0, 1.0), TOLERANCE);
            Assert.AreEqual(13.0, PlayerControlRules.MergeEndsAt(12.0, 10.0, 3.0), TOLERANCE);
            Assert.AreEqual(11.0, PlayerControlRules.MergeEndsAt(0.0, 10.0, 1.0), TOLERANCE);
        }

        [TestCase(0.5f, true)]
        [TestCase(0f, false)]
        [TestCase(-1f, false)]
        [TestCase(float.NaN, false)]
        [TestCase(float.PositiveInfinity, false)]
        public void IsValidDuration_Values_OnlyPositiveFinitePasses(float seconds, bool expected)
        {
            Assert.AreEqual(expected, PlayerControlRules.IsValidDuration(seconds));
        }

        /****************************************
        *           Tracker Tests
        ****************************************/

        [Test]
        public void TryApply_Stun_ActiveUntilEndExclusive()
        {
            Assert.AreEqual(EPlayerControlResult.Applied, _tracker.TryApply(EPlayerControlKind.Stun, STUN_SECONDS, START));

            Assert.AreEqual(EPlayerControlKind.Stun, _tracker.Evaluate(START + 0.1));
            Assert.AreEqual(EPlayerControlKind.Stun, _tracker.Evaluate(START + 0.49));
            Assert.AreEqual(EPlayerControlKind.None, _tracker.Evaluate(START + STUN_SECONDS));
        }

        [Test]
        public void TryApply_SameKindShorterWhileActive_KeepsLongerEnd()
        {
            _tracker.TryApply(EPlayerControlKind.Stun, 1f, START);

            EPlayerControlResult result = _tracker.TryApply(EPlayerControlKind.Stun, 0.3f, START + 0.5);

            Assert.AreEqual(EPlayerControlResult.Unchanged, result);
            Assert.AreEqual(START + 1.0, _tracker.StunEndsAt, TOLERANCE);
        }

        [Test]
        public void TryApply_SameKindLongerWhileActive_ExtendsWithoutStacking()
        {
            _tracker.TryApply(EPlayerControlKind.Stun, 1f, START);

            EPlayerControlResult result = _tracker.TryApply(EPlayerControlKind.Stun, 1f, START + 0.5);

            Assert.AreEqual(EPlayerControlResult.Extended, result);
            Assert.AreEqual(START + 1.5, _tracker.StunEndsAt, TOLERANCE);
        }

        [Test]
        public void TryApply_DuringImmunity_RejectsUntilImmunityEnds()
        {
            _tracker.TryApply(EPlayerControlKind.Stun, STUN_SECONDS, START);
            double ended = START + STUN_SECONDS;

            Assert.AreEqual(EPlayerControlResult.Immune, _tracker.TryApply(EPlayerControlKind.Stun, STUN_SECONDS, ended));
            Assert.AreEqual(EPlayerControlResult.Immune, _tracker.TryApply(EPlayerControlKind.Root, ROOT_SECONDS, ended + 0.5));
            Assert.AreEqual(EPlayerControlKind.None, _tracker.Evaluate(ended + 0.5));
            Assert.AreEqual(EPlayerControlResult.Immune, _tracker.TryApply(EPlayerControlKind.Stun, STUN_SECONDS, ended + 0.99));
            Assert.AreEqual(EPlayerControlResult.Applied, _tracker.TryApply(EPlayerControlKind.Stun, STUN_SECONDS, ended + IMMUNITY));
        }

        [Test]
        public void TryApply_FirstCheckLongAfterEnd_CountsImmunityFromEndTime()
        {
            _tracker.TryApply(EPlayerControlKind.Stun, STUN_SECONDS, START);

            // 틱이 한참 늦게 와도 면역은 끝난 시각부터 센다
            EPlayerControlResult result = _tracker.TryApply(EPlayerControlKind.Stun, STUN_SECONDS, START + 5.0);

            Assert.AreEqual(EPlayerControlResult.Applied, result);
        }

        [Test]
        public void TryApply_StunAndRootTogether_StunFirstThenRootAndImmunityStartsAfterBoth()
        {
            _tracker.TryApply(EPlayerControlKind.Stun, STUN_SECONDS, START);
            Assert.AreEqual(EPlayerControlResult.Applied, _tracker.TryApply(EPlayerControlKind.Root, ROOT_SECONDS, START + 0.1));

            Assert.AreEqual(EPlayerControlKind.Stun, _tracker.Evaluate(START + 0.3));
            Assert.AreEqual(EPlayerControlKind.Root, _tracker.Evaluate(START + 0.7));
            Assert.AreEqual(EPlayerControlKind.None, _tracker.Evaluate(START + 1.1));

            // 속박이 끝난 START + 1.1부터 1초가 면역이다
            Assert.AreEqual(EPlayerControlResult.Immune, _tracker.TryApply(EPlayerControlKind.Stun, STUN_SECONDS, START + 2.0));
            Assert.AreEqual(EPlayerControlResult.Applied, _tracker.TryApply(EPlayerControlKind.Stun, STUN_SECONDS, START + 2.2));
        }

        [Test]
        public void TryApply_OtherKindWhileControlActive_IsAccepted()
        {
            _tracker.TryApply(EPlayerControlKind.Root, ROOT_SECONDS, START);

            EPlayerControlResult result = _tracker.TryApply(EPlayerControlKind.Stun, STUN_SECONDS, START + 0.2);

            Assert.AreEqual(EPlayerControlResult.Applied, result);
            Assert.AreEqual(EPlayerControlKind.Stun, _tracker.Evaluate(START + 0.3));
        }

        [TestCase(EPlayerControlKind.None, 1f)]
        [TestCase(EPlayerControlKind.Stun, 0f)]
        [TestCase(EPlayerControlKind.Stun, -1f)]
        [TestCase(EPlayerControlKind.Root, float.NaN)]
        [TestCase(EPlayerControlKind.Root, float.PositiveInfinity)]
        public void TryApply_InvalidKindOrSeconds_ReturnsInvalidAndChangesNothing(EPlayerControlKind kind, float seconds)
        {
            Assert.AreEqual(EPlayerControlResult.Invalid, _tracker.TryApply(kind, seconds, START));

            Assert.AreEqual(EPlayerControlKind.None, _tracker.Evaluate(START + 0.1));
            Assert.AreEqual(EPlayerControlResult.Applied, _tracker.TryApply(EPlayerControlKind.Stun, STUN_SECONDS, START + 0.2));
        }

        [Test]
        public void TryApply_HugeSeconds_IsClampedToSafetyLimit()
        {
            _tracker.TryApply(EPlayerControlKind.Root, 100000f, START);

            Assert.AreEqual(START + Constants.Combat.PLAYER_CONTROL_MAX_SECONDS, _tracker.RootEndsAt, TOLERANCE);
        }

        [Test]
        public void Clear_ActiveControl_RemovesControlAndImmunity()
        {
            _tracker.TryApply(EPlayerControlKind.Stun, 1f, START);

            _tracker.Clear();

            Assert.AreEqual(EPlayerControlKind.None, _tracker.Evaluate(START + 0.1));
            Assert.AreEqual(EPlayerControlResult.Applied, _tracker.TryApply(EPlayerControlKind.Stun, STUN_SECONDS, START + 0.1));
        }

        [Test]
        public void TryApply_ZeroImmunity_AllowsImmediateReapplyAfterEnd()
        {
            var tracker = new PlayerControlTracker(0f);
            tracker.TryApply(EPlayerControlKind.Stun, STUN_SECONDS, START);

            Assert.AreEqual(EPlayerControlResult.Applied, tracker.TryApply(EPlayerControlKind.Stun, STUN_SECONDS, START + STUN_SECONDS));
        }

        [Test]
        public void GetEndsAt_ByKind_ReturnsOwnEndOrZero()
        {
            _tracker.TryApply(EPlayerControlKind.Root, ROOT_SECONDS, START);

            Assert.AreEqual(START + ROOT_SECONDS, _tracker.GetEndsAt(EPlayerControlKind.Root), TOLERANCE);
            Assert.AreEqual(0.0, _tracker.GetEndsAt(EPlayerControlKind.Stun), TOLERANCE);
            Assert.AreEqual(0.0, _tracker.GetEndsAt(EPlayerControlKind.None), TOLERANCE);
        }
    }
}
