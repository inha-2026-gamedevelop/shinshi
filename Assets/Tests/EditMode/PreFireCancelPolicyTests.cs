// System
using System;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Tests
{
    // 발사 전 동작 취소 사유표와, 그 표를 실제 PlayerServer가 어떻게 적용하는지 본다
    public sealed class PreFireCancelPolicyTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const double START_TIME   = 100.0;
        private const double TICK_STEP    = 1.0 / 30.0;
        private const int    PARTICIPANT  = 1;
        private const int    PATH_BUDGET  = 4;
        private const float  ENEMY_Z      = 9f;
        private const float  SLOW_SPEED   = 6f;
        private const float  RANGE        = 10f;
        private const float  HUGE_HEALTH  = 1e9f;
        private const int    LEGACY_COUNT = 3;
        private const int    MAX_STEPS    = 60;

        // PlayerServerTestRig가 만드는 서버 플레이어의 세션 번호
        private const int    SESSION_EPOCH  = 1;
        private const uint   MOVE_SEQUENCE  = 7;
        private const float  MOVE_DISTANCE  = 2f;
        private const int    COMBO_STEP     = 1;
        private const double IMMUNE_UNTIL   = 1e9;

        // 발사 틱과 이동 시작 틱, 틱 경계 반올림을 합친 여유
        private const int    HOLD_SLACK_TICKS = 3;
        private const int    FIRST_STEP       = 0;
        private const float  SECONDS_EPSILON  = 1e-6f;

        private PlayerServerTestRig _rig;
        private PlayerServer        _player;
        private PathRequestBudget   _budget;
        private double              _now;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _rig    = new PlayerServerTestRig();
            _budget = new PathRequestBudget(PATH_BUDGET);
            _now    = START_TIME;
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
        public void Get_EveryReason_ReturnsNonEmptyTargets()
        {
            foreach (EPreFireCancelReason reason in Enum.GetValues(typeof(EPreFireCancelReason)))
            {
                Assert.AreNotEqual(EPreFireTargets.None, PreFireCancelPolicy.Get(reason).Targets, reason.ToString());
            }
        }

        [Test]
        public void Get_UndefinedReason_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PreFireCancelPolicy.Get((EPreFireCancelReason)byte.MaxValue));
        }

        [Test]
        public void Get_OnlyShutdown_ClearsFiredArrows()
        {
            foreach (EPreFireCancelReason reason in Enum.GetValues(typeof(EPreFireCancelReason)))
            {
                bool clearsArrows = PreFireCancelPolicy.Get(reason).Has(EPreFireTargets.FiredArrows);
                Assert.AreEqual(reason == EPreFireCancelReason.Shutdown, clearsArrows, reason.ToString());
            }
        }

        [Test]
        public void Get_DetachVersusShutdown_DetachKeepsArrowsAndDodgeButClearsReviveRequest()
        {
            PreFireCancelPolicy detach   = PreFireCancelPolicy.Get(EPreFireCancelReason.Detach);
            PreFireCancelPolicy shutdown = PreFireCancelPolicy.Get(EPreFireCancelReason.Shutdown);

            Assert.IsFalse(detach.Has(EPreFireTargets.FiredArrows));
            Assert.IsFalse(detach.Has(EPreFireTargets.ActiveDodge));
            Assert.IsTrue(detach.Has(EPreFireTargets.ReviveRequest));
            Assert.IsTrue(shutdown.Has(EPreFireTargets.FiredArrows));
            Assert.IsTrue(shutdown.Has(EPreFireTargets.ActiveDodge));
            Assert.IsFalse(shutdown.Has(EPreFireTargets.ReviveRequest));
        }

        [Test]
        public void Get_DetachAndShutdown_DropPendingInputSilently()
        {
            foreach (EPreFireCancelReason reason in new[] { EPreFireCancelReason.Detach, EPreFireCancelReason.Shutdown })
            {
                PreFireCancelPolicy policy = PreFireCancelPolicy.Get(reason);
                Assert.IsTrue(policy.Has(EPreFireTargets.PendingSkill), reason.ToString());
                Assert.IsTrue(policy.Has(EPreFireTargets.PendingDodge), reason.ToString());
                Assert.AreEqual(ERejectReason.None, policy.SkillRejectReason, reason.ToString());
                Assert.AreEqual(ERejectReason.None, policy.DodgeRejectReason, reason.ToString());
            }
        }

        [TestCase(EPreFireCancelReason.SkillCommit, ERejectReason.WrongPhase, ERejectReason.None)]
        [TestCase(EPreFireCancelReason.SkillDodgeCancel, ERejectReason.WrongPhase, ERejectReason.None)]
        [TestCase(EPreFireCancelReason.SkillCombatStop, ERejectReason.WrongPhase, ERejectReason.None)]
        [TestCase(EPreFireCancelReason.Death, ERejectReason.Dead, ERejectReason.Dead)]
        [TestCase(EPreFireCancelReason.ControlInterrupt, ERejectReason.None, ERejectReason.Disabled)]
        [TestCase(EPreFireCancelReason.StunInterrupt, ERejectReason.Disabled, ERejectReason.None)]
        public void Get_ReasonWithClientNotice_ReturnsExpectedRejectReasons(EPreFireCancelReason reason, ERejectReason skill,
            ERejectReason dodge)
        {
            PreFireCancelPolicy policy = PreFireCancelPolicy.Get(reason);

            Assert.AreEqual(skill, policy.SkillRejectReason);
            Assert.AreEqual(dodge, policy.DodgeRejectReason);
        }

        [Test]
        public void Get_SkillBufferedAndSkillStops_DoNotTouchMovementOrDodge()
        {
            Assert.AreEqual(EPreFireTargets.PendingAttack, PreFireCancelPolicy.Get(EPreFireCancelReason.SkillBuffered).Targets);
            Assert.AreEqual(EPreFireTargets.PendingSkill, PreFireCancelPolicy.Get(EPreFireCancelReason.SkillDodgeCancel).Targets);
            Assert.AreEqual(EPreFireTargets.PendingSkill, PreFireCancelPolicy.Get(EPreFireCancelReason.SkillCombatStop).Targets);
        }

        [Test]
        public void Get_StunVersusRootControl_OnlyStunCutsAttackWindupAndSkillInput()
        {
            PreFireCancelPolicy control = PreFireCancelPolicy.Get(EPreFireCancelReason.ControlInterrupt);
            PreFireCancelPolicy stun    = PreFireCancelPolicy.Get(EPreFireCancelReason.StunInterrupt);

            Assert.IsFalse(control.Has(EPreFireTargets.PendingAttack));
            Assert.IsFalse(control.Has(EPreFireTargets.Windup));
            Assert.IsFalse(control.Has(EPreFireTargets.PendingSkill));
            Assert.IsTrue(stun.Has(EPreFireTargets.PendingAttack));
            Assert.IsTrue(stun.Has(EPreFireTargets.Windup));
            Assert.IsTrue(stun.Has(EPreFireTargets.PendingSkill));
        }

        [Test]
        public void Get_MoveInputReasons_TouchOnlyAttackIntentWindupAndComboImmunity()
        {
            PreFireCancelPolicy keepWindup   = PreFireCancelPolicy.Get(EPreFireCancelReason.MoveInput);
            PreFireCancelPolicy cancelWindup = PreFireCancelPolicy.Get(EPreFireCancelReason.MoveInputWindupCancel);

            Assert.AreEqual(EPreFireTargets.PendingAttack | EPreFireTargets.ComboImmunity, keepWindup.Targets);
            Assert.AreEqual(EPreFireTargets.PendingAttack | EPreFireTargets.Windup | EPreFireTargets.ComboImmunity, cancelWindup.Targets);
            foreach (PreFireCancelPolicy policy in new[] { keepWindup, cancelWindup })
            {
                // 단계(Combo)와 보관 이동(PendingMove)은 지우지 않는다. 보관 이동은 HandleMove 뒤쪽이 이번 입력으로 덮어쓴다
                Assert.IsFalse(policy.Has(EPreFireTargets.Combo));
                Assert.IsFalse(policy.Has(EPreFireTargets.PendingMove));
                Assert.IsFalse(policy.Has(EPreFireTargets.Path));
                Assert.AreEqual(ERejectReason.None, policy.SkillRejectReason);
                Assert.AreEqual(ERejectReason.None, policy.DodgeRejectReason);
            }
        }

        [Test]
        public void Get_MoveInputHeld_ClearsOnlyPendingAttack()
        {
            PreFireCancelPolicy held = PreFireCancelPolicy.Get(EPreFireCancelReason.MoveInputHeld);

            Assert.AreEqual(EPreFireTargets.PendingAttack, held.Targets, "동작, 준비, 면역은 동작이 끝날 때까지 둔다");
            Assert.AreEqual(ERejectReason.None, held.SkillRejectReason);
            Assert.AreEqual(ERejectReason.None, held.DodgeRejectReason);
        }

        [Test]
        public void HandleMove_DuringSwordsmanWindup_HoldsMoveOnlyUntilFire()
        {
            _player = _rig.Add(PARTICIPANT, Vector3.zero, EPlayerClass.Swordsman);
            SetField("_pendingAimYaw", FacingRules.EncodeYaw(Vector3.forward));
            SetField("_hasPendingAttack", true);
            StepWithMovement();

            double attackStart = _now;
            double nextAttack  = (double)GetField("_nextAttackTime");
            Assert.IsTrue((bool)GetField("_isWindingUp"), "검사 기본 공격은 준비 시간이 있다");

            _player.HandleMove(new PlayerMoveInput(Header(MOVE_SEQUENCE), new Vector3(MOVE_DISTANCE, 0f, 0f)));
            Assert.IsTrue((bool)GetField("_isWindingUp"), "준비 중 이동은 검사 공격 준비를 끊지 않는다");
            Assert.IsTrue((bool)GetField("_hasPendingMove"));

            // 발사할 때까지만 보관한다
            for (int i = 0; (i < MAX_STEPS) && ((bool)GetField("_isWindingUp")); ++i)
            {
                StepWithMovement();
                Assert.IsTrue((bool)GetField("_hasPendingMove"), $"발사 전에는 이동을 시작하지 않는다, now={_now}");
            }

            StepWithMovement();
            Assert.IsFalse((bool)GetField("_hasPendingMove"), "발사 다음 틱에 보관한 이동을 시작한다");
            Assert.Less(_now, nextAttack, "공격 주기 전체가 아니라 준비 시간만 이동을 막는다");
            Assert.Less(_now - attackStart, _rig.Config.GetBasicAttack(EPlayerClass.Swordsman).GetWindup(FIRST_STEP) + (HOLD_SLACK_TICKS * TICK_STEP));
        }

        [Test]
        public void HandleMove_AfterFireBeforeNextAttack_MovesWithoutWaitingForInterval()
        {
            _player = _rig.Add(PARTICIPANT, Vector3.zero, EPlayerClass.Swordsman);
            SetField("_pendingAimYaw", FacingRules.EncodeYaw(Vector3.forward));
            SetField("_hasPendingAttack", true);
            StepWithMovement();
            for (int i = 0; (i < MAX_STEPS) && ((bool)GetField("_isWindingUp")); ++i)
            {
                StepWithMovement();
            }
            double nextAttack = (double)GetField("_nextAttackTime");
            Assert.Less(_now, nextAttack, "발사 뒤에도 공격 주기는 남아 있어야 한다");

            _player.HandleMove(new PlayerMoveInput(Header(MOVE_SEQUENCE), new Vector3(MOVE_DISTANCE, 0f, 0f)));
            StepWithMovement();

            Assert.IsFalse((bool)GetField("_hasPendingMove"), "발사 뒤 이동은 남은 동작을 끊고 바로 시작한다");
        }

        [Test]
        public void HandleMove_DuringArcherWindup_CancelsShotAndMovesNextTick()
        {
            _player = _rig.Add(PARTICIPANT, Vector3.zero, EPlayerClass.Archer);
            SetField("_pendingAimYaw", FacingRules.EncodeYaw(Vector3.forward));
            SetField("_hasPendingAttack", true);
            StepWithMovement();
            Assert.IsTrue((bool)GetField("_isWindingUp"), "궁수 기본 공격은 준비 시간이 있다");
            SetField("_comboImmuneUntil", IMMUNE_UNTIL);
            byte cancelBefore = _rig.Combats[0].ActionCancelSequence;

            _player.HandleMove(new PlayerMoveInput(Header(MOVE_SEQUENCE), new Vector3(MOVE_DISTANCE, 0f, 0f)));

            Assert.IsFalse((bool)GetField("_isWindingUp"), "궁수는 준비 중 이동으로 발사를 버린다");
            Assert.AreEqual(unchecked((byte)(cancelBefore + 1)), _rig.Combats[0].ActionCancelSequence,
                "경로를 못 만들어도 관찰자가 취소를 알 수 있게 취소 순번을 올린다");
            Assert.AreEqual(0.0, (double)GetField("_comboImmuneUntil"), "이동 입력이 들어오면 면역은 바로 끝난다");

            StepWithMovement();
            Assert.IsFalse((bool)GetField("_hasPendingMove"), "준비를 버린 다음 틱에 바로 움직인다");
            Assert.AreEqual(0, (int)GetField("_arrowCount"), "취소한 준비는 화살을 쏘지 않는다");
        }

        [Test]
        public void HandleMove_SameDestinationWhileHeld_KeepsFirstSequence()
        {
            _player = _rig.Add(PARTICIPANT, Vector3.zero, EPlayerClass.Swordsman);
            SetField("_pendingAimYaw", FacingRules.EncodeYaw(Vector3.forward));
            SetField("_hasPendingAttack", true);
            StepWithMovement();
            var destination = new Vector3(MOVE_DISTANCE, 0f, 0f);

            _player.HandleMove(new PlayerMoveInput(Header(MOVE_SEQUENCE), destination));
            _player.HandleMove(new PlayerMoveInput(Header(MOVE_SEQUENCE + 1), destination));

            Assert.AreEqual(MOVE_SEQUENCE, (uint)GetField("_pendingMoveSequence"), "보관한 같은 목적지는 앞 순번을 그대로 둔다");
        }

        [TestCase(EPlayerClass.Swordsman)]
        [TestCase(EPlayerClass.Mageknight)]
        [TestCase(EPlayerClass.Archer)]
        public void GetMoveHoldSeconds_MatchesServerWindupLock(EPlayerClass playerClass)
        {
            PlayerConfigSO.BasicAttackSpec spec = _rig.Config.GetBasicAttack(playerClass);
            for (int step = 0; step < spec.ComboStepCount; ++step)
            {
                float expected = spec.CancelOnMove
                    ? 0f
                    : spec.GetWindup(step) + ((spec.GetFiredShotCount(step) - 1) * spec.GetShotInterval(step));
                Assert.AreEqual(expected, spec.GetMoveHoldSeconds(step), SECONDS_EPSILON, $"step={step}");
                Assert.LessOrEqual(spec.GetMoveHoldSeconds(step), spec.GetMaxMoveHoldSeconds());
                Assert.Less(spec.GetMoveHoldSeconds(step), spec.GetInterval(step), "보관 구간은 공격 주기보다 짧다");
            }
        }

        [Test]
        public void HandleMove_CancelOnMoveClass_ClearsAttackWindupAndComboImmunityButKeepsComboStep()
        {
            _player = _rig.Add(PARTICIPANT, Vector3.zero, EPlayerClass.Archer);
            SetField("_hasPendingAttack", true);
            SetField("_isWindingUp", true);
            SetField("_comboStep", COMBO_STEP);
            SetField("_comboImmuneUntil", IMMUNE_UNTIL);

            _player.HandleMove(new PlayerMoveInput(Header(MOVE_SEQUENCE), new Vector3(MOVE_DISTANCE, 0f, 0f)));

            Assert.IsFalse((bool)GetField("_hasPendingAttack"));
            Assert.IsFalse((bool)GetField("_isWindingUp"));
            Assert.AreEqual(0.0, (double)GetField("_comboImmuneUntil"));
            Assert.AreEqual(COMBO_STEP, (int)GetField("_comboStep"));
            Assert.IsTrue((bool)GetField("_hasPendingMove"), "취소 뒤에도 이번 이동 의도는 보관한다");
            Assert.AreEqual(MOVE_SEQUENCE, (uint)GetField("_pendingMoveSequence"));
        }

        [Test]
        public void HandleMove_ClassWhoseWindupSurvivesMove_KeepsWindupAndImmunityButClearsPendingAttack()
        {
            // 검사는 이동으로 준비가 끊기지 않는 공격이라 이미 시작한 준비는 이어 간다
            _player = _rig.Add(PARTICIPANT, Vector3.zero, EPlayerClass.Swordsman);
            SetField("_hasPendingAttack", true);
            SetField("_isWindingUp", true);
            SetField("_comboStep", COMBO_STEP);
            SetField("_comboImmuneUntil", IMMUNE_UNTIL);

            _player.HandleMove(new PlayerMoveInput(Header(MOVE_SEQUENCE), new Vector3(MOVE_DISTANCE, 0f, 0f)));

            Assert.IsFalse((bool)GetField("_hasPendingAttack"));
            Assert.IsTrue((bool)GetField("_isWindingUp"));
            Assert.AreEqual(IMMUNE_UNTIL, (double)GetField("_comboImmuneUntil"), "면역은 보관한 이동이 실제로 시작될 때 거둔다");
            Assert.AreEqual(COMBO_STEP, (int)GetField("_comboStep"));
            Assert.IsTrue((bool)GetField("_hasPendingMove"));
        }

        [Test]
        public void Detach_ArrowInFlightAndPendingInput_KeepsArrowAndClearsPendingInput()
        {
            CreateArcherWithFlyingArrow();
            SetField("_hasPendingAttack", true);
            SetField("_hasPendingMove", true);
            SetField("_hasPendingDodge", true);
            SetField("_hasPendingSkill", true);
            SetField("_isDodging", true);
            int arrows = (int)GetField("_arrowCount");

            _player.Detach();

            Assert.AreEqual(arrows, (int)GetField("_arrowCount"));
            Assert.IsTrue((bool)GetField("_isDodging"));
            Assert.IsFalse((bool)GetField("_hasPendingAttack"));
            Assert.IsFalse((bool)GetField("_hasPendingMove"));
            Assert.IsFalse((bool)GetField("_hasPendingDodge"));
            Assert.IsFalse((bool)GetField("_hasPendingSkill"));
        }

        [Test]
        public void Shutdown_ArrowInFlightAndDodging_ClearsArrowsAndDodge()
        {
            CreateArcherWithFlyingArrow();
            SetField("_isDodging", true);
            SetField("_hasPendingSkill", true);

            _player.Shutdown();

            Assert.AreEqual(0, (int)GetField("_arrowCount"));
            Assert.IsFalse((bool)GetField("_isDodging"));
            Assert.IsFalse((bool)GetField("_hasPendingSkill"));
        }

        [Test]
        public void Reattach_AfterDetach_KeepsHealthAndArrows()
        {
            CreateArcherWithFlyingArrow();
            float health = _player.Health;
            int   arrows = (int)GetField("_arrowCount");

            _player.Detach();
            _player.Reattach();

            Assert.AreEqual(health, _player.Health);
            Assert.AreEqual(arrows, (int)GetField("_arrowCount"));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void CreateArcherWithFlyingArrow()
        {
            FieldInfo specs = typeof(PlayerConfigSO).GetField("_classBasicAttacks", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(specs);
            var array = (PlayerConfigSO.BasicAttackSpec[])specs.GetValue(_rig.Config);
            array[(int)EPlayerClass.Archer - LEGACY_COUNT] = new PlayerConfigSO.BasicAttackSpec(1f, 0.25f, RANGE, 0f, 0f, SLOW_SPEED, 0.3f, false);

            _player = _rig.Add(PARTICIPANT, Vector3.zero, EPlayerClass.Archer);
            var enemy = new FakeEnemy(_rig.Registry.IssueCombatId()) { Position = new Vector3(0f, 0f, ENEMY_Z) };
            Assert.IsTrue(_rig.Registry.Register(enemy));
            _rig.Resolver.BeginTick(_now);

            SetField("_pendingAimYaw", FacingRules.EncodeYaw(Vector3.forward));
            SetField("_hasPendingAttack", true);
            for (int i = 0; (i < MAX_STEPS) && ((int)GetField("_arrowCount") == 0); ++i)
            {
                Step();
            }
            Assert.Greater((int)GetField("_arrowCount"), 0, "화살이 날아간 상태에서 시작해야 한다");
        }

        private static PlayerInputHeader Header(uint sequence)
        {
            return new PlayerInputHeader(Constants.Network.PROTOCOL_VERSION, SESSION_EPOCH, PARTICIPANT, sequence, START_TIME);
        }

        private void Step()
        {
            _now += TICK_STEP;
            _rig.Resolver.BeginTick(_now);
            _budget.BeginTick();
            _player.TickSkills((float)TICK_STEP, _now, _budget);
            _player.TickAttack(_now);
        }

        // 서버 틱 순서대로 이동 뒤 공격을 진행한다
        private void StepWithMovement()
        {
            _now += TICK_STEP;
            _rig.Resolver.BeginTick(_now);
            _budget.BeginTick();
            _player.TickMovement((float)TICK_STEP, _now, _budget);
            _player.TickSkills((float)TICK_STEP, _now, _budget);
            _player.TickAttack(_now);
        }

        private object GetField(string name)
        {
            FieldInfo field = typeof(PlayerServer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            return field.GetValue(_player);
        }

        private void SetField(string name, object value)
        {
            FieldInfo field = typeof(PlayerServer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(_player, value);
        }

        private sealed class FakeEnemy : ISkillCombatant
        {
            public int                   CombatId        { get; }
            public ECombatFaction        Faction         => ECombatFaction.Enemy;
            public EParticipantLifeState LifeState       { get; private set; } = EParticipantLifeState.Alive;
            public bool                  IsSummon        => false;
            public int                   OwnerCombatId   => CombatIds.NONE;
            public Vector3               Position        { get; set; }
            public Vector3               Forward         { get; private set; } = Vector3.back;
            public float                 BodyRadius      => 0.5f;
            public float                 Health          { get; private set; } = HUGE_HEALTH;
            public float                 MaxHealth       => HUGE_HEALTH;
            public float                 Defense         => 0f;
            public bool                  IsInvulnerable  => false;
            public bool                  IsGuardingFront => false;
            public float                 AttackPower     => 0f;
            public SkillServer           Skills          => null;

            public FakeEnemy(int combatId)
            {
                CombatId = combatId;
            }

            public void SetSkillPosition(Vector3 position) { Position = position; }
            public void SetSkillFacing(Vector3 direction) { Forward = direction; }

            public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
            {
                Health -= appliedAmount;
                if (isLethal)
                {
                    LifeState = EParticipantLifeState.Dead;
                }
            }
        }
    }
}
