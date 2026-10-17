// System
using System;
using System.IO;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;

namespace Shinshi.Tests
{
    // 무철 돌진 구조물 배치 복원 값이 쓰고 읽은 뒤에도 같은지, 상한을 넘는 배치를 쓰기와 읽기 모두에서 막는지,
    // 기록이 파괴를 이어 가고 스폰 초기 상태로 옮겨 복원으로 넘기는지, 클라이언트 표현이 연출을 한 번만 내는지 본다
    public class BossChargeStructureNetTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float EPSILON = 0.0001f;

        /****************************************
        *               Helpers
        ****************************************/

        private static BossChargeStructureSnapshot MakeLayout(int count, int destroyedIndex = -1)
        {
            var structures = new BossChargeStructureInfo[count];
            var destroyed  = new bool[count];
            for (int i = 0; i < count; ++i)
            {
                structures[i] = new BossChargeStructureInfo(i + 1, new Vector3(i, 0.5f, -i), 1f + (i * 0.25f), (i % 2) == 0);
                destroyed[i]  = i == destroyedIndex;
            }

            return new BossChargeStructureSnapshot(structures, destroyed, count);
        }

        private static void AssertSame(in BossChargeStructureSnapshot expected, in BossChargeStructureSnapshot actual)
        {
            Assert.AreEqual(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; ++i)
            {
                BossChargeStructureInfo a = expected.GetStructure(i);
                BossChargeStructureInfo b = actual.GetStructure(i);
                Assert.AreEqual(a.Id, b.Id);
                Assert.AreEqual(a.Center.x, b.Center.x, EPSILON);
                Assert.AreEqual(a.Center.y, b.Center.y, EPSILON);
                Assert.AreEqual(a.Center.z, b.Center.z, EPSILON);
                Assert.AreEqual(a.Radius, b.Radius, EPSILON);
                Assert.AreEqual(a.IsGimmickTarget, b.IsGimmickTarget);
                Assert.AreEqual(expected.IsDestroyed(i), actual.IsDestroyed(i));
            }
        }

        private static T RoundTrip<T>(Action<NetworkWriter> write, Func<NetworkReader, T> read)
        {
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                write(writer);
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    T value = read(reader);
                    Assert.AreEqual(0, reader.Remaining, "쓴 만큼 정확히 읽는다");
                    return value;
                }
            }
        }

        /****************************************
        *              Serializer
        ****************************************/

        [Test]
        public void StructureSnapshot_WriteRead_KeepsEveryField()
        {
            BossChargeStructureSnapshot source = MakeLayout(4, 2);

            BossChargeStructureSnapshot copy = RoundTrip(writer => writer.WriteBossChargeStructureSnapshot(source),
                reader => reader.ReadBossChargeStructureSnapshot());

            AssertSame(in source, in copy);
        }

        [Test]
        public void StructureSnapshot_AtLimit_RoundTrips()
        {
            BossChargeStructureSnapshot source = MakeLayout(Constants.Network.BOSS_CHARGE_STRUCTURE_MAX_COUNT);

            BossChargeStructureSnapshot copy = RoundTrip(writer => writer.WriteBossChargeStructureSnapshot(source),
                reader => reader.ReadBossChargeStructureSnapshot());

            Assert.AreEqual(Constants.Network.BOSS_CHARGE_STRUCTURE_MAX_COUNT, copy.Count);
        }

        [Test]
        public void StructureSnapshot_OverLimitOrShortArrays_WriteThrows()
        {
            BossChargeStructureSnapshot over  = MakeLayout(Constants.Network.BOSS_CHARGE_STRUCTURE_MAX_COUNT + 1);
            var                         shortFlags = new BossChargeStructureSnapshot(new BossChargeStructureInfo[2], new bool[1], 2);

            Assert.IsFalse(over.IsWritable);
            Assert.IsFalse(shortFlags.IsWritable);
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                Assert.Throws<InvalidDataException>(() => writer.WriteBossChargeStructureSnapshot(over));
                Assert.Throws<InvalidDataException>(() => writer.WriteBossChargeStructureSnapshot(shortFlags));
            }
        }

        // 받은 수가 상한을 넘으면 프로토콜 불일치로 보고 읽기를 멈춘다
        [Test]
        public void StructureSnapshot_ReadCountOverLimit_Throws()
        {
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                writer.WriteByte((byte)(Constants.Network.BOSS_CHARGE_STRUCTURE_MAX_COUNT + 1));
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    Assert.Throws<InvalidDataException>(() => reader.ReadBossChargeStructureSnapshot());
                }
            }
        }

        /****************************************
        *                Ledger
        ****************************************/

        [Test]
        public void Ledger_SetLayoutOverLimit_KeepsPrevious()
        {
            var ledger = new BossChargeStructureLedger();
            BossChargeStructureSnapshot layout = MakeLayout(3);
            Assert.IsTrue(ledger.SetLayout(in layout));

            BossChargeStructureSnapshot over = MakeLayout(Constants.Network.BOSS_CHARGE_STRUCTURE_MAX_COUNT + 1);
            Assert.IsFalse(ledger.SetLayout(in over));
            Assert.AreEqual(3, ledger.Count);
        }

        [Test]
        public void Ledger_MarkDestroyed_OnlyKnownAndOnce()
        {
            var ledger = new BossChargeStructureLedger();
            BossChargeStructureSnapshot layout = MakeLayout(3);
            ledger.SetLayout(in layout);

            Assert.IsFalse(ledger.MarkDestroyed(99), "배치에 없는 구조물");
            Assert.IsTrue(ledger.MarkDestroyed(2));
            Assert.IsFalse(ledger.MarkDestroyed(2), "이미 부서진 구조물");
            Assert.IsTrue(ledger.IsDestroyed(2));
        }

        // 서버 기록을 스폰 초기 상태로 쓰고 새 클라이언트 기록이 읽어 복원으로 넘긴다. 파괴 여부도 함께 간다
        [Test]
        public void Ledger_WriteReadReplay_RestoresLayoutAndDestroyed()
        {
            var server = new BossChargeStructureLedger();
            BossChargeStructureSnapshot layout = MakeLayout(4);
            server.SetLayout(in layout);
            server.MarkDestroyed(3);

            var client = new BossChargeStructureLedger();
            RoundTrip(writer => server.Write(writer), reader => { client.Read(reader); return 0; });

            var recorder = new BossEventRecorder();
            client.Replay(recorder);

            Assert.AreEqual(1, recorder.RestoredStructures.Count);
            BossChargeStructureSnapshot restored = recorder.RestoredStructures[0];
            Assert.AreEqual(4, restored.Count);
            for (int i = 0; i < restored.Count; ++i)
            {
                Assert.AreEqual(restored.GetStructure(i).Id == 3, restored.IsDestroyed(i));
            }

            Assert.AreEqual(0, recorder.RestoreCount, "구조물 복원은 진행 중 공격 복원에 섞이지 않는다");
        }

        [Test]
        public void Ledger_NoLayout_WritesFlagOnlyAndReplaysNothing()
        {
            var server = new BossChargeStructureLedger();
            var client = new BossChargeStructureLedger();
            RoundTrip(writer => server.Write(writer), reader => { client.Read(reader); return 0; });

            var recorder = new BossEventRecorder();
            client.Replay(recorder);

            Assert.IsFalse(client.HasLayout);
            Assert.AreEqual(0, recorder.RestoredStructures.Count);
        }

        // 전투가 끝나면 파괴 기록만 지우고 배치는 남긴다
        [Test]
        public void Ledger_ClearDestroyed_KeepsLayout()
        {
            var ledger = new BossChargeStructureLedger();
            BossChargeStructureSnapshot layout = MakeLayout(2);
            ledger.SetLayout(in layout);
            ledger.MarkDestroyed(1);

            ledger.ClearDestroyed();

            Assert.IsTrue(ledger.HasLayout);
            Assert.AreEqual(2, ledger.Count);
            Assert.IsFalse(ledger.IsDestroyed(1));
        }

        // 복원이 넘긴 값은 복사본이라 기록이 뒤에 바뀌어도 그대로다
        [Test]
        public void Ledger_SnapshotAfterReplay_IsNotAliased()
        {
            var ledger = new BossChargeStructureLedger();
            BossChargeStructureSnapshot layout = MakeLayout(2);
            ledger.SetLayout(in layout);
            BossChargeStructureSnapshot before = ledger.CreateSnapshot();

            ledger.MarkDestroyed(1);

            Assert.IsFalse(before.IsDestroyed(0));
        }

        /****************************************
        *                 View
        ****************************************/

        // 씬 표식이 없는 편집 모드에서는 임시 기둥을 만들고, 복원은 연출 없이, 파괴 사건은 구조물마다 한 번만 연출 훅을 부른다
        [Test]
        public void View_RestoreThenDestroyed_PillarsAndHookOnce()
        {
            Assert.IsNull(BossChargeStructureSet.Current, "시험 전제: 씬 표식 목록이 없다");

            var view = new MucheolChargeStructureView();
            int hooks = 0;
            view.Destroyed += (id, center) => ++hooks;
            try
            {
                BossChargeStructureSnapshot layout = MakeLayout(3, 0);
                view.Restore(in layout);

                Assert.AreEqual(3, view.Count);
                Assert.AreEqual(3, view.TemporaryPillarCount);
                Assert.IsTrue(view.IsDestroyed(1));
                Assert.AreEqual(0, hooks, "복원은 연출을 재생하지 않는다");

                Assert.IsFalse(view.ApplyDestroyed(1), "복원에서 이미 부서진 구조물");
                Assert.IsTrue(view.ApplyDestroyed(2));
                Assert.IsFalse(view.ApplyDestroyed(2));
                Assert.IsFalse(view.ApplyDestroyed(99), "모르는 구조물");
                Assert.AreEqual(1, hooks);
            }
            finally
            {
                view.Clear();
            }

            Assert.AreEqual(0, view.TemporaryPillarCount);
        }
    }
}
