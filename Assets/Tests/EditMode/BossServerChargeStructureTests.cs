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
    // 무철 서버 몸체가 받은 돌진 구조물 배치를 복제 상태용 배치로 내주고, 워프 착지 검사가 부서지지 않은 구조물과의 겹침을 막는지 본다.
    // 무철이 아닌 보스는 배치를 받아도 쓰지 않는다
    public class BossServerChargeStructureTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CATALOG_PATH     = "Assets/09.Data/AI/BossPatternCatalog.asset";
        private const float  REFERENCE_ATTACK = 644.32f;
        private const float  REFERENCE_HEALTH = 3541f;
        private const float  HEALTH_SCALE     = 1f;
        private const int    RANDOM_SEED      = 7;

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void TryGetChargeStructureLayout_MucheolWithDevSites_ReturnsIntactLayout()
        {
            BossChargeStructureField field = BossChargeStructureField.CreateDevSites(Vector3.zero);
            BossServer boss = CreateBoss(MucheolPatternFactory.BOSS_ID, field);

            Assert.IsTrue(boss.TryGetChargeStructureLayout(out BossChargeStructureSnapshot layout));
            Assert.AreEqual(MucheolChargeStructureDevSites.COUNT, layout.Count);
            for (int i = 0; i < layout.Count; ++i)
            {
                Assert.IsFalse(layout.IsDestroyed(i));
            }
        }

        [Test]
        public void TryGetChargeStructureLayout_NotMucheol_ReturnsFalse()
        {
            BossChargeStructureField field = BossChargeStructureField.CreateDevSites(Vector3.zero);
            BossServer boss = CreateBoss(Constants.Combat.DEV_BOSS_ID, field);

            Assert.IsFalse(boss.TryGetChargeStructureLayout(out BossChargeStructureSnapshot _));
        }

        [Test]
        public void CanStandAt_IntactStructure_RejectsUntilDestroyed()
        {
            BossChargeStructureField field = BossChargeStructureField.CreateDevSites(Vector3.zero);
            BossServer boss = CreateBoss(MucheolPatternFactory.BOSS_ID, field);
            BossChargeStructureInfo first = field.CreateSnapshot().GetStructure(0);

            Assert.IsFalse(boss.CanStandAt(first.Center));

            Assert.IsTrue(field.TryDestroy(first.Id));
            Assert.IsTrue(boss.CanStandAt(first.Center));
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static BossServer CreateBoss(string bossId, BossChargeStructureField field)
        {
            BossPatternCatalogSO asset = AssetDatabase.LoadAssetAtPath<BossPatternCatalogSO>(CATALOG_PATH);
            Assume.That(asset, Is.Not.Null, "보스 카탈로그 에셋이 없다");

            var catalog  = new BossPatternCatalogSource(asset);
            var registry = new CombatTargetRegistry(Constants.Combat.MAX_COMBAT_TARGETS);
            return new BossServer(bossId, registry.IssueCombatId(), Vector3.zero, Vector3.forward, Vector3.zero,
                REFERENCE_ATTACK, REFERENCE_HEALTH, HEALTH_SCALE, catalog, registry, new AlwaysAppliedResolver(), null,
                new SeededBossRandom(RANDOM_SEED), chargeStructures: field);
        }

        private sealed class AlwaysAppliedResolver : IDamageResolver
        {
            private int _nextId = 1;

            public DamageResult Apply(in DamageRequest request)
            {
                return DamageResult.Applied(request.RawDamage, false, false);
            }

            public int IssueInstanceId()
            {
                int id = _nextId;
                ++_nextId;
                return id;
            }
        }
    }
}
