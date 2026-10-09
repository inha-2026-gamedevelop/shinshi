using System;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using Shinshi.Editor;
using UnityEngine;

namespace Shinshi.Tests
{
    public sealed class UiSandboxScenarioSOTests
    {
        private UiSandboxScenarioSO _scenario = null;

        [SetUp]
        public void SetUp()
        {
            _scenario = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_scenario);
            _scenario = null;
        }

        [Test]
        public void ToJson_ThenTryLoadJson_RoundTripsAllValues()
        {
            _scenario.SetInt("gold", 77);
            _scenario.SetString("name", "한글 \"따옴표\" 값");
            _scenario.SetFloat("rate", 0.125f);
            _scenario.SetBool("flag", true);

            UiSandboxScenarioSO other = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
            try
            {
                other.SetString("stale", "지워져야 한다");

                Assert.IsTrue(other.TryLoadJson(_scenario.ToJson()));

                Assert.IsTrue(other.ContentEquals(_scenario));
                Assert.IsFalse(other.Contains("stale"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(other);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("{ 이건 JSON이 아니다")]
        public void TryLoadJson_EmptyOrBrokenJson_KeepsCurrentValues(string json)
        {
            _scenario.SetInt("keep", 5);
            int versionBefore = _scenario.Version;

            Assert.IsFalse(_scenario.TryLoadJson(json));

            Assert.AreEqual(5, _scenario.GetInt("keep"));
            Assert.AreEqual(versionBefore, _scenario.Version);
        }

        [Test]
        public void TryLoadJson_DuplicateOrEmptyKeys_KeepsFirstAndSkipsEmpty()
        {
            const string json = "{\"Entries\":[{\"Key\":\"a\",\"Value\":\"1\"},{\"Key\":\"a\",\"Value\":\"2\"},{\"Key\":\"\",\"Value\":\"x\"}]}";

            Assert.IsTrue(_scenario.TryLoadJson(json));

            Assert.AreEqual(1, _scenario.Count);
            Assert.AreEqual(1, _scenario.GetInt("a"));
        }

        [Test]
        public void TryLoadJson_EmptySnapshot_ClearsValuesAndBumpsVersion()
        {
            _scenario.SetInt("gone", 1);
            int versionBefore = _scenario.Version;

            Assert.IsTrue(_scenario.TryLoadJson("{\"Entries\":[]}"));

            Assert.AreEqual(0, _scenario.Count);
            Assert.Greater(_scenario.Version, versionBefore);
        }

        [Test]
        public void SetInt_ThenGetInt_RoundTripsValue()
        {
            _scenario.SetInt("gold", -1234);

            Assert.AreEqual(-1234, _scenario.GetInt("gold"));
            Assert.IsTrue(_scenario.Contains("gold"));
        }

        [Test]
        public void SetLong_LargeValue_RoundTripsWithoutLoss()
        {
            _scenario.SetLong("big", long.MaxValue);

            Assert.AreEqual(long.MaxValue, _scenario.GetLong("big"));
        }

        [Test]
        public void SetFloat_ThenGetFloat_RoundTripsValue()
        {
            _scenario.SetFloat("rate", 0.1f);

            Assert.AreEqual(0.1f, _scenario.GetFloat("rate"));
        }

        [Test]
        public void SetBool_ThenGetBool_RoundTripsBothValues()
        {
            _scenario.SetBool("on", true);
            _scenario.SetBool("off", false);

            Assert.IsTrue(_scenario.GetBool("on", false));
            Assert.IsFalse(_scenario.GetBool("off", true));
        }

        [Test]
        public void SetString_NullValue_StoresEmptyString()
        {
            _scenario.SetString("name", null);

            Assert.IsTrue(_scenario.Contains("name"));
            Assert.AreEqual(string.Empty, _scenario.GetString("name", "fallback"));
        }

        [Test]
        public void SetFloat_CommaDecimalCultureActive_StoresInvariantText()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                _scenario.SetFloat("rate", 1.5f);

                Assert.AreEqual("1.5", _scenario.GetString("rate"));
                Assert.AreEqual(1.5f, _scenario.GetFloat("rate"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Test]
        public void GetInt_MissingKey_ReturnsFallback()
        {
            Assert.AreEqual(7, _scenario.GetInt("missing", 7));
            Assert.IsFalse(_scenario.Contains("missing"));
        }

        [Test]
        public void GetInt_NonNumericValue_ReturnsFallback()
        {
            _scenario.SetString("gold", "abc");

            Assert.AreEqual(9, _scenario.GetInt("gold", 9));
        }

        [Test]
        public void GetBool_OneAndZeroText_AreParsed()
        {
            _scenario.SetString("a", "1");
            _scenario.SetString("b", "0");

            Assert.IsTrue(_scenario.GetBool("a", false));
            Assert.IsFalse(_scenario.GetBool("b", true));
        }

        [Test]
        public void GetInt_EmptyKey_ReturnsFallbackWithoutThrowing()
        {
            Assert.AreEqual(3, _scenario.GetInt(string.Empty, 3));
            Assert.AreEqual(4, _scenario.GetInt(null, 4));
            Assert.IsFalse(_scenario.Contains(null));
        }

        [Test]
        public void SetInt_EmptyKey_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => _scenario.SetInt(string.Empty, 1));
            Assert.Throws<ArgumentException>(() => _scenario.SetString(null, "x"));
        }

        [Test]
        public void SeedInt_ExistingKey_KeepsUserValue()
        {
            _scenario.SetInt("gold", 500);

            _scenario.SeedInt("gold", 10);

            Assert.AreEqual(500, _scenario.GetInt("gold"));
        }

        [Test]
        public void SeedInt_MissingKey_SetsDefault()
        {
            _scenario.SeedInt("gold", 10);
            _scenario.SeedString("title", "기본");
            _scenario.SeedBool("flag", true);
            _scenario.SeedFloat("rate", 2.5f);
            _scenario.SeedLong("big", 99L);

            Assert.AreEqual(10, _scenario.GetInt("gold"));
            Assert.AreEqual("기본", _scenario.GetString("title"));
            Assert.IsTrue(_scenario.GetBool("flag"));
            Assert.AreEqual(2.5f, _scenario.GetFloat("rate"));
            Assert.AreEqual(99L, _scenario.GetLong("big"));
        }

        [Test]
        public void Remove_ExistingKey_RemovesAndReportsTrue()
        {
            _scenario.SetInt("gold", 1);

            Assert.IsTrue(_scenario.Remove("gold"));
            Assert.IsFalse(_scenario.Remove("gold"));
            Assert.IsFalse(_scenario.Contains("gold"));
        }

        [Test]
        public void Clear_WithEntries_EmptiesScenario()
        {
            _scenario.SetInt("a", 1);
            _scenario.SetInt("b", 2);

            _scenario.Clear();

            Assert.AreEqual(0, _scenario.Count);
        }

        [Test]
        public void Keys_AfterSets_ListsAllKeysOnce()
        {
            _scenario.SetInt("a", 1);
            _scenario.SetInt("a", 2);
            _scenario.SetInt("b", 3);

            CollectionAssert.AreEquivalent(new[] { "a", "b" }, _scenario.Keys);
        }

        [Test]
        public void Version_ValueChanges_IncrementsOnlyWhenValueDiffers()
        {
            _scenario.SetInt("a", 1);
            int afterFirst = _scenario.Version;

            _scenario.SetInt("a", 1);
            Assert.AreEqual(afterFirst, _scenario.Version);

            _scenario.SetInt("a", 2);
            Assert.Greater(_scenario.Version, afterFirst);
        }

        [Test]
        public void Clone_ModifyingClone_LeavesOriginalUnchanged()
        {
            _scenario.SetInt("gold", 100);
            UiSandboxScenarioSO clone = _scenario.Clone();
            try
            {
                clone.SetInt("gold", 999);
                clone.SetInt("extra", 1);

                Assert.AreEqual(100, _scenario.GetInt("gold"));
                Assert.IsFalse(_scenario.Contains("extra"));
                Assert.AreNotSame(_scenario, clone);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(clone);
            }
        }

        [Test]
        public void CopyFrom_OtherScenario_ReplacesAllEntries()
        {
            UiSandboxScenarioSO other = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
            try
            {
                _scenario.SetInt("old", 1);
                other.SetInt("new", 2);

                _scenario.CopyFrom(other);

                Assert.IsFalse(_scenario.Contains("old"));
                Assert.AreEqual(2, _scenario.GetInt("new"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void CopyFrom_NullOrSelf_DoesNothing()
        {
            _scenario.SetInt("a", 1);

            _scenario.CopyFrom(null);
            _scenario.CopyFrom(_scenario);

            Assert.AreEqual(1, _scenario.GetInt("a"));
        }

        [Test]
        public void ContentEquals_SameValuesDifferentOrder_ReturnsTrue()
        {
            UiSandboxScenarioSO other = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
            try
            {
                _scenario.SetInt("a", 1);
                _scenario.SetString("b", "x");
                other.SetString("b", "x");
                other.SetInt("a", 1);

                Assert.IsTrue(_scenario.ContentEquals(other));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void ContentEquals_DifferentValueOrNull_ReturnsFalse()
        {
            UiSandboxScenarioSO other = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
            try
            {
                _scenario.SetInt("a", 1);
                other.SetInt("a", 2);

                Assert.IsFalse(_scenario.ContentEquals(other));
                Assert.IsFalse(_scenario.ContentEquals(null));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(other);
            }
        }
    }
}
