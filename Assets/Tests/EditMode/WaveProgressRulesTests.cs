// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Data;
using Shinshi.Network;
using Shinshi.Systems;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public class WaveProgressRulesTests
    {
        // 서버 진행 값 -> 메시지 -> 직렬화 -> 진행 패널 칸 상태까지의 규칙을 본다

        /****************************************
        *                Fields
        ****************************************/

        private const float  TIME_LIMIT  = 420f;
        private const double SERVER_TIME = 100.0;

        private static readonly ushort[] PLANNED = { 10, 12, 8 };

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void NodeState_MidWave_ClearedBeforeCurrentAfterPending()
        {
            WaveProgressMessage progress = Make(1, 5, true, 30f);

            Assert.AreEqual(EWaveNodeState.Cleared, DungeonProgressRules.NodeState(in progress, 0));
            Assert.AreEqual(EWaveNodeState.Current, DungeonProgressRules.NodeState(in progress, 1));
            Assert.AreEqual(EWaveNodeState.Pending, DungeonProgressRules.NodeState(in progress, 2));
            Assert.AreEqual(10, DungeonProgressRules.NodeKilled(in progress, 0), "완료 칸은 계획 수만큼 처치했다");
            Assert.AreEqual(7, DungeonProgressRules.NodeKilled(in progress, 1), "현재 칸은 계획에서 남은 수를 뺀다");
            Assert.AreEqual(0, DungeonProgressRules.NodeKilled(in progress, 2));
            Assert.AreEqual(2, DungeonProgressRules.CurrentNumber(in progress));
            Assert.AreEqual(17f / 30f, DungeonProgressRules.OverallRatio(in progress), 0.0001f);
        }

        [Test]
        public void NodeState_WaveClearedAwaitingArrival_NextNodeIsCurrentWithZeroKills()
        {
            WaveProgressMessage progress = Make(0, 0, true, 50f);

            Assert.AreEqual(EWaveNodeState.Cleared, DungeonProgressRules.NodeState(in progress, 0));
            Assert.AreEqual(EWaveNodeState.Current, DungeonProgressRules.NodeState(in progress, 1));
            Assert.AreEqual(0, DungeonProgressRules.NodeKilled(in progress, 1));
            Assert.AreEqual(2, DungeonProgressRules.CurrentNumber(in progress));
        }

        [Test]
        public void NodeState_LastWaveCleared_AllClearedAndFullRatio()
        {
            WaveProgressMessage progress = Make(2, 0, false, 200f);

            Assert.AreEqual(DungeonProgressRules.ALL_CLEARED, DungeonProgressRules.CurrentNode(in progress));
            for (int i = 0; i < PLANNED.Length; ++i)
            {
                Assert.AreEqual(EWaveNodeState.Cleared, DungeonProgressRules.NodeState(in progress, i));
            }

            Assert.AreEqual(PLANNED.Length, DungeonProgressRules.CurrentNumber(in progress));
            Assert.AreEqual(1f, DungeonProgressRules.OverallRatio(in progress));
        }

        [Test]
        public void RemainingSeconds_RunningCountsOnStoppedFreezes()
        {
            WaveProgressMessage running = Make(0, 10, true, 20f);
            Assert.AreEqual(TIME_LIMIT - 25f, DungeonProgressRules.RemainingSeconds(in running, SERVER_TIME + 5.0), 0.0001f);
            Assert.AreEqual(TIME_LIMIT - 20f, DungeonProgressRules.RemainingSeconds(in running, SERVER_TIME - 1.0), 0.0001f,
                "서버 시각보다 이른 시계는 되감지 않는다");

            WaveProgressMessage stopped = Make(0, 10, false, 20f);
            Assert.AreEqual(TIME_LIMIT - 20f, DungeonProgressRules.RemainingSeconds(in stopped, SERVER_TIME + 60.0), 0.0001f);

            WaveProgressMessage over = Make(0, 10, true, TIME_LIMIT);
            Assert.AreEqual(0f, DungeonProgressRules.RemainingSeconds(in over, SERVER_TIME + 10.0));
        }

        [Test]
        public void Build_ClampsIndexAndRemaining()
        {
            var snapshot = new WaveProgressSnapshot(7, PLANNED.Length, -3, -1.0);
            WaveProgressMessage message = WaveProgressPublishRules.Build(in snapshot, true, TIME_LIMIT, SERVER_TIME, PLANNED);

            Assert.AreEqual(PLANNED.Length - 1, message.WaveIndex, "웨이브 번호는 마지막 칸을 넘지 않는다");
            Assert.AreEqual(0, message.RemainingInWave);
            Assert.AreEqual(0f, message.ElapsedSeconds);
            Assert.AreEqual(PLANNED.Length, message.WaveCount);
        }

        [Test]
        public void BuildPlanned_CountsEnemiesPerWave()
        {
            WaveConfigSO waves = ScriptableObject.CreateInstance<WaveConfigSO>();
            try
            {
                ushort[] planned = WaveProgressPublishRules.BuildPlanned(waves);

                Assert.AreEqual(waves.Waves.Count, planned.Length);
                int total = 0;
                for (int i = 0; i < planned.Length; ++i)
                {
                    Assert.AreEqual(waves.Waves[i].CountEnemies(), planned[i]);
                    total += planned[i];
                }

                Assert.AreEqual(waves.CountTotalEnemies(), total);
                Assert.AreEqual(0, WaveProgressPublishRules.BuildPlanned(null).Length);
            }
            finally
            {
                Object.DestroyImmediate(waves);
            }
        }

        [Test]
        public void Serializer_RoundTrip_KeepsAllFields()
        {
            WaveProgressMessage source = Make(1, 4, true, 33.5f);
            WaveProgressMessage copy;
            using (NetworkWriterPooled writer = NetworkWriterPool.Get())
            {
                writer.WriteWaveProgressMessage(source);
                using (NetworkReaderPooled reader = NetworkReaderPool.Get(writer.ToArraySegment()))
                {
                    copy = reader.ReadWaveProgressMessage();
                }
            }

            Assert.IsTrue(copy.SameAs(in source));
            Assert.AreEqual(source.ElapsedSeconds, copy.ElapsedSeconds);
            Assert.AreEqual(source.ServerTime, copy.ServerTime);
            for (int i = 0; i < PLANNED.Length; ++i)
            {
                Assert.AreEqual(PLANNED[i], copy.GetPlanned(i));
            }
        }

        [Test]
        public void SameAs_IgnoresTimeButNotProgress()
        {
            WaveProgressMessage source = Make(1, 4, true, 10f);

            Assert.IsTrue(source.SameAs(Make(1, 4, true, 99f)), "흐르는 시간만 다르면 다시 보내지 않는다");
            Assert.IsFalse(source.SameAs(Make(1, 3, true, 10f)));
            Assert.IsFalse(source.SameAs(Make(2, 4, true, 10f)));
            Assert.IsFalse(source.SameAs(Make(1, 4, false, 10f)), "멈춤은 보내야 클라이언트 시계가 선다");
        }

        /****************************************
        *                Methods
        ****************************************/

        private static WaveProgressMessage Make(byte waveIndex, ushort remaining, bool running, float elapsed)
        {
            return new WaveProgressMessage(waveIndex, remaining, running, elapsed, TIME_LIMIT, SERVER_TIME, PLANNED);
        }
    }
}
