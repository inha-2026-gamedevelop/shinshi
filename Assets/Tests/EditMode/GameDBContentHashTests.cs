// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Data;
using Shinshi.Network;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class GameDBContentHashTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   SHA256_HEX_LENGTH = 64;
        private const float VALUE_CHANGE      = 0.125f;
        private const int   FIRST_INDEX       = 0;
        private const int   SECOND_INDEX      = 1;

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
        public void Compute_EqualSeparateAssets_ReturnsSameLowercaseSha256()
        {
            GameDatabaseSO first  = CreateRoot();
            GameDatabaseSO second = CreateRoot();
            first.name         = "first root";
            second.name        = "second root";
            first.Player.name  = "first player";
            second.Player.name = "second player";

            string digest = GameDBContentHash.Compute(first);

            Assert.AreEqual(digest, GameDBContentHash.Compute(first));
            Assert.AreEqual(digest, GameDBContentHash.Compute(second));
            Assert.AreEqual(SHA256_HEX_LENGTH, digest.Length);
            StringAssert.IsMatch("^[0-9a-f]+$", digest);
        }

        [Test]
        public void Compute_InitializedGameDB_MatchesDirectRoot()
        {
            GameDatabaseSO root = CreateRoot();
            GameDB.Initialize(root);

            Assert.AreEqual(GameDBContentHash.Compute(root), GameDBContentHash.Compute());
        }

        [Test]
        public void Compute_BeforeGameDBInitialization_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => GameDBContentHash.Compute());
        }

        [Test]
        public void Compute_NullRoot_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => GameDBContentHash.Compute(null));
        }

        [Test]
        public void Compute_MissingCatalog_Throws()
        {
            GameDatabaseSO root = CreateRoot();
            SetField(root, "_rewards", null);

            Assert.Throws<InvalidOperationException>(() => GameDBContentHash.Compute(root));
        }

        [Test]
        public void Compute_ContentVersionChanged_ChangesHash()
        {
            GameDatabaseSO root     = CreateRoot();
            string         original = GameDBContentHash.Compute(root);
            SetField(root, "_contentVersion", "hash-test-version");

            Assert.AreNotEqual(original, GameDBContentHash.Compute(root));
        }

        [TestCase(nameof(GameDatabaseSO.Player))]
        [TestCase(nameof(GameDatabaseSO.Session))]
        [TestCase(nameof(GameDatabaseSO.Skills))]
        [TestCase(nameof(GameDatabaseSO.Talismans))]
        [TestCase(nameof(GameDatabaseSO.Enemies))]
        [TestCase(nameof(GameDatabaseSO.BossPatterns))]
        [TestCase(nameof(GameDatabaseSO.Waves))]
        [TestCase(nameof(GameDatabaseSO.Equipment))]
        [TestCase(nameof(GameDatabaseSO.Growth))]
        [TestCase(nameof(GameDatabaseSO.Rewards))]
        [TestCase(nameof(GameDatabaseSO.Life))]
        [TestCase(nameof(GameDatabaseSO.Progression))]
        [TestCase(nameof(GameDatabaseSO.Avatar))]
        [TestCase(nameof(GameDatabaseSO.Camera))]
        public void Compute_CatalogSchemaVersionChanged_ChangesHash(string catalogProperty)
        {
            GameDatabaseSO root     = CreateRoot();
            var            catalog  = (GameDataSO)typeof(GameDatabaseSO).GetProperty(catalogProperty).GetValue(root);
            string         original = GameDBContentHash.Compute(root);
            SetField(catalog, "_schemaVersion", catalog.SchemaVersion + 1);

            Assert.AreNotEqual(original, GameDBContentHash.Compute(root), catalogProperty);
        }

        [Test]
        public void Compute_SingleBalanceFloatChanged_ChangesHash()
        {
            GameDatabaseSO root     = CreateRoot();
            string         original = GameDBContentHash.Compute(root);
            SetField(root.Player, "_moveSpeed", root.Player.MoveSpeed + VALUE_CHANGE);

            Assert.AreNotEqual(original, GameDBContentHash.Compute(root));
        }

        [Test]
        public void Compute_NestedAttackShapeChanged_ChangesHash()
        {
            GameDatabaseSO                 root     = CreateRoot();
            string                         original = GameDBContentHash.Compute(root);
            EnemyCatalogSO.AttackShapeSpec shape    = root.Enemies.Enemies[FIRST_INDEX].AttackShape;
            SetField(shape, "_range", shape.Range + VALUE_CHANGE);

            Assert.AreNotEqual(original, GameDBContentHash.Compute(root));
        }

        [Test]
        public void Compute_SameItemsInDifferentCatalogOrder_ChangesHash()
        {
            GameDatabaseSO                 root     = CreateRoot();
            string                         original = GameDBContentHash.Compute(root);
            var                            enemies  = (EnemyCatalogSO.EnemyDefinition[])GetField(root.Enemies, "_enemies");
            EnemyCatalogSO.EnemyDefinition first    = enemies[FIRST_INDEX];
            enemies[FIRST_INDEX]  = enemies[SECOND_INDEX];
            enemies[SECOND_INDEX] = first;

            Assert.AreNotEqual(original, GameDBContentHash.Compute(root));
        }

        [Test]
        public void Compute_AppearanceIndexOrderChanged_ChangesHash()
        {
            GameDatabaseSO                   root     = CreateRoot();
            string                           original = GameDBContentHash.Compute(root);
            var                              sliders  = (AvatarCatalogSO.SliderDefinition[])GetField(root.Avatar, "_faceSliders");
            AvatarCatalogSO.SliderDefinition first    = sliders[FIRST_INDEX];
            sliders[FIRST_INDEX]  = sliders[SECOND_INDEX];
            sliders[SECOND_INDEX] = first;

            Assert.AreNotEqual(original, GameDBContentHash.Compute(root));
        }

        [Test]
        public void Compute_GradientColorAlphaModeAndSpaceChanged_EachChangesHash()
        {
            GameDatabaseSO     root          = CreateRoot();
            Gradient           gradient      = root.Avatar.SkinTones;
            string             original      = GameDBContentHash.Compute(root);
            GradientColorKey[] colors        = gradient.colorKeys;
            Color              originalColor = colors[FIRST_INDEX].color;
            colors[FIRST_INDEX].color = Color.black;
            gradient.colorKeys        = colors;
            Assert.AreNotEqual(original, GameDBContentHash.Compute(root), "color");
            colors[FIRST_INDEX].color = originalColor;
            gradient.colorKeys        = colors;

            GradientAlphaKey[] alpha         = gradient.alphaKeys;
            float              originalAlpha = alpha[FIRST_INDEX].alpha;
            alpha[FIRST_INDEX].alpha -= VALUE_CHANGE;
            gradient.alphaKeys = alpha;
            Assert.AreNotEqual(original, GameDBContentHash.Compute(root), "alpha");
            alpha[FIRST_INDEX].alpha = originalAlpha;
            gradient.alphaKeys       = alpha;

            GradientMode originalMode = gradient.mode;
            gradient.mode = (originalMode == GradientMode.Fixed) ? GradientMode.Blend : GradientMode.Fixed;
            Assert.AreNotEqual(original, GameDBContentHash.Compute(root), "mode");
            gradient.mode = originalMode;

            gradient.colorSpace = (gradient.colorSpace == ColorSpace.Linear) ? ColorSpace.Gamma : ColorSpace.Linear;
            Assert.AreNotEqual(original, GameDBContentHash.Compute(root), "colorSpace");
        }

        [Test]
        public void Compute_LookupCachePopulated_DoesNotChangeHash()
        {
            GameDatabaseSO root     = CreateRoot();
            string         original = GameDBContentHash.Compute(root);
            root.Enemies.TryGetEnemy(root.Enemies.Enemies[FIRST_INDEX].DefinitionId, out _);

            Assert.AreEqual(original, GameDBContentHash.Compute(root));
        }

        [Test]
        public void Compute_DifferentCultures_ReturnsSameHash()
        {
            GameDatabaseSO root     = CreateRoot();
            CultureInfo    original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                string first = GameDBContentHash.Compute(root);
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Assert.AreEqual(first, GameDBContentHash.Compute(root));
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
                Assert.AreEqual(first, GameDBContentHash.Compute(root));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private GameDatabaseSO CreateRoot()
        {
            return GameDBTestFixtures.CreateValidRoot(_created);
        }

        // 검증되지 않는 데이터를 넣을 때 OnValidate 로그를 유발하지 않게 메모리 값만 바꾼다
        private static void SetField(object target, string fieldName, object value)
        {
            FindField(target.GetType(), fieldName).SetValue(target, value);
        }

        private static object GetField(object target, string fieldName)
        {
            return FindField(target.GetType(), fieldName).GetValue(target);
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            Type current = type;
            while (current != null)
            {
                FieldInfo field = current.GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }

                current = current.BaseType;
            }

            throw new MissingFieldException(type.FullName, fieldName);
        }
    }
}
