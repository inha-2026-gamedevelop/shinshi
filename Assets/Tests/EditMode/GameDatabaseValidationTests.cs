// System
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Tests
{
    public class GameDatabaseValidationTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float INVALID_MOVE_SPEED       = 0f;
        private const int   SHORT_SCALE_TABLE_LENGTH = 2;

        /****************************************
        *                Fields
        ****************************************/

        private readonly List<Object> _created = new List<Object>();

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
        public void Validate_ProjectGameDB_HasNoErrors()
        {
            GameDatabaseSO root = Resources.Load<GameDatabaseSO>(Constants.Resource.GAME_DB_PATH);
            Assert.IsNotNull(root, "Resources/GameDB.asset이 없다");

            List<string> errors = Validate(root);

            CollectionAssert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void Validate_MissingReferences_ReportsEachReference()
        {
            GameDatabaseSO root = Create<GameDatabaseSO>();

            List<string> errors = Validate(root);

            Assert.AreEqual(GameDBTestFixtures.ReferenceCount, errors.Count, string.Join("\n", errors));
            StringAssert.Contains("_player", errors[0]);
            StringAssert.Contains("_session", errors[1]);
        }

        [Test]
        public void Validate_EmptyContentVersion_ReportsContentVersion()
        {
            GameDatabaseSO root = CreateValidRoot();
            SetString(root, "_contentVersion", string.Empty);

            List<string> errors = Validate(root);

            Assert.AreEqual(1, errors.Count, string.Join("\n", errors));
            StringAssert.Contains("_contentVersion", errors[0]);
        }

        [Test]
        public void Validate_DraftRoot_HasNoErrors()
        {
            GameDatabaseSO root = CreateValidRoot();

            List<string> errors = Validate(root);

            CollectionAssert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void Validate_DefaultConfigs_HaveNoErrors()
        {
            List<string> playerErrors  = Validate(Create<PlayerConfigSO>());
            List<string> sessionErrors = Validate(Create<SessionConfigSO>());

            CollectionAssert.IsEmpty(playerErrors, string.Join("\n", playerErrors));
            CollectionAssert.IsEmpty(sessionErrors, string.Join("\n", sessionErrors));
        }

        [Test]
        public void Validate_NonPositiveMoveSpeed_ReportsMoveSpeed()
        {
            PlayerConfigSO player = Create<PlayerConfigSO>();

            // 값을 바꾸면 OnValidate가 인스펙터용 오류를 바로 남긴다
            LogAssert.Expect(LogType.Error, new Regex("_moveSpeed"));
            SetFloat(player, "_moveSpeed", INVALID_MOVE_SPEED);

            List<string> errors = Validate(player);

            Assert.AreEqual(1, errors.Count, string.Join("\n", errors));
            StringAssert.Contains("_moveSpeed", errors[0]);
        }

        [Test]
        public void Validate_ShortScaleTable_ReportsTable()
        {
            SessionConfigSO session = Create<SessionConfigSO>();

            var serialized = new SerializedObject(session);
            serialized.FindProperty("_enemyHealthScale").arraySize = SHORT_SCALE_TABLE_LENGTH;

            LogAssert.Expect(LogType.Error, new Regex("_enemyHealthScale"));
            serialized.ApplyModifiedPropertiesWithoutUndo();

            List<string> errors = Validate(session);

            Assert.AreEqual(1, errors.Count, string.Join("\n", errors));
            StringAssert.Contains("_enemyHealthScale", errors[0]);
        }

        /****************************************
        *                Methods
        ****************************************/

        private T Create<T>() where T : ScriptableObject
        {
            T instance = ScriptableObject.CreateInstance<T>();
            _created.Add(instance);
            return instance;
        }

        private GameDatabaseSO CreateValidRoot()
        {
            return GameDBTestFixtures.CreateValidRoot(_created);
        }

        private static List<string> Validate(IDataValidatable target)
        {
            var errors = new List<string>();
            target.Validate(errors);
            return errors;
        }

        private static void SetString(Object target, string field, string value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string field, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
