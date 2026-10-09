// System
using System;
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Editor;

namespace Shinshi.Tests
{
    /// <summary> 경제 화면 13개의 등록 목록, 원본 씬 경로 실재, 계약 전용 표기를 확인한다 </summary>
    public sealed class EconomyPageRegistrarTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int VIEW_PAGE_COUNT     = 5;
        private const int CONTRACT_PAGE_COUNT = 8;

        private static readonly string[] G_ContractSuffixes =
        {
            "growth", "equipment", "enhancement", "inheritance", "training", "talisman", "crafting", "life"
        };

        /****************************************
        *                Methods
        ****************************************/

        [Test]
        public void CreatePages_Default_ReturnsThirteenUniqueIds()
        {
            List<IUiSandboxPage> pages = EconomyPageRegistrar.CreatePages();

            Assert.AreEqual(VIEW_PAGE_COUNT + CONTRACT_PAGE_COUNT, pages.Count);
            HashSet<string> ids = new HashSet<string>();
            for (int i = 0; i < pages.Count; ++i)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(pages[i].Id));
                Assert.IsTrue(ids.Add(pages[i].Id.ToLowerInvariant()), "id 중복: " + pages[i].Id);
            }
        }

        [Test]
        public void CreatePages_Default_SplitsFiveViewPagesAndEightContractPages()
        {
            List<IUiSandboxPage> pages = EconomyPageRegistrar.CreatePages();

            int viewPages = 0;
            int contractPages = 0;
            for (int i = 0; i < pages.Count; ++i)
            {
                if (pages[i].HasView)
                {
                    ++viewPages;
                }
                else
                {
                    ++contractPages;
                }
            }

            Assert.AreEqual(VIEW_PAGE_COUNT, viewPages);
            Assert.AreEqual(CONTRACT_PAGE_COUNT, contractPages);
        }

        [Test]
        public void CreatePages_ViewPages_SourceScenesExistOnDisk()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            List<IUiSandboxPage> pages = EconomyPageRegistrar.CreatePages();

            for (int i = 0; i < pages.Count; ++i)
            {
                if (!pages[i].HasView)
                {
                    continue;
                }

                Assert.IsFalse(string.IsNullOrEmpty(pages[i].SourceScenePath), pages[i].Id + " 원본 씬 경로가 비었다");
                string full = Path.Combine(projectRoot, pages[i].SourceScenePath);
                Assert.IsTrue(File.Exists(full), pages[i].Id + " 원본 씬이 디스크에 없다: " + pages[i].SourceScenePath);
            }
        }

        [Test]
        public void CreatePages_ContractPages_HaveEmptyScenePathAndNoView()
        {
            List<IUiSandboxPage> pages = EconomyPageRegistrar.CreatePages();

            int checkedCount = 0;
            for (int i = 0; i < pages.Count; ++i)
            {
                if (!pages[i].Id.StartsWith(ContractOnlyDefinitions.ID_PREFIX, StringComparison.Ordinal))
                {
                    continue;
                }

                ++checkedCount;
                Assert.IsFalse(pages[i].HasView, pages[i].Id);
                Assert.IsEmpty(pages[i].SourceScenePath, pages[i].Id);
            }

            Assert.AreEqual(CONTRACT_PAGE_COUNT, checkedCount);
        }

        [Test]
        public void CreatePages_ContractPages_CoverEightFutureScreens()
        {
            List<IUiSandboxPage> pages = EconomyPageRegistrar.CreatePages();
            HashSet<string> ids = new HashSet<string>();
            for (int i = 0; i < pages.Count; ++i)
            {
                ids.Add(pages[i].Id);
            }

            for (int i = 0; i < G_ContractSuffixes.Length; ++i)
            {
                Assert.IsTrue(ids.Contains(ContractOnlyDefinitions.ID_PREFIX + G_ContractSuffixes[i]), G_ContractSuffixes[i]);
            }
        }

        [Test]
        public void CreatePages_ContractPages_TitleAndRequirementMarkContractOnly()
        {
            List<IUiSandboxPage> pages = EconomyPageRegistrar.CreatePages();

            for (int i = 0; i < pages.Count; ++i)
            {
                if (pages[i].HasView)
                {
                    continue;
                }

                StringAssert.Contains("계약 전용", pages[i].Title, pages[i].Id);
                StringAssert.Contains("docs/design/", pages[i].Requirement, pages[i].Id + " 요구 문서 출처가 없다");
            }
        }

        [Test]
        public void CreatePages_CalledTwice_ReturnsSeparateInstances()
        {
            List<IUiSandboxPage> first = EconomyPageRegistrar.CreatePages();
            List<IUiSandboxPage> second = EconomyPageRegistrar.CreatePages();

            for (int i = 0; i < first.Count; ++i)
            {
                Assert.AreNotSame(first[i], second[i], first[i].Id);
            }
        }

        [Test]
        public void RegisterMissing_AfterFirstCall_SecondCallRegistersNothing()
        {
            EconomyPageRegistrar.RegisterMissing();

            int second = EconomyPageRegistrar.RegisterMissing();

            Assert.AreEqual(0, second);
        }

        [Test]
        public void RegisterMissing_Default_AllThirteenIdsFoundInCatalog()
        {
            EconomyPageRegistrar.RegisterMissing();

            List<IUiSandboxPage> pages = EconomyPageRegistrar.CreatePages();
            for (int i = 0; i < pages.Count; ++i)
            {
                Assert.IsNotNull(UiSandboxCatalog.Find(pages[i].Id), pages[i].Id + "이(가) 카탈로그에 없다");
            }
        }
    }
}
