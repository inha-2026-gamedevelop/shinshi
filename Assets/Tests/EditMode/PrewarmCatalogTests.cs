// System
using System.Collections.Generic;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Art;

namespace Shinshi.Tests
{
    public sealed class PrewarmCatalogTests
    {
        private sealed class StubTarget : MonoBehaviour, IPrewarmTarget
        {
            public void BeginPrewarm() { }
            public void TickPrewarm(float elapsedSeconds) { }
            public void EndPrewarm() { }
        }

        private const int   VISIBLE_FRAMES = 3;
        private const float BUDGET_MS      = 8f;
        private const float TIMEOUT        = 10f;
        private const int   CONCURRENT     = 8;

        private const string CATALOG_ASSET_PATH = "Assets/09.Data/Prewarm/PrewarmCatalog.asset";

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; ++i)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        private GameObject MakePrefab(string objectName, bool withTarget)
        {
            var go = new GameObject(objectName);
            if (withTarget)
            {
                go.AddComponent<StubTarget>();
            }

            _created.Add(go);
            return go;
        }

        private PrewarmCatalogSO MakeCatalog(params PrewarmCatalogEntry[] entries)
        {
            PrewarmCatalogSO catalog = ScriptableObject.CreateInstance<PrewarmCatalogSO>();
            catalog.Configure(entries, BUDGET_MS, TIMEOUT, CONCURRENT);
            _created.Add(catalog);
            return catalog;
        }

        private static PrewarmCatalogEntry Entry(GameObject prefab, EPrewarmKind kind, int visibleFrames = VISIBLE_FRAMES)
        {
            return new PrewarmCatalogEntry(prefab, kind, visibleFrames, null, false, false);
        }

        [Test]
        public void Validate_ShippedCatalogAsset_HasNoErrors()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PrewarmCatalogSO>(CATALOG_ASSET_PATH);
            Assert.IsNotNull(catalog, CATALOG_ASSET_PATH);

            bool isValid = catalog.Validate(out List<string> errors);

            Assert.IsTrue(isValid, string.Join(", ", errors));
            Assert.AreEqual(0, errors.Count);
        }

        [Test]
        public void Validate_ValidEntries_ReturnsTrue()
        {
            PrewarmCatalogSO catalog = MakeCatalog(
                Entry(MakePrefab("A", false), EPrewarmKind.Particle),
                Entry(MakePrefab("B", true), EPrewarmKind.Scripted));

            bool isValid = catalog.Validate(out List<string> errors);

            Assert.IsTrue(isValid);
            Assert.AreEqual(0, errors.Count);
        }

        [Test]
        public void Validate_NullPrefab_ReportsError()
        {
            PrewarmCatalogSO catalog = MakeCatalog(Entry(null, EPrewarmKind.Particle));

            bool isValid = catalog.Validate(out List<string> errors);

            Assert.IsFalse(isValid);
            Assert.AreEqual(1, errors.Count);
        }

        [Test]
        public void Validate_DuplicatePrefab_ReportsError()
        {
            GameObject prefab = MakePrefab("A", false);
            PrewarmCatalogSO catalog = MakeCatalog(
                Entry(prefab, EPrewarmKind.Particle),
                Entry(prefab, EPrewarmKind.Model));

            bool isValid = catalog.Validate(out List<string> errors);

            Assert.IsFalse(isValid);
            Assert.AreEqual(1, errors.Count);
        }

        [Test]
        public void Validate_ScriptedWithoutTarget_ReportsError()
        {
            PrewarmCatalogSO catalog = MakeCatalog(Entry(MakePrefab("A", false), EPrewarmKind.Scripted));

            bool isValid = catalog.Validate(out List<string> errors);

            Assert.IsFalse(isValid);
            Assert.AreEqual(1, errors.Count);
        }

        [Test]
        public void Validate_VisibleFramesBelowOne_ReportsError()
        {
            PrewarmCatalogSO catalog = MakeCatalog(Entry(MakePrefab("A", false), EPrewarmKind.Model, 0));

            bool isValid = catalog.Validate(out List<string> errors);

            Assert.IsFalse(isValid);
            Assert.AreEqual(1, errors.Count);
        }
    }
}
