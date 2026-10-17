// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    // 무철 돌진 구조물 조회가 물리 없이 원판과 원기둥 접촉 거리를 정확히 내는지, 부서진 구조물을 빼는지, 같은 거리의 우선순위와
    // 배치 거절 규칙을 지키는지 본다. 거리와 반경은 계산을 손으로 맞출 수 있게 고른 시험값이다
    public class BossChargeStructureFieldTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float BODY      = 2f;
        private const float RADIUS    = 1f;
        private const float FAR       = 100f;
        private const float EPSILON   = 0.0001f;

        /****************************************
        *               Helpers
        ****************************************/

        private static BossChargeStructureField Create(params BossChargeStructureInfo[] structures)
        {
            Assert.IsTrue(BossChargeStructureField.TryCreate(structures, out BossChargeStructureField field, out string reason), reason);
            return field;
        }

        private static BossChargeStructureInfo At(int id, float x, float z, float radius = RADIUS, bool target = true)
        {
            return new BossChargeStructureInfo(id, new Vector3(x, 0f, z), radius, target);
        }

        /****************************************
        *                Query
        ****************************************/

        // 정면 10m의 반경 1m 기둥에 몸체 반경 2m가 닿는 거리는 10 - 3 = 7m다
        [Test]
        public void TryGetFirstStructure_HeadOn_ReturnsContactDistance()
        {
            BossChargeStructureField field = Create(At(1, 0f, 10f));

            Assert.IsTrue(field.TryGetFirstStructure(Vector3.zero, Vector3.forward, FAR, BODY, out BossChargeStructureHit hit));
            Assert.AreEqual(1, hit.StructureId);
            Assert.AreEqual(7f, hit.Distance, EPSILON);
        }

        // 옆으로 2m 비켜 선 기둥은 반경 합 3m 원과 만나 거리 10 - sqrt(9 - 4)다
        [Test]
        public void TryGetFirstStructure_OffsetPath_ReturnsChordDistance()
        {
            BossChargeStructureField field = Create(At(1, 2f, 10f));

            Assert.IsTrue(field.TryGetFirstStructure(Vector3.zero, Vector3.forward, FAR, BODY, out BossChargeStructureHit hit));
            Assert.AreEqual(10f - Mathf.Sqrt(5f), hit.Distance, EPSILON);
        }

        // 높이와 방향 길이는 쓰지 않는다. 기울어지고 길이가 다른 방향도 수평 단위 방향으로 본다
        [Test]
        public void TryGetFirstStructure_TiltedDirectionAndHeight_UsesHorizontalPlane()
        {
            BossChargeStructureField field = Create(new BossChargeStructureInfo(1, new Vector3(0f, 5f, 10f), RADIUS, true));

            Assert.IsTrue(field.TryGetFirstStructure(new Vector3(0f, -3f, 0f), new Vector3(0f, 4f, 3f), FAR, BODY,
                out BossChargeStructureHit hit));
            Assert.AreEqual(7f, hit.Distance, EPSILON);
        }

        [Test]
        public void TryGetFirstStructure_GrazingMiss_ReturnsFalse()
        {
            BossChargeStructureField field = Create(At(1, 3.01f, 10f));

            Assert.IsFalse(field.TryGetFirstStructure(Vector3.zero, Vector3.forward, FAR, BODY, out BossChargeStructureHit _));
        }

        [Test]
        public void TryGetFirstStructure_BeyondMaxDistance_ReturnsFalse()
        {
            BossChargeStructureField field = Create(At(1, 0f, 10f));

            Assert.IsFalse(field.TryGetFirstStructure(Vector3.zero, Vector3.forward, 6.99f, BODY, out BossChargeStructureHit _));
            Assert.IsTrue(field.TryGetFirstStructure(Vector3.zero, Vector3.forward, 7f, BODY, out BossChargeStructureHit _));
        }

        [Test]
        public void TryGetFirstStructure_Behind_ReturnsFalse()
        {
            BossChargeStructureField field = Create(At(1, 0f, -10f));

            Assert.IsFalse(field.TryGetFirstStructure(Vector3.zero, Vector3.forward, FAR, BODY, out BossChargeStructureHit _));
        }

        [Test]
        public void TryGetFirstStructure_TwoOnPath_ReturnsNearest()
        {
            BossChargeStructureField field = Create(At(9, 0f, 20f), At(4, 0f, 10f));

            Assert.IsTrue(field.TryGetFirstStructure(Vector3.zero, Vector3.forward, FAR, BODY, out BossChargeStructureHit hit));
            Assert.AreEqual(4, hit.StructureId);
        }

        // 같은 거리에서 둘이 닿으면 식별자가 작은 쪽이다. 배치 순서와 상관없다
        [Test]
        public void TryGetFirstStructure_EqualDistance_ReturnsSmallerId()
        {
            BossChargeStructureField field = Create(At(7, 1f, 10f), At(3, -1f, 10f));

            Assert.IsTrue(field.TryGetFirstStructure(Vector3.zero, Vector3.forward, FAR, BODY, out BossChargeStructureHit hit));
            Assert.AreEqual(3, hit.StructureId);
        }

        [Test]
        public void TryGetFirstStructure_AfterDestroy_SkipsDestroyedAndReturnsNext()
        {
            BossChargeStructureField field = Create(At(1, 0f, 10f), At(2, 0f, 20f));

            Assert.IsTrue(field.TryDestroy(1));
            Assert.IsTrue(field.TryGetFirstStructure(Vector3.zero, Vector3.forward, FAR, BODY, out BossChargeStructureHit hit));
            Assert.AreEqual(2, hit.StructureId);

            Assert.IsTrue(field.TryDestroy(2));
            Assert.IsFalse(field.TryGetFirstStructure(Vector3.zero, Vector3.forward, FAR, BODY, out BossChargeStructureHit _));
        }

        // 구조물 앞에 멈춘 보스는 반경 합 경계에 있다. 그 구조물 쪽으로는 거리 0으로 막고, 돌아서 나갈 때는 막지 않는다
        [Test]
        public void TryGetFirstStructure_StoppedAtContact_BlocksTowardOnly()
        {
            BossChargeStructureField field = Create(At(1, 0f, 10f));
            Vector3 stopped = new Vector3(0f, 0f, 7.0001f);

            Assert.IsTrue(field.TryGetFirstStructure(stopped, Vector3.forward, FAR, BODY, out BossChargeStructureHit hit));
            Assert.AreEqual(0f, hit.Distance, EPSILON);
            Assert.IsFalse(field.TryGetFirstStructure(stopped, Vector3.back, FAR, BODY, out BossChargeStructureHit _));
            Assert.IsFalse(field.TryGetFirstStructure(stopped, Vector3.right, FAR, BODY, out BossChargeStructureHit _));
        }

        [Test]
        public void TryGetFirstStructure_ZeroDirection_ReturnsFalse()
        {
            BossChargeStructureField field = Create(At(1, 0f, 10f));

            Assert.IsFalse(field.TryGetFirstStructure(Vector3.zero, Vector3.up, FAR, BODY, out BossChargeStructureHit _));
        }

        /****************************************
        *            Destroy And Overlap
        ****************************************/

        [Test]
        public void TryDestroy_UnknownOrTwice_ReturnsFalse()
        {
            BossChargeStructureField field = Create(At(1, 0f, 10f));

            Assert.IsFalse(field.TryDestroy(5));
            Assert.IsTrue(field.TryDestroy(1));
            Assert.IsFalse(field.TryDestroy(1));
            Assert.IsTrue(field.IsDestroyed(1));
        }

        [Test]
        public void OverlapsIntact_InsideTouchingAndDestroyed_OnlyInsideOverlaps()
        {
            BossChargeStructureField field = Create(At(1, 0f, 10f));

            Assert.IsTrue(field.OverlapsIntact(new Vector3(0f, 0f, 8f), BODY));
            Assert.IsFalse(field.OverlapsIntact(new Vector3(0f, 0f, 7f), BODY), "닿기만 한 것은 겹침이 아니다");

            field.TryDestroy(1);
            Assert.IsFalse(field.OverlapsIntact(new Vector3(0f, 0f, 8f), BODY));
        }

        [Test]
        public void CreateSnapshot_AfterDestroy_KeepsLayoutAndFlags()
        {
            BossChargeStructureField field = Create(At(1, 0f, 10f), At(2, 5f, 5f, 2f, false));
            field.TryDestroy(2);

            BossChargeStructureSnapshot snapshot = field.CreateSnapshot();

            Assert.AreEqual(2, snapshot.Count);
            Assert.AreEqual(1, snapshot.GetStructure(0).Id);
            Assert.IsFalse(snapshot.IsDestroyed(0));
            Assert.AreEqual(2f, snapshot.GetStructure(1).Radius, EPSILON);
            Assert.IsFalse(snapshot.GetStructure(1).IsGimmickTarget);
            Assert.IsTrue(snapshot.IsDestroyed(1));
        }

        /****************************************
        *               Creation
        ****************************************/

        [Test]
        public void TryCreate_ZeroOrNegativeId_Rejects()
        {
            Assert.IsFalse(BossChargeStructureField.TryCreate(new[] { At(0, 0f, 10f) }, out BossChargeStructureField _, out string _));
            Assert.IsFalse(BossChargeStructureField.TryCreate(new[] { At(-1, 0f, 10f) }, out BossChargeStructureField _, out string _));
        }

        [Test]
        public void TryCreate_DuplicateId_Rejects()
        {
            Assert.IsFalse(BossChargeStructureField.TryCreate(new[] { At(1, 0f, 10f), At(1, 5f, 10f) },
                out BossChargeStructureField field, out string reason));
            Assert.IsNull(field);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void TryCreate_BadRadius_Rejects()
        {
            Assert.IsFalse(BossChargeStructureField.TryCreate(new[] { At(1, 0f, 10f, 0f) }, out BossChargeStructureField _, out string _));
            Assert.IsFalse(BossChargeStructureField.TryCreate(new[] { At(1, 0f, 10f, float.NaN) },
                out BossChargeStructureField _, out string _));
        }

        [Test]
        public void TryCreate_OverReplicationLimit_Rejects()
        {
            var structures = new List<BossChargeStructureInfo>();
            for (int i = 0; i <= Constants.Network.BOSS_CHARGE_STRUCTURE_MAX_COUNT; ++i)
            {
                structures.Add(At(i + 1, i * 5f, 10f));
            }

            Assert.IsFalse(BossChargeStructureField.TryCreate(structures, out BossChargeStructureField _, out string _));
        }

        [Test]
        public void GimmickTargetIds_MixedTargets_ListsOnlyTargetsInOrder()
        {
            BossChargeStructureField field = Create(At(5, 0f, 10f), At(2, 5f, 10f, RADIUS, false), At(9, 9f, 10f));

            CollectionAssert.AreEqual(new[] { 5, 9 }, field.GimmickTargetIds);
        }

        // 임시 개발 지점은 네 곳, 식별자 1에서 4, 모두 지정이고 전장 중심을 기준으로 움직인다
        [Test]
        public void CreateDevSites_ArenaCenter_FourDesignatedAroundCenter()
        {
            Vector3 center = new Vector3(100f, 2f, -50f);
            BossChargeStructureField field = BossChargeStructureField.CreateDevSites(center);

            Assert.IsNotNull(field);
            Assert.AreEqual(MucheolChargeStructureDevSites.COUNT, field.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, field.GimmickTargetIds);

            BossChargeStructureSnapshot snapshot = field.CreateSnapshot();
            for (int i = 0; i < snapshot.Count; ++i)
            {
                Vector3 offset = snapshot.GetStructure(i).Center - center;
                offset.y = 0f;
                Assert.Greater(offset.magnitude, BODY + snapshot.GetStructure(i).Radius, "중심의 보스와 겹치지 않는다");
            }
        }
    }
}
