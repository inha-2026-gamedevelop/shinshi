// System
using System;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class DungeonMapRoutingTests
    {
        // 범용 던전 슬롯이 첫 입장 계약에 맞는 전장으로 서버 씬을 바꾸는 규칙. 실제 씬 로드와 접속은 다인 접속 검증이 맡는다

        /****************************************
        *                Fields
        ****************************************/

        private const string BUILD_TYPE_NAME = "Shinshi.Editor.ShinshiBuild, Assembly-CSharp-Editor";
        private const string TOWN_SCENE_PATH = Constants.Scene.RELEASE_FOLDER + Constants.Scene.SHINSHI_TOWN + Constants.Scene.EXTENSION;
        private const string SORTIE_ID       = "6f1c2d3e-4a5b-4c6d-8e7f-001122334455";
        private const string API_ONE         = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc01";
        private const string API_TWO         = "0a1b2c3d-4e5f-4a6b-8c7d-8899aabbcc02";
        private const string MAP_ID          = "map_forest";
        private const string BATTLE_ID       = "battle_forest";
        private const string BOSS_MUCHEOL    = "boss_mucheol";
        private const string BOSS_WOLBAEK    = "boss_wolbaek";
        private const string OTHER_BOSS      = "boss_unknown";

        private SessionConfigSO       _config;
        private ServerSessionRegistry _registry;
        private DungeonSessionHost    _host;
        private FakeMapLoader         _loader;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _config   = ScriptableObject.CreateInstance<SessionConfigSO>();
            _registry = new ServerSessionRegistry(16);
            _host     = new DungeonSessionHost(_config, _registry, null, true, null, new NullSink());
            _loader   = new FakeMapLoader(Constants.Scene.DUNGEON_FOREST);
            _host.TracksPlayerReadiness = true;
        }

        [TearDown]
        public void TearDown()
        {
            _host.Dispose();
            Object.DestroyImmediate(_config);
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void ResolveSceneName_ByContract_PicksDedicatedMap()
        {
            Assert.AreEqual(Constants.Scene.DUNGEON_FOREST, DungeonMapRules.ResolveSceneName(false, null));
            Assert.AreEqual(Constants.Scene.DUNGEON_FOREST, DungeonMapRules.ResolveSceneName(false, BOSS_MUCHEOL));
            Assert.AreEqual(Constants.Scene.DUNGEON_MUCHEOL, DungeonMapRules.ResolveSceneName(true, BOSS_MUCHEOL));
            Assert.AreEqual(Constants.Scene.DUNGEON_WOLBAEK, DungeonMapRules.ResolveSceneName(true, BOSS_WOLBAEK));
            Assert.AreEqual(Constants.Scene.DUNGEON_FOREST, DungeonMapRules.ResolveSceneName(true, OTHER_BOSS));
        }

        [Test]
        public void ResolveBootSceneName_DefaultBoss_FollowsBoss()
        {
            Assert.AreEqual(Constants.Scene.DUNGEON_FOREST, DungeonMapRules.ResolveBootSceneName(null));
            Assert.AreEqual(Constants.Scene.DUNGEON_FOREST, DungeonMapRules.ResolveBootSceneName(string.Empty));
            Assert.AreEqual(Constants.Scene.DUNGEON_MUCHEOL, DungeonMapRules.ResolveBootSceneName(BOSS_MUCHEOL));
            Assert.AreEqual(Constants.Scene.DUNGEON_WOLBAEK, DungeonMapRules.ResolveBootSceneName(BOSS_WOLBAEK));
        }

        [Test]
        public void IsDungeonSceneName_OnlyThreeBattlefields()
        {
            Assert.IsTrue(DungeonMapRules.IsDungeonSceneName(Constants.Scene.DUNGEON_FOREST));
            Assert.IsTrue(DungeonMapRules.IsDungeonSceneName(Constants.Scene.DUNGEON_MUCHEOL));
            Assert.IsTrue(DungeonMapRules.IsDungeonSceneName(Constants.Scene.DUNGEON_WOLBAEK));
            Assert.IsFalse(DungeonMapRules.IsDungeonSceneName(Constants.Scene.SHINSHI_TOWN));
            Assert.IsFalse(DungeonMapRules.IsDungeonSceneName(null));
        }

        [Test]
        public void ResolveOnlineScene_Dungeon_BootsDefaultOrBossMap()
        {
            Assert.AreEqual(DungeonMapRules.ToScenePath(Constants.Scene.DUNGEON_FOREST),
                ShinshiNetworkManager.ResolveOnlineScene(EServerRole.Dungeon, TOWN_SCENE_PATH));
            Assert.AreEqual(DungeonMapRules.ToScenePath(Constants.Scene.DUNGEON_MUCHEOL),
                ShinshiNetworkManager.ResolveOnlineScene(EServerRole.Dungeon, TOWN_SCENE_PATH, BOSS_MUCHEOL));
            Assert.AreEqual(string.Empty, ShinshiNetworkManager.ResolveOnlineScene(EServerRole.Dungeon, string.Empty, BOSS_MUCHEOL),
                "단일 씬 개발 구성은 씬을 바꾸지 않는다");
        }

        [Test]
        public void Admit_FirstContractNeedsOtherMap_StartsLoadAndFixesContract()
        {
            _host.MapLoader = _loader;

            Assert.AreEqual(ERejectReason.None, Admit(API_ONE, GameSessionCodes.Mode.BOSS, BOSS_MUCHEOL));

            CollectionAssert.AreEqual(new[] { Constants.Scene.DUNGEON_MUCHEOL }, _loader.Requested);
            Assert.IsTrue(_host.IsMapTransitioning);
            Assert.AreEqual(BOSS_MUCHEOL, _host.Sortie.BossId);
            Assert.AreEqual(1, _host.AwaitingPlayerCount, "전환이 끝나기 전에는 플레이어 개체가 없어 시작하지 않는다");

            _loader.Finish();
            Assert.IsFalse(_host.IsMapTransitioning);
        }

        [Test]
        public void Admit_SameMapAlreadyLoaded_DoesNotReload()
        {
            _host.MapLoader = _loader;

            Assert.AreEqual(ERejectReason.None, Admit(API_ONE, GameSessionCodes.Mode.NORMAL, null));

            Assert.AreEqual(0, _loader.Requested.Count);
            Assert.IsFalse(_host.IsMapTransitioning);
        }

        [Test]
        public void Admit_SecondJoinerDuringLoad_PassesWithoutSecondLoadAndStaysTransitioning()
        {
            _host.MapLoader = _loader;
            Assert.AreEqual(ERejectReason.None, Admit(API_ONE, GameSessionCodes.Mode.BOSS, BOSS_WOLBAEK));

            Assert.AreEqual(ERejectReason.None, Admit(API_TWO, GameSessionCodes.Mode.BOSS, BOSS_WOLBAEK));

            Assert.AreEqual(1, _loader.Requested.Count);
            Assert.IsTrue(_host.IsMapTransitioning, "인증기는 두 번째 참가자의 확정도 같은 전환이 끝날 때까지 미룬다");
            Assert.AreEqual(2, _host.AwaitingPlayerCount);
        }

        [Test]
        public void Admit_OtherContractAfterFixed_IsRejected()
        {
            _host.MapLoader = _loader;
            Assert.AreEqual(ERejectReason.None, Admit(API_ONE, GameSessionCodes.Mode.BOSS, BOSS_MUCHEOL));
            _loader.Finish();

            Assert.AreEqual(ERejectReason.SessionMismatch, Admit(API_TWO, GameSessionCodes.Mode.BOSS, BOSS_WOLBAEK));
            Assert.AreEqual(1, _loader.Requested.Count);
        }

        [Test]
        public void Admit_LoadCannotStart_RejectsWithoutFixingContract()
        {
            _loader.CanStart = false;
            _host.MapLoader  = _loader;

            Assert.AreEqual(ERejectReason.SessionMismatch, Admit(API_ONE, GameSessionCodes.Mode.BOSS, BOSS_MUCHEOL));

            Assert.IsFalse(_host.Sortie.HasContract, "전환을 못 시작하면 출정을 고정하지 않아 같은 토큰이 다시 시도될 수 있다");
            Assert.IsFalse(_host.IsMapTransitioning);
        }

        [Test]
        public void Admit_NoMapLoader_KeepsSingleSceneBehavior()
        {
            Assert.AreEqual(ERejectReason.None, Admit(API_ONE, GameSessionCodes.Mode.BOSS, BOSS_MUCHEOL));
            Assert.IsFalse(_host.IsMapTransitioning);
        }

        [Test]
        public void TryExtendVerify_OnlyVerifyingEntry_MovesDeadline()
        {
            var table = new PendingAuthTable(4, 4);
            Assert.IsTrue(table.TryAdd(1, "127.0.0.1", 1.0));
            Assert.IsFalse(table.TryExtendVerify(1, 20.0), "요청 대기(Waiting)는 늘리지 않는다");

            Assert.IsTrue(table.TryBeginVerify(1, 7, 5.0));
            Assert.IsTrue(table.TryExtendVerify(1, 20.0));
            Assert.IsTrue(table.TryFindVerifying(7, out int connectionId, out double deadline));
            Assert.AreEqual(1, connectionId);
            Assert.AreEqual(20.0, deadline);

            var due = new List<PendingAuthEntry>();
            Assert.AreEqual(0, table.CollectDue(19.0, due));
            Assert.AreEqual(1, table.CollectDue(20.0, due));
            Assert.IsFalse(table.TryExtendVerify(99, 30.0));
        }

        [Test]
        public void BuildScenes_ClientAndServer_IncludeEveryBattlefield()
        {
            foreach (string method in new[] { "GetClientScenes", "GetServerScenes" })
            {
                string[] scenes = InvokeBuildScenes(method);
                CollectionAssert.Contains(scenes, DungeonMapRules.ToScenePath(Constants.Scene.DUNGEON_FOREST), method);
                CollectionAssert.Contains(scenes, DungeonMapRules.ToScenePath(Constants.Scene.DUNGEON_MUCHEOL), method);
                CollectionAssert.Contains(scenes, DungeonMapRules.ToScenePath(Constants.Scene.DUNGEON_WOLBAEK), method);
                CollectionAssert.Contains(scenes, DungeonMapRules.ToScenePath(Constants.Scene.DUNGEON_PROLOGUE), method);
            }
        }

        /****************************************
        *                Helpers
        ****************************************/

        private ERejectReason Admit(string apiId, int mode, string bossId)
        {
            var sortie = new SortieDescriptor(mode, MAP_ID, BATTLE_ID, bossId,
                (mode == GameSessionCodes.Mode.BOSS) ? GameSessionCodes.Difficulty.HARD : GameSessionCodes.Difficulty.EASY);
            JoinTokenClaims claims = new JoinTokenClaims(Constants.Network.PARTICIPANT_KEY_DUNGEON_PREFIX + apiId, null, null,
                SORTIE_ID, null, EPlayerClass.Warrior).WithSortie(in sortie);
            Assert.AreEqual(ERejectReason.None, _registry.TryAdmit(in claims, out SessionBinding binding));
            return _host.Admit(in claims, binding);
        }

        private static string[] InvokeBuildScenes(string methodName)
        {
            Type buildType = Type.GetType(BUILD_TYPE_NAME);
            Assert.IsNotNull(buildType, BUILD_TYPE_NAME);

            MethodInfo method = buildType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, methodName);

            return (string[])method.Invoke(null, null);
        }

        private sealed class FakeMapLoader : IDungeonMapLoader
        {
            public List<string> Requested { get; } = new List<string>();
            public bool         CanStart  { get; set; } = true;

            public string CurrentMapName { get; private set; }
            public bool   IsLoading      { get; private set; }

            public FakeMapLoader(string current)
            {
                CurrentMapName = current;
            }

            public bool TryBeginLoad(string mapName)
            {
                if ((!CanStart) || (IsLoading))
                {
                    return false;
                }

                Requested.Add(mapName);
                CurrentMapName = mapName;
                IsLoading      = true;
                return true;
            }

            public void Finish()
            {
                IsLoading = false;
            }
        }

        private sealed class NullSink : IBackendEventSink
        {
            public bool TrySend(string sessionId, long attemptId, SessionEvent[] events, int count, IBackendEventSinkListener listener)
            {
                listener.OnBatchCompleted(attemptId, EEventBatchResult.Committed);
                return true;
            }
        }
    }
}
