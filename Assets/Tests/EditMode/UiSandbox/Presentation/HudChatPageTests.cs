using System.Reflection;
using NUnit.Framework;
using Shinshi.Editor;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    /// <summary>
    /// ChatLog는 전역 정적 상태다. 시작 시 비어 있지 않으면 다른 작업의 줄을 지키려고 모든 테스트를 건너뛰고, 끝나면 시작 때처럼 비워 둔다.
    /// 실제 입력 칸 · Enter · 채널 드롭다운은 시험 배치에서 연결하지 않으므로 여기서 확인하지 않는다 (Play에서 부모가 확인)
    /// </summary>
    public sealed class HudChatPageTests
    {
        private PresentationTestHarness _harness;
        private bool                    _startedEmpty;

        [SetUp]
        public void SetUp()
        {
            _startedEmpty = (ChatLog.Entries.Count == 0);
            Assume.That(_startedEmpty, "ChatLog에 이미 줄이 있어 전역 로그를 보호하려고 건너뜀");

            _harness = new PresentationTestHarness();
        }

        [TearDown]
        public void TearDown()
        {
            if (_harness != null)
            {
                _harness.Dispose();
                _harness = null;
            }

            if (_startedEmpty)
            {
                ChatLog.Clear();
            }
        }

        private HudChatPage AttachPlaying(bool seedSamples)
        {
            _harness.Scenario.SetBool(HudChatPage.KEY_SAMPLES, seedSamples);
            HudChatPage page = _harness.Track(new HudChatPage());
            page.Attach(_harness.Context);
            return page;
        }

        private static object ReadPrivate(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "HudChatView에 필드가 없다: " + fieldName);
            return field.GetValue(target);
        }

        /****************************************
        *            Attach / Play gate
        ****************************************/

        [Test]
        public void Attach_NotPlaying_BuildsNoFixtureAndWritesNoLog()
        {
            _harness.Playing = false;
            HudChatPage page = _harness.Track(new HudChatPage());

            page.Attach(_harness.Context);

            Assert.IsNull(page.ChatView);
            Assert.IsFalse(page.UsesFixture);
            Assert.IsFalse(page.LogWritable);
            Assert.AreEqual(HudChatPage.NOTE_NOT_PLAYING, page.LogNote);
            Assert.AreEqual(0, ChatLog.Entries.Count);
        }

        [Test]
        public void TryMethods_NotPlaying_ReturnFalseAndLeaveLogUntouched()
        {
            _harness.Playing = false;
            HudChatPage page = _harness.Track(new HudChatPage());
            page.Attach(_harness.Context);

            Assert.IsFalse(page.TryAddLine());
            Assert.IsFalse(page.TryAddSystemLine());
            Assert.IsFalse(page.TryClearLog());
            Assert.IsFalse(page.TrySend());

            Assert.AreEqual(0, ChatLog.Entries.Count);
            Assert.IsFalse(_harness.Context.IsBusy);
        }

        [Test]
        public void Attach_Playing_BuildsFixtureWithoutInputPath()
        {
            HudChatPage page = AttachPlaying(true);

            Assert.IsNotNull(page.ChatView);
            Assert.IsTrue(page.UsesFixture);
            Assert.IsNull(ReadPrivate(page.ChatView, "_input"));
            Assert.IsNull(ReadPrivate(page.ChatView, "_router"));
            Assert.IsNull(ReadPrivate(page.ChatView, "_channel"));
        }

        [Test]
        public void Attach_PlayingWithSamples_SeedsThreeLinesAndIsWritable()
        {
            HudChatPage page = AttachPlaying(true);

            Assert.IsTrue(page.LogWritable);
            Assert.IsTrue(page.AddedLines);
            Assert.AreEqual(string.Empty, page.LogNote);
            Assert.AreEqual(3, ChatLog.Entries.Count);
        }

        [Test]
        public void Attach_PlayingWithoutSamples_IsWritableWithEmptyLog()
        {
            HudChatPage page = AttachPlaying(false);

            Assert.IsTrue(page.LogWritable);
            Assert.IsFalse(page.AddedLines);
            Assert.AreEqual(0, ChatLog.Entries.Count);
        }

        [Test]
        public void Attach_ChatLogAlreadyHasLines_RefusesLocalWritesAndKeepsExistingLog()
        {
            ChatLog.AddSystem("기존 줄");
            HudChatPage page = AttachPlaying(true);

            Assert.IsFalse(page.LogWritable);
            Assert.AreEqual(HudChatPage.NOTE_EXISTING, page.LogNote);
            Assert.IsFalse(page.TryAddLine());
            Assert.IsFalse(page.TryAddSystemLine());
            Assert.IsFalse(page.TryClearLog());
            Assert.IsFalse(page.TrySend());
            Assert.AreEqual(1, ChatLog.Entries.Count);

            page.Detach();

            Assert.AreEqual(1, ChatLog.Entries.Count);
        }

        [Test]
        public void Apply_ScenarioTabChanged_SelectsTabOnView()
        {
            HudChatPage page = AttachPlaying(true);
            _harness.Scenario.SetInt(HudChatPage.KEY_TAB, (int)EChatTab.Party);

            page.Apply(_harness.Context);

            Assert.AreEqual(EChatTab.Party, page.ChatView.ActiveTab);
        }

        /****************************************
        *               Log writes
        ****************************************/

        [Test]
        public void TryAddLine_Writable_AppendsScenarioChannelSenderAndText()
        {
            HudChatPage page = AttachPlaying(false);
            _harness.Scenario.SetInt(HudChatPage.KEY_CHANNEL, (int)EChatChannel.All);
            _harness.Scenario.SetString(HudChatPage.KEY_SENDER, "보낸이");
            _harness.Scenario.SetString(HudChatPage.KEY_TEXT, "본문");

            bool added = page.TryAddLine();

            Assert.IsTrue(added);
            Assert.AreEqual(1, ChatLog.Entries.Count);
            Assert.AreEqual(EChatChannel.All, ChatLog.Entries[0].Channel);
            Assert.AreEqual("보낸이", ChatLog.Entries[0].Sender);
            Assert.AreEqual("본문", ChatLog.Entries[0].Text);
        }

        [Test]
        public void TryAddLine_SystemChannel_AddsNothingAndKeepsWritable()
        {
            HudChatPage page = AttachPlaying(false);
            _harness.Scenario.SetInt(HudChatPage.KEY_CHANNEL, (int)EChatChannel.System);

            bool added = page.TryAddLine();

            Assert.IsFalse(added);
            Assert.AreEqual(0, ChatLog.Entries.Count);
            Assert.IsTrue(page.LogWritable);
        }

        [Test]
        public void TryAddSystemLine_Writable_AppendsSystemEntry()
        {
            HudChatPage page = AttachPlaying(false);
            _harness.Scenario.SetString(HudChatPage.KEY_SYSTEM, "시험 안내");

            bool added = page.TryAddSystemLine();

            Assert.IsTrue(added);
            Assert.AreEqual(1, ChatLog.Entries.Count);
            Assert.AreEqual(EChatChannel.System, ChatLog.Entries[0].Channel);
            Assert.AreEqual("시험 안내", ChatLog.Entries[0].Text);
        }

        [Test]
        public void TryClearLog_WithOwnLines_ClearsAndStaysWritable()
        {
            HudChatPage page = AttachPlaying(true);
            Assert.AreEqual(3, ChatLog.Entries.Count);

            bool cleared = page.TryClearLog();

            Assert.IsTrue(cleared);
            Assert.AreEqual(0, ChatLog.Entries.Count);
            Assert.IsTrue(page.LogWritable);
            Assert.IsTrue(page.TryAddLine());
            Assert.AreEqual(1, ChatLog.Entries.Count);
        }

        /****************************************
        *               Detach
        ****************************************/

        [Test]
        public void Detach_OwnLinesOnly_ClearsLog()
        {
            HudChatPage page = AttachPlaying(true);
            page.TryAddLine();
            Assert.AreEqual(4, ChatLog.Entries.Count);

            page.Detach();

            Assert.AreEqual(0, ChatLog.Entries.Count);
            Assert.IsNull(page.ChatView);
            Assert.IsFalse(page.UsesFixture);
        }

        [Test]
        public void TryAddLine_ForeignLineAdded_StopsWritingAndDetachKeepsLog()
        {
            HudChatPage page = AttachPlaying(true);
            ChatLog.AddMessage(EChatChannel.All, "외부", "외부 줄");
            Assert.AreEqual(4, ChatLog.Entries.Count);

            bool added = page.TryAddLine();

            Assert.IsFalse(added);
            Assert.IsFalse(page.LogWritable);
            Assert.AreEqual(HudChatPage.NOTE_FOREIGN, page.LogNote);
            Assert.AreEqual(4, ChatLog.Entries.Count);

            page.Detach();

            Assert.AreEqual(4, ChatLog.Entries.Count);
        }

        [Test]
        public void Detach_ForeignLineAddedWithoutWrite_KeepsLogBecauseCountDiffers()
        {
            HudChatPage page = AttachPlaying(true);
            ChatLog.AddMessage(EChatChannel.All, "외부", "외부 줄");

            page.Detach();

            Assert.AreEqual(4, ChatLog.Entries.Count);
        }

        /****************************************
        *          Play toggle (NeedsReattach)
        ****************************************/

        [Test]
        public void Apply_PlayToggledOff_DropsFixtureAndOwnLines()
        {
            HudChatPage page = AttachPlaying(true);
            Assert.AreEqual(3, ChatLog.Entries.Count);

            _harness.Playing = false;
            page.Apply(_harness.Context);

            Assert.IsNull(page.ChatView);
            Assert.IsFalse(page.UsesFixture);
            Assert.IsFalse(page.LogWritable);
            Assert.AreEqual(0, ChatLog.Entries.Count);
        }

        [Test]
        public void Apply_PlayToggledBackOn_RebuildsFixtureAndSamples()
        {
            HudChatPage page = AttachPlaying(true);
            _harness.Playing = false;
            page.Apply(_harness.Context);

            _harness.Playing = true;
            page.Apply(_harness.Context);

            Assert.IsNotNull(page.ChatView);
            Assert.IsTrue(page.LogWritable);
            Assert.AreEqual(3, ChatLog.Entries.Count);
        }

        /****************************************
        *            TrySend (request)
        ****************************************/

        [Test]
        public void TrySend_Writable_WritesNothingUntilResponseArrives()
        {
            HudChatPage page = AttachPlaying(false);

            bool started = page.TrySend();

            Assert.IsTrue(started);
            Assert.IsTrue(_harness.Context.IsBusy);
            Assert.AreEqual(HudChatPage.OP_SEND, _harness.Context.PendingOperation);
            Assert.AreEqual(0, ChatLog.Entries.Count);
        }

        [Test]
        public void TrySend_ScenarioEditedWhilePending_WritesValuesCapturedAtRequestTime()
        {
            HudChatPage page = AttachPlaying(false);
            _harness.Scenario.SetInt(HudChatPage.KEY_CHANNEL, (int)EChatChannel.Party);
            _harness.Scenario.SetString(HudChatPage.KEY_SENDER, "원래 보낸이");
            _harness.Scenario.SetString(HudChatPage.KEY_TEXT, "원래 내용");
            Assert.IsTrue(page.TrySend());

            _harness.Scenario.SetInt(HudChatPage.KEY_CHANNEL, (int)EChatChannel.All);
            _harness.Scenario.SetString(HudChatPage.KEY_SENDER, "바뀐 보낸이");
            _harness.Scenario.SetString(HudChatPage.KEY_TEXT, "바뀐 내용");
            _harness.Tick();

            Assert.AreEqual(1, ChatLog.Entries.Count);
            Assert.AreEqual(EChatChannel.Party, ChatLog.Entries[0].Channel);
            Assert.AreEqual("원래 보낸이", ChatLog.Entries[0].Sender);
            Assert.AreEqual("원래 내용", ChatLog.Entries[0].Text);
            StringAssert.Contains("성공", page.LastResponse);
        }

        [Test]
        public void TrySend_SecondCallWhilePending_ReturnsFalseAndWritesOneLine()
        {
            HudChatPage page = AttachPlaying(false);
            Assert.IsTrue(page.TrySend());

            bool second = page.TrySend();
            _harness.Tick();

            Assert.IsFalse(second);
            Assert.AreEqual(1, ChatLog.Entries.Count);
        }

        [Test]
        public void TrySend_Rejected_WritesNothing()
        {
            HudChatPage page = AttachPlaying(false);
            _harness.SetMode(EUiSandboxResponseMode.Rejected, "서버 거절 (모의)");
            Assert.IsTrue(page.TrySend());

            _harness.Tick();

            Assert.AreEqual(0, ChatLog.Entries.Count);
            StringAssert.Contains("실패", page.LastResponse);
        }

        [Test]
        public void TrySend_DetachedBeforeResponse_WritesNothing()
        {
            HudChatPage page = AttachPlaying(false);
            Assert.IsTrue(page.TrySend());

            page.Detach();
            _harness.Tick();

            Assert.AreEqual(0, ChatLog.Entries.Count);
            Assert.IsFalse(_harness.Context.IsBusy);
        }
    }
}
