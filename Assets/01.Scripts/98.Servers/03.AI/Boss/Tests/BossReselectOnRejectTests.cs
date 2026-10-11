// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Tests
{
    // 시작을 거절한 패턴 중 IBossPatternRetryOnReject 표식을 단 패턴(월백 2페이즈 P07)만 같은 선택에서 그 정의를 빼고 한 번 더 고르는 두뇌 동작을 확인한다.
    // 가짜 패턴과 가짜 카탈로그로 두뇌 전체(선택 BT, 공격 FSM)를 돌린다. 표식이 없는 거절 패턴은 기존 동작(기록 없이 재조회 대기)이 그대로여야 한다
    public class BossReselectOnRejectTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  RETRY_SECONDS = 0.25f;
        private const double STEP          = 0.25;
        private const int    FIRST_PLAYER  = 10;
        private const int    SECOND_PLAYER = 11;

        private const string WARP_ID  = "warp";
        private const string ALT_ID   = "alt";
        private const string ALT2_ID  = "alt2";
        private const string PLAIN_ID = "plain";

        /****************************************
        *               Fakes
        ****************************************/

        private abstract class TestPattern : BossPatternBase
        {
            public int    BeginCount;
            public int    CancelCount;
            public int    CastId;
            public int    TargetId;
            public Action Hook;

            protected override void OnBegin(double serverTime)
            {
                ++BeginCount;
                CastId   = Context.CastInstanceId;
                TargetId = Context.TargetCombatId;
                Hook?.Invoke();
            }

            protected override void OnCancelled(EBossInterruptReason reason)
            {
                ++CancelCount;
            }
        }

        private sealed class NormalPattern : TestPattern
        {
        }

        // 표식을 단 거절 패턴, 월백 P07처럼 착지 후보가 없을 때를 흉내 낸다
        private sealed class OptInRejecting : TestPattern, IBossPatternStartResult, IBossPatternRetryOnReject
        {
            public bool IsStartRejected => true;
        }

        // 표식이 없는 거절 패턴, 별형, 빔, 장판 같은 기존 거절 패턴을 흉내 낸다
        private sealed class PlainRejecting : TestPattern, IBossPatternStartResult
        {
            public bool IsStartRejected => true;
        }

        private sealed class MapFactory : IBossPatternFactory
        {
            private readonly Dictionary<string, Func<TestPattern>> _makers = new Dictionary<string, Func<TestPattern>>();

            public Dictionary<string, List<TestPattern>> Created { get; } = new Dictionary<string, List<TestPattern>>();

            public void Map(string id, Func<TestPattern> maker)
            {
                _makers[id] = maker;
                Created[id] = new List<TestPattern>();
            }

            public int Count(string id)
            {
                return Created[id].Count;
            }

            public TestPattern Last(string id)
            {
                return Created[id][Created[id].Count - 1];
            }

            public int Begins(string id)
            {
                int total = 0;
                for (int i = 0; i < Created[id].Count; ++i)
                {
                    total += Created[id][i].BeginCount;
                }

                return total;
            }

            public IBossPattern Create(PatternDefinition definition)
            {
                TestPattern pattern = _makers[definition.DefinitionId]();
                Created[definition.DefinitionId].Add(pattern);
                return pattern;
            }
        }

        private sealed class SequenceRandom : IBossRandom
        {
            public Queue<int> Ints = new Queue<int>();
            public int        IntCalls;
            public float      Roll;

            public float NextFloat01()
            {
                return Roll;
            }

            public int NextInt(int maxExclusive)
            {
                ++IntCalls;
                return (Ints.Count > 0) ? Ints.Dequeue() : 0;
            }
        }

        private sealed class RecordingCasts : IBossCastListener
        {
            public List<int> Executed = new List<int>();
            public List<int> Finished = new List<int>();

            public void OnCastExecuted(int castInstanceId, double serverTime)
            {
                Executed.Add(castInstanceId);
            }

            public void OnCastFinished(int castInstanceId, double serverTime)
            {
                Finished.Add(castInstanceId);
            }
        }

        // 1페이즈 가짜 카탈로그 위에서 두뇌를 돌리는 구성. 후보는 만든 순서대로 가중 선택에 들어가고 Roll 0이면 첫 양수 가중치가 뽑힌다
        private sealed class Rig
        {
            public FakeCombatTarget    Self      { get; } = new FakeCombatTarget { CombatId = 1, Faction = ECombatFaction.Enemy };
            public FakeCombatTarget    First     { get; } = new FakeCombatTarget { CombatId = FIRST_PLAYER, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 3f) };
            public FakeCombatTarget    Second    { get; } = new FakeCombatTarget { CombatId = SECOND_PLAYER, Faction = ECombatFaction.Player, Position = new Vector3(0f, 0f, 4f) };
            public FakeTargetQuery     Query     { get; } = new FakeTargetQuery();
            public FakeDamageResolver  Damage    { get; } = new FakeDamageResolver();
            public FakeBossCatalog     Catalog   { get; } = new FakeBossCatalog();
            public MapFactory          Factory   { get; } = new MapFactory();
            public SequenceRandom      Random    { get; } = new SequenceRandom();
            public RecordingCasts      Casts     { get; } = new RecordingCasts();
            public BossBrain           Brain     { get; }

            public Rig(IBossSelectionRule rule = null)
            {
                Query.Players.Add(First);
                Query.Players.Add(Second);
                Catalog.Boss = BossTestFixtures.MakeBoss(emptyRetry: RETRY_SECONDS);

                Brain = new BossBrain(BossTestFixtures.BOSS_ID, new BossBrainDependencies
                {
                    Catalog  = Catalog,
                    Self     = Self,
                    Vitals   = new FakeBossVitals(),
                    Targets  = Query,
                    Damage   = Damage,
                    Patterns = Factory,
                    Effects  = new FakeEffectLifetime(),
                    Control  = new FakeControlSource(),
                    Random   = Random,
                    Casts    = Casts
                });

                if (rule != null)
                {
                    Assert.IsTrue(Brain.AddSelectionRule(rule));
                }
            }

            // 정의를 카탈로그에 더하고 팩토리에 만드는 방법을 등록한다. 공격은 예고 1초, 실행 0.5초, 회복 0.5초라 총 2초 걸린다
            public PatternDefinition Add(string id, Func<TestPattern> maker, float[] recent = null)
            {
                PatternDefinition definition = BossTestFixtures.MakePattern(id, 10f, 1f, 0.5f, 0.5f, recentMultipliers: recent);
                Catalog.Patterns.Add(definition);
                Factory.Map(id, maker);
                return definition;
            }

            public void Begin()
            {
                Assert.IsTrue(Brain.Begin(0.0));
            }

            public void TickUntil(double to, double from = 0.0)
            {
                for (double t = from; t <= to; t += STEP)
                {
                    Brain.Tick(t);
                }
            }
        }

        /****************************************
        *       Alternative Is Chosen
        ****************************************/

        // 거절된 옵트인 패턴 대신 같은 틱에 다른 패턴이 시작하고, 처음 대상을 그대로 쓰며 새 대상을 뽑지 않는다.
        // 거절된 패턴은 기록, 피해, 시전 알림을 남기지 않고 실제로 시작한 대안만 시전 알림을 받는다
        [Test]
        public void Reject_OptIn_StartsAnAlternativeInTheSameTickWithTheOriginalTarget()
        {
            Rig r = new Rig();
            r.Add(WARP_ID, () => new OptInRejecting());
            r.Add(ALT_ID, () => new NormalPattern());
            r.Random.Ints.Enqueue(1);
            r.Begin();

            r.Brain.Tick(0.0);

            Assert.AreEqual(EBossAttackState.Telegraph, r.Brain.AttackState);
            Assert.AreEqual(ALT_ID, r.Brain.ActivePatternId);
            Assert.AreEqual(1, r.Factory.Begins(WARP_ID), "거절된 패턴은 한 번 시작을 시도했다");
            Assert.AreEqual(1, r.Factory.Begins(ALT_ID));
            Assert.AreEqual(SECOND_PLAYER, r.Factory.Last(ALT_ID).TargetId, "처음 뽑은 대상을 그대로 쓴다");
            Assert.AreEqual(1, r.Random.IntCalls, "다시 고를 때 새 대상을 뽑지 않는다");
            Assert.AreEqual(0, r.Damage.Requests.Count);

            r.TickUntil(2.5, 0.25);

            int firstAlternativeCast = r.Factory.Created[ALT_ID][0].CastId;
            CollectionAssert.AreEqual(new[] { firstAlternativeCast }, r.Casts.Executed, "거절된 시전은 발동 알림을 받지 않는다");
            CollectionAssert.AreEqual(new[] { firstAlternativeCast }, r.Casts.Finished);
            Assert.AreEqual(1, r.Factory.Created[WARP_ID][0].CancelCount, "거절된 시전은 한 번 취소된다");
        }

        // 거절된 패턴은 사용 기록에 남지 않고 대안만 남는다. 기록이 남았다면 직전/2회 전 배율 때문에 다시 뽑히지 못한다
        [Test]
        public void Reject_OptIn_RecordsOnlyTheStartedAlternative()
        {
            Rig r = new Rig();
            // 2회 전 배율이 0이라 거절된 패턴이 기록에 남았다면 대안 다음 선택에서 뽑히지 않는다. 기록이 없으면 최근 3회 밖이라 1이다
            r.Add(WARP_ID, () => new OptInRejecting(), new[] { 1f, 0f, 1f, 1f });
            r.Add(ALT_ID, () => new NormalPattern(), new[] { 0f, 1f, 1f, 1f });
            r.Begin();

            r.Brain.Tick(0.0);
            Assert.AreEqual(ALT_ID, r.Brain.ActivePatternId);
            r.TickUntil(2.25, 0.25); // 2.0초에 대안이 끝나고 2.25초 틱이 다음 선택이다

            Assert.AreEqual(2, r.Factory.Begins(WARP_ID), "대안이 끝난 다음 선택에서 거절됐던 패턴이 다시 후보가 된다(이번 선택에서만 뺐다)");
            Assert.AreEqual(1, r.Factory.Begins(ALT_ID), "직전 사용 배율 0이라 대안은 다시 뽑히지 않았다");
        }

        // 가중 선택뿐 아니라 규칙 선택이 고른 패턴도 이번 선택에서는 제외된다
        [Test]
        public void Reject_OptIn_ForcedByRule_IsAlsoExcludedFromTheReselection()
        {
            FakeSelectionRule rule = new FakeSelectionRule();
            Rig r = new Rig(rule);
            rule.Forced = r.Add(WARP_ID, () => new OptInRejecting());
            r.Add(ALT_ID, () => new NormalPattern());
            r.Begin();

            r.Brain.Tick(0.0);

            Assert.AreEqual(ALT_ID, r.Brain.ActivePatternId, "규칙이 계속 거절된 패턴을 고집해도 대안이 선택된다");
            Assert.AreEqual(1, r.Factory.Begins(WARP_ID));
        }

        // 규칙이 제외된 패턴만 고르고 대안 후보가 없으면 가중 선택까지 가서 실패하고 기존 재조회 대기로 돌아간다
        [Test]
        public void Reject_OptIn_RuleOnlyWantsTheRejected_AndNoAlternative_WaitsTheRetryInterval()
        {
            FakeSelectionRule rule = new FakeSelectionRule();
            Rig r = new Rig(rule);
            rule.Forced = r.Add(WARP_ID, () => new OptInRejecting());
            r.Begin();

            r.Brain.Tick(0.0);
            Assert.AreEqual(EBossAttackState.Ready, r.Brain.AttackState);
            Assert.AreEqual(1, r.Factory.Begins(WARP_ID));

            r.Brain.Tick(0.2);
            Assert.AreEqual(1, r.Factory.Begins(WARP_ID), "재조회 시간 전에는 다시 평가하지 않는다");

            r.Brain.Tick(0.25);
            Assert.AreEqual(2, r.Factory.Begins(WARP_ID));
        }

        /****************************************
        *   Single Reselect, No Alternative
        ****************************************/

        // 대안이 없으면 기존 0.25초 재조회를 쓰고, 재조회 사이에는 같은 패턴을 다시 시도하지 않는다
        [Test]
        public void Reject_OptIn_NoAlternative_FallsBackToTheExistingRetryWait()
        {
            Rig r = new Rig();
            r.Add(WARP_ID, () => new OptInRejecting());
            r.Begin();

            r.Brain.Tick(0.0);
            Assert.AreEqual(EBossAttackState.Ready, r.Brain.AttackState);
            Assert.AreEqual(1, r.Factory.Begins(WARP_ID), "제외했으므로 같은 틱에 다시 뽑지 않는다");

            r.Brain.Tick(0.125);
            Assert.AreEqual(1, r.Factory.Begins(WARP_ID));

            r.Brain.Tick(0.25);
            Assert.AreEqual(2, r.Factory.Begins(WARP_ID), "재조회 시간이 지나면 다시 평가한다");
            Assert.AreEqual(0, r.Damage.Requests.Count);
            Assert.AreEqual(0, r.Casts.Executed.Count);
        }

        // 다시 고르는 것은 한 번뿐이다: 대안도 거절되면 세 번째 패턴으로 가지 않고 재조회 대기로 돌아간다
        [Test]
        public void Reject_AlternativeAlsoRejected_DoesNotReselectASecondTime()
        {
            Rig r = new Rig();
            r.Add(WARP_ID, () => new OptInRejecting());
            r.Add(ALT2_ID, () => new OptInRejecting());
            r.Add(ALT_ID, () => new NormalPattern());
            r.Begin();

            r.Brain.Tick(0.0);

            Assert.AreEqual(EBossAttackState.Ready, r.Brain.AttackState);
            Assert.AreEqual(1, r.Factory.Begins(WARP_ID));
            Assert.AreEqual(1, r.Factory.Begins(ALT2_ID), "다시 고른 패턴이 거절됐다");
            Assert.AreEqual(0, r.Factory.Begins(ALT_ID), "두 번째 재선택은 하지 않는다");

            r.Brain.Tick(0.125);
            Assert.AreEqual(1, r.Factory.Begins(WARP_ID), "대기 중에는 평가하지 않는다");

            r.Brain.Tick(0.25);
            Assert.AreEqual(2, r.Factory.Begins(WARP_ID), "제외는 지난 선택에서만 적용되었다");
        }

        /****************************************
        *            Target Lost
        ****************************************/

        // 거절 사이에 처음 대상이 사라지면 새 대상을 뽑지 않고 기존 재조회 대기로 돌아가며, 대기 뒤에는 새 대상으로 정상 선택한다
        [Test]
        public void Reject_OptIn_OriginalTargetGone_WaitsInsteadOfDrawingANewTarget()
        {
            Rig r = new Rig();
            bool removed = false;
            r.Add(WARP_ID, () => new OptInRejecting
            {
                Hook = () =>
                {
                    if (!removed)
                    {
                        removed = true;
                        r.Query.Players.Remove(r.First);
                    }
                }
            });
            r.Add(ALT_ID, () => new NormalPattern());
            r.Begin();

            r.Brain.Tick(0.0);

            Assert.IsTrue(removed);
            Assert.AreEqual(EBossAttackState.Ready, r.Brain.AttackState, "대안을 시작하지 않는다");
            Assert.AreEqual(0, r.Factory.Begins(ALT_ID));
            Assert.AreEqual(1, r.Random.IntCalls, "새 대상을 뽑지 않았다");

            r.Brain.Tick(0.125);
            Assert.AreEqual(0, r.Factory.Begins(ALT_ID), "재조회 시간 전이다");

            r.Brain.Tick(0.25);
            Assert.AreEqual(2, r.Random.IntCalls, "재조회에서 남은 대상을 새로 뽑는다");
            Assert.AreEqual(SECOND_PLAYER, r.Factory.Last(WARP_ID).TargetId);
            Assert.AreEqual(ALT_ID, r.Brain.ActivePatternId, "새 대상으로 다시 거절되면 그 대상으로 대안을 한 번 고른다");
            Assert.AreEqual(SECOND_PLAYER, r.Factory.Last(ALT_ID).TargetId);
        }

        /****************************************
        *      Other Rejecting Patterns Unchanged
        ****************************************/

        // 표식이 없는 거절 패턴은 기존대로 기록 없이 재조회 시간 뒤에 다시 고른다. 같은 틱에 대안을 시작하지 않는다
        [Test]
        public void Reject_WithoutOptIn_KeepsTheExistingWaitBehavior()
        {
            Rig r = new Rig();
            r.Add(PLAIN_ID, () => new PlainRejecting());
            r.Add(ALT_ID, () => new NormalPattern());
            r.Begin();

            r.Brain.Tick(0.0);

            Assert.AreEqual(EBossAttackState.Ready, r.Brain.AttackState);
            Assert.AreEqual(1, r.Factory.Begins(PLAIN_ID));
            Assert.AreEqual(0, r.Factory.Begins(ALT_ID), "표식이 없으면 같은 틱에 대안을 고르지 않는다");
            Assert.AreEqual(1, r.Random.IntCalls);

            r.Brain.Tick(0.125);
            Assert.AreEqual(1, r.Factory.Begins(PLAIN_ID));

            r.Brain.Tick(0.25);
            Assert.AreEqual(2, r.Factory.Begins(PLAIN_ID), "재조회 시간 뒤에는 같은 패턴이 다시 뽑힐 수 있다");
            Assert.AreEqual(0, r.Casts.Executed.Count);
        }
    }
}
