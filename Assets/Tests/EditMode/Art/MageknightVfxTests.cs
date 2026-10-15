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
    public class MageknightVfxTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float  BASIC_DELAY        = 0.2f;
        private const float  SHIELD_DURATION    = 3f;
        private const float  FIELD_DURATION     = 4f;
        private const float  WAVE_SPEED         = 18f;
        private const float  WAVE_RANGE         = 9f;
        private const float  MAX_LATE           = 0.25f;
        private const double AFTER_DELAY_MARGIN = 0.01;
        private const float  FLOAT_TOLERANCE    = 0.001f;
        private const int    POOL_SIZE          = 2;

        private const byte SLOT_Q = 0;
        private const byte SLOT_W = 1;
        private const byte SLOT_E = 2;
        private const byte SLOT_R = 3;

        private readonly List<MageknightVfxTracker.Play> _plays   = new List<MageknightVfxTracker.Play>();
        private readonly List<Object>                    _created = new List<Object>();

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _plays.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; ++i)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        /****************************************
        *            Tracker Tests
        ****************************************/

        [Test]
        public void Update_FirstObservationWithPastAttackAndCast_PlaysNothing()
        {
            MageknightVfxTracker tracker = CreateTracker();

            MageknightVfxTracker.Observation first = Idle(100.0);
            first.AttackSequence = 5;
            first.DodgeSequence  = 3;
            first = Cast(first, 9, SLOT_Q, ESkillPhase.Windup, 100.2);

            Step(tracker, first);
            Assert.That(tracker.HasBaseline, Is.True);
            Assert.That(tracker.HasPending, Is.False);

            first.Now        = 100.3;
            first.SkillPhase = ESkillPhase.Active;
            Step(tracker, first);
            first.Now        = 100.5;
            first.SkillPhase = ESkillPhase.Recovery;
            Step(tracker, first);

            Assert.That(_plays, Is.Empty);
        }

        [Test]
        public void Update_BasicSequenceBumped_PlaysOnceAtFacingAfterWindup()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(10.0));

            MageknightVfxTracker.Observation bump = Idle(10.0);
            bump.AttackSequence = 1;
            bump.Facing         = Vector3.right;
            Step(tracker, bump);
            bump.Now = 10.1;
            Step(tracker, bump);
            Assert.That(_plays, Is.Empty);

            bump.Now = 10.0 + BASIC_DELAY + AFTER_DELAY_MARGIN;
            Step(tracker, bump);
            bump.Now = 11.0;
            Step(tracker, bump);

            Assert.That(_plays.Count, Is.EqualTo(1));
            Assert.That(_plays[0].Kind, Is.EqualTo(EMageknightVfxKind.Basic));
            Assert.That(_plays[0].Direction, Is.EqualTo(Vector3.right));
            Assert.That(MageknightVfxTracker.UsesActorPosition(EMageknightVfxKind.Basic), Is.True);
            Assert.That(MageknightVfxTracker.UsesActorPosition(EMageknightVfxKind.Shield), Is.True);
            Assert.That(MageknightVfxTracker.FollowsActor(EMageknightVfxKind.Shield), Is.True);
            Assert.That(MageknightVfxTracker.UsesActorPosition(EMageknightVfxKind.Jump), Is.False);
        }

        [Test]
        public void Update_BasicObservedLongAfterBump_IsDropped()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(10.0));

            MageknightVfxTracker.Observation bump = Idle(10.0);
            bump.AttackSequence = 1;
            Step(tracker, bump);
            bump.Now = 11.0;
            Step(tracker, bump);

            Assert.That(_plays, Is.Empty);
            Assert.That(tracker.HasPending, Is.False);
        }

        [Test]
        public void Update_SkillSlots_MapToKindsWithServerOriginAndAim()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(20.0));

            Vector3 origin = new Vector3(1f, 0f, 2f);
            Vector3 aim    = Vector3.left;
            byte[]  slots  = { SLOT_Q, SLOT_W, SLOT_E, SLOT_R };
            EMageknightVfxKind[] expected =
            {
                EMageknightVfxKind.Shield,
                EMageknightVfxKind.Wave,
                EMageknightVfxKind.Field,
                EMageknightVfxKind.Jump
            };

            double now = 20.0;
            for (int i = 0; i < slots.Length; ++i)
            {
                MageknightVfxTracker.Observation cast = Cast(Idle(now), i + 1, slots[i], ESkillPhase.Windup, now + 0.3);
                cast.SkillOrigin = origin;
                cast.SkillPoint  = origin;
                cast.SkillAim    = aim;
                Step(tracker, cast);
                cast.Now = now + 0.31;
                Step(tracker, cast);
                Step(tracker, Idle(now + 0.4));
                now += 10.0;
            }

            Assert.That(_plays.Count, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; ++i)
            {
                Assert.That(_plays[i].Kind, Is.EqualTo(expected[i]));
                Assert.That(_plays[i].Origin, Is.EqualTo(origin));
                Assert.That(_plays[i].Direction, Is.EqualTo(aim));
            }
        }

        [Test]
        public void Update_SameCastObservedOverManyFrames_PlaysOnce()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(30.0));

            MageknightVfxTracker.Observation cast = Cast(Idle(30.0), 4, SLOT_Q, ESkillPhase.Windup, 30.25);
            Step(tracker, cast);
            cast.Now = 30.26;
            Step(tracker, cast);
            cast.Now        = 30.3;
            cast.SkillPhase = ESkillPhase.Active;
            Step(tracker, cast);
            cast.Now        = 30.5;
            cast.SkillPhase = ESkillPhase.Recovery;
            Step(tracker, cast);
            Assert.That(_plays.Count, Is.EqualTo(1));

            // 같은 시전 번호는 한 번만, 새 번호가 오면 다시 재생한다
            MageknightVfxTracker.Observation next = Cast(Idle(31.0), 5, SLOT_Q, ESkillPhase.Windup, 31.25);
            Step(tracker, next);
            next.Now = 31.26;
            Step(tracker, next);
            Assert.That(_plays.Count, Is.EqualTo(2));
        }

        [Test]
        public void Update_ZeroActiveSkillJumpsFromWindupToRecovery_PlaysOnce()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(40.0));

            MageknightVfxTracker.Observation cast = Cast(Idle(40.0), 2, SLOT_R, ESkillPhase.Windup, 40.3);
            Step(tracker, cast);
            Assert.That(_plays, Is.Empty);

            // 서버 시각 추정이 resolveAt보다 이르더라도 Recovery 신호만으로 도달로 본다
            cast.Now        = 40.28;
            cast.SkillPhase = ESkillPhase.Recovery;
            Step(tracker, cast);
            cast.Now = 40.4;
            Step(tracker, cast);

            Assert.That(_plays.Count, Is.EqualTo(1));
            Assert.That(_plays[0].Kind, Is.EqualTo(EMageknightVfxKind.Jump));
        }

        [Test]
        public void Update_WindupCancelledToIdle_PlaysNothingForAnySlot()
        {
            byte[] slots = { SLOT_Q, SLOT_W, SLOT_E, SLOT_R };
            for (int i = 0; i < slots.Length; ++i)
            {
                MageknightVfxTracker tracker = CreateTracker();
                Step(tracker, Idle(50.0));

                MageknightVfxTracker.Observation cast = Cast(Idle(50.0), 7, slots[i], ESkillPhase.Windup, 50.5);
                Step(tracker, cast);

                // 서버는 취소하면 Idle로 돌리되 시전 번호는 남긴다
                cast.Now        = 50.2;
                cast.SkillPhase = ESkillPhase.Idle;
                Step(tracker, cast);
                cast.Now = 50.6;
                Step(tracker, cast);

                Assert.That(tracker.HasPending, Is.False);
            }

            Assert.That(_plays, Is.Empty);
        }

        [Test]
        public void Update_CastReplacedBeforeResolve_PlaysOnlyNewCast()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(60.0));

            Step(tracker, Cast(Idle(60.0), 1, SLOT_Q, ESkillPhase.Windup, 60.25));
            Step(tracker, Cast(Idle(60.1), 2, SLOT_R, ESkillPhase.Windup, 60.4));
            Step(tracker, Cast(Idle(60.41), 2, SLOT_R, ESkillPhase.Active, 60.4));

            Assert.That(_plays.Count, Is.EqualTo(1));
            Assert.That(_plays[0].Kind, Is.EqualTo(EMageknightVfxKind.Jump));
        }

        [Test]
        public void Update_OtherClassObservation_PlaysNothingAndRebaselines()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(70.0));
            Assert.That(tracker.HasBaseline, Is.True);

            MageknightVfxTracker.Observation other = Cast(Idle(70.1), 3, SLOT_Q, ESkillPhase.Windup, 70.2);
            other.PlayerClass    = EPlayerClass.Swordsman;
            other.AttackSequence = 9;
            Step(tracker, other);
            other.Now        = 70.3;
            other.SkillPhase = ESkillPhase.Active;
            Step(tracker, other);

            Assert.That(_plays, Is.Empty);
            Assert.That(tracker.HasBaseline, Is.False);

            // 마검사로 돌아온 첫 관찰은 쌓인 값을 재생하지 않는 새 기준값이다
            MageknightVfxTracker.Observation back = Cast(Idle(70.4), 3, SLOT_Q, ESkillPhase.Recovery, 70.2);
            back.AttackSequence = 9;
            Step(tracker, back);
            back.Now = 70.6;
            Step(tracker, back);

            Assert.That(_plays, Is.Empty);
            Assert.That(tracker.HasBaseline, Is.True);
        }

        [Test]
        public void Update_DeathWhilePending_DropsPendingAndIgnoresDeadFrames()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(80.0));
            Step(tracker, Cast(Idle(80.0), 1, SLOT_Q, ESkillPhase.Windup, 80.3));
            Assert.That(tracker.HasPending, Is.True);

            MageknightVfxTracker.Observation dead = Cast(Idle(80.1), 1, SLOT_Q, ESkillPhase.Windup, 80.3);
            dead.LifeState = EParticipantLifeState.Dead;
            Step(tracker, dead);
            Assert.That(tracker.HasPending, Is.False);

            dead.Now        = 80.4;
            dead.SkillPhase = ESkillPhase.Recovery;
            Step(tracker, dead);
            dead.AttackSequence = 3;
            dead.Now            = 80.5;
            Step(tracker, dead);

            Assert.That(_plays, Is.Empty);
        }

        [Test]
        public void Update_DodgeWhilePending_DropsPending()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(90.0));
            Step(tracker, Cast(Idle(90.0), 1, SLOT_R, ESkillPhase.Windup, 90.7));
            Assert.That(tracker.HasPending, Is.True);

            MageknightVfxTracker.Observation dodge = Idle(90.2);
            dodge.DodgeSequence = 1;
            Step(tracker, dodge);
            Assert.That(tracker.HasPending, Is.False);

            dodge.Now = 90.8;
            Step(tracker, dodge);
            Assert.That(_plays, Is.Empty);
        }

        [Test]
        public void Update_FieldObservedLate_KeepsServerEndTimeAndDropsWhenExpired()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(100.0));

            // resolveAt 100.3에서 3초 늦게 처음 본 장판은 남은 1초만 보이고 서버와 같은 시각에 끝난다
            Step(tracker, Cast(Idle(103.3), 1, SLOT_E, ESkillPhase.Recovery, 100.3));
            Assert.That(_plays.Count, Is.EqualTo(1));
            Assert.That(_plays[0].Kind, Is.EqualTo(EMageknightVfxKind.Field));
            Assert.That(_plays[0].StartAt, Is.EqualTo(100.3).Within(1e-9));
            Assert.That(_plays[0].EndAt, Is.EqualTo(100.3 + FIELD_DURATION).Within(1e-9));

            _plays.Clear();
            Step(tracker, Cast(Idle(110.0), 2, SLOT_E, ESkillPhase.Recovery, 105.0));
            Assert.That(_plays, Is.Empty);
        }

        [Test]
        public void Update_FieldSpawnedThenIdleDeathDodge_TrackerPlaysNothingMore()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(120.0));
            Step(tracker, Cast(Idle(120.0), 1, SLOT_E, ESkillPhase.Windup, 120.3));
            Step(tracker, Cast(Idle(120.31), 1, SLOT_E, ESkillPhase.Active, 120.3));
            Assert.That(_plays.Count, Is.EqualTo(1));

            Step(tracker, Idle(120.5));
            MageknightVfxTracker.Observation dead = Idle(121.0);
            dead.LifeState = EParticipantLifeState.Dead;
            Step(tracker, dead);
            Step(tracker, Idle(121.5));
            MageknightVfxTracker.Observation dodge = Idle(122.0);
            dodge.DodgeSequence = 1;
            Step(tracker, dodge);

            Assert.That(_plays.Count, Is.EqualTo(1));
        }

        [Test]
        public void Update_WaveObservedLate_StartsAtElapsedDistanceNotAtOrigin()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(130.0));

            MageknightVfxTracker.Observation cast = Cast(Idle(130.5), 1, SLOT_W, ESkillPhase.Active, 130.3);
            cast.SkillOrigin = new Vector3(5f, 0f, 5f);
            cast.SkillAim    = Vector3.forward;
            Step(tracker, cast);

            Assert.That(_plays.Count, Is.EqualTo(1));
            MageknightVfxTracker.Play play = _plays[0];
            Assert.That(play.Kind, Is.EqualTo(EMageknightVfxKind.Wave));
            Assert.That(play.StartAt, Is.EqualTo(130.3).Within(1e-9));

            float traveled = MageknightVfxTracker.WaveDistance(WAVE_SPEED, WAVE_RANGE, 130.5 - play.StartAt);
            Assert.That(traveled, Is.EqualTo(WAVE_SPEED * 0.2f).Within(FLOAT_TOLERANCE));
        }

        [Test]
        public void WaveDistance_IsPlainSpeedTimesElapsedClampedToRange()
        {
            Assert.That(MageknightVfxTracker.WaveDistance(WAVE_SPEED, WAVE_RANGE, -1.0), Is.EqualTo(0f));
            Assert.That(MageknightVfxTracker.WaveDistance(WAVE_SPEED, WAVE_RANGE, 0.0), Is.EqualTo(0f));
            Assert.That(MageknightVfxTracker.WaveDistance(WAVE_SPEED, WAVE_RANGE, 0.1), Is.EqualTo(1.8f).Within(FLOAT_TOLERANCE));
            Assert.That(MageknightVfxTracker.WaveDistance(WAVE_SPEED, WAVE_RANGE, 0.2), Is.EqualTo(3.6f).Within(FLOAT_TOLERANCE));
            Assert.That(MageknightVfxTracker.WaveDistance(WAVE_SPEED, WAVE_RANGE, 0.4), Is.EqualTo(7.2f).Within(FLOAT_TOLERANCE));
            Assert.That(MageknightVfxTracker.WaveDistance(WAVE_SPEED, WAVE_RANGE, 5.0), Is.EqualTo(WAVE_RANGE));
            Assert.That(MageknightVfxTracker.WaveDistance(0f, WAVE_RANGE, 1.0), Is.EqualTo(0f));
            Assert.That(MageknightVfxTracker.WaveTravelSeconds(WAVE_SPEED, WAVE_RANGE), Is.EqualTo(0.5).Within(1e-6));
        }

        [Test]
        public void Update_WaveObservedAfterMaxRange_IsDropped()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(140.0));

            // 사거리 9m를 18m/s로 지나는 데 0.5초가 걸리므로 그 뒤 첫 관찰은 지난 일이다
            Step(tracker, Cast(Idle(141.0), 1, SLOT_W, ESkillPhase.Recovery, 140.3));
            Assert.That(_plays, Is.Empty);
        }

        [Test]
        public void Update_UnconfiguredWaveFieldAndShield_PlayNothing()
        {
            var tracker = new MageknightVfxTracker();
            tracker.Configure(BASIC_DELAY, 0f, 0f, 0f, 0f, MAX_LATE);
            Step(tracker, Idle(150.0));
            Step(tracker, Cast(Idle(150.0), 1, SLOT_W, ESkillPhase.Active, 150.0));
            Step(tracker, Cast(Idle(151.0), 2, SLOT_E, ESkillPhase.Active, 151.0));
            Step(tracker, Cast(Idle(152.0), 3, SLOT_Q, ESkillPhase.Active, 152.0));

            Assert.That(_plays, Is.Empty);
        }

        [Test]
        public void Update_BriefSkillObservedLong_IsDropped()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(160.0));
            Step(tracker, Cast(Idle(161.0), 1, SLOT_R, ESkillPhase.Recovery, 160.3));

            Assert.That(_plays, Is.Empty);
        }

        [Test]
        public void Update_ShieldResolved_LastsForImmunityDurationAtActor()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(170.0));
            Step(tracker, Cast(Idle(170.0), 1, SLOT_Q, ESkillPhase.Windup, 170.1));
            Step(tracker, Cast(Idle(170.11), 1, SLOT_Q, ESkillPhase.Recovery, 170.1));

            Assert.That(_plays.Count, Is.EqualTo(1));
            Assert.That(_plays[0].Kind, Is.EqualTo(EMageknightVfxKind.Shield));
            Assert.That(_plays[0].EndAt, Is.EqualTo(170.1 + SHIELD_DURATION).Within(1e-9));
        }

        [Test]
        public void Update_FieldCast_UsesServerPointNotCasterOrigin()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(180.0));
            MageknightVfxTracker.Observation cast = Cast(Idle(180.31), 1, SLOT_E, ESkillPhase.Active, 180.3);
            cast.SkillOrigin = Vector3.zero;
            cast.SkillPoint  = new Vector3(4f, 0f, 5f);
            Step(tracker, cast);

            Assert.That(_plays.Count, Is.EqualTo(1));
            Assert.That(_plays[0].Origin, Is.EqualTo(new Vector3(4f, 0f, 5f)));
        }

        [Test]
        public void Update_BasicSwing_CarriesComboStep()
        {
            MageknightVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(190.0));
            MageknightVfxTracker.Observation bump = Idle(190.0);
            bump.AttackSequence  = 1;
            bump.AttackComboStep = 2;
            Step(tracker, bump);
            bump.Now = 190.0 + BASIC_DELAY + AFTER_DELAY_MARGIN;
            Step(tracker, bump);

            Assert.That(_plays.Count, Is.EqualTo(1));
            Assert.That(_plays[0].Variant, Is.EqualTo(2));
        }

        /****************************************
        *              Pool Tests
        ****************************************/

        [Test]
        public void Pool_FieldTimed_SurvivesUntilServerEndTime()
        {
            MageknightVfxPool pool = CreatePool();
            double endAt = 200.0 + FIELD_DURATION;

            pool.PlayTimed(Vector3.zero, Quaternion.identity, endAt);
            Assert.That(pool.ActiveCount, Is.EqualTo(1));

            pool.Tick(201.0);
            pool.Tick(203.9);
            Assert.That(pool.ActiveCount, Is.EqualTo(1));

            pool.Tick(endAt);
            Assert.That(pool.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Pool_FieldAfterSkillIdleDeathAndDodge_KeepsRunningUntilFourSeconds()
        {
            MageknightVfxTracker tracker = CreateTracker();
            MageknightVfxPool    pool    = CreatePool();
            Step(tracker, Idle(300.0));

            MageknightVfxTracker.Observation cast = Cast(Idle(300.31), 1, SLOT_E, ESkillPhase.Active, 300.3);
            cast.SkillPoint = new Vector3(2f, 0f, 3f);
            int count = tracker.Update(in cast);
            Assert.That(count, Is.EqualTo(1));
            MageknightVfxTracker.Play play = tracker.GetPlay(0);
            pool.PlayTimed(play.Origin, Quaternion.identity, play.EndAt);

            // 뷰와 같이 추적기 갱신 뒤에 풀만 갱신한다. 추적기는 풀을 멈추라고 요청하지 않는다
            Observe(tracker, pool, Idle(301.0));
            MageknightVfxTracker.Observation dead = Idle(301.5);
            dead.LifeState = EParticipantLifeState.Dead;
            Observe(tracker, pool, dead);
            Observe(tracker, pool, Idle(302.0));
            MageknightVfxTracker.Observation dodge = Idle(303.0);
            dodge.DodgeSequence = 1;
            Observe(tracker, pool, dodge);
            Assert.That(pool.ActiveCount, Is.EqualTo(1));
            Assert.That(pool.GetSlotPosition(0), Is.EqualTo(new Vector3(2f, 0f, 3f)));

            Observe(tracker, pool, Idle(304.29));
            Assert.That(pool.ActiveCount, Is.EqualTo(1));

            Observe(tracker, pool, Idle(304.31));
            Assert.That(pool.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Pool_NewFieldWhileOneActive_DoesNotClearEarlierSlot()
        {
            MageknightVfxPool pool = CreatePool();

            pool.PlayTimed(Vector3.zero, Quaternion.identity, 410.0);
            pool.PlayTimed(Vector3.one, Quaternion.identity, 420.0);

            Assert.That(pool.ActiveCount, Is.EqualTo(2));
            Assert.That(pool.GetSlotPosition(0), Is.EqualTo(Vector3.zero));
            Assert.That(pool.GetSlotPosition(1), Is.EqualTo(Vector3.one));

            pool.Tick(415.0);
            Assert.That(pool.IsSlotActive(0), Is.False);
            Assert.That(pool.IsSlotActive(1), Is.True);
        }

        [Test]
        public void Pool_MoreThanFixedSize_RecyclesOldestWithoutGrowing()
        {
            MageknightVfxPool pool = CreatePool();

            pool.PlayTimed(Vector3.zero, Quaternion.identity, 500.0);
            pool.PlayTimed(Vector3.one, Quaternion.identity, 500.0);
            pool.PlayTimed(Vector3.up, Quaternion.identity, 500.0);

            Assert.That(pool.SlotCount, Is.EqualTo(POOL_SIZE));
            Assert.That(pool.ActiveCount, Is.EqualTo(POOL_SIZE));
            Assert.That(pool.GetSlotPosition(0), Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Pool_WaveLateStart_BeginsAtElapsedDistanceAndMovesStraight()
        {
            MageknightVfxPool pool   = CreatePool();
            Vector3           origin = new Vector3(1f, 0f, 1f);

            pool.PlayWave(origin, Vector3.forward, 600.0, WAVE_SPEED, WAVE_RANGE, 600.2);
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(1f + 3.6f).Within(FLOAT_TOLERANCE));
            Assert.That(pool.GetSlotPosition(0).x, Is.EqualTo(1f).Within(FLOAT_TOLERANCE));

            pool.Tick(600.3);
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(1f + 5.4f).Within(FLOAT_TOLERANCE));
            pool.Tick(600.4);
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(1f + 7.2f).Within(FLOAT_TOLERANCE));
        }

        [Test]
        public void Pool_WaveReachesMaxRange_StopsAtRangeAndCleansUp()
        {
            MageknightVfxPool pool = CreatePool();

            pool.PlayWave(Vector3.zero, Vector3.forward, 700.0, WAVE_SPEED, WAVE_RANGE, 700.0);
            pool.Tick(700.49);
            Assert.That(pool.ActiveCount, Is.EqualTo(1));
            Assert.That(pool.GetSlotPosition(0).z, Is.LessThan(WAVE_RANGE));

            pool.Tick(700.55);
            Assert.That(pool.ActiveCount, Is.EqualTo(1));
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(WAVE_RANGE).Within(FLOAT_TOLERANCE));

            // 사거리 도달 후에도 다음 틱에서 더 나아가지 않고, 잠시 뒤 정리된다
            pool.Tick(700.7);
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(WAVE_RANGE).Within(FLOAT_TOLERANCE));
            Assert.That(pool.ActiveCount, Is.EqualTo(1));

            pool.Tick(701.5);
            Assert.That(pool.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Pool_WaveSurvivesIdleAndNextAttack_UntilStopAll()
        {
            MageknightVfxTracker tracker = CreateTracker();
            MageknightVfxPool    pool    = CreatePool();
            Step(tracker, Idle(800.0));

            MageknightVfxTracker.Observation cast = Cast(Idle(800.31), 1, SLOT_W, ESkillPhase.Active, 800.3);
            cast.SkillAim = Vector3.forward;
            int count = tracker.Update(in cast);
            Assert.That(count, Is.EqualTo(1));
            MageknightVfxTracker.Play play = tracker.GetPlay(0);
            pool.PlayWave(play.Origin, play.Direction, play.StartAt, WAVE_SPEED, WAVE_RANGE, 800.31);

            // 발사 직후 Idle과 다음 기본 공격이 와도 날아가는 검기는 그대로 남는다
            Observe(tracker, pool, Idle(800.35));
            MageknightVfxTracker.Observation attack = Idle(800.4);
            attack.AttackSequence = 1;
            Observe(tracker, pool, attack);
            Assert.That(pool.ActiveCount, Is.EqualTo(1));

            pool.StopAll();
            Assert.That(pool.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Pool_BriefGroup_EnablesOnlySelectedGroupAndTimedRestoresAll()
        {
            var prefab = new GameObject("MageknightVfxGroupPrefab");
            var effect = new GameObject("Effect");
            effect.transform.SetParent(prefab.transform, false);
            for (int i = 0; i < 3; ++i)
            {
                var group = new GameObject("Group" + i);
                group.transform.SetParent(effect.transform, false);
                group.AddComponent<ParticleSystem>();
            }

            var root = new GameObject("MageknightVfxTestRoot");
            _created.Add(prefab);
            _created.Add(root);
            var pool = new MageknightVfxPool(prefab, root.transform, 1);
            Assert.That(pool.GroupCount, Is.EqualTo(3));

            pool.PlayBrief(Vector3.zero, Quaternion.identity, 0.0, 1f, 5);
            Transform played = root.transform.GetChild(0).GetChild(0);
            Assert.That(played.GetChild(0).gameObject.activeSelf, Is.False);
            Assert.That(played.GetChild(1).gameObject.activeSelf, Is.False);
            Assert.That(played.GetChild(2).gameObject.activeSelf, Is.True);

            pool.PlayTimed(Vector3.zero, Quaternion.identity, 2.0);
            Assert.That(played.GetChild(0).gameObject.activeSelf, Is.True);
            Assert.That(played.GetChild(1).gameObject.activeSelf, Is.True);

            pool.MoveActive(Vector3.one);
            Assert.That(pool.GetSlotPosition(0), Is.EqualTo(Vector3.one));
        }

        [Test]
        public void Pool_MissingPrefab_NeverThrowsAndStaysEmpty()
        {
            var root = new GameObject("MageknightVfxTestRoot");
            _created.Add(root);
            var pool = new MageknightVfxPool(null, root.transform, POOL_SIZE);

            Assert.That(pool.HasPrefab, Is.False);
            Assert.DoesNotThrow(() =>
            {
                pool.PlayBrief(Vector3.zero, Quaternion.identity, 0.0, 1f);
                pool.PlayTimed(Vector3.zero, Quaternion.identity, 1.0);
                pool.PlayWave(Vector3.zero, Vector3.forward, 0.0, WAVE_SPEED, WAVE_RANGE, 0.0);
                pool.Tick(1.0);
                pool.StopAll();
            });
            Assert.That(pool.ActiveCount, Is.EqualTo(0));
            Assert.That(pool.IsSlotActive(0), Is.False);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static MageknightVfxTracker CreateTracker()
        {
            var tracker = new MageknightVfxTracker();
            tracker.Configure(BASIC_DELAY, SHIELD_DURATION, FIELD_DURATION, WAVE_SPEED, WAVE_RANGE, MAX_LATE);
            return tracker;
        }

        private MageknightVfxPool CreatePool()
        {
            var prefab = new GameObject("MageknightVfxTestPrefab");
            prefab.AddComponent<ParticleSystem>();
            var root = new GameObject("MageknightVfxTestRoot");
            _created.Add(prefab);
            _created.Add(root);
            return new MageknightVfxPool(prefab, root.transform, POOL_SIZE);
        }

        private static MageknightVfxTracker.Observation Idle(double now)
        {
            var obs = new MageknightVfxTracker.Observation();
            obs.PlayerClass = EPlayerClass.Mageknight;
            obs.LifeState   = EParticipantLifeState.Alive;
            obs.SkillPhase  = ESkillPhase.Idle;
            obs.SkillKind   = ESkillRequestKind.Regular;
            obs.Facing      = Vector3.forward;
            obs.SkillAim    = Vector3.forward;
            obs.Now         = now;
            return obs;
        }

        private static MageknightVfxTracker.Observation Cast(MageknightVfxTracker.Observation source, int castId, byte slot, ESkillPhase phase, double resolveAt)
        {
            source.CastInstanceId = castId;
            source.SkillSlot      = slot;
            source.SkillPhase     = phase;
            source.SkillResolveAt = resolveAt;
            return source;
        }

        // 한 프레임을 반영하고, 그 프레임에 재생된 효과를 _plays에 쌓는다
        private int Step(MageknightVfxTracker tracker, MageknightVfxTracker.Observation obs)
        {
            int count = tracker.Update(in obs);
            for (int i = 0; i < count; ++i)
            {
                _plays.Add(tracker.GetPlay(i));
            }

            return count;
        }

        // 뷰의 Update와 같은 순서로 추적기를 갱신한 뒤 풀을 갱신한다
        private void Observe(MageknightVfxTracker tracker, MageknightVfxPool pool, MageknightVfxTracker.Observation obs)
        {
            Step(tracker, obs);
            pool.Tick(obs.Now);
        }
    }
}
