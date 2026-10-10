// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class DungeonSceneRoutingTests
    {
        // 던전 역할의 접속 씬 선택과 빌드 목록 포함: 정적 규칙만 확인한다. 실제 서버 씬 전환과 접속은 다인 접속 검증이 맡는다

        /****************************************
        *                Fields
        ****************************************/

        private const string BUILD_TYPE_NAME     = "Shinshi.Editor.ShinshiBuild, Assembly-CSharp-Editor";
        private const string TOWN_SCENE_PATH     = Constants.Scene.RELEASE_FOLDER + Constants.Scene.SHINSHI_TOWN + Constants.Scene.EXTENSION;
        private const string DUNGEON_SCENE_PATH  = Constants.Scene.RELEASE_FOLDER + Constants.Scene.DUNGEON_FOREST + Constants.Scene.EXTENSION;
        private const string DUNGEON_SCENE_ASSET = "Assets/00.Scenes/Release/DungeonForest.unity";
        private const int    INVALID_ROLE_VALUE  = 99;
        private const string CHARACTER_ID        = "character-1";
        private const int    MAX_DRAIN_STEPS     = 100;

        private GameObject _host = null;

        /****************************************
        *                Setup
        ****************************************/

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void ResolveOnlineScene_TownRole_ReturnsTownScene()
        {
            Assert.AreEqual(TOWN_SCENE_PATH, ShinshiNetworkManager.ResolveOnlineScene(EServerRole.Town, TOWN_SCENE_PATH));
        }

        [Test]
        public void ResolveOnlineScene_DungeonRole_ReturnsDungeonForestScene()
        {
            Assert.AreEqual(DUNGEON_SCENE_PATH, ShinshiNetworkManager.ResolveOnlineScene(EServerRole.Dungeon, TOWN_SCENE_PATH));
        }

        [Test]
        public void ResolveOnlineScene_InvalidRole_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ShinshiNetworkManager.ResolveOnlineScene((EServerRole)INVALID_ROLE_VALUE, TOWN_SCENE_PATH));
        }

        [Test]
        public void ResolveOnlineScene_EmptyTownScene_KeepsEmptyForBothRoles()
        {
            Assert.AreEqual(string.Empty, ShinshiNetworkManager.ResolveOnlineScene(EServerRole.Town, string.Empty));
            Assert.AreEqual(string.Empty, ShinshiNetworkManager.ResolveOnlineScene(EServerRole.Dungeon, string.Empty));
            Assert.AreEqual(string.Empty, ShinshiNetworkManager.ResolveOnlineScene(EServerRole.Town, null));
            Assert.AreEqual(string.Empty, ShinshiNetworkManager.ResolveOnlineScene(EServerRole.Dungeon, null));
        }

        [Test]
        public void ResolveOnlineScene_RoleRoundTrip_RestoresTownScene()
        {
            string current = ShinshiNetworkManager.ResolveOnlineScene(EServerRole.Town, TOWN_SCENE_PATH);
            Assert.AreEqual(TOWN_SCENE_PATH, current);

            current = ShinshiNetworkManager.ResolveOnlineScene(EServerRole.Dungeon, TOWN_SCENE_PATH);
            Assert.AreEqual(DUNGEON_SCENE_PATH, current);

            current = ShinshiNetworkManager.ResolveOnlineScene(EServerRole.Town, TOWN_SCENE_PATH);
            Assert.AreEqual(TOWN_SCENE_PATH, current);

            current = ShinshiNetworkManager.ResolveOnlineScene(EServerRole.Dungeon, TOWN_SCENE_PATH);
            Assert.AreEqual(DUNGEON_SCENE_PATH, current);
        }

        [Test]
        public void DungeonForestScenePath_ConstantsScene_MatchesExistingSceneAsset()
        {
            Assert.AreEqual(DUNGEON_SCENE_ASSET, DUNGEON_SCENE_PATH);
            Assert.IsNotEmpty(AssetDatabase.AssetPathToGUID(DUNGEON_SCENE_PATH));
        }

        [Test]
        public void GetClientScenes_BuildList_ContainsDungeonForestAndTown()
        {
            string[] scenes = InvokeBuildScenes("GetClientScenes");

            CollectionAssert.Contains(scenes, DUNGEON_SCENE_PATH);
            CollectionAssert.Contains(scenes, TOWN_SCENE_PATH);
        }

        [Test]
        public void GetServerScenes_BuildList_ContainsDungeonForestAndTown()
        {
            string[] scenes = InvokeBuildScenes("GetServerScenes");

            CollectionAssert.Contains(scenes, DUNGEON_SCENE_PATH);
            CollectionAssert.Contains(scenes, TOWN_SCENE_PATH);
        }

        [Test]
        public void EditorBuildSettings_Scenes_ContainsEveryClientBuildScene()
        {
            string[] clientScenes = InvokeBuildScenes("GetClientScenes");
            EditorBuildSettingsScene[] settingsScenes = EditorBuildSettings.scenes;

            for (int i = 0; i < clientScenes.Length; ++i)
            {
                Assert.IsTrue(IsEnabledInSettings(settingsScenes, clientScenes[i]), clientScenes[i]);
            }

            Assert.IsTrue(IsEnabledInSettings(settingsScenes, DUNGEON_SCENE_PATH));
        }

        [Test]
        public void Run_AfterFirstStep_MarksTransferringBeforeFirstYield()
        {
            ShinshiNetworkManager manager = CreateManager(out SortieReturnFlow flow);
            IEnumerator run = flow.Run(CHARACTER_ID, null);

            Assert.IsFalse(manager.IsTransferringServer);
            Assert.IsTrue(run.MoveNext());

            Assert.IsTrue(manager.IsTransferringServer);
            Assert.IsTrue(flow.IsRunning);
            ((IDisposable)run).Dispose();
        }

        [Test]
        public void Run_Disposed_ClearsTransferringAndRunningFlags()
        {
            ShinshiNetworkManager manager = CreateManager(out SortieReturnFlow flow);
            IEnumerator run = flow.Run(CHARACTER_ID, null);
            run.MoveNext();

            ((IDisposable)run).Dispose();

            Assert.IsFalse(manager.IsTransferringServer);
            Assert.IsFalse(flow.IsRunning);
        }

        [Test]
        public void Run_DuplicateWhileRunning_RejectsWithoutClearingRunningFlags()
        {
            ShinshiNetworkManager manager = CreateManager(out SortieReturnFlow flow);
            IEnumerator first = flow.Run(CHARACTER_ID, null);
            first.MoveNext();

            var results = new List<ERejectReason>();
            IEnumerator second = flow.Run(CHARACTER_ID, results.Add);
            Assert.IsFalse(second.MoveNext());

            CollectionAssert.AreEqual(new[] { ERejectReason.PendingCommit }, results);
            Assert.IsTrue(flow.IsRunning);
            Assert.IsTrue(manager.IsTransferringServer);
            ((IDisposable)first).Dispose();
            Assert.IsFalse(manager.IsTransferringServer);
        }

        [Test]
        public void Run_NormalCompletion_ClearsFlagsAndReportsOnce()
        {
            Assume.That(LauncherSession.HasSession, Is.False, "game session present, request would leave the process");
            ShinshiNetworkManager manager = CreateManager(out SortieReturnFlow flow);
            var results = new List<ERejectReason>();

            Drain(flow.Run(CHARACTER_ID, results.Add));

            Assert.AreEqual(1, results.Count);
            Assert.AreNotEqual(ERejectReason.None, results[0]);
            Assert.IsFalse(manager.IsTransferringServer);
            Assert.IsFalse(flow.IsRunning);
        }

        /****************************************
        *                Methods
        ****************************************/

        private ShinshiNetworkManager CreateManager(out SortieReturnFlow flow)
        {
            _host = new GameObject("DungeonSceneRoutingTests");
            var manager       = _host.AddComponent<ShinshiNetworkManager>();
            var authenticator = _host.AddComponent<ShinshiClientAuthenticator>();
            flow = new SortieReturnFlow(new LobbyApiClient(), manager, authenticator);
            return manager;
        }

        // 중첩 코루틴을 대기 없이 끝까지 돌린다, 시간 대기가 걸리면 무한히 기다리지 않고 실패시킨다
        private static void Drain(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            for (int step = 0; stack.Count > 0; ++step)
            {
                Assert.Less(step, MAX_DRAIN_STEPS);
                IEnumerator top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop();
                }
                else if (top.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                }
            }
        }

        // ShinshiBuild는 별도 어셈블리가 없는 에디터 코드라 이 테스트 어셈블리가 직접 참조할 수 없다
        private static string[] InvokeBuildScenes(string methodName)
        {
            Type buildType = Type.GetType(BUILD_TYPE_NAME);
            Assert.IsNotNull(buildType, BUILD_TYPE_NAME);

            MethodInfo method = buildType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, methodName);

            return (string[])method.Invoke(null, null);
        }

        private static bool IsEnabledInSettings(EditorBuildSettingsScene[] scenes, string path)
        {
            for (int i = 0; i < scenes.Length; ++i)
            {
                if ((scenes[i].enabled) && (string.Equals(scenes[i].path, path, StringComparison.Ordinal)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
