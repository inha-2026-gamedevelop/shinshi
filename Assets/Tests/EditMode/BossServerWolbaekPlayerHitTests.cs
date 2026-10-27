// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Tests
{
    // 서버 몸체(BossServer)가 패턴, 중계를 거친 기믹의 맞은 대상 알림을 월백 피격 사건으로 바꿔 수신처에 넘기는지 본다.
    // 복제 상태(BossState)의 Rpc 전송과 클라이언트 임팩트 재생은 이 시험이 다루지 않는다
    public class BossServerWolbaekPlayerHitTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH   = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const string WOLBAEK_ID     = Constants.Combat.DEV_BOSS_ID;
        private const int    RANDOM_SEED    = 20261026;
        private const int    MAX_SECONDS    = 120; // 첫 피격을 기다리는 모의 시간 상한
        private const int    CAST_ID        = 41;
        private const int    TARGET_ID      = 77;
        private const float  FLOAT_EPSILON  = 0.001f;
        private const double TIME_EPSILON   = 0.000001;

        private static readonly Vector3 HitPosition  = new Vector3(3f, 0f, 4f);
        private static readonly Vector3 HitDirection = new Vector3(0.6f, 0f, 0.8f);

        /****************************************
        *               Helpers
        ****************************************/

        private static BossMockCombat CreateWolbaek()
        {
            BossPatternCatalogSO asset = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assume.That(asset, Is.Not.Null, "월백 카탈로그 에셋이 없다");

            var combat = new BossMockCombat(asset, WOLBAEK_ID, EBossDifficulty.Easy, RANDOM_SEED, true, false);
            Assume.That(combat.Catalog.IsTuned(WOLBAEK_ID), Is.True, "월백 수치가 지정되지 않았다");
            return combat;
        }

        /****************************************
        *                Tests
        ****************************************/

        // 실제 월백 패턴이 가짜 플레이어를 맞히면 그 틱에 맞은 대상, 위치, 시각이 피격 사건으로 나간다
        [Test]
        public void WolbaekCombat_PlayerHit_SendsHitEventWithTargetPositionAndTickTime()
        {
            BossMockCombat combat = CreateWolbaek();
            Assert.IsTrue(combat.Begin());

            Assert.IsTrue(combat.StepUntil(() => combat.Events.PlayerHits.Count > 0, MAX_SECONDS),
                $"{MAX_SECONDS}초 동안 월백 공격이 플레이어를 한 번도 맞히지 않았다");

            WolbaekPlayerHit hit = combat.Events.PlayerHits[0];
            Assert.AreEqual(combat.Player.CombatId, hit.TargetId);
            Assert.That(Vector3.Distance(hit.Position, combat.Player.Position), Is.LessThan(FLOAT_EPSILON), "맞은 순간의 대상 위치다");
            Assert.That(hit.ServerTime, Is.InRange(combat.Now - BossMockCombat.TICK_SECONDS - TIME_EPSILON, combat.Now + TIME_EPSILON),
                "맞은 틱의 서버 시각이다");
            Assert.AreEqual(0f, hit.Direction.y, FLOAT_EPSILON, "방향은 수평이다");
            float length = hit.Direction.magnitude;
            Assert.That((length < FLOAT_EPSILON) || (Mathf.Abs(length - 1f) < FLOAT_EPSILON), Is.True, "방향은 단위 벡터이거나 없다");
        }

        [Test]
        public void Wolbaek_TargetHit_ForwardsActivePatternAndValues()
        {
            BossMockCombat combat = CreateWolbaek();

            ((IBossTargetHitListener)combat.Boss).OnTargetHit(CAST_ID, TARGET_ID, HitPosition, HitDirection);

            Assert.AreEqual(1, combat.Events.PlayerHits.Count);
            WolbaekPlayerHit hit = combat.Events.PlayerHits[0];
            Assert.AreEqual(TARGET_ID, hit.TargetId);
            Assert.AreEqual(HitPosition, hit.Position);
            Assert.AreEqual(HitDirection, hit.Direction);
            Assert.AreEqual(((IBossEventRelayHost)combat.Boss).ActivePatternId, hit.PatternId);
        }

        // 체력 기믹은 중계가 수신처라 중계가 몸체로 넘겨야 피격 사건이 나간다
        [Test]
        public void Wolbaek_GimmickRelayTargetHit_ReachesHitEvents()
        {
            BossMockCombat combat = CreateWolbaek();

            ((IBossTargetHitListener)combat.Boss.WolbaekGimmickRelay).OnTargetHit(CAST_ID, TARGET_ID, HitPosition, HitDirection);

            Assert.AreEqual(1, combat.Events.PlayerHits.Count);
            Assert.AreEqual(TARGET_ID, combat.Events.PlayerHits[0].TargetId);
        }

        [Test]
        public void Wolbaek_NoEventListener_IgnoresTargetHit()
        {
            BossMockCombat combat = CreateWolbaek();
            combat.Boss.SetEventListener(null);

            Assert.DoesNotThrow(() =>
                ((IBossTargetHitListener)combat.Boss).OnTargetHit(CAST_ID, TARGET_ID, HitPosition, HitDirection));
            Assert.AreEqual(0, combat.Events.PlayerHits.Count);
        }

        // 임팩트 표현이 월백에만 있어 다른 보스의 피격은 보내지 않는다
        [Test]
        public void Mucheol_TargetHit_SendsNothing()
        {
            BossPatternCatalogSO asset = ScriptableObject.CreateInstance<BossPatternCatalogSO>();
            try
            {
                var combat = new BossMockCombat(asset, MucheolPatternFactory.BOSS_ID, EBossDifficulty.Easy, RANDOM_SEED, false, false);

                ((IBossTargetHitListener)combat.Boss).OnTargetHit(CAST_ID, TARGET_ID, HitPosition, HitDirection);

                Assert.AreEqual(0, combat.Events.PlayerHits.Count);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
