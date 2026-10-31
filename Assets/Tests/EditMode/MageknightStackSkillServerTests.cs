// System
using System.Collections.Generic;

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
    public class MageknightStackSkillServerTests
    {
        // 마검사 스택형 기술의 서버 판정. 기본 공격 휘두르기당 스택, Q 면역, W 스택 소모 피해, E 장판 스택, R 순간 이동을 시험한다

        /****************************************
        *                Fields
        ****************************************/

        private const double START_TIME     = 10.0;
        private const float  TOLERANCE      = 0.01f;
        private const float  ATTACK_POWER   = 100f;
        private const float  TARGET_HEALTH  = 100000f;
        private const int    PARTICIPANT_ID = 1;
        private const int    LEDGER_SIZE    = 256;
        private const double LEDGER_AGE     = 30.0;
        private const double ACTION_AGE     = 10.0;
        private const int    PATH_BUDGET    = 4;
        private const int    LAYER_MASK     = 1;
        private const float  LOS_HEIGHT     = 0.5f;

        private const int SLOT_Q = Constants.Combat.SKILL_SLOT_Q;
        private const int SLOT_W = Constants.Combat.SKILL_SLOT_W;
        private const int SLOT_E = Constants.Combat.SKILL_SLOT_E;
        private const int SLOT_R = Constants.Combat.SKILL_SLOT_R;

        // 물리 시험은 다른 개체와 겹치지 않게 멀리 떨어뜨린 자리에서 한다
        private static readonly Vector3 FAR_BASE = new Vector3(2000f, 0f, 2000f);

        private readonly List<GameObject> _objects = new List<GameObject>();

        private SkillCatalogSO       _catalog;
        private PlayerConfigSO       _player;
        private CombatTargetRegistry _registry;
        private DamageResolver       _core;
        private SkillDamageResolver  _resolver;
        private FakeCaster           _owner;
        private PathRequestBudget    _budget;
        private SkillServer          _server;
        private CombatLineOfSight    _lineOfSight;
        private double               _lastTick;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _catalog     = ScriptableObject.CreateInstance<SkillCatalogSO>();
            _player      = ScriptableObject.CreateInstance<PlayerConfigSO>();
            _registry    = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            var ledger   = new HitLedger(LEDGER_SIZE, LEDGER_AGE, ACTION_AGE);
            _core        = new DamageResolver(_registry, ledger, _player.DefenseReductionConstant, _player.DamageReductionCap);
            _resolver    = new SkillDamageResolver(_core, _registry, ledger);
            _owner       = new FakeCaster(_registry.IssueCombatId(), ECombatFaction.Player);
            Assert.IsTrue(_registry.Register(_owner));
            _budget      = new PathRequestBudget(PATH_BUDGET);
            _lineOfSight = new CombatLineOfSight(0, 0f);
            _lastTick    = START_TIME;
            _core.BeginTick(START_TIME);
        }

        [TearDown]
        public void TearDown()
        {
            _server?.Shutdown();
            for (int i = 0; i < _objects.Count; ++i)
            {
                Object.DestroyImmediate(_objects[i]);
            }

            _objects.Clear();
            Object.DestroyImmediate(_catalog);
            Object.DestroyImmediate(_player);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Catalog_Mageknight_UsesStackSkillsAndPassesValidation()
        {
            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Mageknight, SLOT_Q, out SkillCatalogSO.SkillDefinition q));
            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Mageknight, SLOT_W, out SkillCatalogSO.SkillDefinition w));
            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Mageknight, SLOT_E, out SkillCatalogSO.SkillDefinition e));
            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Mageknight, SLOT_R, out SkillCatalogSO.SkillDefinition r));

            Assert.AreEqual(ESkillShape.Buff, q.Area.Shape);
            Assert.Greater(q.Effect.HitImmunityDuration, 0f);
            Assert.Greater(w.Effect.StackCoefficient, 0f);
            Assert.AreEqual(5, e.Effect.StackGain);
            Assert.AreEqual(10, r.Effect.StackGain);
            Assert.IsTrue(r.Effect.TeleportToEnd);
            Assert.AreEqual(15, _catalog.MageknightStackMax);

            var errors = new List<string>();
            _catalog.Validate(errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void BasicHit_OneSwingHittingTwoEnemies_AddsOneStack()
        {
            Create(EPlayerClass.Mageknight);
            FakeCaster a = Enemy(Vector3.forward);
            FakeCaster b = Enemy(Vector3.forward * 1.5f);

            Swing(a, b);

            Assert.AreEqual(1, _server.ClassStacks);
        }

        [Test]
        public void BasicHit_SwingsBeyondMax_ClampsToMax()
        {
            Create(EPlayerClass.Mageknight);
            FakeCaster enemy = Enemy(Vector3.forward);

            for (int i = 0; i < _catalog.MageknightStackMax + 5; ++i)
            {
                Swing(enemy);
            }

            Assert.AreEqual(_catalog.MageknightStackMax, _server.ClassStacks);
        }

        [Test]
        public void BasicHit_OtherClass_DoesNotAddStack()
        {
            Create(EPlayerClass.Swordsman);
            FakeCaster enemy = Enemy(Vector3.forward);

            Swing(enemy);

            Assert.AreEqual(0, _server.ClassStacks);
        }

        [Test]
        public void WaterShield_Resolve_GrantsHitImmunityForDuration()
        {
            Create(EPlayerClass.Mageknight);
            Assert.IsTrue(_catalog.TryGetSkillBySlot(EPlayerClass.Mageknight, SLOT_Q, out SkillCatalogSO.SkillDefinition q));

            Cast(SLOT_Q, Vector3.zero);
            Assert.AreEqual(0.0, _owner.ImmuneUntil);
            Tick(START_TIME + q.Timeline.Windup);

            Assert.AreEqual(START_TIME + q.Timeline.Windup + q.Effect.HitImmunityDuration, _owner.ImmuneUntil, TOLERANCE);
            Assert.AreEqual(0, _server.ClassStacks);
        }

        [Test]
        public void SlashWave_TenStacks_DealsStackScaledDamageToEveryTargetAndResetsStacks()
        {
            Create(EPlayerClass.Mageknight);
            FakeCaster first  = Enemy(Vector3.forward * 2f);
            FakeCaster second = Enemy(Vector3.forward * 5f);
            for (int i = 0; i < 10; ++i)
            {
                Swing(first);
            }

            float before = first.Health;
            Cast(SLOT_W, Vector3.zero);
            Assert.AreEqual(0, _server.ClassStacks);
            Tick(START_TIME + 0.35);
            Tick(START_TIME + 1.0);

            // 기본 0.85A + 10스택 * 0.17A = 2.55A
            Assert.AreEqual(255f, before - first.Health, TOLERANCE);
            Assert.AreEqual(255f, TARGET_HEALTH - second.Health, TOLERANCE);
        }

        [Test]
        public void SlashWave_NoStacks_DealsBaseDamage()
        {
            Create(EPlayerClass.Mageknight);
            FakeCaster enemy = Enemy(Vector3.forward * 2f);

            Cast(SLOT_W, Vector3.zero);
            Tick(START_TIME + 0.35);
            Tick(START_TIME + 1.0);

            Assert.AreEqual(85f, TARGET_HEALTH - enemy.Health, TOLERANCE);
        }

        [Test]
        public void SpatialSection_Resolve_AddsFiveStacksAndTicksAtPoint()
        {
            Create(EPlayerClass.Mageknight);
            FakeCaster inside  = Enemy(Vector3.forward * 5f);
            FakeCaster outside = Enemy(Vector3.zero + (Vector3.right * 0.5f));

            Cast(SLOT_E, Vector3.forward * 5f);
            Tick(START_TIME + 0.31);
            Assert.AreEqual(5, _server.ClassStacks);
            Tick(START_TIME + 4.5);

            Assert.Greater(inside.DamageCalls, 1);
            Assert.AreEqual(0, outside.DamageCalls);
        }

        [Test]
        public void FlashSlash_ActiveEnd_TeleportsToPathEndHitsPathAndAddsTenStacks()
        {
            Create(EPlayerClass.Mageknight);
            FakeCaster onPath  = Enemy(Vector3.forward * 3f);
            FakeCaster offPath = Enemy(new Vector3(3f, 0f, 3f));

            Cast(SLOT_R, Vector3.zero);
            Tick(START_TIME + 0.15);
            Assert.AreEqual(0f, _owner.Position.z, TOLERANCE);
            Assert.AreEqual(0, onPath.DamageCalls);
            Tick(START_TIME + 0.6);

            Assert.AreEqual(6f, _owner.Position.z, TOLERANCE);
            Assert.AreEqual(1, onPath.DamageCalls);
            Assert.AreEqual(300f, TARGET_HEALTH - onPath.Health, TOLERANCE);
            Assert.AreEqual(0, offPath.DamageCalls);
            Assert.AreEqual(10, _server.ClassStacks);
        }

        [Test]
        public void FlashSlash_CanceledBeforeActiveEnd_StaysAndGainsNothing()
        {
            Create(EPlayerClass.Mageknight);
            FakeCaster onPath = Enemy(Vector3.forward * 3f);

            Cast(SLOT_R, Vector3.zero);
            Tick(START_TIME + 0.3);
            _server.Cancel();
            Tick(START_TIME + 1.0);

            Assert.AreEqual(0f, _owner.Position.z, TOLERANCE);
            Assert.AreEqual(0, onPath.DamageCalls);
            Assert.AreEqual(0, _server.ClassStacks);
        }

        [Test]
        public void FlashSlash_WallAhead_StopsBeforeWall()
        {
            UseWall(FAR_BASE + (Vector3.forward * 3f), new Vector3(10f, 2f, 0.2f));
            Create(EPlayerClass.Mageknight);
            _owner.Position = FAR_BASE;

            Cast(SLOT_R, FAR_BASE);
            Tick(START_TIME + 0.15);
            Tick(START_TIME + 0.6);

            float moved = _owner.Position.z - FAR_BASE.z;
            Assert.Greater(moved, 1.5f);
            Assert.Less(moved, 3f - Constants.Combat.PLAYER_BODY_RADIUS + TOLERANCE);
        }

        [Test]
        public void OwnerDeath_ClearsStacks()
        {
            Create(EPlayerClass.Mageknight);
            FakeCaster enemy = Enemy(Vector3.forward);
            Swing(enemy);
            Assert.AreEqual(1, _server.ClassStacks);

            _server.OnOwnerDeath();

            Assert.AreEqual(0, _server.ClassStacks);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void UseWall(Vector3 center, Vector3 size)
        {
            var wall = new GameObject("TestWall");
            _objects.Add(wall);
            wall.layer = 0;
            wall.transform.position = center + (Vector3.up * LOS_HEIGHT);
            wall.AddComponent<BoxCollider>().size = size;
            Physics.SyncTransforms();
            _lineOfSight = new CombatLineOfSight(LAYER_MASK, LOS_HEIGHT);
        }

        private void Create(EPlayerClass playerClass)
        {
            _server = new SkillServer(_owner, playerClass, PARTICIPANT_ID, _catalog, _player, _registry, _resolver,
                _lineOfSight, START_TIME, null, new FakeNavigation());
            _owner.Skills = _server;
        }

        private FakeCaster Enemy(Vector3 position)
        {
            var target = new FakeCaster(_registry.IssueCombatId(), ECombatFaction.Enemy) { Position = position };
            Assert.IsTrue(_registry.Register(target));
            return target;
        }

        // 기본 공격 한 번 휘두르기, 시전 번호 하나로 여러 대상을 친다
        private void Swing(params FakeCaster[] targets)
        {
            int castId   = _resolver.IssueInstanceId();
            int actionId = _resolver.IssueInstanceId();
            for (int i = 0; i < targets.Length; ++i)
            {
                var request = new DamageRequest(_owner.CombatId, targets[i].CombatId, castId, actionId, ATTACK_POWER,
                    EDamageFlags.None, _owner.Position, Constants.Combat.BASIC_ATTACK_SOURCE_ID);
                _resolver.Apply(in request);
            }
        }

        private void Cast(int slot, Vector3 point)
        {
            Assert.IsTrue(_server.TryCast(ESkillRequestKind.Regular, slot, point, FacingRules.EncodeYaw(Vector3.forward),
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

        private sealed class FakeCaster : ISkillCombatant, IHitImmunityTarget
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
            public double                ImmuneUntil     { get; private set; }

            public FakeCaster(int combatId, ECombatFaction faction)
            {
                CombatId = combatId;
                Faction  = faction;
            }

            public void SetSkillPosition(Vector3 position) { Position = position; }
            public void SetSkillFacing(Vector3 direction) { Forward = direction; }
            public void GrantHitImmunity(double untilServerTime) { ImmuneUntil = untilServerTime; }

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
