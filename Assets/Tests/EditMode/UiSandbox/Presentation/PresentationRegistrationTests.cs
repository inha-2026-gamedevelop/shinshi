using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Shinshi.Editor;

namespace Shinshi.Tests
{
    public sealed class PresentationRegistrationTests
    {
        private static readonly string[] EXPECTED_VIEW_IDS =
        {
            "hud.status", "hud.combat", "hud.resonance.legacy", "hud.party", "hud.chat", "hud.menu",
            "hud.combatmeter", "hud.popup", "sortie.prep", "sortie.result", "lobby.charselect",
            "common.ui"
        };

        private static readonly string[] EXPECTED_CONTRACT_IDS =
        {
            "contract.settings", "contract.keybinding", "contract.npc", "contract.dialogue",
            "contract.partyroom", "contract.charcreate", "contract.login", "contract.networkoverlay", "contract.interaction",
            "contract.partyloot", "contract.progression", "contract.servicenotice", "contract.abandon"
        };

        /****************************************
        *              CreatePages
        ****************************************/

        [Test]
        public void CreatePages_Default_HasUniqueCaseInsensitiveIds()
        {
            IList<IUiSandboxPage> pages = PresentationRegistration.CreatePages();
            HashSet<string> ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < pages.Count; ++i)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(pages[i].Id));
                Assert.IsTrue(ids.Add(pages[i].Id), "중복 Id: " + pages[i].Id);
            }
        }

        [Test]
        public void CreatePages_Default_ContainsEveryExpectedId()
        {
            IList<IUiSandboxPage> pages = PresentationRegistration.CreatePages();
            HashSet<string> ids = new HashSet<string>();
            for (int i = 0; i < pages.Count; ++i)
            {
                ids.Add(pages[i].Id);
            }

            for (int i = 0; i < EXPECTED_VIEW_IDS.Length; ++i)
            {
                Assert.IsTrue(ids.Contains(EXPECTED_VIEW_IDS[i]), EXPECTED_VIEW_IDS[i]);
            }

            for (int i = 0; i < EXPECTED_CONTRACT_IDS.Length; ++i)
            {
                Assert.IsTrue(ids.Contains(EXPECTED_CONTRACT_IDS[i]), EXPECTED_CONTRACT_IDS[i]);
            }

            Assert.AreEqual(EXPECTED_VIEW_IDS.Length + EXPECTED_CONTRACT_IDS.Length, pages.Count);
        }

        [Test]
        public void CreatePages_ViewPages_PointAtExistingDevScenes()
        {
            IList<IUiSandboxPage> pages = PresentationRegistration.CreatePages();

            for (int i = 0; i < pages.Count; ++i)
            {
                if (!pages[i].HasView)
                {
                    continue;
                }

                StringAssert.StartsWith("Assets/00.Scenes/Dev_HUD/", pages[i].SourceScenePath, pages[i].Id);
                Assert.IsTrue(File.Exists(pages[i].SourceScenePath), "씬 파일 없음: " + pages[i].SourceScenePath);
            }
        }

        [Test]
        public void CreatePages_ContractPages_AreHonestlyViewless()
        {
            IList<IUiSandboxPage> pages = PresentationRegistration.CreatePages();
            int contractCount = 0;

            for (int i = 0; i < pages.Count; ++i)
            {
                if (!(pages[i] is ContractOnlyPage))
                {
                    continue;
                }

                ++contractCount;
                Assert.IsFalse(pages[i].HasView, pages[i].Id);
                Assert.IsEmpty(pages[i].SourceScenePath, pages[i].Id);
                StringAssert.Contains("계약 전용", pages[i].Title);
            }

            Assert.AreEqual(EXPECTED_CONTRACT_IDS.Length, contractCount);
        }

        [Test]
        public void CreatePages_Default_EveryRequirementIsFilledIn()
        {
            IList<IUiSandboxPage> pages = PresentationRegistration.CreatePages();

            for (int i = 0; i < pages.Count; ++i)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(pages[i].Title), pages[i].Id);
                Assert.IsFalse(string.IsNullOrWhiteSpace(pages[i].Requirement), pages[i].Id);
            }
        }

        [Test]
        public void CreatePages_WaveAndLevelPages_DocumentLatestContract()
        {
            HudCombatPage combat = new HudCombatPage();
            HudStatusPage status = new HudStatusPage();
            SortieResultPage result = new SortieResultPage();
            SortiePrepPage prep = new SortiePrepPage();

            StringAssert.Contains(PresentationNotes.WAVE_NOTE, combat.Requirement);
            StringAssert.Contains(PresentationNotes.WAVE_NOTE, result.Requirement);
            StringAssert.Contains(PresentationNotes.LEVEL_NOTE, status.Requirement);
            StringAssert.Contains(PresentationNotes.LEVEL_NOTE, prep.Requirement);
        }

        [Test]
        public void CreatePages_ResonancePage_IsSeparatedAsLegacy()
        {
            HudResonanceLegacyPage page = new HudResonanceLegacyPage();
            HudStatusPage status = new HudStatusPage();

            StringAssert.Contains("legacy", page.Id);
            StringAssert.Contains(PresentationNotes.CLASS_NOTE, page.Requirement);
            StringAssert.DoesNotContain("resonance", status.Id);
        }

        [Test]
        public void CreatePages_Default_HasNoFalseMenuContract()
        {
            IList<IUiSandboxPage> pages = PresentationRegistration.CreatePages();
            IUiSandboxPage menu = null;
            for (int i = 0; i < pages.Count; ++i)
            {
                Assert.AreNotEqual("contract.menu", pages[i].Id);
                if (pages[i].Id == HudMenuPage.PAGE_ID)
                {
                    menu = pages[i];
                }
            }

            Assert.IsNotNull(menu);
            Assert.IsTrue(menu.HasView);
            Assert.IsInstanceOf<HudMenuPage>(menu);
        }

        [Test]
        public void ClassNote_Default_LabelsLegacyAndCurrentClassIds()
        {
            StringAssert.Contains("검사(3)", PresentationNotes.CLASS_NOTE);
            StringAssert.Contains("마검사(4)", PresentationNotes.CLASS_NOTE);
            StringAssert.Contains("궁수(5)", PresentationNotes.CLASS_NOTE);
            StringAssert.Contains("무사(0)", PresentationNotes.CLASS_NOTE);
            StringAssert.Contains("도술사(1)", PresentationNotes.CLASS_NOTE);
            StringAssert.Contains("신령사(2)", PresentationNotes.CLASS_NOTE);
        }

        [Test]
        public void BossNote_Default_CallsPhaseTwoPendingDesign()
        {
            StringAssert.Contains("월백 2페이즈", PresentationNotes.BOSS_NOTE);
            StringAssert.Contains("미정", PresentationNotes.BOSS_NOTE);
            StringAssert.Contains("구현된 전투가 아니며", PresentationNotes.BOSS_NOTE);
        }

        [Test]
        public void PopupPage_Requirement_MapsCommonUiCoverage()
        {
            PopupPage page = new PopupPage();

            StringAssert.Contains(PresentationNotes.COMMON_UI_NOTE, page.Requirement);
            StringAssert.Contains("TooltipView", PresentationNotes.COMMON_UI_NOTE);
            StringAssert.Contains("SaveStatusView", PresentationNotes.COMMON_UI_NOTE);
            StringAssert.Contains("검증했다고 말하지 않는다", PresentationNotes.COMMON_UI_NOTE);
        }

        /****************************************
        *               RegisterAll
        ****************************************/

        [Test]
        public void RegisterAll_AfterUnregister_RegistersAllAndSkipsDuplicatesNext()
        {
            IList<IUiSandboxPage> pages = PresentationRegistration.CreatePages();
            for (int i = 0; i < pages.Count; ++i)
            {
                UiSandboxCatalog.Unregister(pages[i].Id);
            }

            int first = PresentationRegistration.RegisterAll();
            int second = PresentationRegistration.RegisterAll();

            Assert.AreEqual(pages.Count, first);
            Assert.AreEqual(0, second);
            for (int i = 0; i < pages.Count; ++i)
            {
                Assert.IsNotNull(UiSandboxCatalog.Find(pages[i].Id), pages[i].Id);
            }
        }
    }
}
