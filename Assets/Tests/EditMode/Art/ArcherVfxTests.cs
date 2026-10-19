// 시스템
using System.Collections.Generic;
using System.Reflection;

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
    public class ArcherVfxTests
    {
        /****************************************
        *                Fields
        ****************************************/

        // 시험용 값이며 밸런스 기준과 같은 모양이다. 실제 값은 뷰가 GameDB에서 읽는다
        private const float BASIC_WINDUP = 0.2f;
        private const float BASIC_SPEED  = 30f;
        private const float BASIC_RANGE  = 12f;
        private const float Q_SPEED      = 24f;
        private const float Q_RANGE      = 10f;
        private const float Q_INTERVAL   = 0.25f;
        private const int   Q_REPEAT     = 3;
        private const float W_SPEED      = 24f;
        private const float W_RANGE      = 10f;
        private const float E_SPEED      = 28f;
        private const float E_RANGE      = 14f;
        private const float R_SPEED      = 22f;
        private const float R_RANGE      = 9f;
        private const float R_SPREAD     = 10f;
        private const int   R_PRONGS     = 5;

        private const float  MAX_LATE           = 0.25f;
        private const float  FLOAT_TOLERANCE    = 0.001f;
        private const float  ANGLE_TOLERANCE    = 0.01f;
        private const double AFTER_DELAY_MARGIN = 0.01;
        private const int    POOL_SIZE          = 2;
        private const int    VIEW_ARROW_POOL    = 8;
        private const float  WIND_SPEED_SCALE   = 1.5f;
        private const float  SERVER_TICK        = 1f / 30f;
        private const float  TICK_WINDUP        = 0.25f;
        // 보스 몸체 반경 2m 근처 옆 거리와 뷰의 적중 정지 옆 거리 상한(가장 큰 몸체 반경 4m + 여유 0.5m)
        private const float  HIT_LATERAL        = 2.1f;
        private const float  HIT_MAX_LATERAL    = 4.5f;

        private const byte SLOT_Q = 0;
        private const byte SLOT_W = 1;
        private const byte SLOT_E = 2;
        private const byte SLOT_R = 3;

        private readonly List<ArcherVfxTracker.Play> _plays   = new List<ArcherVfxTracker.Play>();
        private readonly List<Object>                _created = new List<Object>();

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
            ArcherVfxTracker tracker = CreateTracker();

            ArcherVfxTracker.Observation first = Idle(100.0);
            first.AttackSequence = 5;
            first.DodgeSequence  = 3;
            first = Cast(first, 9, SLOT_R, ESkillPhase.Windup, 100.2);

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
        public void Update_ReEnabledAfterReset_DoesNotReplayPast()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(105.0));
            Step(tracker, Cast(Idle(105.0), 1, SLOT_Q, ESkillPhase.Windup, 105.3));
            Assert.That(tracker.HasPending, Is.True);

            tracker.Reset();
            Assert.That(tracker.HasPending, Is.False);
            Assert.That(tracker.HasBaseline, Is.False);

            // 다시 켜진 첫 관찰은 쌓인 시전과 공격을 재생하지 않는 새 기준값이다
            ArcherVfxTracker.Observation back = Cast(Idle(105.4), 1, SLOT_Q, ESkillPhase.Recovery, 105.3);
            back.AttackSequence = 4;
            Step(tracker, back);
            back.Now = 105.6;
            Step(tracker, back);

            Assert.That(_plays, Is.Empty);
        }

        [Test]
        public void Update_BasicSequenceBumped_FiresOnceAlongFacingAfterWindup()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(10.0));

            ArcherVfxTracker.Observation bump = Idle(10.0);
            bump.AttackSequence = 1;
            bump.Facing         = Vector3.right;
            Step(tracker, bump);
            bump.Now = 10.1;
            Step(tracker, bump);
            Assert.That(_plays, Is.Empty);

            bump.Now = 10.0 + BASIC_WINDUP + AFTER_DELAY_MARGIN;
            Step(tracker, bump);
            bump.Now = 11.0;
            Step(tracker, bump);

            Assert.That(_plays.Count, Is.EqualTo(2));
            Assert.That(_plays[0].IsMuzzle, Is.True);
            Assert.That(_plays[1].IsMuzzle, Is.False);
            Assert.That(_plays[1].Kind, Is.EqualTo(EArcherVfxKind.Arrow));
            Assert.That(Vector3.Distance(_plays[1].Direction, Vector3.right), Is.LessThan(FLOAT_TOLERANCE));
            Assert.That(_plays[1].Speed, Is.EqualTo(BASIC_SPEED));
            Assert.That(_plays[1].Range, Is.EqualTo(BASIC_RANGE));
            Assert.That(_plays[1].StartAt, Is.EqualTo(10.0 + BASIC_WINDUP).Within(1e-6));
            Assert.That(_plays[1].StopsOnHit, Is.True, "기본 공격 화살만 서버 적중 신호로 멈춘다");
        }

        [Test]
        public void Update_BasicComboStep_FiresAfterThatStepsWindup()
        {
            const float STEP_THREE_WINDUP = 0.33f;

            ArcherVfxTracker tracker = new ArcherVfxTracker();
            var config = new ArcherVfxTracker.Config();
            config.BasicWindup      = BASIC_WINDUP;
            config.BasicStepWindups = new[] { 0.25f, 0.22f, STEP_THREE_WINDUP };
            config.Basic            = Shot(BASIC_SPEED, BASIC_RANGE);
            config.MaxLateSeconds   = MAX_LATE;
            tracker.Configure(in config);
            Step(tracker, Idle(30.0));

            ArcherVfxTracker.Observation bump = Idle(30.0);
            bump.AttackSequence  = 1;
            bump.AttackComboStep = 2;
            Step(tracker, bump);

            // 기본 준비 시간은 지났지만 3단 준비 시간 전이라 아직 발사하지 않는다
            bump.Now = 30.0 + BASIC_WINDUP + AFTER_DELAY_MARGIN;
            Step(tracker, bump);
            Assert.That(_plays, Is.Empty);

            bump.Now = 30.0 + STEP_THREE_WINDUP + AFTER_DELAY_MARGIN;
            Step(tracker, bump);
            bump.Now = 31.0;
            Step(tracker, bump);

            Assert.That(_plays.Count, Is.EqualTo(2));
            Assert.That(_plays[1].StartAt, Is.EqualTo(30.0 + STEP_THREE_WINDUP).Within(1e-6));
        }

        [Test]
        public void Update_BasicMovesDuringWindup_CancelsWithoutFiring()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(20.0));

            ArcherVfxTracker.Observation bump = Idle(20.0);
            bump.AttackSequence = 1;
            Step(tracker, bump);

            // 서버는 틱 끝에 이동 속도를 공개하므로 준비 직후 이동도 취소로 처리한다
            bump.Now             = 20.05;
            bump.LocomotionSpeed = 3f;
            Step(tracker, bump);
            Assert.That(tracker.HasPending, Is.False);

            bump.Now             = 20.3;
            bump.LocomotionSpeed = 0f;
            Step(tracker, bump);

            Assert.That(_plays, Is.Empty);
        }

        [Test]
        public void Update_BasicObservedWithMovement_DoesNotFireAfterStopping()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(25.0));

            // 관찰 가능한 이동으로 한 번 취소한 준비는 정지 후에도 복원하지 않는다
            ArcherVfxTracker.Observation bump = Idle(25.0);
            bump.AttackSequence  = 1;
            bump.LocomotionSpeed = 3f;
            Step(tracker, bump);

            bump.Now             = 25.05;
            bump.LocomotionSpeed = 0f;
            Step(tracker, bump);
            bump.Now = 25.0 + BASIC_WINDUP + AFTER_DELAY_MARGIN;
            Step(tracker, bump);

            Assert.That(_plays, Is.Empty);
        }

        [Test]
        public void Update_MoveStartedAfterServerFireButSeenBeforeWindup_StillFires()
        {
            // 서버 30Hz에서 준비 0.25초는 승인 뒤 8틱(0.2667초)에 쏘고, 쏜 뒤의 이동은 그다음 틱부터 시작한다
            const double SERVER_START = 500.0;
            ArcherVfxTracker tracker = CreateTickTracker();
            Step(tracker, Idle(10.0));

            ArcherVfxTracker.Observation bump = Idle(10.0);
            bump.AttackSequence  = 1;
            bump.AttackStartedAt = SERVER_START;
            Step(tracker, bump);

            // 지터로 발사 뒤의 이동이 클라이언트 준비 시간보다 먼저 도착했다. 서버는 이미 쐈으므로 취소하지 않는다
            bump.Now                 = 10.22;
            bump.LocomotionSpeed     = 4f;
            bump.LocomotionStartedAt = SERVER_START + (9.0 * SERVER_TICK);
            Step(tracker, bump);
            Assert.That(tracker.HasPending, Is.True);

            bump.Now = 10.0 + (8.0 * SERVER_TICK) + AFTER_DELAY_MARGIN;
            Step(tracker, bump);

            Assert.That(CountProjectiles(), Is.EqualTo(1));
            Assert.That(FirstProjectile().StartAt, Is.EqualTo(10.0 + TICK_WINDUP).Within(1e-6));
        }

        [Test]
        public void Update_MoveStartedAtServerFireTickButSeenAfterWindup_Cancels()
        {
            const double SERVER_START = 600.0;
            ArcherVfxTracker tracker = CreateTickTracker();
            Step(tracker, Idle(20.0));

            ArcherVfxTracker.Observation bump = Idle(20.0);
            bump.AttackSequence  = 1;
            bump.AttackStartedAt = SERVER_START;
            Step(tracker, bump);

            // 준비 시간은 지났지만 서버 발사 틱 전이다. 그 틱에 시작한 이동은 서버에서 준비를 끊었다
            bump.Now = 20.0 + TICK_WINDUP + AFTER_DELAY_MARGIN;
            Step(tracker, bump);
            Assert.That(_plays, Is.Empty);

            bump.Now                 = 20.26;
            bump.LocomotionSpeed     = 4f;
            bump.LocomotionStartedAt = SERVER_START + (8.0 * SERVER_TICK);
            Step(tracker, bump);
            bump.Now             = 20.5;
            bump.LocomotionSpeed = 0f;
            Step(tracker, bump);

            Assert.That(_plays, Is.Empty);
        }

        [Test]
        public void Update_NextBasicArrivesWhileEarlierStillPending_FiresEarlierInsteadOfDropping()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(30.0));

            ArcherVfxTracker.Observation bump = Idle(30.0);
            bump.AttackSequence = 1;
            Step(tracker, bump);

            // 앞 승인이 늦게 도착해 다음 승인이 준비 시간 안에 겹쳤다. 서버는 앞 화살을 이미 쐈다
            bump.Now            = 30.15;
            bump.AttackSequence = 2;
            Step(tracker, bump);
            Assert.That(CountProjectiles(), Is.EqualTo(1));
            Assert.That(FirstProjectile().StartAt, Is.EqualTo(30.0 + BASIC_WINDUP).Within(1e-6));

            bump.Now = 30.15 + BASIC_WINDUP + AFTER_DELAY_MARGIN;
            Step(tracker, bump);
            Assert.That(CountProjectiles(), Is.EqualTo(2));
        }

        [Test]
        public void Update_BasicWithoutCancelOnMove_FiresWhileMoving()
        {
            ArcherVfxTracker tracker = CreateTracker(basicCancelOnMove: false);
            Step(tracker, Idle(26.0));

            ArcherVfxTracker.Observation bump = Idle(26.0);
            bump.AttackSequence  = 1;
            bump.LocomotionSpeed = 4f;
            Step(tracker, bump);
            bump.Now = 26.0 + BASIC_WINDUP + AFTER_DELAY_MARGIN;
            Step(tracker, bump);

            Assert.That(_plays.Count, Is.EqualTo(2));
        }

        [Test]
        public void Update_BasicInterruptedByDodgeDeathOrSkill_DoesNotFireBasic()
        {
            // 회피
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(27.0));
            ArcherVfxTracker.Observation bump = Idle(27.0);
            bump.AttackSequence = 1;
            Step(tracker, bump);
            ArcherVfxTracker.Observation dodge = Idle(27.1);
            dodge.AttackSequence = 1;
            dodge.DodgeSequence  = 1;
            Step(tracker, dodge);
            dodge.Now = 27.5;
            Step(tracker, dodge);

            // 사망
            tracker = CreateTracker();
            Step(tracker, Idle(28.0));
            bump = Idle(28.0);
            bump.AttackSequence = 1;
            Step(tracker, bump);
            ArcherVfxTracker.Observation dead = Idle(28.1);
            dead.AttackSequence = 1;
            dead.LifeState      = EParticipantLifeState.Dead;
            Step(tracker, dead);
            dead.Now = 28.5;
            Step(tracker, dead);

            // 기술 시작
            tracker = CreateTracker();
            Step(tracker, Idle(29.0));
            bump = Idle(29.0);
            bump.AttackSequence = 1;
            Step(tracker, bump);
            ArcherVfxTracker.Observation skill = Cast(Idle(29.1), 1, SLOT_E, ESkillPhase.Windup, 29.6);
            skill.AttackSequence = 1;
            Step(tracker, skill);

            // 기본 공격은 사라지고 기술의 첫 발만 시점이 오면 나간다
            Assert.That(_plays, Is.Empty);
            skill.Now = 29.61;
            Step(tracker, skill);
            Assert.That(_plays.Count, Is.EqualTo(2));
            Assert.That(_plays[1].Kind, Is.EqualTo(EArcherVfxKind.Arrow));
            Assert.That(_plays[1].IsMuzzle, Is.False);
        }

        [Test]
        public void Update_BasicWindupCancelledByServerCancelSignal_DoesNotFire()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(40.0));
            ArcherVfxTracker.Observation bump = Idle(40.0);
            bump.AttackSequence = 1;
            Step(tracker, bump);

            // 기절로 준비가 끊기면 기술 단계도 이동도 변하지 않으므로 취소 신호만이 알린다
            ArcherVfxTracker.Observation cancel = Idle(40.05);
            cancel.AttackSequence       = 1;
            cancel.ActionCancelSequence = 1;
            Step(tracker, cancel);
            Assert.That(tracker.HasPending, Is.False);

            cancel.Now = 40.5;
            Step(tracker, cancel);
            Assert.That(_plays, Is.Empty);
        }

        [Test]
        public void Update_RapidFireCancelledByServerCancelSignal_DropsRemainingShots()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(41.0));

            ArcherVfxTracker.Observation cast = Cast(Idle(41.0), 1, SLOT_Q, ESkillPhase.Windup, 41.3);
            Step(tracker, cast);
            cast.Now        = 41.31;
            cast.SkillPhase = ESkillPhase.Active;
            Step(tracker, cast);
            int firedBeforeCancel = _plays.Count;
            Assert.That(firedBeforeCancel, Is.GreaterThan(0));

            // 단계가 아직 Active로 보여도 취소 신호가 오면 남은 발사를 버린다
            cast.Now                  = 41.4;
            cast.ActionCancelSequence = 1;
            Step(tracker, cast);
            Assert.That(tracker.HasPending, Is.False);

            cast.Now = 42.0;
            Step(tracker, cast);
            Assert.That(_plays.Count, Is.EqualTo(firedBeforeCancel));
        }

        [Test]
        public void Update_RapidFire_FiresThreeShotsAtResolveAtPlusInterval()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(30.0));

            ArcherVfxTracker.Observation cast = Cast(Idle(30.0), 1, SLOT_Q, ESkillPhase.Windup, 30.3);
            cast.SkillAim = Vector3.right;
            Step(tracker, cast);
            Assert.That(_plays, Is.Empty);

            cast.Now        = 30.31;
            cast.SkillPhase = ESkillPhase.Active;
            Step(tracker, cast);
            Assert.That(CountProjectiles(), Is.EqualTo(1));

            // 후속 발사의 방향은 시전 순간의 조준으로 고정이다
            cast.SkillAim   = Vector3.left;
            cast.Now        = 30.4;
            cast.SkillPhase = ESkillPhase.Recovery;
            Step(tracker, cast);
            Assert.That(CountProjectiles(), Is.EqualTo(1));

            cast.Now = 30.3 + Q_INTERVAL + AFTER_DELAY_MARGIN;
            Step(tracker, cast);
            Assert.That(CountProjectiles(), Is.EqualTo(2));

            cast.Now = 30.3 + (2 * Q_INTERVAL) + AFTER_DELAY_MARGIN;
            Step(tracker, cast);
            Assert.That(CountProjectiles(), Is.EqualTo(3));

            cast.Now = 31.5;
            Step(tracker, cast);
            Assert.That(CountProjectiles(), Is.EqualTo(Q_REPEAT));
            Assert.That(tracker.HasPending, Is.False);
            Assert.That(FirstProjectile().StopsOnHit, Is.False, "기술 화살은 적중 신호를 보내지 않아 멈추지 않는다");

            double[] startAt = { 30.3, 30.3 + Q_INTERVAL, 30.3 + (2 * Q_INTERVAL) };
            int      shot    = 0;
            for (int i = 0; i < _plays.Count; ++i)
            {
                if (_plays[i].IsMuzzle)
                {
                    continue;
                }

                Assert.That(_plays[i].StartAt, Is.EqualTo(startAt[shot]).Within(1e-6));
                Assert.That(Vector3.Distance(_plays[i].Direction, Vector3.right), Is.LessThan(FLOAT_TOLERANCE));
                Assert.That(_plays[i].Kind, Is.EqualTo(EArcherVfxKind.Arrow));
                Assert.That(_plays[i].Speed, Is.EqualTo(Q_SPEED));
                Assert.That(_plays[i].Range, Is.EqualTo(Q_RANGE));
                ++shot;
            }

            Assert.That(CountMuzzles(), Is.EqualTo(Q_REPEAT));
        }

        [Test]
        public void Update_RapidFireStoppedAfterFirstShot_DropsRemainingShots()
        {
            // Idle로 끝남
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(32.0));
            Step(tracker, Cast(Idle(32.31), 1, SLOT_Q, ESkillPhase.Active, 32.3));
            Assert.That(CountProjectiles(), Is.EqualTo(1));
            Step(tracker, Cast(Idle(32.4), 1, SLOT_Q, ESkillPhase.Idle, 32.3));
            Step(tracker, Cast(Idle(33.5), 1, SLOT_Q, ESkillPhase.Idle, 32.3));
            Assert.That(CountProjectiles(), Is.EqualTo(1));
            Assert.That(tracker.HasPending, Is.False);

            // 회피
            _plays.Clear();
            tracker = CreateTracker();
            Step(tracker, Idle(34.0));
            Step(tracker, Cast(Idle(34.31), 1, SLOT_Q, ESkillPhase.Active, 34.3));
            ArcherVfxTracker.Observation dodge = Cast(Idle(34.4), 1, SLOT_Q, ESkillPhase.Recovery, 34.3);
            dodge.DodgeSequence = 1;
            Step(tracker, dodge);
            dodge.Now = 35.5;
            Step(tracker, dodge);
            Assert.That(CountProjectiles(), Is.EqualTo(1));

            // 사망
            _plays.Clear();
            tracker = CreateTracker();
            Step(tracker, Idle(36.0));
            Step(tracker, Cast(Idle(36.31), 1, SLOT_Q, ESkillPhase.Active, 36.3));
            ArcherVfxTracker.Observation dead = Cast(Idle(36.4), 1, SLOT_Q, ESkillPhase.Recovery, 36.3);
            dead.LifeState = EParticipantLifeState.Dead;
            Step(tracker, dead);
            dead.Now = 37.5;
            Step(tracker, dead);
            Assert.That(CountProjectiles(), Is.EqualTo(1));

            // 다른 시전으로 교체
            _plays.Clear();
            tracker = CreateTracker();
            Step(tracker, Idle(38.0));
            Step(tracker, Cast(Idle(38.31), 1, SLOT_Q, ESkillPhase.Active, 38.3));
            Step(tracker, Cast(Idle(38.4), 2, SLOT_W, ESkillPhase.Windup, 38.5));
            Step(tracker, Cast(Idle(38.51), 2, SLOT_W, ESkillPhase.Active, 38.5));
            Step(tracker, Cast(Idle(39.5), 2, SLOT_W, ESkillPhase.Recovery, 38.5));

            // Q 첫 발 하나와 W 한 발만 나가고, 교체된 Q의 남은 발사는 없다
            Assert.That(CountProjectiles(), Is.EqualTo(2));
        }

        [Test]
        public void Update_WindupCancelledToIdle_PlaysNothingForAnySlot()
        {
            byte[] slots = { SLOT_Q, SLOT_W, SLOT_E, SLOT_R };
            for (int i = 0; i < slots.Length; ++i)
            {
                ArcherVfxTracker tracker = CreateTracker();
                Step(tracker, Idle(50.0));

                ArcherVfxTracker.Observation cast = Cast(Idle(50.0), 7, slots[i], ESkillPhase.Windup, 50.5);
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
        public void Update_ScatterShot_FiresFiveProngsSymmetricAroundAim()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(40.0));

            ArcherVfxTracker.Observation cast = Cast(Idle(40.0), 3, SLOT_R, ESkillPhase.Windup, 40.4);
            cast.SkillAim = Vector3.right;
            Step(tracker, cast);
            cast.Now        = 40.41;
            cast.SkillPhase = ESkillPhase.Active;
            Step(tracker, cast);
            cast.Now = 40.6;
            Step(tracker, cast);

            Assert.That(CountProjectiles(), Is.EqualTo(R_PRONGS));
            Assert.That(CountMuzzles(), Is.EqualTo(1));

            float[] expected = { -2f * R_SPREAD, -R_SPREAD, 0f, R_SPREAD, 2f * R_SPREAD };
            int     prong    = 0;
            for (int i = 0; i < _plays.Count; ++i)
            {
                if (_plays[i].IsMuzzle)
                {
                    Assert.That(Vector3.Distance(_plays[i].Direction, Vector3.right), Is.LessThan(FLOAT_TOLERANCE));
                    continue;
                }

                float angle = Vector3.SignedAngle(Vector3.right, _plays[i].Direction, Vector3.up);
                Assert.That(angle, Is.EqualTo(expected[prong]).Within(ANGLE_TOLERANCE));
                Assert.That(_plays[i].Speed, Is.EqualTo(R_SPEED));
                Assert.That(_plays[i].Range, Is.EqualTo(R_RANGE));
                ++prong;
            }
        }

        [Test]
        public void Update_ScatterShotAroundDiagonalAim_StaysSymmetric()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(41.0));

            Vector3 aim = new Vector3(1f, 0f, 1f).normalized;
            ArcherVfxTracker.Observation cast = Cast(Idle(41.31), 3, SLOT_R, ESkillPhase.Active, 41.3);
            cast.SkillAim = aim;
            Step(tracker, cast);

            Assert.That(CountProjectiles(), Is.EqualTo(R_PRONGS));
            int prong = 0;
            for (int i = 0; i < _plays.Count; ++i)
            {
                if (_plays[i].IsMuzzle)
                {
                    continue;
                }

                float expected = (prong - ((R_PRONGS - 1) * 0.5f)) * R_SPREAD;
                float angle    = Vector3.SignedAngle(aim, _plays[i].Direction, Vector3.up);
                Assert.That(angle, Is.EqualTo(expected).Within(ANGLE_TOLERANCE));
                ++prong;
            }
        }

        [Test]
        public void Update_ProjectileSkillSlots_MapToNeutralArrowWithSingleProngWhenNotSpread()
        {
            byte[]           slots  = { SLOT_W, SLOT_E };
            EArcherVfxKind[] kinds  = { EArcherVfxKind.Arrow, EArcherVfxKind.Arrow };
            float[]          speeds = { W_SPEED, E_SPEED };
            float[]          ranges = { W_RANGE, E_RANGE };

            double now = 45.0;
            for (int i = 0; i < slots.Length; ++i)
            {
                _plays.Clear();
                ArcherVfxTracker tracker = CreateTracker();
                Step(tracker, Idle(now));
                Step(tracker, Cast(Idle(now), 1, slots[i], ESkillPhase.Windup, now + 0.3));
                Step(tracker, Cast(Idle(now + 0.31), 1, slots[i], ESkillPhase.Active, now + 0.3));

                Assert.That(_plays.Count, Is.EqualTo(2));
                for (int p = 0; p < _plays.Count; ++p)
                {
                    Assert.That(_plays[p].Kind, Is.EqualTo(kinds[i]));
                }

                Assert.That(_plays[0].IsMuzzle, Is.True);
                Assert.That(_plays[1].Speed, Is.EqualTo(speeds[i]));
                Assert.That(_plays[1].Range, Is.EqualTo(ranges[i]));
                now += 10.0;
            }
        }

        [Test]
        public void Update_ZeroActiveSkillJumpsFromWindupToRecovery_FiresFirstShotOnce()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(55.0));

            ArcherVfxTracker.Observation cast = Cast(Idle(55.0), 2, SLOT_W, ESkillPhase.Windup, 55.3);
            Step(tracker, cast);
            Assert.That(_plays, Is.Empty);

            // 서버 시각 추정이 resolveAt보다 이르더라도 Recovery 신호만으로 도달로 본다
            cast.Now        = 55.28;
            cast.SkillPhase = ESkillPhase.Recovery;
            Step(tracker, cast);
            cast.Now = 55.4;
            Step(tracker, cast);

            Assert.That(CountProjectiles(), Is.EqualTo(1));
        }

        [Test]
        public void Update_SameCastObservedOverManyFrames_FiresOnceAndNewIdFiresAgain()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(60.0));

            ArcherVfxTracker.Observation cast = Cast(Idle(60.0), 4, SLOT_W, ESkillPhase.Windup, 60.25);
            Step(tracker, cast);
            cast.Now = 60.26;
            Step(tracker, cast);
            cast.Now        = 60.3;
            cast.SkillPhase = ESkillPhase.Active;
            Step(tracker, cast);
            cast.Now        = 60.5;
            cast.SkillPhase = ESkillPhase.Recovery;
            Step(tracker, cast);
            Assert.That(CountProjectiles(), Is.EqualTo(1));

            ArcherVfxTracker.Observation next = Cast(Idle(61.0), 5, SLOT_W, ESkillPhase.Windup, 61.25);
            Step(tracker, next);
            next.Now = 61.26;
            Step(tracker, next);
            Assert.That(CountProjectiles(), Is.EqualTo(2));
        }

        [Test]
        public void Update_CastObservedAlreadyIdle_IsSkipped()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(65.0));

            Step(tracker, Cast(Idle(65.5), 1, SLOT_Q, ESkillPhase.Idle, 65.3));
            Step(tracker, Cast(Idle(66.5), 1, SLOT_Q, ESkillPhase.Idle, 65.3));

            Assert.That(_plays, Is.Empty);
            Assert.That(tracker.HasPending, Is.False);
        }

        [Test]
        public void Update_RapidFireObservedLate_ShowsOnlyLivingShotsAndNoStaleMuzzle()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(70.0));

            // resolveAt 70.3, 지금 70.7: 첫 발은 0.4초(9.6m) 지났고 둘째 발은 0.15초 지났다
            Step(tracker, Cast(Idle(70.7), 1, SLOT_Q, ESkillPhase.Recovery, 70.3));
            Assert.That(CountProjectiles(), Is.EqualTo(2));
            Assert.That(CountMuzzles(), Is.EqualTo(1));

            ArcherVfxTracker.Play first = FirstProjectile();
            Assert.That(first.StartAt, Is.EqualTo(70.3).Within(1e-9));
            Assert.That(ArcherVfxTracker.Distance(first.Speed, first.Range, 70.7 - first.StartAt), Is.EqualTo(9.6f).Within(FLOAT_TOLERANCE));
            for (int i = 0; i < _plays.Count; ++i)
            {
                if (_plays[i].IsMuzzle)
                {
                    Assert.That(_plays[i].StartAt, Is.EqualTo(70.3 + Q_INTERVAL).Within(1e-9));
                }
            }

            // 셋째 발은 아직 오지 않았다가 때가 되면 나간다
            Step(tracker, Cast(Idle(70.81), 1, SLOT_Q, ESkillPhase.Recovery, 70.3));
            Assert.That(CountProjectiles(), Is.EqualTo(3));
        }

        [Test]
        public void Update_ShotsObservedBeyondRange_AreDropped()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(80.0));

            // 첫째와 둘째 발은 사거리 10m를 이미 지났고 셋째 발만 살아 있다
            Step(tracker, Cast(Idle(81.0), 1, SLOT_Q, ESkillPhase.Recovery, 80.3));
            Assert.That(CountProjectiles(), Is.EqualTo(1));
            Assert.That(FirstProjectile().StartAt, Is.EqualTo(80.3 + (2 * Q_INTERVAL)).Within(1e-9));

            _plays.Clear();
            Step(tracker, Cast(Idle(90.0), 2, SLOT_W, ESkillPhase.Recovery, 85.0));
            Assert.That(_plays, Is.Empty);
            Assert.That(tracker.HasPending, Is.False);
        }

        [Test]
        public void Update_BasicObservedBeyondRangeAfterFireTime_IsDropped()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(95.0));

            ArcherVfxTracker.Observation bump = Idle(95.0);
            bump.AttackSequence = 1;
            Step(tracker, bump);

            // 발사 시각 95.2에서 사거리 12m를 날아갈 시간(0.4초)을 넘겨 처음 도달을 보면 버린다
            bump.Now = 95.0 + BASIC_WINDUP + (BASIC_RANGE / BASIC_SPEED) + AFTER_DELAY_MARGIN;
            Step(tracker, bump);
            Assert.That(_plays, Is.Empty);
            Assert.That(tracker.HasPending, Is.False);
        }

        [Test]
        public void Update_BasicObservedLateWithinRange_ShowsArrowWithoutMuzzle()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(96.0));

            ArcherVfxTracker.Observation bump = Idle(96.0);
            bump.AttackSequence = 1;
            Step(tracker, bump);

            // 발사 시각 96.2에서 0.3초 늦게 보면 총구 효과는 생략하고 화살만 9m 나간 자리에서 시작한다
            bump.Now = 96.0 + BASIC_WINDUP + 0.3;
            Step(tracker, bump);
            Assert.That(CountMuzzles(), Is.EqualTo(0));
            Assert.That(CountProjectiles(), Is.EqualTo(1));
            Assert.That(ArcherVfxTracker.Distance(BASIC_SPEED, BASIC_RANGE, bump.Now - _plays[0].StartAt), Is.EqualTo(9f).Within(FLOAT_TOLERANCE));
        }

        [Test]
        public void Update_OtherClassObservation_PlaysNothingAndRebaselines()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(100.0));
            Assert.That(tracker.HasBaseline, Is.True);

            ArcherVfxTracker.Observation other = Cast(Idle(100.1), 3, SLOT_Q, ESkillPhase.Windup, 100.2);
            other.PlayerClass    = EPlayerClass.Swordsman;
            other.AttackSequence = 9;
            Step(tracker, other);
            other.Now        = 100.3;
            other.SkillPhase = ESkillPhase.Active;
            Step(tracker, other);

            Assert.That(_plays, Is.Empty);
            Assert.That(tracker.HasBaseline, Is.False);

            ArcherVfxTracker.Observation back = Cast(Idle(100.4), 3, SLOT_Q, ESkillPhase.Recovery, 100.2);
            back.AttackSequence = 9;
            Step(tracker, back);
            back.Now = 100.6;
            Step(tracker, back);

            Assert.That(_plays, Is.Empty);
            Assert.That(tracker.HasBaseline, Is.True);
        }

        [Test]
        public void Update_InvalidSpeedOrRange_PlaysNothing()
        {
            var tracker = new ArcherVfxTracker();
            var config  = new ArcherVfxTracker.Config();
            config.BasicWindup    = BASIC_WINDUP;
            config.MaxLateSeconds = MAX_LATE;
            config.Basic          = Shot(0f, BASIC_RANGE);
            tracker.Configure(in config);
            tracker.SetSkill(SLOT_Q, Shot(-5f, Q_RANGE));
            tracker.SetSkill(SLOT_W, Shot(W_SPEED, 0f));
            tracker.SetSkill(SLOT_E, Shot(float.NaN, E_RANGE));
            // R은 SetSkill을 부르지 않은 기본값(0)

            Step(tracker, Idle(110.0));
            ArcherVfxTracker.Observation bump = Idle(110.0);
            bump.AttackSequence = 1;
            Step(tracker, bump);
            Assert.That(tracker.HasPending, Is.False);

            byte[] slots = { SLOT_Q, SLOT_W, SLOT_E, SLOT_R };
            for (int i = 0; i < slots.Length; ++i)
            {
                Step(tracker, Cast(Idle(111.0 + i), i + 1, slots[i], ESkillPhase.Active, 111.0 + i));
            }

            Assert.That(_plays, Is.Empty);
            Assert.That(tracker.HasPending, Is.False);
        }

        [Test]
        public void Update_ZeroCountsAreNormalizedAndOutOfRangeSlotIsIgnored()
        {
            ArcherVfxTracker tracker = CreateTracker();
            Step(tracker, Idle(120.0));

            // W는 갈래와 반복 횟수가 0이라 한 갈래 한 번으로 다룬다
            Step(tracker, Cast(Idle(120.31), 1, SLOT_W, ESkillPhase.Active, 120.3));
            Assert.That(CountProjectiles(), Is.EqualTo(1));

            _plays.Clear();
            Step(tracker, Cast(Idle(121.31), 2, 9, ESkillPhase.Active, 121.3));
            Assert.That(_plays, Is.Empty);
        }

        [Test]
        public void Distance_IsPlainSpeedTimesElapsedClampedToRange()
        {
            Assert.That(ArcherVfxTracker.Distance(Q_SPEED, Q_RANGE, -1.0), Is.EqualTo(0f));
            Assert.That(ArcherVfxTracker.Distance(Q_SPEED, Q_RANGE, 0.0), Is.EqualTo(0f));
            Assert.That(ArcherVfxTracker.Distance(Q_SPEED, Q_RANGE, 0.1), Is.EqualTo(2.4f).Within(FLOAT_TOLERANCE));
            Assert.That(ArcherVfxTracker.Distance(Q_SPEED, Q_RANGE, 0.3), Is.EqualTo(7.2f).Within(FLOAT_TOLERANCE));
            Assert.That(ArcherVfxTracker.Distance(Q_SPEED, Q_RANGE, 5.0), Is.EqualTo(Q_RANGE));
            Assert.That(ArcherVfxTracker.Distance(0f, Q_RANGE, 1.0), Is.EqualTo(0f));
            Assert.That(ArcherVfxTracker.Distance(Q_SPEED, 0f, 1.0), Is.EqualTo(0f));
        }

        /****************************************
        *              Pool Tests
        ****************************************/

        [Test]
        public void Pool_ProjectileLateStart_BeginsAtElapsedDistanceAndMovesStraight()
        {
            ArcherVfxPool pool   = CreatePool(POOL_SIZE);
            Vector3       origin = new Vector3(1f, 0f, 1f);

            pool.PlayProjectile(origin, Vector3.forward, 200.0, Q_SPEED, Q_RANGE, 200.1);
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(1f + 2.4f).Within(FLOAT_TOLERANCE));
            Assert.That(pool.GetSlotPosition(0).x, Is.EqualTo(1f).Within(FLOAT_TOLERANCE));

            pool.Tick(200.2);
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(1f + 4.8f).Within(FLOAT_TOLERANCE));
            pool.Tick(200.3);
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(1f + 7.2f).Within(FLOAT_TOLERANCE));
        }

        [Test]
        public void Pool_ProjectileReachesRange_StopsAtRangeAndCleansUp()
        {
            ArcherVfxPool pool = CreatePool(POOL_SIZE);

            pool.PlayProjectile(Vector3.zero, Vector3.forward, 300.0, Q_SPEED, Q_RANGE, 300.0);
            pool.Tick(300.4);
            Assert.That(pool.ActiveCount, Is.EqualTo(1));
            Assert.That(pool.GetSlotPosition(0).z, Is.LessThan(Q_RANGE));

            pool.Tick(300.45);
            Assert.That(pool.ActiveCount, Is.EqualTo(1));
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(Q_RANGE).Within(FLOAT_TOLERANCE));

            // 사거리 끝에서 더 나아가지 않고, 잠시 뒤 정리된다
            pool.Tick(300.55);
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(Q_RANGE).Within(FLOAT_TOLERANCE));
            Assert.That(pool.ActiveCount, Is.EqualTo(1));

            pool.Tick(301.5);
            Assert.That(pool.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Pool_FiredProjectileSurvivesIdleDeathAndDodge_UntilRange()
        {
            ArcherVfxTracker tracker = CreateTracker();
            ArcherVfxPool    pool    = CreatePool(POOL_SIZE);
            Step(tracker, Idle(400.0));

            ArcherVfxTracker.Observation cast = Cast(Idle(400.31), 1, SLOT_W, ESkillPhase.Active, 400.3);
            cast.SkillAim = Vector3.forward;
            Observe(tracker, pool, cast);
            Assert.That(pool.ActiveCount, Is.EqualTo(1));

            // 발사 뒤 Idle, 사망, 회피가 와도 이미 나간 화살은 그대로 날아간다
            Observe(tracker, pool, Idle(400.35));
            ArcherVfxTracker.Observation dead = Idle(400.4);
            dead.LifeState = EParticipantLifeState.Dead;
            Observe(tracker, pool, dead);
            ArcherVfxTracker.Observation dodge = Idle(400.45);
            dodge.DodgeSequence = 1;
            Observe(tracker, pool, dodge);

            Assert.That(pool.ActiveCount, Is.EqualTo(1));
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(W_SPEED * 0.15f).Within(FLOAT_TOLERANCE));

            // 사거리 끝(약 0.417초)을 지나면 정리된다
            Observe(tracker, pool, Idle(400.75));
            Observe(tracker, pool, Idle(401.0));
            Assert.That(pool.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Pool_MoreThanFixedSize_RecyclesWithoutGrowing()
        {
            var root   = new GameObject("ArcherVfxTestRoot");
            var prefab = new GameObject("ArcherVfxTestPrefab");
            prefab.AddComponent<ParticleSystem>();
            _created.Add(root);
            _created.Add(prefab);
            var pool = new ArcherVfxPool(prefab, root.transform, POOL_SIZE);

            pool.PlayProjectile(Vector3.zero, Vector3.forward, 500.0, Q_SPEED, Q_RANGE, 500.0);
            pool.PlayProjectile(Vector3.one, Vector3.forward, 500.0, Q_SPEED, Q_RANGE, 500.0);
            pool.PlayProjectile(Vector3.up, Vector3.forward, 500.0, Q_SPEED, Q_RANGE, 500.0);

            Assert.That(pool.SlotCount, Is.EqualTo(POOL_SIZE));
            Assert.That(pool.ActiveCount, Is.EqualTo(POOL_SIZE));
            Assert.That(root.transform.childCount, Is.EqualTo(POOL_SIZE));
            Assert.That(Vector3.Distance(pool.GetSlotPosition(0), Vector3.up), Is.LessThan(FLOAT_TOLERANCE));
        }

        [Test]
        public void Pool_StopAll_ClearsEverySlot()
        {
            ArcherVfxPool pool = CreatePool(POOL_SIZE);

            pool.PlayProjectile(Vector3.zero, Vector3.forward, 600.0, Q_SPEED, Q_RANGE, 600.0);
            pool.PlayBrief(Vector3.zero, Quaternion.identity, 600.0, 1f);
            Assert.That(pool.ActiveCount, Is.EqualTo(2));

            pool.StopAll();
            Assert.That(pool.ActiveCount, Is.EqualTo(0));
            Assert.That(pool.IsSlotActive(0), Is.False);
            Assert.That(pool.IsSlotActive(1), Is.False);
        }

        [Test]
        public void Pool_BriefMuzzle_EndsAtMaxSeconds()
        {
            ArcherVfxPool pool = CreatePool(POOL_SIZE);

            pool.PlayBrief(Vector3.zero, Quaternion.identity, 700.0, 1.5f);
            Assert.That(pool.ActiveCount, Is.EqualTo(1));

            pool.Tick(701.6);
            Assert.That(pool.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Pool_MissingPrefabOrInvalidSpeed_NeverThrowsAndStaysEmpty()
        {
            var root = new GameObject("ArcherVfxTestRoot");
            _created.Add(root);
            var missing = new ArcherVfxPool(null, root.transform, POOL_SIZE);

            Assert.That(missing.HasPrefab, Is.False);
            Assert.DoesNotThrow(() =>
            {
                missing.PlayBrief(Vector3.zero, Quaternion.identity, 0.0, 1f);
                missing.PlayProjectile(Vector3.zero, Vector3.forward, 0.0, Q_SPEED, Q_RANGE, 0.0);
                missing.Tick(1.0);
                missing.StopAll();
            });
            Assert.That(missing.ActiveCount, Is.EqualTo(0));
            Assert.That(missing.IsSlotActive(0), Is.False);

            ArcherVfxPool pool = CreatePool(POOL_SIZE);
            pool.PlayProjectile(Vector3.zero, Vector3.forward, 0.0, 0f, Q_RANGE, 0.0);
            pool.PlayProjectile(Vector3.zero, Vector3.forward, 0.0, Q_SPEED, -1f, 0.0);
            Assert.That(pool.ActiveCount, Is.EqualTo(0));
        }

        /****************************************
        *            Element Tests
        ****************************************/

        [TestCase(EArcherElement.None)]
        [TestCase(EArcherElement.Ice)]
        [TestCase(EArcherElement.Fire)]
        public void Update_BasicWithElement_CarriesElementAtBaseSpeed(EArcherElement element)
        {
            ArcherVfxTracker tracker = CreateElementTracker();
            Step(tracker, Idle(200.0));
            ArcherVfxTracker.Observation bump = Idle(200.0);
            bump.AttackSequence = 1;
            bump.ArcherElement  = element;
            Step(tracker, bump);
            bump.Now = 200.0 + BASIC_WINDUP + AFTER_DELAY_MARGIN;
            Step(tracker, bump);

            Assert.That(_plays.Count, Is.EqualTo(2));
            foreach (ArcherVfxTracker.Play play in _plays)
            {
                Assert.That(play.Kind, Is.EqualTo(EArcherVfxKind.Arrow));
                Assert.That(play.Element, Is.EqualTo(element));
            }

            Assert.That(FirstProjectile().Speed, Is.EqualTo(BASIC_SPEED).Within(FLOAT_TOLERANCE));
        }

        [Test]
        public void Update_BasicWithWind_FliesFasterByWindScale()
        {
            ArcherVfxTracker tracker = CreateElementTracker();
            Step(tracker, Idle(210.0));
            ArcherVfxTracker.Observation bump = Idle(210.0);
            bump.AttackSequence = 1;
            bump.ArcherElement  = EArcherElement.Wind;
            Step(tracker, bump);
            bump.Now = 210.0 + BASIC_WINDUP + AFTER_DELAY_MARGIN;
            Step(tracker, bump);

            ArcherVfxTracker.Play arrow = FirstProjectile();
            Assert.That(arrow.Element, Is.EqualTo(EArcherElement.Wind));
            Assert.That(arrow.Speed, Is.EqualTo(BASIC_SPEED * WIND_SPEED_SCALE).Within(FLOAT_TOLERANCE));
        }

        [Test]
        public void Update_ArrowHitSequenceBumped_PlaysHitAtServerPositionEvenWhileDodging()
        {
            ArcherVfxTracker tracker = CreateElementTracker();
            ArcherVfxTracker.Observation obs = Idle(220.0);
            obs.ArrowHitSequence = 7;
            Step(tracker, obs);
            Assert.That(_plays, Is.Empty, "첫 관찰의 지난 적중은 재생하지 않는다");

            obs.Now              = 220.1;
            obs.ArrowHitSequence = 8;
            obs.ArrowHitPosition = new Vector3(3f, 0f, 4f);
            obs.ArrowHitElement  = EArcherElement.Fire;
            obs.DodgeSequence    = 1;
            Step(tracker, obs);

            Assert.That(_plays.Count, Is.EqualTo(1));
            Assert.That(_plays[0].Kind, Is.EqualTo(EArcherVfxKind.Hit));
            Assert.That(_plays[0].Element, Is.EqualTo(EArcherElement.Fire));
            Assert.That(_plays[0].HasPosition, Is.True);
            Assert.That(_plays[0].Position, Is.EqualTo(new Vector3(3f, 0f, 4f)));

            obs.Now = 220.2;
            Step(tracker, obs);
            Assert.That(_plays.Count, Is.EqualTo(1), "같은 순번은 다시 재생하지 않는다");
        }

        [Test]
        public void Update_ElementSkill_PlaysSwitchOnceAtResolveWithItsElement()
        {
            ArcherVfxTracker tracker = CreateElementTracker();
            Step(tracker, Idle(230.0));
            Step(tracker, Cast(Idle(230.0), 11, SLOT_W, ESkillPhase.Windup, 230.1));
            Assert.That(_plays, Is.Empty);

            Step(tracker, Cast(Idle(230.12), 11, SLOT_W, ESkillPhase.Recovery, 230.1));
            Step(tracker, Cast(Idle(230.2), 11, SLOT_W, ESkillPhase.Recovery, 230.1));

            Assert.That(_plays.Count, Is.EqualTo(1));
            Assert.That(_plays[0].Kind, Is.EqualTo(EArcherVfxKind.Switch));
            Assert.That(_plays[0].Element, Is.EqualTo(EArcherElement.Wind));
        }

        [Test]
        public void Update_ElementSkillCancelledInWindup_PlaysNothing()
        {
            ArcherVfxTracker tracker = CreateElementTracker();
            Step(tracker, Idle(235.0));
            Step(tracker, Cast(Idle(235.0), 12, SLOT_Q, ESkillPhase.Windup, 235.1));
            Step(tracker, Cast(Idle(235.05), 12, SLOT_Q, ESkillPhase.Idle, 235.1));
            Step(tracker, Cast(Idle(235.3), 12, SLOT_Q, ESkillPhase.Idle, 235.1));

            Assert.That(_plays, Is.Empty);
        }

        [Test]
        public void Update_BurstSkill_PlaysChargeThenBurstAtSkillPoint()
        {
            ArcherVfxTracker tracker = CreateElementTracker();
            Step(tracker, Idle(240.0));
            ArcherVfxTracker.Observation cast = Cast(Idle(240.0), 13, SLOT_R, ESkillPhase.Windup, 240.7);
            cast.SkillPoint = new Vector3(5f, 0f, 6f);
            Step(tracker, cast);

            Assert.That(_plays.Count, Is.EqualTo(1));
            Assert.That(_plays[0].Kind, Is.EqualTo(EArcherVfxKind.Charge));

            cast.Now = 240.5;
            Step(tracker, cast);
            Assert.That(_plays.Count, Is.EqualTo(1));

            cast.Now        = 240.72;
            cast.SkillPhase = ESkillPhase.Recovery;
            Step(tracker, cast);

            Assert.That(_plays.Count, Is.EqualTo(2));
            Assert.That(_plays[1].Kind, Is.EqualTo(EArcherVfxKind.Burst));
            Assert.That(_plays[1].HasPosition, Is.True);
            Assert.That(_plays[1].Position, Is.EqualTo(new Vector3(5f, 0f, 6f)));
        }

        [Test]
        public void HitStopDistance_LateralWithinLimit_StopsAtProjectedDistance()
        {
            bool hit = ArcherVfxTracker.TryGetHitStopDistance(Vector3.zero, Vector3.forward, Q_RANGE, new Vector3(HIT_LATERAL, 0f, 4f),
                HIT_MAX_LATERAL, out float stop, out float lateral);

            Assert.That(hit, Is.True);
            Assert.That(stop, Is.EqualTo(4f).Within(FLOAT_TOLERANCE));
            Assert.That(lateral, Is.EqualTo(HIT_LATERAL).Within(FLOAT_TOLERANCE));
        }

        [Test]
        public void HitStopDistance_BeyondLateralOrPath_False()
        {
            Assert.That(ArcherVfxTracker.TryGetHitStopDistance(Vector3.zero, Vector3.forward, Q_RANGE,
                new Vector3(HIT_MAX_LATERAL + 0.1f, 0f, 4f), HIT_MAX_LATERAL, out _, out _), Is.False);
            Assert.That(ArcherVfxTracker.TryGetHitStopDistance(Vector3.zero, Vector3.forward, Q_RANGE,
                new Vector3(0f, 0f, Q_RANGE + HIT_MAX_LATERAL + 0.1f), HIT_MAX_LATERAL, out _, out _), Is.False);
            Assert.That(ArcherVfxTracker.TryGetHitStopDistance(Vector3.zero, Vector3.forward, Q_RANGE,
                new Vector3(0f, 0f, -(HIT_MAX_LATERAL + 0.1f)), HIT_MAX_LATERAL, out _, out _), Is.False);
        }

        [Test]
        public void HitStopDistance_BigBodyCenterBehindOrPastRange_ClampsToPath()
        {
            Assert.That(ArcherVfxTracker.TryGetHitStopDistance(Vector3.zero, Vector3.forward, Q_RANGE, new Vector3(0f, 0f, -1f),
                HIT_MAX_LATERAL, out float behind, out _), Is.True);
            Assert.That(behind, Is.EqualTo(0f));

            Assert.That(ArcherVfxTracker.TryGetHitStopDistance(Vector3.zero, Vector3.forward, Q_RANGE, new Vector3(0f, 0f, Q_RANGE + 1f),
                HIT_MAX_LATERAL, out float past, out _), Is.True);
            Assert.That(past, Is.EqualTo(Q_RANGE));
        }

        [Test]
        public void Pool_StopProjectileAtHit_LateSignalSnapsBackToHitDistance()
        {
            // 지연으로 화살이 적을 3m 넘게 지나친 뒤 신호가 와도 적중 거리로 되돌아가 멈춘다. 옆으로 2m가 넘는 큰 몸체 적중이다
            ArcherVfxPool pool = CreatePool(POOL_SIZE);
            pool.PlayProjectile(Vector3.zero, Vector3.forward, 0.0, Q_SPEED, Q_RANGE, 0.0, true);
            pool.Tick(0.3);

            Assert.That(pool.StopProjectileAtHit(new Vector3(HIT_LATERAL, 0f, 4f), HIT_MAX_LATERAL, 0.3), Is.True);
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(4f).Within(FLOAT_TOLERANCE));
            pool.Tick(0.35);
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(4f).Within(FLOAT_TOLERANCE), "멈춘 화살은 더 나아가지 않는다");

            pool.Tick(1.0);
            Assert.That(pool.IsSlotActive(0), Is.False);
        }

        [Test]
        public void Pool_StopProjectileAtHit_EarlySignalFliesOnlyToHitDistance()
        {
            ArcherVfxPool pool = CreatePool(POOL_SIZE);
            pool.PlayProjectile(Vector3.zero, Vector3.forward, 0.0, Q_SPEED, Q_RANGE, 0.0, true);
            pool.Tick(0.1);

            Assert.That(pool.StopProjectileAtHit(new Vector3(0f, 0f, 6f), HIT_MAX_LATERAL, 0.1), Is.True);
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(Q_SPEED * 0.1f).Within(FLOAT_TOLERANCE));

            pool.Tick(0.4);
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(6f).Within(FLOAT_TOLERANCE));
            pool.Tick(1.0);
            Assert.That(pool.IsSlotActive(0), Is.False);
        }

        [Test]
        public void Pool_StopProjectileAtHit_IgnoresSkillArrowAndPicksNearestLane()
        {
            ArcherVfxPool pool = CreatePool(POOL_SIZE);
            pool.PlayProjectile(Vector3.zero, Vector3.forward, 0.0, Q_SPEED, Q_RANGE, 0.0);
            pool.PlayProjectile(Vector3.right * 10f, Vector3.forward, 0.0, Q_SPEED, Q_RANGE, 0.0, true);
            pool.Tick(0.1);

            Assert.That(pool.StopProjectileAtHit(new Vector3(0f, 0f, 2f), HIT_MAX_LATERAL, 0.1), Is.False, "기술 화살은 멈추지 않는다");
            Assert.That(pool.StopProjectileAtHit(new Vector3(9f, 0f, 2f), HIT_MAX_LATERAL, 0.1), Is.True);

            pool.Tick(0.2);
            Assert.That(pool.GetSlotPosition(0).z, Is.EqualTo(Q_SPEED * 0.2f).Within(FLOAT_TOLERANCE));
            Assert.That(pool.GetSlotPosition(1).z, Is.EqualTo(2f).Within(FLOAT_TOLERANCE));
            Assert.That(pool.StopProjectileAtHit(new Vector3(10f, 0f, 3f), HIT_MAX_LATERAL, 0.2), Is.False, "이미 멈춘 화살은 다시 고르지 않는다");
        }

        /****************************************
        *              View Tests
        ****************************************/

        [Test]
        public void View_PoolsBuiltOnlyForAssignedPrefabsAndRemovedOnDestroy()
        {
            const string POOL_ROOT_NAME = "ArcherVfxPool";

            var prefab = new GameObject("ArcherVfxTestPrefab");
            prefab.AddComponent<ParticleSystem>();
            var host = new GameObject("ArcherVfxTestHost");
            _created.Add(prefab);
            _created.Add(host);
            var view = host.AddComponent<ArcherVfxView>();

            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(ArcherVfxView).GetField("_arrowPrefab", flags).SetValue(view, prefab);
            typeof(ArcherVfxView).GetMethod("EnsurePools", flags).Invoke(view, null);

            GameObject root = GameObject.Find(POOL_ROOT_NAME);
            Assert.That(root, Is.Not.Null);
            _created.Add(root);

            // 화살 풀 칸만 만들어지고 프리팹이 없는 풀은 아무것도 만들지 않는다
            Assert.That(root.transform.childCount, Is.EqualTo(VIEW_ARROW_POOL));

            // 에디트 모드에서는 라이프사이클 호출이 보장되지 않아 정리 메서드를 직접 부른다
            typeof(ArcherVfxView).GetMethod("OnDisable", flags).Invoke(view, null);
            typeof(ArcherVfxView).GetMethod("OnDestroy", flags).Invoke(view, null);
            Assert.That(GameObject.Find(POOL_ROOT_NAME), Is.Null);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static ArcherVfxTracker.ShotSpec Shot(float speed, float range, int prongs = 1, float spread = 0f, int repeat = 1, float interval = 0f)
        {
            var spec = new ArcherVfxTracker.ShotSpec();
            spec.Speed           = speed;
            spec.Range           = range;
            spec.ProjectileCount = prongs;
            spec.SpreadAngle     = spread;
            spec.RepeatCount     = repeat;
            spec.RepeatInterval  = interval;
            return spec;
        }

        private static ArcherVfxTracker CreateTracker(bool basicCancelOnMove = true)
        {
            var tracker = new ArcherVfxTracker();
            var config  = new ArcherVfxTracker.Config();
            config.BasicWindup       = BASIC_WINDUP;
            config.BasicCancelOnMove = basicCancelOnMove;
            config.Basic             = Shot(BASIC_SPEED, BASIC_RANGE);
            config.MaxLateSeconds    = MAX_LATE;
            tracker.Configure(in config);
            tracker.SetSkill(SLOT_Q, Shot(Q_SPEED, Q_RANGE, 1, 0f, Q_REPEAT, Q_INTERVAL));
            // W는 갈래와 반복 횟수를 0으로 두어도 한 갈래 한 번으로 다뤄야 한다
            tracker.SetSkill(SLOT_W, Shot(W_SPEED, W_RANGE, 0, 0f, 0, 0f));
            tracker.SetSkill(SLOT_E, Shot(E_SPEED, E_RANGE));
            tracker.SetSkill(SLOT_R, Shot(R_SPEED, R_RANGE, R_PRONGS, R_SPREAD));
            return tracker;
        }

        // 서버 틱 간격을 아는 실제 뷰 구성과 같은 모양이다
        private static ArcherVfxTracker CreateTickTracker()
        {
            var tracker = new ArcherVfxTracker();
            var config  = new ArcherVfxTracker.Config();
            config.BasicWindup       = TICK_WINDUP;
            config.BasicCancelOnMove = true;
            config.Basic             = Shot(BASIC_SPEED, BASIC_RANGE);
            config.MaxLateSeconds    = MAX_LATE;
            config.ServerTickSeconds = SERVER_TICK;
            tracker.Configure(in config);
            return tracker;
        }

        // 속성형 실제 구성과 같은 모양, Q/W/E는 속성 부여, R은 속성 폭발이다
        private static ArcherVfxTracker CreateElementTracker()
        {
            var tracker = new ArcherVfxTracker();
            var config  = new ArcherVfxTracker.Config();
            config.BasicWindup         = BASIC_WINDUP;
            config.BasicCancelOnMove   = true;
            config.Basic               = Shot(BASIC_SPEED, BASIC_RANGE);
            config.MaxLateSeconds      = MAX_LATE;
            config.WindSpeedScale      = WIND_SPEED_SCALE;
            config.HasBurstSlot        = true;
            config.BurstSlot           = SLOT_R;
            config.MaxBurstLateSeconds = 1f;
            tracker.Configure(in config);
            tracker.SetSkillElement(SLOT_Q, EArcherElement.Ice);
            tracker.SetSkillElement(SLOT_W, EArcherElement.Wind);
            tracker.SetSkillElement(SLOT_E, EArcherElement.Fire);
            return tracker;
        }

        private ArcherVfxPool CreatePool(int size)
        {
            var prefab = new GameObject("ArcherVfxTestPrefab");
            prefab.AddComponent<ParticleSystem>();
            var root = new GameObject("ArcherVfxTestRoot");
            _created.Add(prefab);
            _created.Add(root);
            return new ArcherVfxPool(prefab, root.transform, size);
        }

        private static ArcherVfxTracker.Observation Idle(double now)
        {
            var obs = new ArcherVfxTracker.Observation();
            obs.PlayerClass = EPlayerClass.Archer;
            obs.LifeState   = EParticipantLifeState.Alive;
            obs.SkillPhase  = ESkillPhase.Idle;
            obs.SkillKind   = ESkillRequestKind.Regular;
            obs.Facing      = Vector3.forward;
            obs.SkillAim    = Vector3.forward;
            obs.Now         = now;
            return obs;
        }

        private static ArcherVfxTracker.Observation Cast(ArcherVfxTracker.Observation source, int castId, byte slot, ESkillPhase phase, double resolveAt)
        {
            source.CastInstanceId = castId;
            source.SkillSlot      = slot;
            source.SkillPhase     = phase;
            source.SkillResolveAt = resolveAt;
            return source;
        }

        // 한 프레임을 반영하고, 그 프레임에 재생된 효과를 _plays에 쌓는다
        private int Step(ArcherVfxTracker tracker, ArcherVfxTracker.Observation obs)
        {
            int count = tracker.Update(in obs);
            for (int i = 0; i < count; ++i)
            {
                _plays.Add(tracker.GetPlay(i));
            }

            return count;
        }

        // 뷰의 Update와 같은 순서로 추적기를 갱신하고 투사체를 풀에 넘긴 뒤 풀을 갱신한다
        private void Observe(ArcherVfxTracker tracker, ArcherVfxPool pool, ArcherVfxTracker.Observation obs)
        {
            int count = Step(tracker, obs);
            for (int i = 0; i < count; ++i)
            {
                ArcherVfxTracker.Play play = tracker.GetPlay(i);
                if (!play.IsMuzzle)
                {
                    pool.PlayProjectile(Vector3.zero, play.Direction, play.StartAt, play.Speed, play.Range, obs.Now, play.StopsOnHit);
                }
            }

            pool.Tick(obs.Now);
        }

        private int CountProjectiles()
        {
            int count = 0;
            for (int i = 0; i < _plays.Count; ++i)
            {
                if (!_plays[i].IsMuzzle)
                {
                    ++count;
                }
            }

            return count;
        }

        private int CountMuzzles()
        {
            return _plays.Count - CountProjectiles();
        }

        private ArcherVfxTracker.Play FirstProjectile()
        {
            for (int i = 0; i < _plays.Count; ++i)
            {
                if (!_plays[i].IsMuzzle)
                {
                    return _plays[i];
                }
            }

            Assert.Fail("투사체 재생이 없다");
            return default;
        }
    }
}
