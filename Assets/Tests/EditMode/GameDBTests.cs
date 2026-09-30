// System
using System;
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class GameDBTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly List<Object> _created = new List<Object>();

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            GameDB.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            GameDB.ResetForTests();

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
        public void Initialize_SameRootTwice_DoesNotThrow()
        {
            GameDatabaseSO root = LoadProjectRoot();

            GameDB.Initialize(root);

            Assert.DoesNotThrow(() => GameDB.Initialize(root));
            Assert.IsTrue(GameDB.IsInitialized);
        }

        [Test]
        public void Initialize_DifferentRootAfterInitialize_ThrowsAndKeepsFirst()
        {
            GameDatabaseSO first = LoadProjectRoot();
            GameDB.Initialize(first);

            GameDatabaseSO second = CreateValidRoot();

            Assert.Throws<InvalidOperationException>(() => GameDB.Initialize(second));
            Assert.AreSame(first.Player, GameDB.Player);
        }

        [Test]
        public void Initialize_InvalidRoot_ThrowsAndStaysUninitialized()
        {
            GameDatabaseSO invalid = Create<GameDatabaseSO>();

            Assert.Throws<InvalidOperationException>(() => GameDB.Initialize(invalid));
            Assert.IsFalse(GameDB.IsInitialized);
        }

        [Test]
        public void Initialize_NullRoot_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => GameDB.Initialize(null));
            Assert.IsFalse(GameDB.IsInitialized);
        }

        [Test]
        public void ContentVersion_BeforeInitialize_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => _ = GameDB.ContentVersion);
        }

        /****************************************
        *                Methods
        ****************************************/

        private static GameDatabaseSO LoadProjectRoot()
        {
            GameDatabaseSO root = Resources.Load<GameDatabaseSO>(Constants.Resource.GAME_DB_PATH);
            Assert.IsNotNull(root, "Resources/GameDB.asset이 없다");
            return root;
        }

        private T Create<T>() where T : ScriptableObject
        {
            T instance = ScriptableObject.CreateInstance<T>();
            _created.Add(instance);
            return instance;
        }

        private GameDatabaseSO CreateValidRoot()
        {
            GameDatabaseSO root = Create<GameDatabaseSO>();

            var serialized = new SerializedObject(root);
            serialized.FindProperty("_player").objectReferenceValue  = Create<PlayerConfigSO>();
            serialized.FindProperty("_session").objectReferenceValue = Create<SessionConfigSO>();
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }
    }
}
