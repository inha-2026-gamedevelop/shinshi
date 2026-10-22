// System
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    // 궁수 기본 공격 화살을 실제 PlayerServer 틱 경로로 돌려 본다. 시각은 직접 넣어 NetworkTime에 기대지 않는다
    public sealed class PlayerServerArcherArrowTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const double START_TIME    = 100.0;
        private const double TICK_STEP     = 1.0 / 30.0;
        private const float  HUGE_HEALTH   = 1e9f;
        private const int    PARTICIPANT   = 1;
        private const int    LEDGER_SIZE   = 256;
        private const double LEDGER_AGE    = 30.0;
        private const double ACTION_AGE    = 10.0;
        private const int    PATH_BUDGET   = 4;
        private const float  ENEMY_Z       = 9f;
        private const float  SLOW_SPEED    = 6f;
        private const float  RANGE         = 10f;
        private const int    SLOT_Q        = Constants.Combat.SKILL_SLOT_Q;
        private const int    SLOT_W        = Constants.Combat.SKILL_SLOT_W;
        private const int    SLOT_E        = Constants.Combat.SKILL_SLOT_E;
        private const int    SLOT_R        = Constants.Combat.SKILL_SLOT_R;
        private const int    LEGACY_COUNT  = 3;

        private PlayerServerTestRig _rig;
        private SkillCatalogSO      _catalog;
        private PlayerServer        _player;
        private PlayerCombatState   _combat;
        private SkillServer         _skills;
        private FakeEnemy           _enemy;
        private PathRequestBudget   _budget;
        private double              _now;

        /****************************************
        *                Lifecycle
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _rig     = new PlayerServerTestRig();
            _catalog = ScriptableObject.CreateInstance<SkillCatalogSO>();
            _budget  = new PathRequestBudget(PATH_BUDGET);
            _now     = START_TIME;
        }

        [TearDown]
        public void TearDown()
        {
            _skills?.Shutdown();
            _rig.Dispose();
            Object.DestroyImmediate(_catalog);
        }

        /****************************************
        *                Tests
        ****************************************/

        // 단일 슬롯이던 때는 첫 화살이 날아가는 중 두 번째가 나가면 첫 화살이 사라졌다. 일부러 느린 화살로 겹침을 만든다
        [Test]
        public void BasicAttack_TwoArrowsInFlight_BothHit()
        {
            SetArcherSpec(new PlayerConfigSO.BasicAttackSpec(1f, 0.25f, RANGE, 0f, 0f, SLOW_SPEED, 0.3f, false));
            CreateArcher(attachSkills: false);

            QueueAttack();
            Step();
            double second = _now + 0.25;
            while (_now < second)
            {
                Step();
            }

            QueueAttack();
            RunUntil(_now + 3.0);

            Assert.AreEqual(2, _enemy.DamageCalls);
        }

        // 화살은 쏜 순간의 속성을 들고 간다. 첫 화살(얼음)이 나는 중에 불로 바꿔 쏴도 둘 다 적중해 1 + 2 = 3이 쌓인다
        [Test]
        public void BasicAttack_ElementSwitchedWhileArrowFlying_KeepsPerArrowElementAndTotal()
        {
            SetArcherSpec(new PlayerConfigSO.BasicAttackSpec(1f, 0.3f, RANGE, 0f, 0f, SLOW_SPEED, 0.3f, false));
            CreateArcher(attachSkills: true);

            CastAndSettle(SLOT_Q);
            QueueAttack();
            Step();

            CastAndSettle(SLOT_E);
            Assert.AreEqual(0, _skills.ClassStacks, "첫 화살이 아직 맞지 않았다");
            QueueAttack();
            RunUntil(_now + 3.0);

            // 불 화살이 맞으면 서버가 화상을 걸고 화상 틱도 ApplyDamage를 부른다. 호출 수는 화살 수가 아니므로
            // 화살 두 발의 적중은 속성별 스택 합(얼음 첫 적중 +1, 불 전환 +2)으로 가린다
            Assert.GreaterOrEqual(_enemy.DamageCalls, 2);
            Assert.AreEqual(EArcherElement.Fire, _skills.ArcherElement);
            Assert.AreEqual(3, _skills.ClassStacks);
        }

        // R로 20을 쓰고 나면 속성은 남아 있어 다음 적중부터 같은 속성이면 +1로 다시 쌓인다
        [Test]
        public void BurstShot_AfterSpendingStacks_BasicAttackAccumulatesAgain()
        {
            CreateArcher(attachSkills: true);
            CastAndSettle(SLOT_Q);

            FakeEnemy dummy = AddEnemy(new Vector3(50f, 0f, 50f));
            DamageResult hit = DamageResult.Applied(10f, false, false);
            for (int i = 0; i < _catalog.ClassStackMax; ++i)
            {
                _skills.OnArrowHit(EArcherElement.Ice, dummy, in hit, _now);
            }

            Assert.AreEqual(_catalog.ClassStackMax, _skills.ClassStacks);

            Assert.IsTrue(_skills.TryCast(ESkillRequestKind.Regular, SLOT_R, new Vector3(10f, 0f, 0f), FacingRules.EncodeYaw(Vector3.forward),
                _now, true, out ERejectReason reason), reason.ToString());
            RunUntil(_now + 1.2);
            Assert.AreEqual(0, _skills.ClassStacks, "R이 스택을 모두 썼다");
            Assert.AreEqual(ESkillPhase.Idle, _skills.Phase, "R 동작이 끝나 서버가 평타를 받는다");

            QueueAttack();
            RunUntil(_now + 1.5);

            Assert.AreEqual(1, _skills.ClassStacks);
        }

        // QWE 동작이 끝난 직후 넣은 평타가 버려지지 않고 정상 준비 시간 뒤 화살로 나가 적중한다
        [Test]
        public void BasicAttack_RightAfterElementSkill_FiresAndHits()
        {
            CreateArcher(attachSkills: true);
            Assert.IsTrue(_skills.TryCast(ESkillRequestKind.Regular, SLOT_W, _player.Position, FacingRules.EncodeYaw(Vector3.forward),
                _now, true, out ERejectReason reason), reason.ToString());

            while (_skills.Phase != ESkillPhase.Idle)
            {
                Step();
            }

            double idleAt = _now;
            QueueAttack();
            RunUntil(idleAt + 0.3);
            Assert.AreEqual(0, _skills.ClassStacks, "준비 시간 전에는 맞을 수 없다");

            RunUntil(idleAt + 1.2);
            Assert.AreEqual(EArcherElement.Wind, _skills.ArcherElement);
            Assert.AreEqual(1, _enemy.DamageCalls);
            Assert.AreEqual(1, _skills.ClassStacks);
        }

        // 실제 수치로 화살이 나는 시간과 연속 발사 간격을 비교한다. 지금 데이터에서는 겹치지 않고, 겹침 방어는 배열이 맡는다
        [Test]
        public void RealArcherSpec_BurstCanOverlapFlight_StaysWithinArrowCapacity()
        {
            PlayerConfigSO.BasicAttackSpec spec = _rig.Config.GetBasicAttack(EPlayerClass.Archer);
            float maxFlight     = spec.Range / spec.ProjectileSpeed;
            Assert.That(spec.GetShotCount(1), Is.EqualTo(3));
            Assert.That(spec.GetShotInterval(1), Is.LessThan(maxFlight));
            Assert.That(spec.GetShotCount(1) + 1, Is.LessThanOrEqualTo(8));
        }

        [Test]
        public void BasicAttack_SecondStep_FiresThreeHitsAndPreservesTotalCoefficient()
        {
            CreateArcher(false);
            SetField("_comboStep", 0);
            SetField("_nextAttackTime", _now);
            float applied = 0f;
            _rig.Resolver.DamageApplied += notice => applied += notice.Result.AppliedAmount;
            QueueAttack();
            _player.TickAttack(_now);
            RunUntil(START_TIME + 1.2);
            Assert.That(_enemy.DamageCalls, Is.EqualTo(3));
            float expected = _player.AttackPower * _rig.Config.GetBasicAttack(EPlayerClass.Archer).GetDamageCoefficient(1);
            Assert.That(applied, Is.EqualTo(expected).Within(0.1f));
        }

        [Test]
        public void BasicAttack_CancelAfterFirstBurstShot_StopsOnlyRemainingShots()
        {
            CreateArcher(false);
            SetField("_comboStep", 0);
            SetField("_nextAttackTime", _now);
            QueueAttack();
            _player.TickAttack(_now);
            RunUntil(START_TIME + 0.27);
            Assert.That(_player.ApplyControl(EPlayerControlKind.Stun, 1f, _now), Is.EqualTo(EPlayerControlResult.Applied));
            RunUntil(START_TIME + 1.2);
            Assert.That(_enemy.DamageCalls, Is.EqualTo(1), "이미 발사한 한 발은 남고 나머지 두 발만 취소한다");
        }

        [Test]
        public void DamageApplied_BurnRequest_PreservesDotFlagForFeedback()
        {
            CreateArcher(false);
            bool observedDot = false;
            _rig.Resolver.DamageApplied += notice => observedDot = notice.IsDamageOverTime;
            var request = new DamageRequest(_player.CombatId, _enemy.CombatId, _rig.Resolver.IssueInstanceId(),
                _rig.Resolver.IssueInstanceId(), 1f, EDamageFlags.DamageOverTime, _player.Position, "skill_archer_scatter_shot");
            Assert.That(_rig.Resolver.Apply(in request).CountsAsHit, Is.True);
            Assert.That(observedDot, Is.True);
        }

        [Test]
        public void BasicAttack_HeavyStep_PublishesHeavyHitOnCombatState()
        {
            CreateArcher(false);
            SetField("_comboStep", 1);
            SetField("_nextAttackTime", _now);
            QueueAttack();
            _player.TickAttack(_now);
            RunUntil(START_TIME + 1.2);
            Assert.That(_enemy.DamageCalls, Is.EqualTo(1));
            Assert.That(_combat.ArrowHitIsHeavy, Is.True);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void SetArcherSpec(PlayerConfigSO.BasicAttackSpec spec)
        {
            FieldInfo field = typeof(PlayerConfigSO).GetField("_classBasicAttacks", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            var specs = (PlayerConfigSO.BasicAttackSpec[])field.GetValue(_rig.Config);
            specs[(int)EPlayerClass.Archer - LEGACY_COUNT] = spec;
        }

        private void CreateArcher(bool attachSkills)
        {
            _player = _rig.Add(PARTICIPANT, Vector3.zero, EPlayerClass.Archer);
            _combat = _rig.Combats[_rig.Combats.Count - 1];
            _enemy  = AddEnemy(new Vector3(0f, 0f, ENEMY_Z));
            _rig.Resolver.BeginTick(_now);

            if (!attachSkills)
            {
                return;
            }

            var ledger   = new HitLedger(LEDGER_SIZE, LEDGER_AGE, ACTION_AGE);
            var resolver = new SkillDamageResolver(_rig.Resolver, _rig.Registry, ledger);
            _skills = new SkillServer(_player, EPlayerClass.Archer, PARTICIPANT, _catalog, _rig.Config, _rig.Registry, resolver,
                new CombatLineOfSight(0, 0f), _now, null, new FakeNavigation());
            _player.ConfigureSkills(_catalog, _rig.Config, _rig.Registry, new FakePhase(), _now, _skills);
        }

        private FakeEnemy AddEnemy(Vector3 position)
        {
            var enemy = new FakeEnemy(_rig.Registry.IssueCombatId()) { Position = position };
            Assert.IsTrue(_rig.Registry.Register(enemy));
            return enemy;
        }

        private void QueueAttack()
        {
            SetField("_pendingAimYaw", FacingRules.EncodeYaw(Vector3.forward));
            SetField("_hasPendingAttack", true);
        }

        private void SetField(string name, object value)
        {
            FieldInfo field = typeof(PlayerServer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(_player, value);
        }

        private void CastAndSettle(int slot)
        {
            Assert.IsTrue(_skills.TryCast(ESkillRequestKind.Regular, slot, _player.Position, FacingRules.EncodeYaw(Vector3.forward),
                _now, true, out ERejectReason reason), reason.ToString());
            while (_skills.Phase != ESkillPhase.Idle)
            {
                Step();
            }
        }

        private void RunUntil(double endTime)
        {
            while (_now < endTime)
            {
                Step();
            }
        }

        // 서버 틱 순서와 같다. 기술 틱 다음에 공격 틱
        private void Step()
        {
            _now += TICK_STEP;
            _rig.Resolver.BeginTick(_now);
            _budget.BeginTick();
            _player.TickSkills((float)TICK_STEP, _now, _budget);
            _player.TickAttack(_now);
            _player.ReplicateSkills();
        }

        private sealed class FakePhase : ICombatSkillPhase
        {
            public bool AllowsCombat => true;
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
            public int                   DamageCalls     { get; private set; }

            public FakeEnemy(int combatId)
            {
                CombatId = combatId;
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
