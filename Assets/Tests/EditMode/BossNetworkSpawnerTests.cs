// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Combat;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class BossNetworkSpawnerTests
    {
        // 스포너가 보스 정의 ID로 프리팹을 고르는 규칙만 본다. 맞는 프리팹이 없으면 기본 프리팹으로 돌아가고,
        // 기본 프리팹은 보스 ID를 지정하지 않은 프리팹이다. 실제 스폰은 서버가 열려 있어야 해서 여기서 하지 않는다

        /****************************************
        *                Fields
        ****************************************/

        private const string PREFAB_ID_FIELD = "_prefabBossId";
        private const string WOLBAEK_ID      = "boss_wolbaek";
        private const string MUCHEOL_ID      = "boss_mucheol";
        private const string UNKNOWN_ID      = "boss_unknown";

        private readonly List<GameObject> _created = new List<GameObject>();

        /****************************************
        *                Setup
        ****************************************/

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; ++i)
            {
                Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void SelectPrefab_MatchingBossId_ReturnsThatCandidate()
        {
            GameObject fallback = CreateBoss(string.Empty);
            GameObject wolbaek  = CreateBoss(WOLBAEK_ID);
            GameObject mucheol  = CreateBoss(MUCHEOL_ID);
            var candidates      = new List<GameObject> { fallback, wolbaek, mucheol };

            Assert.AreSame(mucheol, BossNetworkSpawner.SelectPrefab(candidates, fallback, MUCHEOL_ID));
            Assert.AreSame(wolbaek, BossNetworkSpawner.SelectPrefab(candidates, fallback, WOLBAEK_ID));
        }

        [Test]
        public void SelectPrefab_NoMatchingCandidate_ReturnsFallback()
        {
            GameObject fallback = CreateBoss(string.Empty);
            GameObject wolbaek  = CreateBoss(WOLBAEK_ID);
            var candidates      = new List<GameObject> { wolbaek, null, CreatePlain() };

            Assert.AreSame(fallback, BossNetworkSpawner.SelectPrefab(candidates, fallback, UNKNOWN_ID),
                "맞는 프리팹이 없는 보스는 기본 프리팹을 쓴다");
        }

        [Test]
        public void SelectPrefab_EmptyBossIdOrNoCandidates_ReturnsFallback()
        {
            GameObject fallback = CreateBoss(string.Empty);
            GameObject unnamed  = CreateBoss(string.Empty);
            var candidates      = new List<GameObject> { unnamed };

            Assert.AreSame(fallback, BossNetworkSpawner.SelectPrefab(candidates, fallback, string.Empty),
                "빈 보스 ID는 ID를 지정하지 않은 후보와 맞추지 않는다");
            Assert.AreSame(fallback, BossNetworkSpawner.SelectPrefab(candidates, fallback, null));
            Assert.AreSame(fallback, BossNetworkSpawner.SelectPrefab(null, fallback, MUCHEOL_ID));
            Assert.AreSame(fallback, BossNetworkSpawner.SelectPrefab(new List<GameObject>(), fallback, MUCHEOL_ID));
        }

        [Test]
        public void SelectPrefab_SameBossIdTwice_ReturnsFirst()
        {
            GameObject fallback = CreateBoss(string.Empty);
            GameObject first    = CreateBoss(MUCHEOL_ID);
            GameObject second   = CreateBoss(MUCHEOL_ID);

            Assert.AreSame(first, BossNetworkSpawner.SelectPrefab(new List<GameObject> { first, second }, fallback, MUCHEOL_ID));
        }

        [Test]
        public void FindPrefab_UnnamedBossPrefab_IsDefaultEvenWhenListedLater()
        {
            GameObject mucheol = CreateBoss(MUCHEOL_ID);
            GameObject unnamed = CreateBoss(string.Empty);
            NetworkManager manager = CreateManager(CreatePlain(), null, mucheol, unnamed);

            Assert.AreSame(unnamed, BossNetworkSpawner.FindPrefab(manager), "보스 ID를 지정하지 않은 프리팹이 기본이다");
        }

        [Test]
        public void FindPrefab_AllBossPrefabsNamed_ReturnsFirstBossPrefab()
        {
            GameObject mucheol = CreateBoss(MUCHEOL_ID);
            GameObject wolbaek = CreateBoss(WOLBAEK_ID);
            NetworkManager manager = CreateManager(CreatePlain(), mucheol, wolbaek);

            Assert.AreSame(mucheol, BossNetworkSpawner.FindPrefab(manager));
        }

        [Test]
        public void FindPrefab_NoBossPrefabOrNoManager_ReturnsNull()
        {
            NetworkManager manager = CreateManager(CreatePlain(), null);

            Assert.IsNull(BossNetworkSpawner.FindPrefab(manager));
            Assert.IsNull(BossNetworkSpawner.FindPrefab(null));
        }

        [Test]
        public void Constructor_PrefabWithoutBossState_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new BossNetworkSpawner(null));
            Assert.Throws<ArgumentException>(() => new BossNetworkSpawner(CreatePlain()));
            Assert.DoesNotThrow(() => new BossNetworkSpawner(CreateBoss(string.Empty), new List<GameObject>()));
        }

        /****************************************
        *                Helpers
        ****************************************/

        // 프리팹의 보스 ID는 인스펙터에서만 채우는 직렬화 필드라 시험에서는 직렬화 경로로 넣는다
        private GameObject CreateBoss(string prefabBossId)
        {
            var boss = new GameObject("BossPrefabTest", typeof(NetworkIdentity));
            _created.Add(boss);
            BossState state = boss.AddComponent<BossState>();

            var serialized = new SerializedObject(state);
            SerializedProperty property = serialized.FindProperty(PREFAB_ID_FIELD);
            Assert.IsNotNull(property, "BossState의 프리팹 보스 ID 필드 이름이 바뀌었다");
            property.stringValue = prefabBossId;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(prefabBossId, state.PrefabBossId);
            return boss;
        }

        private GameObject CreatePlain()
        {
            var plain = new GameObject("NotBossPrefabTest");
            _created.Add(plain);
            return plain;
        }

        private NetworkManager CreateManager(params GameObject[] spawnPrefabs)
        {
            var holder = new GameObject("BossSpawnerManagerTest");
            _created.Add(holder);
            NetworkManager manager = holder.AddComponent<NetworkManager>();
            manager.spawnPrefabs.Clear();
            manager.spawnPrefabs.AddRange(spawnPrefabs);
            return manager;
        }
    }
}
