using System.Collections.Generic;
using NUnit.Framework;
using Shinshi.Editor;
using Shinshi.Systems;

namespace Shinshi.Tests
{
    public sealed class PresentationContractCatalogTests
    {
        private PresentationTestHarness _harness;

        [SetUp]
        public void SetUp()
        {
            _harness = new PresentationTestHarness(false);
        }

        [TearDown]
        public void TearDown()
        {
            _harness.Dispose();
        }

        private ContractOnlyPage AttachPage(string id)
        {
            ContractSpec[] specs = PresentationContractCatalog.CreateSpecs();
            for (int i = 0; i < specs.Length; ++i)
            {
                if (specs[i].Id == id)
                {
                    ContractOnlyPage page = _harness.Track(new ContractOnlyPage(specs[i]));
                    page.Attach(_harness.Context);
                    return page;
                }
            }

            Assert.Fail("스펙 없음: " + id);
            return null;
        }

        private static ContractOperation FindOperation(ContractOnlyPage page, string operation)
        {
            for (int i = 0; i < page.Spec.Operations.Length; ++i)
            {
                if (page.Spec.Operations[i].Operation == operation)
                {
                    return page.Spec.Operations[i];
                }
            }

            Assert.Fail("작업 없음: " + operation);
            return null;
        }

        private static int CountOutcome(UiSandboxContext context, EUiSandboxRequestState state)
        {
            int count = 0;
            for (int i = 0; i < context.History.Count; ++i)
            {
                if (context.History[i].State == state)
                {
                    ++count;
                }
            }

            return count;
        }

        /****************************************
        *             Seed / Specs
        ****************************************/

        [Test]
        public void CreateSpecs_Default_HasThirteenUniqueSpecs()
        {
            ContractSpec[] specs = PresentationContractCatalog.CreateSpecs();
            HashSet<string> ids = new HashSet<string>();

            for (int i = 0; i < specs.Length; ++i)
            {
                Assert.IsTrue(ids.Add(specs[i].Id), specs[i].Id);
                Assert.Greater(specs[i].Operations.Length, 0, specs[i].Id);
            }

            Assert.AreEqual(13, specs.Length);
        }

        [Test]
        public void Attach_UserValueExists_DoesNotOverwriteSeed()
        {
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_SETTINGS_HUD, 90);
            _harness.Scenario.SetString(PresentationContractCatalog.KEY_CREATE_NAME, "사용자");

            AttachPage(PresentationContractCatalog.ID_SETTINGS);
            AttachPage(PresentationContractCatalog.ID_CHAR_CREATE);

            Assert.AreEqual(90, _harness.Scenario.GetInt(PresentationContractCatalog.KEY_SETTINGS_HUD));
            Assert.AreEqual("사용자", _harness.Scenario.GetString(PresentationContractCatalog.KEY_CREATE_NAME));
            Assert.IsTrue(_harness.Scenario.Contains(PresentationContractCatalog.KEY_SETTINGS_SHAKE));
        }

        /****************************************
        *            Existing Contracts
        ****************************************/

        [TestCase("가", false)]
        [TestCase("가나", true)]
        [TestCase("가나다라마바사아자차카타", true)]
        [TestCase("가나다라마바사아자차카타파", false)]
        [TestCase("  ", false)]
        [TestCase(null, false)]
        public void ValidateNameLength_Boundaries_AcceptsTwoToTwelve(string name, bool accepted)
        {
            string result = PresentationContractCatalog.ValidateNameLength(name);

            Assert.AreEqual(accepted, result == null);
            if (!accepted)
            {
                StringAssert.Contains("1002", result);
            }
        }

        [Test]
        public void ValidateCharacterCreate_NameTaken_Returns1001()
        {
            _harness.Scenario.SetString(PresentationContractCatalog.KEY_CREATE_NAME, "검사님");
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_CREATE_TAKEN, true);

            StringAssert.Contains("1001", PresentationContractCatalog.ValidateCharacterCreate(_harness.Scenario));
        }

        [Test]
        public void ValidateLogin_InvalidAndLocked_Return1205And1206()
        {
            _harness.Scenario.SetString(PresentationContractCatalog.KEY_LOGIN_ID, "tester");
            _harness.Scenario.SetString(PresentationContractCatalog.KEY_LOGIN_PASSWORD, "sample");
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_LOGIN_VALID, false);
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_LOGIN_LOCKED, false);
            StringAssert.Contains("1205", PresentationContractCatalog.ValidateLogin(_harness.Scenario));

            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_LOGIN_LOCKED, true);
            StringAssert.Contains("1206", PresentationContractCatalog.ValidateLogin(_harness.Scenario));
        }

        [Test]
        public void ValidateRoomJoin_MissingRoomAndWrongPassword_ReturnsNamedErrors()
        {
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_ROOM_EXISTS, false);
            StringAssert.Contains("RoomNotFound", PresentationContractCatalog.ValidateRoomJoin(_harness.Scenario));

            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_ROOM_EXISTS, true);
            _harness.Scenario.SetString(PresentationContractCatalog.KEY_ROOM_PASSWORD, "1234");
            _harness.Scenario.SetString(PresentationContractCatalog.KEY_ROOM_JOIN_PASS, "9999");
            StringAssert.Contains("RoomPasswordMismatch", PresentationContractCatalog.ValidateRoomJoin(_harness.Scenario));

            _harness.Scenario.SetString(PresentationContractCatalog.KEY_ROOM_JOIN_PASS, "1234");
            Assert.IsNull(PresentationContractCatalog.ValidateRoomJoin(_harness.Scenario));
        }

        [Test]
        public void AcceptInvite_AgeBeyondThirty_RejectsAsInviteExpired()
        {
            ContractOnlyPage page = AttachPage(PresentationContractCatalog.ID_PARTY_ROOM);
            ContractOperation accept = FindOperation(page, "partyroom.acceptInvite");

            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_ROOM_INVITE_AGE, PresentationContractCatalog.INVITE_EXPIRE_SECONDS);
            Assert.IsNull(accept.Validate(_harness.Scenario));

            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_ROOM_INVITE_AGE, PresentationContractCatalog.INVITE_EXPIRE_SECONDS + 1);
            StringAssert.Contains("InviteExpired", accept.Validate(_harness.Scenario));
        }

        [Test]
        public void ValidateKeybinding_DuplicateKey_ReportsConflict()
        {
            AttachPage(PresentationContractCatalog.ID_KEYBINDING);
            Assert.IsNull(PresentationContractCatalog.ValidateKeybinding(_harness.Scenario));

            _harness.Scenario.SetString(PresentationContractCatalog.KeybindKey(EHotkeyAction.Chat), "I");

            StringAssert.Contains("키 충돌", PresentationContractCatalog.ValidateKeybinding(_harness.Scenario));
        }

        [Test]
        public void ValidateKeybinding_UnknownKeyName_Rejects()
        {
            AttachPage(PresentationContractCatalog.ID_KEYBINDING);
            _harness.Scenario.SetString(PresentationContractCatalog.KeybindKey(EHotkeyAction.Inventory), "NotAKey");

            StringAssert.Contains("알 수 없는 키", PresentationContractCatalog.ValidateKeybinding(_harness.Scenario));
        }

        [TestCase(79, false)]
        [TestCase(80, true)]
        [TestCase(130, true)]
        [TestCase(131, false)]
        public void ValidateSettings_HudScaleRange_AcceptsEightyToOneThirty(int scale, bool accepted)
        {
            AttachPage(PresentationContractCatalog.ID_SETTINGS);
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_SETTINGS_HUD, scale);

            Assert.AreEqual(accepted, PresentationContractCatalog.ValidateSettings(_harness.Scenario) == null);
        }

        [Test]
        public void ValidateDialogueChoice_NotLastLineOrMissingChoice_Rejects()
        {
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_DIALOGUE_LINES, 3);
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_DIALOGUE_INDEX, 0);
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_DIALOGUE_CHOICES, 2);
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_DIALOGUE_PICK, 0);
            Assert.IsNotNull(PresentationContractCatalog.ValidateDialogueChoice(_harness.Scenario));

            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_DIALOGUE_INDEX, 2);
            Assert.IsNull(PresentationContractCatalog.ValidateDialogueChoice(_harness.Scenario));

            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_DIALOGUE_PICK, 2);
            Assert.AreEqual("없는 선택지", PresentationContractCatalog.ValidateDialogueChoice(_harness.Scenario));
        }

        /****************************************
        *               Party Loot
        ****************************************/

        private void SeedLoot(int eligible, int balance, int highest, int bid, int remain)
        {
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_LOOT_PARTICIPANTS, eligible);
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_LOOT_BALANCE, balance);
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_LOOT_HIGHEST, highest);
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_LOOT_BID, bid);
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_LOOT_REMAIN, remain);
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_LOOT_BLOCKED, false);
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_LOOT_LATE_JOIN, false);
        }

        [Test]
        public void ValidateLootBid_NoBidYet_RequiresMinimumOneHundred()
        {
            SeedLoot(3, 500, 0, 99, 30);
            StringAssert.Contains("100", PresentationContractCatalog.ValidateLootBid(_harness.Scenario));

            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_LOOT_BID, 100);
            Assert.IsNull(PresentationContractCatalog.ValidateLootBid(_harness.Scenario));
        }

        [Test]
        public void ValidateLootBid_ExistingBid_RequiresStepOfTen()
        {
            SeedLoot(3, 500, 200, 209, 30);
            StringAssert.Contains("210", PresentationContractCatalog.ValidateLootBid(_harness.Scenario));

            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_LOOT_BID, 210);
            Assert.IsNull(PresentationContractCatalog.ValidateLootBid(_harness.Scenario));
        }

        [Test]
        public void ValidateLootBid_BlockedLateJoinClosedOrSolo_Rejects()
        {
            SeedLoot(3, 500, 0, 100, 30);
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_LOOT_BLOCKED, true);
            StringAssert.Contains("auction", PresentationContractCatalog.ValidateLootBid(_harness.Scenario));

            SeedLoot(3, 500, 0, 100, 30);
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_LOOT_LATE_JOIN, true);
            StringAssert.Contains("다음 품목", PresentationContractCatalog.ValidateLootBid(_harness.Scenario));

            SeedLoot(3, 500, 0, 100, 0);
            StringAssert.Contains("마감", PresentationContractCatalog.ValidateLootBid(_harness.Scenario));

            SeedLoot(1, 500, 0, 100, 30);
            StringAssert.Contains("단독 구매", PresentationContractCatalog.ValidateLootBid(_harness.Scenario));
        }

        [Test]
        public void ValidateLootBid_BidAboveBalance_Rejects()
        {
            SeedLoot(3, 150, 0, 160, 30);

            StringAssert.Contains("잔액", PresentationContractCatalog.ValidateLootBid(_harness.Scenario));
        }

        [Test]
        public void DescribeLootBid_RemainBelowTen_ExtendsToTen()
        {
            SeedLoot(3, 500, 0, 100, 9);
            StringAssert.Contains("연장", PresentationContractCatalog.DescribeLootBid(_harness.Scenario));

            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_LOOT_REMAIN, 10);
            StringAssert.Contains("연장 없음", PresentationContractCatalog.DescribeLootBid(_harness.Scenario));
        }

        [Test]
        public void ValidateLootBuy_OnlySoloItemWithTimeAndBalance_Accepts()
        {
            SeedLoot(1, 100, 0, 100, 30);
            Assert.IsNull(PresentationContractCatalog.ValidateLootBuy(_harness.Scenario));

            SeedLoot(2, 100, 0, 100, 30);
            Assert.IsNotNull(PresentationContractCatalog.ValidateLootBuy(_harness.Scenario));

            SeedLoot(1, 100, 0, 100, 0);
            StringAssert.Contains("유찰", PresentationContractCatalog.ValidateLootBuy(_harness.Scenario));

            SeedLoot(1, 99, 0, 100, 30);
            StringAssert.Contains("잔액", PresentationContractCatalog.ValidateLootBuy(_harness.Scenario));
        }

        [Test]
        public void LootSettle_ThreeOthers_SplitsFloorAndBurnsRemainder()
        {
            SeedLoot(4, 500, 100, 100, 0);
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_LOOT_OTHERS, 3);

            Assert.IsNull(PresentationContractCatalog.ValidateLootSettle(_harness.Scenario));
            string text = PresentationContractCatalog.DescribeLootSettle(_harness.Scenario);
            StringAssert.Contains("각 33", text);
            StringAssert.Contains("남는 1", text);
        }

        [Test]
        public void ValidateLootSettle_SoloOrNoValidBid_Rejects()
        {
            SeedLoot(1, 500, 200, 100, 0);
            StringAssert.Contains("분배가 없다", PresentationContractCatalog.ValidateLootSettle(_harness.Scenario));

            SeedLoot(3, 500, 99, 100, 0);
            StringAssert.Contains("유찰", PresentationContractCatalog.ValidateLootSettle(_harness.Scenario));
        }

        [Test]
        public void DescribeLootSettle_NoOtherParticipants_BurnsWholePrice()
        {
            SeedLoot(2, 500, 150, 100, 0);
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_LOOT_OTHERS, 0);

            StringAssert.Contains("전부 소모", PresentationContractCatalog.DescribeLootSettle(_harness.Scenario));
        }

        /****************************************
        *              Progression
        ****************************************/

        private void SeedProgress(int profile, bool intro, bool forest, bool ruins, bool easy, bool hard, int traces)
        {
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_PROG_PROFILE, profile);
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_PROG_INTRO_DONE, intro);
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_PROG_FOREST, forest);
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_PROG_RUINS, ruins);
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_PROG_CHEOLGAK_E, easy);
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_PROG_CHEOLGAK_H, hard);
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_PROG_TRACES, traces);
        }

        [Test]
        public void Progression_TrialProfile_ExcludesRuinsCheolgakButAllowsMoonwhite()
        {
            SeedProgress(0, true, true, true, true, true, 3);

            StringAssert.Contains("체험판", PresentationContractCatalog.ValidateRuinsEntry(_harness.Scenario));
            StringAssert.Contains("체험판", PresentationContractCatalog.ValidateCheolgakEasyEntry(_harness.Scenario));
            StringAssert.Contains("체험판", PresentationContractCatalog.ValidateCheolgakHardEntry(_harness.Scenario));
            Assert.IsNull(PresentationContractCatalog.ValidateMoonwhiteEntry(_harness.Scenario));
        }

        [Test]
        public void Progression_FullProfileChain_UnlocksStepByStep()
        {
            SeedProgress(1, false, false, false, false, false, 0);
            Assert.IsNotNull(PresentationContractCatalog.ValidateRuinsEntry(_harness.Scenario));

            SeedProgress(1, true, false, false, false, false, 0);
            Assert.IsNotNull(PresentationContractCatalog.ValidateRuinsEntry(_harness.Scenario));

            SeedProgress(1, true, true, false, false, false, 0);
            Assert.IsNull(PresentationContractCatalog.ValidateRuinsEntry(_harness.Scenario));
            Assert.IsNotNull(PresentationContractCatalog.ValidateCheolgakEasyEntry(_harness.Scenario));

            SeedProgress(1, true, true, true, false, false, 0);
            Assert.IsNull(PresentationContractCatalog.ValidateCheolgakEasyEntry(_harness.Scenario));
            Assert.IsNotNull(PresentationContractCatalog.ValidateCheolgakHardEntry(_harness.Scenario));

            SeedProgress(1, true, true, true, true, false, 0);
            Assert.IsNull(PresentationContractCatalog.ValidateCheolgakHardEntry(_harness.Scenario));
        }

        [Test]
        public void Moonwhite_FullProfile_NeedsThreeTracesAndCheolgakHard()
        {
            SeedProgress(1, true, true, true, true, true, 2);
            StringAssert.Contains("3", PresentationContractCatalog.ValidateMoonwhiteEntry(_harness.Scenario));

            SeedProgress(1, true, true, true, true, false, 3);
            StringAssert.Contains("철각 하드", PresentationContractCatalog.ValidateMoonwhiteEntry(_harness.Scenario));

            SeedProgress(1, true, true, true, true, true, 3);
            Assert.IsNull(PresentationContractCatalog.ValidateMoonwhiteEntry(_harness.Scenario));
        }

        /****************************************
        *             Service Notice
        ****************************************/

        [Test]
        public void ValidateNoticeRefresh_AgeSixtyVersusSixtyOne_RefetchesOnlyAfterSixty()
        {
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_NOTICE_FAILS, false);

            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_NOTICE_AGE, 60);
            Assert.IsNotNull(PresentationContractCatalog.ValidateNoticeRefresh(_harness.Scenario));

            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_NOTICE_AGE, 61);
            Assert.IsNull(PresentationContractCatalog.ValidateNoticeRefresh(_harness.Scenario));
        }

        [Test]
        public void ValidateNoticeRefresh_FetchFails_Rejects()
        {
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_NOTICE_AGE, 300);
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_NOTICE_FAILS, true);

            StringAssert.Contains("받지 못했다", PresentationContractCatalog.ValidateNoticeRefresh(_harness.Scenario));
        }

        [Test]
        public void NoticeOperations_EmptyList_ShowsNoNewsText()
        {
            ContractOnlyPage page = AttachPage(PresentationContractCatalog.ID_NOTICE);
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_NOTICE_COUNT, 0);
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_NOTICE_AGE, 300);

            ContractOperation refresh = FindOperation(page, "notice.refresh");
            Assert.AreEqual("새 소식이 없습니다", refresh.DescribeSuccess(_harness.Scenario));
            Assert.AreEqual("새 소식이 없습니다", FindOperation(page, "notice.open").Validate(_harness.Scenario));
        }

        [Test]
        public void NoticeRefresh_FiveNotices_HomeShowsOnlyThree()
        {
            ContractOnlyPage page = AttachPage(PresentationContractCatalog.ID_NOTICE);
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_NOTICE_COUNT, 5);

            StringAssert.Contains("홈에는 3건", FindOperation(page, "notice.refresh").DescribeSuccess(_harness.Scenario));
        }

        /****************************************
        *               Abandon
        ****************************************/

        [Test]
        public void AbandonSpec_Default_IsRegisteredWithThreeOperations()
        {
            ContractOnlyPage page = AttachPage(PresentationContractCatalog.ID_ABANDON);

            Assert.AreEqual(3, page.Spec.Operations.Length);
            Assert.IsFalse(page.HasView);
            StringAssert.Contains("더미", page.Spec.Requirement);
            StringAssert.Contains("결과 화면 복귀와는 별개", page.Spec.Requirement);
        }

        [Test]
        public void ValidateAbandon_Defaults_Accepts()
        {
            AttachPage(PresentationContractCatalog.ID_ABANDON);

            Assert.IsNull(PresentationContractCatalog.ValidateAbandon(_harness.Scenario));
        }

        [Test]
        public void ValidateAbandon_ErrorStates_ReturnNamedCodes()
        {
            AttachPage(PresentationContractCatalog.ID_ABANDON);

            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_ABANDON_SETTLED, true);
            StringAssert.Contains("AlreadySettled", PresentationContractCatalog.ValidateAbandon(_harness.Scenario));

            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_ABANDON_MISMATCH, true);
            StringAssert.Contains("SessionMismatch", PresentationContractCatalog.ValidateAbandon(_harness.Scenario));

            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_ABANDON_ACTIVE, false);
            StringAssert.Contains("진행 중인 출정이 없다", PresentationContractCatalog.ValidateAbandon(_harness.Scenario));
        }

        [Test]
        public void ValidateAbandon_NotConfirmed_Rejects()
        {
            AttachPage(PresentationContractCatalog.ID_ABANDON);
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_ABANDON_CONFIRMED, false);

            StringAssert.Contains("확인", PresentationContractCatalog.ValidateAbandon(_harness.Scenario));
        }

        [Test]
        public void AbandonPreserved_OddEssence_FloorsHalf()
        {
            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_ABANDON_EXTRA, 1001);

            Assert.AreEqual(500, PresentationContractCatalog.AbandonPreserved(_harness.Scenario));
        }

        [Test]
        public void ValidateReconnect_GraceExpired_RejectsAfterOneTwentySeconds()
        {
            AttachPage(PresentationContractCatalog.ID_ABANDON);
            Assert.IsNull(PresentationContractCatalog.ValidateReconnect(_harness.Scenario));

            _harness.Scenario.SetInt(PresentationContractCatalog.KEY_ABANDON_GRACE, 0);
            StringAssert.Contains(PresentationContractCatalog.ABANDON_GRACE_SECONDS.ToString(), PresentationContractCatalog.ValidateReconnect(_harness.Scenario));
        }

        [Test]
        public void ValidateNewSortieAfterAbandon_SettlementPending_BlocksUntilSettled()
        {
            AttachPage(PresentationContractCatalog.ID_ABANDON);
            StringAssert.Contains("정산", PresentationContractCatalog.ValidateNewSortieAfterAbandon(_harness.Scenario));

            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_ABANDON_PENDING, false);
            Assert.IsNull(PresentationContractCatalog.ValidateNewSortieAfterAbandon(_harness.Scenario));
        }

        /****************************************
        *        Requests / Cancellation
        ****************************************/

        [Test]
        public void Run_ValidationRejects_SendsNoRequest()
        {
            ContractOnlyPage page = AttachPage(PresentationContractCatalog.ID_ABANDON);
            _harness.Scenario.SetBool(PresentationContractCatalog.KEY_ABANDON_SETTLED, true);

            bool sent = page.Run(_harness.Context, FindOperation(page, "abandon.request"));

            Assert.IsFalse(sent);
            Assert.IsFalse(_harness.Context.IsBusy);
            Assert.AreEqual(0, _harness.Context.History.Count);
            StringAssert.Contains("AlreadySettled", page.LocalResult);
        }

        [Test]
        public void Run_ValidInputSuccessMode_ReportsSuccessAfterTick()
        {
            ContractOnlyPage page = AttachPage(PresentationContractCatalog.ID_ABANDON);

            bool sent = page.Run(_harness.Context, FindOperation(page, "abandon.request"));
            Assert.IsTrue(sent);
            Assert.IsTrue(_harness.Context.IsBusy);

            _harness.Tick();

            Assert.IsFalse(_harness.Context.IsBusy);
            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Succeeded));
            StringAssert.Contains("마을 귀환", _harness.Context.Status);
        }

        [Test]
        public void Run_RejectedMode_ShowsFailureWithoutSuccessText()
        {
            ContractOnlyPage page = AttachPage(PresentationContractCatalog.ID_ABANDON);
            _harness.SetMode(EUiSandboxResponseMode.Rejected, "서버 거절");

            page.Run(_harness.Context, FindOperation(page, "abandon.request"));
            _harness.Tick();

            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Failed));
            StringAssert.Contains("실패", page.LastResponse);
            StringAssert.Contains("서버 거절", _harness.Context.Status);
            StringAssert.DoesNotContain("마을 귀환", _harness.Context.Status);
        }

        [Test]
        public void Detach_WhileRequestPending_CancelsAndDropsLateResponse()
        {
            ContractOnlyPage page = AttachPage(PresentationContractCatalog.ID_ABANDON);
            page.Run(_harness.Context, FindOperation(page, "abandon.request"));
            Assert.IsTrue(_harness.Context.IsBusy);

            page.Detach();
            _harness.Tick();

            Assert.IsFalse(_harness.Context.IsBusy);
            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Canceled));
            Assert.AreEqual(0, CountOutcome(_harness.Context, EUiSandboxRequestState.Succeeded));
            StringAssert.DoesNotContain("성공", page.LastResponse);
        }

        [Test]
        public void Run_NotPlaying_IsBlockedAndShowsNotStarted()
        {
            ContractOnlyPage page = AttachPage(PresentationContractCatalog.ID_ABANDON);
            _harness.Playing = false;

            bool sent = page.Run(_harness.Context, FindOperation(page, "abandon.request"));

            Assert.IsFalse(sent);
            Assert.AreEqual(1, CountOutcome(_harness.Context, EUiSandboxRequestState.Blocked));
            StringAssert.Contains("시작되지 않음", page.LastResponse);
        }
    }
}
