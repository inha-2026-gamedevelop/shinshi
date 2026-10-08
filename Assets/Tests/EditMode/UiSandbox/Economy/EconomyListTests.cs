// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Editor;

namespace Shinshi.Tests
{
    /// <summary> 편집 가능한 목록의 개수 · 번호 키 저장과 상한 경계를 확인한다 </summary>
    public sealed class EconomyListTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int MAX_COUNT = 3;

        private static readonly EconomyListSchema G_Schema = new EconomyListSchema(
            "test.list", "시험 목록", MAX_COUNT,
            new[]
            {
                new EconomyField("name", "이름", EEconomyFieldKind.Text, "항목 {n}"),
                new EconomyField("qty", "수량", EEconomyFieldKind.Int, "1"),
                new EconomyField("price", "값", EEconomyFieldKind.Long, "10"),
                new EconomyField("flag", "표시", EEconomyFieldKind.Bool, "false")
            },
            new[]
            {
                new[] { "약초", "5", "100", "true" },
                new[] { "회복약", "2", "300", "false" }
            });

        private UiSandboxScenarioSO _scenario;

        /****************************************
        *               Unity Event
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _scenario = ScriptableObject.CreateInstance<UiSandboxScenarioSO>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_scenario);
        }

        /****************************************
        *                Methods
        ****************************************/

        [Test]
        public void KeyOf_IndexAndField_ReturnsPrefixIndexField()
        {
            Assert.AreEqual("test.list.4.name", G_Schema.KeyOf(4, "name"));
            Assert.AreEqual("test.list.count", G_Schema.CountKey);
        }

        [Test]
        public void Seed_EmptyScenario_StartsWithSampleRowCount()
        {
            EconomyList.Seed(_scenario, G_Schema);

            Assert.AreEqual(2, EconomyList.Count(_scenario, G_Schema));
            Assert.AreEqual("약초", EconomyList.GetText(_scenario, G_Schema, 0, "name"));
            Assert.AreEqual(5, EconomyList.GetInt(_scenario, G_Schema, 0, "qty"));
            Assert.AreEqual(300L, EconomyList.GetLong(_scenario, G_Schema, 1, "price"));
            Assert.IsTrue(EconomyList.GetBool(_scenario, G_Schema, 0, "flag"));
            Assert.IsFalse(EconomyList.GetBool(_scenario, G_Schema, 1, "flag"));
        }

        [Test]
        public void Seed_ExistingKeys_KeepsEditedValues()
        {
            EconomyList.Seed(_scenario, G_Schema);
            EconomyList.SetText(_scenario, G_Schema, 0, "name", "편집한 이름");
            EconomyList.SetInt(_scenario, G_Schema, 0, "qty", 99);

            EconomyList.Seed(_scenario, G_Schema);

            Assert.AreEqual("편집한 이름", EconomyList.GetText(_scenario, G_Schema, 0, "name"));
            Assert.AreEqual(99, EconomyList.GetInt(_scenario, G_Schema, 0, "qty"));
        }

        [Test]
        public void Seed_ZeroCountAlreadyEdited_DoesNotRestoreSamples()
        {
            EconomyList.Seed(_scenario, G_Schema);
            EconomyList.SetCount(_scenario, G_Schema, 0);

            EconomyList.Seed(_scenario, G_Schema);

            Assert.AreEqual(0, EconomyList.Count(_scenario, G_Schema));
        }

        [Test]
        public void Add_BelowMax_AppendsRowWithDefaultsAndIndexToken()
        {
            EconomyList.Seed(_scenario, G_Schema);

            bool added = EconomyList.Add(_scenario, G_Schema);

            Assert.IsTrue(added);
            Assert.AreEqual(3, EconomyList.Count(_scenario, G_Schema));
            Assert.AreEqual("항목 3", EconomyList.GetText(_scenario, G_Schema, 2, "name"));
            Assert.AreEqual(1, EconomyList.GetInt(_scenario, G_Schema, 2, "qty"));
        }

        [Test]
        public void Add_AtMax_ReturnsFalseAndKeepsCount()
        {
            EconomyList.SetCount(_scenario, G_Schema, MAX_COUNT);

            bool added = EconomyList.Add(_scenario, G_Schema);

            Assert.IsFalse(added);
            Assert.AreEqual(MAX_COUNT, EconomyList.Count(_scenario, G_Schema));
        }

        [Test]
        public void Count_StoredValueAboveMax_IsClampedToMax()
        {
            _scenario.SetInt(G_Schema.CountKey, MAX_COUNT + 40);

            Assert.AreEqual(MAX_COUNT, EconomyList.Count(_scenario, G_Schema));
        }

        [Test]
        public void Count_NegativeStoredValue_IsClampedToZero()
        {
            _scenario.SetInt(G_Schema.CountKey, -5);

            Assert.AreEqual(0, EconomyList.Count(_scenario, G_Schema));
        }

        [Test]
        public void SetCount_AboveMax_ClampsAndFillsDefaults()
        {
            EconomyList.SetCount(_scenario, G_Schema, 50);

            Assert.AreEqual(MAX_COUNT, EconomyList.Count(_scenario, G_Schema));
            Assert.AreEqual("약초", EconomyList.GetText(_scenario, G_Schema, 0, "name"));
            Assert.AreEqual("항목 3", EconomyList.GetText(_scenario, G_Schema, 2, "name"));
        }

        [Test]
        public void SetCount_Shrink_DoesNotRewriteExistingRows()
        {
            EconomyList.Seed(_scenario, G_Schema);
            EconomyList.SetText(_scenario, G_Schema, 0, "name", "유지");

            EconomyList.SetCount(_scenario, G_Schema, 1);

            Assert.AreEqual(1, EconomyList.Count(_scenario, G_Schema));
            Assert.AreEqual("유지", EconomyList.GetText(_scenario, G_Schema, 0, "name"));
        }

        [Test]
        public void RemoveAt_MiddleRow_ShiftsLaterRowsUp()
        {
            EconomyList.SetCount(_scenario, G_Schema, MAX_COUNT);
            EconomyList.SetText(_scenario, G_Schema, 0, "name", "A");
            EconomyList.SetText(_scenario, G_Schema, 1, "name", "B");
            EconomyList.SetText(_scenario, G_Schema, 2, "name", "C");
            EconomyList.SetInt(_scenario, G_Schema, 2, "qty", 77);

            bool removed = EconomyList.RemoveAt(_scenario, G_Schema, 1);

            Assert.IsTrue(removed);
            Assert.AreEqual(2, EconomyList.Count(_scenario, G_Schema));
            Assert.AreEqual("A", EconomyList.GetText(_scenario, G_Schema, 0, "name"));
            Assert.AreEqual("C", EconomyList.GetText(_scenario, G_Schema, 1, "name"));
            Assert.AreEqual(77, EconomyList.GetInt(_scenario, G_Schema, 1, "qty"));
        }

        [Test]
        public void RemoveAt_OutOfRange_ReturnsFalseAndKeepsCount()
        {
            EconomyList.Seed(_scenario, G_Schema);

            Assert.IsFalse(EconomyList.RemoveAt(_scenario, G_Schema, -1));
            Assert.IsFalse(EconomyList.RemoveAt(_scenario, G_Schema, 2));
            Assert.AreEqual(2, EconomyList.Count(_scenario, G_Schema));
        }

        [Test]
        public void RemoveAt_LastRow_LeavesEmptyList()
        {
            EconomyList.SetCount(_scenario, G_Schema, 1);

            Assert.IsTrue(EconomyList.RemoveAt(_scenario, G_Schema, 0));
            Assert.AreEqual(0, EconomyList.Count(_scenario, G_Schema));
            Assert.IsFalse(EconomyList.RemoveAt(_scenario, G_Schema, 0));
        }

        [Test]
        public void ResetRow_EditedRow_RestoresSampleValues()
        {
            EconomyList.Seed(_scenario, G_Schema);
            EconomyList.SetText(_scenario, G_Schema, 1, "name", "바뀜");
            EconomyList.SetLong(_scenario, G_Schema, 1, "price", 1L);
            EconomyList.SetBool(_scenario, G_Schema, 1, "flag", true);

            EconomyList.ResetRow(_scenario, G_Schema, 1);

            Assert.AreEqual("회복약", EconomyList.GetText(_scenario, G_Schema, 1, "name"));
            Assert.AreEqual(300L, EconomyList.GetLong(_scenario, G_Schema, 1, "price"));
            Assert.IsFalse(EconomyList.GetBool(_scenario, G_Schema, 1, "flag"));
        }
    }
}
