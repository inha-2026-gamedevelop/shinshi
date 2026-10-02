// System
using System;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

namespace Shinshi.Tests
{
    public class CompanionSpawnTrackerTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int PARTICIPANT_ID = 7;
        private const int MELEE_SLOT     = 0;
        private const int RANGED_SLOT    = 1;
        private const int FIRST_ID       = 101;
        private const int SECOND_ID      = 102;
        private const int RESUMMON_ID    = 103;

        private FakeCompanionObjectHost _host;
        private CompanionSpawnTracker   _tracker;
        private FakeSlotSource          _source;

        /****************************************
        *              Unity Event
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _host    = new FakeCompanionObjectHost();
            _tracker = new CompanionSpawnTracker(_host, Constants.Combat.MAX_COMPANION_OBJECTS);
            _source  = new FakeSlotSource(PARTICIPANT_ID);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Constructor_InvalidArguments_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CompanionSpawnTracker(null, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompanionSpawnTracker(_host, 0));
        }

        [Test]
        public void Track_NoActiveCompanion_SpawnsNothing()
        {
            _tracker.Track(_source);
            _tracker.Track(null);

            Assert.AreEqual(0, _host.Spawns.Count);
            Assert.AreEqual(0, _tracker.Count);
        }

        [Test]
        public void Track_SummonedCompanion_SpawnsOnceAcrossTicks()
        {
            _source.Ids[MELEE_SLOT] = FIRST_ID;

            Tick(_source);
            Tick(_source);
            Tick(_source);

            Assert.AreEqual(1, _host.Spawns.Count);
            Assert.AreSame(_source, _host.Spawns[0].Source);
            Assert.AreEqual(MELEE_SLOT, _host.Spawns[0].Slot);
            Assert.AreEqual(FIRST_ID, _host.Spawns[0].CompanionId);
            Assert.AreEqual(0, _host.Despawns.Count);
            Assert.IsTrue(_tracker.IsTracked(_source, MELEE_SLOT));
            Assert.IsFalse(_tracker.IsTracked(_source, RANGED_SLOT));
        }

        [Test]
        public void Track_BothSlotsAndTwoOwners_SpawnsOneObjectPerSlot()
        {
            var other = new FakeSlotSource(PARTICIPANT_ID + 1);
            _source.Ids[MELEE_SLOT]  = FIRST_ID;
            _source.Ids[RANGED_SLOT] = SECOND_ID;
            other.Ids[MELEE_SLOT]    = RESUMMON_ID;

            Tick(_source, other);

            Assert.AreEqual(3, _host.Spawns.Count);
            Assert.AreEqual(3, _tracker.Count);
            Assert.IsTrue(_tracker.IsTracked(other, MELEE_SLOT));
        }

        [Test]
        public void ReleaseStale_CompanionExpiredOrOwnerDied_DespawnsAndForgets()
        {
            _source.Ids[MELEE_SLOT]  = FIRST_ID;
            _source.Ids[RANGED_SLOT] = SECOND_ID;
            Tick(_source);

            _source.Ids[MELEE_SLOT] = CombatIds.NONE;
            Tick(_source);

            Assert.AreEqual(1, _host.Despawns.Count);
            Assert.AreEqual(MELEE_SLOT, _host.Despawns[0].Slot);
            Assert.IsFalse(_tracker.IsTracked(_source, MELEE_SLOT));
            Assert.IsTrue(_tracker.IsTracked(_source, RANGED_SLOT));

            _source.Ids[RANGED_SLOT] = CombatIds.NONE;
            Tick(_source);

            Assert.AreEqual(2, _host.Despawns.Count);
            Assert.AreEqual(0, _tracker.Count);
            Assert.AreEqual(0, _host.Live);
        }

        [Test]
        public void ReleaseStale_ResummonChangesCombatId_ReplacesObjectInSameTick()
        {
            _source.Ids[MELEE_SLOT] = FIRST_ID;
            Tick(_source);

            _source.Ids[MELEE_SLOT] = RESUMMON_ID;
            Tick(_source);

            Assert.AreEqual(1, _host.Despawns.Count);
            Assert.AreEqual(2, _host.Spawns.Count);
            Assert.AreEqual(RESUMMON_ID, _host.Spawns[1].CompanionId);
            Assert.AreEqual(1, _host.Live);
            Assert.AreEqual(1, _tracker.Count);
        }

        [Test]
        public void ReleaseStale_OwnerRemovedFromTickLists_StillDespawnsWhenCompanionStops()
        {
            _source.Ids[MELEE_SLOT] = FIRST_ID;
            Tick(_source);

            // 소유자가 목록에서 빠져도(보존 해제, 개체 정리) 정리 경로가 동료를 멈추면 다음 틱에 내린다
            _source.Ids[MELEE_SLOT] = CombatIds.NONE;
            Tick();

            Assert.AreEqual(1, _host.Despawns.Count);
            Assert.AreEqual(0, _tracker.Count);
        }

        [Test]
        public void Track_SameSourceMovedToRetainedList_KeepsExistingObject()
        {
            _source.Ids[RANGED_SLOT] = SECOND_ID;
            Tick(_source);

            // 연결이 끊겨 보존 목록으로 옮겨도, 재접속해 새 플레이어로 넘어가도 같은 실행 객체다
            Tick(_source);
            Tick(_source);

            Assert.AreEqual(1, _host.Spawns.Count);
            Assert.AreEqual(0, _host.Despawns.Count);
        }

        [Test]
        public void Track_SpawnFails_RetriesOnNextTick()
        {
            _source.Ids[MELEE_SLOT] = FIRST_ID;
            _host.FailSpawn         = true;
            Tick(_source);

            Assert.AreEqual(0, _tracker.Count);

            _host.FailSpawn = false;
            Tick(_source);

            Assert.AreEqual(1, _host.Spawns.Count);
            Assert.AreEqual(1, _tracker.Count);
        }

        [Test]
        public void ReleaseAll_SessionEnd_DespawnsEveryTrackedObject()
        {
            var other = new FakeSlotSource(PARTICIPANT_ID + 1);
            _source.Ids[MELEE_SLOT]  = FIRST_ID;
            _source.Ids[RANGED_SLOT] = SECOND_ID;
            other.Ids[RANGED_SLOT]   = RESUMMON_ID;
            Tick(_source, other);

            _tracker.ReleaseAll();
            _tracker.ReleaseAll();

            Assert.AreEqual(3, _host.Despawns.Count);
            Assert.AreEqual(0, _host.Live);
            Assert.AreEqual(0, _tracker.Count);
        }

        [Test]
        public void Track_OverCapacity_SkipsAndLogsOnce()
        {
            var tracker = new CompanionSpawnTracker(_host, 1);
            _source.Ids[MELEE_SLOT]  = FIRST_ID;
            _source.Ids[RANGED_SLOT] = SECOND_ID;
            LogAssert.Expect(LogType.Error, new Regex("상한"));

            tracker.ReleaseStale();
            tracker.Track(_source);
            tracker.ReleaseStale();
            tracker.Track(_source);

            Assert.AreEqual(1, tracker.Count);
            Assert.AreEqual(1, _host.Spawns.Count);
            LogAssert.NoUnexpectedReceived();
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void Tick(params ICompanionSlotSource[] sources)
        {
            _tracker.ReleaseStale();
            for (int i = 0; i < sources.Length; ++i)
            {
                _tracker.Track(sources[i]);
            }
        }

        private sealed class FakeSlotSource : ICompanionSlotSource
        {
            public int[] Ids { get; } = new int[Constants.Combat.COMPANIONS_PER_PLAYER];

            public int ParticipantId      { get; }
            public int CompanionSlotCount => Ids.Length;

            public FakeSlotSource(int participantId) { ParticipantId = participantId; }

            public int GetActiveCompanionId(int slot) { return Ids[slot]; }

            public bool BindCompanionState(int slot, CompanionCombatState state) { return true; }

            public void UnbindCompanionState(int slot) { }
        }
    }
}
