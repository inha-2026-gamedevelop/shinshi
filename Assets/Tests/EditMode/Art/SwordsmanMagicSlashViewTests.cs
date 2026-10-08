// 시스템
using System.Collections.Generic;

// 서드파티
using NUnit.Framework;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class SwordsmanMagicSlashViewTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  BASIC_DELAY        = 0.2f;
        private const float  CHAIN_SECOND_DELAY = 0.2f;
        private const float  MAX_LATE           = 0.25f;
        private const double AFTER_DELAY_MARGIN = 0.01;
        private const float  VECTOR_TOLERANCE   = 0.0001f;

        private const byte SLOT_Q = 0;
        private const byte SLOT_W = 1;
        private const byte SLOT_E = 2;
        private const byte SLOT_R = 3;

        private readonly List<EMagicSlashKind> _kinds = new List<EMagicSlashKind>();

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _kinds.Clear();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Update_FirstObservationWithPastAttackAndCast_PlaysNothing()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();

            SwordsmanMagicSlashTracker.Observation first = Idle(100.0);
            first.AttackSequence = 5;
            first.DodgeSequence  = 3;
            first.CastInstanceId = 9;
            first.SkillSlot      = SLOT_Q;
            first.SkillPhase     = ESkillPhase.Windup;
            first.SkillResolveAt = 100.2;

            Step(tracker, first);
            Assert.That(tracker.HasPending, Is.False);

            first.Now        = 100.3;
            first.SkillPhase = ESkillPhase.Active;
            Step(tracker, first);
            first.Now        = 100.5;
            first.SkillPhase = ESkillPhase.Recovery;
            Step(tracker, first);

            Assert.That(_kinds, Is.Empty);
        }

        [Test]
        public void Update_BasicSequenceBumped_PlaysOnceAfterWindup()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(10.0));

            SwordsmanMagicSlashTracker.Observation bump = Idle(10.0);
            bump.AttackSequence = 1;
            Step(tracker, bump);
            Assert.That(_kinds, Is.Empty);

            bump.Now = 10.1;
            Step(tracker, bump);
            Assert.That(_kinds, Is.Empty);

            bump.Now = 10.0 + BASIC_DELAY + AFTER_DELAY_MARGIN;
            Step(tracker, bump);
            bump.Now = 10.5;
            Step(tracker, bump);
            bump.Now = 11.0;
            Step(tracker, bump);

            Assert.That(_kinds, Is.EqualTo(new[] { EMagicSlashKind.Basic }));
        }

        [Test]
        public void Update_BasicSequenceWrapsAround_StillPlaysOnce()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();

            SwordsmanMagicSlashTracker.Observation start = Idle(10.0);
            start.AttackSequence = byte.MaxValue;
            Step(tracker, start);

            SwordsmanMagicSlashTracker.Observation wrapped = Idle(10.0 + BASIC_DELAY + AFTER_DELAY_MARGIN);
            wrapped.AttackSequence = 0;
            Step(tracker, wrapped);
            wrapped.Now += BASIC_DELAY + AFTER_DELAY_MARGIN;
            Step(tracker, wrapped);

            Assert.That(_kinds, Is.EqualTo(new[] { EMagicSlashKind.Basic }));
        }

        [Test]
        public void Update_BasicObservedLongAfterBump_IsDropped()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(10.0));

            SwordsmanMagicSlashTracker.Observation bump = Idle(10.0);
            bump.AttackSequence = 1;
            Step(tracker, bump);

            bump.Now = 11.0;
            Step(tracker, bump);

            Assert.That(_kinds, Is.Empty);
            Assert.That(tracker.HasPending, Is.False);
        }

        [Test]
        public void Update_BasicPendingThenSkillStarts_PlaysOnlySkill()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(10.0));

            SwordsmanMagicSlashTracker.Observation bump = Idle(10.0);
            bump.AttackSequence = 1;
            Step(tracker, bump);

            SwordsmanMagicSlashTracker.Observation cast = Cast(bump, 1, SLOT_Q, ESkillPhase.Windup, 10.3);
            cast.Now = 10.1;
            Step(tracker, cast);
            cast.Now = 10.31;
            Step(tracker, cast);
            cast.Now        = 10.6;
            cast.SkillPhase = ESkillPhase.Recovery;
            Step(tracker, cast);

            Assert.That(_kinds, Is.EqualTo(new[] { EMagicSlashKind.Wide }));
        }

        [Test]
        public void Update_AttackSequenceBumpedDuringSkill_PlaysNoBasic()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(10.0));

            SwordsmanMagicSlashTracker.Observation cast = Cast(Idle(10.0), 1, SLOT_E, ESkillPhase.Windup, 10.6);
            Step(tracker, cast);

            cast.Now            = 10.7;
            cast.SkillPhase     = ESkillPhase.Recovery;
            cast.AttackSequence = 1;
            Step(tracker, cast);

            cast.Now        = 11.2;
            cast.SkillPhase = ESkillPhase.Idle;
            Step(tracker, cast);

            Assert.That(_kinds, Is.EqualTo(new[] { EMagicSlashKind.Focus }));
        }

        [Test]
        public void Update_SkillInWindup_PlaysOnceAtResolveTime()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(20.0));

            SwordsmanMagicSlashTracker.Observation cast = Cast(Idle(20.0), 1, SLOT_Q, ESkillPhase.Windup, 20.2);
            Step(tracker, cast);
            cast.Now = 20.19;
            Step(tracker, cast);
            Assert.That(_kinds, Is.Empty);

            cast.Now = 20.2;
            Step(tracker, cast);
            Assert.That(_kinds, Is.EqualTo(new[] { EMagicSlashKind.Wide }));
        }

        [Test]
        public void Update_SkillWithNoActivePhaseSeenInRecovery_StillPlays()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(20.0));

            SwordsmanMagicSlashTracker.Observation cast = Cast(Idle(20.0), 1, SLOT_W, ESkillPhase.Windup, 20.2);
            Step(tracker, cast);

            // Active 구간이 0이라 서버가 Windup 다음 틱에 곧바로 Recovery로 넘어간 상황
            cast.Now        = 20.25;
            cast.SkillPhase = ESkillPhase.Recovery;
            Step(tracker, cast);
            cast.Now = 20.3;
            Step(tracker, cast);
            cast.Now = 20.45;
            Step(tracker, cast);

            Assert.That(_kinds, Is.EqualTo(new[] { EMagicSlashKind.Dash }));
        }

        [Test]
        public void Update_SameCastAcrossPhaseBoundaries_PlaysOnce()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(60.0));

            SwordsmanMagicSlashTracker.Observation cast = Cast(Idle(60.0), 4, SLOT_Q, ESkillPhase.Windup, 60.2);
            Step(tracker, cast);
            cast.Now = 60.1;
            Step(tracker, cast);

            cast.Now        = 60.21;
            cast.SkillPhase = ESkillPhase.Active;
            Step(tracker, cast);
            cast.Now = 60.3;
            Step(tracker, cast);

            cast.Now        = 60.45;
            cast.SkillPhase = ESkillPhase.Recovery;
            Step(tracker, cast);
            cast.Now = 60.6;
            Step(tracker, cast);

            cast.Now        = 60.7;
            cast.SkillPhase = ESkillPhase.Idle;
            Step(tracker, cast);

            Assert.That(_kinds, Is.EqualTo(new[] { EMagicSlashKind.Wide }));
        }

        [Test]
        public void Update_SkillCancelledDuringWindup_NeverPlays()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(40.0));

            SwordsmanMagicSlashTracker.Observation cast = Cast(Idle(40.0), 2, SLOT_E, ESkillPhase.Windup, 40.6);
            Step(tracker, cast);

            // 서버 Cancel은 단계만 Idle로 돌리고 시전 번호는 그대로 둔다
            cast.Now        = 40.2;
            cast.SkillPhase = ESkillPhase.Idle;
            Step(tracker, cast);
            cast.Now = 40.7;
            Step(tracker, cast);

            Assert.That(_kinds, Is.Empty);
            Assert.That(tracker.HasPending, Is.False);
        }

        [Test]
        public void Update_CastReplacedBeforeResolve_PlaysOnlyNewCast()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(50.0));

            SwordsmanMagicSlashTracker.Observation first = Cast(Idle(50.0), 1, SLOT_Q, ESkillPhase.Windup, 50.5);
            Step(tracker, first);

            SwordsmanMagicSlashTracker.Observation second = Cast(Idle(50.1), 2, SLOT_E, ESkillPhase.Windup, 50.4);
            Step(tracker, second);
            second.Now = 50.41;
            Step(tracker, second);
            second.Now        = 50.8;
            second.SkillPhase = ESkillPhase.Recovery;
            Step(tracker, second);
            second.Now = 51.0;
            Step(tracker, second);

            Assert.That(_kinds, Is.EqualTo(new[] { EMagicSlashKind.Focus }));
        }

        [Test]
        public void Update_CastFirstSeenAfterItEnded_IsNotPlayed()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(30.0));

            SwordsmanMagicSlashTracker.Observation late = Cast(Idle(30.0), 5, SLOT_Q, ESkillPhase.Recovery, 28.0);
            Step(tracker, late);
            late.Now = 30.2;
            Step(tracker, late);

            Assert.That(_kinds, Is.Empty);
            Assert.That(tracker.HasPending, Is.False);
        }

        [TestCase(0, EMagicSlashKind.Wide)]
        [TestCase(1, EMagicSlashKind.Dash)]
        [TestCase(2, EMagicSlashKind.Focus)]
        [TestCase(3, EMagicSlashKind.Chain)]
        public void Update_SkillSlot_MapsToExpectedEffect(int slot, EMagicSlashKind expected)
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(70.0));

            SwordsmanMagicSlashTracker.Observation cast = Cast(Idle(70.0), 1, (byte)slot, ESkillPhase.Windup, 70.2);
            Step(tracker, cast);
            cast.Now        = 70.21;
            cast.SkillPhase = ESkillPhase.Active;
            Step(tracker, cast);

            Assert.That(_kinds.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(_kinds[0], Is.EqualTo(expected));
        }

        [Test]
        public void Update_UnknownSlotOrNonRegularCast_PlaysNothing()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(70.0));

            SwordsmanMagicSlashTracker.Observation unknown = Cast(Idle(70.0), 1, 7, ESkillPhase.Windup, 70.2);
            Step(tracker, unknown);
            unknown.Now        = 70.3;
            unknown.SkillPhase = ESkillPhase.Active;
            Step(tracker, unknown);

            SwordsmanMagicSlashTracker.Observation ascension = Cast(Idle(71.0), 2, SLOT_Q, ESkillPhase.Windup, 71.2);
            ascension.SkillKind = ESkillRequestKind.Ascension;
            Step(tracker, ascension);
            ascension.Now        = 71.3;
            ascension.SkillPhase = ESkillPhase.Active;
            Step(tracker, ascension);

            Assert.That(_kinds, Is.Empty);
        }

        [Test]
        public void Update_ChainSkill_PlaysSecondHitAfterConfiguredDelay()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(80.0));

            SwordsmanMagicSlashTracker.Observation cast = Cast(Idle(80.0), 1, SLOT_R, ESkillPhase.Windup, 80.3);
            Step(tracker, cast);

            cast.Now        = 80.31;
            cast.SkillPhase = ESkillPhase.Active;
            Step(tracker, cast);
            Assert.That(_kinds, Is.EqualTo(new[] { EMagicSlashKind.Chain }));

            cast.Now = 80.4;
            Step(tracker, cast);
            Assert.That(_kinds.Count, Is.EqualTo(1));

            cast.Now = 80.3 + CHAIN_SECOND_DELAY + AFTER_DELAY_MARGIN;
            Step(tracker, cast);
            cast.Now        = 80.7;
            cast.SkillPhase = ESkillPhase.Recovery;
            Step(tracker, cast);

            Assert.That(_kinds, Is.EqualTo(new[] { EMagicSlashKind.Chain, EMagicSlashKind.ChainSecond }));
        }

        [Test]
        public void Update_ChainCancelledBetweenHits_SkipsSecondHit()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(80.0));

            SwordsmanMagicSlashTracker.Observation cast = Cast(Idle(80.0), 1, SLOT_R, ESkillPhase.Windup, 80.3);
            Step(tracker, cast);
            cast.Now        = 80.31;
            cast.SkillPhase = ESkillPhase.Active;
            Step(tracker, cast);

            cast.Now        = 80.4;
            cast.SkillPhase = ESkillPhase.Idle;
            Step(tracker, cast);
            cast.Now = 80.7;
            Step(tracker, cast);

            Assert.That(_kinds, Is.EqualTo(new[] { EMagicSlashKind.Chain }));
        }

        [Test]
        public void Update_ChainWithoutSecondHitDelay_PlaysOnlyFirstHit()
        {
            var tracker = new SwordsmanMagicSlashTracker();
            tracker.Configure(BASIC_DELAY, 0f, MAX_LATE);
            Step(tracker, Idle(80.0));

            SwordsmanMagicSlashTracker.Observation cast = Cast(Idle(80.0), 1, SLOT_R, ESkillPhase.Windup, 80.3);
            Step(tracker, cast);
            cast.Now        = 80.31;
            cast.SkillPhase = ESkillPhase.Active;
            Step(tracker, cast);
            cast.Now = 80.8;
            Step(tracker, cast);

            Assert.That(_kinds, Is.EqualTo(new[] { EMagicSlashKind.Chain }));
        }

        [Test]
        public void Update_SkillPlay_CarriesServerOriginAndAim()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(90.0));

            SwordsmanMagicSlashTracker.Observation cast = Cast(Idle(90.0), 1, SLOT_Q, ESkillPhase.Windup, 90.2);
            cast.SkillOrigin = new Vector3(1f, 2f, 3f);
            cast.SkillAim    = new Vector3(0f, 0f, 1f);
            cast.Facing      = new Vector3(1f, 0f, 0f);
            Step(tracker, cast);
            cast.Now = 90.21;
            int count = tracker.Update(in cast);

            Assert.That(count, Is.EqualTo(1));
            SwordsmanMagicSlashTracker.Play play = tracker.GetPlay(0);
            Assert.That(Vector3.Distance(play.Origin, cast.SkillOrigin), Is.LessThan(VECTOR_TOLERANCE));
            Assert.That(Vector3.Distance(play.Direction, cast.SkillAim), Is.LessThan(VECTOR_TOLERANCE));
        }

        [Test]
        public void Update_BasicPlay_UsesReplicatedFacing()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(90.0));

            SwordsmanMagicSlashTracker.Observation bump = Idle(90.0);
            bump.AttackSequence = 1;
            bump.Facing         = new Vector3(0f, 0f, -1f);
            Step(tracker, bump);

            bump.Now = 90.0 + BASIC_DELAY + AFTER_DELAY_MARGIN;
            int count = tracker.Update(in bump);

            Assert.That(count, Is.EqualTo(1));
            SwordsmanMagicSlashTracker.Play play = tracker.GetPlay(0);
            Assert.That(play.Kind, Is.EqualTo(EMagicSlashKind.Basic));
            Assert.That(Vector3.Distance(play.Direction, bump.Facing), Is.LessThan(VECTOR_TOLERANCE));
        }

        [TestCase(EMagicSlashKind.Basic, true)]
        [TestCase(EMagicSlashKind.Dash, true)]
        [TestCase(EMagicSlashKind.Wide, false)]
        [TestCase(EMagicSlashKind.Focus, false)]
        [TestCase(EMagicSlashKind.Chain, false)]
        [TestCase(EMagicSlashKind.ChainSecond, false)]
        public void UsesActorPosition_Kind_FollowsDesign(EMagicSlashKind kind, bool expected)
        {
            Assert.That(SwordsmanMagicSlashTracker.UsesActorPosition(kind), Is.EqualTo(expected));
        }

        [TestCase(EPlayerClass.Warrior)]
        [TestCase(EPlayerClass.Mageknight)]
        [TestCase(EPlayerClass.Archer)]
        public void Update_NonSwordsmanClass_PlaysNothingAndNeverStops(EPlayerClass playerClass)
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();

            SwordsmanMagicSlashTracker.Observation obs = Idle(100.0);
            obs.PlayerClass = playerClass;
            Step(tracker, obs);

            obs.AttackSequence = 1;
            obs.Now            = 100.5;
            Step(tracker, obs);

            SwordsmanMagicSlashTracker.Observation cast = Cast(obs, 1, SLOT_Q, ESkillPhase.Windup, 100.8);
            cast.Now = 100.6;
            Step(tracker, cast);
            cast.Now        = 100.9;
            cast.SkillPhase = ESkillPhase.Active;
            Step(tracker, cast);

            Assert.That(_kinds, Is.Empty);
            Assert.That(tracker.HasPending, Is.False);
            Assert.That(tracker.StopRequested, Is.False);
        }

        [Test]
        public void Update_ClassChangedAwayAndBack_CancelsPendingAndResetsBaseline()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(110.0));

            SwordsmanMagicSlashTracker.Observation cast = Cast(Idle(110.0), 1, SLOT_Q, ESkillPhase.Windup, 110.2);
            Step(tracker, cast);
            Assert.That(tracker.HasPending, Is.True);

            SwordsmanMagicSlashTracker.Observation archer = cast;
            archer.PlayerClass = EPlayerClass.Archer;
            archer.Now         = 110.1;
            Step(tracker, archer);
            Assert.That(tracker.StopRequested, Is.True);
            Assert.That(tracker.HasPending, Is.False);

            // 되돌아온 첫 관찰은 그 사이에 쌓인 시전과 공격을 지난 일로 소비한다
            SwordsmanMagicSlashTracker.Observation back = cast;
            back.Now            = 112.0;
            back.SkillPhase     = ESkillPhase.Recovery;
            back.AttackSequence = 7;
            back.CastInstanceId = 3;
            Step(tracker, back);
            back.Now = 112.1;
            Step(tracker, back);
            back.Now = 112.5;
            Step(tracker, back);

            Assert.That(_kinds, Is.Empty);
        }

        [Test]
        public void Update_Death_CancelsPendingAndConsumesActionsWhileDown()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(120.0));

            SwordsmanMagicSlashTracker.Observation bump = Idle(120.0);
            bump.AttackSequence = 1;
            Step(tracker, bump);
            Assert.That(tracker.HasPending, Is.True);

            SwordsmanMagicSlashTracker.Observation dead = bump;
            dead.LifeState      = EParticipantLifeState.Dead;
            dead.AttackSequence = 2;
            dead.Now            = 120.05;
            Step(tracker, dead);
            Assert.That(tracker.StopRequested, Is.True);
            Assert.That(tracker.HasPending, Is.False);

            dead.AttackSequence = 3;
            dead.Now            = 121.0;
            Step(tracker, dead);
            Assert.That(tracker.StopRequested, Is.False);

            SwordsmanMagicSlashTracker.Observation revived = dead;
            revived.LifeState = EParticipantLifeState.Alive;
            revived.Now       = 125.0;
            Step(tracker, revived);
            revived.Now = 126.0;
            Step(tracker, revived);
            Assert.That(_kinds, Is.Empty);

            revived.AttackSequence = 4;
            revived.Now            = 127.0;
            Step(tracker, revived);
            revived.Now = 127.0 + BASIC_DELAY + AFTER_DELAY_MARGIN;
            Step(tracker, revived);

            Assert.That(_kinds, Is.EqualTo(new[] { EMagicSlashKind.Basic }));
        }

        [Test]
        public void Update_DodgeSequenceChanged_CancelsPendingSkill()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(130.0));

            SwordsmanMagicSlashTracker.Observation cast = Cast(Idle(130.0), 1, SLOT_R, ESkillPhase.Windup, 130.3);
            Step(tracker, cast);
            Assert.That(tracker.HasPending, Is.True);

            cast.Now           = 130.1;
            cast.DodgeSequence = 1;
            cast.SkillPhase    = ESkillPhase.Idle;
            Step(tracker, cast);
            Assert.That(tracker.StopRequested, Is.True);
            Assert.That(tracker.HasPending, Is.False);

            cast.Now = 131.0;
            Step(tracker, cast);

            Assert.That(_kinds, Is.Empty);
        }

        [Test]
        public void Reset_AfterPendingRegistered_DropsPendingAndTreatsNextAsBaseline()
        {
            SwordsmanMagicSlashTracker tracker = CreateTracker();
            Step(tracker, Idle(140.0));

            SwordsmanMagicSlashTracker.Observation bump = Idle(140.0);
            bump.AttackSequence = 1;
            Step(tracker, bump);
            Assert.That(tracker.HasPending, Is.True);

            tracker.Reset();
            Assert.That(tracker.HasPending, Is.False);

            bump.AttackSequence = 9;
            bump.Now            = 150.0;
            Step(tracker, bump);
            bump.Now = 151.0;
            Step(tracker, bump);

            Assert.That(_kinds, Is.Empty);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static SwordsmanMagicSlashTracker CreateTracker()
        {
            var tracker = new SwordsmanMagicSlashTracker();
            tracker.Configure(BASIC_DELAY, CHAIN_SECOND_DELAY, MAX_LATE);
            return tracker;
        }

        private static SwordsmanMagicSlashTracker.Observation Idle(double now)
        {
            var obs = new SwordsmanMagicSlashTracker.Observation();
            obs.PlayerClass = EPlayerClass.Swordsman;
            obs.LifeState   = EParticipantLifeState.Alive;
            obs.SkillPhase  = ESkillPhase.Idle;
            obs.SkillKind   = ESkillRequestKind.Regular;
            obs.Facing      = Vector3.forward;
            obs.SkillAim    = Vector3.forward;
            obs.Now         = now;
            return obs;
        }

        private static SwordsmanMagicSlashTracker.Observation Cast(SwordsmanMagicSlashTracker.Observation source, int castId, byte slot, ESkillPhase phase, double resolveAt)
        {
            source.CastInstanceId = castId;
            source.SkillSlot      = slot;
            source.SkillPhase     = phase;
            source.SkillResolveAt = resolveAt;
            return source;
        }

        // 한 프레임을 반영하고, 그 프레임에 재생된 효과 종류를 _kinds에 쌓는다
        private int Step(SwordsmanMagicSlashTracker tracker, SwordsmanMagicSlashTracker.Observation obs)
        {
            int count = tracker.Update(in obs);
            for (int i = 0; i < count; ++i)
            {
                _kinds.Add(tracker.GetPlay(i).Kind);
            }

            return count;
        }
    }
}
