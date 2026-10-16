// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class PlayerControlSkillServerTests
    {
        // 기절이면 시전과 진행 중 기술을 끊고, 속박이면 이동 기술만 막고 제자리 사격은 이어 가는지 기술 서버 단위로 시험한다

        /****************************************
        *                Fields
        ****************************************/

        private const double START_TIME     = 10.0;
        private const float  TOLERANCE      = 0.01f;
        private const float  ATTACK_POWER   = 100f;
        private const float  TARGET_HEALTH  = 10000f;
        private const int    PARTICIPANT_ID = 1;
        private const int    LEDGER_SIZE    = 256;
        private const double LEDGER_AGE     = 30.0;
        private const double ACTION_AGE     = 10.0;
        private const int    PATH_BUDGET    = 4;

        private const int SLOT_Q = Constants.Combat.SKILL_SLOT_Q;
        private const int SLOT_W = Constants.Combat.SKILL_SLOT_W;
        private const int SLOT_R = Constants.Combat.SKILL_SLOT_R;

        private SkillCatalogSO       _catalog;
        private PlayerConfigSO       _player;
        private CombatTargetRegistry _registry;
        private DamageResolver       _core;
        private SkillDamageResolver  _resolver;
        private FakeCaster           _owner;
        private PathRequestBudget    _budget;
        private SkillServer          _server;
        private double               _lastTick;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _catalog   = ScriptableObject.CreateInstance<SkillCatalogSO>();
            _player    = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _registry  = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger = new HitLedger(LEDGER_SIZE, LEDGER_AGE, ACTION_AGE);
            _core      = new DamageResolver(_registry, ledger, _player.DefenseReductionConstant, _player.DamageReductionCap);
            _resolver  = new SkillDamageResolver(_core, _registry, ledger);
            _owner     = new FakeCaster(_registry.IssueCombatId(), ECombatFaction.Player);
            Assert.IsTrue(_registry.Register(_owner));
            _budget    = new PathRequestBudget(PATH_BUDGET);
            _lastTick  = START_TIME;
            _core.BeginTick(START_TIME);
        }

        [TearDown]
        public void TearDown()
        {
            _server?.Shutdown();
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_player);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void SetControl_StunWhileIdle_CancelsNothing()
        {
            CreateServer(EPlayerClass.Archer);

            Assert.IsFalse(_server.SetControl(EPlayerControlKind.Stun));
            Assert.AreEqual(ESkillPhase.Idle, _server.Phase);
        }

        [Test]
        public void TryCast_Stunned_RejectsDisabledAndSpendsNothing()
        {
            CreateServer(EPlayerClass.Archer);
            float energyBefore = _server.Energy;
            _server.SetControl(EPlayerControlKind.Stun);

            bool cast = _server.TryCast(ESkillRequestKind.Regular, SLOT_Q, Vector3.zero, FacingRules.EncodeYaw(Vector3.forward),
                START_TIME, true, out ERejectReason reason);

            Assert.IsFalse(cast);
            Assert.AreEqual(ERejectReason.Disabled, reason);
            Assert.AreEqual(energyBefore, _server.Energy, TOLERANCE);
            Assert.AreEqual(0.0, _server.GetReadyAt(SLOT_Q), TOLERANCE);
            Assert.AreEqual(ESkillPhase.Idle, _server.Phase);
        }

        [Test]
        public void TryCast_StunEnded_CastsAgain()
        {
            CreateServer(EPlayerClass.Archer);
            _server.SetControl(EPlayerControlKind.Stun);
            _server.SetControl(EPlayerControlKind.None);

            bool cast = _server.TryCast(ESkillRequestKind.Regular, SLOT_Q, Vector3.zero, FacingRules.EncodeYaw(Vector3.forward),
                START_TIME, true, out ERejectReason reason);

            Assert.IsTrue(cast, reason.ToString());
        }

        [Test]
        public void TryCast_RootedStationarySkill_IsAllowed()
        {
            CreateServer(EPlayerClass.Archer);
            _server.SetControl(EPlayerControlKind.Root);

            bool cast = _server.TryCast(ESkillRequestKind.Regular, SLOT_Q, Vector3.zero, FacingRules.EncodeYaw(Vector3.forward),
                START_TIME, true, out ERejectReason reason);

            Assert.IsTrue(cast, reason.ToString());
        }

        [Test]
        public void TryCast_RootedBlinkSlash_RejectsDisabledAndSpendsNothing()
        {
            CreateServer(EPlayerClass.Mageknight);
            float energyBefore = _server.Energy;
            _server.SetControl(EPlayerControlKind.Root);

            bool cast = _server.TryCast(ESkillRequestKind.Regular, SLOT_R, Vector3.zero, FacingRules.EncodeYaw(Vector3.forward),
                START_TIME, true, out ERejectReason reason);

            Assert.IsFalse(cast);
            Assert.AreEqual(ERejectReason.Disabled, reason);
            Assert.AreEqual(energyBefore, _server.Energy, TOLERANCE);
            Assert.AreEqual(0.0, _server.GetReadyAt(SLOT_R), TOLERANCE);
        }

        [Test]
        public void TryCast_RootedDashSkill_RejectsDisabled()
        {
            CreateServer(EPlayerClass.Warrior);
            _server.SetControl(EPlayerControlKind.Root);

            bool cast = _server.TryCast(ESkillRequestKind.Regular, SLOT_W, Vector3.zero, FacingRules.EncodeYaw(Vector3.forward),
                START_TIME, true, out ERejectReason reason);

            Assert.IsFalse(cast);
            Assert.AreEqual(ERejectReason.Disabled, reason);
        }

        [Test]
        public void ChainSlash_StunBetweenHits_DropsSecondHitWithoutRefund()
        {
            CreateServer(EPlayerClass.Swordsman);
            FakeCaster enemy = Enemy(Vector3.forward * 2f);

            Cast(SLOT_R);
            Tick(START_TIME + 0.3);
            Tick(START_TIME + 0.35);
            Assert.AreEqual(1, enemy.DamageCalls);
            double readyAt = _server.GetReadyAt(SLOT_R);

            Assert.IsTrue(_server.SetControl(EPlayerControlKind.Stun));
            Assert.AreEqual(ESkillPhase.Idle, _server.Phase);
            Tick(START_TIME + 1.5);

            Assert.AreEqual(1, enemy.DamageCalls);
            Assert.AreEqual(readyAt, _server.GetReadyAt(SLOT_R), TOLERANCE);
            Assert.Greater(readyAt, START_TIME);
        }

        [Test]
        public void ChainSlash_RootBetweenHits_KeepsSecondHit()
        {
            CreateServer(EPlayerClass.Swordsman);
            FakeCaster enemy = Enemy(Vector3.forward * 2f);

            Cast(SLOT_R);
            Tick(START_TIME + 0.3);
            Tick(START_TIME + 0.35);
            Assert.AreEqual(1, enemy.DamageCalls);

            Assert.IsFalse(_server.SetControl(EPlayerControlKind.Root));
            Tick(START_TIME + 0.5);
            Tick(START_TIME + 0.8);

            Assert.AreEqual(2, enemy.DamageCalls);
        }

        [Test]
        public void Dash_RootedMidway_StopsMovementButKeepsAction()
        {
            CreateServer(EPlayerClass.Swordsman);

            Cast(SLOT_W);
            Tick(START_TIME + 0.1);
            float before = _owner.Position.z;
            Assert.Greater(before, 0f, "돌진이 시작되어야 속박을 시험할 수 있다");

            Assert.IsFalse(_server.SetControl(EPlayerControlKind.Root));
            Assert.AreNotEqual(ESkillPhase.Idle, _server.Phase);
            Tick(START_TIME + 0.3);

            Assert.AreEqual(before, _owner.Position.z, TOLERANCE);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void CreateServer(EPlayerClass playerClass)
        {
            _server = new SkillServer(_owner, playerClass, PARTICIPANT_ID, _catalog, _player, _registry, _resolver,
                new CombatLineOfSight(0, 0f), START_TIME, null, new FakeNavigation());
            _owner.Skills = _server;
        }

        private FakeCaster Enemy(Vector3 position)
        {
            var target = new FakeCaster(_registry.IssueCombatId(), ECombatFaction.Enemy) { Position = position };
            Assert.IsTrue(_registry.Register(target));
            return target;
        }

        private void Cast(int slot)
        {
            Assert.IsTrue(_server.TryCast(ESkillRequestKind.Regular, slot, Vector3.zero, FacingRules.EncodeYaw(Vector3.forward),
                START_TIME, true, out ERejectReason reason), reason.ToString());
        }

        private void Tick(double now)
        {
            _core.BeginTick(now);
            _budget.BeginTick();
            _server.Tick(now, Mathf.Max(0f, (float)(now - _lastTick)), _budget);
            _lastTick = now;
        }

        private sealed class FakeNavigation : ISkillNavigation
        {
            public bool TrySample(Vector3 point, out Vector3 sampled)
            {
                sampled = point;
                return true;
            }

            public Vector3 Move(Vector3 origin, Vector3 destination)
            {
                return destination;
            }
        }

        private sealed class FakeCaster : ISkillCombatant
        {
            public int                   CombatId        { get; }
            public ECombatFaction        Faction         { get; }
            public EParticipantLifeState LifeState       { get; set; } = EParticipantLifeState.Alive;
            public bool                  IsSummon        => false;
            public int                   OwnerCombatId   => CombatIds.NONE;
            public Vector3               Position        { get; set; }
            public Vector3               Forward         { get; private set; } = Vector3.forward;
            public float                 BodyRadius      => 0f;
            public float                 Health          { get; private set; } = TARGET_HEALTH;
            public float                 MaxHealth       => TARGET_HEALTH;
            public float                 Defense         => 0f;
            public bool                  IsInvulnerable  => false;
            public bool                  IsGuardingFront => false;
            public float                 AttackPower     => ATTACK_POWER;
            public SkillServer           Skills          { get; set; }
            public int                   DamageCalls     { get; private set; }

            public FakeCaster(int combatId, ECombatFaction faction)
            {
                CombatId = combatId;
                Faction  = faction;
            }

            public void SetSkillPosition(Vector3 position) { Position = position; }
            public void SetSkillFacing(Vector3 direction) { Forward = direction; }

            public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
            {
                ++DamageCalls;
                Health -= appliedAmount;
                if (isLethal)
                {
                    LifeState = EParticipantLifeState.Dead;
                }
            }
        }
    }
}
