// System
using System;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    public class MenuGuideRulesTests
    {
        [Test]
        public void Session_Next_MovesThroughStepsAndFinishesAtLast()
        {
            var session = new MenuGuideSession(3);
            Assert.AreEqual(0, session.Index);

            session.Next();
            session.Next();
            Assert.AreEqual(2, session.Index);
            Assert.IsTrue(session.IsLastStep);
            Assert.IsFalse(session.IsFinished);

            session.Next();

            Assert.IsTrue(session.IsFinished);
            Assert.IsFalse(session.WasSkipped);
            Assert.IsTrue(session.CountsAsCompleted);
        }

        [Test]
        public void Session_Skip_FinishesAndCountsAsCompleted()
        {
            var session = new MenuGuideSession(4);
            session.Next();

            session.Skip();

            Assert.IsTrue(session.IsFinished);
            Assert.IsTrue(session.WasSkipped);
            Assert.IsTrue(session.CountsAsCompleted);
        }

        [Test]
        public void Session_Interrupted_DoesNotCountAsCompleted()
        {
            var session = new MenuGuideSession(3);
            session.Next();

            Assert.IsFalse(session.CountsAsCompleted);
        }

        [Test]
        public void Session_AfterFinished_NextAndSkipDoNothing()
        {
            var session = new MenuGuideSession(1);
            session.Next();

            Assert.IsFalse(session.Next());
            session.Skip();

            Assert.IsFalse(session.WasSkipped);
        }

        [Test]
        public void Session_NoSteps_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MenuGuideSession(0));
        }

        [Test]
        public void Record_MarkCompleted_RemembersOnlyThatMenu()
        {
            var record = new MemoryMenuGuideRecord();

            record.MarkCompleted("menu_training");

            Assert.IsTrue(record.IsCompleted("menu_training"));
            Assert.IsFalse(record.IsCompleted("menu_talisman"));
        }

        [Test]
        public void Record_MarkCompletedTwice_StaysCompleted()
        {
            var record = new MemoryMenuGuideRecord();

            record.MarkCompleted("menu_training");
            record.MarkCompleted("menu_training");

            Assert.IsTrue(record.IsCompleted("menu_training"));
        }

        [Test]
        public void Record_EmptyId_IsNeverCompleted()
        {
            var record = new MemoryMenuGuideRecord();

            record.MarkCompleted(string.Empty);

            Assert.IsFalse(record.IsCompleted(string.Empty));
            Assert.IsFalse(record.IsCompleted(null));
        }

        [Test]
        public void Catalog_TryGetSteps_FindsRegisteredMenuOnly()
        {
            var catalog = ScriptableObject.CreateInstance<MenuGuideCatalogSO>();
            catalog.Fill(new[]
            {
                new MenuGuideCatalogSO.MenuEntry("menu_a", new[]
                {
                    new MenuGuideCatalogSO.StepEntry("첫 번째", "target_a"),
                    new MenuGuideCatalogSO.StepEntry("두 번째", string.Empty)
                }),
                new MenuGuideCatalogSO.MenuEntry("menu_empty", new MenuGuideCatalogSO.StepEntry[0])
            });

            Assert.IsTrue(catalog.TryGetSteps("menu_a", out MenuGuideStepData[] steps));
            Assert.AreEqual(2, steps.Length);
            Assert.AreEqual("target_a", steps[0].TargetKey);
            Assert.AreEqual(string.Empty, steps[1].TargetKey);

            Assert.IsFalse(catalog.TryGetSteps("menu_empty", out _));
            Assert.IsFalse(catalog.TryGetSteps("menu_unknown", out _));
            Assert.IsFalse(catalog.TryGetSteps(null, out _));

            UnityEngine.Object.DestroyImmediate(catalog);
        }
    }
}
