// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Tests
{
    /// <summary> 보스 두뇌 테스트가 쓰는 가짜 의존성과 정의 생성 도우미 </summary>
    internal static class BossTestFixtures
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string BOSS_ID = "boss_test";

        /****************************************
        *                Methods
        ****************************************/

        // 거리 5m와 12m, 전방 60도와 측면 120도, 주변 반경 5m는 기획 표의 값이다
        public static BossPatternCatalogSO.BossDefinition MakeBoss(float phaseRate = 0.5f, float openingDelay = 0f,
            float emptyRetry = 0.25f, float controlImmune = 2f)
        {
            return new BossPatternCatalogSO.BossDefinition(BOSS_ID, 36f, 700f, 200f, phaseRate, openingDelay,
                5f, 12f, 60f, 120f, 5f, emptyRetry, 1f, controlImmune);
        }

        public static BossPatternCatalogSO.PatternDefinition MakePattern(string id, float weight = 10f,
            float telegraph = 1f, float active = 0.5f, float recovery = 0.5f,
            EBossActionState controlPhase = EBossActionState.SelectTarget, float controlWindow = 0f,
            bool controlCancels = false, int maxAreas = 0, float[] recentMultipliers = null,
            float[] distanceMultipliers = null)
        {
            return new BossPatternCatalogSO.PatternDefinition(id, BOSS_ID, 1, weight,
                distanceMultipliers ?? new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f }, new[] { 1f, 1f, 1f },
                recentMultipliers ?? new[] { 1f, 1f, 1f, 1f },
                telegraph, 0f, 0f, active, recovery, EnemyCatalogSO.AttackShapeSpec.Circle(5f), 1f, true,
                controlPhase, controlWindow, controlCancels, 0f, 0f, maxAreas);
        }
    }

    internal sealed class FakeCombatTarget : ICombatTarget
    {
        public int                   CombatId      { get; set; }
        public ECombatFaction        Faction       { get; set; }
        public EParticipantLifeState LifeState     { get; set; } = EParticipantLifeState.Alive;
        public bool                  IsSummon      { get; set; }
        public int                   OwnerCombatId { get; set; }
        public Vector3               Position      { get; set; }
        public Vector3               Forward       { get; set; } = Vector3.forward;
        public float                 BodyRadius    { get; set; }
    }

    internal sealed class FakeTargetQuery : ICombatTargetQuery
    {
        public List<ICombatTarget> Players { get; } = new List<ICombatTarget>();

        public int NearbyCount { get; set; } = 1;

        public int GetLivingPlayers(List<ICombatTarget> results)
        {
            results.Clear();
            results.AddRange(Players);
            return results.Count;
        }

        public int GetLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons,
            List<ICombatTarget> results)
        {
            results.Clear();
            return 0;
        }

        public int CountLivingInRadius(Vector3 center, float radius, ECombatFaction faction, bool includeSummons)
        {
            return NearbyCount;
        }

        public bool TryGet(int combatId, out ICombatTarget target)
        {
            for (int i = 0; i < Players.Count; ++i)
            {
                if (Players[i].CombatId == combatId)
                {
                    target = Players[i];
                    return true;
                }
            }

            target = null;
            return false;
        }
    }

    internal sealed class FakeDamageResolver : IDamageResolver
    {
        private int _nextId = 1;

        public List<DamageRequest> Requests { get; } = new List<DamageRequest>();

        public DamageResult Apply(in DamageRequest request)
        {
            Requests.Add(request);
            return DamageResult.Applied(1f, false, false);
        }

        public int IssueInstanceId()
        {
            int issued = _nextId;
            ++_nextId;
            return issued;
        }
    }

    internal sealed class FakeBossVitals : IBossVitals
    {
        public float HealthRate   { get; set; } = 1f;
        public bool  IsHealthZero { get; set; }
    }

    internal sealed class FakeControlSource : IBossControlSource
    {
        public bool Pending { get; set; }

        /// <summary> Pending일 때 넘겨 줄 제어 값 </summary>
        public BossDirectControl Next { get; set; } = new BossDirectControl("test_bind", 0.5f);

        public bool TryConsumeDirectControl(out BossDirectControl control)
        {
            control = Next;

            if (!Pending)
            {
                return false;
            }

            Pending = false;
            return true;
        }
    }

    internal sealed class FakeEffectLifetime : IBossEffectLifetime
    {
        public List<KeyValuePair<EBossInterruptReason, int>> Cancels { get; } = new List<KeyValuePair<EBossInterruptReason, int>>();
        public Dictionary<string, int> Areas { get; } = new Dictionary<string, int>();

        public void Cancel(EBossInterruptReason reason, int castInstanceId)
        {
            Cancels.Add(new KeyValuePair<EBossInterruptReason, int>(reason, castInstanceId));
        }

        public int ActiveAreaCount(string patternId)
        {
            return Areas.TryGetValue(patternId, out int count) ? count : 0;
        }
    }

    internal sealed class FakeBossRandom : IBossRandom
    {
        public int   IntCalls   { get; private set; }
        public int   FloatCalls { get; private set; }
        public float Roll       { get; set; }

        public float NextFloat01()
        {
            ++FloatCalls;
            return Roll;
        }

        public int NextInt(int maxExclusive)
        {
            ++IntCalls;
            return 0;
        }
    }

    internal sealed class FakeBossCatalog : IBossPatternCatalog
    {
        public List<BossPatternCatalogSO.PatternDefinition> Patterns { get; } = new List<BossPatternCatalogSO.PatternDefinition>();

        public BossPatternCatalogSO.BossDefinition Boss   { get; set; }
        public bool                                Tuned  { get; set; } = true;

        public bool TryGetBoss(string bossId, out BossPatternCatalogSO.BossDefinition boss)
        {
            boss = (bossId == BossTestFixtures.BOSS_ID) ? Boss : null;
            return boss != null;
        }

        public void CollectPatterns(string bossId, int phase, List<BossPatternCatalogSO.PatternDefinition> results)
        {
            results.Clear();
            for (int i = 0; i < Patterns.Count; ++i)
            {
                if ((Patterns[i].BossId == bossId) && (Patterns[i].Phase == phase))
                {
                    results.Add(Patterns[i]);
                }
            }
        }

        public bool IsTuned(string bossId)
        {
            return Tuned;
        }
    }

    internal sealed class FakeDeathPolicy : IBossDeathPolicy
    {
        public bool Confirm { get; set; }

        public bool IsDeathConfirmed(IBossVitals vitals, double serverTime)
        {
            return vitals.IsHealthZero && Confirm;
        }
    }

    internal sealed class FakeSelectionRule : IBossSelectionRule
    {
        public BossPatternCatalogSO.PatternDefinition Forced { get; set; }

        public bool TrySelect(BossBlackboard blackboard, out BossPatternCatalogSO.PatternDefinition pattern)
        {
            pattern = Forced;
            return Forced != null;
        }
    }

    /// <summary> 실행 단계에서 정해진 시각에 타격을 예약해 두는 테스트용 패턴, 계약의 경계를 확인하는 용도다 </summary>
    internal sealed class ScheduledHitPattern : BossPatternBase
    {
        private readonly double[] _hitOffsets;

        private double _executeStart;
        private int    _nextHit;

        public int CastInstanceId => Context.CastInstanceId;

        /// <summary> 지속물이 들고 있을 피해 게이트와 같은 것, 닫힌 뒤에도 잡고 있는 쪽이 피해를 못 내는지 본다 </summary>
        public BossDamageGate CapturedGate => Context.Damage;

        /// <summary> 단계가 이어지는 동안 Tick이 불린 횟수 </summary>
        public int TickCount { get; private set; }

        public ScheduledHitPattern(params double[] hitOffsets)
        {
            _hitOffsets = hitOffsets;
        }

        // Tick을 거치지 않고 예약 콜백처럼 바로 타격을 시도한다
        public bool TryHitNow()
        {
            DamageRequest request = new DamageRequest(Context.Self.CombatId, Context.TargetCombatId,
                Context.CastInstanceId, Context.Damage.IssueInstanceId(), 1f, EDamageFlags.None,
                Context.Self.Position, Context.Definition.DefinitionId);
            return TryApplyHit(in request, out DamageResult _);
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Execute)
            {
                _executeStart = serverTime;
            }
        }

        // 실행 시작 기준 오프셋마다 한 번씩 타격한다
        protected override void OnTick(EBossAttackState state, double serverTime)
        {
            ++TickCount;

            if (state == EBossAttackState.Telegraph)
            {
                return;
            }

            while ((_nextHit < _hitOffsets.Length) && (serverTime >= (_executeStart + _hitOffsets[_nextHit])))
            {
                ++_nextHit;
                DamageRequest request = new DamageRequest(Context.Self.CombatId, Context.TargetCombatId,
                    Context.CastInstanceId, Context.Damage.IssueInstanceId(), 1f, EDamageFlags.None,
                    Context.Self.Position, Context.Definition.DefinitionId);
                TryApplyHit(in request, out DamageResult _);
            }
        }
    }

    internal sealed class FakePatternFactory : IBossPatternFactory
    {
        public List<ScheduledHitPattern> Created { get; } = new List<ScheduledHitPattern>();

        public double[] HitOffsets { get; set; } = new double[0];
        public bool     ReturnNull { get; set; }

        public int CreateCount => Created.Count;

        public ScheduledHitPattern Last => Created[Created.Count - 1];

        public IBossPattern Create(BossPatternCatalogSO.PatternDefinition definition)
        {
            if (ReturnNull)
            {
                return null;
            }

            ScheduledHitPattern pattern = new ScheduledHitPattern(HitOffsets);
            Created.Add(pattern);
            return pattern;
        }
    }

    /// <summary> 가짜 의존성을 한 번에 엮어 보스 두뇌를 만든다 </summary>
    internal sealed class BossHarness
    {
        public const int SELF_ID   = 1;
        public const int PLAYER_ID = 10;

        public FakeCombatTarget  Self { get; } = new FakeCombatTarget { CombatId = SELF_ID, Faction = ECombatFaction.Enemy };
        public FakeCombatTarget  Player { get; } = new FakeCombatTarget { CombatId = PLAYER_ID, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f) };
        public FakeTargetQuery   Query { get; } = new FakeTargetQuery();
        public FakeDamageResolver Damage { get; } = new FakeDamageResolver();
        public FakeBossVitals    Vitals { get; } = new FakeBossVitals();
        public FakeControlSource Control { get; } = new FakeControlSource();
        public FakeEffectLifetime Effects { get; } = new FakeEffectLifetime();
        public FakeBossRandom    Random { get; } = new FakeBossRandom();
        public FakeBossCatalog   Catalog { get; } = new FakeBossCatalog();
        public FakePatternFactory Factory { get; } = new FakePatternFactory();

        public BossBrain Brain { get; private set; }

        public BossHarness(BossPatternCatalogSO.BossDefinition boss, IBossDeathPolicy deathPolicy,
            params BossPatternCatalogSO.PatternDefinition[] patterns)
        {
            Query.Players.Add(Player);
            Catalog.Boss = boss;
            Catalog.Patterns.AddRange(patterns);

            Brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
            {
                Catalog     = Catalog,
                Self        = Self,
                Vitals      = Vitals,
                Targets     = Query,
                Damage      = Damage,
                Patterns    = Factory,
                Effects     = Effects,
                Control     = Control,
                Random      = Random,
                DeathPolicy = deathPolicy
            });
        }
    }
}
